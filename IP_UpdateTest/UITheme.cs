using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        public static readonly Color Primary = Color.FromArgb(66, 153, 225);          // 主色调 #4299E1
        public static readonly Color Success = Color.FromArgb(72, 187, 120);          // 成功色 #48BB78
        public static readonly Color Danger = Color.FromArgb(245, 101, 101);          // 危险色 #F56565
        public static readonly Color Warning = Color.FromArgb(237, 137, 54);          // 警告色 #ED8936
        public static readonly Color TextPrimary = Color.FromArgb(45, 55, 72);        // 主文字 #2D3748
        public static readonly Color TextSecondary = Color.FromArgb(113, 128, 150);   // 次文字 #718096
        public static readonly Color Border = Color.FromArgb(226, 232, 240);          // 边框 #E2E8F0
        public static readonly Color InputBackground = Color.White;                    // 输入框背景
        public static readonly Color CardBackground = Color.White;                     // 卡片背景
        public static readonly Color HoverBackground = Color.FromArgb(237, 242, 247); // 悬停背景 #EDF2F7

        // 字体
        public static readonly Font TitleFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        public static readonly Font LabelFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        public static readonly Font InputFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        public static readonly Font ButtonFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);

        // 尺寸
        public const int BorderRadius = 8;
        public const int TitleBarHeight = 40;
        public const int ControlHeight = 32;
        public const int Padding = 16;
        public const int Spacing = 12;

        #region Win32 API - 窗体阴影和圆角

        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
            int nWidthEllipse, int nHeightEllipse);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int leftWidth;
            public int rightWidth;
            public int topHeight;
            public int bottomHeight;
        }

        private const int DWMWA_NCRENDERING_POLICY = 2;
        private const int DWMNCRP_ENABLED = 2;
        private const int CS_DROPSHADOW = 0x00020000;

        #endregion

        /// <summary>
        /// 为窗体应用圆角
        /// </summary>
        public static void ApplyRoundedCorners(Form form, int radius = BorderRadius)
        {
            form.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, form.Width, form.Height, radius, radius));
        }

        /// <summary>
        /// 为窗体启用阴影效果
        /// </summary>
        public static void EnableFormShadow(Form form)
        {
            try
            {
                var margins = new MARGINS { leftWidth = 1, rightWidth = 1, topHeight = 1, bottomHeight = 1 };
                DwmExtendFrameIntoClientArea(form.Handle, ref margins);
            }
            catch { }
        }

        /// <summary>
        /// 应用主题到窗体
        /// </summary>
        public static void ApplyTheme(Form form)
        {
            form.BackColor = Background;
            form.Font = LabelFont;
            ApplyRoundedCorners(form);
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
        /// 样式化主按钮
        /// </summary>
        public static void StylePrimaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Primary;
            button.ForeColor = Color.White;
            button.Font = ButtonFont;
            button.Cursor = Cursors.Hand;

            button.MouseEnter += (s, e) => button.BackColor = Color.FromArgb(49, 130, 206);
            button.MouseLeave += (s, e) => button.BackColor = Primary;
        }

        /// <summary>
        /// 样式化成功按钮
        /// </summary>
        public static void StyleSuccessButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Success;
            button.ForeColor = Color.White;
            button.Font = ButtonFont;
            button.Cursor = Cursors.Hand;

            button.MouseEnter += (s, e) => button.BackColor = Color.FromArgb(56, 161, 105);
            button.MouseLeave += (s, e) => button.BackColor = Success;
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
        /// 创建自定义标题栏面板
        /// </summary>
        public static Panel CreateTitleBar(Form form, string title, bool showCloseButton = true)
        {
            var titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = TitleBarHeight,
                BackColor = TitleBar,
                Padding = new Padding(Padding, 0, Padding, 0)
            };

            var titleLabel = new Label
            {
                Text = title,
                ForeColor = Color.White,
                Font = TitleFont,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };

            titleBar.Controls.Add(titleLabel);

            if (showCloseButton)
            {
                var closeButton = new Label
                {
                    Text = "×",
                    ForeColor = Color.White,
                    Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Regular),
                    AutoSize = false,
                    Size = new Size(40, TitleBarHeight),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Right,
                    Cursor = Cursors.Hand
                };

                closeButton.MouseEnter += (s, e) => closeButton.BackColor = Danger;
                closeButton.MouseLeave += (s, e) => closeButton.BackColor = TitleBar;
                closeButton.Click += (s, e) => form.Close();

                titleBar.Controls.Add(closeButton);
            }

            // 拖动支持
            Point offset = Point.Empty;
            titleBar.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    offset = new Point(e.X, e.Y);
            };
            titleBar.MouseMove += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    form.Location = new Point(form.Left + e.X - offset.X, form.Top + e.Y - offset.Y);
            };
            titleLabel.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    offset = new Point(e.X, e.Y);
            };
            titleLabel.MouseMove += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    form.Location = new Point(form.Left + e.X - offset.X, form.Top + e.Y - offset.Y);
            };

            return titleBar;
        }

        /// <summary>
        /// 创建工具栏按钮
        /// </summary>
        public static Button CreateToolButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = TextPrimary,
                Font = LabelFont,
                AutoSize = true,
                Padding = new Padding(8, 4, 8, 4),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = HoverBackground;
            button.Click += onClick;
            return button;
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
}
