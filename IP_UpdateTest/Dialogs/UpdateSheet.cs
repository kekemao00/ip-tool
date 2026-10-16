using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using IP_UpdateTest.Core.Update;
using IP_UpdateTest.Ui;

namespace IP_UpdateTest
{
    /// <summary>
    /// 更新面板：从标题栏的更新胶囊变形展开。
    /// 上面是版本变化（v2.2.2 → v2.3.0）和更新内容，下面一行显示安全说明 / 下载进度 / 校验结果，按钮随状态变化。
    /// 关闭面板不会中断下载，进度留在标题栏胶囊上
    /// </summary>
    public sealed class UpdateSheet : SheetContent
    {
        private const float PanelWidth = 500;
        private const float PanelHeight = 480;
        private const float NotesTop = 116;
        private const float StatusHeight = 48;
        private const float FooterHeight = 76;

        private readonly UpdateManager manager;
        private readonly Action skip;
        private readonly Func<Task> install;
        private readonly Action openPage;
        private readonly NotesView notes = new NotesView();
        private readonly SpringButton btnPage = new SpringButton { Kind = ButtonKind.Ghost, Glyph = Glyph.ExternalLink, Text = "在 GitHub 查看" };
        private readonly SpringButton btnLeft = FooterButton("", ButtonKind.Ghost);
        private readonly SpringButton btnSecondary = FooterButton("", ButtonKind.Secondary);
        private readonly SpringButton btnPrimary = FooterButton("", ButtonKind.Primary);
        private readonly SpringValue bar = new SpringValue(0, Spring.Smooth);
        private readonly Crossfade<Glyph> badge = new Crossfade<Glyph>(Glyph.ArrowUp);
        private readonly Crossfade<string> heading = new Crossfade<string>("");
        private UpdateState shown = (UpdateState)(-1);
        private ReleaseInfo notesFor;

        public UpdateSheet(UpdateManager manager, Action skip, Func<Task> install, Action openPage)
        {
            this.manager = manager;
            this.skip = skip;
            this.install = install;
            this.openPage = openPage;

            btnPrimary.Click += (s, e) => OnPrimary();
            btnSecondary.Click += (s, e) => OnSecondary();
            btnLeft.Click += (s, e) => OnLeft();
            btnPage.Click += (s, e) => openPage();
            InkToolTip.Shared.SetToolTip(btnPage, "在浏览器中打开发布页");
            Controls.AddRange(new Control[] { notes, btnPage, btnLeft, btnSecondary, btnPrimary });
            int tab = 0;
            foreach (Control c in new Control[] { btnPrimary, btnSecondary, btnLeft, btnPage }) c.TabIndex = tab++;
            Accept = btnPrimary;

            notesFor = manager.Release;
            notes.SetNotes(ReleaseNotes.Parse(notesFor == null ? "" : notesFor.Notes));
            manager.Changed += Sync;
            Sync();
        }

        public override Size LogicalSize
        {
            get { return new Size((int)PanelWidth, (int)PanelHeight); }
        }

        public override Control InitialFocus
        {
            get { return btnPrimary; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) manager.Changed -= Sync;
            base.Dispose(disposing);
        }

        #region 状态

