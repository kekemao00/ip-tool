using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using IP_UpdateTest.Core;
using IP_UpdateTest.Core.Diagnosis;

namespace IP_UpdateTest
{
    /// <summary>
    /// 命令行要执行的操作
    /// </summary>
    public enum CliAction
    {
        None,
        Help,
        List,
        Static,
        Dhcp,
        Profile,
        Enable,
        Disable,
        Diagnose
    }

    /// <summary>
    /// 解析后的命令行参数
    /// </summary>
    public sealed class CliOptions
    {
        public CliAction Action { get; set; }
        public string Adapter { get; set; }
        public bool Json { get; set; }
        public string IpAddress { get; set; }
        public string SubnetMask { get; set; }
        public string Gateway { get; set; }

        /// <summary>
        /// --dns 指定的服务器；null 表示未指定
        /// </summary>
        public string[] Dns { get; set; }

        public string ProfileName { get; set; }
        public bool NoElevate { get; set; }
        public BackendKind Backend { get; set; }

        /// <summary>
        /// 提权后的子进程把输出写到这个文件，由发起提权的进程读回并显示
        /// </summary>
        public string ResultFile { get; set; }

        /// <summary>
        /// 参数错误说明，为 null 表示解析成功
        /// </summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// 命令行接口：不显示窗口，执行完以退出码返回结果，便于脚本和快捷方式调用
    /// </summary>
    public static class CommandLine
    {
        public const int ExitOk = 0;
        public const int ExitUsage = 1;
        public const int ExitAdapterNotFound = 2;
        public const int ExitInvalid = 3;
        public const int ExitFailed = 4;
        public const int ExitElevation = 5;
        public const int ExitProblemFound = 6;

        public const string Usage =
@"用法：
  IP_UpdateTest.exe --list [--json]
  IP_UpdateTest.exe --diagnose [--adapter <网卡>] [--json]
  IP_UpdateTest.exe --adapter <网卡> --static <IP>[/<前缀>] [--mask <掩码>] [--gateway <网关>] [--dns <DNS1>[,<DNS2>]]
  IP_UpdateTest.exe --adapter <网卡> --dhcp [--dns <DNS1>[,<DNS2>]]
  IP_UpdateTest.exe --adapter <网卡> --profile <配置方案名称>
  IP_UpdateTest.exe --adapter <网卡> --enable | --disable

  <网卡> 可以是连接名称（如 以太网、WLAN）、接口索引或网卡 GUID。
  --diagnose 依次检测本机网卡、路由器、外网、DNS、代理/VPN、UDP，指出问题所在环节并给出建议；
  不指定网卡时诊断当前上网的网卡，不需要管理员权限。
  --static 未指定 --gateway 时不设网关，未指定 --dns 时清空 DNS；
  --dhcp 未指定 --dns 时 DNS 也自动获取。
  非管理员运行时会请求提权；加 --no-elevate 则直接以退出码 5 结束。
  本程序是窗口程序：在 cmd 中用 start /wait 运行才能拿到退出码，
  PowerShell 中可用 Start-Process -Wait -PassThru。

退出码：0 成功，1 参数错误，2 找不到网卡，3 配置无效，4 应用失败，5 需要管理员权限，6 诊断发现问题";

        private static readonly HashSet<string> ActionSwitches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--list", "--diagnose", "--static", "--dhcp", "--profile", "--enable", "--disable", "--help", "-h", "/?"
        };

        /// <summary>
        /// 是否按命令行方式运行（只带 --adapter 时仍打开界面并选中该网卡）
        /// </summary>
        public static bool IsCliInvocation(string[] args)
        {
            return args.Any(a => ActionSwitches.Contains(a));
        }

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions { Backend = BackendKind.Auto };
            var actions = new List<CliAction>();
            bool hasPrefix = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value;

                // 取当前开关后面的值
                bool TakeValue(out string v)
                {
                    v = null;
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) return false;
                    v = args[++i];
                    return true;
                }

