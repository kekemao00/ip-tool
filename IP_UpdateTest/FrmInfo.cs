using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 网卡信息报表：按范围筛选，可复制或导出为文本、JSON
    /// </summary>
    public class FrmInfo : ThemedForm
    {
        private const string HomePage = "https://github.com/kekemao00/ip-tool";

        private readonly Segmented segScope = new Segmented { Fill = false };
        private readonly SpringButton btnRefresh = new SpringButton { Kind = ButtonKind.Ghost, Glyph = Glyph.Refresh };
        private readonly Card card = new Card();
        private readonly TextBox txtReport = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BorderStyle = BorderStyle.None,
            BackColor = Palette.Surface,
            ForeColor = Palette.Ink
        };

        private readonly SpringButton btnLink = new SpringButton { Kind = ButtonKind.Ghost, Glyph = Glyph.ExternalLink, Text = "github.com/kekemao00/ip-tool" };
        private readonly SpringButton btnExport = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Download, Text = "导出" };
        private readonly SpringButton btnClose = new SpringButton { Kind = ButtonKind.Secondary, Text = "关闭" };
        private readonly SpringButton btnCopy = new SpringButton { Kind = ButtonKind.Primary, Glyph = Glyph.Copy, Text = "复制" };
        private List<NetworkAdapter> adapters = new List<NetworkAdapter>();
        private int loadVersion;

        public FrmInfo()
        {
            Text = "网卡信息报表";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            SetLogicalSize(720, 580);

            segScope.SetItems("已连接的网卡", "物理网卡", "全部网卡");
            segScope.SelectedIndexChanged += async (s, e) => await RefreshAsync();
            InkToolTip.Shared.SetToolTip(btnRefresh, "重新读取");
            InkToolTip.Shared.SetToolTip(btnLink, "在浏览器中打开项目主页");
            btnRefresh.Click += async (s, e) => await RefreshAsync();
            btnLink.Click += (s, e) => OpenHomePage();
            btnExport.Click += (s, e) => Export();
            btnClose.Click += (s, e) => Close();
            btnCopy.Click += (s, e) => CopyReport();

            card.Controls.Add(txtReport);
            Controls.AddRange(new Control[] { segScope, btnRefresh, card, btnLink, btnExport, btnClose, btnCopy });
            int tab = 0;
            foreach (Control c in new Control[] { segScope, btnRefresh, card, btnLink, btnExport, btnClose, btnCopy })
                c.TabIndex = tab++;
            CancelButton = btnClose;
            ApplyNativeFonts();

            Load += async (s, e) =>
            {
                segScope.SelectedIndex = 0;
                await RefreshAsync();
            };
        }

        private void ApplyNativeFonts()
        {
            txtReport.Font = Typo.NativeFont(TextStyle.Mono, S, true);
        }

        protected override void OnScaleChanged()
        {
            base.OnScaleChanged();
            ApplyNativeFonts();
        }

        protected override void LayoutContent(float s)
        {
            int gutter = P(Metrics.PageGutter);
            int top = P(Metrics.TitleBarHeight + 4);
            int h = P(Metrics.ButtonHeight);
            segScope.SetBounds(gutter, top, segScope.PreferredWidth, h);
            btnRefresh.SetBounds(segScope.Right + P(6), top, h, h);

            int footerH = P(Metrics.PrimaryHeight);
            int footerY = ClientSize.Height - P(20) - footerH;
            int cardTop = top + h + P(12);
            card.SetBounds(gutter, cardTop, ClientSize.Width - gutter * 2, footerY - P(16) - cardTop);
            // 文字和卡片边缘留出 16 的内边距；圆角处不放内容
            txtReport.SetBounds(P(16), P(14), card.Width - P(16) - P(6), card.Height - P(14) - P(6));

            int copyW = Math.Max(P(108), btnCopy.PreferredWidth);
            btnCopy.SetBounds(ClientSize.Width - gutter - copyW, footerY, copyW, footerH);
            int y = footerY + (footerH - h) / 2;
            int closeW = Math.Max(P(84), btnClose.PreferredWidth);
            btnClose.SetBounds(btnCopy.Left - P(8) - closeW, y, closeW, h);
            int exportW = Math.Max(P(84), btnExport.PreferredWidth);
            btnExport.SetBounds(btnClose.Left - P(8) - exportW, y, exportW, h);
            btnLink.SetBounds(gutter - P(10), y, Math.Min(btnLink.PreferredWidth, btnExport.Left - gutter), h);
        }

        private async Task RefreshAsync()
        {
            int scope = segScope.SelectedIndex;
            if (scope < 0) return;
            int version = ++loadVersion;
            btnCopy.Enabled = btnExport.Enabled = false;
            btnRefresh.BeginLoading();
            txtReport.Text = "正在读取网卡信息…";
            try
            {
                // 物理网卡只读物理网卡，其余两种读全部再筛选
                List<NetworkAdapter> list = await Task.Run(() => AdapterService.GetAdapters(scope != 1));
                if (IsDisposed || version != loadVersion) return;
                adapters = scope == 0 ? list.Where(a => a.Status == AdapterStatus.Connected).ToList() : list;
                txtReport.Text = AdapterReport.Build(adapters, DateTime.Now);
                btnCopy.Enabled = btnExport.Enabled = true;
            }
            catch (Exception ex) when (ex is System.Net.NetworkInformation.NetworkInformationException || ex is System.Management.ManagementException)
            {
                if (IsDisposed || version != loadVersion) return;
                txtReport.Text = "读取网卡信息失败：" + ex.Message;
                Notify(NoticeKind.Error, "读取网卡信息失败");
            }
            finally
            {
                if (!IsDisposed && version == loadVersion) btnRefresh.Reset();
            }
            txtReport.SelectionStart = 0;
        }

        private void CopyReport()
        {
            try
            {
                Clipboard.SetText(txtReport.Text);
                Notify(NoticeKind.Success, "报表已复制");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Notify(NoticeKind.Warning, "剪贴板被其他程序占用，请稍后再试");
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
                    Notify(NoticeKind.Success, "已导出到 " + Path.GetFileName(dialog.FileName));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Notify(NoticeKind.Error, "导出失败：" + ex.Message);
                }
            }
        }

        private void OpenHomePage()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = HomePage, UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Notify(NoticeKind.Warning, "无法打开浏览器");
            }
        }
    }
}
