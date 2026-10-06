using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 参与测速的一组 DNS
    /// </summary>
    public sealed class DnsCandidate
    {
        public DnsCandidate(string name, string[] servers)
        {
            Name = name;
            Servers = servers.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }

        public string Name { get; private set; }

        public string[] Servers { get; private set; }
    }

    /// <summary>
    /// DNS 测速：直接向各 DNS 发送查询，按首选 DNS 的延迟排序，选择后填入主窗体
    /// </summary>
    public class FrmDnsBenchmark : ThemedForm
    {
        private const string Hint = "直接向各 DNS 查询 3 次取中位数，不经过系统缓存；延迟越低越好，“超时”表示当前网络用不了这个 DNS。";

        private readonly List<DnsCandidate> candidates;
        private readonly DataTable table = new DataTable
        {
            EmptyGlyph = Glyph.Server,
            EmptyTitle = "没有可测速的 DNS",
            EmptyText = "在主窗口的 DNS 预设里添加后再来测速"
        };

        private readonly SpringButton btnRetest = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Refresh, Text = "重新测速" };
        private readonly SpringButton btnClose = new SpringButton { Kind = ButtonKind.Secondary, Text = "关闭" };
        private readonly SpringButton btnUse = new SpringButton { Kind = ButtonKind.Primary, Text = "使用所选" };
        private bool running;

        /// <summary>
        /// 用户选择的 DNS
        /// </summary>
        public string[] SelectedServers { get; private set; }

        public FrmDnsBenchmark(IEnumerable<DnsCandidate> candidates) : this(candidates, true)
        {
        }

        /// <summary>
        /// autoRun 为 false 时打开后不自动测速（界面截图测试用）
        /// </summary>
        internal FrmDnsBenchmark(IEnumerable<DnsCandidate> candidates, bool autoRun)
        {
            this.candidates = candidates.Where(c => c.Servers.Length > 0).ToList();
            Text = "DNS 测速";
            TitleBar.ShowMinimize = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            SetLogicalSize(640, 500);

            table.Columns.Add(new TableColumn("名称", 130, CellKind.Strong));
            table.Columns.Add(new TableColumn("首选 DNS", 128, CellKind.Mono));
            table.Columns.Add(new TableColumn("备用 DNS", 128, CellKind.Mono));
            table.Columns.Add(new TableColumn("延迟（首选 / 备用）", 0, CellKind.Mono));
            table.RowActivated += (s, e) => UseSelected();
            table.SelectionChanged += (s, e) => UpdateButtons();

            btnRetest.Click += async (s, e) => await RunAsync();
            btnClose.Click += (s, e) => Close();
            btnUse.Click += (s, e) => UseSelected();

            Controls.AddRange(new Control[] { table, btnRetest, btnClose, btnUse });
            int tab = 0;
            foreach (Control c in new Control[] { table, btnRetest, btnClose, btnUse })
                c.TabIndex = tab++;
            CancelButton = btnClose;

            table.SetRows(this.candidates.Select(c => new TableRow(c, c.Name, c.Servers[0], c.Servers.Length > 1 ? c.Servers[1] : "-", "")), -1, true);
            UpdateButtons();
            Shown += async (s, e) =>
            {
                table.Focus();
                if (autoRun) await RunAsync();
            };
        }

        protected override void LayoutContent(float s)
        {
            int gutter = P(Metrics.PageGutter);
            int top = P(Metrics.TitleBarHeight + 4);
            int h = P(Metrics.ButtonHeight);
            int footerH = P(Metrics.PrimaryHeight);
            int footerY = ClientSize.Height - P(20) - footerH;
            int hintHeight = HintHeight();
            table.SetBounds(gutter, top, ClientSize.Width - gutter * 2, footerY - P(16) - hintHeight - P(10) - top);

            int useW = Math.Max(P(120), btnUse.PreferredWidth);
            btnUse.SetBounds(ClientSize.Width - gutter - useW, footerY, useW, footerH);
            int closeW = Math.Max(P(84), btnClose.PreferredWidth);
            btnClose.SetBounds(btnUse.Left - P(8) - closeW, footerY + (footerH - h) / 2, closeW, h);
            btnRetest.SetBounds(gutter, footerY + (footerH - h) / 2, Math.Max(P(100), btnRetest.PreferredWidth), h);
        }

        private int HintHeight()
        {
            float s = S;
            float width = ClientSize.Width - P(Metrics.PageGutter) * 2 - P(4);
            return (int)Math.Ceiling(Typo.Wrap(Hint, TextStyle.Small, width, s).Count * Typo.LineHeight(TextStyle.Small, s));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Shapes.Prepare(g);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float s = S;
            float x = P(Metrics.PageGutter) + P(2);
            float y = table.Bottom + P(10);
            float lineHeight = Typo.LineHeight(TextStyle.Small, s);
            foreach (string line in Typo.Wrap(Hint, TextStyle.Small, ClientSize.Width - x * 2, s))
            {
                Typo.DrawLine(g, line, TextStyle.Small, Palette.Ink3, x, Typo.CenterBaseline(new RectangleF(0, y, ClientSize.Width, lineHeight), TextStyle.Small, s), s);
                y += lineHeight;
            }
        }

        private void UpdateButtons()
        {
            btnUse.Enabled = !running && table.SelectedRow != null;
            btnRetest.Enabled = !running && table.Rows.Count > 0;
        }

        private async Task RunAsync()
        {
            if (running || table.Rows.Count == 0) return;
            running = true;
            UpdateButtons();
            btnRetest.BeginLoading();
            foreach (TableRow row in table.Rows)
            {
                row.Cells[3] = "";
                row.Badge = null;
                row.BusyColumn = 3;
            }
            table.RefreshRows();

            var results = new Dictionary<TableRow, long?>();
            await Task.WhenAll(table.Rows.ToList().Select(async row =>
            {
                var candidate = (DnsCandidate)row.Tag;
                var texts = new List<string>();
                long? primary = null;
                foreach (string server in candidate.Servers.Take(2))
                {
                    IPAddress address;
                    if (!IpValidator.TryParseIPv4(server, out address))
                    {
                        texts.Add("无效");
                        continue;
                    }
                    DnsBenchmarkResult result = await DnsProbe.BenchmarkAsync(address);
                    if (texts.Count == 0) primary = result.MedianMilliseconds;
                    texts.Add(Describe(result));
                }
                results[row] = primary;
                if (IsDisposed) return;
                row.Cells[3] = string.Join(" / ", texts);
                row.BusyColumn = -1;
                table.RefreshRows();
            }));
            if (IsDisposed) return;

            running = false;
            btnRetest.Reset();
            ShowResults(results);
        }

        /// <summary>
        /// 按首选 DNS 延迟排序，超时的排最后，并默认选中最快的
        /// </summary>
        internal void ShowResults(IDictionary<TableRow, long?> results)
        {
            List<TableRow> sorted = table.Rows.OrderBy(r => results[r] ?? long.MaxValue).ToList();
            bool anyOk = sorted.Count > 0 && results[sorted[0]].HasValue;
            foreach (TableRow row in sorted)
            {
                row.BusyColumn = -1;
                row.Badge = null;
            }
            if (anyOk) sorted[0].Badge = "最快";
            table.SetRows(sorted, anyOk ? 0 : -1, true);
            UpdateButtons();
            if (anyOk) Notify(NoticeKind.Success, $"测速完成，最快的是 {((DnsCandidate)sorted[0].Tag).Name}（{results[sorted[0]]} ms）");
            else Notify(NoticeKind.Warning, "所有 DNS 都超时了，请先检查网络连接");
        }

        internal IReadOnlyList<TableRow> Rows
        {
            get { return table.Rows; }
        }

        private static string Describe(DnsBenchmarkResult result)
        {
            if (!result.MedianMilliseconds.HasValue) return "超时";
            string text = result.MedianMilliseconds + " ms";
            return result.Succeeded < result.Attempts ? $"{text}（{result.Succeeded}/{result.Attempts}）" : text;
        }

        private void UseSelected()
        {
            if (running || table.SelectedRow == null) return;
            SelectedServers = ((DnsCandidate)table.SelectedRow.Tag).Servers;
            DialogResult = DialogResult.OK;
        }
    }
}
