using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;

namespace IP_UpdateTest
{
    /// <summary>
    /// 网卡信息报表：按范围筛选，可复制或导出为文本、JSON
    /// </summary>
    public class FrmInfo : DialogBase
    {
        private readonly ComboBox cbxScope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly TextBox txtReport = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };

        private readonly Button btnRefresh;
        private readonly Button btnCopy;
        private readonly Button btnExport;
        private List<NetworkAdapter> adapters = new List<NetworkAdapter>();

        public FrmInfo() : base("网卡信息报表", 700, 540)
        {
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;

            cbxScope.Items.AddRange(new object[] { "已连接的网卡", "物理网卡", "全部网卡" });
            cbxScope.SelectedIndexChanged += async (s, e) => await RefreshAsync();

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false };
            var label = new Label { Text = "范围", AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
            UITheme.StyleLabel(label);
            btnRefresh = new Button { Text = "刷新", Size = new Size(64, 28), Margin = new Padding(8, 2, 0, 0) };
            UITheme.StyleSecondaryButton(btnRefresh);
            btnRefresh.Click += async (s, e) => await RefreshAsync();
            cbxScope.Margin = new Padding(0, 4, 0, 0);
            toolbar.Controls.AddRange(new Control[] { label, cbxScope, btnRefresh });

            ContentPanel.Controls.Add(txtReport);
            ContentPanel.Controls.Add(toolbar);

            CancelButton = AddButton("关闭", false, (s, e) => Close());
            btnExport = AddButton("导出…", false, (s, e) => Export());
            btnCopy = AddButton("复制", true, (s, e) => CopyReport());

            var link = new LinkLabel { Text = "github.com/kekemao00/ip-tool", AutoSize = true, Margin = new Padding(0, 8, 160, 0) };
            link.LinkClicked += (s, e) => OpenHomePage();
            ButtonPanel.Controls.Add(link);
            FinishLayout();

            UITheme.StyleComboBox(cbxScope);
            txtReport.Font = UITheme.MonoFont;
            txtReport.BackColor = UITheme.CardBackground;
            txtReport.ForeColor = UITheme.TextPrimary;
            txtReport.BorderStyle = BorderStyle.FixedSingle;

            Load += (s, e) => cbxScope.SelectedIndex = 0;
        }

        private async Task RefreshAsync()
        {
            int scope = cbxScope.SelectedIndex;
            btnRefresh.Enabled = btnCopy.Enabled = btnExport.Enabled = false;
            txtReport.Text = "正在读取网卡信息…";
            try
            {
                // 物理网卡只读物理网卡，其余两种读全部再筛选
                List<NetworkAdapter> list = await Task.Run(() => AdapterService.GetAdapters(scope != 1));
                if (IsDisposed) return;
                adapters = scope == 0 ? list.Where(a => a.Status == AdapterStatus.Connected).ToList() : list;
                txtReport.Text = AdapterReport.Build(adapters, DateTime.Now);
            }
            catch (Exception ex) when (ex is System.Net.NetworkInformation.NetworkInformationException || ex is System.Management.ManagementException)
            {
                txtReport.Text = "读取网卡信息失败：" + ex.Message;
            }
            finally
            {
                if (!IsDisposed) btnRefresh.Enabled = btnCopy.Enabled = btnExport.Enabled = true;
            }
            txtReport.SelectionStart = 0;
        }

        private void CopyReport()
        {
            try
            {
                Clipboard.SetText(txtReport.Text);
                btnCopy.Text = "已复制";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                UITheme.ShowMessage("剪贴板被其他程序占用，请稍后再试", "复制", MessageBoxIcon.Warning);
            }
        }

        private void Export()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "文本文件|*.txt|JSON 文件|*.json",
                FileName = $"网卡信息_{DateTime.Now:yyyyMMdd_HHmmss}"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (dialog.FilterIndex == 2)
                    {
                        var serializer = new DataContractJsonSerializer(typeof(List<AdapterInfo>));
                        using (var stream = File.Create(dialog.FileName))
                        {
                            serializer.WriteObject(stream, adapters.Select(AdapterInfo.From).ToList());
                        }
                    }
                    else
                    {
                        // 带 BOM 的 UTF-8，旧版记事本也能正确显示中文
                        File.WriteAllText(dialog.FileName, txtReport.Text, new UTF8Encoding(true));
                    }
                    UITheme.ShowMessage("已导出到 " + dialog.FileName, "导出报表");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    UITheme.ShowMessage("导出失败：" + ex.Message, "导出报表", MessageBoxIcon.Error);
                }
            }
        }

        private static void OpenHomePage()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "https://github.com/kekemao00/ip-tool", UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }
    }
}
