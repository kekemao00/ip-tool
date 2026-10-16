using System;
using System.Threading;
using System.Threading.Tasks;

namespace IP_UpdateTest.Core.Update
{
    public enum UpdateState
    {
        /// <summary>还没检查，或已是最新</summary>
        Idle,
        Checking,
        /// <summary>有新版本，尚未下载</summary>
        Available,
        Downloading,
        Verifying,
        /// <summary>已下载并校验，等待重启</summary>
        Ready,
        Installing,
        /// <summary>下载、校验或替换失败（Error 中有原因）</summary>
        Failed
    }

    /// <summary>
    /// 更新流程的状态：检查 → 有新版本 → 下载 → 校验 → 就绪 → 替换并重启。
    /// 在界面线程上使用，状态每次变化都触发 Changed；标题栏胶囊和更新面板都只读这里的状态
    /// </summary>
    public sealed class UpdateManager
    {
        private readonly UpdateClient client;
        private CancellationTokenSource download;

        public UpdateManager(Version current, UpdateClient client)
        {
            CurrentVersion = Versions.Normalize(current);
            this.client = client;
        }

        public event Action Changed;

        public Version CurrentVersion { get; private set; }
        public UpdateState State { get; private set; }

        /// <summary>
        /// 最近一次查到的更新版本（没有更新时为 null）
        /// </summary>
        public ReleaseInfo Release { get; private set; }

        public DownloadProgress Progress { get; private set; }
        public string Error { get; private set; }

        /// <summary>
        /// 已校验的新文件所在目录（Ready 时有效）
        /// </summary>
        public string StagedDirectory { get; private set; }

        public bool HasUpdate
        {
            get { return Release != null && State != UpdateState.Idle && State != UpdateState.Checking; }
        }

        public bool IsBusy
        {
            get { return State == UpdateState.Checking || State == UpdateState.Downloading || State == UpdateState.Verifying || State == UpdateState.Installing; }
        }

        /// <summary>
        /// 检查更新。返回新版本（没有更新时返回 null）；网络等错误抛出 UpdateException。
        /// 已在下载或已就绪时不重复检查，直接返回当前的版本
        /// </summary>
        public async Task<ReleaseInfo> CheckAsync(CancellationToken cancel)
        {
            if (State != UpdateState.Idle && State != UpdateState.Available && State != UpdateState.Failed) return Release;
            UpdateState previous = State;
            SetState(UpdateState.Checking);
            ReleaseInfo latest;
            try
            {
                latest = await client.GetLatestAsync(cancel);
            }
            catch (UpdateException)
            {
                SetState(previous);
                throw;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // 意外的错误也只当作检查失败，不让后台检查把程序带崩
                SetState(previous);
                throw new UpdateException(ex.Message, ex);
            }

            if (!Versions.IsNewer(latest.Version, CurrentVersion))
            {
                Release = null;
                SetState(UpdateState.Idle);
                return null;
            }

            Release = latest;
            Error = null;
            string staged = UpdateClient.FindStaged(latest);
            if (staged != null)
            {
                StagedDirectory = staged;
                SetState(UpdateState.Ready);
            }
            else
            {
                SetState(UpdateState.Available);
            }
            return latest;
        }

        /// <summary>
        /// 开始下载（在后台进行，进度通过 Changed 通知）
        /// </summary>
        public async void StartDownload()
        {
            if (Release == null || (State != UpdateState.Available && State != UpdateState.Failed)) return;
            ReleaseInfo release = Release;
            SynchronizationContext context = SynchronizationContext.Current;
            download = new CancellationTokenSource();
            CancellationToken token = download.Token;
            Error = null;
            Progress = new DownloadProgress(0, release.PackageSize, 0);
            SetState(UpdateState.Downloading);

            var progress = new Progress<DownloadProgress>(p =>
            {
                if (token.IsCancellationRequested || State != UpdateState.Downloading) return;
                Progress = p;
                Raise();
            });
            try
            {
                StagedDirectory = await client.DownloadAsync(release, progress, () => Post(context, () =>
                {
                    if (!token.IsCancellationRequested) SetState(UpdateState.Verifying);
                }), token);
                if (token.IsCancellationRequested) return;
                SetState(UpdateState.Ready);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // CancelDownload 已经改回 Available
            }
            catch (UpdateException ex)
            {
                if (token.IsCancellationRequested) return;
                Fail(ex.Message);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // 后台下载是 async void，意外的错误不能抛出去
                if (token.IsCancellationRequested) return;
                Fail("更新失败：" + ex.Message);
            }
            finally
            {
                if (download != null && download.Token == token)
                {
                    download.Dispose();
                    download = null;
                }
            }
        }

        public void CancelDownload()
        {
            if (download == null) return;
            download.Cancel();
            download = null;
            if (State == UpdateState.Downloading || State == UpdateState.Verifying) SetState(UpdateState.Available);
        }

        /// <summary>
        /// 替换程序文件。返回 false 表示用户取消了管理员权限请求；失败时抛出 UpdateException
        /// </summary>
        public async Task<bool> InstallAsync(string targetDirectory, string executablePath)
        {
            if (State != UpdateState.Ready || StagedDirectory == null) return false;
            SetState(UpdateState.Installing);
            try
            {
                if (!await UpdateInstaller.InstallAsync(StagedDirectory, targetDirectory, executablePath))
                {
                    SetState(UpdateState.Ready);
                    return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException
                || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                Fail("替换程序文件失败：" + ex.Message);
                throw new UpdateException(Error, ex);
            }
        }

        private void Fail(string message)
        {
            Error = message;
            SetState(UpdateState.Failed);
        }

        private void SetState(UpdateState state)
        {
            State = state;
            Raise();
        }

        private void Raise()
        {
            Changed?.Invoke();
        }

        private static void Post(SynchronizationContext context, Action action)
        {
            if (context == null) action();
            else context.Post(_ => action(), null);
        }

        /// <summary>
        /// 直接设置状态（界面截图测试用）
        /// </summary>
        internal void Simulate(UpdateState state, ReleaseInfo release, DownloadProgress progress = default(DownloadProgress), string error = null)
        {
            Release = release;
            Progress = progress;
            Error = error;
            SetState(state);
        }
    }
}
