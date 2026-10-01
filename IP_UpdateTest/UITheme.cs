using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IP_UpdateTest
{
    /// <summary>
    /// 统一的 UI 主题配置和辅助方法
    /// </summary>
    public static class UITheme
    {
        // 配色方案
        public static readonly Color Background = Color.FromArgb(245, 247, 250);      // 主背景 #F5F7FA
        public static readonly Color TitleBar = Color.FromArgb(45, 55, 72);           // 标题栏 #2D3748
        public static readonly Color TitleBarHover = Color.FromArgb(74, 85, 104);     // 标题栏按钮悬停 #4A5568
        public static readonly Color Primary = Color.FromArgb(66, 153, 225);          // 主色调 #4299E1
        public static readonly Color PrimaryHover = Color.FromArgb(49, 130, 206);     // 主色调悬停 #3182CE
        public static readonly Color SuccessText = Color.FromArgb(39, 103, 73);       // 成功文字 #276749
        public static readonly Color Danger = Color.FromArgb(245, 101, 101);          // 危险色 #F56565
        public static readonly Color DangerText = Color.FromArgb(197, 48, 48);        // 错误文字 #C53030
        public static readonly Color TextPrimary = Color.FromArgb(45, 55, 72);        // 主文字 #2D3748
        public static readonly Color TextSecondary = Color.FromArgb(113, 128, 150);   // 次文字 #718096
        public static readonly Color Border = Color.FromArgb(226, 232, 240);          // 边框 #E2E8F0
        public static readonly Color InputBackground = Color.White;                    // 输入框背景
        public static readonly Color CardBackground = Color.White;                     // 卡片背景
        public static readonly Color HoverBackground = Color.FromArgb(237, 242, 247); // 悬停背景 #EDF2F7
        public static readonly Color WarningBackground = Color.FromArgb(254, 243, 199); // 提示条背景 #FEF3C7
        public static readonly Color WarningText = Color.FromArgb(146, 64, 14);        // 提示条文字 #92400E

        // 字体
        public static readonly Font TitleFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        public static readonly Font LabelFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        public static readonly Font InputFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        public static readonly Font ButtonFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        public static readonly Font MonoFont = new Font("Consolas", 9F, FontStyle.Regular);

        // 尺寸（96 DPI 下的逻辑像素，使用时用 LogicalToDeviceUnits 换算）
        public const int BorderRadius = 8;
        public const int TitleBarHeight = 40;
        public const int TitleButtonWidth = 44;
        public const int Padding = 16;

        #region Win32 API - 窗体圆角和拖动

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        /// <summary>
        /// Windows 11（22000+）才支持的窗口圆角属性
        /// </summary>
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        #endregion

        private static Icon _appIcon;

        /// <summary>
        /// 程序图标（嵌入资源，不放在 resx 中，便于用 dotnet build 构建）
        /// </summary>
        public static Icon AppIcon
        {
            get
            {
                if (_appIcon == null)
                {
                    using (var stream = typeof(UITheme).Assembly.GetManifestResourceStream("IP_UpdateTest.app.ico"))
                    {
                        _appIcon = stream != null ? new Icon(stream) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    }
                }
                return _appIcon;
            }
        }

        /// <summary>
        /// 应用主题到窗体（背景、字体、图标）
        /// </summary>
        public static void ApplyTheme(Form form)
        {
            form.BackColor = Background;
            form.Font = LabelFont;
            form.Icon = AppIcon;
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
                    return true;
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
            int radius = form.LogicalToDeviceUnits(BorderRadius);
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

        /// <summary>
        /// 按住控件可拖动窗体。交给系统处理，拖动更流畅，跨显示器时 DPI 也能正确切换。
        /// </summary>
        public static void EnableDrag(Control control, Form form)
        {
            control.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(form.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            };
        }

        /// <summary>
        /// 在面板中搭建自定义标题栏：标题、最小化、关闭，按住可拖动
        /// </summary>
        public static void SetupTitleBar(Panel titleBar, Form form, string title, bool showMinimize = true)
        {
            titleBar.BackColor = TitleBar;
            titleBar.Padding = new System.Windows.Forms.Padding(form.LogicalToDeviceUnits(Padding), 0, 0, 0);
            titleBar.Controls.Clear();

            var titleLabel = new Label
            {
                Name = "lblTitle",
                Text = title,
                ForeColor = Color.White,
                Font = TitleFont,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };

            // Fill 控件需最先添加；Right 控件后添加的先停靠，所以关闭按钮最后添加，位于最右侧
            titleBar.Controls.Add(titleLabel);
            if (showMinimize)
            {
                titleBar.Controls.Add(CreateTitleButton(form, "─", 10F, TitleBarHover, () => form.WindowState = FormWindowState.Minimized));
            }
            titleBar.Controls.Add(CreateTitleButton(form, "×", 14F, Danger, form.Close));

            EnableDrag(titleBar, form);
            EnableDrag(titleLabel, form);
        }

        private static Label CreateTitleButton(Form form, string text, float fontSize, Color hoverColor, Action onClick)
        {
            var button = new Label
            {
                Text = text,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", fontSize, FontStyle.Regular),
                AutoSize = false,
                Width = form.LogicalToDeviceUnits(TitleButtonWidth),
                Dock = DockStyle.Right,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            button.MouseEnter += (s, e) => button.BackColor = hoverColor;
            button.MouseLeave += (s, e) => button.BackColor = Color.Transparent;
            button.Click += (s, e) => onClick();
            return button;
        }

        /// <summary>
        /// 样式化文本框
        /// </summary>
        public static void StyleTextBox(TextBox textBox)
        {
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.BackColor = textBox.ReadOnly ? HoverBackground : InputBackground;
            textBox.ForeColor = TextPrimary;
            textBox.Font = InputFont;
        }

        /// <summary>
        /// 设置输入框为空时显示的灰色提示文字（获得焦点时仍显示，输入内容后隐藏）
        /// </summary>
        public static void SetCueBanner(TextBox textBox, string text)
        {
            SendMessage(textBox.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }

        /// <summary>
        /// 样式化只读信息（无边框，可选中复制）
        /// </summary>
        public static void StyleReadOnlyField(TextBox textBox)
        {
            textBox.ReadOnly = true;
            textBox.BorderStyle = BorderStyle.None;
            textBox.BackColor = Background;
            textBox.ForeColor = TextPrimary;
            textBox.Font = InputFont;
            textBox.TabStop = false;
        }

        /// <summary>
        /// 样式化下拉框
        /// </summary>
        public static void StyleComboBox(ComboBox comboBox)
        {
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.BackColor = InputBackground;
            comboBox.ForeColor = TextPrimary;
            comboBox.Font = InputFont;
        }

        /// <summary>
        /// 样式化单选框、复选框
        /// </summary>
        public static void StyleChoice(ButtonBase choice)
        {
            choice.ForeColor = TextPrimary;
            choice.Font = LabelFont;
            choice.Cursor = Cursors.Hand;
        }

        /// <summary>
        /// 样式化主按钮
        /// </summary>
        public static void StylePrimaryButton(Button button)
        {
            StyleFilledButton(button, Primary, PrimaryHover);
        }

        /// <summary>
        /// 样式化次要按钮（白底灰边）
        /// </summary>
        public static void StyleSecondaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = HoverBackground;
            button.BackColor = CardBackground;
            button.ForeColor = TextPrimary;
            button.Font = ButtonFont;
            button.Cursor = Cursors.Hand;
        }

        private static void StyleFilledButton(Button button, Color color, Color hoverColor)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = hoverColor;
            button.FlatAppearance.MouseDownBackColor = hoverColor;
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.Font = ButtonFont;
            button.Cursor = Cursors.Hand;
        }

        /// <summary>
        /// 样式化标签
        /// </summary>
        public static void StyleLabel(Label label)
        {
            label.ForeColor = TextSecondary;
            label.Font = LabelFont;
        }

        /// <summary>
        /// 显示现代化消息框
        /// </summary>
        public static DialogResult ShowMessage(string message, string title = "提示", MessageBoxIcon icon = MessageBoxIcon.Information)
        {
            return MessageBox.Show(message, title, MessageBoxButtons.OK, icon);
        }

        /// <summary>
        /// 显示确认对话框
        /// </summary>
        public static bool Confirm(string message, string title = "确认")
        {
            return MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
    }

    /// <summary>
    /// 无边框主题窗体：系统阴影、可通过任务栏最小化、圆角
    /// </summary>
    public class ThemedForm : Form
    {
        private const int CS_DROPSHADOW = 0x00020000;
        private const int WS_MINIMIZEBOX = 0x00020000;

        /// <summary>
        /// 系统不支持原生圆角时用窗口区域裁剪，尺寸变化后需重新裁剪
        /// </summary>
        private bool useRegionCorners;

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
    }
}
