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
    /// 执行一键设置 / 清除：写用户级环境变量并通知系统，改 Git、npm、pip 的代理项。改之前的值备份在 %APPDATA%\IPTool\terminal-proxy.json
    /// </summary>
    public static class TerminalProxyConfigurator
    {
        private static readonly string BackupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IPTool", "terminal-proxy.json");

        // 窗口里的一键设置和后台跟随可能同时写同一份备份
        private static readonly object Gate = new object();

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
            lock (Gate)
            {
                var errors = new List<string>();
                TerminalProxyBackup backup = LoadBackup() ?? new TerminalProxyBackup();
                if (!clearing)
                {
                    // 先记下原来的值再改：备份写不进去就不改，免得清除时恢复不了
                    foreach (ProxyChange change in changes) backup.Record(TerminalProxyBackup.KeyOf(change.Target, change.Key), change.OldValue, change.NewValue);
                    if (!SaveBackup(backup, errors)) return errors;
                }

                var done = new List<ProxyChange>();
                List<ProxyChange> env = changes.Where(c => c.Target == ProxyChangeTarget.Environment).ToList();
                if (env.Count > 0)
                {
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Environment"))
                        {
                            foreach (ProxyChange change in env)
                            {
                                if (change.NewValue == null) key.DeleteValue(change.Key, false);
                                else key.SetValue(change.Key, change.NewValue, RegistryValueKind.String);
                                done.Add(change);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException)
                    {
                        errors.Add("环境变量：" + ex.Message);
                    }
                    BroadcastEnvironmentChange();
                }

                foreach (ProxyChange change in changes.Where(c => c.Target == ProxyChangeTarget.Git))
                {
                    int exitCode;
                    // --replace-all：同一项写了多次时 git config key value 会失败（5）
                    if (change.NewValue == null) TerminalProxyScanner.GitConfig(out exitCode, "--global", "--unset-all", change.Key);
                    else TerminalProxyScanner.GitConfig(out exitCode, "--global", "--replace-all", change.Key, change.NewValue);
                    // --unset-all 在项不存在时返回 5，视为成功
                    if (exitCode == 0 || (change.NewValue == null && exitCode == 5)) done.Add(change);
                    else errors.Add($"Git {change.Key}：git config 失败（{exitCode}）");
                }

                UpdateFile(changes.Where(c => c.Target == ProxyChangeTarget.Npm).ToList(), TerminalProxyScanner.NpmrcPath, "npm", done, errors);
                UpdateFile(changes.Where(c => c.Target == ProxyChangeTarget.Pip).ToList(), TerminalProxyScanner.PipIniPath, "pip", done, errors);

                if (clearing && done.Count > 0)
                {
                    // 已恢复的项不再需要备份；没改成的留着，下次清除还能恢复
                    foreach (ProxyChange change in done) backup.Forget(TerminalProxyBackup.KeyOf(change.Target, change.Key));
                    SaveBackup(backup, null);
                }
                return errors;
            }
        }

        private static bool SaveBackup(TerminalProxyBackup backup, List<string> errors)
        {
            try
            {
                if (!backup.IsEmpty) JsonFile.WriteAtomic(BackupPath, backup);
                else if (File.Exists(BackupPath)) File.Delete(BackupPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException || ex is SerializationException)
            {
                errors?.Add("备份原设置失败：" + ex.Message);
                return false;
            }
        }

        private static void UpdateFile(List<ProxyChange> changes, string path, string name, List<ProxyChange> done, List<string> errors)
        {
            if (changes.Count == 0) return;
            try
            {
                // 按文件原来的编码读写：中文 Windows 上 pip.ini 常是 GBK，当 UTF-8 读写会把中文路径、注释弄坏
                Encoding encoding;
                string text = ReadText(File.ReadAllBytes(path), out encoding);
                foreach (ProxyChange change in changes) text = ConfigLines.Update(text, change.Key, change.NewValue);
                File.WriteAllText(path, text, encoding);
                done.AddRange(changes);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                errors.Add(name + "：" + ex.Message);
            }
        }

        /// <summary>
        /// 按 BOM 或内容判断编码：有 BOM 的照旧；没有 BOM 时是合法 UTF-8 就当 UTF-8，否则是系统的 ANSI 代码页（中文系统为 GBK）
        /// </summary>
        internal static string ReadText(byte[] data, out Encoding encoding)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) encoding = new UTF8Encoding(true);
            else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) encoding = new UnicodeEncoding(false, true);
            else if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) encoding = new UnicodeEncoding(true, true);
            else
            {
                try
                {
                    new UTF8Encoding(false, true).GetString(data);
                    encoding = new UTF8Encoding(false);
                }
                catch (DecoderFallbackException)
                {
                    encoding = Encoding.Default;
                }
                return encoding.GetString(data);
            }
            int bom = encoding.GetPreamble().Length;
            return encoding.GetString(data, bom, data.Length - bom);
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
