using System.Collections.Generic;
using System.Linq;
using System.Net;
using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class Ipv4SelectorTests
    {
        private static List<IPAddress> Parse(params string[] addresses)
        {
            return addresses.Select(IPAddress.Parse).ToList();
        }

        [Fact]
        public void FirstIPv4_LinkLocalFirst_ReturnsIPv4()
        {
            var addresses = Parse("fe80::ced1:86dc:dc98:b6bc", "172.18.176.1");

            Assert.Equal(IPAddress.Parse("172.18.176.1"), Ipv4Selector.FirstIPv4(addresses, a => a));
        }

        [Fact]
        public void FirstIPv4_FourAddressesDualStack_ReturnsIPv4()
        {
            // 旧逻辑在 4 个地址时取 [0]，会拿到 IPv6
            var addresses = Parse("240e:3b7::10", "240e:3b7::abcd", "fe80::1", "192.168.1.10");

            Assert.Equal(IPAddress.Parse("192.168.1.10"), Ipv4Selector.FirstIPv4(addresses, a => a));
        }

        [Fact]
        public void FirstIPv4_SkipsUnspecifiedAddress()
        {
            var gateways = Parse("0.0.0.0", "192.168.3.1");

            Assert.Equal(IPAddress.Parse("192.168.3.1"), Ipv4Selector.FirstIPv4(gateways, a => a));
        }

        [Fact]
        public void FirstIPv4_OnlyIPv6_ReturnsNull()
        {
            Assert.Null(Ipv4Selector.FirstIPv4(Parse("fe80::1", "::1"), a => a));
        }

        [Fact]
        public void FirstIPv4_NullCollection_ReturnsNull()
        {
            Assert.Null(Ipv4Selector.FirstIPv4<IPAddress>(null, a => a));
        }

        [Fact]
        public void IPv4Only_KeepsOrderAndDropsIPv6()
        {
            var dns = Parse("fec0:0:0:ffff::1", "223.5.5.5", "fec0:0:0:ffff::2", "119.29.29.29");

            Assert.Equal(Parse("223.5.5.5", "119.29.29.29"), Ipv4Selector.IPv4Only(dns));
        }

        [Fact]
        public void IPv4Only_Null_ReturnsEmpty()
        {
            Assert.Empty(Ipv4Selector.IPv4Only(null));
        }
    }
}
