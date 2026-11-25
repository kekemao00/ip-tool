using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 网卡上手动设置的一个 IPv6 地址
    /// </summary>
    public sealed class Ipv6ManualAddress
    {
        public Ipv6ManualAddress(string address, int prefixLength)
        {
            Address = address;
            PrefixLength = prefixLength;
        }

        public string Address { get; private set; }

        public int PrefixLength { get; private set; }
    }

    /// <summary>
    /// 要应用到网卡的 IPv6 配置，与 Windows“Internet 协议版本 6 (TCP/IPv6) 属性”一致：
    /// 手动地址和默认网关加在路由器通告（SLAAC）、DHCPv6 得到的配置之外，不会关掉自动配置；
    /// 地址与 DNS 互不影响，手动地址也可以用自动获取的 DNS
    /// </summary>
    public sealed class Ipv6ConfigRequest
    {
        /// <summary>
        /// 地址自动获取：删除手动设置的地址和默认网关
        /// </summary>
        public bool UseAuto { get; set; }

        public string Address { get; set; }

        /// <summary>
        /// 前缀长度（用户输入的文本，校验时解析）
        /// </summary>
        public string PrefixLength { get; set; }

        /// <summary>
        /// 默认网关，空表示不设
        /// </summary>
        public string Gateway { get; set; }

        public bool UseAutoDns { get; set; }

        /// <summary>
        /// 手动 DNS，按优先级排列
        /// </summary>
        public string[] DnsServers { get; set; } = new string[0];

        public int ParsedPrefixLength
        {
            get
            {
                int prefix;
                return int.TryParse((PrefixLength ?? "").Trim(), out prefix) ? prefix : -1;
            }
        }

        /// <summary>
        /// 校验配置，返回全部错误（为空表示通过）
        /// </summary>
        public List<ValidationError> Validate()
        {
            var errors = UseAuto ? new List<ValidationError>() : Ipv6Validator.ValidateStatic(Address, PrefixLength, Gateway);
            if (!UseAutoDns)
            {
                string dnsMain = DnsServers.Length > 0 ? DnsServers[0] : "";
                string dnsBackup = DnsServers.Length > 1 ? DnsServers[1] : "";
                errors.AddRange(Ipv6Validator.ValidateDns(dnsMain, dnsBackup));
            }
            return errors;
        }
    }

    /// <summary>
    /// IPv6 配置校验
    /// </summary>
    public static class Ipv6Validator
    {
        public const int DefaultPrefixLength = 64;

        /// <summary>
        /// 解析 IPv6 地址，去掉作用域（ipconfig 显示的 fe80::1%12 中的 %12）
        /// </summary>
        public static bool TryParse(string text, out IPAddress address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string value = text.Trim();
            int zone = value.IndexOf('%');
            if (zone >= 0) value = value.Substring(0, zone);

            IPAddress parsed;
            // IPAddress.TryParse 会接受并忽略“/64”这样的前缀长度
            if (value.IndexOf(':') < 0 || value.IndexOf('/') >= 0
                || !IPAddress.TryParse(value, out parsed) || parsed.AddressFamily != AddressFamily.InterNetworkV6)
                return false;
            address = new IPAddress(parsed.GetAddressBytes());
            return true;
        }

        /// <summary>
        /// 解析“2001:db8::10/64”形式的地址
        /// </summary>
        public static bool TryParseCidr(string text, out IPAddress address, out int prefix)
        {
            address = null;
            prefix = -1;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Trim().Split('/');
            if (parts.Length != 2 || !TryParse(parts[0], out address)) return false;
            if (!int.TryParse(parts[1].Trim(), out prefix) || prefix < 1 || prefix > 128)
            {
                address = null;
                prefix = -1;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 规范写法（小写、压缩零），无法解析时原样返回
        /// </summary>
        public static string Normalize(string text)
        {
            IPAddress address;
            return TryParse(text, out address) ? address.ToString() : (text ?? "").Trim();
        }

        public static bool IsLinkLocal(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            return b[0] == 0xfe && (b[1] & 0xc0) == 0x80;
        }

        /// <summary>
        /// 取前 prefix 位得到的网段地址，如 2001:db8::10 /64 → 2001:db8::
        /// </summary>
        public static IPAddress Network(IPAddress address, int prefix)
        {
            byte[] bytes = address.GetAddressBytes();
            for (int i = 0; i < bytes.Length; i++)
            {
                int bits = prefix - i * 8;
                bytes[i] = bits >= 8 ? bytes[i] : bits <= 0 ? (byte)0 : (byte)(bytes[i] & (0xff << (8 - bits)));
            }
            return new IPAddress(bytes);
        }

        public static bool IsSamePrefix(IPAddress a, IPAddress b, int prefix)
        {
            return Network(a, prefix).Equals(Network(b, prefix));
        }

        /// <summary>
        /// 校验手动地址、前缀长度和网关
        /// </summary>
        public static List<ValidationError> ValidateStatic(string address, string prefixLength, string gateway)
        {
            var errors = new List<ValidationError>();

            IPAddress ip = null;
            if (string.IsNullOrWhiteSpace(address))
                errors.Add(new ValidationError(IpField.IpAddress, "请填写 IPv6 地址"));
            else if (!TryParse(address, out ip))
                errors.Add(new ValidationError(IpField.IpAddress, "IPv6 地址格式不正确，如 2001:db8::10"));
            else
            {
                string reserved = DescribeReserved(ip);
                if (reserved != null)
                {
                    errors.Add(new ValidationError(IpField.IpAddress, reserved));
                    ip = null;
                }
            }

            int prefix;
            if (string.IsNullOrWhiteSpace(prefixLength))
                errors.Add(new ValidationError(IpField.SubnetMask, "请填写前缀长度，通常为 64"));
            else if (!int.TryParse(prefixLength.Trim(), out prefix) || prefix < 1 || prefix > 128)
                errors.Add(new ValidationError(IpField.SubnetMask, "前缀长度应为 1–128 的整数，通常为 64"));

            if (!string.IsNullOrWhiteSpace(gateway))
            {
                IPAddress gw;
                int validPrefix;
                bool prefixOk = int.TryParse((prefixLength ?? "").Trim(), out validPrefix) && validPrefix >= 1 && validPrefix <= 128;
                if (!TryParse(gateway, out gw))
                    errors.Add(new ValidationError(IpField.Gateway, "网关地址格式不正确，如 fe80::1"));
                else if (gw.Equals(IPAddress.IPv6Any) || gw.Equals(IPAddress.IPv6Loopback) || gw.IsIPv6Multicast)
                    errors.Add(new ValidationError(IpField.Gateway, "网关地址无效"));
                else if (ip != null && gw.Equals(ip))
                    errors.Add(new ValidationError(IpField.Gateway, "网关不能与 IPv6 地址相同"));
                else if (ip != null && prefixOk && !IsLinkLocal(gw) && !IsSamePrefix(ip, gw, validPrefix))
                    errors.Add(new ValidationError(IpField.Gateway,
                        $"网关应为链路本地地址（fe80 开头），或在 {Network(ip, validPrefix)}/{validPrefix} 网段内"));
            }
            return errors;
        }

        /// <summary>
        /// 校验手动 DNS：首选必填
        /// </summary>
        public static List<ValidationError> ValidateDns(string dnsMain, string dnsBackup)
        {
            var errors = new List<ValidationError>();
            string main = string.IsNullOrWhiteSpace(dnsMain) ? "请填写首选 DNS" : DnsError(dnsMain, "首选");
            if (main != null) errors.Add(new ValidationError(IpField.DnsMain, main));

            string backup = string.IsNullOrWhiteSpace(dnsBackup) ? null : DnsError(dnsBackup, "备用");
            if (backup != null) errors.Add(new ValidationError(IpField.DnsBackup, backup));
            return errors;
        }

        /// <summary>
        /// 由首选、备用 DNS 组成列表，忽略空值，统一为规范写法
        /// </summary>
        public static string[] DnsList(string dnsMain, string dnsBackup)
        {
            return IpConfigRequest.DnsList(dnsMain, dnsBackup).Select(Normalize).ToArray();
        }

        private static string DnsError(string text, string which)
        {
            IPAddress address;
            if (TryParse(text, out address))
                return address.Equals(IPAddress.IPv6Any) || address.IsIPv6Multicast ? which + " DNS 地址无效" : null;
            IPAddress v4;
            return IpValidator.TryParseIPv4(text, out v4)
                ? which + " DNS 应为 IPv6 地址；IPv4 的 DNS 请在“IPv4”页设置"
                : which + " DNS 格式不正确，如 2400:3200::1";
        }

        /// <summary>
        /// 不能手动设置的地址，返回原因；可用时返回 null
        /// </summary>
        private static string DescribeReserved(IPAddress ip)
        {
            if (ip.Equals(IPAddress.IPv6Any)) return "不能使用未指定地址（::）";
            if (ip.Equals(IPAddress.IPv6Loopback)) return "不能使用回环地址（::1）";
            if (ip.IsIPv6Multicast) return "不能使用组播地址（ff 开头）";
            if (IsLinkLocal(ip)) return "链路本地地址（fe80 开头）由系统自动生成，请填写全局地址或唯一本地地址（fd 开头）";
            if (ip.IsIPv4MappedToIPv6) return "不能使用 IPv4 映射地址（::ffff: 开头）";
            return null;
        }
    }
}
