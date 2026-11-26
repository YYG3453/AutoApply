using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 皮肤选择浮层：无边框卡片，每套皮肤一行，左边是这套配色的缩略图，点一下立即生效。
    /// 之前用系统 ContextMenuStrip，深色下泛白、字挤在一起、也看不出配色差别，所以改成自绘。
    /// </summary>
    internal class SkinPicker : Form
    {
        private const float CardW = 276f;      // 逻辑像素
        private const float Pad = 12f;
        private const float Head = 50f;        // 标题区
        private const float RowH = 42f;
        private const float RowGap = 2f;
        private const float FootH = 40f;

        private readonly MainForm _f;
        private readonly DateTime _born = DateTime.Now;
        private int _hover = -1;               // -1 无 / 0..5 皮肤 / 6 底部日夜按钮

        public SkinPicker(MainForm f)
        {
            _f = f;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            BackColor = T.Bg;
            Font = T.F(12f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            int rows = T.StyleNames.Length;
            float h = Head + rows * (RowH + RowGap) + 6f + 1f + 6f + FootH + Pad;
            ClientSize = new Size(T.RS(CardW), T.RS(h));
            using (var p = T.Round(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), T.RS(12f)))
                Region = new Region(p);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW：给浮层一点投影，不至于糊在背景上
                return cp;
            }
        }

        /// <summary>贴着窗口右上角弹：右边缘与客户区留 12px，顶在主题按钮下面；放不下就往上翻。</summary>
        public void ShowAt(Rectangle anchorScreen, Rectangle clientScreen)
        {
            var wa = Screen.FromRectangle(clientScreen).WorkingArea;
            int x = clientScreen.Right - ClientSize.Width - T.RS(12);
            int y = anchorScreen.Bottom + T.RS(4);
            x = Math.Max(wa.Left + T.RS(8), Math.Min(x, wa.Right - ClientSize.Width - T.RS(8)));
            if (y + ClientSize.Height > wa.Bottom - T.RS(8))
                y = Math.Max(wa.Top + T.RS(8), anchorScreen.Top - ClientSize.Height - T.RS(6));
            Location = new Point(x, y);
            TopMost = true;    // 主窗口在截图模式下是 TopMost，面板不跟上就会被压在下面
            Show(_f);
            Activate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        private float RowsHeight { get { return T.StyleNames.Length * (RowH + RowGap); } }

        private float FootY { get { return Head + RowsHeight + 6f; } }

        private RectangleF RowRect(int i)
        {
            return new RectangleF(Pad - 6, Head + i * (RowH + RowGap), CardW - (Pad - 6) * 2, RowH);
        }

        private RectangleF FootRect
        {
            get { return new RectangleF(Pad - 6, FootY + 7f, CardW - (Pad - 6) * 2, FootH); }
        }

        // ---------------- 绘制 ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            float w = CardW, h = ClientSize.Height / T.S;

            // 浮层比底色亮一档，边框用常规分隔线：贴在上面的卡片才分得出两层
            T.FillRound(g, new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), 12f, T.SurfaceHi);
            T.StrokeRound(g, new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), 12f, T.Line, 1f);

            T.Draw(g, "外观", T.F(14f, FontStyle.Bold), T.Text, Pad + 2f, 14f);
            T.TextR(g, "点一下即生效", T.F(10f), T.Text3, w - Pad - 2f, 17f);

            for (int i = 0; i < T.StyleNames.Length; i++) DrawRow(g, i, RowRect(i));

            float fy = FootY;
            T.Hairline(g, Pad + 2f, w - Pad - 2f, fy);
            DrawFoot(g, FootRect);
        }

        private void DrawRow(Graphics g, int i, RectangleF r)
        {
            bool sel = i == T.NightStyle;
            bool hov = _hover == i;

            if (sel || hov)
            {
                T.FillRound(g, r, 9f, T.Alpha(sel ? T.Jade : T.Text, sel ? 26 : 20));
                if (sel) T.StrokeRound(g, r, 9f, T.Alpha(T.Jade, 110), 1f);
                if (sel) T.FillRound(g, new RectangleF(r.X + 1f, r.Y + 9f, 3f, r.Height - 18f), 1.5f, T.Jade);
            }

            bool dark = T.Night || i != 5;
            var sw = T.SwatchOf(i, dark);
            var box = new RectangleF(r.X + 10f, r.Y + (r.Height - 30f) / 2f, 52f, 30f);
            T.FillRound(g, box, 7f, sw.Bg);
            // 缩略图里放两笔"界面"：一条面层卡片 + 一条强调色按钮 + 一个状态点
            T.FillRound(g, new RectangleF(box.X + 8f, box.Y + 7f, 36f, 8f), 3f, sw.Surface);
            T.FillRound(g, new RectangleF(box.X + 8f, box.Y + 19f, 18f, 4.5f), 2.2f, sw.Accent);
            T.Dot(g, box.X + 45f, box.Y + 21f, 3.2f, sw.Accent);
            T.StrokeRound(g, box, 7f, sw.Line, 1f);

            float tx = box.Right + 11f;
            T.Draw(g, T.StyleNames[i], T.F(13f, sel ? FontStyle.Bold : FontStyle.Regular),
                   sel || hov ? T.Text : T.TextSoft, tx, r.Y + 4f);
            T.Draw(g, T.StyleDesc[i], T.F(10.5f), T.Text2, tx, r.Y + 22f);

            if (sel)
            {
                float cx = r.Right - 20f, cy = r.Y + r.Height / 2f;
                T.Dot(g, cx, cy, 8.5f, T.Jade);
                using (var p = new Pen(T.JadeInk, 1.7f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLines(p, new[]
                    {
                        new PointF(cx - 4.2f, cy + 0.2f),
                        new PointF(cx - 1.1f, cy + 3.2f),
                        new PointF(cx + 4.4f, cy - 3.4f)
                    });
                }
            }
            else if (hov)
            {
                // 未选中不给圈（六个空心圈看着像表单），只在悬停时给一个淡淡的箭头
                var c = T.Text3;
                float ax = r.Right - 22f, ay = r.Y + r.Height / 2f;
                using (var p = new Pen(c, 1.4f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, ax - 2.5f, ay - 4f, ax + 2.5f, ay);
                    g.DrawLine(p, ax - 2.5f, ay + 4f, ax + 2.5f, ay);
                }
            }
        }

        private void DrawFoot(Graphics g, RectangleF r)
        {
            bool hov = _hover == 6;
            if (hov) T.FillRound(g, r, 9f, T.Alpha(T.Text, 20));

            float ix = r.X + 18f, iy = r.Y + r.Height / 2f;
            var col = hov ? T.Text : T.Text2;
            SunMoon(g, ix, iy, col);

            string label = T.Night ? "切换到日间模式" : "切换到夜间模式";
            T.Draw(g, label, T.F(12f), hov ? T.Text : T.TextSoft, ix + 13f, r.Y + (r.Height - 17f) / 2f);
            T.TextR(g, "Esc 关闭", T.F(10f), T.Text3, r.Right - 10f, r.Y + (r.Height - 15f) / 2f);
        }

        /// <summary>和右上角那个按钮同一套日月图标。</summary>
        private static void SunMoon(Graphics g, float cx, float cy, Color c)
        {
            if (T.Night)
            {
                using (var path = new GraphicsPath())
                using (var reg = new System.Drawing.Region())
                {
                    path.AddEllipse(cx - 5.2f, cy - 5.2f, 10.4f, 10.4f);
                    reg.MakeEmpty();
                    reg.Union(path);
                    using (var cut = new GraphicsPath())
                    {
                        cut.AddEllipse(cx - 1.0f, cy - 7.2f, 10.4f, 10.4f);
                        reg.Exclude(cut);
                    }
                    using (var b = new SolidBrush(c)) g.FillRegion(b, reg);
                }
            }
            else
            {
                using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - 3.2f, cy - 3.2f, 6.4f, 6.4f);
                using (var p = new Pen(c, 1.1f))
                    for (int i = 0; i < 8; i++)
                    {
                        double a = Math.PI * i / 4.0;
                        g.DrawLine(p,
                            cx + (float)Math.Cos(a) * 5.4f, cy + (float)Math.Sin(a) * 5.4f,
                            cx + (float)Math.Cos(a) * 7.6f, cy + (float)Math.Sin(a) * 7.6f);
                    }
            }
        }

        // ---------------- 交互 ----------------

        private int HitTest(Point p)
        {
            var lp = new PointF(p.X / T.S, p.Y / T.S);
            for (int i = 0; i < T.StyleNames.Length; i++)
                if (RowRect(i).Contains(lp)) return i;
            if (FootRect.Contains(lp)) return 6;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = HitTest(e.Location);
            if (i < 0) return;
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (i == 6) _f.ToggleTheme();
            else _f.ApplySkin(i);
            BackColor = T.Bg;
            _hover = HitTest(e.Location);
            Invalidate();   // 换完皮肤，面板自己也跟着换色
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            base.OnKeyDown(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if ((DateTime.Now - _born).TotalMilliseconds < 250) return;   // 刚弹出时的激活抖动不算
            Close();
        }
    }
}