        private void Sync()
        {
            if (IsDisposed) return;
            // 面板开着时后台重新查询发现已经没有可更新的版本（如版本被撤下）：没有可做的了，收起面板
            if (!manager.HasUpdate && Overlay != null)
            {
                Cancel();
                return;
            }
            UpdateState state = manager.State;
            double now = Motion.Now;
            bool animate = IsHandleCreated && !Motion.IsFrozen;

            // 面板开着时后台查到了更新的版本：换成新版本的更新内容
            if (manager.Release != null && manager.Release != notesFor)
            {
                notesFor = manager.Release;
                notes.SetNotes(ReleaseNotes.Parse(notesFor.Notes));
            }

            double fraction = state == UpdateState.Downloading ? manager.Progress.Fraction
                : state == UpdateState.Verifying || state == UpdateState.Ready || state == UpdateState.Installing ? 1
                : 0;
            if (animate) bar.Set(fraction, now);
            else bar.Snap(fraction);

            if (state == shown)
            {
                Invalidate();
                return;
            }
            UpdateState previous = shown;
            shown = state;

            Glyph glyph = state == UpdateState.Downloading || state == UpdateState.Verifying ? Glyph.ArrowDown
                : state == UpdateState.Ready ? Glyph.Check
                : state == UpdateState.Installing ? Glyph.Refresh
                : state == UpdateState.Failed ? Glyph.Alert
                : Glyph.ArrowUp;
            string title = HeadingFor(state);
            if (animate)
            {
                badge.Set(glyph, now);
                heading.Set(title, now);
            }
            else
            {
                badge.Reset(glyph);
                heading.Reset(title);
            }

            btnSecondary.Enabled = btnPrimary.Enabled = true;
            switch (state)
            {
                case UpdateState.Downloading:
                case UpdateState.Verifying:
                    Buttons(null, Glyph.None, "取消下载", "在后台继续", Glyph.None);
                    break;
                case UpdateState.Ready:
                    Buttons(null, Glyph.None, "稍后", "重启并更新", Glyph.Refresh);
                    break;
                case UpdateState.Installing:
                    Buttons(null, Glyph.None, "稍后", "重启并更新", Glyph.Refresh);
                    btnSecondary.Enabled = false;
                    if (!btnPrimary.IsLoading) btnPrimary.BeginLoading();
                    break;
                case UpdateState.Failed:
                    Buttons("在浏览器中下载", Glyph.ExternalLink, "关闭", "重试", Glyph.Refresh);
                    break;
                default:
                    Buttons("跳过此版本", Glyph.None, "稍后", "立即更新", Glyph.Download);
                    break;
            }
            if (previous == UpdateState.Installing && state != UpdateState.Installing) btnPrimary.Reset();
            PerformLayout();
            Invalidate(true);
        }

        private static string HeadingFor(UpdateState state)
        {
            switch (state)
            {
                case UpdateState.Downloading:
                case UpdateState.Verifying:
                    return "正在下载更新";
                case UpdateState.Ready:
                    return "更新已就绪";
                case UpdateState.Installing:
                    return "正在安装更新";
                case UpdateState.Failed:
                    return "更新没有完成";
                default:
                    return "发现新版本";
            }
        }

        private void Buttons(string left, Glyph leftGlyph, string secondary, string primary, Glyph primaryGlyph)
        {
            btnLeft.Visible = left != null;
            if (left != null)
            {
                btnLeft.Text = left;
                btnLeft.Glyph = leftGlyph;
            }
            btnSecondary.Text = secondary;
            btnPrimary.Text = primary;
            btnPrimary.Glyph = primaryGlyph;
        }

        private async void OnPrimary()
        {
            switch (manager.State)
            {
                case UpdateState.Available:
                case UpdateState.Failed:
                    manager.StartDownload();
                    break;
                case UpdateState.Downloading:
                case UpdateState.Verifying:
                    Close(null);
                    break;
                case UpdateState.Ready:
                    await install();
                    break;
            }
        }

        private void OnSecondary()
        {
            if (manager.State == UpdateState.Downloading || manager.State == UpdateState.Verifying) manager.CancelDownload();
            else Cancel();
        }

        private void OnLeft()
        {
            if (manager.State == UpdateState.Failed)
            {
                openPage();
                return;
            }
            skip();
            Cancel();
        }

        #endregion

        #region 布局与绘制

        protected override void LayoutContent(float s)
        {
            int top = P(NotesTop);
            int bottom = Height - P(FooterHeight + StatusHeight);
            notes.SetBounds(P(24), top, Width - P(48), Math.Max(P(40), bottom - top));
            int pageW = btnPage.PreferredWidth, pageH = P(28);
            btnPage.SetBounds(Width - P(24) + P(10) - pageW, top - P(6) - pageH, pageW, pageH);
            LayoutFooter(s, btnPrimary, btnSecondary);
            if (btnLeft.Visible)
            {
                int h = P(Metrics.PanelButtonHeight);
                btnLeft.SetBounds(P(14), Height - P(20) - h, btnLeft.PreferredWidth, h);
            }
            // 按钮宽度随文字变化
            int primaryMin = P(112);
            if (btnPrimary.Width < primaryMin)
            {
                int shift = primaryMin - btnPrimary.Width;
                btnPrimary.SetBounds(btnPrimary.Left - shift, btnPrimary.Top, primaryMin, btnPrimary.Height);
                btnSecondary.Left -= shift;
            }
        }

