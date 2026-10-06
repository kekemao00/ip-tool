using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    public sealed class PaletteCommand
    {
        public PaletteCommand(string group, string title, Glyph glyph, Action run)
        {
            Group = group;
            Title = title;
            Glyph = glyph;
            Run = run;
        }

        public string Group { get; private set; }
        public string Title { get; private set; }
        public Glyph Glyph { get; private set; }
        public Action Run { get; private set; }

        /// <summary>右侧的快捷键或说明</summary>
        public string Hint { get; set; }

        /// <summary>额外的搜索关键词</summary>
        public string Keywords { get; set; }

        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string text = (Title + " " + Group + " " + Keywords + " " + Hint).ToLowerInvariant();
            return query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(text.Contains);
        }
    }

    /// <summary>
    /// 命令面板（Ctrl+K）：顶部 44 高搜索输入，输入即筛选；上下键移动时高亮块滑过去，回车执行
    /// </summary>
    public sealed class CommandPalette : SheetContent
    {
        private readonly FieldBox search = new FieldBox();
        private readonly CommandList list = new CommandList();
        private readonly List<PaletteCommand> commands;

        public CommandPalette(IEnumerable<PaletteCommand> commands)
        {
            this.commands = commands.ToList();
            search.Glyph = Glyph.Search;
            search.Placeholder = "输入要执行的操作，如“诊断”“DNS”";
            search.TextChanged += (s, e) => Filter();
            search.Box.KeyDown += OnSearchKeyDown;
            list.Activated += (s, e) => RunHighlighted();
            Controls.Add(search);
            Controls.Add(list);
            RegisterNative(search.Box);
            HideCloseButton();
            Filter();
        }

        public override Control InitialFocus
        {
            get { return search; }
        }

        public override Size LogicalSize
        {
            get
            {
                float listHeight = Math.Min(380, Math.Max(CommandList.Row, list.LogicalContentHeight));
                return new Size(560, (int)(12 + 44 + 8 + listHeight + 12 + 30));
            }
        }

        private void Filter()
        {
            list.SetCommands(commands.Where(c => c.Matches(search.Text)).ToList());
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                list.MoveHighlight(e.KeyCode == Keys.Down ? 1 : -1);
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Enter)
            {
                RunHighlighted();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        private void RunHighlighted()
        {
            PaletteCommand command = list.Highlighted;
            if (command != null) Close(command);
        }

        protected override void LayoutContent(float s)
        {
            search.Place(new Rectangle(P(12), P(12), Width - P(24), P(44)));
            list.SetBounds(P(8), P(12 + 44 + 8), Width - P(16), Height - P(12 + 44 + 8) - P(30));
        }

        protected override void PaintContent(Graphics g, double opacity, double blur)
        {
            float s = S;
            using (var pen = new Pen(Palette.Fade(Palette.Line, opacity), s))
                g.DrawLine(pen, 0, Height - P(30), Width, Height - P(30));
            var footer = new RectangleF(P(20), Height - P(30), Width - P(40), P(30));
            Typo.Draw(g, "↑↓ 选择    Enter 执行    Esc 关闭", TextStyle.Caption, Palette.Fade(Palette.Ink3, opacity), footer, s);
            string count = list.Count + " 项";
            Typo.Draw(g, count, TextStyle.Caption, Palette.Fade(Palette.Ink3, opacity), footer, s, StringAlignment.Far);
        }

        /// <summary>
        /// 结果列表：行高 44，分组标题 caption；高亮块用 default 弹簧滑动，选中行右侧显示回车图标
        /// </summary>
        private sealed class CommandList : ScrollSurface
        {
            public const float Row = 44;
            private const float GroupHeader = 28;

            private readonly List<Entry> entries = new List<Entry>();
            private readonly SpringValue highlightY = new SpringValue(0, Spring.Default);
            private int highlight = -1;
            private double changedAt = double.NegativeInfinity;

            private sealed class Entry
            {
                public PaletteCommand Command;
                public string Group;
                public float Top;
            }

            public CommandList()
            {
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
            }

            public event EventHandler Activated;

            public int Count
            {
                get { return entries.Count(e => e.Command != null); }
            }

            public float LogicalContentHeight
            {
                get { return entries.Count == 0 ? Row : entries[entries.Count - 1].Top + Row; }
            }

            protected override float ContentHeight
            {
                get { return LogicalContentHeight; }
            }

            public PaletteCommand Highlighted
            {
                get { return highlight >= 0 && highlight < entries.Count ? entries[highlight].Command : null; }
            }

            public void SetCommands(List<PaletteCommand> commands)
            {
                entries.Clear();
                float y = 0;
                string group = null;
                foreach (PaletteCommand c in commands)
                {
                    if (c.Group != group)
                    {
                        group = c.Group;
                        entries.Add(new Entry { Group = group, Top = y });
                        y += GroupHeader;
                    }
                    entries.Add(new Entry { Command = c, Top = y });
                    y += Row;
                }
                highlight = entries.FindIndex(e => e.Command != null);
                if (highlight >= 0) highlightY.Snap(entries[highlight].Top);
                changedAt = IsHandleCreated ? Motion.Now : double.NegativeInfinity;
                ScrollTo(0, false);
                Invalidate();
            }

            public void MoveHighlight(int step)
            {
                if (highlight < 0) return;
                int i = highlight;
                do
                {
                    i += step;
                } while (i >= 0 && i < entries.Count && entries[i].Command == null);
                if (i < 0 || i >= entries.Count) return;
                SetHighlight(i);
            }

            private void SetHighlight(int index)
            {
                if (index == highlight) return;
                highlight = index;
                highlightY.Set(entries[index].Top);
                float top = entries[index].Top;
                // 往上移到第一项时把分组标题也露出来
                if (index > 0 && entries[index - 1].Command == null) top = entries[index - 1].Top;
                EnsureVisible(top, entries[index].Top + Row);
                Animate();
            }

            private int EntryAt(Point p)
            {
                float y = p.Y / S + (float)ScrollAt(Motion.Now);
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Command == null) continue;
                    if (y >= entries[i].Top && y < entries[i].Top + Row) return i;
                }
                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int i = EntryAt(e.Location);
                if (i >= 0) SetHighlight(i);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (HandleScrollbarDown(e)) return;
                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                int i = EntryAt(e.Location);
                if (e.Button == MouseButtons.Left && i >= 0 && i == highlight && Activated != null) Activated(this, EventArgs.Empty);
            }

            protected override bool IsAnimating(double now)
            {
                return base.IsAnimating(now) || highlightY.IsAnimatingAt(now) || now - changedAt < 0.4;
            }

            protected override void PaintContent(Graphics g, double now)
            {
                float s = S;
                double opacity = HostOpacity;
                double scroll = ScrollAt(now);
                if (entries.Count == 0)
                {
                    Typo.Draw(g, "没有匹配的操作", TextStyle.Body, Palette.Fade(Palette.Ink3, opacity), new RectangleF(0, 0, Width, Px(Row)), s, StringAlignment.Center);
                    return;
                }

                if (highlight >= 0)
                {
                    var r = new RectangleF(0, (float)(highlightY.ValueAt(now) - scroll) * s, Width, Px(Row));
                    Shapes.Fill(g, r, Px(10), Palette.Fade(Palette.Surface3, 0.75 * opacity));
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    Entry entry = entries[i];
                    double enter = Motion.EaseOutCubic(Motion.Progress(now, changedAt + Math.Min(i, 10) * 0.022, 0.2));
                    double a = opacity * enter;
                    float top = (float)(entry.Top - scroll + 5 * (1 - enter)) * s;
                    if (top > Height || top < -Px(Row)) continue;
                    if (entry.Command == null)
                    {
                        Typo.Draw(g, entry.Group, TextStyle.Caption, Palette.Fade(Palette.Ink3, a), new RectangleF(Px(12), top + Px(6), Width - Px(24), Px(GroupHeader - 6)), s);
                        continue;
                    }
                    PaletteCommand c = entry.Command;
                    var row = new RectangleF(0, top, Width, Px(Row));
                    float icon = Px(16);
                    Icons.Draw(g, c.Glyph, new RectangleF(Px(14), top + (row.Height - icon) / 2, icon, icon), Palette.Fade(Palette.Ink2, a), s);
                    float right = Width - Px(14);
                    if (i == highlight)
                    {
                        Icons.Draw(g, Glyph.Enter, new RectangleF(right - icon, top + (row.Height - icon) / 2, icon, icon), Palette.Fade(Palette.Ink3, a), s);
                        right -= icon + Px(10);
                    }
                    if (!string.IsNullOrEmpty(c.Hint))
                    {
                        float hw = Typo.Measure(c.Hint, TextStyle.Small, s);
                        Typo.Draw(g, c.Hint, TextStyle.Small, Palette.Fade(Palette.Ink3, a), new RectangleF(right - hw, top, hw, row.Height), s);
                        right -= hw + Px(12);
                    }
                    Typo.Draw(g, c.Title, TextStyle.Body, Palette.Fade(Palette.Ink, a), new RectangleF(Px(14 + 16 + 12), top, right - Px(14 + 16 + 12), row.Height), s);
                }
                PaintScrollbar(g, now, opacity);
            }
        }
    }
}
