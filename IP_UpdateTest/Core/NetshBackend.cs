using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 通过 netsh 修改 IPv4 配置。用于 WMI 做不到的情况：
    /// 网卡未连接时切换为 DHCP、修改 DNS（WMI 对未连接网卡返回 84），以及清除默认网关。
    /// </summary>
    public static class NetshBackend
    {
        private const int TimeoutMilliseconds = 30000;

        /// <summary>
        /// 设置静态 IP 所需的 netsh 参数，每项对应一次 netsh 调用；gateway 为空表示不设网关
        /// </summary>
        public static List<string> BuildStaticCommands(int interfaceIndex, string ipAddress, string subnetMask, string gateway, string[] dns)
        {
            string gw = string.IsNullOrWhiteSpace(gateway) ? "none" : gateway.Trim();
            var commands = new List<string>
            {
                $"interface ipv4 set address name={interfaceIndex} source=static address={ipAddress} mask={subnetMask} gateway={gw}"
            };
            commands.AddRange(BuildDnsCommands(interfaceIndex, dns));
            return commands;
        }

        /// <summary>
        /// 启用 DHCP 所需的 netsh 参数；dns 为 null 时 DNS 也自动获取。
        /// 网卡已是 DHCP 时 netsh 会报“已在此接口上启用 DHCP”并返回失败，所以已是目标状态的步骤直接跳过。
        /// </summary>
        public static List<string> BuildDhcpCommands(int interfaceIndex, string[] dns, bool addressIsDhcp = false, bool dnsIsDhcp = false)
        {
            var commands = new List<string>();
            if (!addressIsDhcp)
                commands.Add($"interface ipv4 set address name={interfaceIndex} source=dhcp");
            if (dns != null)
                commands.AddRange(BuildDnsCommands(interfaceIndex, dns));
            else if (!dnsIsDhcp)
                commands.Add($"interface ipv4 set dnsservers name={interfaceIndex} source=dhcp");
            return commands;
        }

        /// <summary>
        /// 手动 DNS：第一个用 set 覆盖原列表，其余用 add 依次追加；为空则清空列表
        /// </summary>
        public static List<string> BuildDnsCommands(int interfaceIndex, string[] dns)
        {
            if (dns == null || dns.Length == 0)
            {
                return new List<string>
                {
                    $"interface ipv4 set dnsservers name={interfaceIndex} source=static address=none register=primary validate=no"
                };
            }

            var commands = new List<string>
            {
                $"interface ipv4 set dnsservers name={interfaceIndex} source=static address={dns[0]} register=primary validate=no"
            };
            for (int i = 1; i < dns.Length; i++)
            {
                commands.Add($"interface ipv4 add dnsservers name={interfaceIndex} address={dns[i]} index={i + 1} validate=no");
            }
            return commands;
        }

        /// <summary>
        /// 设置静态 IP。只适用于已连接的网卡：实测网卡未连接时 netsh 不会关闭 DHCP（EnableDHCP 仍为 1），
        /// 会留下 DHCP 与静态地址并存的配置。
        /// </summary>
        public static ApplyResult ApplyStatic(int interfaceIndex, string ipAddress, string subnetMask, string gateway, string[] dns)
        {
            return Run(BuildStaticCommands(interfaceIndex, ipAddress, subnetMask, gateway, dns));
        }

        /// <summary>
        /// 启用 DHCP（已是 DHCP 时只修改 DNS）
        /// </summary>
        public static ApplyResult ApplyDhcp(int interfaceIndex, string[] dns, bool addressIsDhcp, bool dnsIsDhcp)
        {
            return Run(BuildDhcpCommands(interfaceIndex, dns, addressIsDhcp, dnsIsDhcp));
        }

        /// <summary>
        /// 清除默认网关所需的 netsh 参数。网卡须已是静态地址（WMI EnableStatic 之后），重设地址并指定无网关。
        /// </summary>
        public static string BuildClearGatewayCommand(int interfaceIndex, string ipAddress, string subnetMask)
        {
            return $"interface ipv4 set address name={interfaceIndex} source=static address={ipAddress} mask={subnetMask} gateway=none";
        }

        public static ApplyResult ClearGateway(int interfaceIndex, string ipAddress, string subnetMask)
        {
            return Run(new[] { BuildClearGatewayCommand(interfaceIndex, ipAddress, subnetMask) });
        }

        private static ApplyResult Run(IEnumerable<string> commands)
        {
            foreach (string arguments in commands)
            {
                int exitCode;
                string output;
                Execute(arguments, out exitCode, out output);
                if (exitCode != 0)
                {
                    string reason = string.IsNullOrWhiteSpace(output) ? $"退出码 {exitCode}" : output.Trim();
                    return ApplyResult.Fail($"netsh 执行失败：{reason}\n命令：netsh {arguments}");
                }
            }
            return ApplyResult.Ok();
        }

        internal static void Execute(string arguments, out int exitCode, out string output)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(psi))
            {
                // 两个流同时读，避免缓冲区写满导致互相等待；按字节读取，编码随控制台而变（见 ConsoleText.Decode）
                Task<byte[]> stdout = ReadAllBytesAsync(process.StandardOutput.BaseStream);
                Task<byte[]> stderr = ReadAllBytesAsync(process.StandardError.BaseStream);
                if (!process.WaitForExit(TimeoutMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    exitCode = -1;
                    output = "netsh 执行超时";
                    return;
                }
                exitCode = process.ExitCode;
                output = ConsoleText.Decode(stdout.Result) + ConsoleText.Decode(stderr.Result);
            }
        }

        private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
        {
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }
    }
}
