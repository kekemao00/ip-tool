using System.Collections.Generic;
using System.Linq;

namespace IP_UpdateTest.Core.Diagnosis
{
    /// <summary>
    /// 连续 ping 的统计
    /// </summary>
    public sealed class PingStats
    {
        public string Target { get; set; }
        public int Sent { get; set; }
        public int Received { get; set; }
        public long AverageMs { get; set; }
        public long MaxMs { get; set; }

        public bool AnyReply
        {
            get { return Received > 0; }
        }

        public int LossPercent
        {
            get { return Sent == 0 ? 0 : (Sent - Received) * 100 / Sent; }
        }

        /// <summary>
        /// 由每次的往返时间（null 表示未收到应答）汇总
        /// </summary>
        public static PingStats From(string target, IList<long?> roundtrips)
        {
            List<long> replies = roundtrips.Where(r => r.HasValue).Select(r => r.Value).ToList();
            return new PingStats
            {
                Target = target,
                Sent = roundtrips.Count,
                Received = replies.Count,
                AverageMs = replies.Count == 0 ? 0 : (long)replies.Average(),
                MaxMs = replies.Count == 0 ? 0 : replies.Max()
            };
        }

        public string Describe()
        {
            if (Sent == 0) return "未测试";
            if (Received == 0) return $"{Sent} 次均无响应";
            string loss = Received < Sent ? $"，丢包 {LossPercent}%" : "";
            return $"平均 {AverageMs} ms，最大 {MaxMs} ms{loss}";
        }
    }

    /// <summary>
    /// 一次 HTTP(S) 访问的结果
    /// </summary>
    public sealed class HttpOutcome
    {
        public bool Success { get; set; }
        public long Milliseconds { get; set; }
        public string Message { get; set; }

        /// <summary>
        /// 被重定向或内容不对，通常是需要网页认证的网络
        /// </summary>
        public bool NeedsLogin { get; set; }

        /// <summary>
        /// HTTPS 证书校验失败（HTTPS 被拦截或系统时间错误）
        /// </summary>
        public bool CertificateError { get; set; }
    }

    /// <summary>
    /// 一个代理设置及其可达性
    /// </summary>
    public sealed class ProxyFact
    {
        /// <summary>
        /// 来源说明，如“系统代理”“PAC 脚本”“WinHTTP 代理”“环境变量 HTTP_PROXY”
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// 设置的原文
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// 代理服务器地址 host:port；无法解析出地址时为空
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// 能否建立 TCP 连接；未测试为 null
        /// </summary>
        public bool? Reachable { get; set; }
    }

    /// <summary>
    /// 诊断时采集到的原始数据。采集（NetworkDiagnosis）与分析（DiagnosisAnalyzer）分开，分析部分是纯函数，便于测试。
    /// 未测到的项保持默认值（null / 空）。
    /// </summary>
    public sealed class DiagnosisFacts
    {
        #region 本机网卡

        public bool AdapterFound { get; set; }
        public string AdapterName { get; set; }
        public string AdapterDescription { get; set; }
        public bool AdapterConnected { get; set; }
        public bool IsWireless { get; set; }
        public long LinkSpeedBps { get; set; }
        public bool Dhcp { get; set; }
        public string IpAddress { get; set; }
        public string SubnetMask { get; set; }
        public string Gateway { get; set; }
        public string[] DnsServers { get; set; } = new string[0];

        /// <summary>
        /// 默认路由（去往外网）所在的网卡名称；与诊断的网卡不同时说明上网走的是别的网卡
        /// </summary>
        public string RouteAdapterName { get; set; }

        /// <summary>
        /// 默认路由是否就在诊断的网卡上
        /// </summary>
        public bool RouteViaAdapter { get; set; } = true;

        /// <summary>
        /// 其他同样配置了默认网关的已连接网卡（不含 VPN）
        /// </summary>
        public List<string> OtherGatewayAdapters { get; } = new List<string>();

