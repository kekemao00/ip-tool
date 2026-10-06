using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 右键菜单 / 下拉：白底、1px line-strong 描边、上下 4 内边距；菜单项内边距 7/12/7/16，悬停 surface-3；
    /// 每项带 16px 线性图标，危险项图标用 danger
    /// </summary>
    public static class Menus
    {
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWCP_ROUNDSMALL = 3;

        /// <summary>
        /// 新建一个套好样式的菜单
        /// </summary>
        public static ContextMenuStrip Create()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new InkMenuRenderer(),
                ShowImageMargin = true,
                ShowCheckMargin = false,
                DropShadowEnabled = true
            };
            menu.Opening += (s, e) => Style(menu);
            menu.HandleCreated += (s, e) => RoundCorners(menu);
            return menu;
        }

        /// <summary>
        /// 按当前 DPI 设置内边距和字体（子菜单同样处理）
        /// </summary>
        private static void Style(ToolStripDropDown menu)
        {
            float s = menu.DeviceDpi / 96f;
            menu.Padding = new Padding(0, (int)(4 * s), 0, (int)(4 * s));
            menu.ImageScalingSize = new Size((int)(16 * s), (int)(16 * s));
            menu.Font = new Font(Typo.CjkFamilyName, 9.75f);
            foreach (ToolStripItem item in menu.Items)
            {
                item.Padding = new Padding((int)(4 * s), (int)(7 * s), (int)(12 * s), (int)(7 * s));
                item.Margin = Padding.Empty;
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems)
                {
                    var dropDown = menuItem.DropDown as ToolStripDropDownMenu;
                    if (dropDown != null && !(dropDown.Renderer is InkMenuRenderer))
                    {
                        dropDown.Renderer = new InkMenuRenderer();
                        dropDown.ShowCheckMargin = false;
                        dropDown.Opening += (o, e) => Style(dropDown);
                        dropDown.HandleCreated += (o, e) => RoundCorners(dropDown);
                    }
                }
            }
        }

        private static void RoundCorners(ToolStripDropDown menu)
        {
            try
            {
                int preference = DWMWCP_ROUNDSMALL;
                bool rounded = DwmSetWindowAttribute(menu.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) == 0;
                if (rounded)
                {
                    Color c = Palette.LineStrong;
                    int colorRef = c.R | (c.G << 8) | (c.B << 16);
                    DwmSetWindowAttribute(menu.Handle, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
                    var renderer = menu.Renderer as InkMenuRenderer;
                    if (renderer != null) renderer.SystemBorder = true;
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        public static ToolStripMenuItem Item(string text, Glyph glyph, EventHandler onClick, bool danger = false)
        {
            var item = new ToolStripMenuItem(text) { Tag = new ItemStyle(glyph, danger), Image = InkMenuRenderer.Placeholder };
            if (onClick != null) item.Click += onClick;
            return item;
        }

        /// <summary>
        /// 菜单项右侧的辅助说明（如 DNS 地址），用 ink-3
        /// </summary>
        public static ToolStripMenuItem Item(string text, string detail, Glyph glyph, EventHandler onClick)
        {
            ToolStripMenuItem item = Item(text, glyph, onClick);
            item.ShortcutKeyDisplayString = detail;
            item.ShowShortcutKeys = true;
            return item;
        }

        public static void Clear(ToolStripDropDown menu)
        {
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        internal sealed class ItemStyle
        {
            public ItemStyle(Glyph glyph, bool danger)
            {
                Glyph = glyph;
                Danger = danger;
            }

            public Glyph Glyph { get; private set; }
            public bool Danger { get; private set; }
        }
    }

    public sealed class InkMenuRenderer : ToolStripRenderer
    {
        /// <summary>
        /// Windows 11 上由系统画圆角和描边
        /// </summary>
        public bool SystemBorder { get; set; }

        private static float ScaleOf(ToolStrip strip)
        {
            return strip == null ? 1 : strip.DeviceDpi / 96f;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(Palette.Surface))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (SystemBorder) return;
            using (var pen = new Pen(Palette.LineStrong))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            float s = ScaleOf(e.Item != null ? e.Item.Owner : null);
            Shapes.Prepare(e.Graphics);
            var r = new RectangleF(4 * s, 0, e.Item.Width - 8 * s, e.Item.Height);
            Shapes.Fill(e.Graphics, r, 6 * s, Palette.Surface3);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            float s = ScaleOf(e.Item != null ? e.Item.Owner : null);
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Palette.Line, Math.Max(1, s)))
                e.Graphics.DrawLine(pen, 12 * s, y, e.Item.Width - 12 * s, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            float s = ScaleOf(e.Item != null ? e.Item.Owner : null);
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var menuItem = e.Item as ToolStripMenuItem;
            bool isDetail = menuItem != null && !string.IsNullOrEmpty(menuItem.ShortcutKeyDisplayString) && e.Text == menuItem.ShortcutKeyDisplayString;
            var style = e.Item.Tag as Menus.ItemStyle;
            Color color = !e.Item.Enabled ? Palette.Ink3 : isDetail ? Palette.Ink3 : style != null && style.Danger ? Palette.Danger : Palette.Ink;
            TextStyle textStyle = isDetail ? TextStyle.Small : TextStyle.Body;
            Typo.Draw(g, e.Text, textStyle, color, e.TextRectangle, s, isDetail ? StringAlignment.Far : StringAlignment.Near);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            DrawGlyph(e.Graphics, e.Item, e.ImageRectangle, e.ToolStrip);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            float s = ScaleOf(e.Item != null ? e.Item.Owner : null);
            Shapes.Prepare(e.Graphics);
            Icons.Draw(e.Graphics, Glyph.Check, e.ImageRectangle, Palette.Accent, s);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            float s = ScaleOf(e.Item != null ? e.Item.Owner : null);
            Shapes.Prepare(e.Graphics);
            float size = 14 * s;
            var r = new RectangleF(e.ArrowRectangle.X + (e.ArrowRectangle.Width - size) / 2, e.ArrowRectangle.Y + (e.ArrowRectangle.Height - size) / 2, size, size);
            Icons.Draw(e.Graphics, Glyph.ChevronRight, r, Palette.Ink3, s);
        }

        /// <summary>
        /// 图标不用位图，直接画线性图标；勾选的项画对勾
        /// </summary>
        private static void DrawGlyph(Graphics g, ToolStripItem item, Rectangle rect, ToolStrip strip)
        {
            float s = ScaleOf(strip);
            Shapes.Prepare(g);
            var style = item.Tag as Menus.ItemStyle;
            var menuItem = item as ToolStripMenuItem;
            float size = 16 * s;
            var box = new RectangleF(rect.X + (rect.Width - size) / 2, rect.Y + (rect.Height - size) / 2, size, size);
            if (menuItem != null && menuItem.Checked)
            {
                Icons.Draw(g, Glyph.Check, box, Palette.Accent, s);
                return;
            }
            if (style == null || style.Glyph == Glyph.None) return;
            Color color = !item.Enabled ? Palette.Ink3 : style.Danger ? Palette.Danger : Palette.Ink2;
            Icons.Draw(g, style.Glyph, box, color, s);
        }

        protected override void InitializeItem(ToolStripItem item)
        {
            base.InitializeItem(item);
            // 给每项占位图，让菜单留出图标列；实际图标在 OnRenderItemImage 里画
            var menuItem = item as ToolStripMenuItem;
            if (menuItem != null && menuItem.Image == null) menuItem.Image = Placeholder;
        }

        private static Bitmap placeholder;

        internal static Bitmap Placeholder
        {
            get { return placeholder ?? (placeholder = new Bitmap(16, 16)); }
        }
    }
}
