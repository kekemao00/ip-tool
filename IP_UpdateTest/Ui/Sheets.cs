using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 浮层面板的内容。背景透明，透出下面正在变形的面板形状；自己画标题，子控件按内容不透明度绘制
    /// </summary>
    public abstract class SheetContent : ContainerControl, IFadeHost
    {
        private readonly SpringButton closeButton;
        private readonly List<Control> natives = new List<Control>();
        private double lastOpacity = -1;

        protected SheetContent()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            closeButton = new SpringButton { Kind = ButtonKind.Ghost, Glyph = Glyph.Close, TabStop = false };
            closeButton.Click += (s, e) => Cancel();
            InkToolTip.Shared.SetToolTip(closeButton, "关闭（Esc）");
            Controls.Add(closeButton);
        }

        internal SheetOverlay Overlay { get; set; }

        protected void HideCloseButton()
        {
            closeButton.Visible = false;
        }

        public string Title { get; set; }

        /// <summary>
        /// Enter 触发的按钮
        /// </summary>
        public SpringButton Accept { get; set; }

        /// <summary>
        /// 取消时的结果
        /// </summary>
        public object CancelResult { get; set; }

        public double ContentOpacity
        {
            get { return Overlay == null ? 1 : Overlay.ContentOpacityAt(Motion.Now); }
        }

        protected float S
        {
            get { return DeviceDpi / 96f; }
        }

        protected int P(float logical)
        {
            return (int)Math.Round(logical * S);
        }

        /// <summary>
        /// 面板大小（逻辑像素）
        /// </summary>
        public abstract Size LogicalSize { get; }

        /// <summary>
        /// 打开后获得焦点的控件
        /// </summary>
        public virtual Control InitialFocus
        {
            get { return Accept; }
        }

        /// <summary>
        /// 原生控件（输入框等）不能淡入，内容显现过半后再显示
        /// </summary>
        protected void RegisterNative(Control control)
        {
            natives.Add(control);
            control.Visible = false;
        }

        internal void SyncOpacity(double opacity)
        {
            bool show = opacity >= 0.6;
            bool wasShown = lastOpacity >= 0.6;
            lastOpacity = opacity;
            if (show == wasShown) return;
            foreach (Control c in natives) c.Visible = show;
            if (show)
            {
                Control focus = InitialFocus;
                var field = focus as FieldBox;
                if (field != null) field.FocusInput();
                else if (focus != null && focus.CanFocus) focus.Focus();
            }
        }

        public void Close(object result)
        {
            if (Overlay != null) Overlay.Close(result);
        }

        public void Cancel()
        {
            Close(CancelResult);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float s = S;
            int size = P(32);
            closeButton.SetBounds(Width - P(14) - size, P(14), size, size);
            LayoutContent(s);
        }

        protected abstract void LayoutContent(float s);

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Cancel();
                return true;
            }
            if (keyData == Keys.Enter && Accept != null && !(ActiveControl is SpringButton && ActiveControl != Accept))
            {
                if (Accept.Enabled) Accept.PerformClick();
                return true;
            }
            if ((keyData & ~Keys.Shift) == Keys.Tab)
            {
                // 焦点只在面板内循环
                SelectNextControl(ActiveControl, (keyData & Keys.Shift) == 0, true, true, true);
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Shapes.Prepare(g);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            double opacity = ContentOpacity;
            if (opacity > 0.004) PaintContent(g, opacity, 4 * (1 - opacity));
            base.OnPaint(e);
        }

        /// <summary>
        /// 画标题等文字；blur 为当前模糊半径
        /// </summary>
        protected virtual void PaintContent(Graphics g, double opacity, double blur)
        {
            if (string.IsNullOrEmpty(Title)) return;
            float s = S;
            Typo.DrawLine(g, Title, TextStyle.Title, Palette.Ink, P(24), Typo.CenterBaseline(new RectangleF(0, P(14), Width, P(32)), TextStyle.Title, s), s, opacity, blur);
        }

        protected static SpringButton FooterButton(string text, ButtonKind kind)
        {
            return new SpringButton { Text = text, Kind = kind };
        }

        /// <summary>
        /// 底部按钮右对齐（从右到左）
        /// </summary>
        protected void LayoutFooter(float s, params SpringButton[] rightToLeft)
        {
            int h = P(Metrics.PanelButtonHeight);
            int x = Width - P(24);
            int y = Height - P(20) - h;
            foreach (SpringButton b in rightToLeft)
            {
                if (b == null || !b.Visible) continue;
                int w = Math.Max(P(80), b.PreferredWidth);
                x -= w;
                b.SetBounds(x, y, w, h);
                x -= P(8);
            }
        }
    }

    /// <summary>
    /// 浮层：截下窗口当前画面，盖上遮罩，面板由触发它的控件变形展开；关闭时原路收回
    /// </summary>
    public sealed class SheetOverlay : Control
    {
        private readonly ThemedForm form;
        private readonly SheetContent content;
        private readonly Bitmap snapshot;
        private readonly RectangleF origin;
        private readonly Color originColor;
        private readonly float originRadius;
        private readonly SpringValue progress = new SpringValue(0, Spring.Smooth);
        private readonly SpringValue scrim = new SpringValue(0, Spring.Snappy);
        private readonly TaskCompletionSource<object> completion = new TaskCompletionSource<object>();
        private readonly Control previousFocus;
        private RectangleF target;
        private double closedAt = double.NaN;
        private bool tickerRunning;

        private SheetOverlay(ThemedForm form, SheetContent content, Rectangle originRect, Color originColor, float originRadius)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            this.form = form;
            this.content = content;
            this.originColor = originColor;
            this.originRadius = originRadius;
            previousFocus = form.ActiveControl;
            content.Overlay = this;

            snapshot = WindowCapture.Client(form);
            // 原控件在变形期间不画（同一时刻只有一个形状）：用它旁边的底色盖掉
            if (!originRect.IsEmpty)
            {
                using (Graphics g = Graphics.FromImage(snapshot))
                {
                    Color behind = SampleBehind(snapshot, originRect);
                    using (var brush = new SolidBrush(behind))
                        g.FillRectangle(brush, Rectangle.Inflate(originRect, 1, 1));
                }
            }
            origin = originRect;
            Bounds = new Rectangle(Point.Empty, form.ClientSize);
            form.Island.Changed += OnIslandChanged;
        }

        private void OnIslandChanged()
        {
            Invalidate();
            StartTicker();
        }

        public Task<object> Task
        {
            get { return completion.Task; }
        }

        public bool IsClosing
        {
            get { return !double.IsNaN(closedAt); }
        }

        /// <summary>
        /// 打开浮层面板。origin 为触发它的控件（可为 null，从面板中心放大）
        /// </summary>
        public static Task<object> Show(ThemedForm form, SheetContent content, Control origin)
        {
            // 前一个面板正在收起时直接结束它，避免被截进新画面
            foreach (Control c in form.Controls)
            {
                var overlay = c as SheetOverlay;
                if (overlay != null && overlay.IsClosing)
                {
                    overlay.Finish();
                    break;
                }
            }

            Rectangle rect = Rectangle.Empty;
            Color color = Palette.Surface;
            float radius = Metrics.ButtonRadius * form.DeviceDpi / 96f;
            if (origin != null && origin.Visible && origin.IsHandleCreated)
            {
                rect = form.RectangleToClient(origin.RectangleToScreen(origin.ClientRectangle));
                var button = origin as SpringButton;
                if (button != null)
                {
                    color = button.Kind == ButtonKind.Primary ? Palette.Ink
                        : button.Kind == ButtonKind.Accent ? Palette.Accent
                        : button.Kind == ButtonKind.Danger ? Palette.Danger
                        : button.Kind == ButtonKind.Ghost ? Palette.Surface3
                        : Palette.Surface;
                    if (button.Height >= 40 * form.DeviceDpi / 96f && button.Kind != ButtonKind.Secondary && button.Kind != ButtonKind.Ghost)
                        radius = Metrics.PrimaryRadius * form.DeviceDpi / 96f;
                }
                else if (origin is FieldBox || origin is SelectBox)
                {
                    int m = (int)(Metrics.FieldMargin * form.DeviceDpi / 96f);
                    rect = Rectangle.Inflate(rect, -m, -m);
                    radius = Metrics.InputRadius * form.DeviceDpi / 96f;
                }
            }
            return Show(form, content, rect, color, radius);
        }

        /// <summary>
        /// 从窗口中指定的矩形（设备像素）展开
        /// </summary>
        public static Task<object> Show(ThemedForm form, SheetContent content, Rectangle originRect, Color originColor, float originRadius)
        {
            var overlay = new SheetOverlay(form, content, originRect, originColor, originRadius);
            overlay.Open();
            return overlay.Task;
        }

        private void Open()
        {
            float s = form.DeviceDpi / 96f;
            Size logical = content.LogicalSize;
            float w = Math.Min(logical.Width * s, form.ClientSize.Width - 32 * s);
            float h = Math.Min(logical.Height * s, form.ClientSize.Height - 32 * s);
            target = new RectangleF((form.ClientSize.Width - w) / 2, (form.ClientSize.Height - h) / 2, w, h);
            target = Rectangle.Round(target);

            SuspendLayout();
            content.Bounds = Rectangle.Round(target);
            Controls.Add(content);
            ResumeLayout(true);
            form.Controls.Add(this);
            BringToFront();

            double now = Motion.Now;
            progress.Set(1, now);
            scrim.Set(1, now);
            content.SyncOpacity(0);
            if (!(content.InitialFocus is FieldBox) && content.InitialFocus != null) content.InitialFocus.Focus();
            else content.Focus();
            Invalidate(true);
            StartTicker();
        }

        /// <summary>
        /// 收起并给出结果（结果立即交给调用方，动画继续播放）
        /// </summary>
        public void Close(object result)
        {
            if (IsClosing) return;
            double now = Motion.Now;
            closedAt = now;
            // 内容先于形状消失
            progress.Set(0, now + 0.06);
            scrim.Set(0, now + 0.06);
            content.SyncOpacity(0);
            Invalidate(true);
            StartTicker();
            completion.TrySetResult(result);
        }

        internal double ContentOpacityAt(double now)
        {
            double p = progress.ValueAt(now);
            double opening = Motion.Clamp01((p - 0.55) / 0.35);
            if (IsClosing) opening = Math.Min(opening, 1 - Motion.Progress(now, closedAt, 0.1));
            return opening;
        }

        internal void Finish()
        {
            if (IsDisposed) return;
            form.Island.Changed -= OnIslandChanged;
            bool hadFocus = ContainsFocus;
            Parent = null;
            content.Dispose();
            snapshot.Dispose();
            Dispose();
            if (hadFocus && previousFocus != null && !previousFocus.IsDisposed && previousFocus.CanFocus) previousFocus.Focus();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // 点遮罩关闭
            if (!target.Contains(e.Location) && !IsClosing) content.Cancel();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Shapes.Prepare(g);
            double now = Motion.Now;
            float s = form.DeviceDpi / 96f;
            g.DrawImageUnscaled(snapshot, 0, 0);

            double sc = Motion.Clamp01(scrim.ValueAt(now));
            using (var brush = new SolidBrush(Palette.Fade(Palette.Scrim, sc)))
                g.FillRectangle(brush, ClientRectangle);

            double p = progress.ValueAt(now);
            RectangleF from = origin.IsEmpty ? ScaleAround(target, 0.92f) : (RectangleF)origin;
            RectangleF shape = Shapes.Lerp(from, target, p);
            float radius = (float)Motion.Lerp(originRadius, Metrics.PanelRadius * s, Motion.Clamp01(p));
            double colorT = Motion.Clamp01(p * 1.6);
            double alpha = origin.IsEmpty ? Motion.Clamp01(p * 2) : 1;
            if (shape.Width > 1 && shape.Height > 1)
            {
                Shapes.Fill(g, shape, radius, Palette.Fade(Palette.Mix(originColor, Palette.Surface, colorT), alpha));
                Shapes.Stroke(g, shape, radius, Palette.Fade(Palette.Line, Motion.Clamp01(p * 2) * alpha), s);
            }

            form.Island.Paint(g, Width, s, now);

            content.SyncOpacity(ContentOpacityAt(now));
        }

        /// <summary>
        /// 动画期间每帧推进：同步内容不透明度、重绘自己和内容，收起后移除
        /// </summary>
        private void StartTicker()
        {
            if (tickerRunning || IsDisposed) return;
            tickerRunning = true;
            FrameClock.Run(() =>
            {
                if (IsDisposed)
                {
                    tickerRunning = false;
                    return false;
                }
                double now = Motion.Now;
                content.SyncOpacity(ContentOpacityAt(now));
                if (IsClosing && !progress.IsAnimatingAt(now) && now - closedAt > 0.1)
                {
                    tickerRunning = false;
                    Finish();
                    return false;
                }
                bool animating = progress.IsAnimatingAt(now) || scrim.IsAnimatingAt(now) || form.Island.IsAnimatingAt(now) || IsClosing;
                // 子控件的不透明度跟着变化，一起重绘
                Invalidate(true);
                if (!animating) tickerRunning = false;
                return animating;
            });
        }

        private static RectangleF ScaleAround(RectangleF r, float factor)
        {
            float w = r.Width * factor, h = r.Height * factor;
            return new RectangleF(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
        }

        private static Color SampleBehind(Bitmap bitmap, Rectangle rect)
        {
            int x = Math.Max(0, rect.Left - 3), y = Math.Min(bitmap.Height - 1, Math.Max(0, rect.Top + rect.Height / 2));
            if (x >= bitmap.Width) return Palette.Canvas;
            return bitmap.GetPixel(x, y);
        }
    }

    /// <summary>
    /// 截取窗口画面：优先用 PrintWindow（按真实层次），失败时退回 DrawToBitmap
    /// </summary>
    public static class WindowCapture
    {
        private const uint PW_CLIENTONLY = 0x1;
        private const uint PW_RENDERFULLCONTENT = 0x2;

        public static Bitmap Client(Form form)
        {
            Size size = form.ClientSize;
            var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppArgb);
            bool ok = false;
            if (form.IsHandleCreated && form.Visible)
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        ok = PrintWindow(form.Handle, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }
            }
            if (!ok)
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            }
            return bitmap;
        }

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    }

    /// <summary>
    /// 消息 / 确认面板
    /// </summary>
    public sealed class MessageSheet : SheetContent
    {
        private const float Width0 = 420;
        private readonly string message;
        private readonly List<SpringButton> buttons = new List<SpringButton>();

        /// <param name="choices">按从右到左排列：第一个是主操作</param>
        public MessageSheet(string title, string message, params SheetChoice[] choices)
        {
            Title = title;
            this.message = message ?? "";
            foreach (SheetChoice choice in choices)
            {
                SheetChoice c = choice;
                SpringButton button = FooterButton(c.Text, c.Kind);
                button.Click += (s, e) => Close(c.Result);
                buttons.Add(button);
                Controls.Add(button);
            }
            if (buttons.Count > 0) Accept = buttons[0];
        }

        private List<string> Lines(float s)
        {
            return Typo.Wrap(message, TextStyle.Body, (Width0 - 48) * s, s);
        }

        public override Size LogicalSize
        {
            get
            {
                float s = S;
                int lines = Lines(s).Count;
                float height = 60 + lines * Typo.LineHeight(TextStyle.Body, s) / s + 28 + Metrics.PanelButtonHeight + 20;
                return new Size((int)Width0, (int)Math.Ceiling(height));
            }
        }

        protected override void LayoutContent(float s)
        {
            LayoutFooter(s, buttons.ToArray());
        }

        protected override void PaintContent(Graphics g, double opacity, double blur)
        {
            base.PaintContent(g, opacity, blur);
            float s = S;
            float y = P(60);
            float lineHeight = Typo.LineHeight(TextStyle.Body, s);
            foreach (string line in Lines(s))
            {
                Typo.DrawLine(g, line, TextStyle.Body, Palette.Ink2, P(24), Typo.CenterBaseline(new RectangleF(0, y, Width, lineHeight), TextStyle.Body, s), s, opacity, blur);
                y += lineHeight;
            }
        }
    }

    public sealed class SheetChoice
    {
        public SheetChoice(string text, ButtonKind kind, object result)
        {
            Text = text;
            Kind = kind;
            Result = result;
        }

        public string Text { get; private set; }
        public ButtonKind Kind { get; private set; }
        public object Result { get; private set; }
    }

    /// <summary>
    /// 输入一行文字的面板
    /// </summary>
    public sealed class PromptSheet : SheetContent
    {
        private readonly string label;
        private readonly FieldBox field = new FieldBox();
        private readonly SpringButton ok;
        private readonly SpringButton cancel;

        public PromptSheet(string title, string label, string value, Glyph glyph)
        {
            Title = title;
            this.label = label;
            field.Glyph = glyph;
            field.Text = value ?? "";
            ok = FooterButton("确定", ButtonKind.Primary);
            cancel = FooterButton("取消", ButtonKind.Secondary);
            ok.Click += (s, e) =>
            {
                string text = field.Text.Trim();
                if (text.Length == 0)
                {
                    field.SetError("请填写" + label.TrimEnd('：', ':'));
                    field.FocusInput();
                    return;
                }
                Close(text);
            };
            cancel.Click += (s, e) => Cancel();
            field.TextChanged += (s, e) => field.SetError(null);
            Controls.Add(field);
            Controls.Add(ok);
            Controls.Add(cancel);
            RegisterNative(field.Box);
            Accept = ok;
        }

        public override Control InitialFocus
        {
            get { return field; }
        }

        public override Size LogicalSize
        {
            get { return new Size(400, 60 + 20 + 6 + 40 + 28 + 36 + 20); }
        }

        protected override void LayoutContent(float s)
        {
            field.Place(new Rectangle(P(24), P(86), Width - P(48), P(Metrics.InputHeight)));
            LayoutFooter(s, ok, cancel);
        }

        protected override void PaintContent(Graphics g, double opacity, double blur)
        {
            base.PaintContent(g, opacity, blur);
            float s = S;
            Typo.DrawLine(g, label, TextStyle.Small, Palette.Ink2, P(24), Typo.CenterBaseline(new RectangleF(0, P(58), Width, P(20)), TextStyle.Small, s), s, opacity, blur);
        }
    }

    /// <summary>
    /// 常用面板：确认、提示、选择、输入
    /// </summary>
    public static class Sheets
    {
        public static async Task<bool> ConfirmAsync(ThemedForm form, Control origin, string title, string message, string okText = "确定", bool danger = false)
        {
            var sheet = new MessageSheet(title, message,
                new SheetChoice(okText, danger ? ButtonKind.Danger : ButtonKind.Primary, true),
                new SheetChoice("取消", ButtonKind.Secondary, false)) { CancelResult = false };
            object result = await SheetOverlay.Show(form, sheet, origin);
            return result is bool && (bool)result;
        }

        public static async Task AlertAsync(ThemedForm form, Control origin, string title, string message)
        {
            var sheet = new MessageSheet(title, message, new SheetChoice("知道了", ButtonKind.Primary, null));
            await SheetOverlay.Show(form, sheet, origin);
        }

        /// <summary>
        /// 多个选项；返回所选项的下标，取消时返回 -1。choices 从右到左排列，第一个是主操作
        /// </summary>
        public static async Task<int> ChooseAsync(ThemedForm form, Control origin, string title, string message, params string[] choices)
        {
            var list = new List<SheetChoice>();
            for (int i = 0; i < choices.Length; i++)
                list.Add(new SheetChoice(choices[i], i == 0 ? ButtonKind.Primary : ButtonKind.Secondary, i));
            list.Add(new SheetChoice("取消", ButtonKind.Secondary, -1));
            var sheet = new MessageSheet(title, message, list.ToArray()) { CancelResult = -1 };
            object result = await SheetOverlay.Show(form, sheet, origin);
            return result is int ? (int)result : -1;
        }

        public static async Task<string> PromptAsync(ThemedForm form, Control origin, string title, string label, string value, Glyph glyph = Glyph.Tag)
        {
            object result = await SheetOverlay.Show(form, new PromptSheet(title, label, value, glyph), origin);
            return result as string;
        }
    }
}
