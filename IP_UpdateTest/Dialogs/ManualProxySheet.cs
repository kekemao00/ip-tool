using System.Drawing;
using System.Windows.Forms;
using IP_UpdateTest.Core.TerminalProxy;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 手动添加自动检测不到的代理（如 SSH 隧道、局域网里的代理服务器）：填地址、端口和类型。
    /// 保存时返回 ProxyCandidate，删除时返回 Deleted，取消时返回 null
    /// </summary>
    public sealed class ManualProxySheet : SheetContent
    {
        public static readonly object Deleted = new object();

        private const float PanelWidth = 460;
        private const float LabelColumn = 64;
        private const float Step = 44;
        private const float FirstRow = 60;
        private const string Hint = "ssh -D 建立的是 SOCKS5 代理；npm、pip 不支持 SOCKS5，能选 HTTP 时选 HTTP。";

        private readonly FieldBox txtHost = new FieldBox { Glyph = Glyph.Server, Mono = true, Placeholder = "如 127.0.0.1 或 proxy.corp.com" };
        private readonly FieldBox txtPort = new FieldBox { Glyph = Glyph.Hash, Mono = true, Placeholder = "如 1080" };
        private readonly Segmented segType = new Segmented();
        private readonly SpringButton btnSave = FooterButton("保存并选用", ButtonKind.Primary);
        private readonly SpringButton btnDelete = FooterButton("删除", ButtonKind.Secondary);
        private readonly SpringButton btnCancel = FooterButton("取消", ButtonKind.Secondary);
        private readonly string[] captions = { "地址", "端口", "类型" };

        /// <param name="current">已添加的代理，没有时为 null</param>
        public ManualProxySheet(ProxyCandidate current)
        {
            Title = current == null ? "手动添加代理" : "修改手动添加的代理";
            segType.SetItems("HTTP", "SOCKS5");
            if (current != null)
            {
                txtHost.Text = current.Host;
                txtPort.Text = current.Port.ToString();
                segType.SelectedIndex = current.Http ? 0 : 1;
            }
            btnDelete.Visible = current != null;

            Controls.AddRange(new Control[] { txtHost, txtPort, segType, btnSave, btnDelete, btnCancel });
            int tab = 0;
            foreach (Control c in new Control[] { txtHost, txtPort, segType, btnSave, btnDelete, btnCancel })
                c.TabIndex = tab++;
            foreach (FieldBox field in new[] { txtHost, txtPort })
            {
                RegisterNative(field.Box);
                FieldBox f = field;
                f.TextChanged += (s, e) => f.SetError(null);
            }

            btnSave.Click += (s, e) => Save();
            btnDelete.Click += (s, e) => Close(Deleted);
            btnCancel.Click += (s, e) => Cancel();
            Accept = btnSave;
        }

        public override Control InitialFocus
        {
            get { return txtHost; }
        }

        public override Size LogicalSize
        {
            get { return new Size((int)PanelWidth, (int)(FirstRow + captions.Length * Step + 40 + 16 + Metrics.PanelButtonHeight + 20)); }
        }

        private void Save()
        {
            txtHost.SetError(null, false);
            txtPort.SetError(null, false);
            string host = txtHost.Text.Trim();
            ProxyCandidate pasted;
            // 粘贴了完整地址（如 socks5://127.0.0.1:1080）时直接拆开
            if (host.Contains("://") && ProxyCandidate.TryParseManual(host, out pasted))
            {
                Close(pasted);
                return;
            }

            host = host.Trim('[', ']');
            int port;
            if (!ProxyCandidate.IsValidHost(host))
            {
                txtHost.SetError(host.Length == 0 ? "请填写代理地址" : "地址格式不正确，应为 IP 地址或域名");
                txtHost.FocusInput();
                return;
            }
            if (!int.TryParse(txtPort.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                txtPort.SetError("端口应为 1–65535 的整数");
                txtPort.FocusInput();
                return;
            }
            bool socks = segType.SelectedIndex == 1;
            Close(new ProxyCandidate { Host = host.ToLowerInvariant(), Port = port, Http = !socks, Socks5 = socks, IsManual = true });
        }

        protected override void LayoutContent(float s)
        {
            int x = P(24 + LabelColumn);
            int width = Width - x - P(24);
            int h = P(Metrics.InputHeight);
            txtHost.Place(new Rectangle(x, P(FirstRow), width, h));
            txtPort.Place(new Rectangle(x, P(FirstRow + Step), P(140), h));
            int sh = P(Metrics.ButtonHeight);
            segType.SetBounds(x, P(FirstRow + Step * 2) + (h - sh) / 2, P(200), sh);
            LayoutFooter(s, btnSave, btnCancel, btnDelete);
        }

        protected override void PaintContent(Graphics g, double opacity, double blur)
        {
            base.PaintContent(g, opacity, blur);
            float s = S;
            for (int i = 0; i < captions.Length; i++)
            {
                var row = new RectangleF(P(24), P(FirstRow + i * Step), P(LabelColumn), P(Metrics.InputHeight));
                Typo.DrawLine(g, captions[i], TextStyle.Body, Palette.Ink2, row.X, Typo.CenterBaseline(row, TextStyle.Body, s), s, opacity, blur);
            }
            float x = P(24 + LabelColumn + 2), y = P(FirstRow + captions.Length * Step);
            float lineHeight = Typo.LineHeight(TextStyle.Small, s);
            foreach (string line in Typo.Wrap(Hint, TextStyle.Small, Width - x - P(24), s))
            {
                Typo.DrawLine(g, line, TextStyle.Small, Palette.Ink3, x, Typo.CenterBaseline(new RectangleF(x, y, Width, lineHeight), TextStyle.Small, s), s, opacity, blur);
                y += lineHeight;
            }
        }
    }
}
