using System.Net.NetworkInformation;
using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests
{
    public class IpProfileTests
    {
        [Fact]
        public void ToRequest_OldDhcpProfileWithDns_KeepsAutoDns()
        {
            // 旧版本保存 DHCP 方案时也会存下当时显示的 DNS，但应用时会把 DNS 恢复为自动
            var profile = new IpProfile { IsDhcp = true, DnsMain = "192.168.3.1", ManualDns = null };

            IpConfigRequest request = profile.ToRequest();

            Assert.True(request.UseDhcp);
            Assert.True(request.UseDhcpDns);
            Assert.Empty(request.DnsServers);
        }

        [Fact]
        public void ToRequest_DhcpWithManualDns()
        {
            var profile = new IpProfile { IsDhcp = true, ManualDns = true, DnsMain = "223.5.5.5", DnsBackup = "119.29.29.29" };

            IpConfigRequest request = profile.ToRequest();

            Assert.False(request.UseDhcpDns);
            Assert.Equal(new[] { "223.5.5.5", "119.29.29.29" }, request.DnsServers);
            Assert.Empty(request.Validate());
        }

        [Fact]
        public void ToRequest_Static()
        {
            var profile = new IpProfile { IpAddress = "10.0.0.5", SubnetMask = "255.255.255.0", Gateway = "10.0.0.1", DnsMain = "10.0.0.1" };

            IpConfigRequest request = profile.ToRequest();

            Assert.False(request.UseDhcp);
            Assert.Equal("10.0.0.5", request.IpAddress);
            Assert.Equal(new[] { "10.0.0.1" }, request.DnsServers);
            Assert.Empty(request.Validate());
        }
    }

    public class IpConfigRequestTests
    {
        [Fact]
        public void DnsList_SkipsBlanks()
        {
            Assert.Equal(new[] { "223.5.5.5" }, IpConfigRequest.DnsList(" 223.5.5.5 ", " "));
        }

        [Fact]
        public void Validate_DhcpWithManualDnsButEmpty_RequiresMainDns()
        {
            var request = new IpConfigRequest { UseDhcp = true, UseDhcpDns = false, DnsServers = new string[0] };

            Assert.Equal(IpField.DnsMain, Assert.Single(request.Validate()).Field);
        }

        [Fact]
        public void Validate_DhcpAutoDns_NoErrors()
        {
            Assert.Empty(IpConfigRequest.Dhcp().Validate());
        }
    }

    public class NetworkAdapterTests
    {
        [Theory]
        [InlineData(1000000000L, "1 Gbps")]
        [InlineData(2500000000L, "2.5 Gbps")]
        [InlineData(100000000L, "100 Mbps")]
        [InlineData(0L, "-")]
        public void FormatSpeed(long bps, string expected)
        {
            Assert.Equal(expected, NetworkAdapter.FormatSpeed(bps));
        }

        [Fact]
        public void FormatMac_UsesWindowsStyle()
        {
            Assert.Equal("00-E2-69-6E-F7-3D", NetworkAdapter.FormatMac(PhysicalAddress.Parse("00E2696EF73D")));
            Assert.Equal("", NetworkAdapter.FormatMac(null));
        }

        [Fact]
        public void DisconnectedStaticAdapter_ShowsSavedConfig()
        {
            var adapter = new NetworkAdapter
            {
                Status = AdapterStatus.Disconnected,
                IsDhcpEnabled = false,
                ConfiguredIpAddress = "10.254.254.10",
                ConfiguredSubnetMask = "255.255.255.0",
                ConfiguredGateway = "10.254.254.1",
                ConfiguredDns = new[] { "223.5.5.5", "119.29.29.29" }
            };

            Assert.Equal("10.254.254.10", adapter.IpAddress);
            Assert.Equal("255.255.255.0", adapter.SubnetMask);
            Assert.Equal("10.254.254.1", adapter.Gateway);
            Assert.Equal("119.29.29.29", adapter.DnsBackup);
            Assert.True(adapter.HasStaticDns);
        }

        [Fact]
        public void DisconnectedDhcpAdapter_HidesStaleStaticConfig()
        {
            var adapter = new NetworkAdapter { IsDhcpEnabled = true, ConfiguredIpAddress = "10.254.254.10" };

            Assert.Equal("", adapter.IpAddress);
        }

        [Fact]
        public void DisplayNameAndStatus()
        {
            var adapter = new NetworkAdapter
            {
                Name = "以太网",
                InterfaceDescription = "Realtek Gaming 2.5GbE Family Controller",
                Status = AdapterStatus.Connected,
                LinkSpeedBps = 1000000000L,
                IsPhysical = true
            };

            Assert.Equal("以太网 — Realtek Gaming 2.5GbE Family Controller", adapter.DisplayName);
            Assert.Equal("已连接 · 1 Gbps", adapter.StatusText);

            adapter.Status = AdapterStatus.Disconnected;
            adapter.IsPhysical = false;
            adapter.IsVirtual = true;
            Assert.Equal("未连接 · 虚拟网卡", adapter.StatusText);
        }
    }
}
