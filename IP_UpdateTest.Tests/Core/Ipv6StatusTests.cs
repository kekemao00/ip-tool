using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using IP_UpdateTest.Core;
using Xunit;

namespace IP_UpdateTest.Tests.Core
{
    public class Ipv6StatusTests
    {
        private const string Global = "2408:8207:24a1:5c00:a32c:7c34:a627:f521";
        private const string LinkLocal = "fe80::a32c:7c34:a627:f521";

        private static Ipv6AddressInfo A(string address, Ipv6AddressKind kind,
            DuplicateAddressDetectionState state = DuplicateAddressDetectionState.Preferred)
        {
            return new Ipv6AddressInfo(address, 64, kind, state);
        }

        private static NetworkAdapter Connected(params Ipv6AddressInfo[] addresses)
        {
            return new NetworkAdapter
            {
                Status = AdapterStatus.Connected,
                Ipv6Enabled = true,
                Ipv6LiveAddresses = addresses.ToList()
            };
        }

        [Theory]
        [InlineData("fe80::1", PrefixOrigin.WellKnown, SuffixOrigin.LinkLayerAddress, Ipv6AddressKind.LinkLocal)]
        [InlineData("2001:db8::10", PrefixOrigin.Manual, SuffixOrigin.Manual, Ipv6AddressKind.Manual)]
        [InlineData(Global, PrefixOrigin.RouterAdvertisement, SuffixOrigin.LinkLayerAddress, Ipv6AddressKind.Global)]
        [InlineData(Global, PrefixOrigin.RouterAdvertisement, SuffixOrigin.Random, Ipv6AddressKind.Temporary)]
        [InlineData("2408:8207:24a1:5c00::1234", PrefixOrigin.Dhcp, SuffixOrigin.OriginDhcp, Ipv6AddressKind.Dhcp)]
        [InlineData("fd12:3456:789a::10", PrefixOrigin.RouterAdvertisement, SuffixOrigin.LinkLayerAddress, Ipv6AddressKind.UniqueLocal)]
        public void Classify_MatchesIpconfig(string address, PrefixOrigin prefix, SuffixOrigin suffix, Ipv6AddressKind expected)
        {
            Assert.Equal(expected, Ipv6Status.Classify(IPAddress.Parse(address), prefix, suffix));
        }

        [Fact]
        public void ForDisplay_OrdersByKindAndCollapsesTemporaries()
        {
            int otherTemporary;
            List<Ipv6AddressInfo> list = Ipv6Status.ForDisplay(new[]
            {
                A(LinkLocal, Ipv6AddressKind.LinkLocal),
                A("2408::aaaa", Ipv6AddressKind.Temporary),
                A("2408::dead", Ipv6AddressKind.Temporary, DuplicateAddressDetectionState.Deprecated),
                A("2408::bbbb", Ipv6AddressKind.Temporary),
                A("fd00::10", Ipv6AddressKind.UniqueLocal),
                A(Global, Ipv6AddressKind.Global)
            }, out otherTemporary);

            Assert.Equal(new[] { Global, "fd00::10", "2408::aaaa", LinkLocal }, list.Select(a => a.Address));
            // 即将停用的不算
            Assert.Equal(1, otherTemporary);
        }

        [Fact]
        public void Describe_OnlyLinkLocal_ExplainsInsteadOfBlank()
        {
            Ipv6StatusSummary status = Ipv6Status.Describe(Connected(A(LinkLocal, Ipv6AddressKind.LinkLocal)), 4);

            Assert.Equal("本地链接", status.Rows.Single().Caption);
            Assert.Equal(LinkLocal, status.Rows.Single().Value);
            Assert.Equal(Ipv6Status.NoIpv6Notice, status.Notice);
        }

