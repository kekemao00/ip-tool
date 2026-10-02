using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Core.Diagnosis;

namespace IP_UpdateTest
{
    /// <summary>
    /// 分层网络诊断：从本机网卡到外网逐个环节检测，指出问题所在环节并给出建议，可复制或导出
    /// </summary>
    public class FrmDiagnosis : DialogBase
    {
        private static readonly Color WarningColor = Color.FromArgb(192, 86, 33);

        private readonly NetworkAdapter adapter;
        private readonly RichTextBox txtReport = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            WordWrap = true,
            DetectUrls = false
        };

        private readonly Button btnRerun;
        private readonly Button btnCopy;
        private readonly Button btnExport;
        private DiagnosisReport report;

        public FrmDiagnosis(NetworkAdapter adapter)
            : base(adapter == null ? "网络诊断" : "网络诊断 - " + adapter.Name, 760, 600)
        {
            this.adapter = adapter;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;

            // RichTextBox 不支持 Padding，放在带内边距的面板里
            var border = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 4, 8), BorderStyle = BorderStyle.FixedSingle };
            border.Controls.Add(txtReport);
            ContentPanel.Controls.Add(border);

            AddButton("关闭", false, (s, e) => Close());
            btnExport = AddButton("导出…", false, async (s, e) => await ExportAsync());
            btnCopy = AddButton("复制", false, (s, e) => CopyReport());
            btnRerun = AddButton("重新诊断", true, async (s, e) => await RunAsync());
            FinishLayout();

            border.BackColor = txtReport.BackColor = UITheme.CardBackground;
            txtReport.ForeColor = UITheme.TextPrimary;
            txtReport.Font = UITheme.InputFont;

            Load += async (s, e) => await RunAsync();
        }

        private async Task RunAsync()
        {
            btnRerun.Enabled = btnCopy.Enabled = btnExport.Enabled = false;
            txtReport.ForeColor = UITheme.TextSecondary;
            txtReport.Text = "正在诊断：本机网卡 → 路由器 → 外网 → DNS → 代理/VPN → UDP，约需 10 秒…";
            try
            {
                report = await NetworkDiagnosis.RunAsync(adapter);
                if (IsDisposed) return;
                ShowReport(report.ToText());
            }
            catch (Exception ex) when (ex is System.Net.NetworkInformation.NetworkInformationException
                || ex is System.Net.Sockets.SocketException || ex is InvalidOperationException)
            {
                if (IsDisposed) return;
                report = null;
                txtReport.Text = "诊断失败：" + ex.Message;
            }
            finally
            {
                if (!IsDisposed)
                {
                    btnRerun.Enabled = true;
                    btnCopy.Enabled = btnExport.Enabled = report != null;
                }
            }
        }

        /// <summary>
        /// 显示报告，按状态给各行着色
        /// </summary>
        private void ShowReport(string text)
        {
            txtReport.ForeColor = UITheme.TextPrimary;
            txtReport.Text = text;
            // 自动换行时 GetFirstCharIndexFromLine 按显示行计算，这里自己累计逻辑行的起点（RichTextBox 内部以 \n 换行）
            int start = 0;
            foreach (string line in txtReport.Lines)
            {
                int lineStart = start;
                start += line.Length + 1;
                string trimmed = line.TrimStart();
                Color? color = null;
                bool bold = false;
                if (line.StartsWith("结论", StringComparison.Ordinal))
                {
                    color = report.FaultLayer.HasValue ? UITheme.DangerText : UITheme.SuccessText;
                    bold = true;
                }
                else if (line.StartsWith("[", StringComparison.Ordinal) || line.StartsWith("建议", StringComparison.Ordinal)) bold = true;
                else if (trimmed.StartsWith("×", StringComparison.Ordinal)) color = UITheme.DangerText;
                else if (trimmed.StartsWith("! ", StringComparison.Ordinal)) color = WarningColor;
                else if (trimmed.StartsWith("√", StringComparison.Ordinal)) color = UITheme.SuccessText;
                else if (trimmed.StartsWith("→", StringComparison.Ordinal) || trimmed.StartsWith("·", StringComparison.Ordinal)
                    || trimmed.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("说明", StringComparison.Ordinal))
                    color = UITheme.TextSecondary;

                if (color == null && !bold) continue;
                txtReport.Select(lineStart, line.Length);
                if (color != null) txtReport.SelectionColor = color.Value;
                if (bold) txtReport.SelectionFont = new Font(txtReport.Font, FontStyle.Bold);
            }
            txtReport.Select(0, 0);
        }

        private void CopyReport()
        {
            try
            {
                Clipboard.SetText(report.ToText());
                btnCopy.Text = "已复制";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                UITheme.ShowMessage("剪贴板被其他程序占用，请稍后再试", "复制", MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 导出为文本（附上网卡信息，便于发给他人排查）或 JSON
        /// </summary>
        private async Task ExportAsync()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "文本文件|*.txt|JSON 文件|*.json",
                FileName = $"网络诊断_{report.GeneratedAt:yyyyMMdd_HHmmss}"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (dialog.FilterIndex == 2)
                    {
                        File.WriteAllText(dialog.FileName, report.ToJson(), new UTF8Encoding(false));
                    }
                    else
                    {
                        List<NetworkAdapter> adapters = await Task.Run(() => AdapterService.GetAdapters(true));
                        List<NetworkAdapter> connected = adapters.Where(a => a.Status == AdapterStatus.Connected).ToList();
                        string text = report.ToText() + Environment.NewLine + AdapterReport.Build(connected, DateTime.Now);
                        // 带 BOM 的 UTF-8，旧版记事本也能正确显示中文
                        File.WriteAllText(dialog.FileName, text, new UTF8Encoding(true));
                    }
                    UITheme.ShowMessage("已导出到 " + dialog.FileName, "导出诊断报告");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                    || ex is System.Management.ManagementException || ex is System.Net.NetworkInformation.NetworkInformationException)
                {
                    UITheme.ShowMessage("导出失败：" + ex.Message, "导出诊断报告", MessageBoxIcon.Error);
                }
            }
        }
    }
}
