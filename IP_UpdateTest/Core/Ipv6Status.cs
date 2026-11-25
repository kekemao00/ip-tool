using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// IPv6 地址的类别，与 ipconfig 的分法一致；枚举顺序即显示顺序
    /// </summary>
    public enum Ipv6AddressKind
    {
        Manual,
        Dhcp,
        Global,
        UniqueLocal,
        Temporary,
        LinkLocal
    }

    /// <summary>
    /// 系统报告的一个 IPv6 地址（已分类）
    /// </summary>
    public sealed class Ipv6AddressInfo
    {
        public Ipv6AddressInfo(string address, int prefixLength, Ipv6AddressKind kind,
            DuplicateAddressDetectionState state = DuplicateAddressDetectionState.Preferred)
        {
            Address = address;
            PrefixLength = prefixLength;
            Kind = kind;
            State = state;
        }

        /// <summary>
        /// 规范写法，不含作用域（%12）
        /// </summary>
        public string Address { get; private set; }

        public int PrefixLength { get; private set; }

        public Ipv6AddressKind Kind { get; private set; }

        /// <summary>
        /// 重复地址检测（DAD）的状态：Duplicate 表示与网段内其他设备冲突，Deprecated 表示即将停用
        /// </summary>
        public DuplicateAddressDetectionState State { get; private set; }

        public bool IsDuplicate
        {
            get { return State == DuplicateAddressDetectionState.Duplicate; }
        }
    }

    /// <summary>
    /// 状态列表中的一行：类别、地址，以及悬停时的说明
    /// </summary>
    public sealed class Ipv6StatusRow
    {
        public Ipv6StatusRow(string caption, string value, string detail)
        {
            Caption = caption;
            Value = value;
            Detail = detail;
        }

        public string Caption { get; private set; }
        public string Value { get; private set; }
        public string Detail { get; private set; }
    }

    /// <summary>
    /// IPv6 页“自动获取”时显示的状态：地址列表，以及没有可用地址等情况的说明
    /// </summary>
    public sealed class Ipv6StatusSummary
    {
        public List<Ipv6StatusRow> Rows { get; } = new List<Ipv6StatusRow>();

        /// <summary>
        /// 列表下方的说明，没有时为 null
        /// </summary>
        public string Notice { get; set; }
    }

    /// <summary>
    /// IPv6 地址的分类与显示。一块网卡常同时有公网、临时、内网、本地链接等多个地址，都在生效，
    /// 不能像 IPv4 那样只显示一个
    /// </summary>
    public static class Ipv6Status
    {
        public const string NoIpv6Notice = "没有获取到 IPv6 地址：网络里没有路由器分配 IPv6，只能在本网段内通信。"
            + "常见于路由器未开启 IPv6、虚拟机的 NAT 网络，需要时可改为手动设置。";

        public const string NotEnabledNotice = "该网卡未启用 IPv6：在网卡属性中勾选“Internet 协议版本 6 (TCP/IPv6)”后才能使用。";

        public const string DisconnectedNotice = "网卡未连接：连接后这里显示自动获取的 IPv6 地址。";

        public const string DisabledNotice = "网卡已禁用：启用后才能使用 IPv6。";

        /// <summary>
        /// 按系统报告的来源分类：临时地址的后缀是随机生成的；fd 开头的是只在内网使用的唯一本地地址
        /// </summary>
        public static Ipv6AddressKind Classify(IPAddress address, PrefixOrigin prefixOrigin, SuffixOrigin suffixOrigin)
        {
            if (Ipv6Validator.IsLinkLocal(address)) return Ipv6AddressKind.LinkLocal;
            if (prefixOrigin == PrefixOrigin.Manual) return Ipv6AddressKind.Manual;
            if (suffixOrigin == SuffixOrigin.Random) return Ipv6AddressKind.Temporary;
            if (prefixOrigin == PrefixOrigin.Dhcp) return Ipv6AddressKind.Dhcp;
            return IsUniqueLocal(address) ? Ipv6AddressKind.UniqueLocal : Ipv6AddressKind.Global;
        }

        public static bool IsUniqueLocal(IPAddress address)
        {
            return (address.GetAddressBytes()[0] & 0xfe) == 0xfc;
        }

        /// <summary>
        /// 由系统报告的地址生成列表（去掉回环和 Teredo 隧道地址）
        /// </summary>
        public static List<Ipv6AddressInfo> FromSystem(IEnumerable<UnicastIPAddressInformation> addresses)
        {
            var list = new List<Ipv6AddressInfo>();
            if (addresses == null) return list;
            foreach (UnicastIPAddressInformation a in addresses)
            {
                if (a.Address.AddressFamily != AddressFamily.InterNetworkV6 || IPAddress.IsLoopback(a.Address) || a.Address.IsIPv6Teredo) continue;
                var address = new IPAddress(a.Address.GetAddressBytes());
                list.Add(new Ipv6AddressInfo(address.ToString(), a.PrefixLength, Classify(address, a.PrefixOrigin, a.SuffixOrigin),
                    a.DuplicateAddressDetectionState));
            }
            return list;
        }

        public static string NameOf(Ipv6AddressInfo address)
        {
            if (address.IsDuplicate) return "地址冲突";
            switch (address.Kind)
            {
                case Ipv6AddressKind.Manual: return "手动地址";
                case Ipv6AddressKind.Dhcp: return "DHCPv6";
                case Ipv6AddressKind.Global: return "公网地址";
                case Ipv6AddressKind.UniqueLocal: return "内网地址";
                case Ipv6AddressKind.Temporary: return "临时地址";
                default: return "本地链接";
            }
        }

        /// <summary>
        /// 左侧网卡卡片里用的两字简称
        /// </summary>
        public static string ShortNameOf(Ipv6AddressInfo address)
        {
            if (address.IsDuplicate) return "冲突";
            switch (address.Kind)
            {
                case Ipv6AddressKind.Manual: return "手动";
                case Ipv6AddressKind.Dhcp: return "DHCP";
                case Ipv6AddressKind.Global: return "公网";
                case Ipv6AddressKind.UniqueLocal: return "内网";
                case Ipv6AddressKind.Temporary: return "临时";
                default: return "本地";
            }
        }

        public static string DescribeKind(Ipv6AddressInfo address)
        {
            if (address.IsDuplicate) return "与网段内其他设备冲突（重复地址检测失败），系统不会使用它";
            switch (address.Kind)
            {
                case Ipv6AddressKind.Manual: return "手动设置的地址";
                case Ipv6AddressKind.Dhcp: return "DHCPv6 服务器分配的地址";
                case Ipv6AddressKind.Global: return "路由器分配的公网地址（SLAAC）";
                case Ipv6AddressKind.UniqueLocal: return "唯一本地地址：只在内网使用，相当于 IPv4 的 192.168.x.x";
                case Ipv6AddressKind.Temporary: return "临时地址：系统定期更换，主动访问外网时优先使用，保护隐私";
                default: return "本地链接地址：系统自动生成，只能与同一网段的设备通信";
            }
        }

        /// <summary>
        /// 显示用的地址：按类别排序（同类保持系统顺序），去掉即将停用的；
        /// 临时地址可能有好几个，只留第一个，otherTemporary 为其余的个数
        /// </summary>
        public static List<Ipv6AddressInfo> ForDisplay(IEnumerable<Ipv6AddressInfo> addresses, out int otherTemporary)
        {
            List<Ipv6AddressInfo> ordered = (addresses ?? Enumerable.Empty<Ipv6AddressInfo>())
                .Where(a => a.State != DuplicateAddressDetectionState.Deprecated)
                .Select((a, i) => new { Address = a, Index = i })
                .OrderBy(x => (int)x.Address.Kind)
                .ThenBy(x => x.Index)
                .Select(x => x.Address)
                .ToList();
            Ipv6AddressInfo firstTemporary = ordered.FirstOrDefault(a => a.Kind == Ipv6AddressKind.Temporary);
            otherTemporary = ordered.Count(a => a.Kind == Ipv6AddressKind.Temporary) - (firstTemporary == null ? 0 : 1);
            return ordered.Where(a => a.Kind != Ipv6AddressKind.Temporary || a == firstTemporary).ToList();
        }

        /// <summary>
        /// 有能访问本网段以外的地址（不算本地链接和冲突的地址）
        /// </summary>
        public static bool HasRoutableAddress(IEnumerable<Ipv6AddressInfo> addresses)
        {
            return addresses.Any(a => a.Kind != Ipv6AddressKind.LinkLocal && !a.IsDuplicate);
        }

        /// <summary>
        /// “自动获取”时的状态：最多 maxRows 行（有可用地址时最后一行是默认网关），没有可用地址等情况给出说明
        /// </summary>
        public static Ipv6StatusSummary Describe(NetworkAdapter adapter, int maxRows)
        {
            var summary = new Ipv6StatusSummary();
            if (adapter == null) return summary;
            if (adapter.Status == AdapterStatus.Disabled)
            {
                summary.Notice = DisabledNotice;
                return summary;
            }
            if (!adapter.Ipv6Enabled)
            {
                summary.Notice = NotEnabledNotice;
                return summary;
            }
            if (adapter.Status != AdapterStatus.Connected)
            {
                summary.Notice = DisconnectedNotice;
                return summary;
            }

            int otherTemporary;
            List<Ipv6AddressInfo> addresses = ForDisplay(adapter.Ipv6LiveAddresses, out otherTemporary);
            bool routable = HasRoutableAddress(addresses);
            string gateway = adapter.Ipv6LiveGateways == null ? null : adapter.Ipv6LiveGateways.FirstOrDefault();
            bool gatewayRow = routable || gateway != null;

            foreach (Ipv6AddressInfo a in addresses.Take(maxRows - (gatewayRow ? 1 : 0)))
            {
                string detail = $"{a.Address}/{a.PrefixLength}\n{DescribeKind(a)}";
                if (a.Kind == Ipv6AddressKind.Temporary && otherTemporary > 0) detail += $"\n另有 {otherTemporary} 个临时地址";
                summary.Rows.Add(new Ipv6StatusRow(NameOf(a), a.Address, detail));
            }
            if (gatewayRow)
            {
                summary.Rows.Add(gateway != null
                    ? new Ipv6StatusRow("默认网关", gateway, "路由器通告的默认网关")
                    : new Ipv6StatusRow("默认网关", "无", "没有收到路由器通告：只能访问本网段内的 IPv6 地址"));
            }
            if (!routable) summary.Notice = NoIpv6Notice;
            return summary;
        }
    }
}
