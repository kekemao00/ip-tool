using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core.TerminalProxy;

namespace IP_UpdateTest
{
    /// <summary>
    /// 主窗口中的终端代理：打开“终端代理”窗口，以及在后台跟随系统代理
    /// </summary>
    public partial class FrmMain
    {
        private TerminalProxyFollower terminalProxyFollower;

        private void btnTerminalProxy_Click(object sender, EventArgs e)
        {
            OpenTerminalProxy();
        }

        private void OpenTerminalProxy()
        {
            FrmTerminalProxy open = OwnedForms.OfType<FrmTerminalProxy>().FirstOrDefault(f => !f.IsDisposed);
            if (open != null)
            {
                // Activate 不会还原最小化的窗口
                if (open.WindowState == FormWindowState.Minimized) open.WindowState = FormWindowState.Normal;
                open.Activate();
                return;
            }
            new FrmTerminalProxy(settings, enabled =>
            {
                SaveSettings();
                SetFollowSystemProxy(enabled);
                // 用户关掉跟随：忘掉同步到的状态，下次开启时第一次读到的状态只作为基准
                if (!enabled) Task.Run(() => TerminalProxyConfigurator.SaveFollowState(null));
            }).Show(this);
        }

        /// <summary>
        /// 启停“命令行代理跟随系统代理”
        /// </summary>
        private void SetFollowSystemProxy(bool enabled)
        {
            if (enabled && terminalProxyFollower == null)
            {
                terminalProxyFollower = new TerminalProxyFollower();
                terminalProxyFollower.Synced += message => OnFollowerEvent(message, StatusKind.Success);
                terminalProxyFollower.Failed += message => OnFollowerEvent(message, StatusKind.Error);
            }
            else if (!enabled && terminalProxyFollower != null)
            {
                terminalProxyFollower.Dispose();
                terminalProxyFollower = null;
            }
        }

        /// <summary>
        /// 后台跟随改了环境变量：状态栏提示，打开着的终端代理窗口原地刷新（在线程池线程上调用）
        /// </summary>
        private void OnFollowerEvent(string message, StatusKind kind)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                SetStatus(message, kind);
                foreach (FrmTerminalProxy dialog in OwnedForms.OfType<FrmTerminalProxy>().Where(f => !f.IsDisposed))
                    dialog.RefreshAfterSync();
            }));
        }
    }
}
