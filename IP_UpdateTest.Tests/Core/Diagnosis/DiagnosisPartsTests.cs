using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using IP_UpdateTest.Core.Diagnosis;
using Xunit;

namespace IP_UpdateTest.Tests.Core.Diagnosis
{
    public class ProxySettingsTests
    {
        [Fact]
        public void ParseServerList_Single()
        {
            Assert.Equal(new[] { "127.0.0.1:7890" }, ProxySettings.ParseServerList("127.0.0.1:7890"));
        }

        [Fact]
        public void ParseServerList_PerProtocol_Deduplicated()
        {
            Assert.Equal(new[] { "127.0.0.1:7890", "127.0.0.1:7891" },
                ProxySettings.ParseServerList("http=127.0.0.1:7890;https=127.0.0.1:7890;socks=127.0.0.1:7891"));
        }

        [Fact]
        public void ParseServerList_DefaultPort80()
        {
            Assert.Equal(new[] { "proxy.corp:80" }, ProxySettings.ParseServerList("proxy.corp"));
            Assert.Empty(ProxySettings.ParseServerList(""));
        }

        [Fact]
        public void UrlEndpoint()
        {
            Assert.Equal("127.0.0.1:7890", ProxySettings.UrlEndpoint("http://127.0.0.1:7890"));
            Assert.Equal("127.0.0.1:33331", ProxySettings.UrlEndpoint("http://127.0.0.1:33331/commands/pac"));
            Assert.Equal("host:1080", ProxySettings.UrlEndpoint("socks5://host"));
            Assert.Equal("127.0.0.1:10809", ProxySettings.UrlEndpoint("127.0.0.1:10809"));
            Assert.Null(ProxySettings.UrlEndpoint(" "));
        }

        [Fact]
        public void ParseWinHttpSettings_Direct()
        {
            // netsh winhttp reset proxy 之后的默认值
            byte[] direct = { 0x18, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            Assert.Null(ProxySettings.ParseWinHttpSettings(direct));
            Assert.Null(ProxySettings.ParseWinHttpSettings(null));
        }

        [Fact]
        public void ParseWinHttpSettings_Proxy()
        {
            string proxy = "127.0.0.1:7890";
            var data = new System.Collections.Generic.List<byte> { 0x28, 0, 0, 0, 5, 0, 0, 0, 3, 0, 0, 0, (byte)proxy.Length, 0, 0, 0 };
            data.AddRange(System.Text.Encoding.ASCII.GetBytes(proxy));
            data.AddRange(new byte[] { 0, 0, 0, 0 });

            Assert.Equal(proxy, ProxySettings.ParseWinHttpSettings(data.ToArray()));
        }

        [Fact]
        public void TrySplit()
        {
            string host;
            int port;
            Assert.True(ProxySettings.TrySplit("127.0.0.1:7890", out host, out port));
            Assert.Equal("127.0.0.1", host);
            Assert.Equal(7890, port);
            Assert.False(ProxySettings.TrySplit("127.0.0.1", out host, out port));
            Assert.False(ProxySettings.TrySplit(null, out host, out port));
        }
    }

    public class StunProbeTests
    {
        private static readonly byte[] Id = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();

        [Fact]
        public void BuildBindingRequest()
        {
            byte[] packet = StunProbe.BuildBindingRequest(Id);

            Assert.Equal(20, packet.Length);
            Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x21, 0x12, 0xA4, 0x42 }, packet.Take(8).ToArray());
            Assert.Equal(Id, packet.Skip(8).ToArray());
        }

        private static byte[] Response(byte[] id, params byte[] attributes)
        {
            var packet = new System.Collections.Generic.List<byte> { 0x01, 0x01, 0x00, (byte)attributes.Length, 0x21, 0x12, 0xA4, 0x42 };
            packet.AddRange(id);
            packet.AddRange(attributes);
            return packet.ToArray();
        }

        [Fact]
        public void ParseXorMappedAddress()
        {
            // 61.135.10.20:40000，端口与地址分别与 Magic Cookie 异或
            int port = 40000 ^ 0x2112;
            byte[] attribute =
            {
                0x00, 0x20, 0x00, 0x08, 0x00, 0x01, (byte)(port >> 8), (byte)port,
                61 ^ 0x21, 135 ^ 0x12, 10 ^ 0xA4, 20 ^ 0x42
            };

            IPEndPoint mapped = StunProbe.ParseBindingResponse(Response(Id, attribute), Id);

            Assert.Equal(new IPEndPoint(IPAddress.Parse("61.135.10.20"), 40000), mapped);
        }