        protected override void PaintContent(Graphics g, double opacity, double blur)
        {
            float s = S;
            double now = Motion.Now;
            ReleaseInfo release = manager.Release;
            PaintMark(g, s, opacity, now);

            // 标题：随状态在模糊里换掉
            float textX = P(24 + 48 + 14);
            var titleRect = new RectangleF(0, P(24), Width, P(26));
            foreach (var layer in heading.Layers(now))
            {
                float baseline = Typo.CenterBaseline(titleRect, TextStyle.Title, s) + (float)layer.OffsetY * s;
                Typo.DrawLine(g, layer.Value, TextStyle.Title, Palette.Ink, textX, baseline, s, opacity * layer.Opacity, Math.Max(blur, layer.Blur));
            }

            // 版本变化：v2.2.2 → v2.3.0 · 日期 · 大小
            var metaRect = new RectangleF(0, P(50), Width, P(22));
            float metaBase = Typo.CenterBaseline(metaRect, TextStyle.Mono, s);
            float x = textX;
            string from = Versions.Display(manager.CurrentVersion);
            Typo.DrawLine(g, from, TextStyle.Mono, Palette.Ink3, x, metaBase, s, opacity, blur);
            x += Typo.Measure(from, TextStyle.Mono, s) + P(6);
            float arrow = P(13);
            Icons.Draw(g, Glyph.ArrowRight, new RectangleF(x, metaRect.Y + (metaRect.Height - arrow) / 2, arrow, arrow), Palette.Fade(Palette.Ink3, opacity), s);
            x += arrow + P(6);
            string to = release == null ? "" : release.DisplayVersion;
            Typo.DrawLine(g, to, TextStyle.Mono, Palette.Accent, x, metaBase, s, opacity, blur);
            x += Typo.Measure(to, TextStyle.Mono, s);
            string detail = Detail(release);
            if (detail.Length > 0)
            {
                float smallBase = Typo.CenterBaseline(metaRect, TextStyle.Small, s);
                Typo.DrawLine(g, detail, TextStyle.Small, Palette.Ink3, x + P(10), smallBase, s, opacity, blur);
            }

            // 更新内容
            var labelRect = new RectangleF(P(24), P(NotesTop - 28), Width, P(22));
            Typo.DrawLine(g, "更新内容", TextStyle.Caption, Palette.Ink3, labelRect.X, Typo.CenterBaseline(labelRect, TextStyle.Caption, s), s, opacity, blur);

            PaintStatus(g, s, opacity, blur, now);

            if (bar.IsAnimatingAt(now) || badge.IsAnimatingAt(now) || heading.IsAnimatingAt(now)
                || manager.State == UpdateState.Verifying || manager.State == UpdateState.Installing)
                FrameClock.Request(this);
        }

