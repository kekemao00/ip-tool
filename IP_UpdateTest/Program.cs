using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using IP_UpdateTest.Core;

namespace IP_UpdateTest
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。带操作参数时按命令行方式运行，返回退出码。
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            if (CommandLine.IsCliInvocation(args)) return CommandLine.Run(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            using (var mutex = new Mutex(true, SingleInstance.MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    SingleInstance.ActivateExisting();
                    return 0;
                }

                bool startInTray = args.Contains(AutoStart.TrayArgument, StringComparer.OrdinalIgnoreCase);
                Application.Run(new FrmMain(GetArgument(args, "--adapter"), startInTray));
            }
            return 0;
        }

        /// <summary>
        /// 取命令行参数的值，如 --adapter {GUID}；不存在时返回 null
        /// </summary>
        private static string GetArgument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }

    /// <summary>
    /// 单实例：同一权限级别只运行一个界面，再次启动时显示已运行的窗口
    /// </summary>
    internal static class SingleInstance
    {
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);

        public static readonly int ShowMainWindowMessage = RegisterWindowMessage("IPTool.ShowMainWindow");

        /// <summary>
        /// 普通权限与管理员身份各自单实例，否则以管理员身份重启时会被尚未退出的普通实例挡住
        /// </summary>
        public static string MutexName
        {
            get { return @"Local\IPTool.SingleInstance." + (Elevation.IsAdministrator() ? "Admin" : "User"); }
        }

        public static void ActivateExisting()
        {
            PostMessage(HwndBroadcast, ShowMainWindowMessage, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
