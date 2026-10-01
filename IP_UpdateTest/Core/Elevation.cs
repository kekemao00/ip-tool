using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace IP_UpdateTest.Core
{
    /// <summary>
    /// 管理员权限检测与提权重启
    /// </summary>
    public static class Elevation
    {
        /// <summary>
        /// 用户在 UAC 对话框中点了“否”
        /// </summary>
        private const int ErrorCancelled = 1223;

        /// <summary>
        /// 当前进程是否以管理员身份运行
        /// </summary>
        public static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// 以管理员身份启动本程序；用户取消 UAC 时返回 null
        /// </summary>
        public static Process StartElevated(string arguments)
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath, arguments ?? "")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            try
            {
                return Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return null;
            }
        }
    }
}
