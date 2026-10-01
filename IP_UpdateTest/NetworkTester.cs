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
    }

    /// <summary>
    /// 诊断报告中的一项
    /// </summary>
    public sealed class DiagnosticItem
    {
        public DiagnosticItem(string title, bool ok, string detail)
        {
            Title = title;
            Ok = ok;
            Detail = detail;
        }

        public string Title { get; private set; }
        public bool Ok { get; private set; }
        public string Detail { get; private set; }
    }

    /// <summary>
    /// 网络诊断报告
    /// </summary>
    public sealed class DiagnosticReport
    {
        public List<DiagnosticItem> Items { get; } = new List<DiagnosticItem>();

        public string Conclusion { get; set; }

        public string ToText()
        {
            var sb = new StringBuilder();
            foreach (DiagnosticItem item in Items)
            {
                sb.AppendLine($"{(item.Ok ? "✓" : "✗")} {item.Title}：{item.Detail}");
            }
            sb.AppendLine();
            sb.AppendLine("结论：" + Conclusion);
            return sb.ToString();
        }
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

        private const string ProbeHost = "www.baidu.com";
        private static readonly string[] PingTargets = { "223.5.5.5", "114.114.114.114" };

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
                result.RoundtripTime = sw.ElapsedMilliseconds;
                result.Message = result.Success
                    ? $"{hostname} → {addresses[0]}（{sw.ElapsedMilliseconds} ms）"
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
        /// 网关连通性测试
        /// </summary>
        public static async Task<NetworkTestResult> TestGatewayAsync(string gateway)
        {
            if (string.IsNullOrEmpty(gateway))
                return new NetworkTestResult { Success = false, Message = "未配置网关" };
            return await PingAsync(gateway, 2000);
        }

        /// <summary>
        /// HTTP 方式检测外网。被重定向或返回内容不对时，通常是需要网页认证的网络（如酒店、机场 Wi-Fi）
        /// </summary>
        public static async Task<NetworkTestResult> TestHttpAsync(string url, string expectedContent, int timeoutMilliseconds)
        {
            var result = new NetworkTestResult { Target = url };
            HttpWebRequest request = WebRequest.CreateHttp(url);
            request.AllowAutoRedirect = false;
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
                    if (response.StatusCode == HttpStatusCode.OK && body.Contains(expectedContent))
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
                result.Message = "HTTP 访问失败：" + ex.Message;
            }
            catch (IOException ex)
            {
                result.Message = "HTTP 访问失败：" + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// 外网连通性：先用 Windows 的连通性检测地址（HTTP，企业网禁 ping 时也能判断），失败再 ping 公共 DNS
        /// </summary>
        public static async Task<NetworkTestResult> TestInternetAsync()
        {
            Task<NetworkTestResult> http = TestHttpAsync(NcsiUrl, NcsiContent, 3000);
            List<Task<NetworkTestResult>> pings = PingTargets.Select(t => PingAsync(t, 2000)).ToList();
            await Task.WhenAll(pings.Cast<Task>().Concat(new[] { http }));

            if (http.Result.Success || http.Result.NeedsLogin) return http.Result;
            NetworkTestResult ping = pings.Select(p => p.Result).FirstOrDefault(p => p.Success);
            if (ping != null)
            {
                ping.Message = $"正常（ping {ping.Target} {ping.RoundtripTime} ms；HTTP 检测未通过，可能有代理或防火墙）";
                return ping;
            }
            return new NetworkTestResult { Success = false, Message = "无法访问外网（HTTP 和 ping 均失败）" };
        }

        /// <summary>
        /// 完整网络诊断，各项检测并行进行
        /// </summary>
        public static async Task<DiagnosticReport> DiagnoseAsync(NetworkAdapter adapter)
        {
            string gateway = adapter == null ? "" : adapter.Gateway;
            var dnsServers = new List<IPAddress>();
            if (adapter != null)
            {
                foreach (string server in new[] { adapter.DnsMain, adapter.DnsBackup })
                {
                    IPAddress address;
                    if (IpValidator.TryParseIPv4(server, out address)) dnsServers.Add(address);
                }
            }

            Task<NetworkTestResult> gatewayTask = TestGatewayAsync(gateway);
            Task<NetworkTestResult> internetTask = TestInternetAsync();
            Task<NetworkTestResult> resolveTask = DnsResolveAsync(ProbeHost);
            List<Task<long?>> dnsTasks = dnsServers.Select(s => DnsProbe.QueryAsync(s, ProbeHost, 2000)).ToList();
            await Task.WhenAll(new Task[] { gatewayTask, internetTask, resolveTask }.Concat(dnsTasks));

            var report = new DiagnosticReport();
            bool connected = adapter == null || adapter.Status == AdapterStatus.Connected;
            if (adapter != null)
                report.Items.Add(new DiagnosticItem("网卡", connected, $"{adapter.Name} · {adapter.StatusText}"));

            NetworkTestResult gw = gatewayTask.Result;
            report.Items.Add(new DiagnosticItem("网关", gw.Success,
                string.IsNullOrEmpty(gateway) ? "未配置" : $"{gateway} {gw.Message}"));

            NetworkTestResult internet = internetTask.Result;
            report.Items.Add(new DiagnosticItem("外网", internet.Success, internet.Message));

            for (int i = 0; i < dnsServers.Count; i++)
            {
                long? elapsed = dnsTasks[i].Result;
                report.Items.Add(new DiagnosticItem($"DNS {dnsServers[i]}", elapsed.HasValue,
                    elapsed.HasValue ? $"响应 {elapsed} ms" : "无响应"));
            }

            NetworkTestResult resolve = resolveTask.Result;
            report.Items.Add(new DiagnosticItem("域名解析", resolve.Success, resolve.Message));

            report.Conclusion = Conclude(connected, !string.IsNullOrEmpty(gateway), gw.Success, internet,
                dnsServers.Count > 0, dnsTasks.Any(t => t.Result.HasValue), resolve.Success);
            return report;
        }

        /// <summary>
        /// 根据各项结果给出结论（纯函数，便于测试）
        /// </summary>
        public static string Conclude(bool adapterConnected, bool hasGateway, bool gatewayOk, NetworkTestResult internet,
            bool hasDnsServers, bool anyDnsOk, bool resolveOk)
        {
            if (!adapterConnected) return "网卡未连接，请检查网线或 Wi-Fi 连接";
            if (!hasGateway && !internet.Success) return "未配置默认网关，只能访问本网段，无法上网";
            // 有的路由器不响应 ping，只有外网也不通时才判定网关有问题
            if (!gatewayOk && !internet.Success) return "网关不通，请检查网线、Wi-Fi 或 IP 配置（IP 与网关是否在同一网段）";
            if (internet.NeedsLogin) return "需要网页认证，请在浏览器中打开任意网页完成登录（如酒店、机场 Wi-Fi）";
            if (!internet.Success) return "网关正常但无法访问外网，可能是路由器或运营商线路问题";
            if (hasDnsServers && !anyDnsOk) return "DNS 服务器无响应，建议更换 DNS（可用“测速”选择较快的 DNS）";
            if (!resolveOk) return "域名解析失败，建议更换 DNS 后重试";
            return "网络连接正常";
        }
    }
}
