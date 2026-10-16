using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Core.Diagnosis;
using IP_UpdateTest.Core.TerminalProxy;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 终端代理：v2rayN、Clash 等只设置系统代理，在 cmd、PowerShell 里运行的 git、curl、npm 等只认环境变量，不会跟着走。
    /// 这里一键检查各处的代理设置，一键设置或清除，并给出让已打开的终端立即生效的命令
    /// </summary>
    public class FrmTerminalProxy : ThemedForm
    {
        private const string ApplyHint = "一键设置写入用户环境变量，新开的终端自动生效；VS Code 等已打开的程序需要重启。";
        private const string ClearHint = "清除只删除指向本机代理的设置，新开的终端恢复直连；已打开的终端粘贴上面的命令即可。";
        private const string IdleHint = "启动代理软件后按 F5 或点“重新检测”。";
        private const string ManualHint = "系统级变量和不指向本机的设置“清除”不会改；已打开的终端粘贴上面的命令可先临时清掉。";

        private readonly AppSettings settings;
        private readonly Action<bool> setFollow;
        private readonly TerminalProxyView view = new TerminalProxyView();
        private readonly Card pnlSnippet = new Card();
        private readonly SelectBox cbxProxy = new SelectBox { Glyph = Glyph.Zap, Placeholder = "没有发现本机代理端口" };
        private readonly Segmented segShell = new Segmented();
        private readonly CodeBlock code = new CodeBlock { Placeholder = "启动 v2rayN、Clash 等代理软件后，这里会生成设置命令", RightInset = 40 };
        private readonly SpringButton btnCopy = new SpringButton { Kind = ButtonKind.Ghost, Glyph = Glyph.Copy };
        private readonly SpringButton btnRescan = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Refresh, Text = "重新检测" };
        private readonly Toggle chkFollow = new Toggle { Text = "跟随系统代理" };
        private readonly SpringButton btnClear = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Trash, Text = "清除" };
        private readonly SpringButton btnApply = new SpringButton { Kind = ButtonKind.Primary, Text = "一键设置" };

        private TerminalProxySnapshot snapshot;
        private TerminalProxyReport report;
        private bool running;
        private bool binding;
        private bool refreshPending;

        /// <param name="setFollow">开关“跟随系统代理”（由主窗口启停后台同步）</param>
        public FrmTerminalProxy(AppSettings settings, Action<bool> setFollow) : this(settings, setFollow, null)
        {
        }

        /// <summary>
        /// 直接显示给定的检测数据，不实际检测（界面截图测试用）
        /// </summary>
        internal FrmTerminalProxy(AppSettings settings, Action<bool> setFollow, TerminalProxySnapshot sample)
        {
            this.settings = settings;
            this.setFollow = setFollow;
            Text = "终端代理";
            TitleBar.Subtitle = "cmd · PowerShell · git · curl";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            SetLogicalSize(760, 760);

            segShell.SetItems(ShellSnippets.NameOf(ShellKind.PowerShell), ShellSnippets.NameOf(ShellKind.Cmd), ShellSnippets.NameOf(ShellKind.Bash));
            segShell.SelectedIndex = 0;
            cbxProxy.Format = o => ((ProxyCandidate)o).Url;
            cbxProxy.Detail = o => Describe((ProxyCandidate)o);
            cbxProxy.ItemGlyph = o => Glyph.Zap;
            chkFollow.Checked = settings != null && settings.FollowSystemProxy;

            InkToolTip tips = InkToolTip.Shared;
            tips.SetToolTip(btnCopy, "复制命令（Ctrl+C）");
            tips.SetToolTip(chkFollow, "代理软件开关系统代理或换端口时，自动设置或清除命令行代理。需要 IP Tool 在托盘运行");
            tips.SetToolTip(cbxProxy, "一键设置使用的代理地址");

            // 复制按钮放在代码块里：透明背景只会透出父控件，放在卡片上会露出一块白底
            code.Controls.Add(btnCopy);
            pnlSnippet.Controls.AddRange(new Control[] { cbxProxy, segShell, code });
            Controls.AddRange(new Control[] { view, pnlSnippet, btnRescan, chkFollow, btnClear, btnApply });
            int tab = 0;
            foreach (Control c in new Control[] { view, pnlSnippet, cbxProxy, segShell, code, btnRescan, chkFollow, btnClear, btnApply })
                c.TabIndex = tab++;

            cbxProxy.SelectedIndexChanged += async (s, e) => await ProxySelectedAsync();
            segShell.SelectedIndexChanged += (s, e) => UpdateSnippet();
            btnCopy.Click += (s, e) => CopySnippet();
            btnRescan.Click += async (s, e) => await ScanAsync(true);
            btnApply.Click += async (s, e) => await ApplyAsync(false);
            btnClear.Click += async (s, e) => await ApplyAsync(true);
            chkFollow.CheckedChanged += (s, e) => FollowChanged();
            pnlSnippet.Paint += (s, e) =>
            {
                Shapes.Prepare(e.Graphics);
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                PaintHint(e.Graphics);
            };

            UpdateButtons();
            Load += async (s, e) =>
            {
                if (sample == null)
                {
                    await ScanAsync(true);
                    return;
                }
                snapshot = sample;
                BindCandidates();
                Analyze(true);
            };
        }

        private static string Describe(ProxyCandidate c)
        {
            var parts = new List<string>();
            if (c.Owner != null) parts.Add(c.Owner);
            parts.Add(c.Protocol);
            if (c.IsSystemProxy) parts.Add("系统代理");
            return string.Join(" · ", parts);
        }

        protected override void LayoutContent(float s)
        {
            int gutter = P(Metrics.PageGutter);
            int top = P(Metrics.TitleBarHeight + 4);
            int h = P(Metrics.ButtonHeight);
            int footerH = P(Metrics.PrimaryHeight);
            int footerY = ClientSize.Height - P(20) - footerH;
            int width = ClientSize.Width - gutter * 2;

            // 命令卡片：代理地址、终端类型、命令、说明
            int pad = P(16);
            int hintH = HintHeight(width - pad * 2);
            int snippetH = pad + P(36) + P(12) + P(82) + P(10) + hintH + P(14);
            pnlSnippet.SetBounds(gutter, footerY - P(16) - snippetH, width, snippetH);
            int inner = width - pad * 2;
            int segW = Math.Min(P(330), inner / 2);
            segShell.SetBounds(pnlSnippet.Width - pad - segW, pad, segW, P(36));
            cbxProxy.Place(new Rectangle(pad, pad - P(2), segShell.Left - P(12) - pad, P(40)));
            code.SetBounds(pad, pad + P(36) + P(12), inner, P(82));
            int copy = P(32);
            btnCopy.SetBounds(code.Width - P(6) - copy, P(6), copy, copy);

            view.SetBounds(gutter, top, width, pnlSnippet.Top - P(12) - top);

            int applyW = Math.Max(P(128), btnApply.PreferredWidth);
            btnApply.SetBounds(ClientSize.Width - gutter - applyW, footerY, applyW, footerH);
            int clearW = Math.Max(P(84), btnClear.PreferredWidth);
            btnClear.SetBounds(btnApply.Visible ? btnApply.Left - P(8) - clearW : ClientSize.Width - gutter - clearW, footerY + (footerH - h) / 2, clearW, h);
            if (!btnApply.Visible)
            {
                // 没有可用代理、只剩清除时，清除就是主操作
                btnClear.SetBounds(ClientSize.Width - gutter - applyW, footerY, applyW, footerH);
            }
            int rescanW = Math.Max(P(100), btnRescan.PreferredWidth);
            btnRescan.SetBounds(gutter, footerY + (footerH - h) / 2, rescanW, h);
            chkFollow.SetBounds(btnRescan.Right + P(16), footerY + (footerH - P(26)) / 2, chkFollow.PreferredWidth, P(26));
        }

        /// <summary>
        /// 命令卡片底部的说明，随当前能做的操作变化
        /// </summary>
        private string Hint
        {
            get
            {
                if (report == null || report.Target != null) return ApplyHint;
                if (report.StaleAfterClear != null) return ManualHint;
                return report.ClearChanges.Count > 0 ? ClearHint : IdleHint;
            }
        }

        private int HintHeight(int width)
        {
            float s = S;
            return (int)Math.Ceiling(Typo.Wrap(Hint, TextStyle.Small, width, s).Count * Typo.LineHeight(TextStyle.Small, s));
        }

        /// <summary>
        /// 说明文字画在命令卡片底部
        /// </summary>
        private void PaintHint(Graphics g)
        {
            float s = S;
            float x = P(16);
            float y = code.Bottom + P(10);
            float lineHeight = Typo.LineHeight(TextStyle.Small, s);
            foreach (string line in Typo.Wrap(Hint, TextStyle.Small, pnlSnippet.Width - x * 2, s))
            {
                Typo.DrawLine(g, line, TextStyle.Small, Palette.Ink3, x, Typo.CenterBaseline(new RectangleF(0, y, pnlSnippet.Width, lineHeight), TextStyle.Small, s), s);
                y += lineHeight;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C) && !string.IsNullOrEmpty(code.Text) && !HasSheet)
            {
                CopySnippet();
                return true;
            }
            if (keyData == Keys.F5 && !running)
            {
                btnRescan.PerformClick();
                return true;
            }
            // 打开着的确认框自己处理 Esc
            if (keyData == Keys.Escape && !HasSheet)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        #region 检测

        /// <summary>
        /// full 为 false 时是设置之后的复查：不显示加载画面，结果原地刷新
        /// </summary>
        private async Task ScanAsync(bool full)
        {
            if (running) return;
            running = true;
            refreshPending = false;
            UpdateButtons();
            if (full)
            {
                btnRescan.BeginLoading();
                view.ShowMessage("正在检测：代理软件与端口 → 系统代理 → 环境变量 → Git / npm / pip，约需 3 秒", true);
            }
            try
            {
                // 用户选的不是默认代理时也一并实测，不然重新检测后它的结果就丢了
                TerminalProxySnapshot result = await TerminalProxyScanner.ScanAsync((cbxProxy.SelectedItem as ProxyCandidate)?.Endpoint);
                if (IsDisposed) return;
                snapshot = result;
                BindCandidates();
                Analyze(full);
                if (full) btnRescan.Reset();
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                if (IsDisposed) return;
                // 不再沿用上一次的结果：按钮和命令都要跟着失效
                snapshot = null;
                report = null;
                UpdateSnippet();
                view.ShowMessage("检测失败：" + ex.Message, false);
                btnRescan.Fail("检测失败");
            }
            finally
            {
                if (!IsDisposed)
                {
                    running = false;
                    UpdateButtons();
                }
            }
            // 检测期间后台跟随又改了设置
            if (refreshPending && !IsDisposed) await ScanAsync(false);
        }

        /// <summary>
        /// 后台跟随系统代理改了环境变量后原地刷新
        /// </summary>
        internal async void RefreshAfterSync()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (running)
            {
                refreshPending = true;
                return;
            }
            await ScanAsync(false);
        }

        private void BindCandidates()
        {
            ProxyCandidate previous = cbxProxy.SelectedItem as ProxyCandidate;
            ProxyCandidate pick = (previous == null ? null : snapshot.Candidates.FirstOrDefault(c => c.Endpoint == previous.Endpoint))
                ?? TerminalProxyAnalyzer.PickTarget(snapshot);
            binding = true;
            cbxProxy.SetItems(snapshot.Candidates.Cast<object>(), pick == null ? -1 : snapshot.Candidates.IndexOf(pick));
            binding = false;
        }

        private void Analyze(bool animate)
        {
            report = TerminalProxyAnalyzer.Analyze(snapshot, cbxProxy.SelectedItem as ProxyCandidate, TerminalProxyConfigurator.LoadBackup());
            view.ShowReport(report, animate);
            UpdateSnippet();
            UpdateButtons();
        }

        private async Task ProxySelectedAsync()
        {
            if (binding || running || snapshot == null) return;
            var target = cbxProxy.SelectedItem as ProxyCandidate;
            Analyze(false);
            if (target == null || !target.Http || snapshot.Tunnels.ContainsKey(target.Endpoint)) return;
            // 换了代理就实测一下这个
            await TerminalProxyScanner.TestTunnelAsync(snapshot, target);
            if (!IsDisposed && cbxProxy.SelectedItem == target) Analyze(false);
        }

        #endregion

        #region 设置

        private void UpdateButtons()
        {
            bool ready = !running && report != null;
            bool hasTarget = report != null && report.Target != null;
            int applyCount = report == null ? 0 : report.ApplyChanges.Count;
            int clearCount = report == null ? 0 : report.ClearChanges.Count;
            // 还有一键设置改不了的问题（如 pip 不支持 SOCKS）时不能说“已是最新”
            bool manual = hasTarget && applyCount == 0 && report.Rows.Any(r => (r.Key == "env" || r.Key == "git" || r.Key == "npm" || r.Key == "pip")
                && (r.Status == CheckStatus.Fail || r.Status == CheckStatus.Warning));
            btnApply.Visible = hasTarget || report == null;
            btnApply.Enabled = ready && applyCount > 0;
            btnApply.Text = hasTarget && applyCount == 0 && !manual ? "已是最新" : "一键设置";
            btnClear.Enabled = ready && clearCount > 0;
            btnClear.Kind = btnApply.Visible ? ButtonKind.Secondary : ButtonKind.Primary;
            btnRescan.Enabled = !running;
            cbxProxy.Enabled = !running && snapshot != null && snapshot.Candidates.Count > 1;
            InkToolTip.Shared.SetToolTip(btnApply, applyCount > 0 ? "将修改：\n" + string.Join("\n", report.ApplyChanges.Select(c => c.ToString()))
                : manual ? "剩下的问题需要手动处理，见上方说明" : "各处已指向当前代理");
            InkToolTip.Shared.SetToolTip(btnClear, clearCount > 0 ? "将修改：\n" + string.Join("\n", report.ClearChanges.Select(c => c.ToString()))
                : "删除指向本机代理的环境变量和 Git / npm / pip 代理项，恢复原来的设置");
            PerformLayout();
        }

        private void UpdateSnippet()
        {
            var shell = (ShellKind)Math.Max(0, segShell.SelectedIndex);
            if (report != null && report.Target != null) code.Text = ShellSnippets.Build(shell, report.Target.Url, report.NoProxy);
            else if (report != null && (report.ClearChanges.Count > 0 || report.StaleAfterClear != null)) code.Text = ShellSnippets.Build(shell, null);
            else code.Text = "";
            btnCopy.Visible = !string.IsNullOrEmpty(code.Text);
            pnlSnippet.Invalidate();
        }

        private void CopySnippet()
        {
            if (string.IsNullOrEmpty(code.Text)) return;
            try
            {
                Clipboard.SetText(code.Text);
                Notify(NoticeKind.Success, "已复制，粘贴到 " + ShellSnippets.NameOf((ShellKind)Math.Max(0, segShell.SelectedIndex)) + " 中回车即可生效");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Notify(NoticeKind.Warning, "剪贴板被其他程序占用，请稍后再试");
            }
        }

        private async Task ApplyAsync(bool clearing)
        {
            if (running || report == null) return;
            List<ProxyChange> changes = clearing ? report.ClearChanges : report.ApplyChanges;
            if (changes.Count == 0) return;
            SpringButton button = clearing ? btnClear : btnApply;
            running = true;
            UpdateButtons();
            button.BeginLoading();
            List<string> errors = await Task.Run(() => TerminalProxyConfigurator.Apply(changes, clearing));
            if (IsDisposed) return;
            running = false;
            if (errors.Count == 0)
            {
                button.Succeed();
                Notify(NoticeKind.Success, !clearing ? $"已设置 {changes.Count} 项，新开的终端即可走代理"
                    : report.StaleAfterClear != null ? $"已清除 {changes.Count} 项；{report.StaleAfterClear} 还需要手动删除"
                    : changes.Any(c => c.NewValue != null) ? $"已清除 {changes.Count} 项，恢复了原来的代理设置"
                    : $"已清除 {changes.Count} 项，新开的终端恢复直连");
            }
            else
            {
                button.Fail(clearing ? "清除失败" : "设置失败");
                Notify(NoticeKind.Error, errors[0]);
            }
            await ScanAsync(false);
        }

        private void FollowChanged()
        {
            if (settings == null || settings.FollowSystemProxy == chkFollow.Checked) return;
            settings.FollowSystemProxy = chkFollow.Checked;
            if (setFollow != null) setFollow(chkFollow.Checked);
            if (!chkFollow.Checked)
                Notify(NoticeKind.Success, "已关闭跟随系统代理");
            else if (!settings.MinimizeToTray)
                Notify(NoticeKind.Warning, "已开启。建议同时开启“关闭时最小化到托盘”，否则关闭窗口后不再跟随");
            else
                Notify(NoticeKind.Success, "已开启：系统代理变化时，命令行代理自动跟随");
        }

        #endregion
    }
}
