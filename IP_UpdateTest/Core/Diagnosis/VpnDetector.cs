using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace IP_UpdateTest.Core.Diagnosis
{
    /// <summary>
    /// 识别 VPN / 代理 TUN 网卡和常见的代理、VPN 程序
    /// </summary>
    public static class VpnDetector
    {
        /// <summary>
        /// 网卡名称或描述中出现即视为 VPN / 隧道网卡的关键字（不区分大小写）
        /// </summary>
        private static readonly string[] AdapterKeywords =
        {
            "wintun", "tap-windows", "tap adapter", "tun adapter", "wireguard", "openvpn", "clash", "mihomo", "sing-box", "singbox",
            "v2ray", "xray", "nekoray", "hiddify", "zerotier", "tailscale", "anyconnect", "fortinet", "fortissl", "globalprotect",
            "pangp", "sangfor", "easyconnect", "pulse secure", "softether", "vpn", "meta tunnel", "leigod"
        };

        /// <summary>
        /// 系统自带的隧道网卡，不算 VPN
        /// </summary>
        private static readonly string[] IgnoredKeywords = { "teredo", "isatap", "6to4", "ip-https", "loopback" };

        /// <summary>
        /// 进程名（不含 .exe，按前缀匹配）→ 显示名称
        /// </summary>
        private static readonly KeyValuePair<string, string>[] Programs =
        {
            Pair("clash-verge", "Clash Verge"),
            Pair("verge-mihomo", "Clash Verge"),
            Pair("clash for windows", "Clash for Windows"),
            Pair("clash", "Clash"),
            Pair("mihomo", "mihomo"),
            Pair("flclash", "FlClash"),
            Pair("v2rayn", "v2rayN"),
            Pair("v2ray", "V2Ray"),
            Pair("xray", "Xray"),
            Pair("sing-box", "sing-box"),
            Pair("nekoray", "NekoRay"),
            Pair("nekobox", "NekoBox"),
            Pair("hiddify", "Hiddify"),
            Pair("shadowsocksr", "ShadowsocksR"),
            Pair("shadowsocks", "Shadowsocks"),
            Pair("trojan", "Trojan"),
            Pair("hysteria", "Hysteria"),
            Pair("tuic", "TUIC"),
            Pair("openvpn", "OpenVPN"),
            Pair("wireguard", "WireGuard"),
            Pair("tailscale", "Tailscale"),
            Pair("zerotier", "ZeroTier"),
            Pair("vpnui", "Cisco AnyConnect"),
            Pair("forticlient", "FortiClient"),
            Pair("pangpa", "GlobalProtect"),
            Pair("easyconnect", "EasyConnect"),
            Pair("sangforcsclient", "EasyConnect"),
            Pair("fiddler", "Fiddler"),
            Pair("charles", "Charles"),
            Pair("leigod", "雷神加速器"),
            Pair("uuyc", "网易 UU 加速器")
        };

        private static KeyValuePair<string, string> Pair(string prefix, string name)
        {
            return new KeyValuePair<string, string>(prefix, name);
        }

        /// <summary>
        /// 是否为 VPN / 隧道网卡（纯函数，便于测试）。PPP 类型包括 VPN 和宽带拨号，统一视为拨号或 VPN 连接。
        /// </summary>
        public static bool IsVpnAdapter(string name, string description, NetworkInterfaceType type)
        {
            string text = ((name ?? "") + " " + (description ?? "")).ToLowerInvariant();
            if (IgnoredKeywords.Any(k => text.Contains(k))) return false;
            if (type == NetworkInterfaceType.Ppp) return true;
            return AdapterKeywords.Any(k => text.Contains(k));
        }

        /// <summary>
        /// 由进程名得到正在运行的代理 / VPN 程序（去重，保持顺序）
        /// </summary>
        public static List<string> MatchPrograms(IEnumerable<string> processNames)
        {
            var result = new List<string>();
            foreach (string process in processNames)
            {
                string lower = (process ?? "").ToLowerInvariant();
                foreach (KeyValuePair<string, string> program in Programs)
                {
                    if (!lower.StartsWith(program.Key, StringComparison.Ordinal)) continue;
                    if (!result.Contains(program.Value)) result.Add(program.Value);
                    break;
                }
            }
            return result;
        }
    }
}
