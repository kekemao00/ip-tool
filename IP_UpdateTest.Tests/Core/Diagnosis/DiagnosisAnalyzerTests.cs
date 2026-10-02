using System;
using System.Collections.Generic;
using System.Linq;
using IP_UpdateTest.Core.Diagnosis;
using Xunit;

namespace IP_UpdateTest.Tests.Core.Diagnosis
{
    public class DiagnosisAnalyzerTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 2, 12, 0, 0);

        private static HttpOutcome Ok(long ms = 30)
        {
            return new HttpOutcome { Success = true, Milliseconds = ms, Message = "正常" };
        }

        private static HttpOutcome Failed(string message = "HTTP 访问超时")
        {
            return new HttpOutcome { Success = false, Message = message };
        }

        private static PingStats Ping(string target, params long?[] roundtrips)
        {
            return PingStats.From(target, roundtrips);
        }

        /// <summary>
        /// 一切正常的家庭宽带：有线连接路由器，路由器后直接是运营商网络
        /// </summary>
        private static DiagnosisFacts Healthy()
        {
            var f = new DiagnosisFacts
            {
                AdapterFound = true,
                AdapterName = "以太网",
                AdapterConnected = true,
                LinkSpeedBps = 1000000000,
                Dhcp = true,
                IpAddress = "192.168.1.10",
                SubnetMask = "255.255.255.0",
                Gateway = "192.168.1.1",
                DnsServers = new[] { "192.168.1.1" },
                RouteAdapterName = "以太网",
                GatewayPing = Ping("192.168.1.1", 1, 1, 1, 1),
                GatewayMac = "AA-BB-CC-DD-EE-FF",
                PublicPing = Ping("223.5.5.5", 8, 9, 8, 8),
                PublicTcpOk = true,
                HttpDirect = Ok(),
                HttpsDomestic = Ok(),
                HttpsForeign = Failed(),
                PathMtu = 1492,
                SystemResolveOk = true,
                SystemResolveAddress = "110.242.68.66",
                SystemResolveMs = 12,
                PublicDnsUdpOk = true,
                StunServersTried = 2
            };
            f.Hops.AddRange(new[] { "192.168.1.1", "61.135.1.1", "61.135.2.1" });
            f.DnsServerResults.Add(new KeyValuePair<string, long?>("192.168.1.1", 5));
            f.StunMapped.AddRange(new[] { "61.135.10.20:40000", "61.135.10.20:40000" });
            f.LocalAddresses.Add("192.168.1.10");
            return f;
        }

        private static DiagnosisReport Analyze(DiagnosisFacts f)
        {
            return DiagnosisAnalyzer.Analyze(f, Now);
        }

        private static DiagnosisCheck Check(DiagnosisReport report, string title, DiagnosisLayer? layer = null)
        {
            return report.Checks.Single(c => c.Title == title && (layer == null || c.Layer == layer));
        }

        [Fact]
        public void Healthy_NoProblem()
        {
            DiagnosisReport report = Analyze(Healthy());

            Assert.Null(report.FaultLayer);
            Assert.DoesNotContain(report.Checks, c => c.Status == CheckStatus.Warning || c.Status == CheckStatus.Fail);
            Assert.Equal("网络正常，未发现问题", report.Summary);
            Assert.Equal(CheckStatus.Ok, Check(report, "代理 / VPN").Status);
            Assert.Contains("锥形", Check(report, "NAT 类型").Detail);
        }

        [Fact]
        public void AdapterDisconnected_FaultAtAdapter()
        {
            DiagnosisFacts f = Healthy();
            f.AdapterConnected = false;
            f.PublicPing = Ping("223.5.5.5", null, null);
            f.PublicTcpOk = false;
            f.HttpDirect = Failed();
            f.HttpsDomestic = Failed();

            DiagnosisReport report = Analyze(f);

            Assert.Equal(DiagnosisLayer.Adapter, report.FaultLayer);
            Assert.Contains("未连接", report.Summary);
            Assert.Contains(report.KeySuggestions, s => s.Contains("网线"));
        }

        [Fact]
        public void SelectedAdapterDisconnected_ButOtherAdapterOnline_IsOnlyWarning()
        {
            DiagnosisFacts f = Healthy();
            f.AdapterConnected = false;
            f.RouteViaAdapter = false;
            f.RouteAdapterName = "WLAN";

            DiagnosisReport report = Analyze(f);

            Assert.Null(report.FaultLayer);
            Assert.Contains("WLAN", Check(report, "连接状态").Detail);
            Assert.Equal(CheckStatus.Warning, Check(report, "连接状态").Status);
        }

        [Fact]
        public void Apipa_DhcpFailed()
        {
            DiagnosisFacts f = Healthy();
            f.IpAddress = "169.254.12.34";
            f.SubnetMask = "255.255.0.0";
            f.Gateway = "";
            f.GatewayPing = null;
            f.PublicPing = Ping("223.5.5.5", null, null);
            f.PublicTcpOk = false;
            f.HttpDirect = Failed();
            f.HttpsDomestic = Failed();

            DiagnosisReport report = Analyze(f);

            Assert.Equal(DiagnosisLayer.Adapter, report.FaultLayer);
            Assert.Contains("169.254", report.Summary);
            Assert.Contains(report.KeySuggestions, s => s.Contains("续订 DHCP"));
        }

        [Fact]
        public void GatewayInOtherSubnet_Fails()
        {
            DiagnosisFacts f = Healthy();
            f.Dhcp = false;
            f.Gateway = "192.168.0.1";

            DiagnosisCheck check = Check(Analyze(f), "默认网关");

            Assert.Equal(CheckStatus.Fail, check.Status);
            Assert.Contains("不在同一网段", check.Detail);
        }

        [Fact]
        public void RouterUnreachable_FaultAtGateway()
        {
            DiagnosisFacts f = Healthy();
            f.GatewayPing = Ping("192.168.1.1", null, null, null, null);
            f.GatewayMac = null;
            f.PublicPing = Ping("223.5.5.5", null, null);
            f.PublicTcpOk = false;
            f.HttpDirect = Failed();
            f.HttpsDomestic = Failed();
            f.DnsServerResults[0] = new KeyValuePair<string, long?>("192.168.1.1", null);

            DiagnosisReport report = Analyze(f);

            Assert.Equal(DiagnosisLayer.Gateway, report.FaultLayer);
            Assert.Contains("无法连接路由器", report.Summary);
            Assert.Contains(report.KeySuggestions, s => s.Contains("重启路由器"));
        }

        [Fact]
        public void RouterIgnoresPing_ButInternetWorks_IsFine()
        {
            DiagnosisFacts f = Healthy();
            f.GatewayPing = Ping("192.168.1.1", null, null, null, null);
            f.GatewayMac = null;

            DiagnosisReport report = Analyze(f);

            Assert.Null(report.FaultLayer);
            Assert.Equal(CheckStatus.Info, Check(report, "连通性", DiagnosisLayer.Gateway).Status);
        }

        [Fact]
        public void WifiPacketLossToRouter_Warns()
        {
            DiagnosisFacts f = Healthy();
            f.IsWireless = true;
            f.GatewayPing = Ping("192.168.1.1", 3, null, 80, 5);

            DiagnosisCheck check = Check(Analyze(f), "丢包");

            Assert.Equal(CheckStatus.Warning, check.Status);
            Assert.Contains("25%", check.Detail);
            Assert.Contains(check.Suggestions, s => s.Contains("5GHz"));
        }

        [Fact]
        public void PrivateSecondHop_DoubleNat()
        {
            DiagnosisFacts f = Healthy();
            f.Hops[1] = "192.168.0.1";

            DiagnosisCheck check = Check(Analyze(f), "双重 NAT");

            Assert.Equal(CheckStatus.Warning, check.Status);
            Assert.Contains(check.Suggestions, s => s.Contains("桥接"));
        }

        [Fact]
        public void CgnatHop_Detected()
        {
            DiagnosisFacts f = Healthy();
            f.Hops[2] = "100.72.0.1";

            DiagnosisCheck check = Check(Analyze(f), "运营商 NAT");

            Assert.Contains("没有公网 IPv4", check.Detail);
        }

        [Fact]
        public void DoubleNatBehindCgnat_BothReported()
        {
            DiagnosisFacts f = Healthy();
            f.Hops[1] = "192.168.1.1";
            f.Hops[2] = "100.66.0.1";

            DiagnosisReport report = Analyze(f);

            Assert.Equal(CheckStatus.Warning, Check(report, "双重 NAT").Status);
            Assert.Contains("第 3 跳", Check(report, "运营商 NAT").Detail);
        }

        [Fact]
        public void InternetDownBehindWorkingRouter_FaultAtInternet()
        {
            DiagnosisFacts f = Healthy();
            f.PublicPing = Ping("223.5.5.5", null, null, null, null);
            f.PublicTcpOk = false;
            f.HttpDirect = Failed();
            f.HttpsDomestic = Failed();
            f.DnsServerResults[0] = new KeyValuePair<string, long?>("192.168.1.1", null);
            f.SystemResolveOk = false;
            f.SystemResolveError = "不知道这样的主机";
            f.StunMapped.Clear();
            f.PublicDnsUdpOk = false;

            DiagnosisReport report = Analyze(f);

            Assert.Equal(DiagnosisLayer.Internet, report.FaultLayer);
            Assert.Contains(report.KeySuggestions, s => s.Contains("光猫"));
            Assert.Equal(CheckStatus.Skipped, report.LayerStatus(DiagnosisLayer.Udp));
        }

        [Fact]
        public void CaptivePortal()
        {
            DiagnosisFacts f = Healthy();
            f.HttpDirect = new HttpOutcome { NeedsLogin = true, Message = "返回内容异常" };

            DiagnosisReport report = Analyze(f);

            Assert.Equal(DiagnosisLayer.Internet, report.FaultLayer);
            Assert.Contains("网页登录", report.Summary);
        }

        [Fact]
        public void PingBlockedButTcpWorks_IsInfo()
        {
            DiagnosisFacts f = Healthy();
            f.PublicPing = Ping("223.5.5.5", null, null, null, null);
            f.PathMtu = null;

            DiagnosisReport report = Analyze(f);

            Assert.Null(report.FaultLayer);
            Assert.Equal(CheckStatus.Info, Check(report, "ping").Status);
        }

        [Fact]
        public void SmallMtu_SuggestsNetsh()
        {
            DiagnosisFacts f = Healthy();
            f.PathMtu = 1360;

            DiagnosisCheck check = Check(Analyze(f), "MTU");

            Assert.Equal(CheckStatus.Warning, check.Status);
            Assert.Contains(check.Suggestions, s => s.Contains("mtu=1360") && s.Contains("以太网"));
        }

        [Fact]
        public void HttpsCertificateError_Warns()
        {
            DiagnosisFacts f = Healthy();
            f.HttpsDomestic = new HttpOutcome { CertificateError = true, Message = "未能为 SSL/TLS 安全通道建立信任关系" };

            DiagnosisCheck check = Check(Analyze(f), "HTTPS");

            Assert.Equal(CheckStatus.Warning, check.Status);
            Assert.Contains(check.Suggestions, s => s.Contains("日期和时间"));
        }

        [Fact]
        public void AllDnsServersDown_FaultAtDns()
        {
            DiagnosisFacts f = Healthy();
            f.DnsServerResults[0] = new KeyValuePair<string, long?>("192.168.1.1", null);
            f.SystemResolveOk = false;
            f.SystemResolveError = "不知道这样的主机";

            DiagnosisReport report = Analyze(f);

            Assert.Equal(DiagnosisLayer.Dns, report.FaultLayer);
            Assert.Contains(report.KeySuggestions, s => s.Contains("路由器的 DNS 转发"));
            Assert.Contains(report.KeySuggestions, s => s.Contains("flushdns"));
        }

        [Fact]
        public void OneDnsServerDown_IsWarning()
        {
            DiagnosisFacts f = Healthy();
            f.DnsServerResults.Add(new KeyValuePair<string, long?>("8.8.8.8", null));

            DiagnosisReport report = Analyze(f);

            Assert.Null(report.FaultLayer);
            Assert.Equal(CheckStatus.Warning, Check(report, "DNS 8.8.8.8").Status);
        }

        [Fact]
        public void NxDomainResolved_DnsHijack()
        {
            DiagnosisFacts f = Healthy();
            f.NxDomainAnswer = "220.181.1.1";

            Assert.Equal(CheckStatus.Warning, Check(Analyze(f), "DNS 劫持").Status);
        }

        [Fact]
        public void StaleSystemProxy_FaultAtProxy()
        {
            DiagnosisFacts f = Healthy();
            f.SystemProxyEnabled = true;
            f.Proxies.Add(new ProxyFact { Source = "系统代理", Value = "127.0.0.1:7890", Endpoint = "127.0.0.1:7890", Reachable = false });
            f.HttpViaProxy = Failed();
            f.HttpsDomestic = Failed("无法连接到远程服务器");

            DiagnosisReport report = Analyze(f);

            Assert.Equal(DiagnosisLayer.ProxyVpn, report.FaultLayer);
            Assert.Contains("127.0.0.1:7890 无法连接", report.Summary);
            Assert.Contains(report.KeySuggestions, s => s.Contains("使用代理服务器"));
            // 直连正常时 HTTPS 失败归到代理环节，不在外网环节报警；国际网站打不开是连带结果，不再提示
            Assert.Equal(CheckStatus.Info, Check(report, "HTTPS").Status);
            Assert.DoesNotContain(report.Checks, c => c.Title == "国际网站");
        }

        [Fact]
        public void ProxyReachableButBroken_FaultAtProxy()
        {
            DiagnosisFacts f = Healthy();
            f.SystemProxyEnabled = true;
            f.Proxies.Add(new ProxyFact { Source = "系统代理", Value = "127.0.0.1:7890", Endpoint = "127.0.0.1:7890", Reachable = true });
            f.HttpViaProxy = Failed();

            DiagnosisReport report = Analyze(f);

            Assert.Equal(DiagnosisLayer.ProxyVpn, report.FaultLayer);
            Assert.Contains(report.KeySuggestions, s => s.Contains("切换节点"));
        }

        [Fact]
        public void OnlyReachableViaProxy_IsNotAFault()
        {
            DiagnosisFacts f = Healthy();
            f.SystemProxyEnabled = true;
            f.Proxies.Add(new ProxyFact { Source = "系统代理", Value = "proxy.corp:8080", Endpoint = "proxy.corp:8080", Reachable = true });
            f.HttpViaProxy = Ok();
            f.PublicPing = Ping("223.5.5.5", null, null);
            f.PublicTcpOk = false;
            f.HttpDirect = Failed();
            f.StunMapped.Clear();
            f.PublicDnsUdpOk = false;

            DiagnosisReport report = Analyze(f);

            Assert.Null(report.FaultLayer);
            Assert.Equal(CheckStatus.Info, Check(report, "直连").Status);
            Assert.Equal(CheckStatus.Warning, Check(report, "UDP").Status);
        }

        [Fact]
        public void StaleWinHttpProxy_Warns()
        {
            DiagnosisFacts f = Healthy();
            f.Proxies.Add(new ProxyFact { Source = "WinHTTP 代理", Value = "127.0.0.1:10809", Endpoint = "127.0.0.1:10809", Reachable = false });

            DiagnosisCheck check = Check(Analyze(f), "WinHTTP 代理");

            Assert.Equal(CheckStatus.Warning, check.Status);
            Assert.Contains(check.Suggestions, s => s.Contains("netsh winhttp reset proxy"));
        }

        [Fact]
        public void FakeIpDnsWithoutTun_Fails()
        {
            DiagnosisFacts f = Healthy();
            f.DnsServerResults[0] = new KeyValuePair<string, long?>("198.18.0.2", null);
            f.SystemResolveOk = false;
            f.SystemResolveError = "不知道这样的主机";

            DiagnosisReport report = Analyze(f);

            Assert.Equal(CheckStatus.Fail, Check(report, "Fake-IP").Status);
            Assert.Contains(report.Checks.Single(c => c.Title == "DNS 198.18.0.2").Suggestions, s => s.Contains("代理软件的虚拟 DNS"));
        }

        [Fact]
        public void TunModeActive_IsInfo()
        {
            DiagnosisFacts f = Healthy();
            f.VpnAdapters.Add("Mihomo（Meta Tunnel）");
            f.RouteViaVpn = true;
            f.SystemResolveAddress = "198.18.0.5";
            f.HttpsForeign = Ok(200);
            f.ProxyPrograms.Add("Clash Verge");

            DiagnosisReport report = Analyze(f);

            Assert.Null(report.FaultLayer);
            Assert.Equal(CheckStatus.Info, Check(report, "Fake-IP").Status);
            Assert.Equal(CheckStatus.Ok, Check(report, "国际网站").Status);
            Assert.DoesNotContain(report.Checks, c => c.Title == "代理 / VPN");
        }

        [Fact]
        public void VpnOnButForeignSiteFails_Warns()
        {
            DiagnosisFacts f = Healthy();
            f.VpnAdapters.Add("WireGuard");
            f.RouteViaVpn = true;

            Assert.Equal(CheckStatus.Warning, Check(Analyze(f), "国际网站").Status);
        }

        [Fact]
        public void VpnDownAndNoInternet_FaultMentionsVpn()
        {
            DiagnosisFacts f = Healthy();
            f.VpnAdapters.Add("WireGuard");
            f.RouteViaVpn = true;
            f.PublicPing = Ping("223.5.5.5", null, null);
            f.PublicTcpOk = false;
            f.HttpDirect = Failed();
            f.HttpsDomestic = Failed();

            DiagnosisReport report = Analyze(f);

            Assert.Equal(DiagnosisLayer.Internet, report.FaultLayer);
            Assert.Contains(report.KeySuggestions, s => s.Contains("断开 VPN"));
        }

        [Fact]
        public void UdpBlocked_FaultAtUdp()
        {
            DiagnosisFacts f = Healthy();
            f.StunMapped.Clear();
            f.PublicDnsUdpOk = false;

            DiagnosisReport report = Analyze(f);

            Assert.Equal(DiagnosisLayer.Udp, report.FaultLayer);
            Assert.Contains(report.KeySuggestions, s => s.Contains("QUIC"));
        }

        [Fact]
        public void OnlyDnsUdpAllowed_Warns()
        {
            DiagnosisFacts f = Healthy();
            f.StunMapped.Clear();

            DiagnosisCheck check = Check(Analyze(f), "UDP");

            Assert.Equal(CheckStatus.Warning, check.Status);
            Assert.Contains("只有 UDP 53", check.Detail);
        }

        [Fact]
        public void SymmetricNat_Warns()
        {
            DiagnosisFacts f = Healthy();
            f.StunMapped[1] = "61.135.10.20:40007";

            DiagnosisCheck check = Check(Analyze(f), "NAT 类型");

            Assert.Equal(CheckStatus.Warning, check.Status);
            Assert.Contains("对称型", check.Detail);
            Assert.Contains(check.Suggestions, s => s.Contains("UPnP"));
        }

        [Fact]
        public void PublicIpOnAdapter_NoNat()
        {
            DiagnosisFacts f = Healthy();
            f.LocalAddresses.Add("61.135.10.20");

            Assert.Contains("没有 NAT", Check(Analyze(f), "NAT").Detail);
        }

        [Fact]
        public void MultipleGateways_Warns()
        {
            DiagnosisFacts f = Healthy();
            f.OtherGatewayAdapters.Add("WLAN");

            Assert.Equal(CheckStatus.Warning, Check(Analyze(f), "多个默认网关").Status);
        }

        [Fact]
        public void WeakWifi_Warns()
        {
            DiagnosisFacts f = Healthy();
            f.IsWireless = true;
            f.WifiSignalPercent = 25;

            Assert.Equal(CheckStatus.Warning, Check(Analyze(f), "Wi-Fi 信号").Status);
        }

        [Fact]
        public void FirstFailingLayerWins()
        {
            // 路由器和 UDP 都失败时，结论指向更靠前的路由器
            DiagnosisFacts f = Healthy();
            f.GatewayPing = Ping("192.168.1.1", null, null);
            f.GatewayMac = null;
            f.PublicPing = Ping("223.5.5.5", null, null);
            f.PublicTcpOk = false;
            f.HttpDirect = Failed();
            f.HttpsDomestic = Failed();
            f.StunMapped.Clear();
            f.PublicDnsUdpOk = false;

            Assert.Equal(DiagnosisLayer.Gateway, Analyze(f).FaultLayer);
        }

        [Fact]
        public void AddressClassification()
        {
            Assert.True(DiagnosisAnalyzer.IsFakeIp("198.18.0.1"));
            Assert.True(DiagnosisAnalyzer.IsFakeIp("198.19.255.1"));
            Assert.False(DiagnosisAnalyzer.IsFakeIp("198.20.0.1"));
            Assert.True(DiagnosisAnalyzer.IsCgnat(System.Net.IPAddress.Parse("100.64.0.1")));
            Assert.True(DiagnosisAnalyzer.IsCgnat(System.Net.IPAddress.Parse("100.127.255.1")));
            Assert.False(DiagnosisAnalyzer.IsCgnat(System.Net.IPAddress.Parse("100.128.0.1")));
            Assert.True(DiagnosisAnalyzer.IsPrivateAddress("172.20.1.1"));
            Assert.False(DiagnosisAnalyzer.IsPrivateAddress("172.32.1.1"));
        }
    }
}
