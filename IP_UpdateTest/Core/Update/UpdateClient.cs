using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace IP_UpdateTest.Core.Update
{
    /// <summary>
    /// 下载进度
    /// </summary>
    public struct DownloadProgress
    {
        public DownloadProgress(long received, long total, double bytesPerSecond)
        {
            Received = received;
            Total = total;
            BytesPerSecond = bytesPerSecond;
        }

        public long Received { get; private set; }
        public long Total { get; private set; }
        public double BytesPerSecond { get; private set; }

        public double Fraction
        {
            get { return Total > 0 ? Math.Min(1, (double)Received / Total) : 0; }
        }
    }

    /// <summary>
    /// 更新过程中的错误，Message 是给用户看的中文说明
    /// </summary>
    public sealed class UpdateException : Exception
    {
        public UpdateException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// 从 GitHub Releases 查询最新版本，下载安装包到 %LOCALAPPDATA%\IPTool\updates\&lt;版本&gt;，
    /// 用 SHA256SUMS.txt 校验后解压，并确认解压出的程序就是这个版本
    /// </summary>
    public sealed class UpdateClient
    {
        public const string Repository = "kekemao00/ip-tool";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";
        private const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const string ExecutableName = "IPTool.exe";

        /// <summary>
        /// 下载中这么久没收到数据就算失败
        /// </summary>
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

        private static readonly Lazy<HttpClient> Http = new Lazy<HttpClient>(() =>
        {
            // 使用系统代理设置（与浏览器一致），需要身份验证的公司代理用当前 Windows 账户登录
            IWebProxy proxy = WebRequest.GetSystemWebProxy();
            proxy.Credentials = CredentialCache.DefaultCredentials;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                Proxy = proxy,
                UseProxy = true
            };
            var client = new HttpClient(handler)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("IPTool/" + Versions.Display(Versions.Running).TrimStart('v'));
            return client;
        });

        public static string UpdatesRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPTool", "updates"); }
        }

        /// <summary>
        /// 查询最新的正式版本
        /// </summary>
        public async Task<ReleaseInfo> GetLatestAsync(CancellationToken cancel)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    using (HttpResponseMessage response = await Http.Value.SendAsync(request, timeout.Token).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound) throw new UpdateException("还没有发布过正式版本");
                        if (response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired) throw new UpdateException("代理服务器要求身份验证，请检查代理设置");
                        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                            throw new UpdateException("GitHub 访问次数已达上限，请稍后再试");
                        if (!response.IsSuccessStatusCode) throw new UpdateException("GitHub 返回 " + (int)response.StatusCode);
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        ReleaseInfo release;
                        try
                        {
                            release = ReleaseInfo.FromJson(json);
                        }
                        catch (System.Runtime.Serialization.SerializationException ex)
                        {
                            throw new UpdateException("无法识别 GitHub 返回的版本信息", ex);
                        }
                        if (release == null) throw new UpdateException("最新版本没有可下载的安装包");
                        return release;
                    }
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    throw new UpdateException("连接 GitHub 超时，请检查网络或代理");
                }
                catch (HttpRequestException ex)
                {
                    throw new UpdateException("无法连接 GitHub，请检查网络或代理", ex);
                }
            }
        }

        /// <summary>
        /// 已下载并校验过的版本直接返回解压目录，否则返回 null
        /// </summary>
        public static string FindStaged(ReleaseInfo release)
        {
            string dir = StagingDirectory(release);
            string files = Path.Combine(dir, "files");
            string marker = Path.Combine(dir, "verified");
            try
            {
                if (File.Exists(marker) && File.Exists(Path.Combine(files, ExecutableName)) && MatchesVersion(Path.Combine(files, ExecutableName), release.Version))
                    return files;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is BadImageFormatException)
            {
            }
            return null;
        }

        private static string StagingDirectory(ReleaseInfo release)
        {
            return Path.Combine(UpdatesRoot, release.DisplayVersion);
        }

        /// <summary>
        /// 下载、校验并解压；返回解压出的文件所在目录
        /// </summary>
        public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<DownloadProgress> progress, Action verifying, CancellationToken cancel)
        {
            if (string.IsNullOrEmpty(release.ChecksumUrl))
                throw new UpdateException("这个版本没有校验文件，为安全起见请到发布页手动下载");

            string dir = StagingDirectory(release);
            string package = Path.Combine(dir, Path.GetFileName(release.PackageName));
            string files = Path.Combine(dir, "files");
            try
            {
                Directory.CreateDirectory(dir);
                File.Delete(Path.Combine(dir, "verified"));

                string sums = await GetStringAsync(release.ChecksumUrl, cancel).ConfigureAwait(false);
                string expected = Checksums.Find(sums, release.PackageName);
                if (expected == null) throw new UpdateException("校验文件中没有这个安装包，请到发布页手动下载");

                await DownloadFileAsync(release.PackageUrl, package, release.PackageSize, progress, cancel).ConfigureAwait(false);

                if (verifying != null) verifying();
                string actual = await Task.Run(() => HashFile(package), cancel).ConfigureAwait(false);
                if (actual != expected)
                {
                    TryDelete(package);
                    throw new UpdateException("安装包校验失败（SHA-256 不一致），已删除，请重试");
                }

                await Task.Run(() => Extract(package, files), cancel).ConfigureAwait(false);
                string exe = Path.Combine(files, ExecutableName);
                if (!File.Exists(exe)) throw new UpdateException("安装包中没有 " + ExecutableName);
                if (!MatchesVersion(exe, release.Version)) throw new UpdateException("安装包中的程序版本与发布的版本不符");

                File.WriteAllText(Path.Combine(dir, "verified"), actual);
                TryDelete(package);
                return files;
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                throw new UpdateException("下载超时，请检查网络或代理后重试");
            }
            catch (HttpRequestException ex)
            {
                throw new UpdateException("下载失败，请检查网络或代理后重试", ex);
            }
            catch (InvalidDataException ex)
            {
                throw new UpdateException("安装包已损坏，请重试", ex);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new UpdateException("下载或保存安装包失败：" + ex.Message, ex);
            }
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken cancel)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(StallTimeout);
                using (HttpResponseMessage response = await Http.Value.GetAsync(url, timeout.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new UpdateException("下载校验文件失败（" + (int)response.StatusCode + "）");
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task DownloadFileAsync(string url, string path, long expectedSize, IProgress<DownloadProgress> progress, CancellationToken cancel)
        {
            string part = path + ".part";
            HttpResponseMessage response;
            using (var headers = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                headers.CancelAfter(StallTimeout);
                response = await Http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
            }

            bool abandoned = false;
            try
            {
                if (!response.IsSuccessStatusCode) throw new UpdateException("下载安装包失败（" + (int)response.StatusCode + "）");
                long total = response.Content.Headers.ContentLength ?? expectedSize;
                Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    var watch = Stopwatch.StartNew();
                    long lastReport = -1000;
                    double speed = 0;
                    long windowBytes = 0;
                    long windowStart = 0;
                    while (true)
                    {
                        int read;
                        try
                        {
                            read = await ReadWithWatchdogAsync(input, buffer, cancel).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            abandoned = true;
                            throw;
                        }
                        if (read == 0) break;
                        await output.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                        received += read;
                        windowBytes += read;

                        long now = watch.ElapsedMilliseconds;
                        if (now - windowStart >= 400)
                        {
                            // 速度做指数平滑，避免数字跳动
                            double instant = windowBytes * 1000.0 / Math.Max(1, now - windowStart);
                            speed = speed <= 0 ? instant : speed * 0.6 + instant * 0.4;
                            windowBytes = 0;
                            windowStart = now;
                        }
                        if (progress != null && now - lastReport >= 80)
                        {
                            lastReport = now;
                            progress.Report(new DownloadProgress(received, total, speed));
                        }
                    }
                    if (total > 0 && received != total) throw new UpdateException("下载不完整，请重试");
                    if (progress != null) progress.Report(new DownloadProgress(received, Math.Max(total, received), speed));
                }
            }
            finally
            {
                // 放弃的连接可能还卡在读取上，关闭它也可能等很久：放到后台去关，.part 文件已经在上面关掉了
                if (abandoned) _ = Task.Run(() => response.Dispose());
                else response.Dispose();
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(part, path);
        }

        /// <summary>
        /// .NET Framework 的响应流在读的过程中不理会取消令牌，网络断开（没有 RST）时会一直等下去。
        /// 这里用计时器兜底：超时或用户取消时放弃这次读取，抛出 OperationCanceledException
        /// </summary>
        private static async Task<int> ReadWithWatchdogAsync(Stream input, byte[] buffer, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            Task<int> read = input.ReadAsync(buffer, 0, buffer.Length, cancel);
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                Task first = await Task.WhenAny(read, Task.Delay(StallTimeout, timer.Token)).ConfigureAwait(false);
                if (first == read)
                {
                    timer.Cancel();
                    return await read.ConfigureAwait(false);
                }
            }
            // 放弃的读取在连接关闭后会以异常结束，这里接住，避免未观察的任务异常
            _ = read.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            cancel.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }

        internal static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return Checksums.ToHex(sha.ComputeHash(stream));
            }
        }

        /// <summary>
        /// 解压到空目录。安装包里只允许根目录下的文件（防止“../”之类的路径写到别处）
        /// </summary>
        internal static void Extract(string package, string directory)
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Directory.CreateDirectory(directory);
            using (ZipArchive zip = ZipFile.OpenRead(package))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    string name = entry.FullName;
                    if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        throw new UpdateException("安装包中有不允许的路径：" + name);
                    entry.ExtractToFile(Path.Combine(directory, name), false);
                }
            }
        }

        private static bool MatchesVersion(string exe, Version version)
        {
            Version actual = AssemblyName.GetAssemblyName(exe).Version;
            return Versions.Normalize(actual) == Versions.Normalize(version);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// 删除下载过的更新文件（更新完成后调用）
        /// </summary>
        public static void CleanupStaging()
        {
            try
            {
                if (Directory.Exists(UpdatesRoot)) Directory.Delete(UpdatesRoot, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