                switch (arg.ToLowerInvariant())
                {
                    case "--list":
                        actions.Add(CliAction.List);
                        break;
                    case "--diagnose":
                        actions.Add(CliAction.Diagnose);
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        actions.Add(CliAction.Help);
                        break;
                    case "--adapter":
                        if (!TakeValue(out value)) return Fail(options, "--adapter 后面需要指定网卡");
                        options.Adapter = value;
                        break;
                    case "--static":
                        if (!TakeValue(out value)) return Fail(options, "--static 后面需要指定 IP 地址");
                        actions.Add(CliAction.Static);
                        IPAddress ip;
                        int prefix;
                        if (IpValidator.TryParseCidr(value, out ip, out prefix))
                        {
                            options.IpAddress = ip.ToString();
                            options.SubnetMask = IpValidator.PrefixToMask(prefix).ToString();
                            hasPrefix = true;
                        }
                        else
                        {
                            options.IpAddress = value;
                        }
                        break;
                    case "--mask":
                        if (!TakeValue(out value)) return Fail(options, "--mask 后面需要指定子网掩码");
                        if (hasPrefix) return Fail(options, "不能同时使用 /前缀 和 --mask");
                        options.SubnetMask = value;
                        break;
                    case "--gateway":
                        if (!TakeValue(out value)) return Fail(options, "--gateway 后面需要指定网关地址");
                        options.Gateway = value;
                        break;
                    case "--dns":
                        if (!TakeValue(out value)) return Fail(options, "--dns 后面需要指定 DNS 地址，多个用逗号分隔");
                        options.Dns = value.Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();
                        break;
                    case "--dhcp":
                        actions.Add(CliAction.Dhcp);
                        break;
                    case "--profile":
                        if (!TakeValue(out value)) return Fail(options, "--profile 后面需要指定配置方案名称");
                        actions.Add(CliAction.Profile);
                        options.ProfileName = value;
                        break;
                    case "--enable":
                        actions.Add(CliAction.Enable);
                        break;
                    case "--disable":
                        actions.Add(CliAction.Disable);
                        break;
                    case "--no-elevate":
                        options.NoElevate = true;
                        break;
                    case "--backend":
                        BackendKind backend;
                        if (!TakeValue(out value) || !Enum.TryParse(value, true, out backend) || backend == BackendKind.None)
                            return Fail(options, "--backend 只能是 auto、wmi 或 netsh");
                        options.Backend = backend;
                        break;
                    case "--result-file":
                        if (!TakeValue(out value)) return Fail(options, "--result-file 后面需要指定文件");
                        options.ResultFile = value;
                        break;
                    default:
                        return Fail(options, "无法识别的参数：" + arg);
                }
            }

            if (actions.Count == 0) return Fail(options, "请指定要执行的操作");
            if (actions.Count > 1) return Fail(options, "一次只能执行一个操作");
            options.Action = actions[0];

            if (options.Action == CliAction.Help || options.Action == CliAction.List) return options;
            if (options.Action == CliAction.Diagnose)
            {
                if (options.SubnetMask != null || options.Gateway != null || options.Dns != null)
                    return Fail(options, "--diagnose 只能与 --adapter、--json 一起使用");
                return options;
            }
            if (string.IsNullOrWhiteSpace(options.Adapter)) return Fail(options, "请用 --adapter 指定网卡");
            if (options.Action == CliAction.Static && string.IsNullOrWhiteSpace(options.SubnetMask))
                return Fail(options, "请用 IP/前缀（如 192.168.1.10/24）或 --mask 指定子网掩码");
            if (options.Action != CliAction.Static && (options.SubnetMask != null || options.Gateway != null))
                return Fail(options, "--mask、--gateway 只能与 --static 一起使用");
            if (options.Dns != null && options.Action != CliAction.Static && options.Action != CliAction.Dhcp)
                return Fail(options, "--dns 只能与 --static 或 --dhcp 一起使用");
            return options;
        }

        private static CliOptions Fail(CliOptions options, string error)
        {
            options.Error = error;
            return options;
        }

        /// <summary>
        /// 执行命令行操作，返回退出码
        /// </summary>
        public static int Run(string[] args)
        {
            CliOptions options = Parse(args);
            using (CliOutput output = CliOutput.Open(options.ResultFile))
            {
                if (options.Error != null)
                {
                    output.Line("错误：" + options.Error);
                    output.Line(Usage);
                    return ExitUsage;
                }

                try
                {
                    return Execute(options, args, output);
                }
                catch (Exception ex) when (ex is ManagementException || ex is NetworkInformationException || ex is IOException
                    || ex is Win32Exception || ex is UnauthorizedAccessException)
                {
                    output.Line("错误：" + ex.Message);
                    return ExitFailed;
                }
            }
        }

        private static int Execute(CliOptions options, string[] args, CliOutput output)
        {
            switch (options.Action)
            {
                case CliAction.Help:
                    output.Line(Usage);
                    return ExitOk;
                case CliAction.List:
                    return ListAdapters(options.Json, output);
                case CliAction.Diagnose:
                    return Diagnose(options, output);
            }

            // 先在普通权限下检查网卡和参数，写错时不必弹出 UAC
            NetworkAdapter adapter = AdapterService.MatchAdapter(AdapterService.GetAdapters(true), options.Adapter);
            if (adapter == null)
            {
                output.Line($"错误：找不到网卡“{options.Adapter}”，可用 --list 查看所有网卡");
                return ExitAdapterNotFound;
            }

            IpConfigRequest request = null;
            if (options.Action != CliAction.Enable && options.Action != CliAction.Disable)
            {
                if (options.Action == CliAction.Profile)
                {
                    IpProfile profile = ProfileManager.Profiles.FirstOrDefault(
                        p => string.Equals(p.Name, options.ProfileName, StringComparison.CurrentCultureIgnoreCase));
                    if (profile == null)
                    {
                        output.Line($"错误：找不到配置方案“{options.ProfileName}”");
                        return ExitInvalid;
                    }
                    request = profile.ToRequest();
                }
                else
                {
                    request = options.Action == CliAction.Dhcp
                        ? IpConfigRequest.Dhcp(options.Dns)
                        : IpConfigRequest.Static(options.IpAddress, options.SubnetMask, options.Gateway, options.Dns);
                }

                List<ValidationError> errors = request.Validate();
                if (errors.Count > 0)
                {
                    output.Line("错误：" + errors[0].Message);
                    return ExitInvalid;
                }

                string unsupported = AdapterService.CheckSupported(adapter, request, options.Backend);
                if (unsupported != null)
                {
                    output.Line("失败：" + unsupported.Replace("\n", ""));
                    return ExitFailed;
                }
            }

            if (!Elevation.IsAdministrator())
            {
                // 已经是提权后的子进程仍没有管理员权限时不再重试，避免反复弹出 UAC
                if (options.NoElevate || options.ResultFile != null)
                {
                    output.Line("错误：修改网络配置需要管理员权限");
                    return ExitElevation;
                }
                return RunElevated(args, output);
            }

            ApplyResult result = request == null
                ? AdapterService.SetEnabled(adapter, options.Action == CliAction.Enable)
                : AdapterService.Apply(adapter, request, options.Backend);

            if (!result.Success)
            {
                output.Line("失败：" + result.Message);
                return ExitFailed;
            }

            string note = adapter.ConfigNote == null || options.Action == CliAction.Enable || options.Action == CliAction.Disable
                ? ""
                : "（" + adapter.ConfigNote + "）";
            output.Line($"成功：{adapter.Name} 已{Describe(options.Action)}{note}");
            return ExitOk;
        }

        private static string Describe(CliAction action)
        {
            switch (action)
            {
                case CliAction.Static: return "设置静态 IP";
                case CliAction.Dhcp: return "改为自动获取";
                case CliAction.Profile: return "应用配置方案";
                case CliAction.Enable: return "启用";
                case CliAction.Disable: return "禁用";
                default: return "完成操作";
            }
        }

        private static int ListAdapters(bool json, CliOutput output)
        {
            List<NetworkAdapter> adapters = AdapterService.GetAdapters(true);
            if (json)
            {
                var serializer = new DataContractJsonSerializer(typeof(List<AdapterInfo>));
                using (var stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, adapters.Select(AdapterInfo.From).ToList());
                    output.Line(Encoding.UTF8.GetString(stream.ToArray()));
                }
                return ExitOk;
            }

            foreach (NetworkAdapter a in adapters)
            {
                string dns = string.Join(", ", new[] { a.DnsMain, a.DnsBackup }.Where(d => d.Length > 0));
                output.Line($"{a.Name}（{a.InterfaceDescription}）");
                output.Line($"    状态：{a.StatusText}    接口索引：{a.InterfaceIndex}    {(a.CanConfigure ? "可修改" : "不可修改：" + a.ReadOnlyReason)}");
                output.Line($"    IPv4：{(a.IsDhcpEnabled ? "DHCP" : "静态")}  {Dash(a.IpAddress)} / {Dash(a.SubnetMask)}  网关 {Dash(a.Gateway)}  DNS {Dash(dns)}");
                output.Line($"    MAC：{Dash(a.MacAddressText)}    GUID：{a.NetworkInterfaceID}");
            }
            return ExitOk;
        }

        private static int Diagnose(CliOptions options, CliOutput output)
        {
            List<NetworkAdapter> adapters = AdapterService.GetAdapters(true);
            NetworkAdapter adapter;
            if (options.Adapter != null)
            {
                adapter = AdapterService.MatchAdapter(adapters, options.Adapter);
                if (adapter == null)
                {
                    output.Line($"错误：找不到网卡“{options.Adapter}”，可用 --list 查看所有网卡");
                    return ExitAdapterNotFound;
                }
            }
            else
            {
                // 默认诊断当前上网的网卡，找不到时取第一个已连接的物理网卡
                int primary = AdapterService.GetPrimaryInterfaceIndex();
                adapter = adapters.FirstOrDefault(a => primary >= 0 && a.InterfaceIndex == primary)
                    ?? adapters.FirstOrDefault(a => a.Status == AdapterStatus.Connected && a.IsPhysical)
                    ?? adapters.FirstOrDefault(a => a.IsPhysical);
            }

            if (!options.Json) output.Line("正在诊断，约需 10 秒…");
            DiagnosisReport report = NetworkDiagnosis.RunAsync(adapter).GetAwaiter().GetResult();
            output.Line(options.Json ? report.ToJson() : report.ToText());
            return report.FaultLayer.HasValue ? ExitProblemFound : ExitOk;
        }

        private static string Dash(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }

        /// <summary>
        /// 以管理员身份重新运行同样的命令，读回其输出并返回其退出码
        /// </summary>
        private static int RunElevated(string[] args, CliOutput output)
        {
            string resultFile = Path.Combine(Path.GetTempPath(), "IPTool", "cli-" + Guid.NewGuid().ToString("N") + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(resultFile));

            output.Line("正在请求管理员权限…");
            using (Process process = Elevation.StartElevated(JoinArguments(args.Concat(new[] { "--result-file", resultFile }))))
            {
                if (process == null)
                {
                    output.Line("错误：已取消，未获得管理员权限");
                    return ExitElevation;
                }
                process.WaitForExit();

                try
                {
                    if (File.Exists(resultFile))
                    {
                        output.Raw(File.ReadAllText(resultFile, Encoding.UTF8));
                        File.Delete(resultFile);
                    }
                }
                catch (IOException)
                {
                }
                return process.ExitCode;
            }
        }

        /// <summary>
        /// 按 Windows 命令行规则拼接参数（含空格、引号、末尾反斜杠时正确转义）
        /// </summary>
        public static string JoinArguments(IEnumerable<string> args)
        {
            return string.Join(" ", args.Select(QuoteArgument));
        }

        public static string QuoteArgument(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;

            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                // 引号前的反斜杠需要加倍，引号本身用 \" 转义
                sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                sb.Append(c);
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }

    /// <summary>
    /// --list --json 输出的网卡信息
    /// </summary>
    [DataContract]
    public sealed class AdapterInfo
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public string Description { get; set; }
        [DataMember(Order = 3)] public string Id { get; set; }
        [DataMember(Order = 4)] public int Index { get; set; }
        [DataMember(Order = 5)] public string Status { get; set; }
        [DataMember(Order = 6)] public bool Physical { get; set; }
        [DataMember(Order = 7)] public bool Virtual { get; set; }
        [DataMember(Order = 8)] public bool Dhcp { get; set; }
        [DataMember(Order = 9)] public string IpAddress { get; set; }
        [DataMember(Order = 10)] public string SubnetMask { get; set; }
        [DataMember(Order = 11)] public string Gateway { get; set; }
        [DataMember(Order = 12)] public string[] Dns { get; set; }
        [DataMember(Order = 13)] public bool ManualDns { get; set; }
        [DataMember(Order = 14)] public string Mac { get; set; }
        [DataMember(Order = 15)] public string Backend { get; set; }
        [DataMember(Order = 16)] public string ReadOnlyReason { get; set; }
        [DataMember(Order = 17)] public string[] IPv6 { get; set; }
        [DataMember(Order = 18)] public string DhcpServer { get; set; }
        [DataMember(Order = 19)] public string DhcpLeaseObtained { get; set; }
        [DataMember(Order = 20)] public string DhcpLeaseExpires { get; set; }

        public static AdapterInfo From(NetworkAdapter a)
        {
            return new AdapterInfo
            {
                Name = a.Name,
                Description = a.InterfaceDescription,
                Id = a.NetworkInterfaceID,
                Index = a.InterfaceIndex,
                Status = a.Status.ToString(),
                Physical = a.IsPhysical,
                Virtual = a.IsVirtual,
                Dhcp = a.IsDhcpEnabled,
                IpAddress = a.IpAddress,
                SubnetMask = a.SubnetMask,
                Gateway = a.Gateway,
                Dns = new[] { a.DnsMain, a.DnsBackup }.Where(d => d.Length > 0).ToArray(),
                ManualDns = a.HasStaticDns,
                Mac = a.MacAddressText,
                Backend = a.ConfigBackend.ToString(),
                ReadOnlyReason = a.ReadOnlyReason,
                IPv6 = a.IPv6Addresses.ToArray(),
                DhcpServer = a.DhcpServer,
                DhcpLeaseObtained = a.DhcpLeaseObtained?.ToString("s"),
                DhcpLeaseExpires = a.DhcpLeaseExpires?.ToString("s")
            };
        }
    }

    /// <summary>
    /// 命令行输出。窗口程序默认没有控制台：输出被重定向时直接写入，否则附加到启动它的命令行窗口。
    /// </summary>
    internal sealed class CliOutput : IDisposable
    {
        private const int AttachParentProcess = -1;
        private const int StdOutputHandle = -11;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int stdHandle);

        [DllImport("kernel32.dll")]
        private static extern uint GetConsoleOutputCP();

        private readonly TextWriter console;
        private readonly StreamWriter file;

        private CliOutput(TextWriter console, StreamWriter file)
        {
            this.console = console;
            this.file = file;
        }

        public static CliOutput Open(string resultFile)
        {
            IntPtr handle = GetStdHandle(StdOutputHandle);
            bool attached = false;
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) attached = AttachConsole(AttachParentProcess);

            uint codePage = GetConsoleOutputCP();
            Encoding encoding = codePage == 65001
                ? new UTF8Encoding(false)
                : codePage != 0 ? Encoding.GetEncoding((int)codePage) : ConsoleText.OemEncoding;
            var console = new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true };

            // 附加到控制台时提示符已经显示，先换行再输出
            if (attached) console.WriteLine();

            StreamWriter file = string.IsNullOrEmpty(resultFile)
                ? null
                : new StreamWriter(resultFile, false, new UTF8Encoding(false)) { AutoFlush = true };
            return new CliOutput(console, file);
        }

        public void Line(string text)
        {
            console.WriteLine(text);
            if (file != null) file.WriteLine(text);
        }

        public void Raw(string text)
        {
            console.Write(text);
            if (file != null) file.Write(text);
        }

        public void Dispose()
        {
            console.Dispose();
            if (file != null) file.Dispose();
        }
    }
}
