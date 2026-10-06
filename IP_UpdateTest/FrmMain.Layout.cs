using System.Drawing;
using System.Windows.Forms;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 主窗体的控件与布局：左侧网卡卡片，右侧 IP / DNS 配置卡片，底部操作栏和状态行
    /// </summary>
    public partial class FrmMain
    {
        private const float WindowWidth = 780;
        private const float CardHeight = 384;
        private const float LeftWidth = 300;
        private const float LabelColumn = 76;

        // 提权提示条
        private readonly Card pnlElevation = new Card { Radius = 12 };
        private readonly InkLabel lblElevation = new InkLabel { Style = TextStyle.Small, Glyph = Glyph.Shield, Text = "当前为普通权限：可以查看，修改配置需要管理员权限" };
        private readonly SpringButton btnRestartAsAdmin = new SpringButton { Kind = ButtonKind.Secondary, Text = "以管理员身份重启" };

        // 网卡卡片
        private readonly Card pnlAdapter = new Card();
        private readonly InkLabel lblAdapterCaption = new InkLabel { Style = TextStyle.Caption, TextColor = Palette.Ink3, Text = "网卡" };
        private readonly SelectBox cbxNetworkAdapter = new SelectBox { Placeholder = "没有找到网卡" };
        private readonly SpringButton btnRefresh = new SpringButton { Kind = ButtonKind.Ghost, Glyph = Glyph.Refresh };
        private readonly InkLabel lblAdapterDescription = new InkLabel { Style = TextStyle.Small, TextColor = Palette.Ink3 };
        private readonly InkLabel lblAdapterStatus = new InkLabel { Style = TextStyle.Small, TextColor = Palette.Ink2 };
        private readonly Toggle chkShowAll = new Toggle { Text = "显示全部" };
        private readonly Rule ruleAdapter = new Rule();
        private readonly InkLabel lblMacCaption = new InkLabel { Style = TextStyle.Caption, TextColor = Palette.Ink3, Text = "MAC 地址" };
        private readonly TextBox txtMac = InfoBox(false);
        private readonly InkLabel lblDhcpCaption = new InkLabel { Style = TextStyle.Caption, TextColor = Palette.Ink3, Text = "DHCP 服务器" };
        private readonly TextBox txtDhcpServer = InfoBox(false);
        private readonly InkLabel lblIPv6Caption = new InkLabel { Style = TextStyle.Caption, TextColor = Palette.Ink3, Text = "IPv6" };
        private readonly TextBox txtIPv6 = InfoBox(true);
        private readonly SpringButton btnToggleAdapter = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Power, Text = "禁用" };
        private readonly SpringButton btnMore = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.More, Text = "更多", ShowChevron = true };

        // 配置卡片
        private readonly Card pnlConfig = new Card();
        private readonly InkLabel lblIpMode = RowLabel("IP 获取");
        private readonly Segmented segIpMode = new Segmented();
        private readonly InkLabel lblIpAddress = RowLabel("IP 地址");
        private readonly FieldBox txtIpAddress = new FieldBox { Glyph = Glyph.Monitor, Mono = true };
        private readonly InkLabel lblMask = RowLabel("子网掩码");
        private readonly FieldBox txtMask = new FieldBox { Glyph = Glyph.Hash, Mono = true };
        private readonly InkLabel lblGateway = RowLabel("默认网关");
        private readonly FieldBox txtGateway = new FieldBox { Glyph = Glyph.Router, Mono = true };
        private readonly Rule ruleConfig = new Rule();
        private readonly InkLabel lblDnsMode = RowLabel("DNS 获取");
        private readonly Segmented segDnsMode = new Segmented();
        private readonly SpringButton btnDnsPreset = new SpringButton { Kind = ButtonKind.Secondary, Text = "预设", ShowChevron = true };
        private readonly SpringButton btnDnsBenchmark = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Gauge, Text = "测速" };
        private readonly InkLabel lblDnsMain = RowLabel("首选 DNS");
        private readonly FieldBox txtDnsMain = new FieldBox { Glyph = Glyph.Server, Mono = true };
        private readonly InkLabel lblDnsBackup = RowLabel("备用 DNS");
        private readonly FieldBox txtDnsBackup = new FieldBox { Glyph = Glyph.Server, Mono = true };

        // 操作栏
        private readonly SpringButton btnProfile = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Layers, Text = "配置方案", ShowChevron = true };
        private readonly SpringButton btnDiagnose = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.Activity, Text = "诊断" };
        private readonly SpringButton btnReport = new SpringButton { Kind = ButtonKind.Secondary, Glyph = Glyph.FileText, Text = "报表" };
        private readonly SpringButton btnRevert = new SpringButton { Kind = ButtonKind.Ghost, Glyph = Glyph.Undo, Text = "还原" };
        private readonly SpringButton btnApply = new SpringButton { Kind = ButtonKind.Primary, Text = "应用" };
        private readonly InkLabel lblStatus = new InkLabel { Style = TextStyle.Small, TextColor = Palette.Ink3 };

        private bool showElevation = true;

        private static InkLabel RowLabel(string text)
        {
            return new InkLabel { Style = TextStyle.Body, TextColor = Palette.Ink2, Text = text };
        }

        /// <summary>
        /// 只读信息（无边框，可选中复制）
        /// </summary>
        private static TextBox InfoBox(bool multiline)
        {
            return new TextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Palette.Surface,
                ForeColor = Palette.Ink,
                TabStop = false,
                Multiline = multiline,
                WordWrap = false
            };
        }

        private void BuildControls()
        {
            SuspendLayout();
            segIpMode.SetItems("自动获取 (DHCP)", "手动设置");
            segDnsMode.SetItems("自动获取", "手动设置");

            pnlElevation.Controls.AddRange(new Control[] { lblElevation, btnRestartAsAdmin });
            pnlAdapter.Controls.AddRange(new Control[]
            {
                lblAdapterCaption, cbxNetworkAdapter, btnRefresh, lblAdapterDescription, lblAdapterStatus, chkShowAll, ruleAdapter,
                lblMacCaption, txtMac, lblDhcpCaption, txtDhcpServer, lblIPv6Caption, txtIPv6, btnToggleAdapter, btnMore
            });
            pnlConfig.Controls.AddRange(new Control[]
            {
                lblIpMode, segIpMode, lblIpAddress, txtIpAddress, lblMask, txtMask, lblGateway, txtGateway, ruleConfig,
                lblDnsMode, segDnsMode, btnDnsPreset, btnDnsBenchmark, lblDnsMain, txtDnsMain, lblDnsBackup, txtDnsBackup
            });
            Controls.AddRange(new Control[] { pnlElevation, pnlAdapter, pnlConfig, btnProfile, btnDiagnose, btnReport, btnRevert, btnApply, lblStatus });

            // Tab 顺序：网卡 → 配置 → 操作
            int tab = 0;
            foreach (Control c in new Control[] { btnRestartAsAdmin, cbxNetworkAdapter, btnRefresh, chkShowAll, btnToggleAdapter, btnMore })
                c.TabIndex = tab++;
            foreach (Control c in new Control[] { segIpMode, txtIpAddress, txtMask, txtGateway, segDnsMode, btnDnsPreset, btnDnsBenchmark, txtDnsMain, txtDnsBackup })
                c.TabIndex = tab++;
            pnlElevation.TabIndex = 0;
            pnlAdapter.TabIndex = 1;
            pnlConfig.TabIndex = 2;
            btnProfile.TabIndex = 3;
            btnDiagnose.TabIndex = 4;
            btnReport.TabIndex = 5;
            btnRevert.TabIndex = 6;
            btnApply.TabIndex = 7;

            AcceptButton = btnApply;
            CancelButton = btnRevert;
            ApplyNativeFonts();
            ResumeLayout(false);
        }

        private float ContentTop
        {
            get { return Metrics.TitleBarHeight + 4 + (showElevation ? 44 + 12 : 0); }
        }

        private void UpdateWindowHeight()
        {
            SetLogicalSize(WindowWidth, ContentTop + CardHeight + 16 + 44 + 8 + 22 + 12);
        }

        private Rectangle R(float x, float y, float w, float h)
        {
            return new Rectangle(P(x), P(y), P(w), P(h));
        }

        protected override void LayoutContent(float s)
        {
            if (pnlAdapter == null) return;
            float gutter = Metrics.PageGutter;
            float width = WindowWidth - gutter * 2;

            pnlElevation.Visible = showElevation;
            pnlElevation.Bounds = R(gutter, Metrics.TitleBarHeight + 4, width, 44);
            int restartWidth = btnRestartAsAdmin.PreferredWidth;
            btnRestartAsAdmin.SetBounds(pnlElevation.Width - P(8) - restartWidth, P(6), restartWidth, P(32));
            lblElevation.SetBounds(P(16), 0, btnRestartAsAdmin.Left - P(24), pnlElevation.Height);

            float top = ContentTop;
            pnlAdapter.Bounds = R(gutter, top, LeftWidth, CardHeight);
            LayoutAdapterCard();

            float rightX = gutter + LeftWidth + 16;
            pnlConfig.Bounds = R(rightX, top, WindowWidth - gutter - rightX, CardHeight);
            LayoutConfigCard(WindowWidth - gutter - rightX);

            float actionsY = top + CardHeight + 16;
            float x = gutter;
            foreach (SpringButton b in new[] { btnProfile, btnDiagnose, btnReport })
            {
                int w = b.PreferredWidth;
                b.SetBounds(P(x), P(actionsY + 2), w, P(40));
                x += w / s + 8;
            }
            btnApply.Bounds = R(WindowWidth - gutter - 120, actionsY, 120, 44);
            int revertWidth = btnRevert.PreferredWidth;
            btnRevert.SetBounds(btnApply.Left - P(8) - revertWidth, P(actionsY + 2), revertWidth, P(40));

            lblStatus.Bounds = R(gutter + 2, actionsY + 44 + 8, width - 4, 22);
        }

        private void LayoutAdapterCard()
        {
            const float pad = 20, inner = LeftWidth - pad * 2;
            lblAdapterCaption.Bounds = R(pad, 16, inner, 18);
            cbxNetworkAdapter.Place(R(pad, 40, inner - 48, 40));
            btnRefresh.Bounds = R(pad + inner - 40, 40, 40, 40);
            lblAdapterDescription.Bounds = R(pad, 86, inner, 20);

            int toggleWidth = chkShowAll.PreferredWidth;
            chkShowAll.SetBounds(P(pad + inner) - toggleWidth, P(110), toggleWidth, P(26));
            lblAdapterStatus.SetBounds(P(pad), P(110), chkShowAll.Left - P(pad + 8), P(26));
            ruleAdapter.Bounds = R(pad, 148, inner, 1);

            float y = 160;
            foreach (var pair in new[] { new { Caption = lblMacCaption, Box = txtMac, Lines = 1 }, new { Caption = lblDhcpCaption, Box = txtDhcpServer, Lines = 1 }, new { Caption = lblIPv6Caption, Box = txtIPv6, Lines = 3 } })
            {
                pair.Caption.Bounds = R(pad, y, inner, 16);
                pair.Box.Bounds = R(pad, y + 18, inner, pair.Lines == 1 ? 20 : 54);
                y += 18 + (pair.Lines == 1 ? 20 : 54) + 8;
            }

            float bw = (inner - 8) / 2;
            btnToggleAdapter.Bounds = R(pad, CardHeight - pad - 36, bw, 36);
            btnMore.Bounds = R(pad + bw + 8, CardHeight - pad - 36, bw, 36);
        }

        private void LayoutConfigCard(float cardWidth)
        {
            const float pad = 20;
            float controlX = pad + LabelColumn;
            float controlWidth = cardWidth - pad - controlX;
            float y = pad;

            PlaceRow(lblIpMode, y);
            segIpMode.Bounds = R(controlX, y, controlWidth, 40);
            y += 48;
            foreach (var row in new[] { new { Label = lblIpAddress, Field = txtIpAddress }, new { Label = lblMask, Field = txtMask }, new { Label = lblGateway, Field = txtGateway } })
            {
                PlaceRow(row.Label, y);
                row.Field.Place(R(controlX, y, controlWidth, 40));
                y += 48;
            }

            ruleConfig.Bounds = R(pad, y + 2, cardWidth - pad * 2, 1);
            y += 12;

            PlaceRow(lblDnsMode, y);
            int benchmarkWidth = btnDnsBenchmark.PreferredWidth, presetWidth = btnDnsPreset.PreferredWidth;
            btnDnsBenchmark.SetBounds(P(controlX + controlWidth) - benchmarkWidth, P(y + 2), benchmarkWidth, P(36));
            btnDnsPreset.SetBounds(btnDnsBenchmark.Left - P(8) - presetWidth, P(y + 2), presetWidth, P(36));
            segDnsMode.SetBounds(P(controlX), P(y), btnDnsPreset.Left - P(8) - P(controlX), P(40));
            y += 48;
            foreach (var row in new[] { new { Label = lblDnsMain, Field = txtDnsMain }, new { Label = lblDnsBackup, Field = txtDnsBackup } })
            {
                PlaceRow(row.Label, y);
                row.Field.Place(R(controlX, y, controlWidth, 40));
                y += 48;
            }
        }

        private void PlaceRow(InkLabel label, float y)
        {
            label.Bounds = R(20, y, LabelColumn - 8, 40);
        }

        /// <summary>
        /// 原生只读框的字体（等宽）
        /// </summary>
        private void ApplyNativeFonts()
        {
            foreach (TextBox box in new[] { txtMac, txtDhcpServer, txtIPv6 })
                box.Font = Typo.NativeFont(TextStyle.Mono, S, true);
        }

        protected override void OnScaleChanged()
        {
            base.OnScaleChanged();
            ApplyNativeFonts();
        }
    }
}
