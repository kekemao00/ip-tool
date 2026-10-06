using System;
using System.Drawing;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 白色卡片：surface 底、1px line 描边、圆角 16。层次靠和 canvas 的明度差与描边表达，不用阴影
    /// </summary>
    public class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public float Radius { get; set; } = Metrics.PanelRadius;

        /// <summary>
        /// 填充色，默认白色
        /// </summary>
        public Color Fill { get; set; } = Palette.Surface;

        protected override void OnPaint(PaintEventArgs e)
        {
            Shapes.Prepare(e.Graphics);
            float s = DeviceDpi / 96f;
            var r = new RectangleF(0, 0, Width, Height);
            Shapes.Fill(e.Graphics, r, Radius * s, Fill);
            Shapes.Stroke(e.Graphics, r, Radius * s, Palette.Line, s);
            base.OnPaint(e);
        }
    }

    /// <summary>
    /// 1px 分隔线
    /// </summary>
    public class Rule : Control
    {
        public Rule()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var brush = new SolidBrush(Palette.Line))
                e.Graphics.FillRectangle(brush, 0, 0, Width, Math.Max(1, Height));
        }
    }

    /// <summary>
    /// 自绘文字。换内容时新旧文字在模糊里交叉淡化，超长时以省略号结尾并在悬停提示中显示全文
    /// </summary>
    public class InkLabel : SpringControl
    {
        private readonly Crossfade<string> fade = new Crossfade<string>("");
        private Color color = Palette.Ink2;
        private TextStyle style = TextStyle.Body;
        private Glyph glyph;
        private string lastTip;

        public InkLabel()
        {
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        public TextStyle Style
        {
            get { return style; }
            set
            {
                style = value;
                Invalidate();
            }
        }

        public Color TextColor
        {
            get { return color; }
            set
            {
                color = value;
                Invalidate();
            }
        }

        public StringAlignment Align { get; set; } = StringAlignment.Near;

        /// <summary>
        /// 前置 16px 图标
        /// </summary>
        public Glyph Glyph
        {
            get { return glyph; }
            set
            {
                glyph = value;
                Invalidate();
            }
        }

        /// <summary>
        /// 为 true 时按宽度折行，否则单行截断
        /// </summary>
        public bool Wrap { get; set; }

        protected override void OnTextChanged(EventArgs e)
        {
            if (IsHandleCreated && Visible) fade.Set(Text ?? "", Motion.Now);
            else fade.Reset(Text ?? "");
            base.OnTextChanged(e);
        }

        public int PreferredWidth
        {
            get
            {
                float w = Typo.Measure(Text, style, S);
                if (glyph != Glyph.None) w += Px(16 + 6);
                return (int)Math.Ceiling(w) + 1;
            }
        }

        /// <summary>
        /// 折行后的高度（设备像素）
        /// </summary>
        public int MeasureHeight(int width)
        {
            float s = S;
            float textWidth = width - (glyph != Glyph.None ? Px(22) : 0);
            int lines = Wrap ? Math.Max(1, Typo.Wrap(Text ?? "", style, textWidth, s).Count) : 1;
            return (int)Math.Ceiling(lines * Typo.LineHeight(style, s));
        }

        protected override bool IsAnimating(double now)
        {
            return fade.IsAnimatingAt(now);
        }

        protected override void PaintContent(Graphics g, double now)
        {
            float s = S;
            double opacity = (Enabled ? 1 : 0.6) * HostOpacity;
            float x = 0;
            float lineHeight = Typo.LineHeight(style, s);
            if (glyph != Glyph.None)
            {
                float size = Px(16);
                float top = Wrap ? (lineHeight - size) / 2 : (Height - size) / 2;
                Icons.Draw(g, glyph, new RectangleF(0, top, size, size), Palette.Fade(color, opacity), s);
                x = Px(22);
            }
            float width = Width - x;

            string tip = null;
            foreach (var layer in fade.Layers(now))
            {
                string text = layer.Value ?? "";
                if (text.Length == 0) continue;
                double alpha = opacity * layer.Opacity;
                float dy = (float)layer.OffsetY * s;
                if (Wrap)
                {
                    float y = 0;
                    foreach (string line in Typo.Wrap(text, style, width, s))
                    {
                        float baseline = Typo.CenterBaseline(new RectangleF(0, y + dy, Width, lineHeight), style, s);
                        Typo.DrawLine(g, line, style, color, x, baseline, s, alpha, layer.Blur);
                        y += lineHeight;
                    }
                    continue;
                }

                string fitted = Typo.Ellipsize(text, style, width, s);
                if (!ReferenceEquals(text, fitted) && fitted != text) tip = text;
                float textWidth = Typo.Measure(fitted, style, s);
                float tx = Align == StringAlignment.Center ? x + (width - textWidth) / 2 : Align == StringAlignment.Far ? Width - textWidth : x;
                float b = Typo.CenterBaseline(new RectangleF(0, dy, Width, Height), style, s);
                Typo.DrawLine(g, fitted, style, color, tx, b, s, alpha, layer.Blur);
            }

            // 截断时在悬停提示里显示全文
            if (!Wrap && tip != lastTip)
            {
                lastTip = tip;
                InkToolTip.Shared.SetToolTip(this, tip ?? "");
            }
        }
    }

    /// <summary>
    /// 悬停提示：墨黑底白字，内边距 6×9，延迟 450ms
    /// </summary>
    public sealed class InkToolTip : ToolTip
    {
        private static InkToolTip shared;

        private InkToolTip()
        {
            OwnerDraw = true;
            InitialDelay = 450;
            ReshowDelay = 100;
            AutoPopDelay = 8000;
            UseAnimation = false;
            UseFading = false;
            Popup += OnPopup;
            Draw += OnDraw;
        }

        public static InkToolTip Shared
        {
            get { return shared ?? (shared = new InkToolTip()); }
        }

        private static float ScaleOf(Control control)
        {
            return control == null ? 1 : control.DeviceDpi / 96f;
        }

        private void OnPopup(object sender, PopupEventArgs e)
        {
            string text = GetToolTip(e.AssociatedControl);
            float s = ScaleOf(e.AssociatedControl);
            float width = 0;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines) width = Math.Max(width, Typo.Measure(line, TextStyle.Small, s));
            float maxWidth = 360 * s;
            int lineCount = 0;
            if (width > maxWidth)
            {
                foreach (string line in lines) lineCount += Typo.Wrap(line, TextStyle.Small, maxWidth, s).Count;
                width = maxWidth;
            }
            else lineCount = lines.Length;
            e.ToolTipSize = new Size((int)Math.Ceiling(width + 18 * s), (int)Math.Ceiling(lineCount * Typo.LineHeight(TextStyle.Small, s) + 12 * s));
        }

        private void OnDraw(object sender, DrawToolTipEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float s = ScaleOf(e.AssociatedControl);
            using (var brush = new SolidBrush(Palette.Ink))
                g.FillRectangle(brush, e.Bounds);
            float y = 6 * s;
            float lineHeight = Typo.LineHeight(TextStyle.Small, s);
            foreach (string paragraph in e.ToolTipText.Replace("\r\n", "\n").Split('\n'))
            {
                foreach (string line in Typo.Wrap(paragraph, TextStyle.Small, e.Bounds.Width - 18 * s, s))
                {
                    float baseline = Typo.CenterBaseline(new RectangleF(0, y, e.Bounds.Width, lineHeight), TextStyle.Small, s);
                    Typo.DrawLine(g, line, TextStyle.Small, Color.White, 9 * s, baseline, s);
                    y += lineHeight;
                }
            }
        }
    }
}
