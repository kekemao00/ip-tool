using System.Linq;
using System.Net;
using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class IpValidatorTests
    {
        [Theory]
        [InlineData("192.168.1.10")]
        [InlineData("0.0.0.0")]
        [InlineData("255.255.255.255")]
        [InlineData(" 10.0.0.1 ")]
        public void TryParseIPv4_Valid(string text)
        {
            IPAddress address;
            Assert.True(IpValidator.TryParseIPv4(text, out address));
            Assert.Equal(text.Trim(), address.ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("192.168.1")]
        [InlineData("192.168.1.1.1")]
        [InlineData("192.168.1.256")]
        [InlineData("192.168.01.1")] // 前导 0 会被 IPAddress.TryParse 当八进制
        [InlineData("010.0.0.1")]
        [InlineData("192.168.1.a")]
        [InlineData("192.168..1")]
        [InlineData("-1.0.0.1")]
        [InlineData("fe80::1")]
        public void TryParseIPv4_Invalid(string text)
        {
            IPAddress address;
            Assert.False(IpValidator.TryParseIPv4(text, out address));
            Assert.Null(address);
        }

        [Fact]
        public void TryParseCidr_SplitsAddressAndPrefix()
        {
            IPAddress address;
            int prefix;

            Assert.True(IpValidator.TryParseCidr("192.168.1.10/24", out address, out prefix));
            Assert.Equal("192.168.1.10", address.ToString());
            Assert.Equal(24, prefix);
        }

        [Theory]
        [InlineData("192.168.1.10")]
        [InlineData("192.168.1.10/0")]
        [InlineData("192.168.1.10/33")]
        [InlineData("192.168.1/24")]
        [InlineData("192.168.1.10/x")]
        public void TryParseCidr_Invalid(string text)
        {
            IPAddress address;
            int prefix;
            Assert.False(IpValidator.TryParseCidr(text, out address, out prefix));
        }

        [Theory]
        [InlineData("255.255.255.0", 24)]
        [InlineData("255.255.240.0", 20)]
        [InlineData("255.255.255.255", 32)]
        [InlineData("128.0.0.0", 1)]
        [InlineData("0.0.0.0", 0)]
        [InlineData("255.0.255.0", -1)]
        [InlineData("255.255.255.1", -1)]
        public void MaskToPrefix(string mask, int expected)
        {
            Assert.Equal(expected, IpValidator.MaskToPrefix(IPAddress.Parse(mask)));
        }

        [Theory]
        [InlineData(24, "255.255.255.0")]
        [InlineData(20, "255.255.240.0")]
        [InlineData(32, "255.255.255.255")]
        [InlineData(0, "0.0.0.0")]
        public void PrefixToMask(int prefix, string expected)
        {
            Assert.Equal(expected, IpValidator.PrefixToMask(prefix).ToString());
        }

        [Fact]
        public void IsSameSubnet()
        {
            IPAddress mask = IPAddress.Parse("255.255.255.0");
            Assert.True(IpValidator.IsSameSubnet(IPAddress.Parse("192.168.1.10"), IPAddress.Parse("192.168.1.1"), mask));
            Assert.False(IpValidator.IsSameSubnet(IPAddress.Parse("192.168.1.10"), IPAddress.Parse("192.168.2.1"), mask));
        }

        [Fact]
        public void ValidateStatic_ValidConfig_NoErrors()
        {
            Assert.Empty(IpValidator.ValidateStatic("192.168.1.10", "255.255.255.0", "192.168.1.1", "223.5.5.5", "119.29.29.29"));
        }

        [Fact]
        public void ValidateStatic_GatewayAndDnsOptional()
        {
            Assert.Empty(IpValidator.ValidateStatic("10.254.254.10", "255.255.255.0", "", "", ""));
        }

        [Theory]
        [InlineData("", "255.255.255.0", "", IpField.IpAddress)]
        [InlineData("192.168.1.10", "", "", IpField.SubnetMask)]
        [InlineData("192.168.1.10", "255.0.255.0", "", IpField.SubnetMask)]
        [InlineData("192.168.1.0", "255.255.255.0", "", IpField.IpAddress)]   // 网段地址
        [InlineData("192.168.1.255", "255.255.255.0", "", IpField.IpAddress)] // 广播地址
        [InlineData("127.0.0.1", "255.0.0.0", "", IpField.IpAddress)]
        [InlineData("224.0.0.5", "255.255.255.0", "", IpField.IpAddress)]
        [InlineData("192.168.1.10", "255.255.255.0", "192.168.2.1", IpField.Gateway)]
        [InlineData("192.168.1.10", "255.255.255.0", "192.168.1.10", IpField.Gateway)]
        [InlineData("192.168.1.10", "255.255.255.0", "192.168.1", IpField.Gateway)]
        public void ValidateStatic_ReportsFieldErrors(string ip, string mask, string gateway, IpField field)
        {
            var errors = IpValidator.ValidateStatic(ip, mask, gateway, "", "");

            Assert.Contains(errors, e => e.Field == field);
        }

        [Fact]
        public void ValidateStatic_Slash31_AllowsAnyHost()
        {
            Assert.Empty(IpValidator.ValidateStatic("10.0.0.0", "255.255.255.254", "", "", ""));
        }

        [Fact]
        public void ValidateStatic_GatewayErrorShowsSubnet()
        {
            var error = IpValidator.ValidateStatic("192.168.1.10", "255.255.255.0", "10.0.0.1", "", "").Single();

            Assert.Contains("192.168.1.0/24", error.Message);
        }

        [Fact]
        public void ValidateDns_BackupWithoutMain_IsError()
        {
            var errors = IpValidator.ValidateDns("", "223.5.5.5", false);

            Assert.Equal(IpField.DnsMain, errors.Single().Field);
        }

        [Fact]
        public void ValidateDns_RequireMain()
        {
            Assert.Equal(IpField.DnsMain, IpValidator.ValidateDns("", "", true).Single().Field);
            Assert.Empty(IpValidator.ValidateDns("", "", false));
        }

        [Fact]
        public void ValidateDns_InvalidBackup()
        {
            Assert.Equal(IpField.DnsBackup, IpValidator.ValidateDns("223.5.5.5", "abc", false).Single().Field);
        }
    }
}
