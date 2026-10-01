using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core;

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
    public class FrmDnsBenchmark : DialogBase
    {
        private readonly ListView list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false
        };

        private readonly List<DnsCandidate> candidates;
        private readonly Button btnUse;
        private readonly Button btnRetest;
        private bool running;

        /// <summary>
        /// 用户选择的 DNS
        /// </summary>
        public string[] SelectedServers { get; private set; }

        public FrmDnsBenchmark(IEnumerable<DnsCandidate> candidates) : base("DNS 测速", 560, 400)
        {
            this.candidates = candidates.Where(c => c.Servers.Length > 0).ToList();

            list.Columns.Add("名称", 120);
            list.Columns.Add("首选 DNS", 120);
            list.Columns.Add("备用 DNS", 120);
            list.Columns.Add("延迟（首选 / 备用）", 150);
            list.DoubleClick += (s, e) => UseSelected();
            list.SelectedIndexChanged += (s, e) => btnUse.Enabled = !running && list.SelectedItems.Count > 0;

            var hint = new Label
            {
                Text = "直接向各 DNS 查询 3 次取中位数，不经过系统缓存；延迟越低越好，“超时”表示当前网络无法使用该 DNS。",
                Dock = DockStyle.Bottom,
                Height = 40
            };
            UITheme.StyleLabel(hint);
            ContentPanel.Controls.Add(list);
            ContentPanel.Controls.Add(hint);

            CancelButton = AddButton("关闭", false, (s, e) => DialogResult = DialogResult.Cancel);
            btnUse = AddButton("使用所选", true, (s, e) => UseSelected());
            btnRetest = AddButton("重新测速", false, async (s, e) => await RunAsync());
            FinishLayout();

            list.Font = UITheme.InputFont;
            list.ForeColor = UITheme.TextPrimary;
            foreach (DnsCandidate candidate in this.candidates)
            {
                list.Items.Add(new ListViewItem(new[]
                {
                    candidate.Name,
                    candidate.Servers[0],
                    candidate.Servers.Length > 1 ? candidate.Servers[1] : "-",
                    ""
                }) { Tag = candidate });
            }
            Shown += async (s, e) => await RunAsync();
        }

        private async Task RunAsync()
        {
            if (running) return;
            running = true;
            btnUse.Enabled = btnRetest.Enabled = false;
            foreach (ListViewItem item in list.Items) item.SubItems[3].Text = "测速中…";

            var results = new Dictionary<ListViewItem, long?>();
            await Task.WhenAll(list.Items.Cast<ListViewItem>().Select(async item =>
            {
                var candidate = (DnsCandidate)item.Tag;
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
                results[item] = primary;
                if (!IsDisposed) item.SubItems[3].Text = string.Join(" / ", texts);
            }));
            if (IsDisposed) return;

            // 按首选 DNS 延迟排序，超时的排最后，并默认选中最快的
            List<ListViewItem> sorted = list.Items.Cast<ListViewItem>()
                .OrderBy(i => results[i] ?? long.MaxValue)
                .ToList();
            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(sorted.ToArray());
            list.EndUpdate();
            if (list.Items.Count > 0 && results[sorted[0]].HasValue) list.Items[0].Selected = true;

            running = false;
            btnRetest.Enabled = true;
            btnUse.Enabled = list.SelectedItems.Count > 0;
        }

        private static string Describe(DnsBenchmarkResult result)
        {
            if (!result.MedianMilliseconds.HasValue) return "超时";
            string text = result.MedianMilliseconds + " ms";
            return result.Succeeded < result.Attempts ? $"{text}（{result.Succeeded}/{result.Attempts}）" : text;
        }

        private void UseSelected()
        {
            if (running || list.SelectedItems.Count == 0) return;
            SelectedServers = ((DnsCandidate)list.SelectedItems[0].Tag).Servers;
            DialogResult = DialogResult.OK;
        }
    }
}
