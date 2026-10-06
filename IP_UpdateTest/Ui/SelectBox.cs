using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 下拉选择：外观同输入框（白底、1px line、圆角 10），点开是样式统一的菜单。换选项时文字在模糊里交叉淡化
    /// </summary>
    public class SelectBox : SpringControl
    {
        private readonly List<object> items = new List<object>();
        private readonly Crossfade<string> fade = new Crossfade<string>("");
        private readonly SpringValue open = new SpringValue(0, Spring.Snappy);
        private readonly ContextMenuStrip menu = Menus.Create();
        private int selected = -1;

        public SelectBox()
        {
            SetStyle(ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            menu.Closed += (s, e) =>
            {
                open.Set(0);
                Animate();
            };
        }

        public event EventHandler SelectedIndexChanged;

        public Glyph Glyph { get; set; }

        /// <summary>
        /// 选项的显示文字
        /// </summary>
        public Func<object, string> Format { get; set; } = o => o == null ? "" : o.ToString();

        /// <summary>
        /// 菜单中选项右侧的辅助说明
        /// </summary>
        public Func<object, string> Detail { get; set; }

        /// <summary>
        /// 菜单中选项的图标
        /// </summary>
        public Func<object, Glyph> ItemGlyph { get; set; }

        public string Placeholder { get; set; } = "";

        public IList<object> Items
        {
            get { return items; }
        }

        public void SetItems(IEnumerable<object> values, int selectIndex)
        {
            items.Clear();
            items.AddRange(values);
            selected = -1;
            SetSelected(Math.Min(selectIndex, items.Count - 1), false);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { SetSelected(value, false); }
        }

        public object SelectedItem
        {
            get { return selected >= 0 && selected < items.Count ? items[selected] : null; }
            set { SetSelected(items.IndexOf(value), false); }
        }

        /// <summary>
        /// 不触发事件，只刷新显示的文字（选项内容变化时）
        /// </summary>
        public void RefreshText()
        {
            string text = SelectedItem == null ? "" : Format(SelectedItem);
            if (IsHandleCreated && Visible) fade.Set(text, Motion.Now);
            else fade.Reset(text);
            Invalidate();
        }

        private void SetSelected(int index, bool user)
        {
            if (index < -1 || index >= items.Count) index = -1;
            bool changed = index != selected;
            selected = index;
            RefreshText();
            if (changed && SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        private void ShowMenu()
        {
            if (!Enabled || items.Count == 0) return;
            Menus.Clear(menu);
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                object item = items[i];
                string detail = Detail == null ? null : Detail(item);
                Glyph glyph = ItemGlyph == null ? Glyph.None : ItemGlyph(item);
                ToolStripMenuItem menuItem = string.IsNullOrEmpty(detail)
                    ? Menus.Item(Format(item), glyph, (s, e) => SetSelected(index, true))
                    : Menus.Item(Format(item), detail, glyph, (s, e) => SetSelected(index, true));
                menuItem.Checked = i == selected;
                menu.Items.Add(menuItem);
            }
            menu.MinimumSize = new Size(Width - (int)Px(Metrics.FieldMargin * 2), 0);
            open.Set(1);
            Animate();
            menu.Show(this, new Point((int)Px(Metrics.FieldMargin), Height - (int)Px(Metrics.FieldMargin) + (int)Px(4)));
        }

        public void Place(Rectangle visual)
        {
            int m = (int)Math.Round(Px(Metrics.FieldMargin));
            Bounds = Rectangle.Inflate(visual, m, m);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) ShowMenu();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Enabled) Focus();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.F4 || e.Alt && e.KeyCode == Keys.Down)
            {
                ShowMenu();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && selected > 0)
            {
                SetSelected(selected - 1, true);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down && selected < items.Count - 1)
            {
                SetSelected(selected + 1, true);
                e.Handled = true;
            }
        }

        protected override bool IsAnimating(double now)
        {
            return base.IsAnimating(now) || fade.IsAnimatingAt(now) || open.IsAnimatingAt(now);
        }

        protected override void PaintContent(Graphics g, double now)
        {
            float s = S;
            float m = Px(Metrics.FieldMargin);
            var v = new RectangleF(m, m, Width - m * 2, Height - m * 2);
            float radius = Px(Metrics.InputRadius);
            double opacity = HostOpacity * (Enabled ? 1 : 0.6);
            double hover = HoverT.ValueAt(now), focus = FocusT.ValueAt(now), opened = open.ValueAt(now);
            double active = Math.Max(KeyboardFocused ? focus : 0, opened);

            if (active > 0.01)
            {
                float w = Px(3);
                Shapes.Stroke(g, RectangleF.Inflate(v, w, w), radius + w, Palette.Fade(Palette.FocusRing, active * opacity), w);
            }
            Color border = Palette.Mix(Palette.Mix(Palette.Line, Palette.LineStrong, hover), Palette.Accent, active);
            Shapes.Fill(g, v, radius, Palette.Fade(Enabled ? Palette.Mix(Palette.Surface, Palette.Surface2, hover * 0.6) : Palette.Surface2, opacity));
            Shapes.Stroke(g, v, radius, Palette.Fade(border, opacity), Px(1));

            float x = v.X + Px(13);
            float size = Px(16);
            if (Glyph != Glyph.None)
            {
                Icons.Draw(g, Glyph, new RectangleF(x, v.Y + (v.Height - size) / 2, size, size), Palette.Fade(Palette.Mix(Palette.Ink2, Palette.Ink, active), opacity), s);
                x += Px(25);
            }

            float chevron = Px(14);
            var chevronBox = new RectangleF(v.Right - Px(12) - chevron, v.Y + (v.Height - chevron) / 2, chevron, chevron);
            GraphicsStateRotate(g, chevronBox, (float)(180 * opened), () =>
                Icons.Draw(g, Glyph.ChevronDown, chevronBox, Palette.Fade(Palette.Ink3, opacity), s));

            var textRect = new RectangleF(x, v.Y, chevronBox.X - Px(8) - x, v.Height);
            bool empty = string.IsNullOrEmpty(fade.Current);
            if (empty)
            {
                Typo.Draw(g, Placeholder, TextStyle.Input, Palette.Fade(Palette.Ink3, opacity), textRect, s);
                return;
            }
            foreach (var layer in fade.Layers(now))
            {
                string text = Typo.Ellipsize(layer.Value, TextStyle.Input, textRect.Width, s);
                float baseline = Typo.CenterBaseline(textRect, TextStyle.Input, s) + (float)layer.OffsetY * s;
                Typo.DrawLine(g, text, TextStyle.Input, Palette.Ink, textRect.X, baseline, s, opacity * layer.Opacity, layer.Blur);
            }
        }

        private static void GraphicsStateRotate(Graphics g, RectangleF box, float degrees, Action draw)
        {
            var state = g.Save();
            g.TranslateTransform(box.X + box.Width / 2, box.Y + box.Height / 2);
            g.RotateTransform(degrees);
            g.TranslateTransform(-(box.X + box.Width / 2), -(box.Y + box.Height / 2));
            draw();
            g.Restore(state);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) menu.Dispose();
            base.Dispose(disposing);
        }
    }
}