        [Fact]
        public void Describe_NormalNetwork_ListsAddressesThenGateway()
        {
            NetworkAdapter adapter = Connected(
                A(LinkLocal, Ipv6AddressKind.LinkLocal),
                A("2408::aaaa", Ipv6AddressKind.Temporary),
                A("2408::bbbb", Ipv6AddressKind.Temporary),
                A(Global, Ipv6AddressKind.Global));
            adapter.Ipv6LiveGateways = new List<string> { "fe80::1" };

            Ipv6StatusSummary status = Ipv6Status.Describe(adapter, 4);

            Assert.Equal(new[] { "公网地址", "临时地址", "本地链接", "默认网关" }, status.Rows.Select(r => r.Caption));
            Assert.Equal(Global, status.Rows[0].Value);
            Assert.Contains("/64", status.Rows[0].Detail);
            Assert.Contains("另有 1 个临时地址", status.Rows[1].Detail);
            Assert.Equal("fe80::1", status.Rows[3].Value);
            Assert.Null(status.Notice);
        }

        [Fact]
        public void Describe_TooManyAddresses_KeepsGatewayRow()
        {
            NetworkAdapter adapter = Connected(
                A(Global, Ipv6AddressKind.Global),
                A("fd00::10", Ipv6AddressKind.UniqueLocal),
                A("2408::aaaa", Ipv6AddressKind.Temporary),
                A(LinkLocal, Ipv6AddressKind.LinkLocal));

            Ipv6StatusSummary status = Ipv6Status.Describe(adapter, 4);

            // 放不下时先舍掉本地链接；没有网关也要说出来
            Assert.Equal(new[] { "公网地址", "内网地址", "临时地址", "默认网关" }, status.Rows.Select(r => r.Caption));
            Assert.Equal("无", status.Rows[3].Value);
        }

        [Fact]
        public void Describe_DuplicateAddressIsNotUsable()
        {
            Ipv6StatusSummary status = Ipv6Status.Describe(Connected(
                A("2001:db8::10", Ipv6AddressKind.Manual, DuplicateAddressDetectionState.Duplicate),
                A(LinkLocal, Ipv6AddressKind.LinkLocal)), 4);

            Assert.Equal("地址冲突", status.Rows[0].Caption);
            Assert.Equal(Ipv6Status.NoIpv6Notice, status.Notice);
        }

        [Fact]
        public void Describe_ExplainsWhyThereIsNothingToShow()
        {
            Assert.Equal(Ipv6Status.DisabledNotice, Ipv6Status.Describe(new NetworkAdapter { Status = AdapterStatus.Disabled }, 4).Notice);
            Assert.Equal(Ipv6Status.NotEnabledNotice, Ipv6Status.Describe(new NetworkAdapter { Status = AdapterStatus.Connected }, 4).Notice);

            Ipv6StatusSummary disconnected = Ipv6Status.Describe(new NetworkAdapter { Status = AdapterStatus.Disconnected, Ipv6Enabled = true }, 4);
            Assert.Empty(disconnected.Rows);
            Assert.Equal(Ipv6Status.DisconnectedNotice, disconnected.Notice);

            Assert.Null(Ipv6Status.Describe(null, 4).Notice);
        }

        [Fact]
        public void WaitForDad_WaitsWhileTentative()
        {
            var states = new Queue<DuplicateAddressDetectionState?>(new DuplicateAddressDetectionState?[]
            {
                null, DuplicateAddressDetectionState.Tentative, DuplicateAddressDetectionState.Duplicate
            });
            int waits = 0;

            Assert.Equal(DuplicateAddressDetectionState.Duplicate, Ipv6Backend.WaitForDad(() => states.Dequeue(), 5, () => waits++));
            Assert.Equal(2, waits);
        }

        [Fact]
        public void WaitForDad_GivesUpWithoutResult()
        {
            int reads = 0, waits = 0;
            DuplicateAddressDetectionState? state = Ipv6Backend.WaitForDad(() =>
            {
                reads++;
                return DuplicateAddressDetectionState.Tentative;
            }, 3, () => waits++);

            Assert.Null(state);
            Assert.Equal(3, reads);
            Assert.Equal(2, waits);
        }
    }
}
