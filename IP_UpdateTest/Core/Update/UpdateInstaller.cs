using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IP_UpdateTest.Core.Update
{
    /// <summary>
    /// 替换程序文件并重启。
    /// Windows 允许重命名正在运行的 exe：先把旧文件改名为 *.old，再把新文件复制到原位置，任何一步失败都改回去；
    /// 程序所在文件夹没有写权限（如 Program Files）时，以管理员身份运行本程序的 --install-update 来做同样的事
    /// </summary>
    public static class UpdateInstaller
    {
        public const string InstallArgument = "--install-update";
        public const string WaitArgument = "--wait-pid";
        public const string UpdatedFromArgument = "--updated-from";
        private const string BackupSuffix = ".old";

        private const int ExitOk = 0;
        private const int ExitFailed = 1;
        private const int ErrorCancelled = 1223;

        /// <summary>
        /// 把 sourceDir 下的文件放到 targetDir，失败时恢复原来的文件并抛出异常
        /// </summary>
        public static void Install(string sourceDir, string targetDir)
        {
            string[] files = Directory.GetFiles(sourceDir);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            if (files.Length == 0) throw new IOException("没有要安装的文件");

            var placed = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (string source in files)
                {
                    string target = Path.Combine(targetDir, Path.GetFileName(source));
                    string backup = null;
                    if (File.Exists(target))
                    {
                        backup = FreeBackupPath(target);
                        File.Move(target, backup);
                    }
                    placed.Add(new KeyValuePair<string, string>(target, backup));
                    File.Copy(source, target, false);
                }
            }
            catch
            {
                Rollback(placed);
                throw;
            }
        }

        private static void Rollback(List<KeyValuePair<string, string>> placed)
        {
            for (int i = placed.Count - 1; i >= 0; i--)
            {
                string target = placed[i].Key, backup = placed[i].Value;
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (backup != null) File.Move(backup, target);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>
        /// IPTool.exe.old；上次留下的 .old 还删不掉（旧进程未退出）时用 .old1、.old2…
        /// </summary>
        private static string FreeBackupPath(string target)
        {
            for (int i = 0; ; i++)
            {
                string path = target + BackupSuffix + (i == 0 ? "" : i.ToString());
                if (!File.Exists(path)) return path;
                try
                {
                    File.Delete(path);
                    return path;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (i > 20) throw;
                }
            }
        }

        /// <summary>
        /// 删除上次更新留下的旧文件（新版本启动时调用）
        /// </summary>
        public static void CleanupBackups(string directory)
        {
            try
            {
                foreach (string path in Directory.GetFiles(directory, "IPTool*" + BackupSuffix + "*"))
                {
                    string name = Path.GetFileName(path);
                    int at = name.LastIndexOf(BackupSuffix, StringComparison.OrdinalIgnoreCase);
                    string rest = name.Substring(at + BackupSuffix.Length);
                    if (rest.Length > 0 && !rest.All(char.IsDigit)) continue;
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// 能否在文件夹中创建和重命名文件
        /// </summary>
        public static bool CanWrite(string directory)
        {
            string probe = Path.Combine(directory, ".iptool-write-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// 安装到 targetDir：能写就直接替换，否则请求管理员权限。用户取消 UAC 时返回 false
        /// </summary>
        public static async Task<bool> InstallAsync(string sourceDir, string targetDir, string executablePath)
        {
            if (CanWrite(targetDir))
            {
                await Task.Run(() => Install(sourceDir, targetDir));
                return true;
            }

            var psi = new ProcessStartInfo(executablePath, $"{InstallArgument} \"{sourceDir.TrimEnd('\\')}\" \"{targetDir.TrimEnd('\\')}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return false;
            }
            using (process)
            {
                await Task.Run(() => process.WaitForExit());
                if (process.ExitCode != ExitOk) throw new IOException("以管理员身份替换程序文件失败");
            }
            return true;
        }

        public static bool IsInstallInvocation(string[] args)
        {
            return args.Length > 0 && string.Equals(args[0], InstallArgument, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 以管理员身份运行时的入口：--install-update 源目录 目标目录
        /// </summary>
        public static int RunFromCommandLine(string[] args)
        {
            if (args.Length < 3) return ExitFailed;
            try
            {
                Install(args[1], args[2]);
                return ExitOk;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return ExitFailed;
            }
        }

        /// <summary>
        /// 启动新版本，带上旧进程号（新进程等它退出后再启动界面）和更新前的版本
        /// </summary>
        public static void Relaunch(string executablePath, Version previous, string extraArguments)
        {
            string arguments = $"{WaitArgument} {Process.GetCurrentProcess().Id} {UpdatedFromArgument} {Versions.Normalize(previous).ToString(3)}";
            if (!string.IsNullOrEmpty(extraArguments)) arguments += " " + extraArguments;
            Process.Start(new ProcessStartInfo(executablePath, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executablePath) });
        }

        /// <summary>
        /// 带 --wait-pid 启动时等旧进程退出（最多 15 秒），否则会被单实例检查挡住
        /// </summary>
        public static void WaitForPreviousInstance(string[] args)
        {
            int index = Array.FindIndex(args, a => string.Equals(a, WaitArgument, StringComparison.OrdinalIgnoreCase));
            int pid;
            if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out pid)) return;
            try
            {
                using (Process previous = Process.GetProcessById(pid))
                {
                    previous.WaitForExit(15000);
                }
            }
            catch (ArgumentException)
            {
                // 已经退出
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
