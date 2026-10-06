using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 颜色令牌：浅暖灰底、黑白组件、唯一的强调色。组件只引用这里的值
    /// </summary>
    public static class Palette
    {
        /// <summary>窗口背景（浅暖灰）</summary>
        public static readonly Color Canvas = Hex(0xEFEEEA);

        /// <summary>卡片、输入框、表格、面板</summary>
        public static readonly Color Surface = Color.White;

        /// <summary>表面上的悬停、次级填充</summary>
        public static readonly Color Surface2 = Hex(0xF6F5F2);

        /// <summary>按下态、头像底、幽灵按钮悬停</summary>
        public static readonly Color Surface3 = Hex(0xECEBE7);

        /// <summary>主文字、主按钮、指示条、通知胶囊</summary>
        public static readonly Color Ink = Hex(0x141413);

        public static readonly Color InkHover = Hex(0x2B2A28);

        /// <summary>次级文字、未选中标签</summary>
        public static readonly Color Ink2 = Hex(0x5E5B56);

        /// <summary>占位符、提示、表头、禁用文字</summary>
        public static readonly Color Ink3 = Hex(0x9A968F);

        /// <summary>分隔线、默认描边</summary>
        public static readonly Color Line = Hex(0xE3E1DC);

        /// <summary>悬停描边、标签描边</summary>
        public static readonly Color LineStrong = Hex(0xCFCCC5);

        /// <summary>唯一的强调色：焦点描边、选中条、成功徽标</summary>
        public static readonly Color Accent = Hex(0x2F5BFF);

        /// <summary>选中行底色、文字选区</summary>
        public static readonly Color AccentSoft = Hex(0xE8EDFF);

        /// <summary>焦点光环（强调色 22%）</summary>
        public static readonly Color FocusRing = Color.FromArgb(56, Accent);

        /// <summary>只用于危险操作确认和错误</summary>
        public static readonly Color Danger = Hex(0xD93B2B);

        public static readonly Color DangerSoft = Hex(0xFCEBE8);

        /// <summary>面板遮罩：暖黑 26%</summary>
        public static readonly Color Scrim = Color.FromArgb(66, Hex(0x1A1814));

        public static Color Hex(int rgb)
        {
            return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }

        /// <summary>
        /// 按 t 混合两种颜色（含透明度）
        /// </summary>
        public static Color Mix(Color a, Color b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return Color.FromArgb(
                Channel(a.A, b.A, t),
                Channel(a.R, b.R, t),
                Channel(a.G, b.G, t),
                Channel(a.B, b.B, t));
        }

        /// <summary>
        /// 乘上不透明度
        /// </summary>
        public static Color Fade(Color color, double opacity)
        {
            if (opacity >= 1) return color;
            int a = (int)Math.Round(color.A * Math.Max(0, opacity));
            return Color.FromArgb(Math.Min(255, a), color);
        }

        private static int Channel(int a, int b, double t)
        {
            int v = (int)Math.Round(a + (b - a) * t);
            return v < 0 ? 0 : v > 255 ? 255 : v;
        }
    }

    /// <summary>
    /// 尺寸令牌（96 DPI 下的逻辑像素）
    /// </summary>
    public static class Metrics
    {
        public const float ButtonHeight = 34;
        public const float PanelButtonHeight = 36;
        public const float InputHeight = 40;
        public const float PrimaryHeight = 42;
        public const float RowHeight = 46;
        public const float HeaderHeight = 40;
        public const float TabHeight = 40;

        public const float ButtonRadius = 9;
        public const float PrimaryRadius = 11;
        public const float InputRadius = 10;
        public const float PanelRadius = 16;

        public const float PageGutter = 24;
        public const float TitleBarHeight = 56;

        /// <summary>
        /// 输入框四周留给焦点光环和抖动的空间
        /// </summary>
        public const float FieldMargin = 4;
    }

    /// <summary>
    /// 绘图辅助：圆角矩形、胶囊
    /// </summary>
    public static class Shapes
    {
        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float rr = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
            if (rr < 0.5f)
            {
                path.AddRectangle(r);
                return path;
            }
            float d = rr * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Fill(Graphics g, RectangleF r, float radius, Color color)
        {
            if (color.A == 0 || r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath path = RoundRect(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        /// <summary>
        /// 在形状内侧描边（描边不超出 r）
        /// </summary>
        public static void Stroke(Graphics g, RectangleF r, float radius, Color color, float width)
        {
            if (color.A == 0 || width <= 0 || r.Width <= 0 || r.Height <= 0) return;
            float h = width / 2;
            RectangleF inner = RectangleF.Inflate(r, -h, -h);
            using (GraphicsPath path = RoundRect(inner, Math.Max(0, radius - h)))
            using (var pen = new Pen(color, width))
                g.DrawPath(pen, path);
        }

        public static RectangleF Lerp(RectangleF a, RectangleF b, double t)
        {
            return RectangleF.FromLTRB(
                (float)Motion.Lerp(a.Left, b.Left, t),
                (float)Motion.Lerp(a.Top, b.Top, t),
                (float)Motion.Lerp(a.Right, b.Right, t),
                (float)Motion.Lerp(a.Bottom, b.Bottom, t));
        }

        /// <summary>
        /// 抗锯齿、高质量绘图设置
        /// </summary>
        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }
    }
}
