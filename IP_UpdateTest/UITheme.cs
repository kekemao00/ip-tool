using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 程序图标和窗口外框（圆角）
    /// </summary>
    public static class UITheme
    {
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// Windows 11（22000+）才支持的窗口圆角、描边颜色属性
        /// </summary>
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWCP_ROUND = 2;

        private static System.Drawing.Icon appIcon;

        /// <summary>
        /// 程序图标（嵌入资源，不放在 resx 中，便于用 dotnet build 构建）
        /// </summary>
        public static System.Drawing.Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    using (var stream = typeof(UITheme).Assembly.GetManifestResourceStream("IP_UpdateTest.app.ico"))
                    {
                        appIcon = stream != null ? new System.Drawing.Icon(stream) : System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    }
                }
                return appIcon;
            }
        }

        /// <summary>
        /// 窗体圆角：Windows 11 使用系统原生圆角（抗锯齿），更早的系统用窗口区域裁剪。
        /// 返回 true 表示使用了系统圆角。
        /// </summary>
        public static bool ApplyWindowCorners(Form form)
        {
            int preference = DWMWCP_ROUND;
            try
            {
                if (DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) == 0)
                {
                    // 系统描边用 line-strong，与界面的 1px 描边一致
                    Color c = Palette.LineStrong;
                    int colorRef = c.R | (c.G << 8) | (c.B << 16);
                    DwmSetWindowAttribute(form.Handle, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
                    return true;
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            ApplyRegionCorners(form);
            return false;
        }

        /// <summary>
        /// 用窗口区域裁剪出圆角（Windows 10 及更早）
        /// </summary>
        public static void ApplyRegionCorners(Form form)
        {
            int radius = form.LogicalToDeviceUnits(8);
            IntPtr hrgn = CreateRoundRectRgn(0, 0, form.Width + 1, form.Height + 1, radius, radius);
            try
            {
                form.Region = Region.FromHrgn(hrgn);
            }
            finally
            {
                // Region.FromHrgn 会复制区域，原句柄需要自己释放
                DeleteObject(hrgn);
            }
        }
    }
}

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 无边框主题窗体：canvas 底、自绘顶栏和通知胶囊、窗口内浮层面板。
    /// 布局按逻辑像素 × 当前 DPI 自己计算（不用 WinForms 自动缩放），入场时用 smooth 弹簧淡入
    /// </summary>
    public class ThemedForm : Form
    {
        private const int CS_DROPSHADOW = 0x00020000;
        private const int WS_MINIMIZEBOX = 0x00020000;

        private readonly SpringValue entrance = new SpringValue(1, Spring.Smooth);
        private bool useRegionCorners;
        private bool entering;

        public ThemedForm()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            BackColor = Palette.Canvas;
            ForeColor = Palette.Ink;
            Font = new Font(Typo.CjkFamilyName, 9F);
            Icon = UITheme.AppIcon;
            KeyPreview = true;

            Island = new Island();
            TitleBar = new TitleBar(Island);
            Controls.Add(TitleBar);
        }

        public Island Island { get; private set; }

        public TitleBar TitleBar { get; private set; }

        /// <summary>
        /// 当前 DPI 缩放比例
        /// </summary>
        public float S
        {
            get { return DeviceDpi / 96f; }
        }

        protected int P(float logical)
        {
            return (int)Math.Round(logical * S);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.Style |= WS_MINIMIZEBOX; // 无边框窗体默认不能通过任务栏按钮最小化
                return cp;
            }
        }

        /// <summary>
        /// 按逻辑像素设置客户区大小
        /// </summary>
        protected void SetLogicalSize(float width, float height)
        {
            ClientSize = new Size(P(width), P(height));
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (TitleBar != null && string.IsNullOrEmpty(TitleBar.Text)) TitleBar.Text = Text;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!DesignMode) useRegionCorners = !UITheme.ApplyWindowCorners(this);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (useRegionCorners) UITheme.ApplyRegionCorners(this);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (TitleBar == null) return;
            TitleBar.SetBounds(0, 0, ClientSize.Width, P(Metrics.TitleBarHeight));
            LayoutContent(S);
        }

        /// <summary>
        /// 子类在这里按逻辑像素 × s 摆放控件
        /// </summary>
        protected virtual void LayoutContent(float s)
        {
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            // 等 WinForms 自己的缩放处理完，再按新 DPI 重新设置原生控件字体和布局
            BeginInvoke((Action)(() =>
            {
                OnScaleChanged();
                PerformLayout();
                Invalidate(true);
            }));
        }

        /// <summary>
        /// DPI 变化后重新设置原生控件的字体
        /// </summary>
        protected virtual void OnScaleChanged()
        {
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PlayEntrance();
        }

        /// <summary>
        /// 入场：整窗用 smooth 弹簧从透明淡入
        /// </summary>
        public void PlayEntrance()
        {
            if (Motion.IsFrozen || entering) return;
            entering = true;
            double now = Motion.Now;
            entrance.Snap(0);
            entrance.Set(1, now);
            Opacity = 0.01;
            FrameClock.Run(() =>
            {
                if (IsDisposed) return false;
                double t = Motion.Now;
                bool running = entrance.IsAnimatingAt(t);
                double v = running ? Motion.Clamp01(entrance.ValueAt(t)) : 1;
                Opacity = Math.Max(0.01, Math.Min(1, v));
                if (!running) entering = false;
                return running;
            });
        }

        /// <summary>
        /// 顶部通知
        /// </summary>
        public void Notify(NoticeKind kind, string text)
        {
            Island.Notify(kind, text, ClientSize.Width / S - 160);
            TitleBar.Invalidate();
        }

        public Task<bool> ConfirmAsync(Control origin, string title, string message, string okText = "确定", bool danger = false)
        {
            return Sheets.ConfirmAsync(this, origin, title, message, okText, danger);
        }

        public Task AlertAsync(Control origin, string title, string message)
        {
            return Sheets.AlertAsync(this, origin, title, message);
        }

        /// <summary>
        /// 是否有浮层面板打开
        /// </summary>
        public bool HasSheet
        {
            get
            {
                foreach (Control c in Controls)
                {
                    var overlay = c as SheetOverlay;
                    if (overlay != null && !overlay.IsClosing) return true;
                }
                return false;
            }
        }
    }
}
