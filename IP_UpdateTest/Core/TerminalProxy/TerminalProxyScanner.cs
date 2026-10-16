using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Threading.Tasks;
using IP_UpdateTest.Core.Diagnosis;
using Microsoft.Win32;

namespace IP_UpdateTest.Core.TerminalProxy
{
    /// <summary>
    /// 常见代理软件的进程名
    /// </summary>
    public static class ProxyClients
    {
        /// <summary>
        /// 图形界面程序：进程名（小写，不含 .exe）→ 显示名
        /// </summary>
        private static readonly Dictionary<string, string> Apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "v2rayn", "v2rayN" },
            { "clash-verge", "Clash Verge" },
            { "clash verge", "Clash Verge" },
            { "clash for windows", "Clash for Windows" },
            { "clash-nyanpasu", "Clash Nyanpasu" },
            { "mihomo party", "Mihomo Party" },
            { "flclash", "FlClash" },
            { "clashn", "ClashN" },
            { "nekoray", "NekoRay" },
            { "nekobox", "NekoBox" },
            { "hiddify", "Hiddify" },
            { "v2raya", "v2rayA" },
            { "qv2ray", "Qv2ray" },
            { "shadowsocks", "Shadowsocks" },
            { "shadowsocksr", "ShadowsocksR" },
            { "netch", "Netch" },
            { "karing", "Karing" }
        };

        /// <summary>
        /// 内核：由上面的界面程序启动，真正监听端口
        /// </summary>
        private static readonly Dictionary<string, string> Cores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "xray", "Xray" },
            { "v2ray", "V2Ray" },
            { "sing-box", "sing-box" },
            { "mihomo", "mihomo" },
            { "verge-mihomo", "mihomo" },
            { "verge-mihomo-alpha", "mihomo" },
            { "clash", "Clash" },
            { "clash-meta", "Clash Meta" },
            { "clash-win64", "Clash" },
            { "hysteria", "Hysteria" },
            { "naive", "NaïveProxy" },
            { "tuic-client", "TUIC" },
            { "trojan", "Trojan" },
            { "sslocal", "shadowsocks-rust" },
            { "ss-local", "shadowsocks" },
            { "privoxy", "Privoxy" }
        };

        /// <summary>
        /// 各家默认端口：v2rayN 10808/10809，Clash 7890/7897，v2rayA 20170-20172，NekoRay 2080，Hiddify 12334，Shadowsocks 1080
        /// </summary>
        public static readonly int[] WellKnownPorts = { 10808, 10809, 7890, 7891, 7897, 7898, 1080, 2080, 20170, 20171, 20172, 12334, 8889 };

        public static string AppName(string processName)
        {
            string name;
            return processName != null && Apps.TryGetValue(processName, out name) ? name : null;
        }

        public static string CoreName(string processName)
        {
            string name;
            return processName != null && Cores.TryGetValue(processName, out name) ? name : null;
        }
    }

    /// <summary>
    /// 采集检测数据：代理软件与端口、系统代理、环境变量、Git / npm / pip 的代理设置、WinHTTP，并实测代理
    /// </summary>
    public static class TerminalProxyScanner
    {
        private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const string WinHttpKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";
        private const string MachineEnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
        private const string UserEnvironmentKey = "Environment";

        public static string NpmrcPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npmrc"); }
        }

        public static string PipIniPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pip", "pip.ini"); }
        }

        public static async Task<TerminalProxySnapshot> ScanAsync()
        {
            var f = new TerminalProxySnapshot { GeneratedAt = DateTime.Now };
            Dictionary<int, string> owners = null;
            List<PortListener> listeners = null;
            await Task.Run(() =>
            {
                owners = ReadProcesses(f.Clients);
                ReadSystemProxy(f);
                ReadEnvironment(Registry.CurrentUser, UserEnvironmentKey, f.UserEnvironment);
                ReadEnvironment(Registry.LocalMachine, MachineEnvironmentKey, f.MachineEnvironment);
                f.WinHttpProxy = ReadWinHttp();
                listeners = ProxyPortProbe.Listeners();
            });

            Task tools = Task.Run(() => ReadTools(f));
            await FindCandidatesAsync(f, owners, listeners);

            // 其余设置里出现的地址只测能否连接
            await tools;
            var endpoints = new List<string>();
            foreach (string name in TerminalProxyAnalyzer.ProxyVariables)
            {
                endpoints.Add(ProxySettings.UrlEndpoint(f.EffectiveEnvironment(name)));
            }
            endpoints.AddRange(f.Tools.SelectMany(t => t.Values.Values).Select(ProxySettings.UrlEndpoint));
            endpoints.Add(ProxySettings.FirstEndpoint(f.WinHttpProxy));
            await Task.WhenAll(endpoints.Where(e => e != null && !f.Reachable.ContainsKey(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                .Select(async e => f.Reachable[e] = await Connect(e)));

            ProxyCandidate target = TerminalProxyAnalyzer.PickTarget(f);
            if (target != null) await TestTunnelAsync(f, target);
            return f;
        }

        /// <summary>
        /// 经这个代理访问外网，结果记入 snapshot.Tunnels（SOCKS5 端口不测）
        /// </summary>
        public static async Task TestTunnelAsync(TerminalProxySnapshot f, ProxyCandidate target)
        {
            if (!target.Http || f.Tunnels.ContainsKey(target.Endpoint)) return;
            f.Tunnels[target.Endpoint] = await ProxyPortProbe.TunnelAsync(target.Url);
        }

        private static async Task<bool> Connect(string endpoint)
        {
            string host;
            int port;
            return ProxySettings.TrySplit(endpoint, out host, out port) && await ProxyPortProbe.CanConnectAsync(host, port);
        }

        /// <summary>
        /// 进程 ID → 所属代理软件的显示名；同时列出正在运行的代理软件
        /// </summary>
        private static Dictionary<int, string> ReadProcesses(List<string> clients)
        {
            var owners = new Dictionary<int, string>();
            var cores = new List<string>();
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
            {
                return owners;
            }
            foreach (Process process in processes)
            {
                using (process)
                {
                    string app, core;
                    try
                    {
                        app = ProxyClients.AppName(process.ProcessName);
                        core = ProxyClients.CoreName(process.ProcessName);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                    if (app != null)
                    {
                        owners[process.Id] = app;
                        if (!clients.Contains(app)) clients.Add(app);
                    }
                    else if (core != null)
                    {
                        owners[process.Id] = core;
                        if (!cores.Contains(core)) cores.Add(core);
                    }
                }
            }
            // 内核监听的端口记在界面程序名下（v2rayN 的端口实际由 xray / sing-box 监听）
            if (clients.Count > 0)
            {
                foreach (int pid in owners.Where(p => cores.Contains(p.Value)).Select(p => p.Key).ToList())
                    owners[pid] = clients[0];
            }
            else
            {
                clients.AddRange(cores);
            }
            return owners;
        }

        private static async Task FindCandidatesAsync(TerminalProxySnapshot f, Dictionary<int, string> owners, List<PortListener> listeners)
        {
            var ports = new Dictionary<int, string>();
            bool haveTable = listeners.Count > 0;
            foreach (PortListener l in listeners)
            {
                if (!IPAddress.IsLoopback(l.Address) && !l.Address.Equals(IPAddress.Any)) continue;
                string owner;
                if (owners.TryGetValue(l.ProcessId, out owner)) ports[l.Port] = owner;
                else if (ProxyClients.WellKnownPorts.Contains(l.Port) && !ports.ContainsKey(l.Port)) ports[l.Port] = null;
            }
            if (!haveTable)
            {
                foreach (int port in ProxyClients.WellKnownPorts) ports[port] = null;
            }

            // 系统代理（含局域网里的代理服务器）
            var systemEndpoints = ProxySettings.ParseServerList(f.SystemProxyEnabled ? f.SystemProxyServer : null);
            var hosts = new List<KeyValuePair<string, int>>();
            foreach (string endpoint in systemEndpoints)
            {
                string host;
                int port;
                if (!ProxySettings.TrySplit(endpoint, out host, out port)) continue;
                if (TerminalProxyAnalyzer.IsLoopback("http://" + endpoint)) ports[port] = ports.ContainsKey(port) ? ports[port] : null;
                else hosts.Add(new KeyValuePair<string, int>(host, port));
            }
            hosts.AddRange(ports.Keys.Select(p => new KeyValuePair<string, int>("127.0.0.1", p)));

            var found = new List<ProxyCandidate>();
            await Task.WhenAll(hosts.Select(async h =>
            {
                PortProbeResult probe = await ProxyPortProbe.ProbeAsync(h.Key, h.Value);
                lock (found)
                {
                    f.Reachable[h.Key + ":" + h.Value] = probe.Listening;
                    if (!probe.Http && !probe.Socks5) return;
                    string owner;
                    ports.TryGetValue(h.Value, out owner);
                    found.Add(new ProxyCandidate
                    {
                        Host = h.Key,
                        Port = h.Value,
                        Http = probe.Http,
                        Socks5 = probe.Socks5,
                        Owner = h.Key == "127.0.0.1" ? owner : null,
                        IsSystemProxy = systemEndpoints.Any(e => TerminalProxyAnalyzer.SameEndpoint(e, h.Key + ":" + h.Value))
                    });
                }
            }));
            // 系统代理的端口最前，其次是能用 HTTP 的、认识的代理软件的
            f.Candidates.AddRange(found.OrderByDescending(c => c.IsSystemProxy).ThenByDescending(c => c.Http).ThenByDescending(c => c.Owner != null).ThenBy(c => c.Port));
        }

        private static void ReadSystemProxy(TerminalProxySnapshot f)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey))
                {
                    if (key == null) return;
                    string server = key.GetValue("ProxyServer") as string;
                    f.SystemProxyEnabled = key.GetValue("ProxyEnable") is int flag && flag != 0 && !string.IsNullOrWhiteSpace(server);
                    f.SystemProxyServer = f.SystemProxyEnabled ? server.Trim() : null;
                    string pac = key.GetValue("AutoConfigURL") as string;
                    f.PacUrl = string.IsNullOrWhiteSpace(pac) ? null : pac.Trim();
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static void ReadEnvironment(RegistryKey root, string path, Dictionary<string, string> values)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(path))
                {
                    if (key == null) return;
                    foreach (string name in TerminalProxyAnalyzer.AllVariables)
                    {
                        string value = key.GetValue(name) as string;
                        if (!string.IsNullOrWhiteSpace(value)) values[name] = value.Trim();
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static string ReadWinHttp()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(WinHttpKey))
                {
                    return ProxySettings.ParseWinHttpSettings(key?.GetValue("WinHttpSettings") as byte[]);
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void ReadTools(TerminalProxySnapshot f)
        {
            var git = new ToolProxySetting { Tool = ProxyTool.Git };
            foreach (string key in ToolProxySetting.KeysOf(ProxyTool.Git))
            {
                int exitCode;
                string output = GitConfig(out exitCode, "--global", "--get", key);
                if (exitCode < 0) break;
                git.Installed = true;
                if (exitCode == 0 && !string.IsNullOrWhiteSpace(output)) git.Values[key] = output.Trim();
            }
            f.Tools.Add(git);

            var npm = new ToolProxySetting { Tool = ProxyTool.Npm, Installed = OnPath("npm.cmd") };
            foreach (KeyValuePair<string, string> pair in ReadConfig(NpmrcPath, ToolProxySetting.KeysOf(ProxyTool.Npm))) npm.Values[pair.Key] = pair.Value;
            f.Tools.Add(npm);

            var pip = new ToolProxySetting { Tool = ProxyTool.Pip, Installed = OnPath("pip.exe") };
            foreach (KeyValuePair<string, string> pair in ReadConfig(PipIniPath, ToolProxySetting.KeysOf(ProxyTool.Pip))) pip.Values[pair.Key] = pair.Value;
            f.Tools.Add(pip);
        }

        private static Dictionary<string, string> ReadConfig(string path, IEnumerable<string> keys)
        {
            try
            {
                return File.Exists(path) ? ConfigLines.Read(File.ReadAllText(path), keys) : new Dictionary<string, string>();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                return new Dictionary<string, string>();
            }
        }

        private static bool OnPath(string fileName)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    if (File.Exists(Path.Combine(dir.Trim().Trim('"'), fileName))) return true;
                }
                catch (ArgumentException)
                {
                }
            }
            return false;
        }

        /// <summary>
        /// 运行 git config；没装 Git 或超时时 exitCode 为 -1
        /// </summary>
        public static string GitConfig(out int exitCode, params string[] args)
        {
            exitCode = -1;
            var info = new ProcessStartInfo("git", "config " + string.Join(" ", args.Select(Quote)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                using (Process process = Process.Start(info))
                {
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(4000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
                        {
                        }
                        return null;
                    }
                    exitCode = process.ExitCode;
                    return output.Result;
                }
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
            {
                return null;
            }
        }

        private static string Quote(string arg)
        {
            return arg.IndexOfAny(new[] { ' ', '"' }) >= 0 ? "\"" + arg.Replace("\"", "\\\"") + "\"" : arg;
        }
    }
}
