using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;

// AutoApply logo generator — X4 "Night Ember", refined.
// Usage: LogoGen.exe <pngOutDir> <icoPath> [previewPath]
internal static class LogoGen
{
    static readonly Color Plate1 = Color.FromArgb(255, 26, 32, 56);
    static readonly Color Plate2 = Color.FromArgb(255, 15, 18, 30);
    static readonly Color Plate3 = Color.FromArgb(255, 7, 8, 14);

    static readonly Color Wing1 = Color.FromArgb(255, 255, 224, 132);
    static readonly Color Wing2 = Color.FromArgb(255, 255, 152, 66);
    static readonly Color Wing3 = Color.FromArgb(255, 240, 62, 86);

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(Math.Min(rad * 2f, r.Width), r.Height);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static LinearGradientBrush VGradient(RectangleF r, Color a, Color b, Color c)
    {
        var br = new LinearGradientBrush(r, a, c, 90f);
        br.InterpolationColors = new ColorBlend(3) { Colors = new[] { a, b, c }, Positions = new[] { 0f, 0.42f, 1f } };
        return br;
    }

    static Bitmap Render(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);

            float pad = s * 0.04f, side = s - pad * 2f;
            var r = new RectangleF(pad, pad, side, side);
            using (var plate = RoundRect(r, side * 0.235f))
            {
                var st = g.Save();
                g.SetClip(plate, CombineMode.Intersect);

                using (var br = VGradient(r, Plate1, Plate2, Plate3))
                    g.FillRectangle(br, r);

                // 顶部冷光（让黑面不塌）
                using (var sheen = new LinearGradientBrush(
                    new RectangleF(r.X, r.Y, r.Width, r.Height * 0.34f),
                    Color.FromArgb(20, 160, 196, 255), Color.FromArgb(0, 160, 196, 255), 90f))
                    g.FillRectangle(sheen, r.X, r.Y, r.Width, r.Height * 0.34f);

                float ox = r.X, oy = r.Y;

                // 机身背后暖光（平滑径向，小尺寸不加）
                if (s >= 96)
                {
                    float cx = ox + side * 0.48f, cy = oy + side * 0.50f, rad = side * 0.40f;
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
                        using (var pgb = new PathGradientBrush(gp))
                        {
                            pgb.CenterPoint = new PointF(cx, cy);
                            pgb.CenterColor = Color.FromArgb(14, 255, 138, 58);
                            pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 138, 58) };
                            g.FillPath(pgb, gp);
                        }
                    }
                }

                // 机身：整体略微内缩，让黑底透气
                float k = 0.945f, mcx = ox + side * 0.5f, mcy = oy + side * 0.5f;
                Func<float, float, PointF> P = (fx, fy) =>
                    new PointF(mcx + (ox + side * fx - mcx) * k, mcy + (oy + side * fy - mcy) * k);

                var nose = P(0.885f, 0.145f);
                var tailU = P(0.100f, 0.435f);
                var fold = P(0.545f, 0.645f);
                var tailD = P(0.440f, 0.795f);

                // 机身投影
                if (s >= 32)
                {
                    var shadowPath = new GraphicsPath();
                    shadowPath.AddPolygon(new[] {
                        new PointF(nose.X + side * 0.012f, nose.Y + side * 0.016f),
                        new PointF(tailU.X + side * 0.012f, tailU.Y + side * 0.016f),
                        new PointF(fold.X + side * 0.012f, fold.Y + side * 0.016f) });
                    using (var sb = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                        g.FillPath(sb, shadowPath);
                    shadowPath.Dispose();
                }

                // 机身：单面暖色渐变（干净、缩小不糊）
                using (var plane = new GraphicsPath())
                {
                    plane.AddPolygon(new[] { nose, tailU, fold });
                    var pb = plane.GetBounds();
                    var st2 = g.Save();
                    g.SetClip(plane, CombineMode.Intersect);
                    using (var wb = new LinearGradientBrush(pb, Color.FromArgb(255, 255, 246, 210), Color.FromArgb(255, 240, 62, 86), 62f))
                    {
                        wb.InterpolationColors = new ColorBlend(3)
                        {
                            Colors = new[] { Color.FromArgb(255, 255, 240, 190), Color.FromArgb(255, 255, 168, 84), Color.FromArgb(255, 244, 88, 84) },
                            Positions = new[] { 0f, 0.52f, 1f }
                        };
                        g.FillRectangle(wb, pb);
                    }
                    g.Restore(st2);

                    using (var pen = new Pen(Color.FromArgb(70, 255, 250, 226), Math.Max(0.6f, side * 0.006f)))
                        g.DrawLine(pen, nose, tailU);
                }

                // 速度线：三条，越靠下越短
                float[][] segs = new float[][] {
                    new float[] { 0.360f, 0.300f, 0.150f },
                    new float[] { 0.462f, 0.424f, 0.118f },
                    new float[] { 0.560f, 0.552f, 0.078f }
                };
                foreach (var seg in segs)
                {
                    float lw = Math.Max(1.1f, side * 0.022f);
                    using (var pen = new Pen(Color.FromArgb(206, 255, 176, 104), lw))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        float x1 = ox + side * 0.085f;
                        float x2 = x1 + side * seg[2];
                        g.DrawLine(pen, x1, oy + side * seg[0], x2, oy + side * seg[1]);
                    }
                }

                g.Restore(st);
            }

            // 边缘：不再描一圈浅色边，保持纯色轮廓（避免看起来像半透明）
            using (var plate2 = RoundRect(r, side * 0.235f))
            using (var pen = new Pen(Color.FromArgb(255, 28, 34, 58), Math.Max(1.0f, side * 0.006f)))
            {
                var stX = g.Save();
                g.SetClip(plate2, CombineMode.Intersect);
                g.DrawPath(pen, plate2);
                g.Restore(stX);
            }

            if (s >= 64)
            {
                float in2 = side * 0.030f;
                var ir = new RectangleF(r.X + in2, r.Y + in2, r.Width - in2 * 2, r.Height - in2 * 2);
                using (var ipath = RoundRect(ir, side * 0.205f))
                using (var pen = new Pen(Color.FromArgb(48, 200, 222, 255), Math.Max(0.7f, side * 0.0045f)))
                    g.DrawPath(pen, ipath);
            }
        }
        return bmp;
    }

    // ---------- ICO ----------
    static byte[] BmpBytes(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = w * 4;
        var xor = new byte[stride * h];
        for (int y = 0; y < h; y++)
            System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, (h - 1 - y) * data.Stride), xor, y * stride, stride);
        bmp.UnlockBits(data);

        int maskStride = ((w + 31) / 32) * 4;
        var mask = new byte[maskStride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (xor[y * stride + x * 4 + 3] < 128)
                    mask[y * maskStride + (x / 8)] |= (byte)(0x80 >> (x % 8));

        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(xor.Length + mask.Length);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            bw.Write(xor); bw.Write(mask);
            bw.Flush();
            return ms.ToArray();
        }
    }

    static void WriteIco(string path, int[] sizes)
    {
        var frames = new List<byte[]>();
        var used = new List<int>();
        foreach (int s in sizes)
        {
            using (var bmp = Render(s))
            {
                frames.Add(BmpBytes(bmp));
                used.Add(s);
            }
        }
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)frames.Count);
            int offset = 6 + 16 * frames.Count;
            for (int i = 0; i < frames.Count; i++)
            {
                int dim = used[i] > 255 ? 0 : used[i];
                bw.Write((byte)dim);
                bw.Write((byte)dim);
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32);
                bw.Write(frames[i].Length); bw.Write(offset);
                offset += frames[i].Length;
            }
            foreach (var f in frames) bw.Write(f);
        }
    }

    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        string icoPath = args.Length > 1 ? args[1] : null;
        string preview = args.Length > 2 ? args[2] : null;
        Directory.CreateDirectory(outDir);

        int[] all = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256, 512 };
        foreach (int s in all)
            using (var bmp = Render(s))
                bmp.Save(Path.Combine(outDir, "icon-" + s + ".png"), ImageFormat.Png);

        if (!string.IsNullOrEmpty(icoPath))
            WriteIco(icoPath, new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 });

        if (!string.IsNullOrEmpty(preview))
        {
            int W = 1240, H = 700;
            using (var sheet = new Bitmap(W, H))
            using (var g = Graphics.FromImage(sheet))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var b = new SolidBrush(Color.FromArgb(255, 247, 244, 238))) g.FillRectangle(b, 0, 0, W / 2, H);
                using (var b = new SolidBrush(Color.FromArgb(255, 8, 9, 13))) g.FillRectangle(b, W / 2, 0, W / 2, H);
                using (var big = Render(512))
                {
                    g.DrawImage(big, 55, 40, 360, 360);
                    g.DrawImage(big, 675, 40, 360, 360);
                }
                using (var fN = new Font("Segoe UI", 13, FontStyle.Bold))
                using (var fS = new Font("Segoe UI", 9))
                using (var ink = new SolidBrush(Color.FromArgb(255, 26, 29, 35)))
                using (var wht = new SolidBrush(Color.FromArgb(255, 232, 236, 240)))
                {
                    g.DrawString("AutoApply · Night Ember", fN, ink, 55, 418);
                    g.DrawString("near-black plate / ember plane / warm backlight", fS, ink, 55, 446);
                    g.DrawString("AutoApply · Night Ember", fN, wht, 675, 418);
                    g.DrawString("near-black plate / ember plane / warm backlight", fS, wht, 675, 446);
                    g.DrawString("96 / 64 / 48 / 32 / 24 / 16 px — each drawn, not downscaled", fS, ink, 55, 632);
                    g.DrawString("96 / 64 / 48 / 32 / 24 / 16 px — each drawn, not downscaled", fS, wht, 675, 632);
                }
                int y = 500;
                int[] small = { 96, 64, 48, 32, 24, 16 };
                foreach (int baseX in new[] { 55, 675 })
                {
                    int x = baseX;
                    foreach (int s in small)
                    {
                        using (var bmp = Render(s))
                            g.DrawImage(bmp, x, y + (96 - s), s, s);
                        x += s + 14;
                    }
                }
                sheet.Save(preview, ImageFormat.Png);
            }
        }
        Console.WriteLine("ok");
    }
}
