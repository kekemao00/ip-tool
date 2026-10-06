using System;
using System.Drawing;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    public enum ButtonKind
    {
        /// <summary>墨黑底白字</summary>
        Primary,
        /// <summary>强调色实心</summary>
        Accent,
        /// <summary>白底 + 1px 描边</summary>
        Secondary,
        /// <summary>透明，悬停 surface-3</summary>
        Ghost,
        /// <summary>只在确认危险操作时用</summary>
        Danger
    }

    /// <summary>
    /// 按钮。主按钮可以变形：按钮 → 加载（收成圆） → 对勾 / 错误（展开、弹到 danger、1.8s 后自己变回）
    /// </summary>
    public class SpringButton : SpringControl, IButtonControl
    {
        private enum Morph
        {
            Idle,
            Loading,
            Success,
            Error
        }

        private const double SuccessHold = 1.1;
        private const double ErrorHold = 1.8;
        private const double CheckDuration = 0.28;

        private readonly Crossfade<string> label = new Crossfade<string>("");
        private readonly Crossfade<Glyph> glyphFade = new Crossfade<Glyph>(Glyph.None);

        /// <summary>1 为完整按钮，0 为收成的圆</summary>
        private readonly SpringValue shape = new SpringValue(1, Spring.Default);

        /// <summary>0..1：底色弹到 danger 的程度</summary>
        private readonly SpringValue danger = new SpringValue(0, Spring.Snappy);

        private readonly Timer revertTimer = new Timer();
        private Morph morph;
        private double morphAt;
        private ButtonKind kind = ButtonKind.Secondary;
        private Glyph glyph;
        private bool pressedInside;

        public SpringButton()
        {
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            SetStyle(ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            revertTimer.Tick += (s, e) =>
            {
                revertTimer.Stop();
                Reset();
            };
        }

        public ButtonKind Kind
        {
            get { return kind; }
            set
            {
                kind = value;
                Invalidate();
            }
        }

        /// <summary>
        /// 前置图标；没有文字时是正方形的纯图标按钮
        /// </summary>
        public Glyph Glyph
        {
            get { return glyph; }
            set
            {
                glyph = value;
                glyphFade.Reset(value);
                Invalidate();
            }
        }

        /// <summary>
        /// 末尾的下拉箭头
        /// </summary>
        public bool ShowChevron { get; set; }

        public DialogResult DialogResult { get; set; }

        public bool IsLoading
        {
            get { return morph == Morph.Loading; }
        }

        private bool IsSolid
        {
            get { return kind == ButtonKind.Primary || kind == ButtonKind.Accent || kind == ButtonKind.Danger; }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (morph != Morph.Error)
            {
                if (IsHandleCreated && Visible) label.Set(Text, Motion.Now);
                else label.Reset(Text);
            }
            base.OnTextChanged(e);
        }

        /// <summary>
        /// 按内容计算的宽度（设备像素）
        /// </summary>
        public int PreferredWidth
        {
            get
            {
                float s = S;
                if (string.IsNullOrEmpty(Text)) return Height;
                float w = Typo.Measure(Text, TextStyle.Label, s) + Px(14) * 2;
                if (glyph != Glyph.None) w += Px(16 + 6);
                if (ShowChevron) w += Px(14 + 4);
                return (int)Math.Ceiling(w);
            }
        }

        #region 变形

        /// <summary>
        /// 收成圆并转圈
        /// </summary>
        public void BeginLoading()
        {
            revertTimer.Stop();
            morph = Morph.Loading;
            morphAt = Motion.Now;
            shape.Set(0);
            danger.Set(0);
            Animate();
        }

        /// <summary>
        /// 圆里画出对勾，稍后变回按钮
        /// </summary>
        public void Succeed()
        {
            morph = Morph.Success;
            morphAt = Motion.Now;
            shape.Set(0);
            danger.Set(0);
            Schedule(SuccessHold);
            Animate();
        }

        /// <summary>
        /// 展开回全宽、底色弹到 danger、文字换成错误信息，1.8s 后自己变回
        /// </summary>
        public void Fail(string text)
        {
            morph = Morph.Error;
            morphAt = Motion.Now;
            shape.Set(1);
            danger.Set(1);
            label.Set(text, morphAt);
            glyphFade.Set(Glyph.Alert, morphAt);
            Schedule(ErrorHold);
            Animate();
        }

        /// <summary>
        /// 变回普通按钮
        /// </summary>
        public void Reset()
        {
            revertTimer.Stop();
            if (morph == Morph.Idle) return;
            morph = Morph.Idle;
            double now = Motion.Now;
            shape.Set(1);
            danger.Set(0);
            label.Set(Text, now);
            glyphFade.Set(glyph, now);
            Animate();
        }

        private void Schedule(double seconds)
        {
            revertTimer.Stop();
            revertTimer.Interval = (int)(seconds * 1000);
            revertTimer.Start();
        }

        #endregion

        #region 绘制

        protected override bool IsAnimating(double now)
        {
            return base.IsAnimating(now) || morph == Morph.Loading || shape.IsAnimatingAt(now) || danger.IsAnimatingAt(now)
                || label.IsAnimatingAt(now) || glyphFade.IsAnimatingAt(now)
                || morph == Morph.Success && now - morphAt < CheckDuration + 0.4;
        }

        protected override void PaintContent(Graphics g, double now)
        {
            float s = S;
            RectangleF full = new RectangleF(0, 0, Width, Height);
            double k = Motion.Clamp01(shape.ValueAt(now));
            float h = full.Height;
            float w = (float)Motion.Lerp(h, full.Width, k);
            var r = new RectangleF(full.X + (full.Width - w) / 2, full.Y, w, h);

            double opacity = (Enabled || morph != Morph.Idle ? 1 : 0.45) * HostOpacity;
            double hover = HoverT.ValueAt(now), press = PressT.ValueAt(now), focus = FocusT.ValueAt(now);
            if (!Enabled) hover = press = 0;

            // 按下时整体缩到 96.5%
            float scale = (float)(1 - 0.035 * press);
            var state = g.Save();
            g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-(r.X + r.Width / 2), -(r.Y + r.Height / 2));

            float radius = IsSolid && h >= Px(40) ? Px(Metrics.PrimaryRadius) : Px(Metrics.ButtonRadius);
            radius = (float)Motion.Lerp(h / 2, radius, k);

            Color fill, border = Color.Empty, text;
            switch (kind)
            {
                case ButtonKind.Primary:
                    fill = Palette.Mix(Palette.Mix(Palette.Ink, Palette.InkHover, hover), Color.Black, 0.6 * press);
                    text = Color.White;
                    break;
                case ButtonKind.Accent:
                    fill = Palette.Mix(Palette.Mix(Palette.Accent, Palette.Ink, 0.12 * hover), Color.Black, 0.2 * press);
                    text = Color.White;
                    break;
                case ButtonKind.Danger:
                    fill = Palette.Mix(Palette.Mix(Palette.Danger, Palette.Ink, 0.12 * hover), Color.Black, 0.2 * press);
                    text = Color.White;
                    break;
                case ButtonKind.Ghost:
                    fill = Palette.Fade(Palette.Mix(Palette.Surface3, Palette.LineStrong, 0.5 * press), 0.75 * Math.Max(hover, press));
                    text = Palette.Mix(Palette.Ink2, Palette.Ink, hover);
                    break;
                default:
                    fill = Palette.Mix(Palette.Mix(Palette.Surface, Palette.Surface2, hover), Palette.Surface3, press);
                    border = Palette.Mix(Palette.Line, Palette.LineStrong, hover);
                    text = Palette.Ink;
                    break;
            }

            double d = Motion.Clamp01(danger.ValueAt(now));
            if (d > 0)
            {
                fill = Palette.Mix(fill, Palette.Danger, d);
                border = border.IsEmpty ? border : Palette.Mix(border, Palette.Danger, d);
                text = Palette.Mix(text, Color.White, d);
            }

            Shapes.Fill(g, r, radius, Palette.Fade(fill, opacity));
            if (!border.IsEmpty) Shapes.Stroke(g, r, radius, Palette.Fade(border, opacity), Px(1));

            if (KeyboardFocused && focus > 0.01)
            {
                if (IsSolid || d > 0.5) Shapes.Stroke(g, RectangleF.Inflate(r, -Px(3), -Px(3)), radius - Px(3), Palette.Fade(Color.White, focus * opacity), Px(1.5f));
                else Shapes.Stroke(g, r, radius, Palette.Fade(Palette.Accent, focus * opacity), Px(2));
            }

            PaintLabel(g, r, k, text, opacity, now, s);
            PaintMorphGlyph(g, r, k, text, opacity, now, s);
            g.Restore(state);
        }

        /// <summary>
        /// 图标 + 文字 + 箭头。文字在形状宽度超过 55% 时才可见
        /// </summary>
        private void PaintLabel(Graphics g, RectangleF r, double k, Color color, double opacity, double now, float s)
        {
            double visible = Motion.Clamp01((k - 0.55) / 0.3) * opacity;
            if (visible <= 0.004) return;

            foreach (var layer in label.Layers(now))
            {
                string text = layer.Value ?? "";
                Glyph icon = glyphFade.Current;
                bool iconOnly = text.Length == 0;
                float iconSize = Px(16);
                float textWidth = Typo.Measure(text, TextStyle.Label, s);
                float content = textWidth;
                if (icon != Glyph.None) content += iconOnly ? iconSize : iconSize + Px(6);
                if (ShowChevron && !iconOnly) content += Px(4 + 14);
                float x = r.X + (r.Width - content) / 2;
                float cy = r.Y + r.Height / 2 + (float)layer.OffsetY * s;
                double alpha = visible * layer.Opacity;

                if (icon != Glyph.None)
                {
                    Icons.Draw(g, icon, new RectangleF(x, cy - iconSize / 2, iconSize, iconSize), Palette.Fade(color, alpha), s);
                    x += iconOnly ? iconSize : iconSize + Px(6);
                }
                if (!iconOnly)
                {
                    float baseline = Typo.CenterBaseline(new RectangleF(r.X, cy - r.Height / 2, r.Width, r.Height), TextStyle.Label, s);
                    Typo.DrawLine(g, text, TextStyle.Label, color, x, baseline, s, alpha, layer.Blur);
                    x += textWidth;
                }
                if (ShowChevron && !iconOnly)
                {
                    float c = Px(14);
                    Icons.Draw(g, Glyph.ChevronDown, new RectangleF(x + Px(4), cy - c / 2, c, c), Palette.Fade(color, alpha * 0.7), s);
                }
            }
        }

        /// <summary>
        /// 加载的圆弧、成功的对勾
        /// </summary>
        private void PaintMorphGlyph(Graphics g, RectangleF r, double k, Color color, double opacity, double now, float s)
        {
            double visible = Motion.Clamp01((0.45 - k) / 0.3) * opacity;
            if (visible <= 0.004 || morph == Morph.Idle && k > 0.95) return;

            var center = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
            if (morph == Morph.Loading)
            {
                Spinner.Draw(g, center, Px(16), Px(1.75f), color, visible, now);
            }
            else if (morph == Morph.Success)
            {
                double p = Motion.EaseOutCubic(Motion.Progress(now, morphAt + 0.12, CheckDuration));
                float size = Px(18);
                Icons.DrawPartial(g, Glyph.Check, new RectangleF(center.X - size / 2, center.Y - size / 2, size, size), Palette.Fade(color, visible), s, p, 2f);
            }
        }

        #endregion

        #region 点击与键盘

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) pressedInside = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool click = pressedInside && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
            pressedInside = false;
            if (click) OnClick(EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space)
            {
                PressT.Set(1);
                Animate();
                e.Handled = true;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space)
            {
                PressT.Set(0);
                Animate();
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            Form form = FindForm();
            if (form != null && DialogResult != DialogResult.None) form.DialogResult = DialogResult;
            base.OnClick(e);
        }

        public void NotifyDefault(bool value)
        {
        }

        public void PerformClick()
        {
            if (Enabled && Visible && CanSelect) OnClick(EventArgs.Empty);
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing) revertTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 加载圆弧：底圈 22%，弧长在 90°–210° 间呼吸，持续旋转
    /// </summary>
    public static class Spinner
    {
        public static void Draw(Graphics g, PointF center, float diameter, float stroke, Color color, double opacity, double now)
        {
            var rect = new RectangleF(center.X - diameter / 2, center.Y - diameter / 2, diameter, diameter);
            using (var track = new Pen(Palette.Fade(color, 0.22 * opacity), stroke))
                g.DrawEllipse(track, rect);
            float rotation = (float)(now * 400 % 360);
            float sweep = (float)(150 + 60 * Math.Sin(now * 2 * Math.PI / 1.4));
            using (var pen = new Pen(Palette.Fade(color, opacity), stroke))
            {
                pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawArc(pen, rect, rotation, sweep);
            }
        }
    }
}
