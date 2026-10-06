using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 可滚动的自绘白色卡片：滚动位置是弹簧，滚动条无箭头、轨道透明，滑块 5px 胶囊 ink 22%，悬停变粗到 8px、42%
    /// </summary>
    public abstract class ScrollSurface : SpringControl
    {
        private readonly SpringValue scroll = new SpringValue(0, Spring.Default);
        private readonly SpringValue barHover = new SpringValue(0, Spring.Snappy);
        private bool draggingThumb;
        private float dragStartY;
        private double dragStartScroll;

        /// <summary>
        /// 内容总高度（逻辑像素，不含顶部固定区）
        /// </summary>
        protected abstract float ContentHeight { get; }

        /// <summary>
        /// 顶部固定区（如表头）的高度（逻辑像素）
        /// </summary>
        protected virtual float HeaderHeight
        {
            get { return 0; }
        }

        protected float ViewportHeight
        {
            get { return Height / S - HeaderHeight; }
        }

        protected float MaxScroll
        {
            get { return Math.Max(0, ContentHeight - ViewportHeight); }
        }

        protected double ScrollAt(double now)
        {
            return scroll.ValueAt(now);
        }

        protected void ScrollTo(double target, bool animate = true)
        {
            target = Math.Max(0, Math.Min(MaxScroll, target));
            if (animate && IsHandleCreated) scroll.Set(target);
            else scroll.Snap(target);
            Animate();
        }

        protected double ScrollTarget
        {
            get { return scroll.Target; }
        }

        /// <summary>
        /// 让 [top, bottom]（内容坐标）完整可见
        /// </summary>
        protected void EnsureVisible(float top, float bottom)
        {
            double target = scroll.Target;
            if (top < target) ScrollTo(top);
            else if (bottom > target + ViewportHeight) ScrollTo(bottom - ViewportHeight);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScroll <= 0) return;
            ScrollTo(scroll.Target - e.Delta / 120.0 * 92);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (scroll.Target > MaxScroll) ScrollTo(MaxScroll, false);
        }

        private RectangleF ThumbRect(double now, float width)
        {
            float s = S;
            float viewport = ViewportHeight, content = ContentHeight;
            float trackTop = (HeaderHeight + 6) * s, trackHeight = (viewport - 12) * s;
            float h = Math.Max(28 * s, trackHeight * viewport / content);
            float y = trackTop + (float)((trackHeight - h) * ScrollAt(now) / Math.Max(1, MaxScroll));
            return new RectangleF(Width - (4 + width) * s, y, width * s, h);
        }

        private bool OverScrollbar(Point p)
        {
            return MaxScroll > 0 && p.X >= Width - 16 * S && p.Y >= HeaderHeight * S;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (draggingThumb)
            {
                float s = S;
                float trackHeight = (ViewportHeight - 12) * s;
                float thumb = Math.Max(28 * s, trackHeight * ViewportHeight / ContentHeight);
                double perPixel = MaxScroll / Math.Max(1, trackHeight - thumb);
                scroll.Snap(Math.Max(0, Math.Min(MaxScroll, dragStartScroll + (e.Y - dragStartY) * perPixel)));
                Animate();
                return;
            }
            double target = OverScrollbar(e.Location) ? 1 : 0;
            if (barHover.Target != target)
            {
                barHover.Set(target);
                Animate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!draggingThumb) barHover.Set(0);
            Animate();
        }

        /// <summary>
        /// 按在滚动条上时返回 true，子类不再处理这次按下
        /// </summary>
        protected bool HandleScrollbarDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !OverScrollbar(e.Location)) return false;
            double now = Motion.Now;
            RectangleF thumb = ThumbRect(now, 8);
            if (e.Y < thumb.Top || e.Y > thumb.Bottom)
            {
                // 点轨道翻一页
                ScrollTo(scroll.Target + (e.Y < thumb.Top ? -1 : 1) * ViewportHeight * 0.9);
                return true;
            }
            draggingThumb = true;
            dragStartY = e.Y;
            dragStartScroll = ScrollAt(now);
            scroll.Snap(dragStartScroll);
            return true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!draggingThumb) return;
            draggingThumb = false;
            if (!OverScrollbar(e.Location)) barHover.Set(0);
            Animate();
        }

        protected override bool IsAnimating(double now)
        {
            return base.IsAnimating(now) || scroll.IsAnimatingAt(now) || barHover.IsAnimatingAt(now);
        }

        protected void PaintScrollbar(Graphics g, double now, double opacity)
        {
            if (MaxScroll <= 0) return;
            double h = Math.Max(barHover.ValueAt(now), draggingThumb ? 1 : 0);
            float width = (float)Motion.Lerp(5, 8, h);
            RectangleF thumb = ThumbRect(now, width);
            Shapes.Fill(g, thumb, thumb.Width / 2, Palette.Fade(Palette.Ink, Motion.Lerp(0.22, 0.42, h) * opacity));
        }

        /// <summary>
        /// 白色卡片底和裁剪路径
        /// </summary>
        protected GraphicsPath CardPath()
        {
            return Shapes.RoundRect(new RectangleF(0, 0, Width, Height), Px(Metrics.PanelRadius));
        }

        protected void PaintEmptyState(Graphics g, Glyph glyph, string title, string body, double opacity, RectangleF area)
        {
            float s = S;
            float circle = Px(44);
            float bodyLineHeight = Typo.LineHeight(TextStyle.Small, s);
            List<string> lines = string.IsNullOrEmpty(body) ? new List<string>() : Typo.Wrap(body, TextStyle.Small, Math.Min(area.Width - Px(48), Px(320)), s);
            float total = circle + Px(14) + Typo.LineHeight(TextStyle.Title, s) + Px(4) + lines.Count * bodyLineHeight;
            float y = area.Y + (area.Height - total) / 2;
            float cx = area.X + area.Width / 2;
            using (var brush = new SolidBrush(Palette.Fade(Palette.Surface2, opacity)))
                g.FillEllipse(brush, cx - circle / 2, y, circle, circle);
            float icon = Px(20);
            Icons.Draw(g, glyph, new RectangleF(cx - icon / 2, y + (circle - icon) / 2, icon, icon), Palette.Fade(Palette.Ink2, opacity), s);
            y += circle + Px(14);
            float lh = Typo.LineHeight(TextStyle.Title, s);
            Typo.Draw(g, title, TextStyle.Title, Palette.Fade(Palette.Ink, opacity), new RectangleF(area.X, y, area.Width, lh), s, StringAlignment.Center);
            y += lh + Px(4);
            foreach (string line in lines)
            {
                Typo.Draw(g, line, TextStyle.Small, Palette.Fade(Palette.Ink3, opacity), new RectangleF(area.X, y, area.Width, bodyLineHeight), s, StringAlignment.Center);
                y += bodyLineHeight;
            }
        }
    }

    public enum CellKind
    {
        /// <summary>普通文字</summary>
        Text,
        /// <summary>名字（body-medium）</summary>
        Strong,
        /// <summary>次级文字（ink-2）</summary>
        Secondary,
        /// <summary>等宽数字（Geist Mono）</summary>
        Mono,
        /// <summary>标签 chip</summary>
        Chip
    }

    public sealed class TableColumn
    {
        public TableColumn(string header, float width, CellKind kind)
        {
            Header = header;
            Width = width;
            Kind = kind;
        }

        public string Header { get; private set; }

        /// <summary>逻辑像素；小于等于 0 时按比例分剩余宽度</summary>
        public float Width { get; private set; }

        public CellKind Kind { get; private set; }
    }

    public sealed class TableRow
    {
        public TableRow(object tag, params string[] cells)
        {
            Tag = tag;
            Cells = cells;
        }

        public object Tag { get; private set; }

        public string[] Cells { get; private set; }

        /// <summary>
        /// 正在处理的列：画加载圆弧代替文字；-1 表示没有
        /// </summary>
        public int BusyColumn { get; set; } = -1;

        /// <summary>
        /// 最后一列文字后的标签 chip
        /// </summary>
        public string Badge { get; set; }
    }

    /// <summary>
    /// 表格：无竖线，行间 1px line（左右内缩 12），行高 46，表头 40。
    /// 悬停高亮和选中高亮各是一块会在行之间滑动的形状；刷新时各行错开 22ms 从下方 5px 淡入
    /// </summary>
    public class DataTable : ScrollSurface
    {
        private const float Row = Metrics.RowHeight;
        private const float CellPadding = 20;
        private const float ColumnGap = 12;

        private readonly List<TableColumn> columns = new List<TableColumn>();
        private readonly List<TableRow> rows = new List<TableRow>();
        private readonly SpringValue selY = new SpringValue(0, Spring.Default);
        private readonly SpringValue selAlpha = new SpringValue(0, Spring.Snappy);
        private readonly SpringValue hoverY = new SpringValue(0, Spring.Default);
        private readonly SpringValue hoverAlpha = new SpringValue(0, Spring.Snappy);
        private int selected = -1;
        private int hovered = -1;
        private double staggerAt = double.NegativeInfinity;

        public DataTable()
        {
            SetStyle(ControlStyles.Selectable, true);
        }

        public event EventHandler SelectionChanged;

        /// <summary>双击或回车</summary>
        public event EventHandler RowActivated;

        /// <summary>Delete 键</summary>
        public event EventHandler DeletePressed;

        /// <summary>右键（参数为控件坐标）</summary>
        public event EventHandler<MouseEventArgs> RowMenuRequested;

        public Glyph EmptyGlyph { get; set; } = Glyph.Inbox;
        public string EmptyTitle { get; set; } = "暂无内容";
        public string EmptyText { get; set; }

        public IList<TableColumn> Columns
        {
            get { return columns; }
        }

        public IReadOnlyList<TableRow> Rows
        {
            get { return rows; }
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { Select(value, true); }
        }

        public TableRow SelectedRow
        {
            get { return selected >= 0 && selected < rows.Count ? rows[selected] : null; }
        }

        protected override float HeaderHeight
        {
            get { return Metrics.HeaderHeight; }
        }

        protected override float ContentHeight
        {
            get { return rows.Count * Row; }
        }

        /// <summary>
        /// 换一批行。stagger 为 true 时各行错开淡入
        /// </summary>
        public void SetRows(IEnumerable<TableRow> values, int select, bool stagger)
        {
            rows.Clear();
            rows.AddRange(values);
            if (stagger && IsHandleCreated) staggerAt = Motion.Now;
            hovered = -1;
            hoverAlpha.Snap(0);
            int index = rows.Count == 0 ? -1 : Math.Max(-1, Math.Min(select, rows.Count - 1));
            selected = -2;
            Select(index, !stagger);
            if (ScrollTarget > MaxScroll) ScrollTo(MaxScroll, false);
            Invalidate();
        }

        /// <summary>
        /// 只改行内容（如测速结果），不重新入场
        /// </summary>
        public void RefreshRows()
        {
            Invalidate();
        }

        /// <summary>
        /// 某一行的范围（控件坐标，设备像素），面板从它展开
        /// </summary>
        public Rectangle RowBounds(int index)
        {
            float s = S;
            float y = (HeaderHeight + index * Row - (float)ScrollAt(Motion.Now)) * s;
            return Rectangle.Round(new RectangleF(0, y, Width, Row * s));
        }

        private void Select(int index, bool animate)
        {
            if (index < -1 || index >= rows.Count) index = -1;
            if (index == selected) return;
            bool hadSelection = selected >= 0;
            selected = index;
            double now = Motion.Now;
            if (index >= 0)
            {
                if (!hadSelection || !animate || !IsHandleCreated) selY.Snap(index * Row);
                else selY.Set(index * Row, now);
                if (animate && IsHandleCreated) selAlpha.Set(1, now);
                else selAlpha.Snap(1);
                EnsureVisible(index * Row, (index + 1) * Row);
            }
            else
            {
                selAlpha.Set(0, now);
            }
            Animate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        private int RowAt(Point p)
        {
            float s = S;
            float y = p.Y / s - HeaderHeight;
            if (y < 0) return -1;
            int index = (int)Math.Floor((y + ScrollAt(Motion.Now)) / Row);
            return index >= 0 && index < rows.Count ? index : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = e.X >= Width - 16 * S ? -1 : RowAt(e.Location);
            if (index == hovered) return;
            hovered = index;
            double now = Motion.Now;
            if (index < 0) hoverAlpha.Set(0, now);
            else
            {
                if (hoverAlpha.ValueAt(now) < 0.05) hoverY.Snap(index * Row);
                else hoverY.Set(index * Row, now);
                hoverAlpha.Set(1, now);
            }
            Animate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = -1;
            hoverAlpha.Set(0);
            Animate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (HandleScrollbarDown(e)) return;
            base.OnMouseDown(e);
            int index = RowAt(e.Location);
            if (index >= 0) Select(index, true);
            if (e.Button == MouseButtons.Right && index >= 0 && RowMenuRequested != null) RowMenuRequested(this, e);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            if (RowAt(PointToClient(Cursor.Position)) >= 0 && RowActivated != null) RowActivated(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (rows.Count == 0) return;
            int page = Math.Max(1, (int)(ViewportHeight / Row) - 1);
            switch (e.KeyCode)
            {
                case Keys.Up: Select(Math.Max(0, selected - 1), true); break;
                case Keys.Down: Select(Math.Min(rows.Count - 1, selected + 1), true); break;
                case Keys.Home: Select(0, true); break;
                case Keys.End: Select(rows.Count - 1, true); break;
                case Keys.PageUp: Select(Math.Max(0, selected - page), true); break;
                case Keys.PageDown: Select(Math.Min(rows.Count - 1, selected + page), true); break;
                case Keys.Enter:
                    if (selected >= 0 && RowActivated != null) RowActivated(this, EventArgs.Empty);
                    break;
                case Keys.Delete:
                    if (selected >= 0 && DeletePressed != null) DeletePressed(this, EventArgs.Empty);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // 回车在表格里是“打开所选行”，不交给窗体的默认按钮
            if (keyData == Keys.Enter && Focused && selected >= 0 && RowActivated != null)
            {
                RowActivated(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override bool IsAnimating(double now)
        {
            return base.IsAnimating(now) || selY.IsAnimatingAt(now) || selAlpha.IsAnimatingAt(now)
                || hoverY.IsAnimatingAt(now) || hoverAlpha.IsAnimatingAt(now)
                || now - staggerAt < 0.25 + rows.Count * 0.022
                || rows.Exists(r => r.BusyColumn >= 0);
        }

        /// <summary>
        /// 各列的 x 与宽度（设备像素）
        /// </summary>
        private void ColumnSpans(float s, float[] xs, float[] ws)
        {
            float fixedTotal = 0, shares = 0;
            foreach (TableColumn c in columns)
            {
                if (c.Width > 0) fixedTotal += c.Width;
                else shares += 1;
            }
            float available = Width / s - CellPadding * 2 - ColumnGap * Math.Max(0, columns.Count - 1) - 8;
            float share = shares > 0 ? Math.Max(40, (available - fixedTotal) / shares) : 0;
            float x = CellPadding;
            for (int i = 0; i < columns.Count; i++)
            {
                float w = columns[i].Width > 0 ? columns[i].Width : share;
                xs[i] = x * s;
                ws[i] = w * s;
                x += w + ColumnGap;
            }
        }

        protected override void PaintContent(Graphics g, double now)
        {
            float s = S;
            double opacity = HostOpacity;
            using (GraphicsPath card = CardPath())
            {
                using (var brush = new SolidBrush(Palette.Fade(Palette.Surface, opacity)))
                    g.FillPath(brush, card);

                var state = g.Save();
                g.SetClip(card);

                var xs = new float[columns.Count];
                var ws = new float[columns.Count];
                ColumnSpans(s, xs, ws);

                // 表头
                float header = HeaderHeight * s;
                for (int i = 0; i < columns.Count; i++)
                    Typo.Draw(g, columns[i].Header, TextStyle.Caption, Palette.Fade(Palette.Ink3, opacity), new RectangleF(xs[i], 0, ws[i], header), s);
                using (var pen = new Pen(Palette.Fade(Palette.Line, opacity), Px(1)))
                    g.DrawLine(pen, 0, header - Px(0.5f), Width, header - Px(0.5f));

                g.SetClip(new RectangleF(0, header, Width, Height - header), CombineMode.Intersect);
                double scroll = ScrollAt(now);
                float rowH = Row * s;

                if (rows.Count == 0)
                {
                    double fade = Motion.EaseOutCubic(Motion.Progress(now, staggerAt, 0.2));
                    PaintEmptyState(g, EmptyGlyph, EmptyTitle, EmptyText, opacity * fade, new RectangleF(0, header, Width, Height - header));
                }

                // 悬停块、选中块：各是一块在行之间滑动的形状
                double ha = hoverAlpha.ValueAt(now);
                if (ha > 0.01)
                {
                    float y = header + (float)(hoverY.ValueAt(now) - scroll) * s;
                    using (var brush = new SolidBrush(Palette.Fade(Palette.Surface2, ha * opacity)))
                        g.FillRectangle(brush, 0, y, Width, rowH);
                }
                double sa = Motion.Clamp01(selAlpha.ValueAt(now));
                if (sa > 0.01 && selected >= 0)
                {
                    float y = header + (float)(selY.ValueAt(now) - scroll) * s;
                    using (var brush = new SolidBrush(Palette.Fade(Palette.AccentSoft, sa * opacity)))
                        g.FillRectangle(brush, 0, y, Width, rowH);
                    using (var brush = new SolidBrush(Palette.Fade(Palette.Accent, sa * opacity)))
                        g.FillRectangle(brush, 0, y, Px(3), rowH);
                }

                int first = Math.Max(0, (int)Math.Floor(scroll / Row));
                int last = Math.Min(rows.Count - 1, (int)Math.Ceiling((scroll + ViewportHeight) / Row));
                for (int i = first; i <= last; i++)
                {
                    double enter = Motion.EaseOutCubic(Motion.Progress(now, staggerAt + i * 0.022, 0.2));
                    double rowOpacity = opacity * enter;
                    float top = header + (float)(i * Row - scroll + 5 * (1 - enter)) * s;
                    if (i < rows.Count - 1)
                    {
                        using (var pen = new Pen(Palette.Fade(Palette.Line, rowOpacity), Px(1)))
                            g.DrawLine(pen, Px(12), top + rowH - Px(0.5f), Width - Px(12), top + rowH - Px(0.5f));
                    }
                    PaintRow(g, rows[i], top, rowH, xs, ws, rowOpacity, now, s);
                }

                PaintScrollbar(g, now, opacity);
                g.Restore(state);
                using (var pen = new Pen(Palette.Fade(Palette.Line, opacity), Px(1)))
                using (GraphicsPath border = Shapes.RoundRect(new RectangleF(Px(0.5f), Px(0.5f), Width - Px(1), Height - Px(1)), Px(Metrics.PanelRadius) - Px(0.5f)))
                    g.DrawPath(pen, border);
                if (KeyboardFocused)
                {
                    using (GraphicsPath ring = Shapes.RoundRect(new RectangleF(Px(1), Px(1), Width - Px(2), Height - Px(2)), Px(Metrics.PanelRadius) - Px(1)))
                    using (var pen = new Pen(Palette.Fade(Palette.Accent, FocusT.ValueAt(now) * opacity), Px(2)))
                        g.DrawPath(pen, ring);
                }
            }
        }

        private void PaintRow(Graphics g, TableRow row, float top, float rowH, float[] xs, float[] ws, double opacity, double now, float s)
        {
            var rect = new RectangleF(0, top, Width, rowH);
            for (int c = 0; c < columns.Count && c < row.Cells.Length; c++)
            {
                var cell = new RectangleF(xs[c], top, ws[c], rowH);
                if (row.BusyColumn == c)
                {
                    float d = Px(14);
                    Spinner.Draw(g, new PointF(cell.X + d / 2, top + rowH / 2), d, Px(1.5f), Palette.Ink2, opacity, now);
                    continue;
                }
                string text = row.Cells[c] ?? "";
                CellKind kind = columns[c].Kind;
                float textWidth;
                switch (kind)
                {
                    case CellKind.Chip:
                        if (text.Length > 0)
                        {
                            string fitted = Typo.Ellipsize(text, TextStyle.Caption, cell.Width - Px(16), s);
                            Chips.Draw(g, fitted, cell.X, top + rowH / 2, s, opacity);
                        }
                        textWidth = 0;
                        break;
                    case CellKind.Mono:
                        Typo.Draw(g, text, TextStyle.Mono, Palette.Fade(Palette.Ink, opacity), cell, s);
                        textWidth = Math.Min(cell.Width, Typo.Measure(text, TextStyle.Mono, s));
                        break;
                    case CellKind.Strong:
                        Typo.Draw(g, text, TextStyle.BodyMedium, Palette.Fade(Palette.Ink, opacity), cell, s);
                        textWidth = Math.Min(cell.Width, Typo.Measure(text, TextStyle.BodyMedium, s));
                        break;
                    case CellKind.Secondary:
                        Typo.Draw(g, text, TextStyle.Body, Palette.Fade(Palette.Ink2, opacity), cell, s);
                        textWidth = Math.Min(cell.Width, Typo.Measure(text, TextStyle.Body, s));
                        break;
                    default:
                        Typo.Draw(g, text, TextStyle.Body, Palette.Fade(Palette.Ink, opacity), cell, s);
                        textWidth = Math.Min(cell.Width, Typo.Measure(text, TextStyle.Body, s));
                        break;
                }
                if (c == columns.Count - 1 && !string.IsNullOrEmpty(row.Badge))
                    Chips.Draw(g, row.Badge, cell.X + textWidth + Px(8), top + rowH / 2, s, opacity);
            }
        }
    }
}
