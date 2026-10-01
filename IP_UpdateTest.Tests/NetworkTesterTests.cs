using Xunit;

namespace IP_UpdateTest.Tests
{
    public class NetworkTesterTests
    {
        private static readonly NetworkTestResult Online = new NetworkTestResult { Success = true };
        private static readonly NetworkTestResult Offline = new NetworkTestResult { Success = false };
        private static readonly NetworkTestResult Portal = new NetworkTestResult { Success = false, NeedsLogin = true };

        [Fact]
        public void Conclude_AllGood()
        {
            Assert.Equal("网络连接正常", NetworkTester.Conclude(true, true, true, Online, true, true, true));
        }

        [Fact]
        public void Conclude_AdapterDisconnected()
        {
            Assert.Contains("网卡未连接", NetworkTester.Conclude(false, true, false, Offline, true, false, false));
        }

        [Fact]
        public void Conclude_GatewayDoesNotAnswerPingButInternetWorks_IsFine()
        {
            // 有的路由器不响应 ping，不能据此判定网关故障
            Assert.Equal("网络连接正常", NetworkTester.Conclude(true, true, false, Online, true, true, true));
        }

        [Fact]
        public void Conclude_GatewayDown()
        {
            Assert.Contains("网关不通", NetworkTester.Conclude(true, true, false, Offline, true, false, false));
        }

        [Fact]
        public void Conclude_NoGateway()
        {
            Assert.Contains("未配置默认网关", NetworkTester.Conclude(true, false, false, Offline, false, false, false));
        }

        [Fact]
        public void Conclude_CaptivePortal()
        {
            Assert.Contains("网页认证", NetworkTester.Conclude(true, true, true, Portal, true, true, true));
        }

        [Fact]
        public void Conclude_InternetDownBehindGateway()
        {
            Assert.Contains("无法访问外网", NetworkTester.Conclude(true, true, true, Offline, true, false, false));
        }

        [Fact]
        public void Conclude_DnsServersDown()
        {
            Assert.Contains("DNS 服务器无响应", NetworkTester.Conclude(true, true, true, Online, true, false, false));
        }

        [Fact]
        public void Conclude_ResolveFailedOnly()
        {
            Assert.Contains("域名解析失败", NetworkTester.Conclude(true, true, true, Online, true, true, false));
        }

        [Fact]
        public void Report_ToText()
        {
            var report = new DiagnosticReport { Conclusion = "网络连接正常" };
            report.Items.Add(new DiagnosticItem("网关", true, "192.168.3.1 延迟 1 ms"));
            report.Items.Add(new DiagnosticItem("DNS 223.5.5.5", false, "无响应"));

            string text = report.ToText();

            Assert.Contains("✓ 网关：192.168.3.1 延迟 1 ms", text);
            Assert.Contains("✗ DNS 223.5.5.5：无响应", text);
            Assert.Contains("结论：网络连接正常", text);
        }
    }
}
