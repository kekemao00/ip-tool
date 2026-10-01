using System;
using System.Collections.Generic;
using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class AdapterReportTests
    {
        [Theory]
        [InlineData("DNS", 12, "DNS         ")]
        [InlineData("IP 获取", 12, "IP 获取     ")]   // “获取”各占两格，共 7 格
        [InlineData("DHCP 服务器", 12, "DHCP 服务器 ")]
        [InlineData("很长很长很长很长的标题", 12, "很长很长很长很长的标题 ")] // 超长时至少留一个空格
        public void PadDisplay_UsesDisplayWidth(string text, int width, string expected)
        {
            Assert.Equal(expected, AdapterReport.PadDisplay(text, width));
        }

        [Fact]
        public void Build_ShowsSavedStaticConfigAndReadOnlyReason()
        {
            var adapters = new List<NetworkAdapter>
            {
                new NetworkAdapter
                {
                    Name = "WLAN",
                    InterfaceDescription = "Intel Wi-Fi",
                    Status = AdapterStatus.Disconnected,
                    IsPhysical = true,
                    IsDhcpEnabled = false,
                    ConfiguredIpAddress = "10.254.254.10",
                    ConfiguredSubnetMask = "255.255.255.0",
                    ConfiguredDns = new[] { "223.5.5.5" }
                },
                new NetworkAdapter
                {
                    Name = "vEthernet",
                    Status = AdapterStatus.Connected,
                    IsVirtual = true,
                    ConfigBackend = BackendKind.None,
                    ReadOnlyReason = "由 Hyper-V 管理"
                }
            };

            string report = AdapterReport.Build(adapters, new DateTime(2026, 10, 2, 9, 30, 0));

            Assert.Contains("生成时间：2026-10-02 09:30:00    共 2 个网卡", report);
            Assert.Contains("[1] WLAN", report);
            Assert.Contains("IPv4 地址   10.254.254.10 / 255.255.255.0", report);
            Assert.Contains("DNS         223.5.5.5（手动）", report);
            Assert.Contains("默认网关    -", report);
            Assert.Contains("说明        由 Hyper-V 管理", report);
        }

        [Fact]
        public void Build_Empty()
        {
            Assert.Contains("没有符合条件的网卡", AdapterReport.Build(new List<NetworkAdapter>(), DateTime.Now));
        }
    }
}
