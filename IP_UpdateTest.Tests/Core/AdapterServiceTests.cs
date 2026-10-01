using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class AdapterServiceTests
    {
        private static BackendKind Determine(AdapterStatus status, bool wmiIpEnabled, bool hasIndex, out string reason, out string note)
        {
            return AdapterService.DetermineBackend(status, wmiIpEnabled, hasIndex, out reason, out note);
        }

        [Fact]
        public void DetermineBackend_ConnectedWithWmi_UsesWmi()
        {
            string reason, note;
            Assert.Equal(BackendKind.Wmi, Determine(AdapterStatus.Connected, true, true, out reason, out note));
            Assert.Null(reason);
        }

        [Fact]
        public void DetermineBackend_Disconnected_UsesNetshWithNote()
        {
            // 实测：未连接的 WLAN 在 WMI 中 IPEnabled=False
            string reason, note;
            Assert.Equal(BackendKind.Netsh, Determine(AdapterStatus.Disconnected, false, true, out reason, out note));
            Assert.Contains("静态 IP 需连接后设置", note);
        }

        [Fact]
        public void DetermineBackend_ConnectedWithoutWmi_IsReadOnly()
        {
            // 实测：Hyper-V Default Switch 已连接但不在 Win32_NetworkAdapterConfiguration 中
            string reason, note;
            Assert.Equal(BackendKind.None, Determine(AdapterStatus.Connected, false, true, out reason, out note));
            Assert.Contains("Hyper-V", reason);
        }

        [Fact]
        public void DetermineBackend_Disabled_IsReadOnly()
        {
            string reason, note;
            Assert.Equal(BackendKind.None, Determine(AdapterStatus.Disabled, true, true, out reason, out note));
            Assert.Contains("禁用", reason);
        }

        [Fact]
        public void DetermineBackend_NoIndex_IsReadOnly()
        {
            string reason, note;
            Assert.Equal(BackendKind.None, Determine(AdapterStatus.Disconnected, false, false, out reason, out note));
        }

        private static readonly IpConfigRequest StaticNoGateway = IpConfigRequest.Static("192.168.1.10", "255.255.255.0", "", new string[0]);

        [Fact]
        public void NeedsGatewayClear_OnlyWhenStaticRequestDropsExistingGateway()
        {
            Assert.True(AdapterService.NeedsGatewayClear("192.168.1.1", StaticNoGateway));
            Assert.False(AdapterService.NeedsGatewayClear("", StaticNoGateway));
            Assert.False(AdapterService.NeedsGatewayClear("192.168.1.1",
                IpConfigRequest.Static("192.168.1.10", "255.255.255.0", "192.168.1.1", new string[0])));
            Assert.False(AdapterService.NeedsGatewayClear("192.168.1.1", IpConfigRequest.Dhcp()));
        }

        [Fact]
        public void CheckSupported_ConnectedWmiAdapter_AllowsAll()
        {
            var adapter = new NetworkAdapter { ConfigBackend = BackendKind.Wmi, InterfaceIndex = 21 };

            Assert.Null(AdapterService.CheckSupported(adapter, StaticNoGateway));
            Assert.Null(AdapterService.CheckSupported(adapter, IpConfigRequest.Dhcp()));
        }

        [Fact]
        public void CheckSupported_Disconnected_RefusesStatic()
        {
            // 实测：未连接网卡上 netsh 设置静态地址不会关闭 DHCP，造成 DHCP 与静态地址并存
            var adapter = new NetworkAdapter { ConfigBackend = BackendKind.Netsh, InterfaceIndex = 6, IsDhcpEnabled = true };

            Assert.Equal(AdapterService.StaticOnDisconnectedReason, AdapterService.CheckSupported(adapter, StaticNoGateway));
            Assert.Null(AdapterService.CheckSupported(adapter, IpConfigRequest.Dhcp(new[] { "223.5.5.5" })));
        }

        [Fact]
        public void CheckSupported_DisconnectedWithStaticLeftovers_RefusesDhcp()
        {
            var adapter = new NetworkAdapter { ConfigBackend = BackendKind.Netsh, InterfaceIndex = 6, IsDhcpEnabled = true, ConfiguredIpAddress = "10.254.254.10" };

            Assert.Contains("冲突", AdapterService.CheckSupported(adapter, IpConfigRequest.Dhcp()));
        }

        [Fact]
        public void CheckSupported_ReadOnlyAdapter_ReturnsReason()
        {
            var adapter = new NetworkAdapter { ConfigBackend = BackendKind.None, ReadOnlyReason = "由 Hyper-V 管理" };

            Assert.Equal("由 Hyper-V 管理", AdapterService.CheckSupported(adapter, IpConfigRequest.Dhcp()));
        }

        [Fact]
        public void CheckSupported_ForcedBackend_SkipsDisconnectedRule()
        {
            var adapter = new NetworkAdapter { ConfigBackend = BackendKind.Netsh, InterfaceIndex = 6, IsDhcpEnabled = true };

            Assert.Null(AdapterService.CheckSupported(adapter, StaticNoGateway, BackendKind.Wmi));
        }

        [Theory]
        [InlineData("223.5.5.5,119.29.29.29", new[] { "223.5.5.5", "119.29.29.29" })]
        [InlineData("223.5.5.5 119.29.29.29", new[] { "223.5.5.5", "119.29.29.29" })]
        [InlineData("", new string[0])]
        [InlineData(null, new string[0])]
        public void SplitServers(string value, string[] expected)
        {
            Assert.Equal(expected, AdapterService.SplitServers(value));
        }
    }
}
