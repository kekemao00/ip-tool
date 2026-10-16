using System;
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
            new FrmTerminalProxy(settings, enabled =>
            {
                SaveSettings();
                SetFollowSystemProxy(enabled);
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
                terminalProxyFollower.Synced += message =>
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new Action(() => SetStatus(message, StatusKind.Success)));
                };
            }
            else if (!enabled && terminalProxyFollower != null)
            {
                terminalProxyFollower.Dispose();
                terminalProxyFollower = null;
            }
        }
    }
}
