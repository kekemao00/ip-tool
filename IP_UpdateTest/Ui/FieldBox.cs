using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 输入框：白底、1px line、圆角 10、高 40，前置 16px 图标。
    /// 悬停描边变深，聚焦描边变 accent 加 3px 光环；出错时描边和光环弹到 danger 并水平抖一下。
    /// 外围留 4px 给光环和抖动，布局时用 Place 按可见框定位
    /// </summary>
    public class FieldBox : SpringControl
    {
        private const int EM_SETCUEBANNER = 0x1501;

        private readonly SpringValue error = new SpringValue(0, Spring.Snappy);
        private readonly SpringValue shake = new SpringValue(0, Spring.Shake);
        private Glyph glyph;
        private string placeholder = "";
        private string errorText;
        private bool mono;
        private bool shaking;

        public FieldBox()
        {
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.IBeam;
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Palette.Surface,
                ForeColor = Palette.Ink
            };
            Box.TextChanged += (s, e) => OnTextChanged(e);
            Box.GotFocus += (s, e) =>
            {
                FocusT.Set(1);
                Animate();
            };
            Box.LostFocus += (s, e) =>
            {
                FocusT.Set(0);
                Animate();
            };
            Box.MouseEnter += (s, e) => UpdateHover();
            Box.MouseLeave += (s, e) => UpdateHover();
            Box.HandleCreated += (s, e) => ApplyCueBanner();
            Controls.Add(Box);
        }

        /// <summary>
        /// 内部的原生输入框
        /// </summary>
        public TextBox Box { get; private set; }

        public Glyph Glyph
        {
            get { return glyph; }
            set
            {
                glyph = value;
                PerformLayout();
                Invalidate();
            }
        }

        /// <summary>
        /// 只输入 IP 等 ASCII 内容时用等宽的 Geist Mono
        /// </summary>
        public bool Mono
        {
            get { return mono; }
            set
            {
                mono = value;
                ApplyFont();
            }
        }

        public string Placeholder
        {
            get { return placeholder; }
            set
            {
                placeholder = value ?? "";
                ApplyCueBanner();
            }
        }

        public override string Text
        {
            get { return Box == null ? "" : Box.Text; }
            set { if (Box != null) Box.Text = value ?? ""; }
        }

        /// <summary>
        /// 错误信息；为 null 或空时恢复正常
        /// </summary>
        public string ErrorText
        {
            get { return errorText ?? ""; }
        }

        public void SetError(string message, bool shakeNow = true)
        {
            bool had = !string.IsNullOrEmpty(errorText);
            errorText = string.IsNullOrEmpty(message) ? null : message;
            bool has = errorText != null;
            error.Set(has ? 1 : 0);
            InkToolTip.Shared.SetToolTip(Box, errorText ?? "");
            InkToolTip.Shared.SetToolTip(this, errorText ?? "");
            if (has && shakeNow && (!had || shakeNow)) Shake();
            Animate();
        }

        /// <summary>
        /// 整框水平抖一下（shake 弹簧，初速度 260px/s）
        /// </summary>
        public void Shake()
        {
            double now = Motion.Now;
            shake.Snap(shake.ValueAt(now));
            shake.Set(0, now);
            shake.Kick(260, now);
            if (shaking) return;
            shaking = true;
            FrameClock.Run(() =>
            {
                if (IsDisposed) return false;
                LayoutBox();
                shaking = shake.IsAnimatingAt(Motion.Now);
                if (!shaking) LayoutBox();
                return shaking;
            });
            Animate();
        }

        public void FocusInput()
        {
            Box.Focus();
            Box.SelectAll();
        }

        /// <summary>
        /// 按可见框（不含外围留白）定位，单位为设备像素
        /// </summary>
        public void Place(Rectangle visual)
        {
            int m = (int)Math.Round(Px(Metrics.FieldMargin));
            Bounds = Rectangle.Inflate(visual, m, m);
        }

        private RectangleF Visual(double now)
        {
            float m = Px(Metrics.FieldMargin);
            float dx = (float)(shake.ValueAt(now) * S);
            return new RectangleF(m + dx, m, Width - m * 2, Height - m * 2);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyFont();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            BeginInvoke((Action)ApplyFont);
        }

        private void ApplyFont()
        {
            if (Box == null) return;
            Box.Font = Typo.NativeFont(TextStyle.Input, S, mono);
            LayoutBox();
        }

        private void ApplyCueBanner()
        {
            if (Box != null && Box.IsHandleCreated)
                SendMessage(Box.Handle, EM_SETCUEBANNER, (IntPtr)1, placeholder);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            LayoutBox();
        }

        private void LayoutBox()
        {
            if (Box == null) return;
            RectangleF v = Visual(Motion.Now);
            float left = v.X + Px(glyph != Glyph.None ? 38 : 13);
            int width = (int)(v.Right - Px(12) - left);
            int top = (int)Math.Round(v.Y + (v.Height - Box.Height) / 2);
            Box.SetBounds((int)Math.Round(left), top, Math.Max(10, width), Box.Height);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Box.BackColor = Enabled ? Palette.Surface : Palette.Surface2;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Enabled) Box.Focus();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            UpdateHover();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            UpdateHover();
        }

        private void UpdateHover()
        {
            bool inside = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position));
            HoverT.Set(inside ? 1 : 0);
            Animate();
        }

        protected override bool IsAnimating(double now)
        {
            return base.IsAnimating(now) || error.IsAnimatingAt(now) || shake.IsAnimatingAt(now);
        }

        protected override void PaintContent(Graphics g, double now)
        {
            float s = S;
            RectangleF v = Visual(now);
            float radius = Px(Metrics.InputRadius);
            double opacity = HostOpacity;
            double hover = HoverT.ValueAt(now), focus = FocusT.ValueAt(now), err = Motion.Clamp01(error.ValueAt(now));

            // 光环：实色描边外一圈 3px
            double ring = Math.Max(focus, err);
            if (ring > 0.01)
            {
                Color ringColor = Palette.Mix(Palette.FocusRing, Color.FromArgb(56, Palette.Danger), err);
                float w = Px(3);
                Shapes.Stroke(g, RectangleF.Inflate(v, w, w), radius + w, Palette.Fade(ringColor, ring * opacity), w);
            }

            Color fill = Enabled ? Palette.Surface : Palette.Surface2;
            Color border = Palette.Mix(Palette.Mix(Palette.Line, Palette.LineStrong, hover), Palette.Accent, focus);
            border = Palette.Mix(border, Palette.Danger, err);
            Shapes.Fill(g, v, radius, Palette.Fade(fill, opacity));
            Shapes.Stroke(g, v, radius, Palette.Fade(border, opacity), Px(1));

            if (glyph != Glyph.None)
            {
                float size = Px(16);
                Color iconColor = Palette.Mix(Palette.Mix(Palette.Ink3, Palette.Ink, focus), Palette.Danger, err);
                if (!Enabled) iconColor = Palette.Fade(Palette.Ink3, 0.6);
                Icons.Draw(g, glyph, new RectangleF(v.X + Px(13), v.Y + (v.Height - size) / 2, size, size), Palette.Fade(iconColor, opacity), s);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
