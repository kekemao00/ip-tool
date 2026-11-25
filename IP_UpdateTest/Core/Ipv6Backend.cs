using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 通过 netsh 修改 IPv6 配置（WMI Win32_NetworkAdapterConfiguration 只能设置 IPv4）。
    /// 手动地址、默认网关都是追加的，改之前要先删掉原来手动设置的，否则会越积越多
    /// </summary>
    public static class Ipv6Backend
    {
        private const string DeletePrefix = "interface ipv6 delete ";

        /// <summary>
        /// 应用配置所需的 netsh 参数，每项对应一次 netsh 调用。
        /// manualAddresses、manualGateways 是网卡现有的手动地址和手动默认网关，dnsIsStatic 表示现在是手动 DNS
        /// </summary>
        public static List<string> BuildCommands(int interfaceIndex, IEnumerable<Ipv6ManualAddress> manualAddresses,
            IEnumerable<string> manualGateways, bool dnsIsStatic, Ipv6ConfigRequest request)
        {
            IPAddress address = null, gateway = null;
            int prefix = request.ParsedPrefixLength;
            if (!request.UseAuto)
            {
                Ipv6Validator.TryParse(request.Address, out address);
                Ipv6Validator.TryParse(request.Gateway, out gateway);
            }

            var commands = new List<string>();
            bool keepAddress = false;
            foreach (Ipv6ManualAddress old in manualAddresses)
            {
                IPAddress ip;
                if (!Ipv6Validator.TryParse(old.Address, out ip)) continue;
                if (ip.Equals(address) && old.PrefixLength == prefix) keepAddress = true;
                else commands.Add($"{DeletePrefix}address interface={interfaceIndex} address={ip}");
            }
            bool keepGateway = false;
            foreach (string old in manualGateways)
            {
                IPAddress ip;
                if (!Ipv6Validator.TryParse(old, out ip)) continue;
                if (ip.Equals(gateway)) keepGateway = true;
                else commands.Add($"{DeletePrefix}route prefix=::/0 interface={interfaceIndex} nexthop={ip}");
            }
            if (address != null && !keepAddress)
                commands.Add($"interface ipv6 add address interface={interfaceIndex} address={address}/{prefix}");
            if (gateway != null && !keepGateway)
                commands.Add($"interface ipv6 add route prefix=::/0 interface={interfaceIndex} nexthop={gateway}");

            if (!request.UseAutoDns)
                commands.AddRange(BuildDnsCommands(interfaceIndex, request.DnsServers));
            else if (dnsIsStatic)
                commands.Add($"interface ipv6 set dnsservers name={interfaceIndex} source=dhcp");
            return commands;
        }

        /// <summary>
        /// 手动 DNS：第一个用 set 覆盖原列表，其余用 add 依次追加
        /// </summary>
        public static List<string> BuildDnsCommands(int interfaceIndex, string[] dns)
        {
            string[] servers = (dns ?? new string[0]).Select(Ipv6Validator.Normalize).ToArray();
            var commands = new List<string>
            {
                $"interface ipv6 set dnsservers name={interfaceIndex} source=static address={(servers.Length > 0 ? servers[0] : "none")} register=primary validate=no"
            };
            for (int i = 1; i < servers.Length; i++)
                commands.Add($"interface ipv6 add dnsservers name={interfaceIndex} address={servers[i]} index={i + 1} validate=no");
            return commands;
        }

        /// <summary>
        /// 应用配置。要删除的旧地址、旧网关可能已不存在（如被其他程序删掉），删除失败不影响结果
        /// </summary>
        public static ApplyResult Apply(int interfaceIndex, IEnumerable<Ipv6ManualAddress> manualAddresses,
            IEnumerable<string> manualGateways, bool dnsIsStatic, Ipv6ConfigRequest request)
        {
            List<string> commands = BuildCommands(interfaceIndex, manualAddresses, manualGateways, dnsIsStatic, request);
            return NetshBackend.Run(commands, c => c.StartsWith(DeletePrefix, StringComparison.Ordinal));
        }
    }
}
