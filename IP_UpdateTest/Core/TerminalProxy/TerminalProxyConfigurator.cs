using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace IP_UpdateTest.Core.TerminalProxy
{
    /// <summary>
    /// 执行一键设置 / 清除：写用户级环境变量并通知系统，改 Git、npm、pip 的代理项。设置前的环境变量备份在 %APPDATA%\IPTool\terminal-proxy.json
    /// </summary>
    public static class TerminalProxyConfigurator
    {
        private static readonly string BackupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IPTool", "terminal-proxy.json");

        public static TerminalProxyBackup LoadBackup()
        {
            try
            {
                return JsonFile.Read<TerminalProxyBackup>(BackupPath);
            }
            catch (Exception ex) when (ex is SerializationException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// 逐项执行，返回失败的项及原因（全部成功时为空）。clearing 为 true 表示这是“清除”
        /// </summary>
        public static List<string> Apply(IList<ProxyChange> changes, bool clearing)
        {
            var errors = new List<string>();
            List<ProxyChange> env = changes.Where(c => c.Target == ProxyChangeTarget.Environment).ToList();
            if (env.Count > 0)
            {
                try
                {
                    UpdateBackup(env, clearing);
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Environment"))
                    {
                        foreach (ProxyChange change in env)
                        {
                            if (change.NewValue == null) key.DeleteValue(change.Key, false);
                            else key.SetValue(change.Key, change.NewValue, RegistryValueKind.String);
                        }
                    }
                    BroadcastEnvironmentChange();
                }
                catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException)
                {
                    errors.Add("环境变量：" + ex.Message);
                }
            }

            foreach (ProxyChange change in changes.Where(c => c.Target == ProxyChangeTarget.Git))
            {
                int exitCode;
                if (change.NewValue == null) TerminalProxyScanner.GitConfig(out exitCode, "--global", "--unset-all", change.Key);
                else TerminalProxyScanner.GitConfig(out exitCode, "--global", change.Key, change.NewValue);
                // --unset-all 在项不存在时返回 5，视为成功
                if (exitCode != 0 && !(change.NewValue == null && exitCode == 5)) errors.Add($"Git {change.Key}：git config 失败（{exitCode}）");
            }

            UpdateFile(changes.Where(c => c.Target == ProxyChangeTarget.Npm).ToList(), TerminalProxyScanner.NpmrcPath, "npm", errors);
            UpdateFile(changes.Where(c => c.Target == ProxyChangeTarget.Pip).ToList(), TerminalProxyScanner.PipIniPath, "pip", errors);
            return errors;
        }

        /// <summary>
        /// 第一次设置时记下原来的值；清除（恢复原值）后备份失去意义，删掉
        /// </summary>
        private static void UpdateBackup(List<ProxyChange> env, bool clearing)
        {
            if (clearing)
            {
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                return;
            }

            TerminalProxyBackup backup = LoadBackup() ?? new TerminalProxyBackup();
            if (backup.Written == null) backup.Written = new List<string>();
            foreach (ProxyChange change in env.Where(c => c.NewValue != null))
            {
                if (backup.Wrote(change.Key)) continue;
                backup.Written.Add(change.Key);
                if (change.OldValue != null) backup.Previous[change.Key] = change.OldValue;
            }
            JsonFile.WriteAtomic(BackupPath, backup);
        }

        private static void UpdateFile(List<ProxyChange> changes, string path, string name, List<string> errors)
        {
            if (changes.Count == 0) return;
            try
            {
                string text = File.ReadAllText(path);
                foreach (ProxyChange change in changes) text = ConfigLines.Update(text, change.Key, change.NewValue);
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                errors.Add(name + "：" + ex.Message);
            }
        }

        #region 通知系统

        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);
        private const int WmSettingChange = 0x001A;
        private const int SmtoAbortIfHung = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

        /// <summary>
        /// 通知资源管理器等程序重新读取环境变量，之后新开的终端即可拿到新值
        /// </summary>
        public static void BroadcastEnvironmentChange()
        {
            IntPtr result;
            try
            {
                SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung, 1000, out result);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
        }

        #endregion
    }
}
