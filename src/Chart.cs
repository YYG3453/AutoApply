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
                                    float[] vals, Color[] cols, int hoverIdx)
        {
            float total = 0;
            for (int i = 0; i < vals.Length; i++) total += vals[i];
            if (total <= 0) total = 1;

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
                if (sweep > 0.1f)
                {
                    using (var p = new Pen(col, th))
                    {
                        p.StartCap = LineCap.Flat; p.EndCap = LineCap.Flat;
                        // 小间隙，接近 ECharts 的 borderWidth 效果
                        g.DrawArc(p, rect, start + 0.9f, Math.Max(0.5f, sweep - 1.8f));
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
    }
}
