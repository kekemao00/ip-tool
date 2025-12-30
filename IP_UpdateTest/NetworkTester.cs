using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

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
    }

    /// <summary>
    /// 网络测试工具
    /// </summary>
    public static class NetworkTester
    {
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
                        ? $"延迟: {reply.RoundtripTime}ms"
                        : $"失败: {reply.Status}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"错误: {ex.Message}";
            }
            return result;
        }

        /// <summary>
        /// DNS 解析测试
        /// </summary>
        public static async Task<NetworkTestResult> DnsResolveAsync(string hostname)
        {
            var result = new NetworkTestResult { Target = hostname };
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var addresses = await Dns.GetHostAddressesAsync(hostname);
                sw.Stop();

                result.Success = addresses.Length > 0;
                result.RoundtripTime = sw.ElapsedMilliseconds;
                result.Message = result.Success
                    ? $"解析成功: {addresses[0]} ({sw.ElapsedMilliseconds}ms)"
                    : "解析失败: 无结果";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"解析失败: {ex.Message}";
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
        /// 外网连通性测试
        /// </summary>
        public static async Task<NetworkTestResult> TestInternetAsync()
        {
            // 测试多个目标，任一成功即可
            string[] targets = { "114.114.114.114", "223.5.5.5", "8.8.8.8" };
            foreach (var target in targets)
            {
                var result = await PingAsync(target, 2000);
                if (result.Success) return result;
            }
            return new NetworkTestResult { Success = false, Message = "无法连接外网" };
        }

        /// <summary>
        /// 完整网络诊断
        /// </summary>
        public static async Task<string> DiagnoseAsync(string gateway)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 网络诊断报告 ===\n");

            // 1. 网关测试
            sb.Append("1. 网关连通性: ");
            var gwResult = await TestGatewayAsync(gateway);
            sb.AppendLine(gwResult.Success ? $"正常 ({gwResult.RoundtripTime}ms)" : gwResult.Message);

            // 2. 外网测试
            sb.Append("2. 外网连通性: ");
            var netResult = await TestInternetAsync();
            sb.AppendLine(netResult.Success ? $"正常 - {netResult.Target} ({netResult.RoundtripTime}ms)" : netResult.Message);

            // 3. DNS 测试
            sb.Append("3. DNS 解析: ");
            var dnsResult = await DnsResolveAsync("www.baidu.com");
            sb.AppendLine(dnsResult.Success ? dnsResult.Message : dnsResult.Message);

            // 诊断结论
            sb.AppendLine("\n=== 诊断结论 ===");
            if (!gwResult.Success)
                sb.AppendLine("• 网关不通，请检查网线连接或 IP 配置");
            else if (!netResult.Success)
                sb.AppendLine("• 网关正常但无法访问外网，可能是路由器问题");
            else if (!dnsResult.Success)
                sb.AppendLine("• 网络正常但 DNS 解析失败，建议更换 DNS 服务器");
            else
                sb.AppendLine("• 网络连接正常");

            return sb.ToString();
        }
    }
}
