using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AutoApply
{
    /// <summary>
    /// 轻量的图表绘制层（GDI+）。目标是把 ECharts 那套观感搬到原生窗口里：
    /// 平滑折线 + 渐变面积 + 网格 + 轴标签 + 悬停十字线与提示框；环形图带扇区高亮。
    /// 之所以不用真正的 ECharts：单 exe 无依赖是硬约束，嵌 WebView2 需要额外运行时。
    /// </summary>
    internal static class Chart
    {
        // ---------- 平滑折线 ----------

        /// <summary>构造 Catmull-Rom 平滑路径；pts 至少两个点。</summary>
        private static GraphicsPath Smooth(PointF[] pts, float tension)
        {
            var path = new GraphicsPath();
            if (pts.Length == 0) return path;
            if (pts.Length == 1) { path.AddLine(pts[0], pts[0]); return path; }
            path.AddLine(pts[0], pts[0]);
            for (int i = 0; i < pts.Length - 1; i++)
            {
                PointF p0 = i - 1 >= 0 ? pts[i - 1] : pts[i];
                PointF p1 = pts[i];
                PointF p2 = pts[i + 1];
                PointF p3 = i + 2 < pts.Length ? pts[i + 2] : pts[i + 1];
                var c1 = new PointF(p1.X + (p2.X - p0.X) / 6f * tension, p1.Y + (p2.Y - p0.Y) / 6f * tension);
                var c2 = new PointF(p2.X - (p3.X - p1.X) / 6f * tension, p2.Y - (p3.Y - p1.Y) / 6f * tension);
                path.AddBezier(p1, c1, c2, p2);
            }
            return path;
        }

        /// <summary>
        /// 平滑面积图。plot 是画图区（不含标题），vals 可为 0..n 个值。
        /// 返回每个数据点的屏幕坐标，供命中测试用。
        /// </summary>
        public static PointF[] Area(Graphics g, RectangleF plot, float[] vals, string[] labels,
                                    Color accent, int hoverIdx, bool showGrid)
        {
            if (vals == null || vals.Length == 0) return new PointF[0];
            float max = 1f;
            for (int i = 0; i < vals.Length; i++) if (vals[i] > max) max = vals[i];
            max = (float)Math.Ceiling(max * 1.25f);

            // 网格 + Y 轴刻度
            if (showGrid)
            {
                for (int i = 0; i <= 3; i++)
                {
                    float y = plot.Bottom - plot.Height * i / 3f;
                    using (var p = new Pen(T.Alpha(T.Line, i == 0 ? 200 : 90), 1))
                    {
                        if (i != 0) p.DashStyle = DashStyle.Dash;
                        g.DrawLine(p, plot.X, y, plot.Right, y);
                    }
                    string lbl = i == 0 ? "0" : ((int)(max * i / 3f)).ToString();
                    T.TextR(g, lbl, T.F(9.5f), T.Text3, plot.X - 6, y - 12);
                }
            }

            int n = vals.Length;
            var pts = new PointF[n];
            float step = n > 1 ? plot.Width / (n - 1) : 0;
            for (int i = 0; i < n; i++)
                pts[i] = new PointF(plot.X + step * i, plot.Bottom - plot.Height * (vals[i] / max));

            // 渐变面积
            using (var path = Smooth(pts, 1f))
            {
                var area = (GraphicsPath)path.Clone();
                area.AddLine(pts[n - 1].X, plot.Bottom, pts[0].X, plot.Bottom);
                area.CloseFigure();
                using (var lg = new LinearGradientBrush(new RectangleF(plot.X, plot.Y - 1, Math.Max(2f, plot.Width), plot.Height + 2),
                                                        T.Alpha(accent, 150), T.Alpha(accent, 8), 90f))
                    g.FillPath(lg, area);
                area.Dispose();

                using (var pen = new Pen(accent, 2f)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, path); }
            }

            // 悬停：竖向虚线 + 高亮点 + 提示框
            if (hoverIdx >= 0 && hoverIdx < n)
            {
                var hp = pts[hoverIdx];
                using (var p = new Pen(T.Alpha(accent, 150), 1)) { p.DashStyle = DashStyle.Dot; g.DrawLine(p, hp.X, plot.Y, hp.X, plot.Bottom); }
                using (var b = new SolidBrush(T.Bg)) g.FillEllipse(b, hp.X - 5.5f, hp.Y - 5.5f, 11, 11);
                using (var p = new Pen(accent, 2.5f)) g.DrawEllipse(p, hp.X - 4.5f, hp.Y - 4.5f, 9, 9);

                string tip = (labels != null && hoverIdx < labels.Length ? labels[hoverIdx] : "") + "   " + vals[hoverIdx].ToString("0");
                SizeF sz = g.MeasureString(tip, T.F(11f));
                float tw = sz.Width + 18, th = sz.Height + 12;
                float tx = Math.Min(hp.X - tw / 2f, plot.Right - tw);
                tx = Math.Max(plot.X, tx);
                float ty = Math.Max(plot.Y - 4, hp.Y - th - 10);
                var tr = new RectangleF(tx, ty, tw, th);
                T.FillRound(g, tr, 7f, T.Panel3);
                T.StrokeRound(g, tr, 7f, T.Alpha(accent, 120));
                T.Draw(g, tip, T.F(11f), T.Text, tr.X + 9, tr.Y + 5);
            }

            // X 轴标签
            if (labels != null)
            {
                for (int i = 0; i < n && i < labels.Length; i++)
                {
                    var r = new RectangleF(plot.X + step * i - 26, plot.Bottom + 8, 52, 16);
                    T.TextC(g, labels[i], T.F(9.5f), i == hoverIdx ? T.Text : T.Text3, r);
                }
            }
            return pts;
        }

        /// <summary>面积图的命中：返回鼠标 x 最近的数据点下标，超出容差返回 -1。</summary>
        public static int HitArea(PointF[] pts, PointF m, float tolerance)
        {
            int best = -1; float bd = tolerance;
            for (int i = 0; i < pts.Length; i++)
            {
                float d = Math.Abs(pts[i].X - m.X);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // ---------- 环形图 ----------

        /// <summary>环形图。hoverIdx 的扇区会外扩并提亮；返回各扇区中心角（弧度），供标签定位。</summary>
        public static float[] Donut(Graphics g, PointF c, float rOut, float thickness,
                                    float[] vals, Color[] cols, int hoverIdx, float appear = 1f)
        {
            float total = 0;
            for (int i = 0; i < vals.Length; i++) total += vals[i];
            if (total <= 0) total = 1;

            float ap = appear < 0f ? 0f : (appear > 1f ? 1f : appear);
            float start = -90f;
            var mids = new float[vals.Length];
            for (int i = 0; i < vals.Length; i++)
            {
                float sweep = 360f * (vals[i] / total);
                mids[i] = start + sweep / 2f;
                bool hv = i == hoverIdx;
                float th = thickness + (hv ? 6f : 0f);
                float r = rOut + (hv ? 2f : 0f);
                var rect = new RectangleF(c.X - r + th / 2f, c.Y - r + th / 2f, (r - th / 2f) * 2f, (r - th / 2f) * 2f);
                Color col = hv ? T.Mix(cols[i], Color.White, 0.22f) : cols[i];
                if (hoverIdx >= 0 && !hv) col = T.Alpha(col, 110);      // 悬停时其余扇区压暗
                float drawn = sweep * ap;
                if (drawn > 0.1f)
                {
                    using (var p = new Pen(col, th))
                    {
                        p.StartCap = LineCap.Flat; p.EndCap = LineCap.Flat;
                        // 小间隙，接近 ECharts 的 borderWidth 效果
                        g.DrawArc(p, rect, start + 0.9f, Math.Max(0.5f, drawn - 1.8f));
                    }
                }
                start += sweep;
            }
            return mids;
        }

        /// <summary>环形图命中：返回落在哪个扇区（按半径带 + 角度判断），否则 -1。</summary>
        public static int HitDonut(PointF c, float rOut, float thickness, float[] vals, PointF m)
        {
            float dx = m.X - c.X, dy = m.Y - c.Y;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            float rIn = rOut - thickness;
            if (d < rIn || d > rOut + 4) return -1;
            float ang = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI); // -180..180
            if (ang < -90) ang += 360; else if (ang > 270) ang -= 360;
            float total = 0;
            for (int i = 0; i < vals.Length; i++) total += vals[i];
            if (total <= 0) return -1;
            float cur = -90f;
            for (int i = 0; i < vals.Length; i++)
            {
                float sweep = 360f * (vals[i] / total);
                if (ang >= cur && ang < cur + sweep) return i;
                cur += sweep;
            }
            return -1;
        }
        // ---------- 多序列折线（投递 / 回复 双轴共用）----------

        /// <summary>
        /// 多条平滑折线：共用一套网格与 Y 轴（取所有序列的最大值），
        /// 悬停时在同一列上给每条线都打点，提示框里逐行列出。
        /// 返回每条序列的点坐标，供 HitMulti 做命中。
        /// </summary>
        public static PointF[][] MultiLine(Graphics g, RectangleF plot, float[][] series, Color[] cols,
                                           string[] names, string[] labels, int hoverIdx, bool grid,
                                           float[] scales = null, float appear = 1f)
        {
            if (series == null || series.Length == 0)
            {
                using (var p0 = new Pen(T.Alpha(T.Line, 80), 1)) g.DrawLine(p0, plot.X, plot.Bottom, plot.Right, plot.Bottom);
                return new PointF[0][];
            }
            int n = 0;
            float max = 1f;
            for (int k = 0; k < series.Length; k++)
            {
                if (series[k] == null) continue;
                if (series[k].Length > n) n = series[k].Length;
                for (int i = 0; i < series[k].Length; i++) if (series[k][i] > max) max = series[k][i];
            }
            max = (float)Math.Ceiling(max * 1.2f);

            if (grid)
            {
                for (int i = 0; i <= 4; i++)
                {
                    float y = plot.Bottom - plot.Height * i / 4f;
                    using (var p0 = new Pen(T.Alpha(T.Line, i == 0 ? 190 : 70), 1))
                    {
                        if (i != 0) p0.DashStyle = DashStyle.Dash;
                        g.DrawLine(p0, plot.X, y, plot.Right, y);
                    }
                    string lbl = i == 0 ? "0" : ((int)(max * i / 4f)).ToString();
                    T.TextR(g, lbl, T.F(9f), T.Text3, plot.X - 6, y - 11);
                }
            }

            float step = n > 1 ? plot.Width / (n - 1) : 0;
            float ap = appear < 0f ? 0f : (appear > 1f ? 1f : appear);
            var outPts = new PointF[series.Length][];
            for (int k = 0; k < series.Length; k++)
            {
                var vals = series[k];
                if (vals == null || vals.Length == 0) { outPts[k] = new PointF[0]; continue; }
                var pts = new PointF[vals.Length];
                for (int i = 0; i < vals.Length; i++)
                    pts[i] = new PointF(plot.X + step * i, plot.Bottom - plot.Height * (vals[i] / max) * ap);
                outPts[k] = pts;

                Color col = cols != null && k < cols.Length ? cols[k] : T.Jade;
                if (vals.Length >= 2)
                {
                    using (var path = Smooth(pts, 1f))
                    {
                        if (k == 0)
                        {
                            var area = (GraphicsPath)path.Clone();
                            area.AddLine(pts[pts.Length - 1].X, plot.Bottom, pts[0].X, plot.Bottom);
                            area.CloseFigure();
                            using (var lg = new LinearGradientBrush(
                                       new RectangleF(plot.X, plot.Y - 1, Math.Max(2f, plot.Width), plot.Height + 2),
                                       T.Alpha(col, (int)(120 * ap)), T.Alpha(col, (int)(6 * ap)), 90f))
                                g.FillPath(lg, area);
                            area.Dispose();
                        }
                        using (var pen = new Pen(col, k == 0 ? 2f : 1.8f))
                        {
                            if (k > 0) pen.DashStyle = DashStyle.Dash;
                            pen.LineJoin = LineJoin.Round;
                            g.DrawPath(pen, path);
                        }
                    }
                }
                if (vals.Length == 1)
                    using (var b = new SolidBrush(col)) g.FillEllipse(b, pts[0].X - 3, pts[0].Y - 3, 6, 6);
            }

            // 悬停
            if (hoverIdx >= 0 && hoverIdx < n)
            {
                float hx = plot.X + step * hoverIdx;
                // 悬停的那一列先铺一条淡带，鼠标在哪一目了然
                float bandW = n > 1 ? step : plot.Width;
                float bx = Math.Max(plot.X, hx - bandW / 2f);
                float br = Math.Min(plot.Right, hx + bandW / 2f);
                using (var sb = new SolidBrush(T.Alpha(T.Text, 12)))
                    g.FillRectangle(sb, bx, plot.Y, Math.Max(1f, br - bx), plot.Height);
                using (var p0 = new Pen(T.Alpha(T.Text3, 120), 1)) { p0.DashStyle = DashStyle.Dot; g.DrawLine(p0, hx, plot.Y, hx, plot.Bottom); }

                var rows = new List<KeyValuePair<string, float>>();
                for (int k = 0; k < outPts.Length; k++)
                {
                    if (outPts[k] == null || hoverIdx >= outPts[k].Length) continue;
                    var hp = outPts[k][hoverIdx];
                    Color col = cols != null && k < cols.Length ? cols[k] : T.Jade;
                    using (var b = new SolidBrush(T.Bg)) g.FillEllipse(b, hp.X - 5f, hp.Y - 5f, 10f, 10f);
                    using (var pen = new Pen(col, 2.2f)) g.DrawEllipse(pen, hp.X - 4.2f, hp.Y - 4.2f, 8.4f, 8.4f);
                    float sc = scales != null && k < scales.Length && scales[k] > 0.001f ? scales[k] : 1f;
                    rows.Add(new KeyValuePair<string, float>(names != null && k < names.Length ? names[k] : ("序列" + (k + 1)), series[k][hoverIdx] / sc));
                }

                string head = labels != null && hoverIdx < labels.Length ? labels[hoverIdx] : "";
                float tw = 132f, th = 26f + rows.Count * 19f;
                foreach (var kv in rows) { }
                var sz = g.MeasureString(head, T.F(11f));
                tw = Math.Max(tw, sz.Width + 24f);
                for (int i = 0; i < rows.Count; i++)
                {
                    var s2 = g.MeasureString(rows[i].Key + "  " + rows[i].Value.ToString("0"), T.F(11f));
                    tw = Math.Max(tw, s2.Width + 34f);
                }
                float tx = Math.Min(hx + 12f, plot.Right - tw);
                tx = Math.Max(plot.X, tx);
                float ty = Math.Max(plot.Y + 2f, plot.Y + 6f);
                var tr = new RectangleF(tx, ty, tw, th);
                T.FillRound(g, tr, 8f, T.Panel3);
                T.StrokeRound(g, tr, 8f, T.Line);
                T.Draw(g, head, T.F(10.5f), T.Text3, tr.X + 11, tr.Y + 6);
                for (int i = 0; i < rows.Count; i++)
                {
                    Color col = cols != null && i < cols.Length ? cols[i] : T.Jade;
                    T.Dot(g, tr.X + 15, tr.Y + 28 + i * 19, 3.4f, col);
                    T.Draw(g, rows[i].Key, T.F(11f), T.Text2, tr.X + 24, tr.Y + 21 + i * 19);
                    T.TextR(g, rows[i].Value.ToString("0"), T.F(11f), T.Text, tr.Right - 11, tr.Y + 21 + i * 19);
                }
            }

            if (labels != null)
            {
                for (int i = 0; i < n && i < labels.Length; i++)
                {
                    var r = new RectangleF(plot.X + step * i - 26, plot.Bottom + 7, 52, 15);
                    T.TextC(g, labels[i], T.F(9f), i == hoverIdx ? T.Text : T.Text3, r);
                }
            }
            return outPts;
        }

        /// <summary>多序列折线的命中：按 X 找最近的一列。</summary>
        public static int HitMulti(PointF[][] pts, float x, float tolerance)
        {
            if (pts == null) return -1;
            int best = -1; float bd = tolerance;
            for (int k = 0; k < pts.Length; k++)
            {
                if (pts[k] == null) continue;
                for (int i = 0; i < pts[k].Length; i++)
                {
                    float d = Math.Abs(pts[k][i].X - x);
                    if (d < bd) { bd = d; best = i; }
                }
            }
            return best;
        }

        // ---------- 柱状图 ----------

        /// <summary>竖向柱状图（圆顶）。返回每根柱的矩形，供命中测试。</summary>
        public static RectangleF[] VBars(Graphics g, RectangleF plot, float[] vals, string[] labels,
                                        Color col, string[] tipKeys, int hoverIdx, bool grid, float appear = 1f)
        {
            if (vals == null || vals.Length == 0) return new RectangleF[0];
            float max = 1f;
            for (int i = 0; i < vals.Length; i++) if (vals[i] > max) max = vals[i];
            max = (float)Math.Ceiling(max * 1.15f);

            if (grid)
            {
                for (int i = 0; i <= 3; i++)
                {
                    float y = plot.Bottom - plot.Height * i / 3f;
                    using (var p0 = new Pen(T.Alpha(T.Line, i == 0 ? 190 : 60), 1))
                    {
                        if (i != 0) p0.DashStyle = DashStyle.Dash;
                        g.DrawLine(p0, plot.X, y, plot.Right, y);
                    }
                    T.TextR(g, i == 0 ? "0" : ((int)(max * i / 3f)).ToString(), T.F(9f), T.Text3, plot.X - 6, y - 11);
                }
            }

            int n = vals.Length;
            float slot = plot.Width / n;
            float bw = Math.Min(38f, slot * 0.56f);
            float ap = appear < 0f ? 0f : (appear > 1f ? 1f : appear);
            var rects = new RectangleF[n];
            bool anyHover = hoverIdx >= 0 && hoverIdx < n;
            for (int i = 0; i < n; i++)
            {
                float hFull = plot.Height * (vals[i] / max);
                if (vals[i] > 0 && hFull < 3f) hFull = 3f;
                float h = hFull * ap;
                if (vals[i] > 0 && ap > 0f && h < 1.5f) h = 1.5f;
                float x = plot.X + slot * i + (slot - bw) / 2f;
                var r = new RectangleF(x, plot.Bottom - h, bw, h);
                rects[i] = new RectangleF(x, plot.Bottom - hFull, bw, hFull);    // 命中用完整高度，别跟着动画抖
                bool hv = i == hoverIdx;
                Color c0 = hv ? T.Mix(col, Color.White, 0.18f) : col;
                if (anyHover && !hv) c0 = T.Alpha(c0, 105);                      // 其余柱子压暗
                if (h > 0.5f)
                {
                    using (var lg = new LinearGradientBrush(new RectangleF(r.X, r.Y, Math.Max(2f, r.Width), Math.Max(2f, r.Height)),
                                                           T.Alpha(c0, 235), T.Alpha(c0, 120), 90f))
                        T.FillRound(g, r, Math.Min(5f, r.Width / 2f), lg);
                }
                if (labels != null && i < labels.Length)
                {
                    var lr = new RectangleF(plot.X + slot * i, plot.Bottom + 7, slot, 15);
                    Color lc = hv ? T.Text : (anyHover ? T.Alpha(T.Text3, 120) : T.Text3);
                    T.TextC(g, labels[i], T.F(9f), lc, lr);
                }
                if (hv)
                {
                    string tip = (tipKeys != null && i < tipKeys.Length ? tipKeys[i] : "") + "  " + vals[i].ToString("0");
                    var sz = g.MeasureString(tip, T.F(11f));
                    float tw = sz.Width + 18, th = sz.Height + 12;
                    float tx = Math.Min(r.X + r.Width / 2f - tw / 2f, plot.Right - tw);
                    tx = Math.Max(plot.X, tx);
                    float ty = Math.Max(plot.Y - 2, r.Y - th - 8);
                    var tr = new RectangleF(tx, ty, tw, th);
                    T.FillRound(g, tr, 7f, T.Panel3);
                    T.StrokeRound(g, tr, 7f, T.Alpha(col, 120));
                    T.Draw(g, tip, T.F(11f), T.Text, tr.X + 9, tr.Y + 5);
                }
            }
            return rects;
        }

        /// <summary>柱状图命中：返回鼠标落在哪根柱（含整列），否则 -1。</summary>
        public static int HitBars(RectangleF[] rects, RectangleF plot, PointF m)
        {
            if (rects == null || rects.Length == 0) return -1;
            if (m.X < plot.X || m.X > plot.Right || m.Y < plot.Y - 4 || m.Y > plot.Bottom + 18) return -1;
            float slot = plot.Width / rects.Length;
            int i = (int)((m.X - plot.X) / slot);
            if (i < 0) i = 0;
            if (i >= rects.Length) i = rects.Length - 1;
            return i;
        }

    }
}
