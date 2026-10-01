using System;
using System.Drawing;
using System.Windows.Forms;

namespace IP_UpdateTest
{
    /// <summary>
    /// 主题对话框基类：顶部标题栏、中间内容区、底部按钮栏
    /// </summary>
    public class DialogBase : ThemedForm
    {
        protected readonly Panel TitlePanel = new Panel { Dock = DockStyle.Top, Height = UITheme.TitleBarHeight };
        protected readonly Panel ContentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 4) };
        protected readonly FlowLayoutPanel ButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 10, 16, 10),
            WrapContents = false
        };

        private readonly string title;

        protected DialogBase(string title, int width, int height)
        {
            this.title = title;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = UITheme.LabelFont;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(width, height);
            Text = title;

            // Fill 控件最先添加，Top/Bottom 控件后添加的先停靠
            Controls.Add(ContentPanel);
            Controls.Add(ButtonPanel);
            Controls.Add(TitlePanel);
        }

        /// <summary>
        /// 子类构造完控件后调用：完成缩放并套用主题
        /// </summary>
        protected void FinishLayout()
        {
            ResumeLayout(false);
            PerformLayout();
            UITheme.ApplyTheme(this);
            UITheme.SetupTitleBar(TitlePanel, this, title, false);
        }

        /// <summary>
        /// 在底部按钮栏添加按钮，按从右到左的顺序排列
        /// </summary>
        protected Button AddButton(string text, bool primary, EventHandler onClick, int width = 84)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, 32),
                Margin = new Padding(8, 0, 0, 0)
            };
            if (primary) UITheme.StylePrimaryButton(button);
            else UITheme.StyleSecondaryButton(button);
            if (onClick != null) button.Click += onClick;
            ButtonPanel.Controls.Add(button);
            return button;
        }

        /// <summary>
        /// 创建表单式布局：左列标签，右列输入控件
        /// </summary>
        protected static TableLayoutPanel CreateFormTable(int labelWidth = 84)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoScroll = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return table;
        }

        /// <summary>
        /// 向表单式布局追加一行
        /// </summary>
        protected static void AddRow(TableLayoutPanel table, string caption, Control control, int height = 34)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            if (caption != null)
            {
                var label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
                UITheme.StyleLabel(label);
                table.Controls.Add(label, 0, row);
            }
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(0, 0, 20, 0);
            table.Controls.Add(control, 1, row);
        }
    }

    /// <summary>
    /// 输入一行文字的对话框（替代 VB 的 InputBox，风格与主窗体一致）
    /// </summary>
    public class InputDialog : DialogBase
    {
        private readonly TextBox textBox = new TextBox();

        private InputDialog(string title, string prompt, string defaultValue) : base(title, 380, 170)
        {
            var label = new Label { Text = prompt, Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft };
            UITheme.StyleLabel(label);
            textBox.Dock = DockStyle.Top;
            textBox.Text = defaultValue ?? "";
            UITheme.StyleTextBox(textBox);

            ContentPanel.Controls.Add(textBox);
            ContentPanel.Controls.Add(label);

            AcceptButton = AddButton("确定", true, (s, e) =>
            {
                if (textBox.Text.Trim().Length == 0) return;
                DialogResult = DialogResult.OK;
            });
            CancelButton = AddButton("取消", false, (s, e) => DialogResult = DialogResult.Cancel);
            FinishLayout();
        }

        /// <summary>
        /// 显示对话框；返回去掉首尾空格的输入内容，取消时返回 null
        /// </summary>
        public static string Show(IWin32Window owner, string title, string prompt, string defaultValue = "")
        {
            using (var dialog = new InputDialog(title, prompt, defaultValue))
            {
                dialog.Shown += (s, e) => dialog.textBox.SelectAll();
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.textBox.Text.Trim() : null;
            }
        }
    }
}
