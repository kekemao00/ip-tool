using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace IP_UpdateTest.Core.Diagnosis
{
    /// <summary>
    /// 根据采集到的数据逐层判断问题所在并给出建议（纯函数，便于测试）
    /// </summary>
    public static class DiagnosisAnalyzer
    {
        public const string ProbeHost = "www.baidu.com";
        public const string ForeignHost = "www.google.com";

        private const string ChangeDnsTip = "更换 DNS：可用主界面的“测速”选出可用且较快的 DNS（如 223.5.5.5、119.29.29.29）";

        private static readonly string[] UdpBlockedTips =
        {
            "公司、校园、酒店等网络常封锁 UDP，可向网络管理员确认",
            "检查防火墙或安全软件是否拦截了 UDP",
            "受影响的有语音视频通话、网络游戏、QUIC（HTTP/3），以及 WireGuard、Hysteria 等基于 UDP 的 VPN 或代理协议，可改用 TCP 模式"
        };

        public static DiagnosisReport Analyze(DiagnosisFacts f, DateTime generatedAt)
        {
            var report = new DiagnosisReport { GeneratedAt = generatedAt, AdapterName = f.AdapterName };
            var checks = report.Checks;
            var ctx = new Context(f);

            AnalyzeAdapter(f, ctx, checks);
            AnalyzeGateway(f, ctx, checks);
            AnalyzeInternet(f, ctx, checks);
            AnalyzeDns(f, ctx, checks);
            AnalyzeProxyVpn(f, ctx, checks);
            AnalyzeUdp(f, ctx, checks);
            return report;
        }

        /// <summary>
        /// 各环节共用的推断结果
        /// </summary>
        private sealed class Context
        {
            public Context(DiagnosisFacts f)
            {
                DirectOk = f.PublicTcpOk == true || (f.HttpDirect != null && f.HttpDirect.Success)
                    || (f.PublicPing != null && f.PublicPing.AnyReply);
                InternetOk = DirectOk || (f.HttpViaProxy != null && f.HttpViaProxy.Success)
                    || (f.HttpsDomestic != null && f.HttpsDomestic.Success);
                FakeIp = IsFakeIp(f.SystemResolveAddress);
                UsingProxy = f.SystemProxyEnabled || f.RouteViaVpn || f.VpnAdapters.Count > 0;
            }

            /// <summary>
            /// 不经代理能访问外网
            /// </summary>
            public bool DirectOk { get; private set; }

            /// <summary>
            /// 无论是否经代理，能访问外网
            /// </summary>
            public bool InternetOk { get; private set; }

            public bool FakeIp { get; private set; }

            public bool UsingProxy { get; private set; }
        }

        #region 本机网卡

        private static void AnalyzeAdapter(DiagnosisFacts f, Context ctx, List<DiagnosisCheck> checks)
        {
            const DiagnosisLayer L = DiagnosisLayer.Adapter;
            if (!f.AdapterFound)
            {
                checks.Add(new DiagnosisCheck(L, "网卡", ctx.InternetOk ? CheckStatus.Info : CheckStatus.Fail, "没有找到可用的网卡",
                    "确认网卡没有被禁用（可在本工具的网卡列表中启用）",
                    "在设备管理器中检查网卡驱动是否正常"));
                return;
            }

            if (!f.AdapterConnected)
            {
                // 所选网卡未连接、但能经其他网卡上网时，只是选错了网卡
                bool otherWorks = ctx.InternetOk && !f.RouteViaAdapter && !string.IsNullOrEmpty(f.RouteAdapterName);
                checks.Add(otherWorks
                    ? new DiagnosisCheck(L, "连接状态", CheckStatus.Warning, $"“{f.AdapterName}”未连接，当前上网走的是“{f.RouteAdapterName}”",
                        $"要诊断正在上网的网卡，请在主界面选择“{f.RouteAdapterName}”后重新诊断")
                    : new DiagnosisCheck(L, "连接状态", CheckStatus.Fail, $"“{f.AdapterName}”未连接",
                        "有线：检查网线两端是否插好、网口指示灯是否亮，换根网线或换个网口试试",
                        "无线：确认已连接 Wi-Fi，且没有开启飞行模式",
                        "网卡被禁用时可在本工具中启用",
                        "仍不行时，在设备管理器中更新或重装网卡驱动"));
                return;
            }

            string speed = f.LinkSpeedBps > 0 ? " · " + NetworkAdapter.FormatSpeed(f.LinkSpeedBps) : "";
            checks.Add(new DiagnosisCheck(L, "连接状态", CheckStatus.Ok, $"{f.AdapterName} 已连接{speed}"));

            IPAddress ip, mask, gateway;
            bool hasIp = IpValidator.TryParseIPv4(f.IpAddress, out ip);
            bool hasMask = IpValidator.TryParseIPv4(f.SubnetMask, out mask);
            if (!hasIp)
            {
                checks.Add(new DiagnosisCheck(L, "IP 地址", CheckStatus.Fail, "没有 IPv4 地址",
                    f.Dhcp ? "在主界面“更多”菜单中续订 DHCP 租约" : "检查手动设置的 IP 是否正确",
                    "重启路由器后重试"));
            }
            else if (IsApipa(ip))
            {
                checks.Add(new DiagnosisCheck(L, "IP 地址", CheckStatus.Fail,
                    $"{ip} 是系统自动分配的临时地址（169.254.x.x），没有从路由器获取到 IP",
                    "在主界面“更多”菜单中续订 DHCP 租约",
                    "重启路由器；检查路由器的 DHCP 服务是否开启、地址池是否已用完",
                    "有线连接时检查网线；无线时断开 Wi-Fi 重新连接",
                    "临时可改为手动设置与路由器同网段的 IP"));
            }
            else
            {
                string prefix = hasMask ? "/" + IpValidator.MaskToPrefix(mask) : "";
                checks.Add(new DiagnosisCheck(L, "IP 地址", CheckStatus.Ok, $"{ip}{prefix}（{(f.Dhcp ? "自动获取" : "手动")}）"));
            }

            if (!IpValidator.TryParseIPv4(f.Gateway, out gateway))
            {
                checks.Add(ctx.InternetOk
                    ? new DiagnosisCheck(L, "默认网关", CheckStatus.Info, "未配置（上网走其他网卡）")
                    : new DiagnosisCheck(L, "默认网关", CheckStatus.Fail, "未配置默认网关，只能访问本网段",
                        f.Dhcp ? "续订 DHCP 租约，或检查路由器的 DHCP 设置" : "手动设置时补上网关（一般是路由器地址，如 192.168.1.1）"));
            }
            else if (hasIp && hasMask && !IsApipa(ip) && !IpValidator.IsSameSubnet(ip, gateway, mask))
            {
                checks.Add(new DiagnosisCheck(L, "默认网关", CheckStatus.Fail, $"网关 {gateway} 与 IP {ip}/{IpValidator.MaskToPrefix(mask)} 不在同一网段",
                    "修改 IP 或网关，使两者在同一网段"));
            }
            else
            {
                checks.Add(new DiagnosisCheck(L, "默认网关", CheckStatus.Ok, gateway.ToString()));
            }

            if (!f.RouteViaAdapter && !f.RouteViaVpn && !string.IsNullOrEmpty(f.RouteAdapterName))
            {
                checks.Add(new DiagnosisCheck(L, "上网网卡", CheckStatus.Info, $"默认路由在“{f.RouteAdapterName}”上，本网卡不是当前上网的网卡"));
            }

            if (f.OtherGatewayAdapters.Count > 0)
            {
                string others = string.Join("、", f.OtherGatewayAdapters.Select(n => $"“{n}”"));
                checks.Add(new DiagnosisCheck(L, "多个默认网关", CheckStatus.Warning, $"{others}也配置了默认网关，流量可能走错网卡",
                    "断开或禁用不用的网卡（如同时插着网线又连着 Wi-Fi）",
                    "需要同时使用时，去掉内网网卡的默认网关，或调整接口跃点数（metric）"));
            }

            if (f.WifiSignalPercent.HasValue)
            {
                int signal = f.WifiSignalPercent.Value;
                checks.Add(signal < 40
                    ? new DiagnosisCheck(L, "Wi-Fi 信号", CheckStatus.Warning, $"{signal}%，信号较弱",
                        "靠近路由器或减少墙体遮挡", "改连 5GHz 频段，或改用有线连接")
                    : new DiagnosisCheck(L, "Wi-Fi 信号", CheckStatus.Ok, signal + "%"));
            }
        }

        #endregion

        #region 路由器

        private static void AnalyzeGateway(DiagnosisFacts f, Context ctx, List<DiagnosisCheck> checks)
        {
            const DiagnosisLayer L = DiagnosisLayer.Gateway;
            if (!f.AdapterConnected || string.IsNullOrEmpty(f.Gateway) || f.GatewayPing == null)
            {
                checks.Add(new DiagnosisCheck(L, "路由器", CheckStatus.Skipped, "没有可测试的网关，跳过"));
                return;
            }

            PingStats ping = f.GatewayPing;
            string mac = f.GatewayMac == null ? "" : $"（MAC {f.GatewayMac}）";
            if (!ping.AnyReply)
            {
                if (f.GatewayMac != null)
                    checks.Add(new DiagnosisCheck(L, "连通性", CheckStatus.Info, $"{f.Gateway} 不响应 ping，但能通过 ARP 找到{mac}，路由器在线"));
                else if (ctx.InternetOk)
                    checks.Add(new DiagnosisCheck(L, "连通性", CheckStatus.Info, $"{f.Gateway} 不响应 ping（部分路由器或 VPN 会屏蔽），外网正常，可忽略"));
                else
                    checks.Add(new DiagnosisCheck(L, "连通性", CheckStatus.Fail, $"无法连接路由器 {f.Gateway}（ping 和 ARP 均无响应）",
                        "检查网线或 Wi-Fi 连接，确认连的是正确的 Wi-Fi",
                        "重启路由器（断电 10 秒后再通电）",
                        "手动设置 IP 时，确认网关地址就是路由器的地址",
                        "同一路由器下的其他设备也连不上时，问题在路由器本身"));
            }
            else if (ping.Received < ping.Sent)
            {
                checks.Add(new DiagnosisCheck(L, "丢包", CheckStatus.Warning, $"到路由器 {f.Gateway} {ping.Describe()}",
                    f.IsWireless ? "Wi-Fi 信号弱或有干扰：靠近路由器、改连 5GHz 频段，或改用有线" : "检查网线和水晶头，换根网线或换个路由器网口试试",
                    "路由器负载过高也会丢包，可重启路由器"));
            }
            else if (ping.AverageMs > (f.IsWireless ? 30 : 10) || ping.MaxMs > 200)
            {
                checks.Add(new DiagnosisCheck(L, "延迟", CheckStatus.Warning, $"到路由器 {f.Gateway} {ping.Describe()}，局域网延迟偏高",
                    f.IsWireless ? "Wi-Fi 信号弱或有干扰：靠近路由器、改连 5GHz 频段，或改用有线" : "检查网线，或换个路由器网口试试",
                    "局域网内有设备大量上传下载时也会变慢，可重启路由器"));
            }
            else
            {
                checks.Add(new DiagnosisCheck(L, "连通性", CheckStatus.Ok, $"{f.Gateway} {ping.Describe()}{mac}"));
            }

            AnalyzeHops(f, checks);
        }

        /// <summary>
        /// 根据前几跳地址判断双重 NAT 和运营商级 NAT
        /// </summary>
        private static void AnalyzeHops(DiagnosisFacts f, List<DiagnosisCheck> checks)
        {
            const DiagnosisLayer L = DiagnosisLayer.Gateway;
            if (f.RouteViaVpn || f.Hops.Count < 2) return;

            IPAddress second;
            if (IpValidator.TryParseIPv4(f.Hops[1], out second))
            {
                byte[] b = second.GetAddressBytes();
                if (b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    checks.Add(new DiagnosisCheck(L, "双重 NAT", CheckStatus.Warning,
                        $"路由器之后的第 2 跳 {second} 仍是内网地址，光猫和路由器可能都在做路由（双重 NAT）",
                        "一般上网不受影响；需要端口映射、远程访问、P2P 或游戏联机时，把光猫改为桥接、由路由器拨号，或把路由器改为 AP（有线中继）模式",
                        "也可在光猫上把路由器设为 DMZ 主机"));
                else if (b[0] == 10)
                    checks.Add(new DiagnosisCheck(L, "上级线路", CheckStatus.Info, $"第 2 跳 {second} 是内网地址，可能是双重 NAT 或运营商内网"));
                else if (!IsCgnat(second))
                    checks.Add(new DiagnosisCheck(L, "上级线路", CheckStatus.Ok, $"路由器之后直接进入运营商网络（{second}）"));
            }

            for (int i = 1; i < f.Hops.Count; i++)
            {
                IPAddress hop;
                if (IpValidator.TryParseIPv4(f.Hops[i], out hop) && IsCgnat(hop))
                {
                    checks.Add(new DiagnosisCheck(L, "运营商 NAT", CheckStatus.Info,
                        $"第 {i + 1} 跳 {hop} 是运营商级 NAT 地址（100.64.0.0/10），这条宽带没有公网 IPv4",
                        "一般上网不受影响；需要远程访问或端口映射时，可向运营商申请公网 IP，或使用 IPv6、内网穿透"));
                    break;
                }
            }
        }

        #endregion

        #region 外网

        private static void AnalyzeInternet(DiagnosisFacts f, Context ctx, List<DiagnosisCheck> checks)
        {
            const DiagnosisLayer L = DiagnosisLayer.Internet;
            if (f.HttpDirect != null && f.HttpDirect.NeedsLogin)
            {
                checks.Add(new DiagnosisCheck(L, "网页认证", CheckStatus.Fail, "访问被重定向，当前网络需要网页登录（如酒店、机场、校园 Wi-Fi）",
                    "在浏览器中打开 http://www.msftconnecttest.com/redirect 完成登录",
                    "登录后重新诊断"));
                return;
            }

            if (!ctx.DirectOk)
            {
                if (f.HttpViaProxy != null && f.HttpViaProxy.Success)
                {
                    checks.Add(new DiagnosisCheck(L, "直连", CheckStatus.Info, "直连无法访问外网，只能通过代理上网（公司网络常见）"));
                }
                else if (f.RouteViaVpn)
                {
                    checks.Add(new DiagnosisCheck(L, "连通性", CheckStatus.Fail, "无法访问外网，且上网流量走的是 VPN / 代理 TUN 网卡",
                        "VPN 可能已断开或节点不可用：重连 VPN 或更换节点",
                        "断开 VPN（或关闭代理软件的 TUN 模式）后重新诊断，确认本地网络是否正常"));
                }
                else
                {
                    checks.Add(new DiagnosisCheck(L, "连通性", CheckStatus.Fail, "无法访问外网（ping、TCP、HTTP 均失败）",
                        "查看光猫和路由器的指示灯：光猫 LOS 红灯一般是光纤线路故障，WAN / Internet 灯不亮说明路由器没连上宽带",
                        "依次重启光猫和路由器",
                        "登录路由器管理页面，查看 WAN 口是否获取到 IP、拨号是否成功",
                        "同一网络下其他设备也不能上网时，基本可以确定是路由器或宽带问题：确认宽带未欠费，或联系运营商报修"));
                }
                return;
            }

            PingStats ping = f.PublicPing;
            if (ping == null || !ping.AnyReply)
            {
                checks.Add(new DiagnosisCheck(L, "ping", CheckStatus.Info, "公网 ping 无响应，但 TCP / HTTP 正常：网络屏蔽了 ping，不影响上网"));
            }
            else if (ping.Received < ping.Sent)
            {
                checks.Add(new DiagnosisCheck(L, "丢包", CheckStatus.Warning, $"到 {ping.Target} {ping.Describe()}",
                    "路由器正常而公网丢包，多为宽带线路问题：换个时间再测，持续丢包时联系运营商",
                    "检查是否有设备在大量上传下载、占满了带宽"));
            }
            else if (ping.AverageMs > 150)
            {
                checks.Add(new DiagnosisCheck(L, "延迟", CheckStatus.Warning, $"到 {ping.Target} {ping.Describe()}，延迟偏高",
                    "检查是否有设备在大量上传下载、占满了带宽",
                    "持续偏高时联系运营商检查线路"));
            }
            else
            {
                checks.Add(new DiagnosisCheck(L, "连通性", CheckStatus.Ok, $"ping {ping.Target} {ping.Describe()}"));
            }

            if (f.HttpDirect != null && !f.HttpDirect.Success && f.PublicTcpOk == true)
            {
                checks.Add(new DiagnosisCheck(L, "HTTP", CheckStatus.Warning, "TCP 能连通，但 HTTP 访问失败：" + f.HttpDirect.Message,
                    "可能被防火墙、安全软件或上网行为管理拦截，检查安全软件的网络防护设置"));
            }

            HttpOutcome https = f.HttpsDomestic;
            if (https != null)
            {
                if (https.CertificateError)
                    checks.Add(new DiagnosisCheck(L, "HTTPS", CheckStatus.Warning, "HTTPS 证书校验失败：" + https.Message,
                        "检查系统日期和时间是否正确",
                        "公司网络或安全软件的 HTTPS 扫描会替换证书，可暂时关闭安全软件的 HTTPS 扫描后重试"));
                else if (https.Success)
                    checks.Add(new DiagnosisCheck(L, "HTTPS", CheckStatus.Ok, $"访问 {ProbeHost} 正常（{https.Milliseconds} ms）"));
                else if (f.SystemProxyEnabled)
                    checks.Add(new DiagnosisCheck(L, "HTTPS", CheckStatus.Info, $"按系统代理访问 {ProbeHost} 失败，见“代理/VPN”环节"));
                else
                    checks.Add(new DiagnosisCheck(L, "HTTPS", CheckStatus.Warning, $"访问 {ProbeHost} 失败：{https.Message}",
                        "可能被防火墙或安全软件拦截；可在浏览器中打开该网站确认"));
            }

            if (f.PathMtu.HasValue)
            {
                int mtu = f.PathMtu.Value;
                if (mtu < 1400 && !f.RouteViaVpn)
                    checks.Add(new DiagnosisCheck(L, "MTU", CheckStatus.Warning, $"路径 MTU 只有 {mtu}，偏小，可能导致部分网站打不开或加载卡住",
                        $"以管理员身份运行 netsh interface ipv4 set subinterface \"{f.AdapterName}\" mtu={mtu} store=persistent",
                        "检查路由器 WAN 口的 MTU 设置（PPPoE 拨号一般为 1492）"));
                else
                    checks.Add(new DiagnosisCheck(L, "MTU", CheckStatus.Info, $"路径 MTU {mtu}{(mtu == 1492 ? "（PPPoE 拨号常见值）" : "")}"));
            }
        }

        #endregion

        #region DNS

        private static void AnalyzeDns(DiagnosisFacts f, Context ctx, List<DiagnosisCheck> checks)
        {
            const DiagnosisLayer L = DiagnosisLayer.Dns;
            var servers = f.DnsServerResults;
            bool anyServerOk = servers.Any(s => s.Value.HasValue);

            if (servers.Count == 0 && f.AdapterConnected)
            {
                checks.Add(new DiagnosisCheck(L, "DNS 服务器", CheckStatus.Warning, "网卡没有配置 DNS 服务器",
                    "手动设置 DNS，如 223.5.5.5、119.29.29.29"));
            }

            foreach (KeyValuePair<string, long?> server in servers)
            {
                string title = "DNS " + server.Key;
                if (server.Value.HasValue)
                {
                    long ms = server.Value.Value;
                    checks.Add(ms > 300
                        ? new DiagnosisCheck(L, title, CheckStatus.Warning, $"响应慢（{ms} ms）", ChangeDnsTip)
                        : new DiagnosisCheck(L, title, CheckStatus.Ok, $"响应 {ms} ms"));
                    continue;
                }
                if (anyServerOk)
                {
                    checks.Add(new DiagnosisCheck(L, title, CheckStatus.Warning, "无响应（其他 DNS 可用）", "把无响应的 DNS 换掉：" + ChangeDnsTip));
                    continue;
                }

                IPAddress address;
                bool isFake = IpValidator.TryParseIPv4(server.Key, out address) && IsFakeIp(address);
                bool isLocal = !isFake && (server.Key == f.Gateway || IsPrivateAddress(server.Key));
                checks.Add(new DiagnosisCheck(L, title, CheckStatus.Fail, "无响应",
                    isFake ? "这是代理软件的虚拟 DNS 地址：重新打开代理软件，或把网卡 DNS 改回自动获取" : null,
                    isLocal ? $"DNS 指向路由器（{server.Key}），路由器的 DNS 转发可能异常：重启路由器，或改为手动 DNS" : null,
                    ChangeDnsTip,
                    f.PublicDnsUdpOk == false && f.PublicTcpOk == true ? "公共 DNS 的 UDP 查询也没有应答，网络可能限制了 UDP，见“UDP”环节" : null));
            }

            if (!f.SystemResolveOk)
            {
                checks.Add(new DiagnosisCheck(L, "域名解析", CheckStatus.Fail, $"系统解析 {ProbeHost} 失败：{f.SystemResolveError}",
                    "以管理员身份运行 ipconfig /flushdns 清除 DNS 缓存",
                    "检查 hosts 文件（C:\\Windows\\System32\\drivers\\etc\\hosts）中有没有错误条目",
                    ChangeDnsTip,
                    ctx.UsingProxy || f.ProxyPrograms.Count > 0 ? "开着代理软件时，检查其 DNS 设置" : null));
            }
            else if (ctx.FakeIp)
            {
                checks.Add(new DiagnosisCheck(L, "域名解析", CheckStatus.Info, $"{ProbeHost} → {f.SystemResolveAddress}（代理软件的 Fake-IP 地址，解析由代理接管）"));
            }
            else
            {
                checks.Add(new DiagnosisCheck(L, "域名解析", CheckStatus.Ok, $"{ProbeHost} → {f.SystemResolveAddress}（{f.SystemResolveMs} ms）"));
            }

            if (f.NxDomainAnswer != null && !IsFakeIp(f.NxDomainAnswer))
            {
                checks.Add(new DiagnosisCheck(L, "DNS 劫持", CheckStatus.Warning, $"不存在的域名被解析到 {f.NxDomainAnswer}，DNS 被运营商或路由器劫持（常用于广告跳转）",
                    "改用可靠的公共 DNS，如 223.5.5.5、119.29.29.29",
                    "检查路由器是否开启了“上网导航”等广告类功能"));
            }

            if (f.HostsEntries > 0)
            {
                checks.Add(new DiagnosisCheck(L, "hosts 文件", CheckStatus.Info, $"有 {f.HostsEntries} 条自定义解析，访问这些域名时不经过 DNS"));
            }
        }

        #endregion

        #region 代理 / VPN

        private static void AnalyzeProxyVpn(DiagnosisFacts f, Context ctx, List<DiagnosisCheck> checks)
        {
            const DiagnosisLayer L = DiagnosisLayer.ProxyVpn;
            int before = checks.Count;

            foreach (ProxyFact proxy in f.Proxies)
            {
                if (proxy.Reachable != false)
                {
                    checks.Add(new DiagnosisCheck(L, proxy.Source, CheckStatus.Info, proxy.Value + (proxy.Reachable == true ? "（可连接）" : "")));
                }
                else if (proxy.Source.StartsWith("WinHTTP", StringComparison.Ordinal))
                {
                    checks.Add(new DiagnosisCheck(L, proxy.Source, CheckStatus.Warning, $"{proxy.Value} 无法连接，会影响 Windows 更新等系统服务",
                        "以管理员身份运行 netsh winhttp reset proxy 恢复直连"));
                }
                else if (proxy.Source.StartsWith("环境变量", StringComparison.Ordinal))
                {
                    checks.Add(new DiagnosisCheck(L, proxy.Source, CheckStatus.Warning, $"{proxy.Value} 无法连接，会影响 git、npm、curl 等命令行工具",
                        "启动对应的代理软件，或删除该环境变量（系统属性 → 环境变量）"));
                }
                else
                {
                    checks.Add(new DiagnosisCheck(L, proxy.Source, CheckStatus.Fail, $"{proxy.Value} 无法连接，浏览器等使用系统代理的程序会打不开网页",
                        "启动对应的代理软件",
                        "不用代理时，在“设置 → 网络和 Internet → 代理”中关闭“使用代理服务器”和“使用设置脚本”",
                        "代理软件异常退出时常会留下系统代理设置"));
                }
            }

            bool proxyReachable = f.Proxies.All(p => p.Reachable != false);
            bool proxyBroken = !proxyReachable || (f.SystemProxyEnabled && f.HttpViaProxy != null && !f.HttpViaProxy.Success && ctx.DirectOk);
            if (f.SystemProxyEnabled && f.HttpViaProxy != null && proxyReachable)
            {
                if (!f.HttpViaProxy.Success && ctx.DirectOk)
                    checks.Add(new DiagnosisCheck(L, "代理效果", CheckStatus.Fail, "经系统代理无法访问外网，但直连正常：代理软件的节点或规则有问题",
                        "在代理软件中切换节点、更新订阅，或改为规则模式",
                        "暂时关闭系统代理即可恢复正常上网"));
                else if (f.HttpViaProxy.Success)
                    checks.Add(new DiagnosisCheck(L, "代理效果", CheckStatus.Ok, $"经系统代理访问正常（{f.HttpViaProxy.Milliseconds} ms）"));
            }

            if (f.VpnAdapters.Count > 0)
                checks.Add(new DiagnosisCheck(L, "VPN / 隧道网卡", CheckStatus.Info, string.Join("、", f.VpnAdapters)));
            if (f.RouteViaVpn)
                checks.Add(new DiagnosisCheck(L, "默认路由", CheckStatus.Info, "上网流量走 VPN / 隧道网卡（全局 VPN 或代理 TUN 模式），外网检测结果反映的是 VPN 线路"));

            bool fakeDnsServer = f.DnsServerResults.Any(s =>
            {
                IPAddress a;
                return IpValidator.TryParseIPv4(s.Key, out a) && IsFakeIp(a);
            });
            if ((ctx.FakeIp || fakeDnsServer) && !f.RouteViaVpn && f.VpnAdapters.Count == 0)
            {
                checks.Add(new DiagnosisCheck(L, "Fake-IP", CheckStatus.Fail, "DNS 指向代理软件的虚拟地址（198.18.x.x），但 TUN 网卡没有运行",
                    "重新打开代理软件并开启 TUN 模式",
                    "不用代理时，把网卡 DNS 改回自动获取"));
            }
            else if (ctx.FakeIp)
            {
                checks.Add(new DiagnosisCheck(L, "Fake-IP", CheckStatus.Info, "代理软件以 TUN / Fake-IP 模式接管了域名解析"));
            }

            if (f.ProxyPrograms.Count > 0)
            {
                string programs = string.Join("、", f.ProxyPrograms);
                bool unused = !f.SystemProxyEnabled && !f.RouteViaVpn && f.VpnAdapters.Count == 0;
                checks.Add(new DiagnosisCheck(L, "代理 / VPN 程序", CheckStatus.Info,
                    "正在运行：" + programs + (unused ? "；但未设置系统代理也未启用 TUN，浏览器等不会走代理" : "")));
            }

            HttpOutcome foreign = f.HttpsForeign;
            // 代理本身已失败时，国际网站打不开只是连带结果，不再重复提示
            if (foreign != null && ctx.InternetOk && !proxyBroken)
            {
                if (ctx.UsingProxy)
                    checks.Add(foreign.Success
                        ? new DiagnosisCheck(L, "国际网站", CheckStatus.Ok, $"可访问 {ForeignHost}（{foreign.Milliseconds} ms）")
                        : new DiagnosisCheck(L, "国际网站", CheckStatus.Warning, $"已使用代理 / VPN，但无法访问 {ForeignHost}",
                            "代理节点可能失效：切换节点或更新订阅",
                            "检查代理规则是否把该网站设成了直连"));
                else
                    checks.Add(new DiagnosisCheck(L, "国际网站", CheckStatus.Info, foreign.Success
                        ? $"可直接访问 {ForeignHost}"
                        : $"无法访问 {ForeignHost}（未使用代理，国内网络通常如此）"));
            }

            if (f.Proxies.Count == 0 && !ctx.UsingProxy && f.ProxyPrograms.Count == 0 && !ctx.FakeIp && !fakeDnsServer)
            {
                checks.Insert(before, new DiagnosisCheck(L, "代理 / VPN", CheckStatus.Ok, "未使用代理或 VPN"));
            }
        }

        #endregion

        #region UDP

        private static void AnalyzeUdp(DiagnosisFacts f, Context ctx, List<DiagnosisCheck> checks)
        {
            const DiagnosisLayer L = DiagnosisLayer.Udp;
            if (!ctx.InternetOk)
            {
                checks.Add(new DiagnosisCheck(L, "UDP", CheckStatus.Skipped, "外网不通，跳过"));
                return;
            }

            bool stunOk = f.StunMapped.Count > 0;
            if (!stunOk && f.PublicDnsUdpOk != true)
            {
                checks.Add(ctx.DirectOk
                    ? new DiagnosisCheck(L, "UDP", CheckStatus.Fail, "TCP 正常，但 UDP 没有任何应答（DNS 53、STUN 3478 端口），网络限制了 UDP", UdpBlockedTips)
                    : new DiagnosisCheck(L, "UDP", CheckStatus.Warning, "只能经代理上网，UDP 无法直连（HTTP 代理不转发 UDP）", UdpBlockedTips));
                return;
            }
            if (!stunOk)
            {
                checks.Add(new DiagnosisCheck(L, "UDP", CheckStatus.Warning,
                    f.StunServersTried == 0 ? "UDP 53（DNS）正常，未能测试其他 UDP 端口" : "只有 UDP 53（DNS）可用，其他 UDP 端口（STUN 3478）没有应答，网络可能只放行 DNS",
                    UdpBlockedTips));
                return;
            }

            checks.Add(new DiagnosisCheck(L, "UDP", CheckStatus.Ok, "STUN 服务器有应答，UDP 正常"));
            if (f.PublicDnsUdpOk == false)
            {
                checks.Add(new DiagnosisCheck(L, "UDP 53", CheckStatus.Warning, "其他 UDP 正常，但直接查询公共 DNS 没有应答，53 端口可能被拦截或劫持",
                    "使用路由器或运营商分配的 DNS"));
            }

            string ip1, ip2;
            int port1, port2;
            SplitEndpoint(f.StunMapped[0], out ip1, out port1);
            string exit = f.RouteViaVpn ? "（经 VPN 出口）" : "";
            if (f.LocalAddresses.Contains(ip1))
            {
                checks.Add(new DiagnosisCheck(L, "NAT", CheckStatus.Ok, $"本机直接使用公网 IP {ip1}，没有 NAT"));
            }
            else if (f.StunMapped.Count >= 2)
            {
                SplitEndpoint(f.StunMapped[1], out ip2, out port2);
                if (ip1 == ip2 && port1 == port2)
                {
                    checks.Add(new DiagnosisCheck(L, "NAT 类型", CheckStatus.Ok, $"公网地址 {ip1}{exit}，端口映射保持不变（锥形 NAT），P2P 一般可用"));
                }
                else
                {
                    string detail = ip1 != ip2
                        ? $"不同服务器看到的公网 IP 不同（{ip1} / {ip2}），存在多个出口，P2P 联机可能不稳定"
                        : $"公网地址 {ip1}{exit}，发往不同服务器时端口不同（{port1} / {port2}），属于对称型 NAT，P2P 联机、游戏语音可能连不上";
                    checks.Add(f.RouteViaVpn
                        ? new DiagnosisCheck(L, "NAT 类型", CheckStatus.Info, detail)
                        : new DiagnosisCheck(L, "NAT 类型", CheckStatus.Warning, detail,
                            "在路由器中开启 UPnP",
                            "为本机设置端口转发或 DMZ 主机",
                            "双重 NAT 或运营商级 NAT 时，需要光猫改桥接或向运营商申请公网 IP"));
                }
            }
            else
            {
                checks.Add(new DiagnosisCheck(L, "NAT", CheckStatus.Info, $"公网地址 {f.StunMapped[0]}{exit}"));
            }
        }

        private static void SplitEndpoint(string endpoint, out string ip, out int port)
        {
            int colon = endpoint.LastIndexOf(':');
            ip = colon < 0 ? endpoint : endpoint.Substring(0, colon);
            if (colon < 0 || !int.TryParse(endpoint.Substring(colon + 1), out port)) port = 0;
        }

        #endregion

        #region 地址分类

        /// <summary>
        /// 169.254.0.0/16：DHCP 失败时系统自动分配的地址
        /// </summary>
        public static bool IsApipa(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            return b.Length == 4 && b[0] == 169 && b[1] == 254;
        }

        /// <summary>
        /// 100.64.0.0/10：运营商级 NAT
        /// </summary>
        public static bool IsCgnat(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            return b.Length == 4 && b[0] == 100 && (b[1] & 0xC0) == 64;
        }

        /// <summary>
        /// 198.18.0.0/15：Clash、sing-box 等代理软件 Fake-IP 模式使用的地址
        /// </summary>
        public static bool IsFakeIp(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            return b.Length == 4 && b[0] == 198 && (b[1] == 18 || b[1] == 19);
        }

        public static bool IsFakeIp(string address)
        {
            IPAddress parsed;
            return IpValidator.TryParseIPv4(address, out parsed) && IsFakeIp(parsed);
        }

        /// <summary>
        /// RFC 1918 内网地址
        /// </summary>
        public static bool IsPrivateAddress(string address)
        {
            IPAddress parsed;
            if (!IpValidator.TryParseIPv4(address, out parsed)) return false;
            byte[] b = parsed.GetAddressBytes();
            return b[0] == 10 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 192 && b[1] == 168;
        }

        #endregion
    }
}
