using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace IP_UpdateTest.Ui
{
    /// <summary>
    /// 字号与字重。字号刻度很紧凑，层级靠字重和 ink 深浅区分
    /// </summary>
    public enum TextStyle
    {
        /// <summary>22 / 600 页面主标题</summary>
        Headline,
        /// <summary>15 / 600 面板标题、空状态标题</summary>
        Title,
        /// <summary>13 / 400 正文、表格单元格</summary>
        Body,
        /// <summary>13 / 500 名字、通知文字</summary>
        BodyMedium,
        /// <summary>13 / 500 按钮、标签页</summary>
        Label,
        /// <summary>12 / 400 辅助说明</summary>
        Small,
        /// <summary>11 / 500 表头、标签 chip、分组标题</summary>
        Caption,
        /// <summary>12.5 / 400 等宽数字</summary>
        Mono,
        /// <summary>13.5 / 400 输入框文字</summary>
        Input
    }

    /// <summary>
    /// 文字排版：拉丁字母和数字用随程序打包的 Geist / Geist Mono，中文回落到系统中文字体。
    /// 同一行按字符拆成多段分别用两种字体绘制，基线对齐。字体加载失败时全部回落系统字体
    /// </summary>
    public static class Typo
    {
        private enum Face
        {
            Regular,
            Medium,
            SemiBold,
            Mono,
            Cjk,
            CjkBold
        }

        private static readonly string[] CjkCandidates =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans SC", "Source Han Sans SC", "SimSun"
        };

        private static readonly FontFamily[] Families = new FontFamily[6];
        private static readonly bool[] LatinCoverage = new bool[65536];
        private static readonly bool[] MonoCoverage = new bool[65536];
        private static readonly List<PrivateFontCollection> Collections = new List<PrivateFontCollection>();
        private static readonly Dictionary<long, Font> FontCache = new Dictionary<long, Font>();
        private static readonly Dictionary<string, float> WidthCache = new Dictionary<string, float>();
        private static readonly StringFormat Format;
        private static readonly Bitmap MeasureBitmap = new Bitmap(1, 1);
        private static readonly Graphics MeasureGraphics;

        static Typo()
        {
            Format = (StringFormat)StringFormat.GenericTypographic.Clone();
            Format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
            MeasureGraphics = Graphics.FromImage(MeasureBitmap);
            MeasureGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            Families[(int)Face.Cjk] = ResolveCjk();
            Families[(int)Face.CjkBold] = Families[(int)Face.Cjk];
            Families[(int)Face.Regular] = LoadFont("Geist-Regular.ttf", LatinCoverage);
            Families[(int)Face.Medium] = LoadFont("Geist-Medium.ttf", null);
            Families[(int)Face.SemiBold] = LoadFont("Geist-SemiBold.ttf", null);
            Families[(int)Face.Mono] = LoadFont("GeistMono-Regular.ttf", MonoCoverage);
            HasGeist = Families[(int)Face.Regular] != null;

            // 任意一个字重加载失败就退到能用的那个
            for (int i = (int)Face.Regular; i <= (int)Face.SemiBold; i++)
                if (Families[i] == null) Families[i] = Families[(int)Face.Regular];
        }

        /// <summary>
        /// Geist 是否加载成功（失败时全部使用系统字体）
        /// </summary>
        public static bool HasGeist { get; private set; }

        /// <summary>
        /// 中文字体名，供原生控件使用
        /// </summary>
        public static string CjkFamilyName
        {
            get { return Families[(int)Face.Cjk].Name; }
        }

        public static float SizeOf(TextStyle style)
        {
            switch (style)
            {
                case TextStyle.Headline: return 22;
                case TextStyle.Title: return 15;
                case TextStyle.Small: return 12;
                case TextStyle.Caption: return 11;
                case TextStyle.Mono: return 12.5f;
                case TextStyle.Input: return 13.5f;
                default: return 13;
            }
        }

        private static Face LatinFace(TextStyle style)
        {
            switch (style)
            {
                case TextStyle.Headline:
                case TextStyle.Title:
                    return Face.SemiBold;
                case TextStyle.BodyMedium:
                case TextStyle.Label:
                case TextStyle.Caption:
                    return Face.Medium;
                case TextStyle.Mono:
                    return Face.Mono;
                default:
                    return Face.Regular;
            }
        }

        private static Face CjkFace(TextStyle style)
        {
            return style == TextStyle.Headline || style == TextStyle.Title ? Face.CjkBold : Face.Cjk;
        }

        /// <summary>
        /// 一行的高度（设备像素）
        /// </summary>
        public static float LineHeight(TextStyle style, float scale)
        {
            return (float)Math.Round(SizeOf(style) * 1.45f * scale);
        }

        /// <summary>
        /// 让文字在 rect 中垂直居中的基线位置
        /// </summary>
        public static float CenterBaseline(RectangleF rect, TextStyle style, float scale)
        {
            return rect.Top + rect.Height / 2 + SizeOf(style) * scale * 0.36f;
        }

        #region 字体

        private static FontFamily ResolveCjk()
        {
            foreach (string name in CjkCandidates)
            {
                try
                {
                    var family = new FontFamily(name);
                    if (family.IsStyleAvailable(FontStyle.Regular)) return family;
                }
                catch (ArgumentException)
                {
                }
            }
            return SystemFonts.MessageBoxFont.FontFamily;
        }

        private static FontFamily LoadFont(string file, bool[] coverage)
        {
            try
            {
                byte[] data;
                using (Stream stream = typeof(Typo).Assembly.GetManifestResourceStream("IP_UpdateTest.Fonts." + file))
                {
                    if (stream == null) return null;
                    data = new byte[stream.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int n = stream.Read(data, read, data.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                }
                if (coverage != null && !ReadCoverage(data, coverage)) return null;

                // 字体数据在进程生命周期内都要保持有效，不释放
                IntPtr memory = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, memory, data.Length);
                var collection = new PrivateFontCollection();
                collection.AddMemoryFont(memory, data.Length);
                Collections.Add(collection);

                // 同时登记到 GDI，原生输入框才能用（只对本进程可见）
                uint installed = 0;
                AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref installed);

                FontFamily family = collection.Families.Length > 0 ? collection.Families[0] : null;
                return family != null && family.IsStyleAvailable(FontStyle.Regular) ? family : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is ExternalException || ex is IOException || ex is OutOfMemoryException)
            {
                return null;
            }
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

        /// <summary>
        /// 读取 TrueType cmap（format 4）得到字体包含哪些字符，缺的字符交给中文字体
        /// </summary>
        internal static bool ReadCoverage(byte[] font, bool[] coverage)
        {
            if (font.Length < 12) return false;
            int numTables = U16(font, 4);
            int cmap = -1;
            for (int i = 0; i < numTables; i++)
            {
                int record = 12 + i * 16;
                if (record + 16 > font.Length) return false;
                if (font[record] == 'c' && font[record + 1] == 'm' && font[record + 2] == 'a' && font[record + 3] == 'p')
                {
                    cmap = (int)U32(font, record + 8);
                    break;
                }
            }
            if (cmap < 0) return false;

            int subtables = U16(font, cmap + 2);
            int table = -1;
            for (int i = 0; i < subtables; i++)
            {
                int record = cmap + 4 + i * 8;
                int platform = U16(font, record), encoding = U16(font, record + 2);
                int offset = cmap + (int)U32(font, record + 4);
                if (U16(font, offset) == 4 && (platform == 3 && encoding == 1 || platform == 0))
                {
                    table = offset;
                    if (platform == 3) break;
                }
            }
            if (table < 0) return false;

            int segCount = U16(font, table + 6) / 2;
            int endCodes = table + 14;
            int startCodes = endCodes + segCount * 2 + 2;
            int idDeltas = startCodes + segCount * 2;
            int idRangeOffsets = idDeltas + segCount * 2;
            for (int s = 0; s < segCount; s++)
            {
                int end = U16(font, endCodes + s * 2);
                int start = U16(font, startCodes + s * 2);
                int delta = U16(font, idDeltas + s * 2);
                int rangeOffset = U16(font, idRangeOffsets + s * 2);
                if (start == 0xFFFF) continue;
                for (int c = start; c <= end && c < 0xFFFF; c++)
                {
                    int glyph;
                    if (rangeOffset == 0)
                    {
                        glyph = (c + delta) & 0xFFFF;
                    }
                    else
                    {
                        int address = idRangeOffsets + s * 2 + rangeOffset + (c - start) * 2;
                        if (address + 1 >= font.Length) continue;
                        glyph = U16(font, address);
                        if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                    }
                    if (glyph != 0) coverage[c] = true;
                }
            }
            return true;
        }

        private static int U16(byte[] b, int i)
        {
            return i + 1 < b.Length ? (b[i] << 8) | b[i + 1] : 0;
        }

        private static uint U32(byte[] b, int i)
        {
            return i + 3 < b.Length ? (uint)((b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]) : 0;
        }

        private static Font GetFont(Face face, float sizePx)
        {
            long key = ((long)face << 32) | (uint)Math.Round(sizePx * 4);
            Font font;
            if (!FontCache.TryGetValue(key, out font))
            {
                FontFamily family = Families[(int)face];
                FontStyle fs = face == Face.CjkBold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
                if (!HasGeist && (face == Face.SemiBold) && family.IsStyleAvailable(FontStyle.Bold)) fs = FontStyle.Bold;
                font = new Font(family, sizePx, fs, GraphicsUnit.Pixel);
                FontCache[key] = font;
            }
            return font;
        }

        /// <summary>
        /// 原生输入框用的字体。mono 为 true 时用 Geist Mono（只输入 IP 等 ASCII 内容的框）
        /// </summary>
        public static Font NativeFont(TextStyle style, float scale, bool mono)
        {
            Face face = mono && Families[(int)Face.Mono] != null ? Face.Mono : Face.Cjk;
            return GetFont(face, SizeOf(style) * scale);
        }

        #endregion

        #region 分段

        private struct Run
        {
            public string Text;
            public Face Face;
        }

        private static bool IsContextPunctuation(char c)
        {
            // 中文里的引号、省略号、破折号跟随前一个字的字体，避免夹在中文里显得太窄
            return c >= 0x2010 && c <= 0x206F;
        }

        private static List<Run> Split(string text, TextStyle style)
        {
            var runs = new List<Run>();
            if (string.IsNullOrEmpty(text)) return runs;

            Face latin = LatinFace(style), cjk = CjkFace(style);
            bool[] coverage = latin == Face.Mono ? MonoCoverage : LatinCoverage;
            bool hasLatin = Families[(int)latin] != null && (latin == Face.Mono ? Families[(int)Face.Mono] != null : HasGeist);
            if (!hasLatin)
            {
                runs.Add(new Run { Text = text, Face = cjk });
                return runs;
            }

            var sb = new StringBuilder();
            Face? current = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                Face face;
                if (char.IsSurrogate(c)) face = cjk;
                else if (IsContextPunctuation(c) && current.HasValue) face = current.Value;
                else face = coverage[c] ? latin : cjk;

                if (current.HasValue && face != current.Value)
                {
                    runs.Add(new Run { Text = sb.ToString(), Face = current.Value });
                    sb.Clear();
                }
                current = face;
                sb.Append(c);
            }
            if (sb.Length > 0) runs.Add(new Run { Text = sb.ToString(), Face = current.Value });
            return runs;
        }

        private static float Ascent(Font font)
        {
            FontFamily family = font.FontFamily;
            return font.Size * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
        }

        #endregion

        #region 测量与绘制

        /// <summary>
        /// 一段同字体文字的宽度。首尾空格按字体的空格宽度单独计算：
        /// GDI+ 在网格对齐时对行首、行尾空格的处理不稳定，混排时字体切换处的空格会被吃掉
        /// </summary>
        private static float RunWidth(Graphics g, string text, Face face, float size)
        {
            string body = text.Trim(' ');
            int spaces = text.Length - body.Length;
            float width = spaces * SpaceWidth(face, size);
            if (body.Length > 0) width += g.MeasureString(body, GetFont(face, size), PointF.Empty, Format).Width;
            return width;
        }

        private static readonly Dictionary<Face, float> SpaceEm = new Dictionary<Face, float>();

        /// <summary>
        /// 空格宽度：在 100px 下量“x x”与“xx”的差，避免小字号的网格取整
        /// </summary>
        private static float SpaceWidth(Face face, float size)
        {
            float em;
            if (!SpaceEm.TryGetValue(face, out em))
            {
                Font big = GetFont(face, 100);
                em = (MeasureGraphics.MeasureString("x x", big, PointF.Empty, Format).Width
                    - MeasureGraphics.MeasureString("xx", big, PointF.Empty, Format).Width) / 100f;
                if (em <= 0.05f || em > 0.6f) em = 0.27f;
                SpaceEm[face] = em;
            }
            return em * size;
        }

        /// <summary>
        /// 一行文字的宽度（设备像素）
        /// </summary>
        public static float Measure(string text, TextStyle style, float scale)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            string key = (int)style + "|" + scale.ToString("0.###") + "|" + text;
            float width;
            if (WidthCache.TryGetValue(key, out width)) return width;

            float size = SizeOf(style) * scale;
            width = 0;
            foreach (Run run in Split(text, style))
                width += RunWidth(MeasureGraphics, run.Text, run.Face, size);

            if (WidthCache.Count > 4000) WidthCache.Clear();
            WidthCache[key] = width;
            return width;
        }

        /// <summary>
        /// 在 (x, 基线) 处绘制一行文字
        /// </summary>
        public static void DrawLine(Graphics g, string text, TextStyle style, Color color, float x, float baseline, float scale)
        {
            if (string.IsNullOrEmpty(text) || color.A == 0) return;
            float size = SizeOf(style) * scale;
            using (var brush = new SolidBrush(color))
            {
                foreach (Run run in Split(text, style))
                {
                    Font font = GetFont(run.Face, size);
                    string body = run.Text.Trim(' ');
                    float space = SpaceWidth(run.Face, size);
                    float lead = (run.Text.Length - run.Text.TrimStart(' ').Length) * space;
                    if (body.Length > 0) g.DrawString(body, font, brush, x + lead, baseline - Ascent(font), Format);
                    x += RunWidth(g, run.Text, run.Face, size);
                }
            }
        }

        /// <summary>
        /// 带不透明度和模糊的一行文字（模糊用缩小再放大的位图近似）
        /// </summary>
        public static void DrawLine(Graphics g, string text, TextStyle style, Color color, float x, float baseline, float scale, double opacity, double blur)
        {
            if (string.IsNullOrEmpty(text) || opacity <= 0.004) return;
            if (blur < 0.35)
            {
                DrawLine(g, text, style, Palette.Fade(color, opacity), x, baseline, scale);
                return;
            }

            float width = Measure(text, style, scale);
            float size = SizeOf(style) * scale;
            float ascent = size * 1.15f, descent = size * 0.4f;
            float pad = (float)(blur * scale * 1.5) + 2;
            float factor = (float)(1 / (1 + blur * 0.45));
            int bw = (int)Math.Ceiling((width + pad * 2) * factor) + 1;
            int bh = (int)Math.Ceiling((ascent + descent + pad * 2) * factor) + 1;
            if (bw <= 0 || bh <= 0 || bw > 4000) return;

            using (var bitmap = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb))
            {
                using (Graphics bg = Graphics.FromImage(bitmap))
                {
                    bg.TextRenderingHint = TextRenderingHint.AntiAlias;
                    bg.ScaleTransform(factor, factor);
                    DrawLine(bg, text, style, color, pad, pad + ascent, scale);
                }

                var matrix = new ColorMatrix { Matrix33 = (float)Math.Min(1, opacity) };
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(matrix);
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    float left = x - pad, top = baseline - ascent - pad;
                    var dest = new[]
                    {
                        new PointF(left, top),
                        new PointF(left + bw / factor, top),
                        new PointF(left, top + bh / factor)
                    };
                    InterpolationMode old = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(bitmap, dest, new RectangleF(0, 0, bw, bh), GraphicsUnit.Pixel, attributes);
                    g.InterpolationMode = old;
                }
            }
        }

        /// <summary>
        /// 单行文字，垂直居中，超出时以省略号结尾
        /// </summary>
        public static void Draw(Graphics g, string text, TextStyle style, Color color, RectangleF rect, float scale, StringAlignment align = StringAlignment.Near)
        {
            if (string.IsNullOrEmpty(text) || rect.Width <= 0) return;
            string fitted = Ellipsize(text, style, rect.Width, scale);
            float width = Measure(fitted, style, scale);
            float x = align == StringAlignment.Center ? rect.X + (rect.Width - width) / 2
                : align == StringAlignment.Far ? rect.Right - width
                : rect.X;
            DrawLine(g, fitted, style, color, x, CenterBaseline(rect, style, scale), scale);
        }

        /// <summary>
        /// 超出宽度时截断并加省略号
        /// </summary>
        public static string Ellipsize(string text, TextStyle style, float maxWidth, float scale)
        {
            if (string.IsNullOrEmpty(text) || Measure(text, style, scale) <= maxWidth) return text;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Measure(text.Substring(0, mid).TrimEnd() + "…", style, scale) <= maxWidth) lo = mid;
                else hi = mid - 1;
            }
            return lo == 0 ? "…" : text.Substring(0, lo).TrimEnd() + "…";
        }

        /// <summary>
        /// 按宽度折行：中文逐字可断，英文和数字按词断开，过长的词再按字符断
        /// </summary>
        public static List<string> Wrap(string text, TextStyle style, float maxWidth, float scale)
        {
            var lines = new List<string>();
            if (text == null) return lines;
            foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                var tokens = Tokenize(paragraph);
                var line = new StringBuilder();
                foreach (string token in tokens)
                {
                    string candidate = line.ToString() + token;
                    if (line.Length == 0 || Measure(candidate.TrimEnd(), style, scale) <= maxWidth)
                    {
                        line.Append(token);
                        if (line.Length == token.Length && Measure(token.TrimEnd(), style, scale) > maxWidth)
                        {
                            // 单个词就超宽：按字符拆
                            line.Clear();
                            foreach (char c in token)
                            {
                                if (line.Length > 0 && Measure(line.ToString() + c, style, scale) > maxWidth)
                                {
                                    lines.Add(line.ToString());
                                    line.Clear();
                                }
                                line.Append(c);
                            }
                        }
                        continue;
                    }
                    lines.Add(line.ToString().TrimEnd());
                    line.Clear();
                    line.Append(token.TrimStart());
                }
                lines.Add(line.ToString().TrimEnd());
            }
            return lines;
        }

        private static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            var word = new StringBuilder();
            foreach (char c in text)
            {
                bool breakable = c > 0x2E7F || char.IsWhiteSpace(c);
                if (breakable)
                {
                    if (char.IsWhiteSpace(c))
                    {
                        word.Append(c);
                        tokens.Add(word.ToString());
                        word.Clear();
                    }
                    else
                    {
                        if (word.Length > 0) tokens.Add(word.ToString());
                        word.Clear();
                        // 中文标点不放在行首：并到前一个字上
                        if (IsClosingPunctuation(c) && tokens.Count > 0) tokens[tokens.Count - 1] += c;
                        else tokens.Add(c.ToString());
                    }
                }
                else
                {
                    word.Append(c);
                }
            }
            if (word.Length > 0) tokens.Add(word.ToString());
            return tokens;
        }

        private static bool IsClosingPunctuation(char c)
        {
            return "，。、；：？！）》」』】”’".IndexOf(c) >= 0;
        }

        #endregion
    }
}
