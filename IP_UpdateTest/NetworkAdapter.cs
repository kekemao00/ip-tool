using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using IP_UpdateTest.Core;

namespace IP_UpdateTest
{
    /// <summary>
    /// 网卡连接状态
    /// </summary>
    public enum AdapterStatus
    {
        Unknown,
        Connected,
        Disconnected,
        Disabled
    }

    /// <summary>
    /// 网络适配器类
    /// </summary>
    public class NetworkAdapter
    {
        #region 标识与状态

        /// <summary>
        /// 网络适配器标识符，如：{274F9DD5-3650-4D59-B61E-710B6AF5AB36}
        /// </summary>
        public string NetworkInterfaceID { get; set; }

        /// <summary>
        /// 连接名称，如“以太网”“WLAN”
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 硬件描述，如“Intel(R) Wi-Fi 6E AX210 160MHz”
        /// </summary>
        public string InterfaceDescription { get; set; }

        /// <summary>
        /// 接口索引（netsh、路由表使用），未知时为 -1
        /// </summary>
        public int InterfaceIndex { get; set; } = -1;

        /// <summary>
        /// 网络接口类型
        /// </summary>
        public string NetworkInterfaceType { get; set; }

        /// <summary>
        /// 是否为物理网卡
        /// </summary>
        public bool IsPhysical { get; set; }

        /// <summary>
        /// 是否为虚拟网卡（Hyper-V、VPN、蓝牙等）
        /// </summary>
        public bool IsVirtual { get; set; }

        public AdapterStatus Status { get; set; }

        /// <summary>
        /// 连接速率（bps），未知或未连接时为 0
        /// </summary>
        public long LinkSpeedBps { get; set; }

        /// <summary>
        /// 是否启用DHCP服务
        /// </summary>
        public bool IsDhcpEnabled { get; set; }

        /// <summary>
        /// DHCP 租约获取时间（仅已连接的 DHCP 网卡）
        /// </summary>
        public DateTime? DhcpLeaseObtained { get; set; }

        /// <summary>
        /// DHCP 租约到期时间
        /// </summary>
        public DateTime? DhcpLeaseExpires { get; set; }

        /// <summary>
        /// 修改配置所用的方式，None 表示不可修改
        /// </summary>
        public BackendKind ConfigBackend { get; set; }

        /// <summary>
        /// 不可修改时的原因
        /// </summary>
        public string ReadOnlyReason { get; set; }

        /// <summary>
        /// 修改配置时需要提醒用户的信息，如“网卡未连接，配置会在连接后生效”
        /// </summary>
        public string ConfigNote { get; set; }

        public bool CanConfigure
        {
            get { return ConfigBackend == BackendKind.Wmi || ConfigBackend == BackendKind.Netsh; }
        }

        #endregion

        #region 地址集合

        /// <summary>
        /// IP地址集合（IPv4 与 IPv6 混在一起，顺序不固定）
        /// </summary>
        public UnicastIPAddressInformationCollection IPAddresses { get; set; }

        /// <summary>
        /// 网关地址集合
        /// </summary>
        public GatewayIPAddressInformationCollection Gateways { get; set; }

        /// <summary>
        /// DNS集合
        /// </summary>
        public IPAddressCollection DnsAddresses { get; set; }

        /// <summary>
        /// DHCP地址集合
        /// </summary>
        public IPAddressCollection DhcpServerAddresses { get; set; }

        /// <summary>
        /// 网卡MAC地址
        /// </summary>
        public PhysicalAddress MacAddress { get; set; }

        #endregion

        #region 已保存的配置（注册表）

        /// <summary>
        /// 保存的静态 IP。网卡未连接时系统不报告地址，用它显示已配置的内容
        /// </summary>
        public string ConfiguredIpAddress { get; set; }

        public string ConfiguredSubnetMask { get; set; }

        public string ConfiguredGateway { get; set; }

        /// <summary>
        /// 手动设置的 DNS；为空表示 DNS 自动获取
        /// </summary>
        public string[] ConfiguredDns { get; set; } = new string[0];

        /// <summary>
        /// DNS 是否为手动设置（DHCP 网卡也可能手动指定 DNS）
        /// </summary>
        public bool HasStaticDns
        {
            get { return ConfiguredDns != null && ConfiguredDns.Length > 0; }
        }

        #endregion

        #region 显示用属性

        /// <summary>
        /// IPv4 地址；网卡未连接时显示保存的静态 IP
        /// </summary>
        public string IpAddress
        {
            get
            {
                UnicastIPAddressInformation address = UseLiveAddresses ? GetIPv4Address() : null;
                if (address != null) return address.Address.ToString();
                return IsDhcpEnabled || UseLiveAddresses ? "" : ConfiguredIpAddress ?? "";
            }
        }

        /// <summary>
        /// 子网掩码
        /// </summary>
        public string SubnetMask
        {
            get
            {
                UnicastIPAddressInformation address = UseLiveAddresses ? GetIPv4Address() : null;
                if (address != null) return address.IPv4Mask == null ? "" : address.IPv4Mask.ToString();
                return IsDhcpEnabled || UseLiveAddresses ? "" : ConfiguredSubnetMask ?? "";
            }
        }

