using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IP_UpdateTest.Core.Diagnosis
{
    /// <summary>
    /// 分层网络诊断：采集本机网卡、路由器、外网、DNS、代理 / VPN、UDP 各环节的数据，再交给 DiagnosisAnalyzer 判断。
    /// 各项检测并行进行，约需 10 秒；不需要管理员权限。
    /// </summary>
    public static class NetworkDiagnosis
    {
        /// <summary>
        /// 外网检测用的公网地址（阿里 DNS，同时提供 443 端口）
        /// </summary>
        private static readonly IPAddress PublicTarget = IPAddress.Parse("223.5.5.5");
        private static readonly IPAddress PublicTarget2 = IPAddress.Parse("223.6.6.6");
        private static readonly IPAddress[] PublicDnsServers = { IPAddress.Parse("223.5.5.5"), IPAddress.Parse("119.29.29.29") };

        private const string DomesticUrl = "https://" + DiagnosisAnalyzer.ProbeHost + "/";
        private const string ForeignUrl = "https://" + DiagnosisAnalyzer.ForeignHost + "/generate_204";

        public static async Task<DiagnosisReport> RunAsync(NetworkAdapter adapter)
        {
            DiagnosisFacts facts = await CollectAsync(adapter).ConfigureAwait(false);
            return DiagnosisAnalyzer.Analyze(facts, DateTime.Now);
        }

        public static async Task<DiagnosisFacts> CollectAsync(NetworkAdapter adapter)
        {
            var f = new DiagnosisFacts();
            FillAdapter(f, adapter);
            await Task.Run(() => FillInterfaces(f, adapter)).ConfigureAwait(false);

            bool systemProxy;
            f.Proxies.AddRange(ProxySettings.Read(out systemProxy));
            f.SystemProxyEnabled = systemProxy;

            bool hasGateway = f.AdapterConnected && !string.IsNullOrEmpty(f.Gateway);
            Task<PingStats> gatewayPing = hasGateway ? PingSeriesAsync(f.Gateway, 4, 1000) : Task.FromResult<PingStats>(null);
            Task<string> gatewayMac = hasGateway ? Task.Run(() => ResolveMac(f.Gateway)) : Task.FromResult<string>(null);
            Task<List<string>> hops = TraceHopsAsync(PublicTarget, 3);
            Task<PingStats> publicPing = PingSeriesAsync(PublicTarget.ToString(), 4, 1500);
            Task<bool> publicTcp = AnyAsync(TcpConnectAsync(PublicTarget.ToString(), 443, 3000), TcpConnectAsync(PublicTarget2.ToString(), 443, 3000));
            Task<NetworkTestResult> httpDirect = NetworkTester.TestHttpAsync(NetworkTester.NcsiUrl, NetworkTester.NcsiContent, 4000, true);
            Task<NetworkTestResult> httpViaProxy = f.SystemProxyEnabled
                ? NetworkTester.TestHttpAsync(NetworkTester.NcsiUrl, NetworkTester.NcsiContent, 5000)
                : Task.FromResult<NetworkTestResult>(null);
            Task<NetworkTestResult> httpsDomestic = NetworkTester.TestHttpAsync(DomesticUrl, null, 5000);
            Task<NetworkTestResult> httpsForeign = NetworkTester.TestHttpAsync(ForeignUrl, null, 5000);
            Task<int?> mtu = ProbeMtuAsync(PublicTarget);

            var dnsServers = new List<IPAddress>();
            foreach (string server in f.DnsServers)
            {
                IPAddress address;
                if (IpValidator.TryParseIPv4(server, out address)) dnsServers.Add(address);
            }
            List<Task<long?>> dnsTasks = dnsServers.Select(s => DnsProbe.QueryAsync(s, DiagnosisAnalyzer.ProbeHost, 2000)).ToList();
            Task<NetworkTestResult> resolve = NetworkTester.DnsResolveAsync(DiagnosisAnalyzer.ProbeHost);
            Task<string> nxDomain = ResolveFirstIPv4Async("iptool-nx-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".com", 5000);
            Task<int> hosts = Task.Run(() => ReadHostsEntries());
            Task<bool> publicDnsUdp = AnyAsync(PublicDnsServers.Select(async s =>
                (await DnsProbe.QueryAsync(s, DiagnosisAnalyzer.ProbeHost, 2000).ConfigureAwait(false)).HasValue).ToArray());
            Task stun = ProbeStunAsync(f);

            List<Task<bool>> proxyTasks = f.Proxies.Select(p => ProbeEndpointAsync(p.Endpoint)).ToList();
            Task<List<string>> programs = Task.Run(() => FindProxyPrograms());
            Task<int?> wifi = f.IsWireless && f.AdapterConnected ? Task.Run(() => ReadWifiSignal()) : Task.FromResult<int?>(null);

            await Task.WhenAll(new Task[]
            {
                gatewayPing, gatewayMac, hops, publicPing, publicTcp, httpDirect, httpViaProxy, httpsDomestic, httpsForeign, mtu,
                resolve, nxDomain, hosts, publicDnsUdp, stun, programs, wifi
            }.Concat(dnsTasks).Concat(proxyTasks)).ConfigureAwait(false);

            f.GatewayPing = gatewayPing.Result;
            f.GatewayMac = gatewayMac.Result;
            f.Hops.AddRange(hops.Result);
            f.PublicPing = publicPing.Result;
            f.PublicTcpOk = publicTcp.Result;
            f.HttpDirect = ToOutcome(httpDirect.Result);
            f.HttpViaProxy = ToOutcome(httpViaProxy.Result);
            f.HttpsDomestic = ToOutcome(httpsDomestic.Result);
            f.HttpsForeign = ToOutcome(httpsForeign.Result);
            f.PathMtu = mtu.Result;

            for (int i = 0; i < dnsServers.Count; i++)
                f.DnsServerResults.Add(new KeyValuePair<string, long?>(dnsServers[i].ToString(), dnsTasks[i].Result));
            NetworkTestResult resolved = resolve.Result;
            f.SystemResolveOk = resolved.Success;
            f.SystemResolveMs = resolved.RoundtripTime;
            f.SystemResolveAddress = resolved.Address;
            f.SystemResolveError = resolved.Success ? null : resolved.Message;
            f.NxDomainAnswer = nxDomain.Result;
            f.HostsEntries = hosts.Result;
            f.PublicDnsUdpOk = publicDnsUdp.Result;

            for (int i = 0; i < f.Proxies.Count; i++)
            {
                if (f.Proxies[i].Endpoint != null) f.Proxies[i].Reachable = proxyTasks[i].Result;
            }
            f.ProxyPrograms.AddRange(programs.Result);
            f.WifiSignalPercent = wifi.Result;
            return f;
        }

        #region 网卡信息

        private static void FillAdapter(DiagnosisFacts f, NetworkAdapter adapter)
        {
            f.AdapterFound = adapter != null;
            if (adapter == null) return;
            f.AdapterName = adapter.Name;
            f.AdapterDescription = adapter.InterfaceDescription;
            f.AdapterConnected = adapter.Status == AdapterStatus.Connected;
            f.IsWireless = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211.ToString();
            f.LinkSpeedBps = adapter.LinkSpeedBps;
            f.Dhcp = adapter.IsDhcpEnabled;
            f.IpAddress = adapter.IpAddress;
            f.SubnetMask = adapter.SubnetMask;
            f.Gateway = adapter.Gateway;
            f.DnsServers = new[] { adapter.DnsMain, adapter.DnsBackup }.Where(d => !string.IsNullOrEmpty(d)).ToArray();
        }

        /// <summary>
        /// 遍历所有已连接的网卡：默认路由所在网卡、其他带默认网关的网卡、VPN / 隧道网卡、本机地址
        /// </summary>
        private static void FillInterfaces(DiagnosisFacts f, NetworkAdapter adapter)
        {
            int primary = AdapterService.GetPrimaryInterfaceIndex();
            bool selectedHasGateway = f.AdapterConnected && !string.IsNullOrEmpty(f.Gateway);
            f.RouteViaAdapter = primary < 0 || adapter == null || adapter.InterfaceIndex == primary;

            NetworkInterface[] nics;
            try
            {
                nics = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException)
            {
                return;
            }

            foreach (NetworkInterface nic in nics)
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                IPInterfaceProperties properties = nic.GetIPProperties();
                int index = -1;
                try
                {
                    IPv4InterfaceProperties ipv4 = properties.GetIPv4Properties();
                    if (ipv4 != null) index = ipv4.Index;
                }
                catch (NetworkInformationException)
                {
                    // 未启用 IPv4
                }

                foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork) f.LocalAddresses.Add(address.Address.ToString());
                }

                bool vpn = VpnDetector.IsVpnAdapter(nic.Name, nic.Description, nic.NetworkInterfaceType);
                if (vpn) f.VpnAdapters.Add(nic.Name == nic.Description ? nic.Name : $"{nic.Name}（{nic.Description}）");

                if (primary >= 0 && index == primary)
                {
                    f.RouteAdapterName = nic.Name;
                    f.RouteViaVpn = vpn;
                }

                bool isSelected = adapter != null && nic.Id == adapter.NetworkInterfaceID;
                bool hasGateway = properties.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                if (selectedHasGateway && !vpn && !isSelected && hasGateway) f.OtherGatewayAdapters.Add(nic.Name);
            }
        }

        private static string ResolveMac(string ip)
        {
            IPAddress address;
            if (!IpValidator.TryParseIPv4(ip, out address)) return null;
            PhysicalAddress mac = IpConflict.Probe(address);
            return mac == null ? null : NetworkAdapter.FormatMac(mac);
        }

        /// <summary>
        /// 读取 Wi-Fi 信号强度（netsh wlan show interfaces 中的百分比）
        /// </summary>
        private static int? ReadWifiSignal()
        {
            try
            {
                int exitCode;
                string output;
                NetshBackend.Execute("wlan show interfaces", out exitCode, out output);
                return exitCode == 0 ? ParseWifiSignal(output) : null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// 从 netsh wlan show interfaces 的输出中取信号强度。输出随系统语言变化（“信号”“Signal”），
        /// 但只有信号一行以百分号结尾（纯函数，便于测试）
        /// </summary>
        public static int? ParseWifiSignal(string output)
        {
            if (string.IsNullOrEmpty(output)) return null;
            Match match = Regex.Match(output, @"^[^:\r\n]+:\s*(\d{1,3})\s*%\s*$", RegexOptions.Multiline);
            int value;
            return match.Success && int.TryParse(match.Groups[1].Value, out value) && value <= 100 ? value : (int?)null;
        }

        #endregion

        #region 连通性检测

        private static async Task<PingStats> PingSeriesAsync(string host, int count, int timeout)
        {
            var roundtrips = new List<long?>();
            for (int i = 0; i < count; i++)
            {
                NetworkTestResult result = await NetworkTester.PingAsync(host, timeout).ConfigureAwait(false);
                roundtrips.Add(result.Success ? result.RoundtripTime : (long?)null);
                if (i < count - 1) await Task.Delay(200).ConfigureAwait(false);
            }
            return PingStats.From(host, roundtrips);
        }

        /// <summary>
        /// 用逐渐增大的 TTL 探测去往外网的前几跳，某跳无响应时为 null
        /// </summary>
        private static async Task<List<string>> TraceHopsAsync(IPAddress target, int maxHops)
        {
            var tasks = Enumerable.Range(1, maxHops).Select(async ttl =>
            {
                try
                {
                    using (var ping = new Ping())
                    {
                        PingReply reply = await ping.SendPingAsync(target, 1500, new byte[32], new PingOptions(ttl, true)).ConfigureAwait(false);
                        bool answered = reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success;
                        return answered && reply.Address != null ? reply.Address.ToString() : null;
                    }
                }
                catch (PingException)
                {
                    return null;
                }
            }).ToList();
            string[] hops = await Task.WhenAll(tasks).ConfigureAwait(false);

            // 某跳已到达目标时，后面的跳没有意义
            int reached = Array.IndexOf(hops, target.ToString());
            return (reached >= 0 ? hops.Take(reached + 1) : hops).ToList();
        }

        /// <summary>
        /// 用禁止分片的 ping 查找路径 MTU：1500 能通过时直接返回，否则二分查找；公网不响应 ping 时返回 null
        /// </summary>
        private static async Task<int?> ProbeMtuAsync(IPAddress target)
        {
            const int Overhead = 28; // IPv4 头 20 字节 + ICMP 头 8 字节
            if (!await PingDontFragmentAsync(target, 64).ConfigureAwait(false)) return null;
            if (await PingDontFragmentAsync(target, 1500 - Overhead).ConfigureAwait(false)) return 1500;

            int low = 1200 - Overhead, high = 1500 - Overhead;
            if (!await PingDontFragmentAsync(target, low).ConfigureAwait(false)) return null;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (await PingDontFragmentAsync(target, middle).ConfigureAwait(false)) low = middle;
                else high = middle;
            }
            return low + Overhead;
        }

        private static async Task<bool> PingDontFragmentAsync(IPAddress target, int payload)
        {
            try
            {
                using (var ping = new Ping())
                {
                    PingReply reply = await ping.SendPingAsync(target, 1000, new byte[payload], new PingOptions(64, true)).ConfigureAwait(false);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch (PingException)
            {
                return false;
            }
        }

        public static async Task<bool> TcpConnectAsync(string host, int port, int timeout)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    Task connect = client.ConnectAsync(host, port);
                    if (await Task.WhenAny(connect, Task.Delay(timeout)).ConfigureAwait(false) != connect)
                    {
                        _ = connect.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                        return false;
                    }
                    await connect.ConfigureAwait(false);
                    return client.Connected;
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }

        private static Task<bool> ProbeEndpointAsync(string endpoint)
        {
            string host;
            int port;
            return ProxySettings.TrySplit(endpoint, out host, out port) ? TcpConnectAsync(host, port, 2000) : Task.FromResult(false);
        }

        private static async Task<bool> AnyAsync(params Task<bool>[] tasks)
        {
            bool[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.Any(r => r);
        }

        private static HttpOutcome ToOutcome(NetworkTestResult result)
        {
            if (result == null) return null;
            return new HttpOutcome
            {
                Success = result.Success,
                Milliseconds = result.RoundtripTime,
                Message = result.Message,
                NeedsLogin = result.NeedsLogin,
                CertificateError = result.CertificateError
            };
        }

        #endregion

        #region DNS

        /// <summary>
        /// 用系统解析器解析，返回第一个 IPv4 地址；解析失败或超时返回 null
        /// </summary>
        private static async Task<string> ResolveFirstIPv4Async(string host, int timeout)
        {
            try
            {
                Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(host);
                if (await Task.WhenAny(lookup, Task.Delay(timeout)).ConfigureAwait(false) != lookup)
                {
                    _ = lookup.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    return null;
                }
                IPAddress address = (await lookup.ConfigureAwait(false)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                return address?.ToString();
            }
            catch (SocketException)
            {
                return null;
            }
        }

        private static int ReadHostsEntries()
        {
            try
            {
                string path = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
                return File.Exists(path) ? CountHostsEntries(File.ReadAllLines(path)) : 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return 0;
            }
        }

        /// <summary>
        /// hosts 文件中除注释和 localhost 外的条目数（纯函数，便于测试）
        /// </summary>
        public static int CountHostsEntries(IEnumerable<string> lines)
        {
            int count = 0;
            foreach (string raw in lines)
            {
                string line = raw;
                int comment = line.IndexOf('#');
                if (comment >= 0) line = line.Substring(0, comment);
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                count += parts.Skip(1).Count(h => !h.Equals("localhost", StringComparison.OrdinalIgnoreCase));
            }
            return count;
        }

        #endregion

        #region 代理 / UDP

        private static List<string> FindProxyPrograms()
        {
            var names = new List<string>();
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        names.Add(process.ProcessName);
                    }
                    catch (InvalidOperationException)
                    {
                        // 进程已退出
                    }
                }
            }
            return VpnDetector.MatchPrograms(names);
        }

        /// <summary>
        /// 用同一个本地端口依次询问 STUN 服务器，最多取得两个映射地址
        /// </summary>
        private static async Task ProbeStunAsync(DiagnosisFacts f)
        {
            try
            {
                using (var probe = new StunProbe())
                {
                    foreach (string host in StunProbe.Servers)
                    {
                        if (f.StunMapped.Count >= 2) break;
                        string ip = await ResolveFirstIPv4Async(host, 3000).ConfigureAwait(false);
                        if (ip == null) continue;
                        f.StunServersTried++;
                        IPEndPoint mapped = await probe.QueryAsync(new IPEndPoint(IPAddress.Parse(ip), StunProbe.Port)).ConfigureAwait(false);
                        if (mapped != null) f.StunMapped.Add(mapped.ToString());
                    }
                }
            }
            catch (SocketException)
            {
                // 本机无可用网络
            }
        }

        #endregion
    }
}
