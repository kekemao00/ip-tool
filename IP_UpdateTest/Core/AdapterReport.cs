using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 生成网卡信息报表（纯文本）
    /// </summary>
    public static class AdapterReport
    {
        private const int CaptionWidth = 12;

        public static string Build(IList<NetworkAdapter> adapters, DateTime generatedAt)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"网卡信息报表    生成时间：{generatedAt:yyyy-MM-dd HH:mm:ss}    共 {adapters.Count} 个网卡");
            if (adapters.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("没有符合条件的网卡");
                return sb.ToString();
            }

            int index = 0;
            foreach (NetworkAdapter a in adapters)
            {
                sb.AppendLine();
                sb.AppendLine($"[{++index}] {a.Name}");
                Line(sb, "描述", a.InterfaceDescription);
                Line(sb, "状态", a.StatusText);
                Line(sb, "MAC 地址", a.MacAddressText);
                Line(sb, "IP 获取", a.IsDhcpEnabled ? "自动 (DHCP)" : "手动");
                Line(sb, "IPv4 地址", a.IpAddress.Length > 0 ? $"{a.IpAddress} / {a.SubnetMask}" : "");
                Line(sb, "默认网关", a.Gateway);

                string dns = string.Join(", ", new[] { a.DnsMain, a.DnsBackup }.Where(d => d.Length > 0));
                bool manualDns = a.HasStaticDns || !a.IsDhcpEnabled;
                Line(sb, "DNS", dns.Length == 0 ? "" : dns + (manualDns ? "（手动）" : "（自动）"));

                if (a.IsDhcpEnabled && a.Status == AdapterStatus.Connected)
                {
                    Line(sb, "DHCP 服务器", a.DhcpServer);
                    Line(sb, "租约获取", Format(a.DhcpLeaseObtained));
                    Line(sb, "租约到期", Format(a.DhcpLeaseExpires));
                }

                List<string> ipv6 = a.IPv6Addresses;
                for (int i = 0; i < ipv6.Count; i++) Line(sb, i == 0 ? "IPv6" : "", ipv6[i]);
                if (!a.CanConfigure && a.ReadOnlyReason != null) Line(sb, "说明", a.ReadOnlyReason);
            }
            return sb.ToString();
        }

        private static string Format(DateTime? time)
        {
            return time.HasValue ? time.Value.ToString("yyyy-MM-dd HH:mm:ss") : "";
        }

        private static void Line(StringBuilder sb, string caption, string value)
        {
            sb.Append("    ").Append(PadDisplay(caption, CaptionWidth)).AppendLine(string.IsNullOrEmpty(value) ? "-" : value);
        }

        /// <summary>
        /// 按显示宽度补齐空格（中文等全角字符占两格），使等宽字体下各行对齐
        /// </summary>
        public static string PadDisplay(string text, int width)
        {
            int display = text.Sum(c => c > 0xFF ? 2 : 1);
            return text + new string(' ', Math.Max(1, width - display));
        }
    }
}
