using System.Collections.Generic;
using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests
{
    public class CommandLineTests
    {
        [Fact]
        public void IsCliInvocation_AdapterOnly_OpensGui()
        {
            // 以管理员身份重启界面时只带 --adapter
            Assert.False(CommandLine.IsCliInvocation(new[] { "--adapter", "{F64D4136-A54B-485D-AE96-742D3946BE16}" }));
            Assert.False(CommandLine.IsCliInvocation(new string[0]));
            Assert.True(CommandLine.IsCliInvocation(new[] { "--list" }));
            Assert.True(CommandLine.IsCliInvocation(new[] { "--adapter", "WLAN", "--DHCP" }));
        }

        [Fact]
        public void Parse_StaticWithCidr()
        {
            CliOptions o = CommandLine.Parse(new[] { "--adapter", "WLAN", "--static", "10.254.254.10/24", "--gateway", "10.254.254.1", "--dns", "223.5.5.5, 119.29.29.29" });

            Assert.Null(o.Error);
            Assert.Equal(CliAction.Static, o.Action);
            Assert.Equal("WLAN", o.Adapter);
            Assert.Equal("10.254.254.10", o.IpAddress);
            Assert.Equal("255.255.255.0", o.SubnetMask);
            Assert.Equal("10.254.254.1", o.Gateway);
            Assert.Equal(new[] { "223.5.5.5", "119.29.29.29" }, o.Dns);
        }

        [Fact]
        public void Parse_StaticWithMask()
        {
            CliOptions o = CommandLine.Parse(new[] { "--adapter", "以太网", "--static", "192.168.1.10", "--mask", "255.255.255.0" });

            Assert.Null(o.Error);
            Assert.Equal("255.255.255.0", o.SubnetMask);
            Assert.Null(o.Dns);
        }

        [Fact]
        public void Parse_DhcpWithBackendAndNoElevate()
        {
            CliOptions o = CommandLine.Parse(new[] { "--adapter", "6", "--dhcp", "--backend", "NETSH", "--no-elevate" });

            Assert.Null(o.Error);
            Assert.Equal(CliAction.Dhcp, o.Action);
            Assert.Equal(BackendKind.Netsh, o.Backend);
            Assert.True(o.NoElevate);
        }

        [Theory]
        [InlineData(new[] { "--adapter", "WLAN" }, "操作")]
        [InlineData(new[] { "--adapter", "WLAN", "--dhcp", "--disable" }, "一次只能")]
        [InlineData(new[] { "--dhcp" }, "--adapter")]
        [InlineData(new[] { "--adapter", "WLAN", "--static", "10.0.0.5" }, "子网掩码")]
        [InlineData(new[] { "--adapter", "WLAN", "--static", "10.0.0.5/24", "--mask", "255.0.0.0" }, "--mask")]
        [InlineData(new[] { "--adapter", "WLAN", "--dhcp", "--gateway", "10.0.0.1" }, "--gateway")]
        [InlineData(new[] { "--adapter", "WLAN", "--enable", "--dns", "223.5.5.5" }, "--dns")]
        [InlineData(new[] { "--adapter", "WLAN", "--dhcp", "--backend", "none" }, "--backend")]
        [InlineData(new[] { "--adapter" }, "--adapter")]
        [InlineData(new[] { "--list", "--bogus" }, "--bogus")]
        public void Parse_Errors(string[] args, string expected)
        {
            CliOptions o = CommandLine.Parse(args);

            Assert.NotNull(o.Error);
            Assert.Contains(expected, o.Error);
        }

        [Fact]
        public void Parse_ListJson()
        {
            CliOptions o = CommandLine.Parse(new[] { "--list", "--json" });

            Assert.Null(o.Error);
            Assert.Equal(CliAction.List, o.Action);
            Assert.True(o.Json);
        }

        [Theory]
        [InlineData("WLAN", "WLAN")]
        [InlineData("vEthernet (Default Switch)", "\"vEthernet (Default Switch)\"")]
        [InlineData("", "\"\"")]
        [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [InlineData(@"C:\Temp Dir\", "\"C:\\Temp Dir\\\\\"")]
        public void QuoteArgument(string arg, string expected)
        {
            Assert.Equal(expected, CommandLine.QuoteArgument(arg));
        }

        [Fact]
        public void MatchAdapter_ByNameIndexGuidOrDescription()
        {
            var adapters = new List<NetworkAdapter>
            {
                new NetworkAdapter { Name = "以太网", InterfaceIndex = 21, NetworkInterfaceID = "{11111111-2222-3333-4444-555555555555}", InterfaceDescription = "Realtek" },
                new NetworkAdapter { Name = "WLAN", InterfaceIndex = 6, NetworkInterfaceID = "{AAAAAAAA-2222-3333-4444-555555555555}", InterfaceDescription = "Intel Wi-Fi" }
            };

            Assert.Equal("WLAN", AdapterService.MatchAdapter(adapters, "wlan").Name);
            Assert.Equal("以太网", AdapterService.MatchAdapter(adapters, "21").Name);
            Assert.Equal("WLAN", AdapterService.MatchAdapter(adapters, "aaaaaaaa-2222-3333-4444-555555555555").Name);
            Assert.Equal("WLAN", AdapterService.MatchAdapter(adapters, "Intel Wi-Fi").Name);
            Assert.Null(AdapterService.MatchAdapter(adapters, "蓝牙"));
        }
    }
}
