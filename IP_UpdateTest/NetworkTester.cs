using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using IP_UpdateTest.Core;

namespace IP_UpdateTest
{
    /// <summary>
    /// 网络测试结果
    /// </summary>
    public class NetworkTestResult
    {
        public bool Success { get; set; }
        public string Target { get; set; }
        public long RoundtripTime { get; set; }
        public string Message { get; set; }

        /// <summary>
        /// HTTP 检测被重定向或返回内容不对，通常是需要网页认证的网络
        /// </summary>
        public bool NeedsLogin { get; set; }

        /// <summary>
        /// HTTPS 证书校验失败（HTTPS 被拦截或系统时间错误）
        /// </summary>
        public bool CertificateError { get; set; }

        /// <summary>
        /// 域名解析得到的地址
        /// </summary>
        public string Address { get; set; }
    }

    /// <summary>
    /// 网络测试工具
    /// </summary>
    public static class NetworkTester
    {
        /// <summary>
        /// Windows 自身检测网络连通性（NCSI）所用的地址及应返回的内容
        /// </summary>
        public const string NcsiUrl = "http://www.msftconnecttest.com/connecttest.txt";
        public const string NcsiContent = "Microsoft Connect Test";

        /// <summary>
        /// Ping 测试
        /// </summary>
        public static async Task<NetworkTestResult> PingAsync(string host, int timeout = 3000)
        {
            var result = new NetworkTestResult { Target = host };
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(host, timeout);
                    result.Success = reply.Status == IPStatus.Success;
                    result.RoundtripTime = reply.RoundtripTime;
                    result.Message = result.Success
                        ? $"延迟 {reply.RoundtripTime} ms"
                        : DescribeStatus(reply.Status);
                }
            }
            catch (PingException ex)
            {
                result.Success = false;
                result.Message = "错误：" + (ex.InnerException ?? ex).Message;
            }
            return result;
        }

        private static string DescribeStatus(IPStatus status)
        {
            switch (status)
            {
                case IPStatus.TimedOut: return "超时无响应";
                case IPStatus.DestinationHostUnreachable: return "主机不可达";
                case IPStatus.DestinationNetworkUnreachable: return "网络不可达";
                default: return "失败：" + status;
            }
        }

        /// <summary>
        /// 用系统解析器解析域名（会用到系统缓存）
        /// </summary>
        public static async Task<NetworkTestResult> DnsResolveAsync(string hostname)
        {
            var result = new NetworkTestResult { Target = hostname };
            try
            {
                var sw = Stopwatch.StartNew();
                var addresses = await Dns.GetHostAddressesAsync(hostname);
                sw.Stop();

                result.Success = addresses.Length > 0;
                IPAddress first = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?? addresses.FirstOrDefault();
                result.Address = first?.ToString();
                result.RoundtripTime = sw.ElapsedMilliseconds;
                result.Message = result.Success
                    ? $"{hostname} → {result.Address}（{sw.ElapsedMilliseconds} ms）"
                    : "解析失败：无结果";
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                result.Success = false;
                result.Message = $"解析失败：{ex.Message}";
            }
            return result;
        }

        /// <summary>
        /// HTTP(S) 方式检测外网。指定 expectedContent 时，被重定向或返回内容不对通常是需要网页认证的网络（如酒店、机场 Wi-Fi）；
        /// 不指定时返回 2xx、3xx 即算成功。direct 为 true 时不经过系统代理。
        /// </summary>
        public static async Task<NetworkTestResult> TestHttpAsync(string url, string expectedContent, int timeoutMilliseconds, bool direct = false)
        {
            var result = new NetworkTestResult { Target = url };
            HttpWebRequest request = WebRequest.CreateHttp(url);
            request.AllowAutoRedirect = false;
            if (direct) request.Proxy = null;
            request.Timeout = timeoutMilliseconds;
            request.ReadWriteTimeout = timeoutMilliseconds;

            Stopwatch watch = Stopwatch.StartNew();
            Task<WebResponse> responseTask = request.GetResponseAsync();
            if (await Task.WhenAny(responseTask, Task.Delay(timeoutMilliseconds)) != responseTask)
            {
                request.Abort();
                _ = responseTask.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                result.Message = "HTTP 访问超时";
                return result;
            }

            try
            {
                using (var response = (HttpWebResponse)await responseTask)
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string body = await reader.ReadToEndAsync();
                    result.RoundtripTime = watch.ElapsedMilliseconds;
                    if (expectedContent == null ? (int)response.StatusCode < 400
                        : response.StatusCode == HttpStatusCode.OK && body.Contains(expectedContent))
                    {
                        result.Success = true;
                        result.Message = $"正常（HTTP {result.RoundtripTime} ms）";
                    }
                    else
                    {
                        result.NeedsLogin = true;
                        result.Message = $"返回内容异常（HTTP {(int)response.StatusCode}），可能需要网页认证";
                    }
                }
            }
            catch (WebException ex)
            {
                result.CertificateError = ex.Status == WebExceptionStatus.TrustFailure;
                result.Message = "HTTP 访问失败：" + ex.Message;
            }
            catch (IOException ex)
            {
                result.Message = "HTTP 访问失败：" + ex.Message;
            }
            return result;
        }
    }
}
