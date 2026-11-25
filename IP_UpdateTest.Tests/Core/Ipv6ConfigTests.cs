using System.Collections.Generic;
using System.Linq;
using System.Net;
using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class Ipv6ConfigTests
    {
        private static Ipv6ConfigRequest Manual(string address, string prefix, string gateway, params string[] dns)
        {
            return new Ipv6ConfigRequest
            {
                Address = address,
                PrefixLength = prefix,
                Gateway = gateway,
                UseAutoDns = dns.Length == 0,
                DnsServers = dns
            };
        }

        private static List<IpField> Fields(Ipv6ConfigRequest request)
        {
            return request.Validate().Select(e => e.Field).ToList();
        }

        [Theory]
        [InlineData("2001:db8::10", "2001:db8::10")]
        [InlineData(" 2001:DB8:0:0::10 ", "2001:db8::10")]
        [InlineData("fe80::1%12", "fe80::1")]
        public void TryParse_NormalizesAndDropsZone(string text, string expected)
        {
            IPAddress address;
            Assert.True(Ipv6Validator.TryParse(text, out address));
            Assert.Equal(expected, address.ToString());
        }

        [Theory]
        [InlineData("192.168.1.1")]
        [InlineData("2001:db8::zz")]
        [InlineData("")]
        [InlineData("2001:db8::10/64")]
        public void TryParse_RejectsOthers(string text)
        {
            IPAddress address;
            Assert.False(Ipv6Validator.TryParse(text, out address));
        }

        [Fact]
        public void TryParseCidr_SplitsAddressAndPrefix()
        {
            IPAddress address;
            int prefix;
            Assert.True(Ipv6Validator.TryParseCidr("2001:db8:1::20/64", out address, out prefix));
            Assert.Equal("2001:db8:1::20", address.ToString());
            Assert.Equal(64, prefix);
            Assert.False(Ipv6Validator.TryParseCidr("2001:db8:1::20/129", out address, out prefix));
            Assert.False(Ipv6Validator.TryParseCidr("10.0.0.1/24", out address, out prefix));
        }

        [Fact]
        public void Network_MasksHostBits()
        {
            Assert.Equal("2001:db8:1::", Ipv6Validator.Network(IPAddress.Parse("2001:db8:1::20"), 64).ToString());
            Assert.Equal("2001:db8:100::", Ipv6Validator.Network(IPAddress.Parse("2001:db8:1ff::1"), 40).ToString());
            Assert.Equal("2001:db8:f000::", Ipv6Validator.Network(IPAddress.Parse("2001:db8:ffff::1"), 36).ToString());
        }

        [Fact]
        public void Validate_ManualWithLinkLocalGatewayAndDns_Passes()
        {
            Assert.Empty(Manual("2001:db8:1::20", "64", "fe80::1%12", "2400:3200::1", "2400:3200:baba::1").Validate());
            Assert.Empty(Manual("2001:db8:1::20", "64", "2001:db8:1::1").Validate());
            Assert.Empty(Manual("fd00::20", "64", "").Validate());
        }

        [Fact]
        public void Validate_RejectsReservedAddressesAndBadPrefix()
        {
            Assert.Equal(new[] { IpField.IpAddress }, Fields(Manual("fe80::20", "64", "")));
            Assert.Equal(new[] { IpField.IpAddress }, Fields(Manual("::1", "64", "")));
            Assert.Equal(new[] { IpField.IpAddress }, Fields(Manual("ff02::1", "64", "")));
            Assert.Equal(new[] { IpField.IpAddress }, Fields(Manual("192.168.1.10", "64", "")));
            Assert.Equal(new[] { IpField.SubnetMask }, Fields(Manual("2001:db8::10", "", "")));
            Assert.Equal(new[] { IpField.SubnetMask }, Fields(Manual("2001:db8::10", "0", "")));
            Assert.Equal(new[] { IpField.SubnetMask }, Fields(Manual("2001:db8::10", "129", "")));
        }

        [Fact]
        public void Validate_GatewayMustBeLinkLocalOrOnLink()
        {
            List<ValidationError> errors = Manual("2001:db8:1::20", "64", "2001:db8:2::1").Validate();
            Assert.Equal(IpField.Gateway, errors.Single().Field);
            Assert.Contains("2001:db8:1::/64", errors.Single().Message);

            Assert.Equal(new[] { IpField.Gateway }, Fields(Manual("2001:db8:1::20", "64", "2001:db8:1::20")));
            Assert.Equal(new[] { IpField.Gateway }, Fields(Manual("2001:db8:1::20", "64", "ff02::2")));
        }

        [Fact]
        public void Validate_Dns_RequiresIpv6()
        {
            List<ValidationError> errors = Manual("2001:db8::10", "64", "", "223.5.5.5").Validate();
            Assert.Equal(IpField.DnsMain, errors.Single().Field);
            Assert.Contains("IPv4", errors.Single().Message);

            var request = new Ipv6ConfigRequest { UseAuto = true, UseAutoDns = false, DnsServers = new string[0] };
            Assert.Equal(new[] { IpField.DnsMain }, Fields(request));

            request.DnsServers = new[] { "2400:3200::1", "nope" };
            Assert.Equal(new[] { IpField.DnsBackup }, Fields(request));
        }

        [Fact]
        public void Validate_AutoIgnoresAddressFields()
        {
            var request = new Ipv6ConfigRequest { UseAuto = true, UseAutoDns = true, Address = "garbage" };
            Assert.Empty(request.Validate());
        }

        [Fact]
        public void BuildCommands_ReplacesOldManualAddressAndGateway()
        {
            var old = new[] { new Ipv6ManualAddress("2001:db8:1::5", 64) };
            List<string> commands = Ipv6Backend.BuildCommands(12, old, new[] { "fe80::2" }, false,
                Manual("2001:db8:1::20", "64", "fe80::1", "2400:3200::1", "2400:3200:BABA::1"));

            Assert.Equal(new[]
            {
                "interface ipv6 delete address interface=12 address=2001:db8:1::5",
                "interface ipv6 delete route prefix=::/0 interface=12 nexthop=fe80::2",
                "interface ipv6 add address interface=12 address=2001:db8:1::20/64",
                "interface ipv6 add route prefix=::/0 interface=12 nexthop=fe80::1",
                "interface ipv6 set dnsservers name=12 source=static address=2400:3200::1 register=primary validate=no",
                "interface ipv6 add dnsservers name=12 address=2400:3200:baba::1 index=2 validate=no"
            }, commands);
        }

        [Fact]
        public void BuildCommands_KeepsWhatIsAlreadySet()
        {
            var old = new[] { new Ipv6ManualAddress("2001:db8:1::20", 64) };
            List<string> commands = Ipv6Backend.BuildCommands(12, old, new[] { "fe80::1" }, false,
                Manual("2001:DB8:1:0::20", "64", "fe80::1"));

            Assert.Empty(commands);
        }

        [Fact]
        public void BuildCommands_NewPrefixLengthReAddsTheAddress()
        {
            var old = new[] { new Ipv6ManualAddress("2001:db8:1::20", 64) };
            List<string> commands = Ipv6Backend.BuildCommands(12, old, new string[0], false, Manual("2001:db8:1::20", "56", ""));

            Assert.Equal(new[]
            {
                "interface ipv6 delete address interface=12 address=2001:db8:1::20",
                "interface ipv6 add address interface=12 address=2001:db8:1::20/56"
            }, commands);
        }

        [Fact]
        public void BuildCommands_AutoRemovesManualConfigAndRestoresDhcpDns()
        {
            var old = new[] { new Ipv6ManualAddress("2001:db8:1::20", 64), new Ipv6ManualAddress("fd00::20", 64) };
            var request = new Ipv6ConfigRequest { UseAuto = true, UseAutoDns = true };
            List<string> commands = Ipv6Backend.BuildCommands(7, old, new[] { "fe80::1" }, true, request);

            Assert.Equal(new[]
            {
                "interface ipv6 delete address interface=7 address=2001:db8:1::20",
                "interface ipv6 delete address interface=7 address=fd00::20",
                "interface ipv6 delete route prefix=::/0 interface=7 nexthop=fe80::1",
                "interface ipv6 set dnsservers name=7 source=dhcp"
            }, commands);

            // DNS 本来就是自动获取：不用再设
            Assert.DoesNotContain("interface ipv6 set dnsservers name=7 source=dhcp",
                Ipv6Backend.BuildCommands(7, old, new string[0], false, request));
        }

        [Fact]
        public void Adapter_ShowsManualIpv6Config()
        {
            var adapter = new NetworkAdapter
            {
                Status = AdapterStatus.Connected,
                Ipv6ManualAddresses = new List<Ipv6ManualAddress> { new Ipv6ManualAddress("2001:db8:1::20", 64) },
                Ipv6ManualGateways = new List<string> { "fe80::1" },
                ConfiguredIpv6Dns = new[] { "2400:3200::1" }
            };

            Assert.True(adapter.HasManualIpv6);
            Assert.Equal("2001:db8:1::20", adapter.Ipv6Address);
            Assert.Equal("64", adapter.Ipv6PrefixLength);
            Assert.Equal("fe80::1", adapter.Ipv6Gateway);
            Assert.Equal("2400:3200::1", adapter.Ipv6DnsMain);
            Assert.Equal("", adapter.Ipv6DnsBackup);

            var auto = new NetworkAdapter { Status = AdapterStatus.Disconnected };
            Assert.False(auto.HasManualIpv6);
            Assert.Equal("", auto.Ipv6Address);
            Assert.Equal("", auto.Ipv6Gateway);
        }

        [Fact]
        public void CheckIpv6Supported_ExplainsWhy()
        {
            var adapter = new NetworkAdapter { ConfigBackend = BackendKind.Wmi, InterfaceIndex = 5, Ipv6Enabled = true, Ipv6ConfigKnown = true };
            Assert.Null(AdapterService.CheckIpv6Supported(adapter));

            adapter.Ipv6Enabled = false;
            Assert.Contains("未启用 IPv6", AdapterService.CheckIpv6Supported(adapter));

            adapter.Ipv6Enabled = true;
            adapter.Ipv6ConfigKnown = false;
            Assert.Contains("Windows 8", AdapterService.CheckIpv6Supported(adapter));

            adapter.ConfigBackend = BackendKind.None;
            adapter.ReadOnlyReason = "网卡已禁用，启用后才能修改配置";
            Assert.Equal(adapter.ReadOnlyReason, AdapterService.CheckIpv6Supported(adapter));
        }
    }
}
