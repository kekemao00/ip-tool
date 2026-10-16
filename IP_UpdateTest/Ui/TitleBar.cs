using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    public enum NoticeKind
    {
        Success,
        Error,
        Warning,
        Info
    }

    /// <summary>
    /// 灵动岛式通知：顶部居中的墨黑胶囊。从 10px 小圆点用 smooth 弹簧展开成消息宽度；
    /// 新消息来时不新建，同一个胶囊改宽度、徽标和文字在模糊里换掉；消失时先收成圆，再缩成点淡出
    /// </summary>
    public sealed class Island
    {
        public const float Top = 8;
        public const float Height = 40;
        private const float BadgeSize = 22;
        private const float PadLeft = 9;
        private const float PadRight = 16;
        private const float Gap = 10;

        private enum Phase
        {
            Hidden,
            Shown,
            Collapsing,
            Dot
        }

        private readonly SpringValue width = new SpringValue(10, Spring.Smooth);
        private readonly SpringValue height = new SpringValue(10, Spring.Smooth);
        private readonly SpringValue alpha = new SpringValue(0, Spring.Snappy);
        private readonly SpringValue contentOpacity = new SpringValue(0, Spring.Snappy);
        private readonly Crossfade<Notice> content = new Crossfade<Notice>(null);
        private readonly Timer timer = new Timer();
        private Phase phase;

        public Island()
        {
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Advance();
            };
        }

        /// <summary>
        /// 需要重绘时触发（宿主据此请求重绘）
        /// </summary>
        public event Action Changed;

        /// <summary>
        /// 显示一条通知。maxWidth 为宿主可用宽度（逻辑像素）
        /// </summary>
        public void Notify(NoticeKind kind, string text, float maxWidth)
        {
            double now = Motion.Now;
            var notice = new Notice(kind, text ?? "", now);
            float textWidth = Typo.Measure(notice.Text, TextStyle.BodyMedium, 1);
            float target = Math.Min(maxWidth, PadLeft + BadgeSize + Gap + textWidth + PadRight);
            notice.Width = target;

            if (phase == Phase.Hidden || phase == Phase.Dot)
            {
                width.Snap(10);
                height.Snap(10);
                alpha.Snap(alpha.ValueAt(now));
                alpha.Set(1, now);
                width.Set(target, now);
                height.Set(Height, now);
                content.Reset(notice);
                contentOpacity.Snap(0);
                // 内容在形状展开到约 58% 之后才出现
                contentOpacity.Set(1, now + 0.12);
            }
            else
            {
                width.Set(target, now);
                height.Set(Height, now);
                alpha.Set(1, now);
                content.Set(notice, now);
                contentOpacity.Set(1, now);
            }
            phase = Phase.Shown;

            // 停留 1.8s + 每字 60ms（最多再加 2.6s）
            double dwell = 1.8 + Math.Min(2.6, 0.06 * notice.Text.Length);
            timer.Stop();
            timer.Interval = (int)(dwell * 1000);
            timer.Start();
            Raise();
        }

        private void Advance()
        {
            double now = Motion.Now;
            if (phase == Phase.Shown)
            {
                // 先收成圆（宽 = 高），内容先于形状消失
                phase = Phase.Collapsing;
                contentOpacity.Set(0, now);
                width.Set(Height, now);
                timer.Interval = 200;
                timer.Start();
            }
            else if (phase == Phase.Collapsing)
            {
                phase = Phase.Dot;
                width.Set(8, now);
                height.Set(8, now);
                alpha.Set(0, now, Spring.Smooth);
            }
            Raise();
        }

        private void Raise()
        {
            if (Changed != null) Changed();
        }

        public bool IsVisibleAt(double now)
        {
            return phase != Phase.Hidden && (alpha.ValueAt(now) > 0.005 || alpha.IsAnimatingAt(now));
        }

        public bool IsAnimatingAt(double now)
        {
            if (phase == Phase.Dot && !alpha.IsAnimatingAt(now) && !width.IsAnimatingAt(now)) phase = Phase.Hidden;
            return width.IsAnimatingAt(now) || height.IsAnimatingAt(now) || alpha.IsAnimatingAt(now)
                || contentOpacity.IsAnimatingAt(now) || content.IsAnimatingAt(now)
                || content.Current != null && now - content.Current.ShownAt < 0.6;
        }

        /// <summary>
        /// 胶囊的范围（设备像素）
        /// </summary>
        public RectangleF Bounds(float hostWidth, float scale, double now)
        {
            float w = (float)width.ValueAt(now) * scale, h = (float)height.ValueAt(now) * scale;
            float cy = (Top + Height / 2) * scale;
            return new RectangleF(hostWidth / 2 - w / 2, cy - h / 2, w, h);
        }

        public void Paint(Graphics g, float hostWidth, float scale, double now)
        {
            if (phase == Phase.Hidden) return;
            double a = Motion.Clamp01(alpha.ValueAt(now));
            if (a < 0.005) return;

            RectangleF r = Bounds(hostWidth, scale, now);
            Shapes.Fill(g, r, r.Height / 2, Palette.Fade(Palette.Ink, a));

            double co = Motion.Clamp01(contentOpacity.ValueAt(now)) * a;
            if (co < 0.005) return;
            var state = g.Save();
            g.SetClip(r);
            foreach (var layer in content.Layers(now))
            {
                Notice notice = layer.Value;
                if (notice == null) continue;
                double la = co * layer.Opacity;
                float dy = (float)layer.OffsetY * scale;
                float badge = BadgeSize * scale;
                var badgeRect = new RectangleF(r.X + PadLeft * scale, r.Y + (r.Height - badge) / 2 + dy, badge, badge);
                Color badgeColor = notice.Kind == NoticeKind.Success ? Palette.Accent
                    : notice.Kind == NoticeKind.Error ? Palette.Danger
                    : Color.FromArgb(46, Color.White);
                using (var brush = new SolidBrush(Palette.Fade(badgeColor, la)))
                    g.FillEllipse(brush, badgeRect);

                float icon = 14 * scale;
                var iconRect = new RectangleF(badgeRect.X + (badge - icon) / 2, badgeRect.Y + (badge - icon) / 2, icon, icon);
                Color white = Palette.Fade(Color.White, la);
                switch (notice.Kind)
                {
                    case NoticeKind.Success:
                        Icons.DrawPartial(g, Glyph.Check, iconRect, white, scale, Motion.EaseOutCubic(Motion.Progress(now, notice.ShownAt + 0.15, 0.28)), 2f);
                        break;
                    case NoticeKind.Error:
                        Icons.Draw(g, Glyph.Close, iconRect, white, scale, 2f);
                        break;
                    case NoticeKind.Warning:
                        Icons.Draw(g, Glyph.Alert, iconRect, white, scale);
                        break;
                    default:
                        Icons.Draw(g, Glyph.Info, iconRect, white, scale);
                        break;
                }

                float textX = badgeRect.Right + Gap * scale;
                float maxText = notice.Width * scale - (PadLeft + BadgeSize + Gap + PadRight) * scale;
                string text = Typo.Ellipsize(notice.Text, TextStyle.BodyMedium, Math.Max(0, maxText), scale);
                float baseline = Typo.CenterBaseline(r, TextStyle.BodyMedium, scale) + dy;
                Typo.DrawLine(g, text, TextStyle.BodyMedium, Color.White, textX, baseline, scale, la, layer.Blur);
            }
            g.Restore(state);
        }

        public sealed class Notice
        {
            public Notice(NoticeKind kind, string text, double shownAt)
            {
                Kind = kind;
                Text = text;
                ShownAt = shownAt;
            }

            public NoticeKind Kind { get; private set; }
            public string Text { get; private set; }
            public double ShownAt { get; private set; }
            public float Width { get; set; }
        }
    }

    /// <summary>
    /// 标题栏里的更新胶囊
    /// </summary>
    public enum UpdateBadgeKind
    {
        None,
        /// <summary>有新版本：强调色圆点</summary>
        Available,
        /// <summary>下载 / 校验中：进度环</summary>
        Progress,
        /// <summary>已就绪：向上箭头</summary>
        Ready,
        /// <summary>失败：红点</summary>
        Failed
    }

    /// <summary>
    /// 窗口顶栏：标识、标题、更新胶囊、命令面板入口、最小化 / 关闭；按住空白处拖动窗口。通知胶囊画在顶栏上
    /// </summary>
    public class TitleBar : SpringControl
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        private readonly List<BarButton> buttons = new List<BarButton>();
        private readonly BarButton search;
        private readonly BarButton update;
        private readonly BarButton minimize;
        private readonly BarButton close;
        private BarButton hovered;
        private BarButton pressed;
        private string chip;
        private string subtitle;

        // 更新胶囊：出现 / 消失淡入淡出，宽度随文字用 smooth 弹簧变化，文字在模糊里换掉
        private readonly SpringValue updateAlpha = new SpringValue(0, Spring.Smooth);
        private readonly SpringValue updateWidth = new SpringValue(0, Spring.Smooth);
        private readonly SpringValue updateProgress = new SpringValue(0, Spring.Smooth);
        private readonly Crossfade<string> updateText = new Crossfade<string>("");
        private UpdateBadgeKind updateKind;
        private UpdateBadgeKind updatePaintedKind;

        public TitleBar(Island island)
        {
            Island = island;
            island.Changed += Animate;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Palette.Canvas;

            search = new BarButton { Glyph = Glyph.Search, Label = "搜索操作", Hint = "Ctrl K", Tip = "搜索并执行操作（Ctrl+K）", Visible = false };
            update = new BarButton { Visible = false };
            minimize = new BarButton { Glyph = Glyph.Minimize, Tip = "最小化" };
            close = new BarButton { Glyph = Glyph.Close, Tip = "关闭" };
            buttons.Add(update);
            buttons.Add(search);
            buttons.Add(minimize);
            buttons.Add(close);
            minimize.Click = () =>
            {
                Form form = FindForm();
                if (form != null) form.WindowState = FormWindowState.Minimized;
            };
            close.Click = () =>
            {
                Form form = FindForm();
                if (form != null) form.Close();
            };
        }

        public Island Island { get; private set; }

        /// <summary>
        /// 命令面板入口被点击
        /// </summary>
        public event EventHandler SearchClicked;

        /// <summary>
        /// 更新胶囊被点击
        /// </summary>
        public event EventHandler UpdateClicked;

        /// <summary>
        /// 显示 / 更新 / 隐藏更新胶囊。progress 为 0..1（只在 Progress 时使用）
        /// </summary>
        public void SetUpdateBadge(UpdateBadgeKind kind, string text, double progress, string tip)
        {
            double now = Motion.Now;
            bool wasShown = updateKind != UpdateBadgeKind.None;
            bool sameKind = kind == updateKind;
            updateKind = kind;
            update.Tip = tip;
            if (kind == UpdateBadgeKind.None)
            {
                update.Visible = false;
                updateAlpha.Set(0, now);
                updateWidth.Set(0, now);
                if (hovered == update) hovered = null;
                Animate();
                return;
            }

            update.Visible = true;
            updatePaintedKind = kind;
            text = text ?? "";
            float target = 11 + 12 + 7 + Typo.Measure(text, TextStyle.Small, 1) + 13;
            if (!wasShown)
            {
                updateText.Reset(text);
                updateWidth.Snap(target * 0.86);
                updateProgress.Snap(Motion.Clamp01(progress));
            }
            else if (sameKind && kind == UpdateBadgeKind.Progress)
            {
                // 下载百分比直接换数字，不做模糊过渡
                updateText.Reset(text);
            }
            else
            {
                updateText.Set(text, now);
            }
            updateWidth.Set(target, now);
            updateAlpha.Set(1, now);
            updateProgress.Set(Motion.Clamp01(progress), now);
            if (hovered == update) InkToolTip.Shared.SetToolTip(this, tip ?? "");
            Animate();
        }

        /// <summary>
        /// 更新胶囊的位置（设备像素），更新面板从它变形展开
        /// </summary>
        public Rectangle UpdateBadgeBounds
        {
            get
            {
                LayoutButtons();
                return update.Visible ? Rectangle.Round(update.Bounds) : Rectangle.Empty;
            }
        }

        public bool ShowSearch
        {
            get { return search.Visible; }
            set
            {
                search.Visible = value;
                Invalidate();
            }
        }

        public bool ShowMinimize
        {
            get { return minimize.Visible; }
            set
            {
                minimize.Visible = value;
                Invalidate();
            }
        }

        /// <summary>
        /// 标题后的灰色说明
        /// </summary>
        public string Subtitle
        {
            get { return subtitle; }
            set
            {
                subtitle = value;
                Invalidate();
            }
        }

        /// <summary>
        /// 标题后的标签（如“管理员”）
        /// </summary>
        public string Chip
        {
            get { return chip; }
            set
            {
                chip = value;
                Invalidate();
            }
        }

        /// <summary>
        /// 命令面板入口按钮的位置（设备像素），面板从它变形展开
        /// </summary>
        public Rectangle SearchBounds
        {
            get
            {
                LayoutButtons();
                return Rectangle.Round(search.Bounds);
            }
        }

        private void LayoutButtons()
        {
            float s = S;
            float x = Width - Px(Metrics.PageGutter - 8);
            float size = Px(32);
            float cy = Height / 2f;
            foreach (BarButton b in new[] { close, minimize })
            {
                if (!b.Visible) continue;
                x -= size;
                b.Bounds = new RectangleF(x, cy - size / 2, size, size);
                x -= Px(4);
            }
            if (search.Visible)
            {
                float w = Px(14 + 14 + 6 + 10 + 12) + Typo.Measure(search.Label, TextStyle.Small, s) + Typo.Measure(search.Hint, TextStyle.Caption, s);
                float h = Px(30);
                x -= Px(8) + w;
                search.Bounds = new RectangleF(x, cy - h / 2, w, h);
            }
            double now = Motion.Now;
            float updateW = Px((float)Math.Max(0, updateWidth.ValueAt(now)));
            if (updateW > 0.5f)
            {
                float h = Px(30);
                x -= Px(8) * (float)Motion.Clamp01(updateAlpha.ValueAt(now)) + updateW;
                update.Bounds = new RectangleF(x, cy - h / 2, updateW, h);
            }
            else update.Bounds = RectangleF.Empty;
        }

        private BarButton HitTest(Point p)
        {
            LayoutButtons();
            foreach (BarButton b in buttons)
                if (b.Visible && b.Bounds.Contains(p)) return b;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            BarButton hit = HitTest(e.Location);
            if (hit == hovered) return;
            if (hovered != null) hovered.Hover.Set(0);
            hovered = hit;
            if (hit != null) hit.Hover.Set(1);
            InkToolTip.Shared.SetToolTip(this, hit == null ? "" : hit.Tip ?? "");
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            Animate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hovered != null) hovered.Hover.Set(0);
            hovered = null;
            InkToolTip.Shared.SetToolTip(this, "");
            Animate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            BarButton hit = HitTest(e.Location);
            if (hit != null)
            {
                pressed = hit;
                hit.Press.Set(1);
                Animate();
                return;
            }
            // 按住空白处拖动窗口，交给系统处理
            Form form = FindForm();
            if (form == null) return;
            ReleaseCapture();
            SendMessage(form.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            BarButton b = pressed;
            pressed = null;
            if (b == null) return;
            b.Press.Set(0);
            Animate();
            if (b.Bounds.Contains(e.Location))
            {
                if (b == search)
                {
                    if (SearchClicked != null) SearchClicked(this, EventArgs.Empty);
                }
                else if (b == update)
                {
                    if (UpdateClicked != null) UpdateClicked(this, EventArgs.Empty);
                }
                else if (b.Click != null) b.Click();
            }
        }

        protected override bool IsAnimating(double now)
        {
            foreach (BarButton b in buttons)
                if (b.Hover.IsAnimatingAt(now) || b.Press.IsAnimatingAt(now)) return true;
            if (updateAlpha.IsAnimatingAt(now) || updateWidth.IsAnimatingAt(now) || updateProgress.IsAnimatingAt(now) || updateText.IsAnimatingAt(now)) return true;
            return Island.IsAnimatingAt(now);
        }

        protected override void PaintContent(Graphics g, double now)
        {
            float s = S;
            g.Clear(Palette.Canvas);
            LayoutButtons();

            // 标识：墨黑圆角方块 + 白色网络图标
            float x = Px(Metrics.PageGutter);
            float mark = Px(24);
            var markRect = new RectangleF(x, (Height - mark) / 2, mark, mark);
            Shapes.Fill(g, markRect, Px(7), Palette.Ink);
            float glyph = Px(14);
            Icons.Draw(g, Glyph.Network, new RectangleF(markRect.X + (mark - glyph) / 2, markRect.Y + (mark - glyph) / 2, glyph, glyph), Color.White, s);
            x = markRect.Right + Px(10);

            float baseline = Typo.CenterBaseline(new RectangleF(0, 0, Width, Height), TextStyle.Title, s);
            Typo.DrawLine(g, Text, TextStyle.Title, Palette.Ink, x, baseline, s);
            x += Typo.Measure(Text, TextStyle.Title, s);
            if (!string.IsNullOrEmpty(subtitle))
            {
                x += Px(8);
                Typo.DrawLine(g, subtitle, TextStyle.Small, Palette.Ink3, x, baseline, s);
                x += Typo.Measure(subtitle, TextStyle.Small, s);
            }
            if (!string.IsNullOrEmpty(chip))
            {
                x += Px(8);
                Chips.Draw(g, chip, x, Height / 2f, s, 1);
            }

            PaintUpdateBadge(g, now, s);

            foreach (BarButton b in buttons)
            {
                if (!b.Visible || b == update) continue;
                double hover = b.Hover.ValueAt(now), press = b.Press.ValueAt(now);
                if (b == search)
                {
                    Color fill = Palette.Mix(Palette.Mix(Palette.Surface, Palette.Surface2, hover), Palette.Surface3, press);
                    Shapes.Fill(g, b.Bounds, Px(Metrics.ButtonRadius), fill);
                    Shapes.Stroke(g, b.Bounds, Px(Metrics.ButtonRadius), Palette.Mix(Palette.Line, Palette.LineStrong, hover), Px(1));
                    float bx = b.Bounds.X + Px(10);
                    float icon = Px(14);
                    Icons.Draw(g, Glyph.Search, new RectangleF(bx, b.Bounds.Y + (b.Bounds.Height - icon) / 2, icon, icon), Palette.Ink2, s);
                    bx += icon + Px(6);
                    float bl = Typo.CenterBaseline(b.Bounds, TextStyle.Small, s);
                    Typo.DrawLine(g, b.Label, TextStyle.Small, Palette.Mix(Palette.Ink2, Palette.Ink, hover), bx, bl, s);
                    float hintWidth = Typo.Measure(b.Hint, TextStyle.Caption, s);
                    Typo.DrawLine(g, b.Hint, TextStyle.Caption, Palette.Ink3, b.Bounds.Right - Px(10) - hintWidth, Typo.CenterBaseline(b.Bounds, TextStyle.Caption, s), s);
                    continue;
                }
                Shapes.Fill(g, b.Bounds, Px(8), Palette.Fade(Palette.Mix(Palette.Surface3, Palette.LineStrong, 0.5 * press), 0.75 * Math.Max(hover, press)));
                float size = Px(16);
                Icons.Draw(g, b.Glyph, new RectangleF(b.Bounds.X + (b.Bounds.Width - size) / 2, b.Bounds.Y + (b.Bounds.Height - size) / 2, size, size),
                    Palette.Mix(Palette.Ink2, Palette.Ink, hover), s);
            }

            Island.Paint(g, Width, s, now);
        }

        /// <summary>
        /// 浅强调色胶囊：前面是状态标记（圆点 / 进度环 / 向上箭头），后面是文字
        /// </summary>
        private void PaintUpdateBadge(Graphics g, double now, float s)
        {
            double alpha = Motion.Clamp01(updateAlpha.ValueAt(now));
            RectangleF r = update.Bounds;
            if (alpha < 0.005 || r.Width < 1) return;
            bool failed = updatePaintedKind == UpdateBadgeKind.Failed;
            Color tint = failed ? Palette.Danger : Palette.Accent;
            Color soft = failed ? Palette.DangerSoft : Palette.AccentSoft;
            double hover = update.Hover.ValueAt(now), press = update.Press.ValueAt(now);
            Color fill = Palette.Mix(soft, Palette.Mix(soft, tint, 0.16), Math.Max(hover * 0.6, press));
            Shapes.Fill(g, r, r.Height / 2, Palette.Fade(fill, alpha));

            var state = g.Save();
            g.SetClip(r);
            float cx = r.X + Px(11 + 6), cy = r.Y + r.Height / 2;
            switch (updatePaintedKind)
            {
                case UpdateBadgeKind.Progress:
                {
                    float d = Px(12);
                    var ring = new RectangleF(cx - d / 2, cy - d / 2, d, d);
                    using (var track = new Pen(Palette.Fade(tint, 0.2 * alpha), Px(1.75f)))
                        g.DrawEllipse(track, ring);
                    double p = Motion.Clamp01(updateProgress.ValueAt(now));
                    if (p > 0.005)
                    {
                        using (var pen = new Pen(Palette.Fade(tint, alpha), Px(1.75f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                            g.DrawArc(pen, ring, -90, (float)(p * 360));
                    }
                    break;
                }
                case UpdateBadgeKind.Ready:
                {
                    float icon = Px(14);
                    Icons.Draw(g, Glyph.ArrowUpCircle, new RectangleF(cx - icon / 2, cy - icon / 2, icon, icon), Palette.Fade(tint, alpha), s);
                    break;
                }
                default:
                {
                    float halo = Px(12), dot = Px(6);
                    using (var brush = new SolidBrush(Palette.Fade(tint, 0.16 * alpha)))
                        g.FillEllipse(brush, cx - halo / 2, cy - halo / 2, halo, halo);
                    using (var brush = new SolidBrush(Palette.Fade(tint, alpha)))
                        g.FillEllipse(brush, cx - dot / 2, cy - dot / 2, dot, dot);
                    break;
                }
            }

            float textX = r.X + Px(11 + 12 + 7);
            foreach (var layer in updateText.Layers(now))
            {
                if (string.IsNullOrEmpty(layer.Value)) continue;
                float baseline = Typo.CenterBaseline(r, TextStyle.Small, s) + (float)layer.OffsetY * s;
                Typo.DrawLine(g, layer.Value, TextStyle.Small, tint, textX, baseline, s, alpha * layer.Opacity, layer.Blur);
            }
            g.Restore(state);
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private sealed class BarButton
        {
            public readonly SpringValue Hover = new SpringValue(0, Spring.Snappy);
            public readonly SpringValue Press = new SpringValue(0, Spring.Snappy);
            public Glyph Glyph;
            public string Label;
            public string Hint;
            public string Tip;
            public bool Visible = true;
            public RectangleF Bounds;
            public Action Click;
        }
    }

    /// <summary>
    /// 标签 chip：高 22 胶囊、白底 line-strong 描边、caption 字号
    /// </summary>
    public static class Chips
    {
        public static float Width(string text, float scale)
        {
            return Typo.Measure(text, TextStyle.Caption, scale) + 16 * scale;
        }

        public static void Draw(Graphics g, string text, float x, float centerY, float scale, double opacity)
        {
            float w = Width(text, scale), h = 22 * scale;
            var r = new RectangleF(x, centerY - h / 2, w, h);
            Shapes.Fill(g, r, h / 2, Palette.Fade(Palette.Surface, opacity));
            Shapes.Stroke(g, r, h / 2, Palette.Fade(Palette.LineStrong, opacity), scale);
            Typo.Draw(g, text, TextStyle.Caption, Palette.Fade(Palette.Ink2, opacity), r, scale, StringAlignment.Center);
        }
    }
}
