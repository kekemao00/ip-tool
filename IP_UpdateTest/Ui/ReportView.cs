using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using IP_UpdateTest.Core.Diagnosis;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 分层诊断报告：顶部结论和链路，下面按环节列出各检查项。状态只用三种颜色：正常 accent、失败 danger、其余黑白灰
    /// </summary>
    public class ReportView : ScrollSurface
    {
        private const float Pad = 24;

        private abstract class Op
        {
            public int Block;
            public float Top;
            public abstract void Paint(Graphics g, ReportView view, float dy, double opacity, float s);
        }

        private sealed class TextOp : Op
        {
            public float X, LineHeight;
            public string Text;
            public TextStyle Style;
            public Color Color;

            public override void Paint(Graphics g, ReportView view, float dy, double opacity, float s)
            {
                float baseline = Typo.CenterBaseline(new RectangleF(0, (Top) * s + dy, view.Width, LineHeight * s), Style, s);
                Typo.DrawLine(g, Text, Style, Palette.Fade(Color, opacity), X * s, baseline, s);
            }
        }

        private sealed class GlyphOp : Op
        {
            public float X, Size;
            public Glyph Glyph;
            public Color Color;
            public Color Fill;

            public override void Paint(Graphics g, ReportView view, float dy, double opacity, float s)
            {
                var box = new RectangleF(X * s, Top * s + dy, Size * s, Size * s);
                if (Fill.A > 0)
                {
                    using (var brush = new SolidBrush(Palette.Fade(Fill, opacity)))
                        g.FillEllipse(brush, box);
                    box.Inflate(-Size * s * 0.25f, -Size * s * 0.25f);
                }
                Icons.Draw(g, Glyph, box, Palette.Fade(Color, opacity), s, Fill.A > 0 ? 2f : 1.5f);
            }
        }

        private sealed class PillOp : Op
        {
            public float X, Width;
            public string Text;
            public Glyph Glyph;
            public Color GlyphColor;

            public override void Paint(Graphics g, ReportView view, float dy, double opacity, float s)
            {
                var r = new RectangleF(X * s, Top * s + dy, Width * s, 26 * s);
                Shapes.Fill(g, r, r.Height / 2, Palette.Fade(Palette.Surface, opacity));
                Shapes.Stroke(g, r, r.Height / 2, Palette.Fade(Palette.Line, opacity), s);
                float icon = 14 * s;
                Icons.Draw(g, Glyph, new RectangleF(r.X + 8 * s, r.Y + (r.Height - icon) / 2, icon, icon), Palette.Fade(GlyphColor, opacity), s, 2f);
                Typo.Draw(g, Text, TextStyle.Small, Palette.Fade(Palette.Ink, opacity), new RectangleF(r.X + 28 * s, r.Y, r.Width - 30 * s, r.Height), s);
            }
        }

        private sealed class RuleOp : Op
        {
            public override void Paint(Graphics g, ReportView view, float dy, double opacity, float s)
            {
                using (var pen = new Pen(Palette.Fade(Palette.Line, opacity), s))
                    g.DrawLine(pen, Pad * s, Top * s + dy, view.Width - Pad * s, Top * s + dy);
            }
        }

        private readonly List<Op> ops = new List<Op>();
        private DiagnosisReport report;
        private string message;
        private bool loading;
        private float contentHeight;
        private int layoutWidth = -1;
        private float layoutScale;
        private double enteredAt = double.NegativeInfinity;

        public DiagnosisReport Report
        {
            get { return report; }
        }

        /// <summary>
        /// 显示加载中或出错信息
        /// </summary>
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

        public void ShowReport(DiagnosisReport value)
        {
            report = value;
            message = null;
            loading = false;
            layoutWidth = -1;
            enteredAt = IsHandleCreated ? Motion.Now : double.NegativeInfinity;
            ScrollTo(0, false);
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

        public static Color StatusColor(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Ok: return Palette.Accent;
                case CheckStatus.Fail: return Palette.Danger;
                case CheckStatus.Warning: return Palette.Ink;
                default: return Palette.Ink3;
            }
        }

        public static Glyph StatusGlyph(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Ok: return Glyph.Check;
                case CheckStatus.Fail: return Glyph.Close;
                case CheckStatus.Warning: return Glyph.Alert;
                case CheckStatus.Info: return Glyph.Info;
                default: return Glyph.Minus;
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
            float textWidth = width - Pad * 2;
            float y = Pad;
            int block = 0;

            // 结论
            DiagnosisLayer? fault = report.FaultLayer;
            int warnings = report.Checks.Count(c => c.Status == CheckStatus.Warning);
            CheckStatus overall = fault.HasValue ? CheckStatus.Fail : warnings > 0 ? CheckStatus.Warning : CheckStatus.Ok;
            ops.Add(new GlyphOp
            {
                Block = block, Top = y, X = Pad, Size = 32, Glyph = StatusGlyph(overall), Color = Color.White,
                Fill = overall == CheckStatus.Fail ? Palette.Danger : overall == CheckStatus.Ok ? Palette.Accent : Palette.Ink
            });
            float x = Pad + 32 + 14;
            float titleLine = Typo.LineHeight(TextStyle.Title, s) / s;
            float ty = y + Math.Max(0, (32 - titleLine) / 2);
            foreach (string line in Typo.Wrap(report.Summary, TextStyle.Title, (width - x - Pad) * s, s))
            {
                ops.Add(new TextOp { Block = block, Top = ty, X = x, LineHeight = titleLine, Text = line, Style = TextStyle.Title, Color = Palette.Ink });
                ty += titleLine;
            }
            string meta = report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss") + (string.IsNullOrEmpty(report.AdapterName) ? "" : " · " + report.AdapterName);
            float small = Typo.LineHeight(TextStyle.Small, s) / s;
            ops.Add(new TextOp { Block = block, Top = ty, X = x, LineHeight = small, Text = meta, Style = TextStyle.Small, Color = Palette.Ink3 });
            y = Math.Max(y + 32, ty + small) + 18;

            // 链路
            block++;
            var layers = ((DiagnosisLayer[])Enum.GetValues(typeof(DiagnosisLayer))).Where(l => report.Checks.Any(c => c.Layer == l)).ToList();
            float px = Pad;
            foreach (DiagnosisLayer layer in layers)
            {
                string name = DiagnosisReport.LayerName(layer);
                float pw = 38 + (float)Math.Ceiling(Typo.Measure(name, TextStyle.Small, s) / s);
                if (px + pw > width - Pad && px > Pad)
                {
                    px = Pad;
                    y += 34;
                }
                CheckStatus status = report.LayerStatus(layer);
                ops.Add(new PillOp { Block = block, Top = y, X = px, Width = pw, Text = name, Glyph = StatusGlyph(status), GlyphColor = StatusColor(status) });
                px += pw;
                if (layer != layers[layers.Count - 1])
                {
                    ops.Add(new GlyphOp { Block = block, Top = y + 6, X = px + 3, Size = 14, Glyph = Glyph.ChevronRight, Color = Palette.Ink3 });
                    px += 20;
                }
            }
            y += 26 + 20;

            // 建议
            List<string> suggestions = report.KeySuggestions;
            float body = Typo.LineHeight(TextStyle.Body, s) / s;
            float caption = Typo.LineHeight(TextStyle.Caption, s) / s;
            if (suggestions.Count > 0)
            {
                block++;
                ops.Add(new TextOp { Block = block, Top = y, X = Pad, LineHeight = caption, Text = "建议", Style = TextStyle.Caption, Color = Palette.Ink3 });
                y += caption + 6;
                for (int i = 0; i < suggestions.Count; i++)
                {
                    ops.Add(new TextOp { Block = block, Top = y, X = Pad, LineHeight = body, Text = (i + 1).ToString(), Style = TextStyle.Mono, Color = Palette.Ink3 });
                    foreach (string line in Typo.Wrap(suggestions[i], TextStyle.Body, (textWidth - 20) * s, s))
                    {
                        ops.Add(new TextOp { Block = block, Top = y, X = Pad + 20, LineHeight = body, Text = line, Style = TextStyle.Body, Color = Palette.Ink });
                        y += body;
                    }
                    y += 4;
                }
                y += 14;
            }

            // 各环节
            int index = 0;
            foreach (DiagnosisLayer layer in layers)
            {
                block++;
                ops.Add(new RuleOp { Block = block, Top = y });
                y += 16;
                CheckStatus status = report.LayerStatus(layer);
                ops.Add(new TextOp { Block = block, Top = y, X = Pad, LineHeight = caption, Text = (++index) + "  " + DiagnosisReport.LayerName(layer), Style = TextStyle.Caption, Color = Palette.Ink3 });
                y += caption + 8;
                foreach (DiagnosisCheck check in report.Checks.Where(c => c.Layer == layer))
                {
                    ops.Add(new GlyphOp { Block = block, Top = y + (body - 16) / 2, X = Pad, Size = 16, Glyph = StatusGlyph(check.Status), Color = StatusColor(check.Status) });
                    float cx = Pad + 26;
                    float cw = (width - cx - Pad) * s;
                    ops.Add(new TextOp { Block = block, Top = y, X = cx, LineHeight = body, Text = check.Title, Style = TextStyle.BodyMedium, Color = check.Status == CheckStatus.Skipped ? Palette.Ink3 : Palette.Ink });
                    y += body;
                    foreach (string line in Typo.Wrap(check.Detail, TextStyle.Body, cw, s))
                    {
                        ops.Add(new TextOp { Block = block, Top = y, X = cx, LineHeight = body, Text = line, Style = TextStyle.Body, Color = Palette.Ink2 });
                        y += body;
                    }
                    foreach (string suggestion in check.Suggestions)
                    {
                        bool firstLine = true;
                        foreach (string line in Typo.Wrap(suggestion, TextStyle.Small, cw - 18 * s, s))
                        {
                            if (firstLine) ops.Add(new GlyphOp { Block = block, Top = y + (small - 12) / 2, X = cx, Size = 12, Glyph = Glyph.ChevronRight, Color = Palette.Ink3 });
                            ops.Add(new TextOp { Block = block, Top = y, X = cx + 18, LineHeight = small, Text = line, Style = TextStyle.Small, Color = Palette.Ink2 });
                            y += small;
                            firstLine = false;
                        }
                    }
                    y += 10;
                }
                y += 8;
            }
            contentHeight = y + Pad - 10;
        }

        protected override bool IsAnimating(double now)
        {
            return base.IsAnimating(now) || loading || now - enteredAt < 0.3 + 0.022 * 12;
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
                        float d = Px(20);
                        var center = new PointF(Width / 2f, Height / 2f - Px(18));
                        Spinner.Draw(g, center, d, Px(1.75f), Palette.Ink, fade, now);
                        var rect = new RectangleF(Px(Pad), center.Y + Px(22), Width - Px(Pad * 2), Px(24));
                        Typo.Draw(g, message, TextStyle.Small, Palette.Fade(Palette.Ink2, fade), rect, s, StringAlignment.Center);
                    }
                    else if (!string.IsNullOrEmpty(message))
                    {
                        PaintEmptyState(g, Glyph.Alert, "诊断失败", message, fade, new RectangleF(0, 0, Width, Height));
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
                        // 各块错开 22ms 从下方 5px 淡入
                        double enter = Motion.EaseOutCubic(Motion.Progress(now, enteredAt + Math.Min(op.Block, 12) * 0.022, 0.2));
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
}
