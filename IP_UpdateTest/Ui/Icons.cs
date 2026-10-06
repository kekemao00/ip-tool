using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace IP_UpdateTest.Ui
{
    public enum Glyph
    {
        None,
        Check,
        Close,
        ChevronDown,
        ChevronRight,
        Search,
        Minimize,
        Plus,
        Refresh,
        More,
        Shield,
        Alert,
        Info,
        Activity,
        FileText,
        Layers,
        Gauge,
        Undo,
        Power,
        Copy,
        Download,
        Upload,
        Pencil,
        Trash,
        ArrowUp,
        ArrowDown,
        Enter,
        Network,
        Globe,
        Server,
        Router,
        Hash,
        Monitor,
        Tag,
        ExternalLink,
        Wifi,
        Inbox,
        LogOut,
        AppWindow,
        Tray,
        Zap,
        Command,
        Save,
        Link,
        Minus
    }

    /// <summary>
    /// 线性图标：24×24 网格、1.5px 描边、圆头圆角、只描不填。缩放时只缩形状，线宽在任何尺寸都是 1.5px
    /// </summary>
    public static class Icons
    {
        private static readonly Dictionary<Glyph, string[]> Paths = new Dictionary<Glyph, string[]>
        {
            { Glyph.Check, new[] { "M20 6 9 17l-5-5" } },
            { Glyph.Close, new[] { "M18 6 6 18", "m6 6 12 12" } },
            { Glyph.ChevronDown, new[] { "m6 9 6 6 6-6" } },
            { Glyph.ChevronRight, new[] { "m9 18 6-6-6-6" } },
            { Glyph.Search, new[] { "circle 11 11 7", "m21 21-4.3-4.3" } },
            { Glyph.Minimize, new[] { "M6 12h12" } },
            { Glyph.Minus, new[] { "M5 12h14" } },
            { Glyph.Plus, new[] { "M5 12h14", "M12 5v14" } },
            { Glyph.Refresh, new[] { "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8", "M21 3v5h-5", "M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16", "M8 16H3v5" } },
            { Glyph.More, new[] { "circle 12 12 1", "circle 19 12 1", "circle 5 12 1" } },
            { Glyph.Shield, new[] { "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z" } },
            { Glyph.Alert, new[] { "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3", "M12 9v4", "M12 17h.01" } },
            { Glyph.Info, new[] { "circle 12 12 10", "M12 16v-4", "M12 8h.01" } },
            { Glyph.Activity, new[] { "M22 12h-4l-3 9L9 3l-3 9H2" } },
            { Glyph.FileText, new[] { "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z", "M14 2v4a2 2 0 0 0 2 2h4", "M10 9H8", "M16 13H8", "M16 17H8" } },
            { Glyph.Layers, new[] { "M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z", "m22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65", "m22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65" } },
            { Glyph.Gauge, new[] { "m12 14 4-4", "M3.34 19a10 10 0 1 1 17.32 0" } },
            { Glyph.Undo, new[] { "M9 14 4 9l5-5", "M4 9h10.5a5.5 5.5 0 0 1 0 11H11" } },
            { Glyph.Power, new[] { "M12 2v10", "M18.4 6.6a9 9 0 1 1-12.77.04" } },
            { Glyph.Copy, new[] { "rect 8 8 14 14 2", "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" } },
            { Glyph.Download, new[] { "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "m7 10 5 5 5-5", "M12 15V3" } },
            { Glyph.Upload, new[] { "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "m17 8-5-5-5 5", "M12 3v12" } },
            { Glyph.Pencil, new[] { "M21.17 6.81a1 1 0 0 0-3.99-3.99L3.84 16.17a2 2 0 0 0-.5.83l-1.32 4.35a.5.5 0 0 0 .62.62l4.35-1.32a2 2 0 0 0 .83-.5z", "m15 5 4 4" } },
            { Glyph.Trash, new[] { "M3 6h18", "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" } },
            { Glyph.ArrowUp, new[] { "m5 12 7-7 7 7", "M12 19V5" } },
            { Glyph.ArrowDown, new[] { "M12 5v14", "m19 12-7 7-7-7" } },
            { Glyph.Enter, new[] { "M20 4v7a4 4 0 0 1-4 4H4", "m9 10-5 5 5 5" } },
            { Glyph.Network, new[] { "rect 16 16 6 6 1", "rect 2 16 6 6 1", "rect 9 2 6 6 1", "M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3", "M12 12V8" } },
            { Glyph.Globe, new[] { "circle 12 12 10", "M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20", "M2 12h20" } },
            { Glyph.Server, new[] { "rect 2 2 20 8 2", "rect 2 14 20 8 2", "M6 6h.01", "M6 18h.01" } },
            { Glyph.Router, new[] { "rect 2 14 20 8 2", "M6.01 18H6", "M10.01 18H10", "M15 10v4", "M17.84 7.17a4 4 0 0 0-5.66 0", "M20.66 4.34a8 8 0 0 0-11.31 0" } },
            { Glyph.Hash, new[] { "M4 9h16", "M4 15h16", "M10 3 8 21", "M16 3l-2 18" } },
            { Glyph.Monitor, new[] { "rect 2 3 20 14 2", "M8 21h8", "M12 17v4" } },
            { Glyph.Tag, new[] { "M12.59 2.59A2 2 0 0 0 11.17 2H4a2 2 0 0 0-2 2v7.17a2 2 0 0 0 .59 1.42l8.7 8.7a2.43 2.43 0 0 0 3.42 0l6.58-6.58a2.43 2.43 0 0 0 0-3.42z", "M7.5 7.5h.01" } },
            { Glyph.ExternalLink, new[] { "M15 3h6v6", "M10 14 21 3", "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" } },
            { Glyph.Wifi, new[] { "M12 20h.01", "M2 8.82a15 15 0 0 1 20 0", "M5 12.86a10 10 0 0 1 14 0", "M8.5 16.43a5 5 0 0 1 7 0" } },
            { Glyph.Inbox, new[] { "M22 12h-6l-2 3h-4l-2-3H2", "M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" } },
            { Glyph.LogOut, new[] { "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4", "m16 17 5-5-5-5", "M21 12H9" } },
            { Glyph.AppWindow, new[] { "rect 2 4 20 16 2", "M10 4v4", "M2 8h20", "M6 4v4" } },
            { Glyph.Tray, new[] { "M12 15V3", "m7 10 5 5 5-5", "M19 21H5" } },
            { Glyph.Zap, new[] { "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z" } },
            { Glyph.Command, new[] { "M15 6v12a3 3 0 1 0 3-3H6a3 3 0 1 0 3 3V6a3 3 0 1 0-3 3h12a3 3 0 1 0-3-3" } },
            { Glyph.Save, new[] { "M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z", "M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7", "M7 3v4a1 1 0 0 0 1 1h7" } },
            { Glyph.Link, new[] { "M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71", "M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" } }
        };

        private static readonly Dictionary<Glyph, GraphicsPath> Cache = new Dictionary<Glyph, GraphicsPath>();

        /// <summary>
        /// 在正方形 box 中画图标。scale 为 DPI 缩放，决定线宽
        /// </summary>
        public static void Draw(Graphics g, Glyph icon, RectangleF box, Color color, float scale, float strokeWidth = 1.5f)
        {
            GraphicsPath path = PathOf(icon);
            if (path == null || color.A == 0 || box.Width <= 0) return;

            float unit = box.Width / 24f;
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(box.X, box.Y);
            g.ScaleTransform(unit, unit);
            using (var pen = new Pen(color, strokeWidth * scale / unit))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, path);
            }
            g.Restore(state);
        }

        /// <summary>
        /// 只画路径的前 progress 部分（对勾“画出来”），按各段长度比例裁剪
        /// </summary>
        public static void DrawPartial(Graphics g, Glyph icon, RectangleF box, Color color, float scale, double progress, float strokeWidth = 1.5f)
        {
            if (progress >= 1)
            {
                Draw(g, icon, box, color, scale, strokeWidth);
                return;
            }
            if (progress <= 0) return;

            GraphicsPath source = PathOf(icon);
            if (source == null) return;
            using (var flat = (GraphicsPath)source.Clone())
            {
                flat.Flatten(null, 0.05f);
                PointF[] points = flat.PathPoints;
                byte[] types = flat.PathTypes;
                double total = 0;
                for (int i = 1; i < points.Length; i++)
                    if ((types[i] & 0x07) != 0) total += Distance(points[i - 1], points[i]);
                double remaining = total * progress;

                float unit = box.Width / 24f;
                GraphicsState state = g.Save();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(box.X, box.Y);
                g.ScaleTransform(unit, unit);
                using (var pen = new Pen(color, strokeWidth * scale / unit))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    var polyline = new List<PointF>();
                    for (int i = 0; i < points.Length && remaining > 0; i++)
                    {
                        bool start = (types[i] & 0x07) == 0;
                        if (start)
                        {
                            if (polyline.Count > 1) g.DrawLines(pen, polyline.ToArray());
                            polyline.Clear();
                            polyline.Add(points[i]);
                            continue;
                        }
                        double d = Distance(points[i - 1], points[i]);
                        if (d >= remaining)
                        {
                            float t = (float)(remaining / d);
                            polyline.Add(new PointF(points[i - 1].X + (points[i].X - points[i - 1].X) * t, points[i - 1].Y + (points[i].Y - points[i - 1].Y) * t));
                            remaining = 0;
                            break;
                        }
                        remaining -= d;
                        polyline.Add(points[i]);
                    }
                    if (polyline.Count > 1) g.DrawLines(pen, polyline.ToArray());
                }
                g.Restore(state);
            }
        }

        /// <summary>
        /// 生成图标位图（菜单项用）
        /// </summary>
        public static Bitmap ToBitmap(Glyph icon, int sizePx, Color color, float scale)
        {
            var bitmap = new Bitmap(sizePx, sizePx, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                Shapes.Prepare(g);
                Draw(g, icon, new RectangleF(0, 0, sizePx, sizePx), color, scale);
            }
            return bitmap;
        }

        private static double Distance(PointF a, PointF b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        internal static GraphicsPath PathOf(Glyph icon)
        {
            GraphicsPath path;
            if (Cache.TryGetValue(icon, out path)) return path;
            string[] parts;
            if (!Paths.TryGetValue(icon, out parts)) return null;

            path = new GraphicsPath();
            foreach (string part in parts) AddPart(path, part);
            Cache[icon] = path;
            return path;
        }

        internal static IEnumerable<Glyph> All
        {
            get { return Paths.Keys; }
        }

        private static void AddPart(GraphicsPath path, string part)
        {
            path.StartFigure();
            if (part.StartsWith("circle ", StringComparison.Ordinal))
            {
                float[] v = Numbers(part.Substring(7));
                path.AddEllipse(v[0] - v[2], v[1] - v[2], v[2] * 2, v[2] * 2);
                return;
            }
            if (part.StartsWith("rect ", StringComparison.Ordinal))
            {
                float[] v = Numbers(part.Substring(5));
                using (GraphicsPath rect = Shapes.RoundRect(new RectangleF(v[0], v[1], v[2], v[3]), v.Length > 4 ? v[4] : 0))
                    path.AddPath(rect, false);
                return;
            }
            new SvgPath(path, part).Run();
        }

        private static float[] Numbers(string text)
        {
            string[] tokens = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var values = new float[tokens.Length];
            for (int i = 0; i < tokens.Length; i++) values[i] = float.Parse(tokens[i], CultureInfo.InvariantCulture);
            return values;
        }

        /// <summary>
        /// SVG path 数据解析（M L H V C S Q A Z，大小写）
        /// </summary>
        private sealed class SvgPath
        {
            private readonly GraphicsPath path;
            private readonly string data;
            private int pos;
            private PointF current, start, lastControl;
            private char lastCommand;

            public SvgPath(GraphicsPath path, string data)
            {
                this.path = path;
                this.data = data;
            }

            public void Run()
            {
                char command = 'M';
                while (true)
                {
                    SkipSeparators();
                    if (pos >= data.Length) break;
                    char c = data[pos];
                    if (char.IsLetter(c))
                    {
                        command = c;
                        pos++;
                    }
                    else if (command == 'M') command = 'L';
                    else if (command == 'm') command = 'l';
                    Execute(command);
                    lastCommand = command;
                }
            }

            private void Execute(char command)
            {
                bool rel = char.IsLower(command);
                float ox = rel ? current.X : 0, oy = rel ? current.Y : 0;
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        current = new PointF(ox + Number(), oy + Number());
                        start = current;
                        path.StartFigure();
                        break;
                    case 'L':
                        LineTo(new PointF(ox + Number(), oy + Number()));
                        break;
                    case 'H':
                        LineTo(new PointF(ox + Number(), current.Y));
                        break;
                    case 'V':
                        LineTo(new PointF(current.X, oy + Number()));
                        break;
                    case 'C':
                    {
                        var c1 = new PointF(ox + Number(), oy + Number());
                        var c2 = new PointF(ox + Number(), oy + Number());
                        var end = new PointF(ox + Number(), oy + Number());
                        Cubic(c1, c2, end);
                        break;
                    }
                    case 'S':
                    {
                        char last = char.ToUpperInvariant(lastCommand);
                        PointF c1 = last == 'C' || last == 'S'
                            ? new PointF(2 * current.X - lastControl.X, 2 * current.Y - lastControl.Y)
                            : current;
                        var c2 = new PointF(ox + Number(), oy + Number());
                        var end = new PointF(ox + Number(), oy + Number());
                        Cubic(c1, c2, end);
                        break;
                    }
                    case 'Q':
                    {
                        var q = new PointF(ox + Number(), oy + Number());
                        var end = new PointF(ox + Number(), oy + Number());
                        var c1 = new PointF(current.X + 2f / 3 * (q.X - current.X), current.Y + 2f / 3 * (q.Y - current.Y));
                        var c2 = new PointF(end.X + 2f / 3 * (q.X - end.X), end.Y + 2f / 3 * (q.Y - end.Y));
                        Cubic(c1, c2, end);
                        break;
                    }
                    case 'A':
                    {
                        float rx = Number(), ry = Number(), rotation = Number();
                        bool large = Flag(), sweep = Flag();
                        var end = new PointF(ox + Number(), oy + Number());
                        Arc(rx, ry, rotation, large, sweep, end);
                        break;
                    }
                    case 'Z':
                        path.CloseFigure();
                        current = start;
                        break;
                    default:
                        pos = data.Length;
                        break;
                }
            }

            private void LineTo(PointF p)
            {
                path.AddLine(current, p);
                current = p;
            }

            private void Cubic(PointF c1, PointF c2, PointF end)
            {
                path.AddBezier(current, c1, c2, end);
                lastControl = c2;
                current = end;
            }

            /// <summary>
            /// SVG 椭圆弧转换为若干段三次贝塞尔曲线
            /// </summary>
            private void Arc(float rx, float ry, float rotationDeg, bool large, bool sweep, PointF end)
            {
                if (rx == 0 || ry == 0)
                {
                    LineTo(end);
                    return;
                }
                double phi = rotationDeg * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
                double dx = (current.X - end.X) / 2, dy = (current.Y - end.Y) / 2;
                double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
                double arx = Math.Abs(rx), ary = Math.Abs(ry);
                double lambda = x1 * x1 / (arx * arx) + y1 * y1 / (ary * ary);
                if (lambda > 1)
                {
                    arx *= Math.Sqrt(lambda);
                    ary *= Math.Sqrt(lambda);
                }
                double num = arx * arx * ary * ary - arx * arx * y1 * y1 - ary * ary * x1 * x1;
                double den = arx * arx * y1 * y1 + ary * ary * x1 * x1;
                double coef = (large != sweep ? 1 : -1) * Math.Sqrt(Math.Max(0, num / den));
                double cx1 = coef * arx * y1 / ary, cy1 = -coef * ary * x1 / arx;
                double cx = cos * cx1 - sin * cy1 + (current.X + end.X) / 2;
                double cy = sin * cx1 + cos * cy1 + (current.Y + end.Y) / 2;

                double theta1 = Angle(1, 0, (x1 - cx1) / arx, (y1 - cy1) / ary);
                double delta = Angle((x1 - cx1) / arx, (y1 - cy1) / ary, (-x1 - cx1) / arx, (-y1 - cy1) / ary);
                if (!sweep && delta > 0) delta -= 2 * Math.PI;
                else if (sweep && delta < 0) delta += 2 * Math.PI;

                int segments = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2));
                double step = delta / segments;
                double k = 4.0 / 3 * Math.Tan(step / 4);
                for (int i = 0; i < segments; i++)
                {
                    double a1 = theta1 + i * step, a2 = a1 + step;
                    double c1x = Math.Cos(a1) - k * Math.Sin(a1), c1y = Math.Sin(a1) + k * Math.Cos(a1);
                    double c2x = Math.Cos(a2) + k * Math.Sin(a2), c2y = Math.Sin(a2) - k * Math.Cos(a2);
                    PointF p1 = Map(c1x, c1y, arx, ary, cos, sin, cx, cy);
                    PointF p2 = Map(c2x, c2y, arx, ary, cos, sin, cx, cy);
                    PointF p3 = i == segments - 1 ? end : Map(Math.Cos(a2), Math.Sin(a2), arx, ary, cos, sin, cx, cy);
                    path.AddBezier(current, p1, p2, p3);
                    current = p3;
                }
            }

            private static PointF Map(double x, double y, double rx, double ry, double cos, double sin, double cx, double cy)
            {
                x *= rx;
                y *= ry;
                return new PointF((float)(cos * x - sin * y + cx), (float)(sin * x + cos * y + cy));
            }

            private static double Angle(double ux, double uy, double vx, double vy)
            {
                double a = Math.Atan2(uy, ux), b = Math.Atan2(vy, vx);
                double d = b - a;
                while (d > Math.PI) d -= 2 * Math.PI;
                while (d < -Math.PI) d += 2 * Math.PI;
                return d;
            }

            private void SkipSeparators()
            {
                while (pos < data.Length && (data[pos] == ' ' || data[pos] == ',')) pos++;
            }

            private bool Flag()
            {
                SkipSeparators();
                bool value = data[pos] == '1';
                pos++;
                return value;
            }

            private float Number()
            {
                SkipSeparators();
                int begin = pos;
                if (pos < data.Length && (data[pos] == '-' || data[pos] == '+')) pos++;
                bool dot = false;
                while (pos < data.Length)
                {
                    char c = data[pos];
                    if (char.IsDigit(c)) pos++;
                    else if (c == '.' && !dot)
                    {
                        dot = true;
                        pos++;
                    }
                    else if ((c == 'e' || c == 'E') && pos + 1 < data.Length && (char.IsDigit(data[pos + 1]) || data[pos + 1] == '-'))
                    {
                        pos += 2;
                    }
                    else break;
                }
                return float.Parse(data.Substring(begin, pos - begin), CultureInfo.InvariantCulture);
            }
        }
    }
}
