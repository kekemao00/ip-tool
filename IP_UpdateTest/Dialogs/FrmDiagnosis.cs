using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Core.Diagnosis;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 分层网络诊断：从本机网卡到外网逐个环节检测，指出问题所在环节并给出建议，可复制或导出
    /// </summary>
    public class FrmDiagnosis : ThemedForm
    {
        private readonly NetworkAdapter adapter;
        private readonly ReportView view = new ReportView();
        private readonly SpringButton btnCopy = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Copy, Text = "复制" };
        private readonly SpringButton btnExport = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Download, Text = "导出" };
        private readonly SpringButton btnClose = new SpringButton { Kind = ButtonKind.Secondary, Text = "关闭" };
        private readonly SpringButton btnRerun = new SpringButton { Kind = ButtonKind.Primary, Glyph = Glyph.Refresh, Text = "重新诊断" };
        private bool running;

        public FrmDiagnosis(NetworkAdapter adapter) : this(adapter, null)
        {
        }

        /// <summary>
        /// 直接显示给定的报告，不运行诊断（界面截图测试用）
        /// </summary>
        internal FrmDiagnosis(NetworkAdapter adapter, DiagnosisReport report)
        {
            this.adapter = adapter;
            Text = "网络诊断";
            TitleBar.Subtitle = adapter == null ? null : adapter.Name;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            SetLogicalSize(760, 640);

            InkToolTip.Shared.SetToolTip(btnCopy, "复制为文本（Ctrl+C）");
            InkToolTip.Shared.SetToolTip(btnExport, "导出为文本或 JSON");
            btnCopy.Click += (s, e) => CopyReport();
            btnExport.Click += async (s, e) => await ExportAsync();
            btnClose.Click += (s, e) => Close();
            btnRerun.Click += async (s, e) => await RunAsync();

            Controls.AddRange(new Control[] { view, btnCopy, btnExport, btnClose, btnRerun });
            int tab = 0;
            foreach (Control c in new Control[] { view, btnCopy, btnExport, btnClose, btnRerun })
                c.TabIndex = tab++;
            CancelButton = btnClose;

            Load += async (s, e) =>
            {
                if (report == null)
                {
                    await RunAsync();
                    return;
                }
                view.ShowReport(report);
                btnCopy.Enabled = btnExport.Enabled = true;
            };
        }

        protected override void LayoutContent(float s)
        {
            int gutter = P(Metrics.PageGutter);
            int top = P(Metrics.TitleBarHeight + 4);
            int h = P(Metrics.ButtonHeight);
            int footerH = P(Metrics.PrimaryHeight);
            int footerY = ClientSize.Height - P(20) - footerH;
            view.SetBounds(gutter, top, ClientSize.Width - gutter * 2, footerY - P(16) - top);

            int rerunW = Math.Max(P(132), btnRerun.PreferredWidth);
            btnRerun.SetBounds(ClientSize.Width - gutter - rerunW, footerY, rerunW, footerH);
            int closeW = Math.Max(P(84), btnClose.PreferredWidth);
            btnClose.SetBounds(btnRerun.Left - P(8) - closeW, footerY + (footerH - h) / 2, closeW, h);
            int y = footerY + (footerH - h) / 2;
            btnCopy.SetBounds(gutter, y, Math.Max(P(84), btnCopy.PreferredWidth), h);
            btnExport.SetBounds(btnCopy.Right + P(8), y, Math.Max(P(84), btnExport.PreferredWidth), h);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C) && view.Report != null && !HasSheet)
            {
                CopyReport();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async Task RunAsync()
        {
            if (running) return;
            running = true;
            btnCopy.Enabled = btnExport.Enabled = false;
            btnRerun.BeginLoading();
            view.ShowMessage("正在诊断：本机网卡 → 路由器 → 外网 → DNS → 代理/VPN → UDP，约需 10 秒", true);
            DiagnosisReport report = null;
            try
            {
                report = await NetworkDiagnosis.RunAsync(adapter);
                if (IsDisposed) return;
                view.ShowReport(report);
                btnRerun.Succeed();
                if (report.FaultLayer.HasValue) Notify(NoticeKind.Warning, "诊断完成：发现问题，建议见报告");
                else Notify(NoticeKind.Success, "诊断完成：各环节正常");
            }
            catch (Exception ex) when (ex is System.Net.NetworkInformation.NetworkInformationException
                || ex is System.Net.Sockets.SocketException || ex is InvalidOperationException)
            {
                if (IsDisposed) return;
                view.ShowMessage("诊断失败：" + ex.Message, false);
                btnRerun.Fail("诊断失败");
            }
            finally
            {
                if (!IsDisposed)
                {
                    running = false;
                    btnCopy.Enabled = btnExport.Enabled = report != null;
                }
            }
        }

        private void CopyReport()
        {
            DiagnosisReport report = view.Report;
            if (report == null) return;
            try
            {
                Clipboard.SetText(report.ToText());
                Notify(NoticeKind.Success, "诊断报告已复制");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Notify(NoticeKind.Warning, "剪贴板被其他程序占用，请稍后再试");
            }
        }

        /// <summary>
        /// 导出为文本（附上网卡信息，便于发给他人排查）或 JSON
        /// </summary>
        private async Task ExportAsync()
        {
            DiagnosisReport report = view.Report;
            if (report == null) return;
            string fileName;
            int filterIndex;
            using (var dialog = new SaveFileDialog
            {
                Filter = "文本文件|*.txt|JSON 文件|*.json",
                FileName = $"网络诊断_{report.GeneratedAt:yyyyMMdd_HHmmss}"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                fileName = dialog.FileName;
                filterIndex = dialog.FilterIndex;
            }

            try
            {
                if (filterIndex == 2)
                {
                    File.WriteAllText(fileName, report.ToJson(), new UTF8Encoding(false));
                }
                else
                {
                    List<NetworkAdapter> adapters = await Task.Run(() => AdapterService.GetAdapters(true));
                    List<NetworkAdapter> connected = adapters.Where(a => a.Status == AdapterStatus.Connected).ToList();
                    string text = report.ToText() + Environment.NewLine + AdapterReport.Build(connected, DateTime.Now);
                    // 带 BOM 的 UTF-8，旧版记事本也能正确显示中文
                    File.WriteAllText(fileName, text, new UTF8Encoding(true));
                }
                if (!IsDisposed) Notify(NoticeKind.Success, "已导出到 " + Path.GetFileName(fileName));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                || ex is System.Management.ManagementException || ex is System.Net.NetworkInformation.NetworkInformationException)
            {
                if (!IsDisposed) Notify(NoticeKind.Error, "导出失败：" + ex.Message);
            }
        }
    }
}
