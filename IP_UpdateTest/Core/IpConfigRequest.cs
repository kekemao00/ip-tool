using System.Collections.Generic;
using System.Linq;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 要应用到网卡的 IPv4 配置
    /// </summary>
    public sealed class IpConfigRequest
    {
        /// <summary>
        /// IP 地址自动获取（DHCP）
        /// </summary>
        public bool UseDhcp { get; set; }

        public string IpAddress { get; set; }

        public string SubnetMask { get; set; }

        /// <summary>
        /// 默认网关，空表示不设网关
        /// </summary>
        public string Gateway { get; set; }

        /// <summary>
        /// DNS 自动获取；只在 IP 自动获取时有效，静态 IP 总是使用手动 DNS
        /// </summary>
        public bool UseDhcpDns { get; set; }

        /// <summary>
        /// 手动 DNS，按优先级排列；为空表示清空 DNS
        /// </summary>
        public string[] DnsServers { get; set; } = new string[0];

        /// <summary>
        /// 由首选、备用 DNS 组成列表，忽略空值
        /// </summary>
        public static string[] DnsList(string dnsMain, string dnsBackup)
        {
            return new[] { dnsMain, dnsBackup }
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim())
                .ToArray();
        }

        public static IpConfigRequest Dhcp(string[] manualDns = null)
        {
            bool manual = manualDns != null && manualDns.Length > 0;
            return new IpConfigRequest
            {
                UseDhcp = true,
                UseDhcpDns = !manual,
                DnsServers = manual ? manualDns : new string[0]
            };
        }

        public static IpConfigRequest Static(string ipAddress, string subnetMask, string gateway, string[] dns)
        {
            return new IpConfigRequest
            {
                UseDhcp = false,
                IpAddress = ipAddress,
                SubnetMask = subnetMask,
                Gateway = gateway ?? "",
                DnsServers = dns ?? new string[0]
            };
        }

        /// <summary>
        /// 校验配置，返回全部错误（为空表示通过）
        /// </summary>
        public List<ValidationError> Validate()
        {
            string dnsMain = DnsServers.Length > 0 ? DnsServers[0] : "";
            string dnsBackup = DnsServers.Length > 1 ? DnsServers[1] : "";

            if (UseDhcp)
            {
                return UseDhcpDns ? new List<ValidationError>() : IpValidator.ValidateDns(dnsMain, dnsBackup, true);
            }
            return IpValidator.ValidateStatic(IpAddress, SubnetMask, Gateway, dnsMain, dnsBackup);
        }
    }
}
