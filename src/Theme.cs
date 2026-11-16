using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>视觉语言：墨底 + 玉色 + 香槟金细线。所有绘制都走这里，保证风格统一。</summary>
    internal static class T
    {
        // 底色
        public static readonly Color Bg = Color.FromArgb(0x0B, 0x0D, 0x11);
        public static readonly Color Bg2 = Color.FromArgb(0x11, 0x14, 0x1A);
        public static readonly Color Surface = Color.FromArgb(0x15, 0x18, 0x1F);
        public static readonly Color SurfaceHi = Color.FromArgb(0x1B, 0x20, 0x28);
        public static readonly Color Line = Color.FromArgb(0x27, 0x2C, 0x36);
        public static readonly Color LineSoft = Color.FromArgb(0x1E, 0x22, 0x2A);

        // 文字
        public static readonly Color Text = Color.FromArgb(0xEC, 0xEE, 0xF3);
        public static readonly Color Text2 = Color.FromArgb(0x9A, 0xA3, 0xB2);
        public static readonly Color Text3 = Color.FromArgb(0x63, 0x6B, 0x79);

        // 强调
        public static readonly Color Jade = Color.FromArgb(0x6F, 0xE3, 0xBE);
        public static readonly Color JadeDim = Color.FromArgb(0x37, 0x8B, 0x74);
        public static readonly Color Gold = Color.FromArgb(0xD9, 0xB2, 0x6A);
        public static readonly Color Warn = Color.FromArgb(0xE8, 0x9B, 0x5C);
        public static readonly Color Danger = Color.FromArgb(0xE0, 0x6C, 0x6C);
        public static readonly Color Blue = Color.FromArgb(0x6C, 0xA8, 0xE0);

        public static readonly FontFamily FfUI = LoadFamily("Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI");
        public static readonly FontFamily FfNum = LoadFamily("Segoe UI", "Microsoft YaHei UI");
        public static readonly FontFamily FfMono = LoadFamily("Cascadia Mono", "Consolas", "Courier New");

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
                try
                {
                    var ff = new FontFamily(n);
                    return ff;
                }
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

        /// <summary>卡片：半透明面层 + 1px 细边，带极淡的顶部高光。</summary>
        public static void Card(Graphics g, RectangleF r, float radius = 14f, bool hover = false)
        {
            var c = hover ? Color.FromArgb(0x1C, 0x21, 0x2A) : Surface;
            FillRound(g, r, radius, c);
            StrokeRound(g, r, radius, hover ? Color.FromArgb(0x35, 0x3D, 0x4A) : Line, 1f);
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
            return new LinearGradientBrush(r, Color.FromArgb(0x8A, 0xEC, 0xC7), Color.FromArgb(0x50, 0xC6, 0xA4), 35f);
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