        /// <summary>
        /// 默认网关（IPv4）
        /// </summary>
        public string Gateway
        {
            get
            {
                // 双栈环境下网关集合里可能先出现 IPv6 链路本地地址（fe80::），只取 IPv4
                GatewayIPAddressInformation gateway = UseLiveAddresses ? Ipv4Selector.FirstIPv4(Gateways, g => g.Address) : null;
                if (gateway != null) return gateway.Address.ToString();
                return IsDhcpEnabled || UseLiveAddresses ? "" : ConfiguredGateway ?? "";
            }
        }

        /// <summary>
        /// 主DNS地址
        /// </summary>
        public string DnsMain
        {
            get
            {
                List<string> dns = GetDnsServers();
                return dns.Count > 0 ? dns[0] : "";
            }
        }

        /// <summary>
        /// 备用DNS地址
        /// </summary>
        public string DnsBackup
        {
            get
            {
                List<string> dns = GetDnsServers();
                return dns.Count > 1 ? dns[1] : "";
            }
        }

        /// <summary>
        /// DHCP服务器地址
        /// </summary>
        public string DhcpServer
        {
            get
            {
                if (!UseLiveAddresses) return "";
                List<IPAddress> servers = Ipv4Selector.IPv4Only(DhcpServerAddresses);
                return servers.Count > 0 ? servers[0].ToString() : "";
            }
        }

        /// <summary>
        /// MAC地址，如 74-56-3C-12-AB-CD
        /// </summary>
        public string MacAddressText
        {
            get { return FormatMac(MacAddress); }
        }

        /// <summary>
        /// IPv6 地址（不含作用域后缀）
        /// </summary>
        public List<string> IPv6Addresses
        {
            get
            {
                if (IPAddresses == null) return new List<string>();
                return IPAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(a => new IPAddress(a.Address.GetAddressBytes()).ToString())
                    .ToList();
            }
        }

        /// <summary>
        /// 下拉框中显示的名称，如“以太网 — Realtek Gaming 2.5GbE Family Controller”
        /// </summary>
        public string DisplayName
        {
            get
            {
                return string.IsNullOrEmpty(InterfaceDescription) || InterfaceDescription == Name
                    ? Name
                    : Name + " — " + InterfaceDescription;
            }
        }

        /// <summary>
        /// 状态说明，如“已连接 · 1 Gbps”“未连接 · 虚拟网卡”
        /// </summary>
        public string StatusText
        {
            get
            {
                var parts = new List<string>();
                switch (Status)
                {
                    case AdapterStatus.Connected:
                        parts.Add("已连接");
                        if (LinkSpeedBps > 0) parts.Add(SpeedText);
                        break;
                    case AdapterStatus.Disconnected:
                        parts.Add("未连接");
                        break;
                    case AdapterStatus.Disabled:
                        parts.Add("已禁用");
                        break;
                    default:
                        parts.Add("状态未知");
                        break;
                }
                if (IsVirtual) parts.Add("虚拟网卡");
                return string.Join(" · ", parts);
            }
        }

        /// <summary>
        /// 速率，如“1 Gbps”“100 Mbps”
        /// </summary>
        public string SpeedText
        {
            get { return FormatSpeed(LinkSpeedBps); }
        }

        public override string ToString()
        {
            return DisplayName;
        }

        #endregion

        #region 格式化

        public static string FormatSpeed(long bitsPerSecond)
        {
            if (bitsPerSecond <= 0) return "-";
            if (bitsPerSecond >= 1000000000) return (bitsPerSecond / 1e9).ToString("0.#") + " Gbps";
            if (bitsPerSecond >= 1000000) return (bitsPerSecond / 1e6).ToString("0.#") + " Mbps";
            return (bitsPerSecond / 1e3).ToString("0.#") + " Kbps";
        }

        public static string FormatMac(PhysicalAddress mac)
        {
            if (mac == null) return "";
            byte[] bytes = mac.GetAddressBytes();
            return bytes.Length == 0 ? "" : string.Join("-", bytes.Select(b => b.ToString("X2")));
        }

        #endregion

        /// <summary>
        /// 得到IPV4地址
        /// </summary>
        private UnicastIPAddressInformation GetIPv4Address()
        {
            // 地址集合里混有 IPv6 且顺序不固定，按地址族筛选而不是按下标猜
            return Ipv4Selector.FirstIPv4(IPAddresses, a => a.Address);
        }

        /// <summary>
        /// 网卡未连接时，系统仍会报告自动配置地址（169.254.x.x）和上次租约的 DNS，
        /// 没有参考意义，改为显示保存的配置
        /// </summary>
        private bool UseLiveAddresses
        {
            get { return Status == AdapterStatus.Connected; }
        }

        /// <summary>
        /// 生效中的 IPv4 DNS；网卡未连接时取保存的手动 DNS
        /// </summary>
        private List<string> GetDnsServers()
        {
            List<string> live = UseLiveAddresses
                ? Ipv4Selector.IPv4Only(DnsAddresses).Select(d => d.ToString()).ToList()
                : new List<string>();
            return live.Count > 0 ? live : (ConfiguredDns ?? new string[0]).ToList();
        }
    }
}