        private static string Detail(ReleaseInfo release)
        {
            if (release == null) return "";
            var parts = new List<string>();
            if (release.PublishedAt.HasValue) parts.Add(release.PublishedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 发布");
            if (release.PackageSize > 0) parts.Add(FormatBytes(release.PackageSize));
            return parts.Count == 0 ? "" : "·  " + string.Join("  ·  ", parts);
        }

        /// <summary>
        /// 程序标识（与标题栏一致的墨黑圆角方块）加右下角的状态徽标
        /// </summary>
        private void PaintMark(Graphics g, float s, double opacity, double now)
        {
            float size = P(48);
            var mark = new RectangleF(P(24), P(22), size, size);
            Shapes.Fill(g, mark, P(13), Palette.Fade(Palette.Ink, opacity));
            float glyph = P(24);
            Icons.Draw(g, Glyph.Network, new RectangleF(mark.X + (size - glyph) / 2, mark.Y + (size - glyph) / 2, glyph, glyph), Palette.Fade(Color.White, opacity), s, 1.75f);

            float d = P(22), ring = P(2.5f);
            var circle = new RectangleF(mark.Right - d + P(6), mark.Bottom - d + P(6), d, d);
            using (var brush = new SolidBrush(Palette.Fade(Palette.Surface, opacity)))
                g.FillEllipse(brush, RectangleF.Inflate(circle, ring, ring));
            Color tint = manager.State == UpdateState.Failed ? Palette.Danger : Palette.Accent;
            using (var brush = new SolidBrush(Palette.Fade(tint, opacity)))
                g.FillEllipse(brush, circle);
            float icon = P(12);
            foreach (var layer in badge.Layers(now))
            {
                var box = new RectangleF(circle.X + (d - icon) / 2, circle.Y + (d - icon) / 2 + (float)layer.OffsetY * s, icon, icon);
                Icons.Draw(g, layer.Value, box, Palette.Fade(Color.White, opacity * layer.Opacity), s, 2f);
            }
        }

        /// <summary>
        /// 底部按钮上方的一行：安全说明 / 下载进度 / 校验结果 / 错误
        /// </summary>
        private void PaintStatus(Graphics g, float s, double opacity, double blur, double now)
        {
            float top = Height - P(FooterHeight + StatusHeight) + P(10);
            float left = P(24), right = Width - P(24);
            var row = new RectangleF(left, top, right - left, P(20));
            float icon = P(14);
            var iconBox = new RectangleF(left, row.Y + (row.Height - icon) / 2, icon, icon);
            float textX = left + icon + P(8);
            float baseline = Typo.CenterBaseline(row, TextStyle.Small, s);
            UpdateState state = manager.State;
            DownloadProgress p = manager.Progress;

            switch (state)
            {
                case UpdateState.Downloading:
                case UpdateState.Verifying:
                case UpdateState.Installing:
                {
                    string label = state == UpdateState.Downloading ? "正在下载  " + ((int)Math.Floor(p.Fraction * 100)).ToString(CultureInfo.InvariantCulture) + "%"
                        : state == UpdateState.Verifying ? "正在校验 SHA-256…"
                        : "正在替换程序文件，完成后会自动重新打开";
                    if (state == UpdateState.Downloading) Icons.Draw(g, Glyph.Download, iconBox, Palette.Fade(Palette.Accent, opacity), s);
                    else Spinner.Draw(g, new PointF(iconBox.X + icon / 2, iconBox.Y + icon / 2), icon - P(2), P(1.75f), Palette.Accent, opacity, now);
                    Typo.DrawLine(g, label, TextStyle.Small, Palette.Ink2, textX, baseline, s, opacity, blur);
                    if (state == UpdateState.Downloading)
                    {
                        string amount = FormatBytes(p.Received) + " / " + FormatBytes(p.Total) + (p.BytesPerSecond > 0 ? "  ·  " + FormatBytes((long)p.BytesPerSecond) + "/s" : "");
                        float w = Typo.Measure(amount, TextStyle.Mono, s);
                        Typo.DrawLine(g, amount, TextStyle.Mono, Palette.Ink3, right - w, Typo.CenterBaseline(row, TextStyle.Mono, s), s, opacity, blur);
                    }
                    PaintBar(g, s, opacity, now, new RectangleF(left, row.Bottom + P(9), right - left, P(4)));
                    break;
                }
                case UpdateState.Ready:
                {
                    float c = icon + P(2);
                    using (var brush = new SolidBrush(Palette.Fade(Palette.Accent, opacity)))
                        g.FillEllipse(brush, iconBox.X - P(1), iconBox.Y - P(1), c, c);
                    float check = P(10);
                    Icons.Draw(g, Glyph.Check, new RectangleF(iconBox.X + (icon - check) / 2, iconBox.Y + (icon - check) / 2, check, check), Palette.Fade(Color.White, opacity), s, 2f);
                    Typo.DrawLine(g, "已下载并通过 SHA-256 校验，重启后即可使用新版本", TextStyle.Small, Palette.Ink2, textX, baseline, s, opacity, blur);
                    PaintBar(g, s, opacity, now, new RectangleF(left, row.Bottom + P(9), right - left, P(4)));
                    break;
                }
                case UpdateState.Failed:
                {
                    Icons.Draw(g, Glyph.Alert, iconBox, Palette.Fade(Palette.Danger, opacity), s);
                    List<string> lines = Typo.Wrap(manager.Error ?? "更新失败", TextStyle.Small, right - textX, s);
                    float lh = Typo.LineHeight(TextStyle.Small, s);
                    for (int i = 0; i < Math.Min(2, lines.Count); i++)
                        Typo.DrawLine(g, lines[i], TextStyle.Small, Palette.Danger, textX, baseline + i * lh, s, opacity, blur);
                    break;
                }
                default:
                    Icons.Draw(g, Glyph.Shield, iconBox, Palette.Fade(Palette.Ink3, opacity), s);
                    Typo.DrawLine(g, "从 GitHub 下载并校验 SHA-256，配置方案和设置都会保留", TextStyle.Small, Palette.Ink3, textX, baseline, s, opacity, blur);
                    break;
            }
        }

        private void PaintBar(Graphics g, float s, double opacity, double now, RectangleF track)
        {
            Shapes.Fill(g, track, track.Height / 2, Palette.Fade(Palette.Surface3, opacity));
            double v = Motion.Clamp01(bar.ValueAt(now));
            if (v <= 0.002) return;
            var fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, (float)(track.Width * v)), track.Height);
            Shapes.Fill(g, fill, track.Height / 2, Palette.Fade(Palette.Accent, opacity));
            // 进行中时在进度条头部扫过一道高光
            if (manager.State == UpdateState.Downloading && !Motion.IsFrozen)
            {
                float sweep = P(64);
                float pos = (float)((now * 0.9 % 1.0) * (fill.Width + sweep)) - sweep;
                var shine = new RectangleF(fill.X + pos, fill.Y, sweep, fill.Height);
                var state = g.Save();
                using (GraphicsPath clip = Shapes.RoundRect(fill, fill.Height / 2))
                using (var brush = new LinearGradientBrush(new RectangleF(shine.X - 1, shine.Y, shine.Width + 2, shine.Height), Color.FromArgb(0, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Horizontal))
                {
                    var blend = new ColorBlend
                    {
                        Positions = new[] { 0f, 0.5f, 1f },
                        Colors = new[] { Color.FromArgb(0, Color.White), Palette.Fade(Color.FromArgb(110, Color.White), opacity), Color.FromArgb(0, Color.White) }
                    };
                    brush.InterpolationColors = blend;
                    g.SetClip(clip, CombineMode.Intersect);
                    g.FillRectangle(brush, shine);
                }
                g.Restore(state);
                FrameClock.Request(this);
            }
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        #endregion

        /// <summary>
        /// 更新内容：浅灰圆角底，分组标题 + 要点；内容多时可滚动，上下边缘淡出
        /// </summary>
        private sealed class NotesView : ScrollSurface
        {
            private const float Pad = 18;
            private const float Indent = 14;

            private readonly List<Placed> placed = new List<Placed>();
            private List<NoteLine> lines = new List<NoteLine>();
            private float layoutWidth = -1;
            private float layoutScale = -1;
            private float contentHeight;

            private struct Placed
            {
                public string Text;
                public TextStyle Style;
                public Color Color;
                public float X;
                public float Y;
                public float Height;
                public bool Dot;
            }

            public NotesView()
            {
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
            }

            public void SetNotes(List<NoteLine> notes)
            {
                lines = notes.Count > 0 ? notes : new List<NoteLine> { new NoteLine(NoteKind.Text, "这个版本没有附带更新说明。") };
                layoutWidth = -1;
                Invalidate();
            }

            protected override float ContentHeight
            {
                get
                {
                    EnsureLayout();
                    return contentHeight;
                }
            }

            /// <summary>
            /// 按当前宽度折行，算出每行的位置（逻辑像素）
            /// </summary>
            private void EnsureLayout()
            {
                float s = S;
                float width = Width / s;
                if (width == layoutWidth && s == layoutScale) return;
                if (width < 80)
                {
                    contentHeight = 0;
                    return;
                }
                layoutWidth = width;
                layoutScale = s;
                placed.Clear();
                float y = 14;
                bool first = true;
                foreach (NoteLine line in lines)
                {
                    if (line.Kind == NoteKind.Heading)
                    {
                        if (!first) y += 10;
                        float h = Typo.LineHeight(TextStyle.BodyMedium, s) / s;
                        placed.Add(new Placed { Text = line.Text, Style = TextStyle.BodyMedium, Color = Palette.Ink, X = Pad, Y = y, Height = h });
                        y += h + 4;
                    }
                    else
                    {
                        float x = Pad + (line.Kind == NoteKind.Bullet ? Indent + line.Indent * Indent : 0);
                        float lh = Typo.LineHeight(TextStyle.Body, s) / s;
                        List<string> wrapped = Typo.Wrap(line.Text, TextStyle.Body, (width - x - Pad - 8) * s, s);
                        for (int i = 0; i < wrapped.Count; i++)
                        {
                            placed.Add(new Placed { Text = wrapped[i], Style = TextStyle.Body, Color = Palette.Ink2, X = x, Y = y, Height = lh, Dot = i == 0 && line.Kind == NoteKind.Bullet });
                            y += lh;
                        }
                        y += 5;
                    }
                    first = false;
                }
                contentHeight = y + 9;
            }

            protected override void OnResize(EventArgs e)
            {
                layoutWidth = -1;
                base.OnResize(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                HandleScrollbarDown(e);
            }

            protected override void PaintContent(Graphics g, double now)
            {
                float s = S;
                double opacity = HostOpacity;
                EnsureLayout();
                var bounds = new RectangleF(0, 0, Width, Height);
                float radius = Px(12);
                Color back = Palette.Surface2;
                Shapes.Fill(g, bounds, radius, Palette.Fade(back, opacity));
                if (opacity < 0.004) return;

                double scroll = ScrollAt(now);
                GraphicsState state = g.Save();
                using (GraphicsPath clip = Shapes.RoundRect(bounds, radius))
                    g.SetClip(clip);
                foreach (Placed p in placed)
                {
                    float y = (float)((p.Y - scroll) * s);
                    float h = p.Height * s;
                    if (y + h < 0 || y > Height) continue;
                    var row = new RectangleF(0, y, Width, h);
                    float baseline = Typo.CenterBaseline(row, p.Style, s);
                    if (p.Dot)
                    {
                        float dot = Px(4);
                        using (var brush = new SolidBrush(Palette.Fade(Palette.Ink3, opacity)))
                            g.FillEllipse(brush, Px(p.X - Indent + 3), y + (h - dot) / 2, dot, dot);
                    }
                    Typo.DrawLine(g, p.Text, p.Style, p.Color, Px(p.X), baseline, s, opacity, 0);
                }

                // 上下边缘淡出，提示还有内容
                float fade = Px(18);
                if (scroll > 0.5) FillFade(g, new RectangleF(0, 0, Width, fade), back, opacity, true);
                if (scroll < MaxScroll - 0.5) FillFade(g, new RectangleF(0, Height - fade, Width, fade), back, opacity, false);
                g.Restore(state);
                PaintScrollbar(g, now, opacity);
            }

            private static void FillFade(Graphics g, RectangleF r, Color color, double opacity, bool top)
            {
                Color solid = Palette.Fade(color, opacity), clear = Color.FromArgb(0, color);
                var gradientRect = new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2);
                using (var brush = new LinearGradientBrush(gradientRect, top ? solid : clear, top ? clear : solid, LinearGradientMode.Vertical))
                    g.FillRectangle(brush, r);
            }
        }
    }
}
