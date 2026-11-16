using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 视觉语言：夜（墨底 + 玉色 + 香槟金细线）/ 日（宣纸底 + 同色系加深）。
    /// 所有绘制都走这里，主题切换只改这一处调色板，保证风格统一。
    /// </summary>
    internal static class T
    {
        /// <summary>true = 夜晚模式（默认），false = 日间模式。</summary>
        public static bool Night = true;

        // ---------- 底色 ----------
        public static Color Bg;
        public static Color BgTop;
        public static Color Bg2;
        public static Color Surface;
        public static Color SurfaceHi;
        public static Color Line;
        public static Color LineSoft;

        // ---------- 面层（按深浅排开，供自绘控件取用）----------
        public static Color RailTop;
        public static Color RailSel;
        public static Color Panel2;
        public static Color Panel3;
        public static Color Panel4;
        public static Color Field;
        public static Color Console;
        public static Color HoverSoft;
        public static Color HoverSoftHi;
        public static Color HoverSoft2;
        public static Color BtnHover;
        public static Color BtnHover2;
        public static Color Track;
        public static Color LineHi;
        public static Color Line2;
        public static Color Line3;

        // ---------- 文字 ----------
        public static Color Text;
        public static Color Text2;
        public static Color Text3;
        public static Color TextSoft;

        // ---------- 强调 ----------
        public static Color Jade;
        public static Color JadeDim;
        public static Color JadeHi;
        public static Color JadeMid;
        public static Color JadeLight;
        public static Color Jade2;
        public static Color JadeDeep;
        public static Color JadeInk;
        public static Color JadeInk2;
        public static Color Gold;
        public static Color Warn;
        public static Color Danger;
        public static Color Blue;

        static T() { SetNight(true); }

        /// <summary>切换主题调色板。切完要调用方重绘。</summary>
        public static void SetNight(bool night)
        {
            Night = night;
            if (night)
            {
                Bg = Color.FromArgb(0x0B, 0x0D, 0x11);
                BgTop = Color.FromArgb(0x0D, 0x10, 0x15);
                Bg2 = Color.FromArgb(0x11, 0x14, 0x1A);
                Surface = Color.FromArgb(0x15, 0x18, 0x1F);
                SurfaceHi = Color.FromArgb(0x1B, 0x20, 0x28);
                Line = Color.FromArgb(0x27, 0x2C, 0x36);
                LineSoft = Color.FromArgb(0x1E, 0x22, 0x2A);

                RailTop = Color.FromArgb(0x14, 0x18, 0x1F);
                RailSel = Color.FromArgb(0x1A, 0x21, 0x28);
                Panel2 = Color.FromArgb(0x12, 0x16, 0x1C);
                Panel3 = Color.FromArgb(0x18, 0x1D, 0x24);
                Panel4 = Color.FromArgb(0x15, 0x19, 0x20);
                Field = Color.FromArgb(0x18, 0x1C, 0x23);
                Console = Color.FromArgb(0x0E, 0x11, 0x16);
                HoverSoft = Color.FromArgb(0x18, 0x1D, 0x25);
                HoverSoftHi = Color.FromArgb(0x24, 0x2B, 0x35);
                HoverSoft2 = Color.FromArgb(0x24, 0x29, 0x31);
                BtnHover = Color.FromArgb(0x22, 0x28, 0x31);
                BtnHover2 = Color.FromArgb(0x22, 0x27, 0x30);
                Track = Color.FromArgb(0x26, 0x2D, 0x37);
                LineHi = Color.FromArgb(0x33, 0x3A, 0x46);
                Line2 = Color.FromArgb(0x2A, 0x30, 0x39);
                Line3 = Color.FromArgb(0x2A, 0x30, 0x3A);

                Text = Color.FromArgb(0xEC, 0xEE, 0xF3);
                Text2 = Color.FromArgb(0x9A, 0xA3, 0xB2);
                Text3 = Color.FromArgb(0x63, 0x6B, 0x79);
                TextSoft = Color.FromArgb(0xC9, 0xCF, 0xDA);

                Jade = Color.FromArgb(0x6F, 0xE3, 0xBE);
                JadeDim = Color.FromArgb(0x37, 0x8B, 0x74);
                JadeHi = Color.FromArgb(0x8A, 0xEC, 0xC7);
                JadeMid = Color.FromArgb(0x50, 0xC6, 0xA4);
                JadeLight = Color.FromArgb(0xA6, 0xF5, 0xD8);
                Jade2 = Color.FromArgb(0x5F, 0xD8, 0xB4);
                JadeDeep = Color.FromArgb(0x2F, 0x8E, 0x76);
                JadeInk = Color.FromArgb(0x07, 0x24, 0x1C);
                JadeInk2 = Color.FromArgb(0x08, 0x2A, 0x22);
                Gold = Color.FromArgb(0xD9, 0xB2, 0x6A);
                Warn = Color.FromArgb(0xE8, 0x9B, 0x5C);
                Danger = Color.FromArgb(0xE0, 0x6C, 0x6C);
                Blue = Color.FromArgb(0x6C, 0xA8, 0xE0);
            }
            else
            {
                // 宣纸：暖白底 + 墨字 + 同色系加深的玉色，白天也保持"安静"的对比度
                Bg = Color.FromArgb(0xF4, 0xF1, 0xEA);
                BgTop = Color.FromArgb(0xFD, 0xFB, 0xF7);
                Bg2 = Color.FromArgb(0xEF, 0xEB, 0xE2);
                Surface = Color.FromArgb(0xFF, 0xFF, 0xFF);
                SurfaceHi = Color.FromArgb(0xFA, 0xF7, 0xF1);
                Line = Color.FromArgb(0xE3, 0xDC, 0xCD);
                LineSoft = Color.FromArgb(0xEC, 0xE7, 0xDB);

                RailTop = Color.FromArgb(0xFF, 0xFF, 0xFF);
                RailSel = Color.FromArgb(0xF1, 0xEC, 0xE1);
                Panel2 = Color.FromArgb(0xFC, 0xFA, 0xF5);
                Panel3 = Color.FromArgb(0xF8, 0xF5, 0xEE);
                Panel4 = Color.FromArgb(0xF7, 0xF4, 0xEC);
                Field = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Console = Color.FromArgb(0xFB, 0xF9, 0xF3);
                HoverSoft = Color.FromArgb(0xF2, 0xEE, 0xE4);
                HoverSoftHi = Color.FromArgb(0xE9, 0xE4, 0xD6);
                HoverSoft2 = Color.FromArgb(0xEF, 0xEB, 0xE0);
                BtnHover = Color.FromArgb(0xED, 0xE8, 0xDB);
                BtnHover2 = Color.FromArgb(0xE7, 0xE1, 0xD2);
                Track = Color.FromArgb(0xE2, 0xDC, 0xCC);
                LineHi = Color.FromArgb(0xD6, 0xCF, 0xBE);
                Line2 = Color.FromArgb(0xDC, 0xD5, 0xC5);
                Line3 = Color.FromArgb(0xD3, 0xCC, 0xBB);

                Text = Color.FromArgb(0x1A, 0x1D, 0x23);
                Text2 = Color.FromArgb(0x5B, 0x62, 0x70);
                Text3 = Color.FromArgb(0x8E, 0x96, 0xA2);
                TextSoft = Color.FromArgb(0x33, 0x38, 0x3F);

                Jade = Color.FromArgb(0x14, 0x8A, 0x69);
                JadeDim = Color.FromArgb(0x6E, 0xBE, 0xA6);
                JadeHi = Color.FromArgb(0x36, 0xB4, 0x8D);
                JadeMid = Color.FromArgb(0x1E, 0x9E, 0x7A);
                JadeLight = Color.FromArgb(0x0F, 0x6B, 0x50);
                Jade2 = Color.FromArgb(0x27, 0xA8, 0x82);
                JadeDeep = Color.FromArgb(0x11, 0x6E, 0x55);
                JadeInk = Color.FromArgb(0xFF, 0xFF, 0xFF);
                JadeInk2 = Color.FromArgb(0xEC, 0xFF, 0xF8);
                Gold = Color.FromArgb(0xA8, 0x7B, 0x2E);
                Warn = Color.FromArgb(0xB8, 0x6C, 0x24);
                Danger = Color.FromArgb(0xC0, 0x41, 0x3C);
                Blue = Color.FromArgb(0x2C, 0x6B, 0xB0);
            }
        }

        public static FontFamily FfUI = LoadFamily("Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI");
        public static FontFamily FfNum = LoadFamily("Segoe UI", "Microsoft YaHei UI");
        public static FontFamily FfMono = LoadFamily("Cascadia Mono", "Consolas", "Courier New");

        /// <summary>DPI 缩放系数（96dpi = 1.0）。所有布局用逻辑像素，绘制时统一缩放。</summary>
        public static float S = 1f;

        public static Point LP(Point p) { return new Point((int)(p.X / S), (int)(p.Y / S)); }
        public static float LS(int v) { return v / S; }
        public static int RS(float v) { return (int)Math.Round(v * S); }
        public static RectangleF Scaled(Graphics g, Control c) { return new RectangleF(0, 0, c.Width / S, c.Height / S); }

        private static FontFamily LoadFamily(params string[] names)
        {
            foreach (var n in names)
            {
                try { return new FontFamily(n); }
                catch { }
            }
            return FontFamily.GenericSansSerif;
        }

        public static Font F(float size, FontStyle style = FontStyle.Regular) { return new Font(FfUI, size, style, GraphicsUnit.Pixel); }
        public static Font Num(float size, FontStyle style = FontStyle.Regular) { return new Font(FfNum, size, style, GraphicsUnit.Pixel); }
        public static Font Mono(float size, FontStyle style = FontStyle.Regular) { return new Font(FfMono, size, style, GraphicsUnit.Pixel); }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, path);
            g.SmoothingMode = old;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Brush b)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius)) g.FillPath(b, path);
            g.SmoothingMode = old;
        }

        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color c, float w = 1f)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            var rr = new RectangleF(r.X + w / 2, r.Y + w / 2, Math.Max(1, r.Width - w), Math.Max(1, r.Height - w));
            using (var path = Round(rr, radius))
            using (var pen = new Pen(c, w)) g.DrawPath(pen, path);
            g.SmoothingMode = old;
        }

        /// <summary>卡片：面层 + 1px 细边，悬停时抬一档。</summary>
        public static void Card(Graphics g, RectangleF r, float radius = 14f, bool hover = false)
        {
            var c = hover ? SurfaceHi : Surface;
            FillRound(g, r, radius, c);
            StrokeRound(g, r, radius, hover ? LineHi : Line, 1f);
        }

        public static void Draw(Graphics g, string s, Font f, Color c, float x, float y)
        {
            var old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y);
            g.TextRenderingHint = old;
        }

        public static void TextR(Graphics g, string s, Font f, Color c, float right, float y)
        {
            var sz = g.MeasureString(s, f);
            Draw(g, s, f, c, right - sz.Width, y);
        }

        public static void TextC(Graphics g, string s, Font f, Color c, RectangleF box)
        {
            var sz = g.MeasureString(s, f);
            Draw(g, s, f, c, box.X + (box.Width - sz.Width) / 2f, box.Y + (box.Height - sz.Height) / 2f);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        /// <summary>玉色主按钮的渐变。</summary>
        public static LinearGradientBrush JadeBrush(RectangleF r)
        {
            return new LinearGradientBrush(r, JadeHi, JadeMid, 35f);
        }

        public static void Hairline(Graphics g, float x1, float x2, float y)
        {
            using (var p = new Pen(LineSoft, 1f)) g.DrawLine(p, x1, y, x2, y);
        }

        public static void Dot(Graphics g, float cx, float cy, float r, Color c)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
        }

        public static void Ring(Graphics g, RectangleF box, float thickness, float pct, Color track, Color fill)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(track, thickness)) g.DrawEllipse(p, box);
            if (pct > 0)
            {
                using (var p = new Pen(fill, thickness))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, box, -90, Math.Max(1f, 360f * Math.Min(1f, pct)));
                }
            }
            g.SmoothingMode = SmoothingMode.Default;
        }
    }
}
