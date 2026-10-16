using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using IP_UpdateTest.Core.Diagnosis;
using IP_UpdateTest.Core.TerminalProxy;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 终端代理检测结果：顶部结论与链路（代理软件 → 端口 → 系统代理 → 命令行），下面逐项列出各处的代理设置。
    /// 颜色沿用诊断报告：正常 accent、失效 danger、需要处理 ink、其余灰
    /// </summary>
    public class TerminalProxyView : ScrollSurface
    {
        private const float Pad = 24;
        private const float Tile = 34;

        private abstract class Op
        {
            public int Block;
            public float Top;
            public abstract void Paint(Graphics g, TerminalProxyView view, float dy, double opacity, float s);
        }

        private sealed class TextOp : Op
        {
            public float X, LineHeight;
            public string Text;
            public TextStyle Style;
            public Color Color;

            public override void Paint(Graphics g, TerminalProxyView view, float dy, double opacity, float s)
            {
                float baseline = Typo.CenterBaseline(new RectangleF(0, Top * s + dy, view.Width, LineHeight * s), Style, s);
                Typo.DrawLine(g, Text, Style, Palette.Fade(Color, opacity), X * s, baseline, s);
            }
        }

        /// <summary>
        /// 实心圆底的状态图标，或圆角方块底的工具图标
        /// </summary>
        private sealed class GlyphOp : Op
        {
            public float X, Size, Inset, Radius = -1;
            public Glyph Glyph;
            public Color Color;
            public Color Fill;
            public float Stroke = 1.5f;

            public override void Paint(Graphics g, TerminalProxyView view, float dy, double opacity, float s)
            {
                var box = new RectangleF(X * s, Top * s + dy, Size * s, Size * s);
                if (Fill.A > 0)
                {
                    if (Radius < 0)
                    {
                        using (var brush = new SolidBrush(Palette.Fade(Fill, opacity)))
                            g.FillEllipse(brush, box);
                    }
                    else
                    {
                        Shapes.Fill(g, box, Radius * s, Palette.Fade(Fill, opacity));
                    }
                }
                box.Inflate(-Inset * s, -Inset * s);
                Icons.Draw(g, Glyph, box, Palette.Fade(Color, opacity), s, Stroke);
            }
        }

        private sealed class PillOp : Op
        {
            public float X, Width;
            public string Text;
            public Glyph Glyph;
            public Color GlyphColor;

            public override void Paint(Graphics g, TerminalProxyView view, float dy, double opacity, float s)
            {
                var r = new RectangleF(X * s, Top * s + dy, Width * s, 26 * s);
                Shapes.Fill(g, r, r.Height / 2, Palette.Fade(Palette.Surface, opacity));
                Shapes.Stroke(g, r, r.Height / 2, Palette.Fade(Palette.Line, opacity), s);
                float icon = 14 * s;
                Icons.Draw(g, Glyph, new RectangleF(r.X + 8 * s, r.Y + (r.Height - icon) / 2, icon, icon), Palette.Fade(GlyphColor, opacity), s, 2f);
                Typo.Draw(g, Text, TextStyle.Small, Palette.Fade(Palette.Ink, opacity), new RectangleF(r.X + 28 * s, r.Y, r.Width - 30 * s, r.Height), s);
            }
        }

        /// <summary>
        /// 右侧状态标签
        /// </summary>
        private sealed class BadgeOp : Op
        {
            public float Right, Width;
            public string Text;
            public Color Fill, TextColor;

            public override void Paint(Graphics g, TerminalProxyView view, float dy, double opacity, float s)
            {
                var r = new RectangleF(view.Width - (Right + Width) * s, Top * s + dy, Width * s, 22 * s);
                Shapes.Fill(g, r, r.Height / 2, Palette.Fade(Fill, opacity));
                Typo.Draw(g, Text, TextStyle.Caption, Palette.Fade(TextColor, opacity), r, s, StringAlignment.Center);
            }
        }

        private sealed class RuleOp : Op
        {
            public float X;

            public override void Paint(Graphics g, TerminalProxyView view, float dy, double opacity, float s)
            {
                using (var pen = new Pen(Palette.Fade(Palette.Line, opacity), s))
                    g.DrawLine(pen, X * s, Top * s + dy, view.Width - Pad * s, Top * s + dy);
            }
        }

        private readonly List<Op> ops = new List<Op>();
        private TerminalProxyReport report;
        private string message;
        private bool loading;
        private float contentHeight;
        private int layoutWidth = -1;
        private float layoutScale;
        private double enteredAt = double.NegativeInfinity;

        public TerminalProxyReport Report
        {
            get { return report; }
        }

        public void ShowMessage(string text, bool isLoading)
        {
            report = null;
            message = text;
            loading = isLoading;
            enteredAt = Motion.Now;
            ops.Clear();
            contentHeight = 0;
            ScrollTo(0, false);
            Invalidate();
        }

        /// <summary>
        /// animate 为 false 时原地刷新（设置后重新检测），不重播淡入
        /// </summary>
        public void ShowReport(TerminalProxyReport value, bool animate = true)
        {
            bool replay = animate || report == null;
            report = value;
            message = null;
            loading = false;
            layoutWidth = -1;
            if (replay)
            {
                enteredAt = IsHandleCreated ? Motion.Now : double.NegativeInfinity;
                ScrollTo(0, false);
            }
            else if (ScrollTarget > MaxScroll)
            {
                // 刷新后内容可能变短，不能停在内容末尾之后
                ScrollTo(MaxScroll, false);
            }
            Invalidate();
        }

        protected override float ContentHeight
        {
            get
            {
                EnsureLayout();
                return contentHeight;
            }
        }

        private static Glyph RowGlyph(string key)
        {
            switch (key)
            {
                case "proxy": return Glyph.Zap;
                case "system": return Glyph.Globe;
                case "env": return Glyph.Terminal;
                case "git": return Glyph.GitBranch;
                case "npm":
                case "pip": return Glyph.Package;
                default: return Glyph.Server;
            }
        }

        private static void BadgeColors(CheckStatus status, out Color fill, out Color text)
        {
            switch (status)
            {
                case CheckStatus.Ok:
                    fill = Palette.AccentSoft;
                    text = Palette.Accent;
                    break;
                case CheckStatus.Fail:
                    fill = Palette.DangerSoft;
                    text = Palette.Danger;
                    break;
                case CheckStatus.Warning:
                    fill = Palette.Ink;
                    text = Color.White;
                    break;
                default:
                    fill = Palette.Surface3;
                    text = Palette.Ink2;
                    break;
            }
        }

        private void EnsureLayout()
        {
            float s = S;
            if (report == null || layoutWidth == Width && layoutScale == s) return;
            layoutWidth = Width;
            layoutScale = s;
            ops.Clear();

            float width = Width / s;
            float y = Pad;
            int block = 0;
            float small = Typo.LineHeight(TextStyle.Small, s) / s;
            float body = Typo.LineHeight(TextStyle.Body, s) / s;
            float caption = Typo.LineHeight(TextStyle.Caption, s) / s;
            float titleLine = Typo.LineHeight(TextStyle.Title, s) / s;

            // 结论
            CheckStatus overall = report.Overall;
            Color heroFill = overall == CheckStatus.Fail ? Palette.Danger : overall == CheckStatus.Ok ? Palette.Accent : overall == CheckStatus.Warning ? Palette.Ink : Palette.Surface3;
            ops.Add(new GlyphOp
            {
                Block = block, Top = y, X = Pad, Size = 40, Inset = 11, Glyph = ReportView.StatusGlyph(overall == CheckStatus.Info ? CheckStatus.Info : overall),
                Color = overall == CheckStatus.Info ? Palette.Ink2 : Color.White, Fill = heroFill, Stroke = 2f
            });
            float x = Pad + 40 + 16;
            float textWidth = width - x - Pad;
            float ty = y + 1;
            foreach (string line in Typo.Wrap(report.Title, TextStyle.Title, textWidth * s, s))
            {
                ops.Add(new TextOp { Block = block, Top = ty, X = x, LineHeight = titleLine, Text = line, Style = TextStyle.Title, Color = Palette.Ink });
                ty += titleLine;
            }
            ty += 2;
            foreach (string line in Typo.Wrap(report.Detail, TextStyle.Body, textWidth * s, s))
            {
                ops.Add(new TextOp { Block = block, Top = ty, X = x, LineHeight = body, Text = line, Style = TextStyle.Body, Color = Palette.Ink2 });
                ty += body;
            }
            y = Math.Max(y + 40, ty) + 18;

            // 链路
            block++;
            float px = x;
            for (int i = 0; i < report.Steps.Count; i++)
            {
                TerminalProxyStep step = report.Steps[i];
                float pw = 38 + (float)Math.Ceiling(Typo.Measure(step.Name, TextStyle.Small, s) / s);
                if (px + pw > width - Pad && px > x)
                {
                    px = x;
                    y += 34;
                }
                ops.Add(new PillOp { Block = block, Top = y, X = px, Width = pw, Text = step.Name, Glyph = ReportView.StatusGlyph(step.Status), GlyphColor = ReportView.StatusColor(step.Status) });
                px += pw;
                if (i < report.Steps.Count - 1)
                {
                    ops.Add(new GlyphOp { Block = block, Top = y + 6, X = px + 3, Size = 14, Glyph = Glyph.ChevronRight, Color = Palette.Ink3 });
                    px += 20;
                }
            }
            y += 26 + 22;

            // 各项设置，分“代理”和“命令行与工具”两组
            var groups = new[]
            {
                new { Caption = "代理", Rows = report.Rows.Where(r => r.Key == "proxy" || r.Key == "system").ToList() },
                new { Caption = "命令行与工具", Rows = report.Rows.Where(r => r.Key != "proxy" && r.Key != "system").ToList() }
            };
            foreach (var group in groups.Where(gr => gr.Rows.Count > 0))
            {
                block++;
                ops.Add(new RuleOp { Block = block, Top = y, X = Pad });
                y += 16;
                ops.Add(new TextOp { Block = block, Top = y, X = Pad, LineHeight = caption, Text = group.Caption, Style = TextStyle.Caption, Color = Palette.Ink3 });
                y += caption + 10;
                foreach (TerminalProxyRow row in group.Rows)
                {
                    block++;
                    ops.Add(new GlyphOp { Block = block, Top = y, X = Pad, Size = Tile, Inset = 9, Radius = 9, Glyph = RowGlyph(row.Key), Color = Palette.Ink2, Fill = Palette.Surface2 });

                    Color fill, text;
                    BadgeColors(row.Status, out fill, out text);
                    float bw = 20 + (float)Math.Ceiling(Typo.Measure(row.Badge, TextStyle.Caption, s) / s);
                    float cx = Pad + Tile + 14;
                    float cw = width - cx - Pad - bw - 12;
                    float rowTop = y;
                    float lineY = y;
                    ops.Add(new TextOp { Block = block, Top = lineY, X = cx, LineHeight = body, Text = Typo.Ellipsize(row.Title, TextStyle.BodyMedium, cw * s, s), Style = TextStyle.BodyMedium, Color = Palette.Ink });
                    ops.Add(new BadgeOp { Block = block, Top = lineY + (body - 22) / 2, Right = Pad, Width = bw, Text = row.Badge, Fill = fill, TextColor = text });
                    lineY += body;
                    foreach (string line in Typo.Wrap(row.Detail, TextStyle.Small, cw * s, s))
                    {
                        ops.Add(new TextOp { Block = block, Top = lineY, X = cx, LineHeight = small, Text = line, Style = TextStyle.Small, Color = Palette.Ink2 });
                        lineY += small;
                    }
                    y = Math.Max(rowTop + Tile, lineY) + 14;
                }
                y += 6;
            }
            contentHeight = y + Pad - 20;
        }

        protected override bool IsAnimating(double now)
        {
            return base.IsAnimating(now) || loading || now - enteredAt < 0.3 + 0.022 * 14;
        }

        protected override void PaintContent(Graphics g, double now)
        {
            float s = S;
            double opacity = HostOpacity;
            using (GraphicsPath card = CardPath())
            {
                using (var brush = new SolidBrush(Palette.Fade(Palette.Surface, opacity)))
                    g.FillPath(brush, card);
                var state = g.Save();
                g.SetClip(card);

                if (report == null)
                {
                    double fade = Motion.EaseOutCubic(Motion.Progress(now, enteredAt, 0.2)) * opacity;
                    if (loading)
                    {
                        var center = new PointF(Width / 2f, Height / 2f - Px(18));
                        Spinner.Draw(g, center, Px(20), Px(1.75f), Palette.Ink, fade, now);
                        var rect = new RectangleF(Px(Pad), center.Y + Px(22), Width - Px(Pad * 2), Px(24));
                        Typo.Draw(g, message, TextStyle.Small, Palette.Fade(Palette.Ink2, fade), rect, s, StringAlignment.Center);
                    }
                    else if (!string.IsNullOrEmpty(message))
                    {
                        PaintEmptyState(g, Glyph.Alert, "检测失败", message, fade, new RectangleF(0, 0, Width, Height));
                    }
                }
                else
                {
                    EnsureLayout();
                    double scroll = ScrollAt(now);
                    float top = (float)scroll, bottom = top + Height / s;
                    foreach (Op op in ops)
                    {
                        if (op.Top > bottom + 40 || op.Top < top - 60) continue;
                        double enter = Motion.EaseOutCubic(Motion.Progress(now, enteredAt + Math.Min(op.Block, 14) * 0.022, 0.2));
                        float dy = (float)(-scroll + 5 * (1 - enter)) * s;
                        op.Paint(g, this, dy, opacity * enter, s);
                    }
                }

                PaintScrollbar(g, now, opacity);
                g.Restore(state);
            }
            Shapes.Stroke(g, new RectangleF(0, 0, Width, Height), Px(Metrics.PanelRadius), Palette.Fade(Palette.Line, opacity), Px(1));
        }

        protected override void OnMouseDown(System.Windows.Forms.MouseEventArgs e)
        {
            Focus();
            if (HandleScrollbarDown(e)) return;
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(System.Windows.Forms.Keys keyData)
        {
            return keyData == System.Windows.Forms.Keys.Up || keyData == System.Windows.Forms.Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(System.Windows.Forms.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case System.Windows.Forms.Keys.Up: ScrollTo(ScrollTarget - 60); break;
                case System.Windows.Forms.Keys.Down: ScrollTo(ScrollTarget + 60); break;
                case System.Windows.Forms.Keys.PageUp: ScrollTo(ScrollTarget - ViewportHeight * 0.9); break;
                case System.Windows.Forms.Keys.PageDown: ScrollTo(ScrollTarget + ViewportHeight * 0.9); break;
                case System.Windows.Forms.Keys.Home: ScrollTo(0); break;
                case System.Windows.Forms.Keys.End: ScrollTo(MaxScroll); break;
                default: return;
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// 代码块：surface2 底、等宽字体，过长的行折行显示；可选中复制由外部按钮完成
    /// </summary>
    public class CodeBlock : SpringControl
    {
        private readonly Crossfade<string> fade = new Crossfade<string>("");

        public CodeBlock()
        {
            SetStyle(System.Windows.Forms.ControlStyles.Selectable, false);
            TabStop = false;
        }

        /// <summary>
        /// 没有代码时显示的提示
        /// </summary>
        public string Placeholder { get; set; } = "";

        /// <summary>
        /// 右侧留给复制按钮的宽度（逻辑像素）
        /// </summary>
        public float RightInset { get; set; }

        protected override void OnTextChanged(EventArgs e)
        {
            if (IsHandleCreated && Visible) fade.Set(Text ?? "", Motion.Now);
            else fade.Reset(Text ?? "");
            base.OnTextChanged(e);
        }

        protected override bool IsAnimating(double now)
        {
            return fade.IsAnimatingAt(now);
        }

        /// <summary>
        /// 宽度为 width 时放下当前代码（折行后）需要的高度，不小于 minHeight
        /// </summary>
        public int HeightFor(int width, int minHeight)
        {
            float s = S;
            int count = Lines(Text ?? "", TextWidth(width), s).Count;
            return Math.Max(minHeight, (int)Math.Ceiling(count * LineHeight(s) + Px(20)));
        }

        private float LineHeight(float s)
        {
            return Typo.LineHeight(TextStyle.Mono, s) + Px(3);
        }

        private float TextWidth(float width)
        {
            return width - Px(14) - Px(14 + RightInset);
        }

        private static List<string> Lines(string text, float width, float s)
        {
            var lines = new List<string>();
            if (text.Length == 0) return lines;
            // 等宽字体：按列数折行
            float column = Typo.Measure("0000000000", TextStyle.Mono, s) / 10;
            int columns = column > 0 ? (int)Math.Floor(width / column) : int.MaxValue;
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
                lines.AddRange(ShellSnippets.Wrap(line, columns));
            return lines;
        }

        protected override void PaintContent(Graphics g, double now)
        {
            float s = S;
            double opacity = HostOpacity;
            var r = new RectangleF(0, 0, Width, Height);
            Shapes.Fill(g, r, Px(Metrics.InputRadius), Palette.Fade(Palette.Surface2, opacity));
            Shapes.Stroke(g, r, Px(Metrics.InputRadius), Palette.Fade(Palette.Line, opacity), Px(1));

            float lineHeight = LineHeight(s);
            float x = Px(14), maxWidth = TextWidth(Width);
            if (string.IsNullOrEmpty(Text))
            {
                Typo.Draw(g, Placeholder, TextStyle.Small, Palette.Fade(Palette.Ink3, opacity), new RectangleF(x, 0, maxWidth, Height), s);
                return;
            }
            foreach (var layer in fade.Layers(now))
            {
                string text = layer.Value ?? "";
                if (text.Length == 0) continue;
                List<string> lines = Lines(text, maxWidth, s);
                float y = (Height - lines.Count * lineHeight) / 2 + (float)layer.OffsetY * s;
                foreach (string line in lines)
                {
                    float baseline = Typo.CenterBaseline(new RectangleF(0, y, Width, lineHeight), TextStyle.Mono, s);
                    Typo.DrawLine(g, Typo.Ellipsize(line, TextStyle.Mono, maxWidth, s), TextStyle.Mono, Palette.Ink, x, baseline, s, opacity * layer.Opacity, layer.Blur);
                    y += lineHeight;
                }
            }
        }
    }
}
