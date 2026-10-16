using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using IP_UpdateTest.Core.Diagnosis;
using Microsoft.Win32;

namespace IP_UpdateTest.Core.TerminalProxy
{
    /// <summary>
    /// 跟随系统代理：代理软件开关系统代理或换端口时，命令行的环境变量（以及本程序写过的 Git / npm / pip 代理项）随之设置或清除，
    /// 避免关掉代理软件后 git、pip 因为指向失效的端口而连不上网。程序在后台（托盘）运行时生效
    /// </summary>
    public sealed class TerminalProxyFollower : IDisposable
    {
        private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const int IntervalMilliseconds = 2000;

        private readonly Timer timer;
        private readonly object gate = new object();
        private string lastState;
        private string failedState;
        private bool loaded;
        private bool disposed;

        /// <summary>
        /// 同步了环境变量时触发（在线程池线程上），参数为给用户看的说明
        /// </summary>
        public event Action<string> Synced;

        /// <summary>
        /// 同步失败时触发（同一状态只报一次，之后每次检查都会重试）
        /// </summary>
        public event Action<string> Failed;

        public TerminalProxyFollower()
        {
            timer = new Timer(_ => Tick(), null, 0, IntervalMilliseconds);
        }

        private void Tick()
        {
            lock (gate)
            {
                if (disposed) return;
                if (!loaded)
                {
                    // 上次运行时最后同步到的状态：程序没运行期间系统代理关了，启动后也要清掉指向它的设置
                    lastState = TerminalProxyConfigurator.LoadBackup()?.FollowState;
                    loaded = true;
                }
                bool enabled;
                string server, pac, bypass;
                if (!ReadSystemProxy(out enabled, out server, out pac, out bypass)) return;
                string state = enabled ? "on:" + server : string.IsNullOrEmpty(pac) ? "off" : "pac";
                if (state == lastState) return;
                if (ShouldSync(lastState, state, enabled))
                {
                    TerminalProxySnapshot f = ReadEnvironment();
                    f.SystemProxyEnabled = enabled;
                    f.SystemProxyServer = server;
                    f.PacUrl = pac;
                    f.SystemProxyBypass = bypass;
                    List<ProxyChange> changes = Decide(f, TerminalProxyConfigurator.LoadBackup());
                    if (changes.Count > 0)
                    {
                        List<string> errors = TerminalProxyConfigurator.Apply(changes, !enabled, true);
                        if (errors.Count > 0)
                        {
                            // 不记下这个状态，下次检查重试
                            if (failedState != state) Failed?.Invoke("命令行代理没能跟随系统代理：" + errors[0]);
                            failedState = state;
                            return;
                        }
                        Synced?.Invoke(Message(changes, enabled));
                    }
                }
                lastState = state;
                failedState = null;
                TerminalProxyConfigurator.SaveFollowState(state);
            }
        }

        /// <summary>
        /// 系统代理有变化时同步。没有记录过的第一次读数只作为基准：开着就同步（换了端口也能跟上），
        /// 关着则不动——可能是 TUN 模式或手动模式，用户刚用一键设置配好的命令行代理不能被清掉。
        /// 记录过（上次运行时同步到“开”）而现在关着，说明系统代理在程序没运行时关了，照常清除
        /// </summary>
        internal static bool ShouldSync(string lastState, string state, bool enabled)
        {
            if (state == lastState) return false;
            return lastState != null || enabled;
        }

        internal static string Message(List<ProxyChange> changes, bool enabled)
        {
            ProxyChange proxy = changes.FirstOrDefault(c => !string.IsNullOrEmpty(c.NewValue) && c.Key != TerminalProxyAnalyzer.NoProxy);
            if (!enabled) return proxy != null ? "系统代理已关闭，命令行代理已恢复为 " + proxy.NewValue : "系统代理已关闭，命令行代理已随之清除";
            return proxy != null ? "命令行代理已跟随系统代理改为 " + proxy.NewValue : "命令行代理与系统代理一致，已补上 NO_PROXY";
        }

        /// <summary>
        /// 系统代理开启时把环境变量设成系统代理，关闭时清除；PAC 模式下不动。
        /// Git / npm / pip 自己的代理项优先于环境变量，只改本程序写过的（用户自己设的不动），不然它们会停在旧端口上
        /// </summary>
        internal static List<ProxyChange> Decide(TerminalProxySnapshot f, TerminalProxyBackup backup)
        {
            var changes = new List<ProxyChange>();
            if (f.SystemProxyEnabled)
            {
                bool socks;
                string endpoint = TerminalProxyAnalyzer.SystemProxyEndpoint(f.SystemProxyServer, out socks);
                string host;
                int port;
                if (!ProxySettings.TrySplit(endpoint, out host, out port)) return changes;
                // WinINet 的 http= / https= 都是 HTTP 代理；只设置了 socks= 时写 socks5h://
                var target = new ProxyCandidate { Host = host, Port = port, Http = !socks, Socks5 = socks, IsSystemProxy = true };
                changes.AddRange(TerminalProxyAnalyzer.Analyze(f, target, backup).ApplyChanges.Where(c => Follows(c, backup)));
            }
            else if (string.IsNullOrEmpty(f.PacUrl))
            {
                changes.AddRange(TerminalProxyAnalyzer.Analyze(f, null, backup).ClearChanges.Where(c => Follows(c, backup)));
            }
            return changes;
        }

        private static bool Follows(ProxyChange change, TerminalProxyBackup backup)
        {
            return change.Target == ProxyChangeTarget.Environment
                || (backup != null && backup.Wrote(TerminalProxyBackup.KeyOf(change.Target, change.Key), change.OldValue));
        }

        private static bool ReadSystemProxy(out bool enabled, out string server, out string pac, out string bypass)
        {
            enabled = false;
            server = pac = bypass = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey))
                {
                    if (key == null) return false;
                    server = (key.GetValue("ProxyServer") as string ?? "").Trim();
                    enabled = key.GetValue("ProxyEnable") is int flag && flag != 0 && server.Length > 0;
                    pac = (key.GetValue("AutoConfigURL") as string ?? "").Trim();
                    bypass = key.GetValue("ProxyOverride") as string;
                    return true;
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// 用户级和系统级都要读：系统级已有 NO_PROXY 时不能再写一个用户级的把它盖住。
        /// 工具的代理项也读（要运行 git，只在系统代理有变化时才读）
        /// </summary>
        private static TerminalProxySnapshot ReadEnvironment()
        {
            var f = new TerminalProxySnapshot { GeneratedAt = DateTime.Now };
            TerminalProxyScanner.ReadEnvironment(Registry.CurrentUser, TerminalProxyScanner.UserEnvironmentKey, f.UserEnvironment);
            TerminalProxyScanner.ReadEnvironment(Registry.LocalMachine, TerminalProxyScanner.MachineEnvironmentKey, f.MachineEnvironment);
            TerminalProxyScanner.ReadTools(f);
            return f;
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                timer.Dispose();
            }
        }
    }
}
