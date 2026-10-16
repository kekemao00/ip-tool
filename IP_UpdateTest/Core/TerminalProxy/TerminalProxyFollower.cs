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
    /// 跟随系统代理：代理软件开关系统代理或换端口时，命令行的环境变量随之设置或清除，
    /// 避免关掉代理软件后 git、pip 因为指向失效的端口而连不上网。程序在后台（托盘）运行时生效
    /// </summary>
    public sealed class TerminalProxyFollower : IDisposable
    {
        private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const int IntervalMilliseconds = 2000;

        private readonly Timer timer;
        private readonly object gate = new object();
        private string lastState;
        private bool disposed;

        /// <summary>
        /// 同步了环境变量时触发（在线程池线程上），参数为给用户看的说明
        /// </summary>
        public event Action<string> Synced;

        public TerminalProxyFollower()
        {
            timer = new Timer(_ => Tick(), null, 0, IntervalMilliseconds);
        }

        private void Tick()
        {
            lock (gate)
            {
                if (disposed) return;
                bool enabled;
                string server, pac;
                if (!ReadSystemProxy(out enabled, out server, out pac)) return;
                string state = enabled ? "on:" + server : string.IsNullOrEmpty(pac) ? "off" : "pac";
                if (state == lastState) return;
                lastState = state;

                TerminalProxySnapshot f = ReadEnvironment();
                f.SystemProxyEnabled = enabled;
                f.SystemProxyServer = server;
                f.PacUrl = pac;
                List<ProxyChange> changes = Decide(f, TerminalProxyConfigurator.LoadBackup());
                if (changes.Count == 0) return;
                List<string> errors = TerminalProxyConfigurator.Apply(changes, !enabled);
                if (errors.Count > 0 || Synced == null) return;
                Synced(enabled ? "命令行代理已跟随系统代理改为 " + changes.First(c => c.NewValue != null).NewValue : "系统代理已关闭，命令行代理已随之清除");
            }
        }

        /// <summary>
        /// 系统代理开启时把环境变量设成系统代理（只改环境变量），关闭时清除；PAC 模式下不动
        /// </summary>
        internal static List<ProxyChange> Decide(TerminalProxySnapshot f, TerminalProxyBackup backup)
        {
            var changes = new List<ProxyChange>();
            if (f.SystemProxyEnabled)
            {
                string endpoint = ProxySettings.FirstEndpoint(f.SystemProxyServer);
                string host;
                int port;
                if (!ProxySettings.TrySplit(endpoint, out host, out port)) return changes;
                // 系统代理一定是 HTTP 代理（WinINet 只支持 HTTP，socks= 项除外，此时取第一个也是 http=）
                var target = new ProxyCandidate { Host = host, Port = port, Http = true, IsSystemProxy = true };
                changes.AddRange(TerminalProxyAnalyzer.Analyze(f, target, backup).ApplyChanges.Where(c => c.Target == ProxyChangeTarget.Environment));
            }
            else if (string.IsNullOrEmpty(f.PacUrl))
            {
                changes.AddRange(TerminalProxyAnalyzer.Analyze(f, null, backup).ClearChanges.Where(c => c.Target == ProxyChangeTarget.Environment));
            }
            return changes;
        }

        private static bool ReadSystemProxy(out bool enabled, out string server, out string pac)
        {
            enabled = false;
            server = pac = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey))
                {
                    if (key == null) return false;
                    server = (key.GetValue("ProxyServer") as string ?? "").Trim();
                    enabled = key.GetValue("ProxyEnable") is int flag && flag != 0 && server.Length > 0;
                    pac = (key.GetValue("AutoConfigURL") as string ?? "").Trim();
                    return true;
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static TerminalProxySnapshot ReadEnvironment()
        {
            var f = new TerminalProxySnapshot { GeneratedAt = DateTime.Now };
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Environment"))
                {
                    if (key != null)
                    {
                        foreach (string name in TerminalProxyAnalyzer.AllVariables)
                        {
                            string value = key.GetValue(name) as string;
                            if (!string.IsNullOrWhiteSpace(value)) f.UserEnvironment[name] = value.Trim();
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
            }
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
