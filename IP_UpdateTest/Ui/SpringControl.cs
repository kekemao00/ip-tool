using System;
using System.Drawing;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 承载内容淡入的容器（浮层面板）：子控件按它的不透明度绘制
    /// </summary>
    public interface IFadeHost
    {
        double ContentOpacity { get; }
    }

    /// <summary>
    /// 自绘控件基类：双缓冲、透明背景、DPI 缩放，悬停 / 按下 / 焦点各是一个 snappy 弹簧
    /// </summary>
    public abstract class SpringControl : Control
    {
        protected readonly SpringValue HoverT = new SpringValue(0, Spring.Snappy);
        protected readonly SpringValue PressT = new SpringValue(0, Spring.Snappy);
        protected readonly SpringValue FocusT = new SpringValue(0, Spring.Snappy);

        protected SpringControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Palette.Ink;
        }

        /// <summary>
        /// 当前 DPI 缩放比例
        /// </summary>
        public float S
        {
            get { return DeviceDpi / 96f; }
        }

        protected float Px(float logical)
        {
            return logical * S;
        }

        /// <summary>
        /// 上层浮层面板的内容不透明度
        /// </summary>
        protected double HostOpacity
        {
            get
            {
                for (Control c = Parent; c != null; c = c.Parent)
                {
                    var host = c as IFadeHost;
                    if (host != null) return host.ContentOpacity;
                }
                return 1;
            }
        }

        /// <summary>
        /// 只给键盘焦点画焦点环
        /// </summary>
        protected bool KeyboardFocused
        {
            get { return Focused && ShowFocusCues; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Shapes.Prepare(g);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            double now = Motion.Now;
            PaintContent(g, now);
            if (IsAnimating(now)) FrameClock.Request(this);
        }

        protected abstract void PaintContent(Graphics g, double now);

        protected virtual bool IsAnimating(double now)
        {
            return HoverT.IsAnimatingAt(now) || PressT.IsAnimatingAt(now) || FocusT.IsAnimatingAt(now);
        }

        /// <summary>
        /// 状态改变后请求重绘，绘制时自然接上动画
        /// </summary>
        protected void Animate()
        {
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (Enabled) HoverT.Set(1);
            Animate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            HoverT.Set(0);
            Animate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
            {
                PressT.Set(1);
                Animate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            PressT.Set(0);
            Animate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            FocusT.Set(1);
            Animate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            FocusT.Set(0);
            PressT.Set(0);
            Animate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!Enabled)
            {
                HoverT.Set(0);
                PressT.Set(0);
            }
            Animate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Animate();
        }
    }
}
