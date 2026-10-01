using System;
using System.Collections.Generic;
using System.Net;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 表单中可能出错的字段
    /// </summary>
    public enum IpField
    {
        IpAddress,
        SubnetMask,
        Gateway,
        DnsMain,
        DnsBackup
    }

    /// <summary>
    /// 字段级校验错误
    /// </summary>
    public sealed class ValidationError
    {
        public ValidationError(IpField field, string message)
        {
            Field = field;
            Message = message;
        }

        public IpField Field { get; private set; }

        public string Message { get; private set; }
    }

    /// <summary>
    /// IPv4 配置校验
    /// </summary>
    public static class IpValidator
    {
        /// <summary>
        /// 严格解析点分十进制 IPv4：必须 4 段、每段 0–255、不允许前导 0。
        /// IPAddress.TryParse 会把 "010" 当八进制、接受 "1.2.3" 这类简写，不适合校验用户输入。
        /// </summary>
        public static bool TryParseIPv4(string text, out IPAddress address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Trim().Split('.');
            if (parts.Length != 4) return false;

            var bytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part.Length > 3) return false;
                if (part.Length > 1 && part[0] == '0') return false;

                int value = 0;
                foreach (char c in part)
                {
                    if (c < '0' || c > '9') return false;
                    value = value * 10 + (c - '0');
                }
                if (value > 255) return false;
                bytes[i] = (byte)value;
            }

            address = new IPAddress(bytes);
            return true;
        }

        /// <summary>
        /// 解析 “192.168.1.10/24” 形式的地址
        /// </summary>
        public static bool TryParseCidr(string text, out IPAddress address, out int prefix)
        {
            address = null;
            prefix = -1;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Trim().Split('/');
            if (parts.Length != 2) return false;
            if (!TryParseIPv4(parts[0], out address)) return false;
            if (!int.TryParse(parts[1].Trim(), out prefix) || prefix < 1 || prefix > 32)
            {
                address = null;
                prefix = -1;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 掩码转前缀长度，如 255.255.255.0 → 24；不是连续掩码时返回 -1
        /// </summary>
        public static int MaskToPrefix(IPAddress mask)
        {
            uint value = ToUInt32(mask);
            int prefix = 0;
            while (prefix < 32 && (value & (0x80000000u >> prefix)) != 0) prefix++;
            return value == PrefixToUInt32(prefix) ? prefix : -1;
        }

        /// <summary>
        /// 前缀长度转掩码，如 24 → 255.255.255.0
        /// </summary>
        public static IPAddress PrefixToMask(int prefix)
        {
            if (prefix < 0 || prefix > 32) throw new ArgumentOutOfRangeException(nameof(prefix));
            return FromUInt32(PrefixToUInt32(prefix));
        }

        /// <summary>
        /// 两个地址在给定掩码下是否属于同一网段
        /// </summary>
        public static bool IsSameSubnet(IPAddress a, IPAddress b, IPAddress mask)
        {
            uint m = ToUInt32(mask);
            return (ToUInt32(a) & m) == (ToUInt32(b) & m);
        }

        /// <summary>
        /// 校验静态 IP 配置，返回全部错误（为空表示通过）
        /// </summary>
        public static List<ValidationError> ValidateStatic(string ipAddress, string subnetMask, string gateway, string dnsMain, string dnsBackup)
        {
            var errors = new List<ValidationError>();

            IPAddress ip = null;
            if (string.IsNullOrWhiteSpace(ipAddress))
                errors.Add(new ValidationError(IpField.IpAddress, "请填写 IP 地址"));
            else if (!TryParseIPv4(ipAddress, out ip))
                errors.Add(new ValidationError(IpField.IpAddress, "IP 地址格式不正确，应为 4 段 0–255 的数字，如 192.168.1.10"));
            else
            {
                string reserved = DescribeReserved(ip);
                if (reserved != null)
                {
                    errors.Add(new ValidationError(IpField.IpAddress, reserved));
                    ip = null;
                }
            }

            IPAddress mask = null;
            int prefix = -1;
            if (string.IsNullOrWhiteSpace(subnetMask))
                errors.Add(new ValidationError(IpField.SubnetMask, "请填写子网掩码"));
            else if (!TryParseIPv4(subnetMask, out mask) || (prefix = MaskToPrefix(mask)) < 1)
            {
                errors.Add(new ValidationError(IpField.SubnetMask, "子网掩码无效，应为连续的 1 加连续的 0，如 255.255.255.0"));
                mask = null;
            }

            // /31、/32 没有网段地址和广播地址之分
            if (ip != null && mask != null && prefix <= 30)
            {
                uint hostBits = ~ToUInt32(mask);
                uint host = ToUInt32(ip) & hostBits;
                if (host == 0)
                    errors.Add(new ValidationError(IpField.IpAddress, "IP 地址不能是网段地址（主机位全为 0）"));
                else if (host == hostBits)
                    errors.Add(new ValidationError(IpField.IpAddress, "IP 地址不能是广播地址（主机位全为 1）"));
            }

            if (!string.IsNullOrWhiteSpace(gateway))
            {
                IPAddress gw;
                if (!TryParseIPv4(gateway, out gw))
                    errors.Add(new ValidationError(IpField.Gateway, "网关地址格式不正确"));
                else if (ip != null && gw.Equals(ip))
                    errors.Add(new ValidationError(IpField.Gateway, "网关不能与 IP 地址相同"));
                else if (ip != null && mask != null && !IsSameSubnet(ip, gw, mask))
                    errors.Add(new ValidationError(IpField.Gateway,
                        $"网关不在 IP 地址所在网段 {FromUInt32(ToUInt32(ip) & ToUInt32(mask))}/{prefix} 内"));
            }

            errors.AddRange(ValidateDns(dnsMain, dnsBackup, false));
            return errors;
        }

        /// <summary>
        /// 校验手动 DNS；requireMain 为 true 时首选 DNS 必填
        /// </summary>
        public static List<ValidationError> ValidateDns(string dnsMain, string dnsBackup, bool requireMain)
        {
            var errors = new List<ValidationError>();
            IPAddress unused;

            if (string.IsNullOrWhiteSpace(dnsMain))
            {
                if (requireMain)
                    errors.Add(new ValidationError(IpField.DnsMain, "请填写首选 DNS"));
                else if (!string.IsNullOrWhiteSpace(dnsBackup))
                    errors.Add(new ValidationError(IpField.DnsMain, "请先填写首选 DNS"));
            }
            else if (!TryParseIPv4(dnsMain, out unused))
            {
                errors.Add(new ValidationError(IpField.DnsMain, "首选 DNS 格式不正确"));
            }

            if (!string.IsNullOrWhiteSpace(dnsBackup) && !TryParseIPv4(dnsBackup, out unused))
                errors.Add(new ValidationError(IpField.DnsBackup, "备用 DNS 格式不正确"));

            return errors;
        }

        /// <summary>
        /// 不能用作本机地址的保留地址，返回原因；可用时返回 null
        /// </summary>
        private static string DescribeReserved(IPAddress ip)
        {
            byte first = ip.GetAddressBytes()[0];
            if (first == 0) return "IP 地址不能以 0 开头";
            if (first == 127) return "不能使用回环地址（127.x.x.x）";
            if (first >= 224 && first <= 239) return "不能使用组播地址（224–239 开头）";
            if (first >= 240) return "不能使用保留地址（240 及以上开头）";
            return null;
        }

        private static uint PrefixToUInt32(int prefix)
        {
            return prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        }

        private static uint ToUInt32(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private static IPAddress FromUInt32(uint value)
        {
            return new IPAddress(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
        }
    }
}
