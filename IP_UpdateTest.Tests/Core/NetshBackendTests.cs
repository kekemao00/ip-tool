using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class NetshBackendTests
    {
        [Fact]
        public void BuildStaticCommands_WithGatewayAndTwoDns()
        {
            var commands = NetshBackend.BuildStaticCommands(6, "10.254.254.10", "255.255.255.0", "10.254.254.1",
                new[] { "223.5.5.5", "119.29.29.29" });

            Assert.Equal(new[]
            {
                "interface ipv4 set address name=6 source=static address=10.254.254.10 mask=255.255.255.0 gateway=10.254.254.1",
                "interface ipv4 set dnsservers name=6 source=static address=223.5.5.5 register=primary validate=no",
                "interface ipv4 add dnsservers name=6 address=119.29.29.29 index=2 validate=no"
            }, commands);
        }

        [Fact]
        public void BuildStaticCommands_EmptyGatewayAndDns_ClearsBoth()
        {
            var commands = NetshBackend.BuildStaticCommands(6, "10.254.254.10", "255.255.255.0", "", new string[0]);

            Assert.Equal(new[]
            {
                "interface ipv4 set address name=6 source=static address=10.254.254.10 mask=255.255.255.0 gateway=none",
                "interface ipv4 set dnsservers name=6 source=static address=none register=primary validate=no"
            }, commands);
        }

        [Fact]
        public void BuildDhcpCommands_AutoDns()
        {
            Assert.Equal(new[]
            {
                "interface ipv4 set address name=21 source=dhcp",
                "interface ipv4 set dnsservers name=21 source=dhcp"
            }, NetshBackend.BuildDhcpCommands(21, null));
        }

        [Fact]
        public void BuildDhcpCommands_ManualDns()
        {
            Assert.Equal(new[]
            {
                "interface ipv4 set address name=21 source=dhcp",
                "interface ipv4 set dnsservers name=21 source=static address=223.5.5.5 register=primary validate=no"
            }, NetshBackend.BuildDhcpCommands(21, new[] { "223.5.5.5" }));
        }

        [Fact]
        public void BuildDhcpCommands_AlreadyDhcp_SkipsAddressStep()
        {
            // 实测：网卡已是 DHCP 时 netsh set address source=dhcp 返回失败（“已在此接口上启用 DHCP”）
            Assert.Equal(new[]
            {
                "interface ipv4 set dnsservers name=6 source=static address=223.5.5.5 register=primary validate=no"
            }, NetshBackend.BuildDhcpCommands(6, new[] { "223.5.5.5" }, addressIsDhcp: true));
        }

        [Fact]
        public void BuildDhcpCommands_AlreadyFullyDhcp_NothingToDo()
        {
            Assert.Empty(NetshBackend.BuildDhcpCommands(6, null, addressIsDhcp: true, dnsIsDhcp: true));
        }

        [Fact]
        public void BuildClearGatewayCommand()
        {
            Assert.Equal("interface ipv4 set address name=21 source=static address=192.168.1.10 mask=255.255.255.0 gateway=none",
                NetshBackend.BuildClearGatewayCommand(21, "192.168.1.10", "255.255.255.0"));
        }
    }

    public class ConsoleTextTests
    {
        [Fact]
        public void Decode_Utf8Output()
        {
            // 从 UTF-8 控制台（PowerShell 7）启动时 netsh 输出 UTF-8
            byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes("已在此接口上启用 DHCP。");

            Assert.Equal("已在此接口上启用 DHCP。", ConsoleText.Decode(bytes));
        }

        [Fact]
        public void Decode_OemOutput()
        {
            // 从窗口程序启动时 netsh 按系统 OEM 代码页输出（中文系统为 936，即 GBK）
            System.Text.Encoding oem = ConsoleText.OemEncoding;
            byte[] bytes = oem.GetBytes("已在此接口上启用 DHCP。");

            Assert.Equal(oem.GetString(bytes), ConsoleText.Decode(bytes));
        }
    }
}
