using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 分段标签：白色胶囊轨道，选中的是墨黑胶囊。指示条左右边各一个弹簧，前沿先走、后沿后跟，移动中被拉长；
    /// 可以直接拖动，松手带速度弹到最近的标签。标签文字在指示条内部被裁成白色
    /// </summary>
    public class Segmented : SpringControl
    {
        private const float Pad = 3;
        private const float Gap = 2;
        private const float ItemPadding = 13;
        private const double MaxFlingVelocity = 2500;

        private readonly List<string> texts = new List<string>();
        private readonly List<bool> enabled = new List<bool>();

        // 逻辑像素，相对控件左边
        private readonly SpringValue left = new SpringValue(0, Spring.Default);
        private readonly SpringValue right = new SpringValue(0, Spring.Default);
        private readonly SpringValue hoverX = new SpringValue(0, Spring.Default);
        private readonly SpringValue hoverW = new SpringValue(0, Spring.Default);
        private readonly SpringValue hoverAlpha = new SpringValue(0, Spring.Snappy);
        private int hoverIndex = -1;
        private int selected;
        private bool placed;

        private bool mouseDown;
        private bool dragging;
        private float downX;
        private float grabOffset;
        private readonly List<KeyValuePair<double, float>> samples = new List<KeyValuePair<double, float>>();

        public Segmented()
        {
            SetStyle(ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
        }

        public event EventHandler SelectedIndexChanged;

        /// <summary>
        /// 为 true 时各标签等宽铺满，否则按文字宽度
        /// </summary>
        public bool Fill { get; set; } = true;

        public int Count
        {
            get { return texts.Count; }
        }

        public void SetItems(params string[] items)
        {
            texts.Clear();
            enabled.Clear();
            texts.AddRange(items);
            foreach (string _ in items) enabled.Add(true);
            selected = Math.Min(selected, Math.Max(0, texts.Count - 1));
            placed = false;
            Invalidate();
        }

        public void SetItemEnabled(int index, bool value)
        {
            if (index < 0 || index >= enabled.Count || enabled[index] == value) return;
            enabled[index] = value;
            Invalidate();
        }

        public bool IsItemEnabled(int index)
        {
            return index >= 0 && index < enabled.Count && enabled[index];
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { Select(value, null); }
        }

        /// <summary>
        /// 按文字宽度时的总宽（设备像素）
        /// </summary>
        public int PreferredWidth
        {
            get
            {
                float w = Pad * 2 + Gap * Math.Max(0, texts.Count - 1);
                foreach (string t in texts) w += Typo.Measure(t, TextStyle.Label, S) / S + ItemPadding * 2;
                return (int)Math.Ceiling(w * S);
            }
        }

        #region 几何（逻辑像素）

        private float LogicalWidth
        {
            get { return Width / S; }
        }

        private float LogicalHeight
        {
            get { return Height / S; }
        }

        private void ItemSpan(int index, out float x, out float w)
        {
            int n = Math.Max(1, texts.Count);
            if (Fill)
            {
                w = (LogicalWidth - Pad * 2 - Gap * (n - 1)) / n;
                x = Pad + index * (w + Gap);
                return;
            }
            x = Pad;
            for (int i = 0; i < index; i++) x += ItemWidth(i) + Gap;
            w = ItemWidth(index);
        }

        private float ItemWidth(int index)
        {
            return Typo.Measure(texts[index], TextStyle.Label, S) / S + ItemPadding * 2;
        }

        private int ItemAt(float logicalX)
        {
            for (int i = 0; i < texts.Count; i++)
            {
                float x, w;
                ItemSpan(i, out x, out w);
                if (logicalX >= x - Gap / 2 && logicalX < x + w + Gap / 2) return i;
            }
            return -1;
        }

        private void PlaceIndicator()
        {
            if (texts.Count == 0) return;
            float x, w;
            ItemSpan(selected, out x, out w);
            left.Snap(x);
            right.Snap(x + w);
            placed = true;
        }

        #endregion

        #region 选择

        /// <summary>
        /// 选中标签；velocity 为松手时的速度（逻辑像素/秒）
        /// </summary>
        private void Select(int index, double? velocity)
        {
            if (index < 0 || index >= texts.Count) return;
            bool changed = index != selected;
            selected = index;
            if (!IsHandleCreated || !placed)
            {
                PlaceIndicator();
            }
            else
            {
                double now = Motion.Now;
                float x, w;
                ItemSpan(index, out x, out w);
                double currentLeft = left.ValueAt(now);
                bool movingRight = x > currentLeft;
                // 前沿用 lead、后沿用 trail
                left.Set(x, now, movingRight ? Spring.Trail : Spring.Lead);
                right.Set(x + w, now, movingRight ? Spring.Lead : Spring.Trail);
                if (velocity.HasValue)
                {
                    left.Kick(velocity.Value, now, movingRight ? Spring.Trail : Spring.Lead);
                    right.Kick(velocity.Value, now, movingRight ? Spring.Lead : Spring.Trail);
                }
            }
            Animate();
            if (changed && SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        private int NearestEnabled(float centerX)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < texts.Count; i++)
            {
                if (!enabled[i]) continue;
                float x, w;
                ItemSpan(i, out x, out w);
                float d = Math.Abs(x + w / 2 - centerX);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }

        #endregion

        #region 鼠标与键盘

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PlaceIndicator();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            PlaceIndicator();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            float x = e.X / S;
            if (mouseDown)
            {
                if (!dragging && Math.Abs(x - downX) > 3 && grabOffset >= 0) dragging = true;
                if (dragging) Drag(x);
                return;
            }
            UpdateHover(ItemAt(x));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!mouseDown) UpdateHover(-1);
        }

        private void UpdateHover(int index)
        {
            if (index >= 0 && !enabled[index]) index = -1;
            if (index == hoverIndex) return;
            hoverIndex = index;
            double now = Motion.Now;
            if (index < 0)
            {
                hoverAlpha.Set(0, now);
            }
            else
            {
                float x, w;
                ItemSpan(index, out x, out w);
                // 第一次出现时不滑，直接出现再淡入
                if (hoverAlpha.ValueAt(now) < 0.05)
                {
                    hoverX.Snap(x);
                    hoverW.Snap(w);
                }
                else
                {
                    hoverX.Set(x, now);
                    hoverW.Set(w, now);
                }
                hoverAlpha.Set(1, now);
            }
            Animate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            Focus();
            mouseDown = true;
            dragging = false;
            downX = e.X / S;
            double now = Motion.Now;
            float l = (float)left.ValueAt(now), r = (float)right.ValueAt(now);
            grabOffset = downX >= l && downX <= r ? downX - l : -1;
            samples.Clear();
            samples.Add(new KeyValuePair<double, float>(now, downX));
        }

        /// <summary>
        /// 拖动中位置跟手，宽度在相邻标签宽度之间插值
        /// </summary>
        private void Drag(float x)
        {
            double now = Motion.Now;
            samples.Add(new KeyValuePair<double, float>(now, x));
            samples.RemoveAll(p => now - p.Key > 0.08);

            int n = texts.Count;
            float firstX, firstW, lastX, lastW;
            ItemSpan(0, out firstX, out firstW);
            ItemSpan(n - 1, out lastX, out lastW);
            float width = (float)(right.ValueAt(now) - left.ValueAt(now));
            float l = x - grabOffset;
            float center = l + width / 2;

            // 按指示条中心在各标签中心之间的位置插值宽度
            float newWidth = firstW;
            for (int i = 0; i < n - 1; i++)
            {
                float ax, aw, bx, bw;
                ItemSpan(i, out ax, out aw);
                ItemSpan(i + 1, out bx, out bw);
                float ca = ax + aw / 2, cb = bx + bw / 2;
                if (center >= ca && center <= cb)
                {
                    newWidth = (float)Motion.Lerp(aw, bw, (center - ca) / (cb - ca));
                    break;
                }
                if (center > cb) newWidth = bw;
            }
            l = center - newWidth / 2;
            l = Math.Max(firstX, Math.Min(lastX + lastW - newWidth, l));
            left.Snap(l);
            right.Snap(l + newWidth);
            Animate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!mouseDown) return;
            mouseDown = false;
            float x = e.X / S;
            if (dragging)
            {
                dragging = false;
                double now = Motion.Now;
                double velocity = 0;
                if (samples.Count >= 2)
                {
                    var first = samples[0];
                    var last = samples[samples.Count - 1];
                    double dt = last.Key - first.Key;
                    if (dt > 0.001) velocity = (last.Value - first.Value) / dt;
                }
                velocity = Math.Max(-MaxFlingVelocity, Math.Min(MaxFlingVelocity, velocity));
                double l = left.ValueAt(now), r = right.ValueAt(now);
                float center = (float)((l + r) / 2 + velocity * 0.08);
                int target = NearestEnabled(center);
                left.Snap(l);
                right.Snap(r);
                if (target < 0) target = selected;
                bool changed = target != selected;
                selected = target;
                float tx, tw;
                ItemSpan(target, out tx, out tw);
                bool movingRight = tx > l;
                left.Set(tx, now, movingRight ? Spring.Trail : Spring.Lead);
                right.Set(tx + tw, now, movingRight ? Spring.Lead : Spring.Trail);
                left.Kick(velocity, now, movingRight ? Spring.Trail : Spring.Lead);
                right.Kick(velocity, now, movingRight ? Spring.Lead : Spring.Trail);
                Animate();
                if (changed && SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                return;
            }

            int index = ItemAt(x);
            if (index >= 0 && enabled[index] && ClientRectangle.Contains(e.Location)) Select(index, null);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
            if (step == 0) return;
            for (int i = selected + step; i >= 0 && i < texts.Count; i += step)
            {
                if (enabled[i])
                {
                    Select(i, null);
                    break;
                }
            }
            e.Handled = true;
        }

        #endregion

        #region 绘制

        protected override bool IsAnimating(double now)
        {
            return base.IsAnimating(now) || left.IsAnimatingAt(now) || right.IsAnimatingAt(now)
                || hoverX.IsAnimatingAt(now) || hoverW.IsAnimatingAt(now) || hoverAlpha.IsAnimatingAt(now);
        }

        protected override void PaintContent(Graphics g, double now)
        {
            if (!placed) PlaceIndicator();
            float s = S;
            double opacity = (Enabled ? 1 : 0.5) * HostOpacity;
            var track = new RectangleF(0, 0, Width, Height);
            float trackRadius = track.Height / 2;
            Shapes.Fill(g, track, trackRadius, Palette.Fade(Palette.Surface, opacity));
            Shapes.Stroke(g, track, trackRadius, Palette.Fade(Palette.Line, opacity), Px(1));

            float innerH = Height - Px(Pad) * 2;
            double ha = hoverAlpha.ValueAt(now);
            if (ha > 0.01 && Enabled)
            {
                var hover = new RectangleF((float)hoverX.ValueAt(now) * s, Px(Pad), (float)hoverW.ValueAt(now) * s, innerH);
                Shapes.Fill(g, hover, innerH / 2, Palette.Fade(Palette.Surface3, ha * opacity));
            }

            // 第一遍：轨道上的文字
            for (int i = 0; i < texts.Count; i++)
            {
                Color color = enabled[i] ? (i == hoverIndex ? Palette.Ink : Palette.Ink2) : Palette.Fade(Palette.Ink3, 0.7);
                DrawItemText(g, i, color, opacity, s);
            }

            var indicator = new RectangleF((float)left.ValueAt(now) * s, Px(Pad), (float)(right.ValueAt(now) - left.ValueAt(now)) * s, innerH);
            using (GraphicsPath path = Shapes.RoundRect(indicator, innerH / 2))
            {
                using (var brush = new SolidBrush(Palette.Fade(Palette.Ink, opacity)))
                    g.FillPath(brush, path);

                // 第二遍：用指示条裁剪，文字画成白色
                GraphicsState state = g.Save();
                g.SetClip(path);
                for (int i = 0; i < texts.Count; i++) DrawItemText(g, i, Color.White, opacity, s);
                g.Restore(state);
            }

            if (KeyboardFocused) Shapes.Stroke(g, track, trackRadius, Palette.Fade(Palette.Accent, FocusT.ValueAt(now) * opacity), Px(2));
        }

        private void DrawItemText(Graphics g, int index, Color color, double opacity, float s)
        {
            float x, w;
            ItemSpan(index, out x, out w);
            var rect = new RectangleF(x * s, 0, w * s, Height);
            Typo.Draw(g, texts[index], TextStyle.Label, Palette.Fade(color, opacity), rect, s, StringAlignment.Center);
        }

        #endregion
    }

    /// <summary>
    /// 开关：圆点左右两边用 lead / trail 两个弹簧，切换时被拉长。文字在右侧，点文字也能切换
    /// </summary>
    public class Toggle : SpringControl
    {
        private const float TrackWidth = 34;
        private const float TrackHeight = 20;
        private const float Knob = 14;
        private const float Inset = 3;

        private readonly SpringValue knobLeft = new SpringValue(Inset, Spring.Lead);
        private readonly SpringValue knobRight = new SpringValue(Inset + Knob, Spring.Lead);
        private readonly SpringValue on = new SpringValue(0, Spring.Snappy);
        private bool isChecked;

        public Toggle()
        {
            SetStyle(ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            Cursor = Cursors.Hand;
        }

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                double now = Motion.Now;
                float l = value ? TrackWidth - Inset - Knob : Inset;
                if (IsHandleCreated && Visible)
                {
                    knobLeft.Set(l, now, value ? Spring.Trail : Spring.Lead);
                    knobRight.Set(l + Knob, now, value ? Spring.Lead : Spring.Trail);
                    on.Set(value ? 1 : 0, now);
                }
                else
                {
                    knobLeft.Snap(l);
                    knobRight.Snap(l + Knob);
                    on.Snap(value ? 1 : 0);
                }
                Animate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public int PreferredWidth
        {
            get { return (int)Math.Ceiling(Px(TrackWidth + 8) + Typo.Measure(Text, TextStyle.Small, S)); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && Enabled && ClientRectangle.Contains(e.Location)) Checked = !Checked;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space) Checked = !Checked;
        }

        protected override bool IsAnimating(double now)
        {
            return base.IsAnimating(now) || knobLeft.IsAnimatingAt(now) || knobRight.IsAnimatingAt(now) || on.IsAnimatingAt(now);
        }

        protected override void PaintContent(Graphics g, double now)
        {
            float s = S;
            double opacity = (Enabled ? 1 : 0.5) * HostOpacity;
            double hover = HoverT.ValueAt(now), t = Motion.Clamp01(on.ValueAt(now));
            var track = new RectangleF(0, (Height - Px(TrackHeight)) / 2, Px(TrackWidth), Px(TrackHeight));
            Color off = Palette.Mix(Palette.LineStrong, Palette.Ink3, 0.4 * hover);
            Color onColor = Palette.Mix(Palette.Ink, Palette.InkHover, hover);
            Shapes.Fill(g, track, track.Height / 2, Palette.Fade(Palette.Mix(off, onColor, t), opacity));

            var knob = new RectangleF(track.X + (float)knobLeft.ValueAt(now) * s, track.Y + Px(Inset),
                (float)(knobRight.ValueAt(now) - knobLeft.ValueAt(now)) * s, Px(Knob));
            Shapes.Fill(g, knob, knob.Height / 2, Palette.Fade(Color.White, opacity));

            if (KeyboardFocused) Shapes.Stroke(g, RectangleF.Inflate(track, Px(2), Px(2)), track.Height / 2 + Px(2), Palette.Fade(Palette.Accent, FocusT.ValueAt(now) * opacity), Px(2));

            var textRect = new RectangleF(track.Right + Px(8), 0, Width - track.Right - Px(8), Height);
            Typo.Draw(g, Text, TextStyle.Small, Palette.Fade(Palette.Mix(Palette.Ink2, Palette.Ink, hover), opacity), textRect, s);
        }
    }
}