        [Fact]
        public void ParseMappedAddress_AfterUnknownAttribute()
        {
            byte[] attributes =
            {
                0x80, 0x22, 0x00, 0x03, 0x61, 0x62, 0x63, 0x00, // SOFTWARE "abc"，补齐到 4 字节
                0x00, 0x01, 0x00, 0x08, 0x00, 0x01, 0x0D, 0x96, 1, 2, 3, 4
            };

            Assert.Equal(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478), StunProbe.ParseBindingResponse(Response(Id, attributes), Id));
        }

        [Fact]
        public void ParseRejectsOtherTransaction()
        {
            byte[] attribute = { 0x00, 0x01, 0x00, 0x08, 0x00, 0x01, 0x0D, 0x96, 1, 2, 3, 4 };
            byte[] other = Enumerable.Repeat((byte)9, 12).ToArray();

            Assert.Null(StunProbe.ParseBindingResponse(Response(other, attribute), Id));
            Assert.Null(StunProbe.ParseBindingResponse(new byte[10], Id));
        }
    }

    public class VpnDetectorTests
    {
        [Theory]
        [InlineData("Mihomo", "Meta Tunnel", NetworkInterfaceType.Unknown, true)]
        [InlineData("以太网 2", "Wintun Userspace Tunnel", NetworkInterfaceType.Unknown, true)]
        [InlineData("OpenVPN TAP-Windows6", "TAP-Windows Adapter V9", NetworkInterfaceType.Ethernet, true)]
        [InlineData("公司 VPN", "公司 VPN", NetworkInterfaceType.Ppp, true)]
        [InlineData("以太网", "Realtek PCIe GbE Family Controller", NetworkInterfaceType.Ethernet, false)]
        [InlineData("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet, false)]
        [InlineData("Teredo", "Microsoft Teredo Tunneling Adapter", NetworkInterfaceType.Tunnel, false)]
        public void IsVpnAdapter(string name, string description, NetworkInterfaceType type, bool expected)
        {
            Assert.Equal(expected, VpnDetector.IsVpnAdapter(name, description, type));
        }

        [Fact]
        public void MatchPrograms()
        {
            var names = new[] { "explorer", "clash-verge", "verge-mihomo", "v2rayN", "ShadowsocksR-dotnet4.0", "chrome" };

            Assert.Equal(new[] { "Clash Verge", "v2rayN", "ShadowsocksR" }, VpnDetector.MatchPrograms(names));
        }
    }

    public class NetworkDiagnosisParseTests
    {
        [Fact]
        public void ParseWifiSignal_Chinese()
        {
            string output = "    名称                   : WLAN\r\n    状态                   : 已连接\r\n    SSID                   : Home\r\n    信号                   : 86% \r\n    配置文件               : Home\r\n";
            Assert.Equal(86, NetworkDiagnosis.ParseWifiSignal(output));
        }

        [Fact]
        public void ParseWifiSignal_English()
        {
            Assert.Equal(42, NetworkDiagnosis.ParseWifiSignal("    Name : Wi-Fi\n    Signal : 42%\n"));
            Assert.Null(NetworkDiagnosis.ParseWifiSignal("系统上没有无线接口。"));
            Assert.Null(NetworkDiagnosis.ParseWifiSignal(null));
        }

        [Fact]
        public void CountHostsEntries()
        {
            var lines = new[]
            {
                "# Copyright (c) 1993-2009 Microsoft Corp.",
                "#      102.54.94.97     rhino.acme.com",
                "",
                "127.0.0.1 localhost",
                "::1 localhost",
                "192.168.1.20  nas.home nas   # 家里的 NAS",
                "0.0.0.0 ads.example.com"
            };
            Assert.Equal(3, NetworkDiagnosis.CountHostsEntries(lines));
        }
    }

    public class DiagnosisReportTests
    {
        private static DiagnosisReport Sample()
        {
            var report = new DiagnosisReport { GeneratedAt = new System.DateTime(2026, 10, 2, 9, 30, 0), AdapterName = "WLAN" };
            report.Checks.Add(new DiagnosisCheck(DiagnosisLayer.Adapter, "连接状态", CheckStatus.Ok, "WLAN 已连接"));
            report.Checks.Add(new DiagnosisCheck(DiagnosisLayer.Gateway, "连通性", CheckStatus.Ok, "192.168.1.1 平均 2 ms"));
            report.Checks.Add(new DiagnosisCheck(DiagnosisLayer.Dns, "DNS 192.168.1.1", CheckStatus.Fail, "无响应", "重启路由器", "更换 DNS"));
            report.Checks.Add(new DiagnosisCheck(DiagnosisLayer.Udp, "UDP", CheckStatus.Warning, "只有 UDP 53 可用", "联系网络管理员"));
            return report;
        }

        [Fact]
        public void FaultLayerIsFirstFailure()
        {
            DiagnosisReport report = Sample();

            Assert.Equal(DiagnosisLayer.Dns, report.FaultLayer);
            Assert.Equal("问题出在【DNS】环节：无响应", report.Summary);
            Assert.Equal(new[] { "重启路由器", "更换 DNS" }, report.KeySuggestions);
        }

        [Fact]
        public void ToText()
        {
            string text = Sample().ToText();

            Assert.Contains("网卡：WLAN", text);
            Assert.Contains("链路：本机网卡 √ → 路由器 √ → DNS × → UDP !", text);
            Assert.Contains("  1. 重启路由器", text);
            Assert.Contains("  × DNS 192.168.1.1：无响应", text);
            Assert.Contains("      → 联系网络管理员", text);
        }

        [Fact]
        public void WarningsOnly_Summary()
        {
            DiagnosisReport report = Sample();
            report.Checks.RemoveAt(2);

            Assert.Null(report.FaultLayer);
            Assert.Equal("可以上网，但有 1 项需要注意", report.Summary);
            Assert.Equal(new[] { "联系网络管理员" }, report.KeySuggestions);
        }

        [Fact]
        public void ToJson()
        {
            string json = Sample().ToJson();

            Assert.Contains("\"FaultLayer\":\"Dns\"", json);
            Assert.Contains("\"Status\":\"Fail\"", json);
            Assert.Contains("\"Adapter\":\"WLAN\"", json);
        }
    }
}