        /// <summary>
        /// Wi-Fi 信号强度（%），非无线网卡或读取失败为 null
        /// </summary>
        public int? WifiSignalPercent { get; set; }

        #endregion

        #region 路由器

        public PingStats GatewayPing { get; set; }

        /// <summary>
        /// 通过 ARP 查到的网关 MAC；查不到为 null
        /// </summary>
        public string GatewayMac { get; set; }

        /// <summary>
        /// 去往外网的前几跳地址（逐跳 TTL 探测），某跳无响应为 null
        /// </summary>
        public List<string> Hops { get; } = new List<string>();

        #endregion

        #region 外网

        public PingStats PublicPing { get; set; }

        /// <summary>
        /// 直连公网 IP 的 TCP 443 端口（不经过系统代理）
        /// </summary>
        public bool? PublicTcpOk { get; set; }

        /// <summary>
        /// Windows 连通性检测地址，直连（不经过系统代理）
        /// </summary>
        public HttpOutcome HttpDirect { get; set; }

        /// <summary>
        /// 国内 HTTPS 网站，按系统代理设置访问（与浏览器一致）
        /// </summary>
        public HttpOutcome HttpsDomestic { get; set; }

        /// <summary>
        /// 路径 MTU；未测到为 null
        /// </summary>
        public int? PathMtu { get; set; }

        #endregion

        #region DNS

        /// <summary>
        /// 各 DNS 服务器直接查询的耗时，无响应为 null
        /// </summary>
        public List<KeyValuePair<string, long?>> DnsServerResults { get; } = new List<KeyValuePair<string, long?>>();

        public bool SystemResolveOk { get; set; }
        public string SystemResolveAddress { get; set; }
        public long SystemResolveMs { get; set; }
        public string SystemResolveError { get; set; }

        /// <summary>
        /// 解析一个不存在的域名得到的地址；正常应为 null（域名不存在）
        /// </summary>
        public string NxDomainAnswer { get; set; }

        /// <summary>
        /// hosts 文件中的自定义解析条数
        /// </summary>
        public int HostsEntries { get; set; }

        #endregion

        #region 代理 / VPN

        public List<ProxyFact> Proxies { get; } = new List<ProxyFact>();

        /// <summary>
        /// 系统代理（含 PAC）是否启用；浏览器等按此访问
        /// </summary>
        public bool SystemProxyEnabled { get; set; }

        /// <summary>
        /// 经系统代理访问 Windows 连通性检测地址；未启用系统代理时为 null
        /// </summary>
        public HttpOutcome HttpViaProxy { get; set; }

        /// <summary>
        /// 已连接的 VPN / 虚拟隧道网卡
        /// </summary>
        public List<string> VpnAdapters { get; } = new List<string>();

        /// <summary>
        /// 默认路由走 VPN / 隧道网卡（全局 VPN 或代理软件 TUN 模式）
        /// </summary>
        public bool RouteViaVpn { get; set; }

        /// <summary>
        /// 正在运行的代理 / VPN 程序
        /// </summary>
        public List<string> ProxyPrograms { get; } = new List<string>();

        /// <summary>
        /// 访问国际网站（按系统代理设置）；未测试为 null
        /// </summary>
        public HttpOutcome HttpsForeign { get; set; }

        #endregion

        #region UDP

        /// <summary>
        /// 直接向公共 DNS 发 UDP 53 查询是否有应答；未测试为 null
        /// </summary>
        public bool? PublicDnsUdpOk { get; set; }

        /// <summary>
        /// STUN 查询到的公网映射地址（ip:port），按服务器顺序；无应答的不计入
        /// </summary>
        public List<string> StunMapped { get; } = new List<string>();

        /// <summary>
        /// 发出 STUN 查询的服务器数量
        /// </summary>
        public int StunServersTried { get; set; }

        /// <summary>
        /// 本机所有 IPv4 地址，用来判断 STUN 映射地址是否就是本机（无 NAT）
        /// </summary>
        public List<string> LocalAddresses { get; } = new List<string>();

        #endregion
    }
}
