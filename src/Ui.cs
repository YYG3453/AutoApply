using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>窗口拖动区：顶部 44 逻辑像素 + 四周 8 逻辑像素，都当作标题栏来拖。</summary>
    internal static class UiDrag
    {
        public const int StripHeight = 44;
        public const int Edge = 8;
    }

    /// <summary>所有页面/自绘控件的底座：命中区 + 悬停插值 + 统一的双缓冲绘制。</summary>
    internal abstract class Page : Control
    {
        protected MainForm App;
        protected readonly List<Hit> Hits = new List<Hit>();
        protected readonly Dictionary<string, float> HoverCur = new Dictionary<string, float>();
        protected readonly Dictionary<string, float> HoverTo = new Dictionary<string, float>();
        private readonly Timer _anim;
        protected string Pressed = "";
        protected bool Animating;

        /// <summary>页面出现动画：0 = 刚切过来，1 = 完全显示。约 220ms。</summary>
        protected float Appear = 1f;

        protected class Hit
        {
            public string Id;
            public RectangleF Rect;
            /// <summary>
            /// 局部重绘的范围。默认就是 Rect；当一块视觉被拆成两个命中区
            /// （例如词条本体和右边的 ×）时，两个命中区都要传整块，
            /// 否则悬停动画只重画半边，会在胶囊上留下半截高亮。
            /// </summary>
            public RectangleF Dirty;
        }

        protected Page(MainForm app)
        {
            App = app;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = T.Bg;
            _anim = new Timer { Interval = 16 };
            _anim.Tick += (s, e) => StepAnim();
        }

        protected void AddHit(string id, RectangleF r) { AddHitCore(id, r, r); }

        /// <summary>命中区只占视觉的一部分时用这个：悬停/离开时按 dirty 整块重画。</summary>
        protected void AddHit(string id, RectangleF r, RectangleF dirty) { AddHitCore(id, r, dirty); }

        /// <summary>
        /// 同一个 id 只保留一份。局部重绘时页面会把整块卡片的命中区再报一遍，
        /// 不去重的话 Hits 会一轮一轮地翻倍，旧的矩形也会一直留在里面。
        /// </summary>
        private void AddHitCore(string id, RectangleF r, RectangleF dirty)
        {
            for (int i = 0; i < Hits.Count; i++)
            {
                if (Hits[i].Id != id) continue;
                Hits[i].Rect = r;
                Hits[i].Dirty = dirty;
                return;
            }
            Hits.Add(new Hit { Id = id, Rect = r, Dirty = dirty });
        }

        /// <summary>
        /// 只在整页重绘时清空命中区。悬停动画是局部 Invalidate，一次只重画一块；
        /// 那时 ClearHits 会把其它卡片的命中区一并丢掉，结果是移过去没有悬停、
        /// 点也点不动，得等下一次整页重绘才恢复。
        /// </summary>
        protected void ClearHits() { if (_paintFull) Hits.Clear(); }

        /// <summary>切到本页时调用：内容从底色里浮出来，图表自己按 AppearK 长出来。</summary>
        public void StartAppear()
        {
            Appear = 0f;
            Invalidate();
            if (!_anim.Enabled) _anim.Start();
        }

        /// <summary>0..1 的三次缓出。线性看着像卡顿，缓出才有"弹一下"的感觉。</summary>
        protected float AppearK
        {
            get
            {
                float a = Appear < 0f ? 0f : (Appear > 1f ? 1f : Appear);
                return 1f - (1f - a) * (1f - a) * (1f - a);
            }
        }

        /// <summary>出现动画的收尾：拿底色盖一层，越盖越薄。必须画在本页内容之后。</summary>
        protected void PaintAppear(Graphics g, float w, float h)
        {
            if (Appear >= 0.999f) return;
            int a = (int)(255 * (1f - AppearK));
            if (a <= 2) return;
            using (var b = new SolidBrush(Color.FromArgb(a, T.Bg))) g.FillRectangle(b, 0, 0, w, h);
        }

        protected float H(string id)
        {
            float v;
            return HoverCur.TryGetValue(id, out v) ? v : 0f;
        }

        private void SetTarget(string id, bool on)
        {
            float t = on ? 1f : 0f;
            float cur;
            if (!HoverCur.ContainsKey(id)) HoverCur[id] = 0f;
            cur = HoverCur[id];
            HoverTo[id] = t;
            if (Math.Abs(cur - t) > 0.01f && !_anim.Enabled) _anim.Start();
        }

        private void StepAnim()
        {
            bool busy = false;
            bool full = false;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            if (Appear < 1f)
            {
                Appear += 0.11f;                 // 16ms × ~14 帧 ≈ 220ms
                if (Appear > 1f) Appear = 1f;
                busy = true;
                full = true;                     // 整页在淡入，必须整页重画
            }
            var keys = new List<string>(HoverTo.Keys);
            foreach (var k in keys)
            {
                float cur = HoverCur.ContainsKey(k) ? HoverCur[k] : 0f;
                float to = HoverTo[k];
                float nxt = cur + (to - cur) * 0.28f;
                if (Math.Abs(to - nxt) < 0.01f) nxt = to; else busy = true;
                HoverCur[k] = nxt;
                if (Math.Abs(nxt - cur) < 0.0005f) continue;
                // 悬停动画只脏它自己那块按钮：整页 20ms × 60fps 会白烧掉一个核。
                bool found = false;
                for (int i = 0; i < Hits.Count; i++)
                {
                    if (Hits[i].Id != k) continue;
                    found = true;
                    var r = Hits[i].Dirty.IsEmpty ? Hits[i].Rect : Hits[i].Dirty;
                    if (r.X < x0) x0 = r.X;
                    if (r.Y < y0) y0 = r.Y;
                    if (r.Right > x1) x1 = r.Right;
                    if (r.Bottom > y1) y1 = r.Bottom;
                }
                if (!found) full = true;         // 找不到矩形就只能整页重画，别画出残影
            }
            Animating = busy;
            if (!busy) _anim.Stop();
            if (full || x0 > x1) { Invalidate(); return; }
            float m = 8f * T.S;                  // 逻辑 8px 余量，盖住描边和阴影
            Invalidate(new Rectangle((int)(x0 * T.S - m), (int)(y0 * T.S - m),
                (int)((x1 - x0) * T.S + m * 2), (int)((y1 - y0) * T.S + m * 2)));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var over = HitAt(e.Location);
            // 指针移动时最多只有一项处于悬停：按已知的悬停项全部收回，而不是只遍历 Hits。
            // 局部重绘会重建 Hits（只包含本块矩形内的项），漏掉的那些会把悬停状态卡在 1，
            // 画面上就留下消不掉的高亮；遍历 HoverTo 才能保证它们一定会淡回去。
            string keep = over != null ? over.Id : null;
            foreach (var k in new List<string>(HoverTo.Keys))
                if (k != keep) SetTarget(k, false);
            if (over != null) SetTarget(over.Id, true);
            Cursor = over != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            // 同上：按已知的悬停项收回，Hits 可能只是最近一次局部重绘里的那一小块。
            foreach (var k in new List<string>(HoverTo.Keys)) SetTarget(k, false);
            Cursor = Cursors.Default;
        }

        protected Hit HitAt(Point p)
        {
            p = T.LP(p);
            for (int i = Hits.Count - 1; i >= 0; i--)
                if (Hits[i].Rect.Contains(p)) return Hits[i];
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var h = HitAt(e.Location);
            if (h != null) { Pressed = h.Id; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var h = HitAt(e.Location);
            var was = Pressed;
            Pressed = "";
            Invalidate();
            if (h != null && h.Id == was) OnHit(h.Id);
        }

        protected virtual void OnHit(string id) { }

        protected virtual void OnKey(KeyEventArgs e) { }

        /// <summary>主题切换后刷新底色（自绘内容在下次重绘时自然取新调色板）。</summary>
        public virtual void RefreshTheme()
        {
            BackColor = T.Bg;
            Invalidate(true);
        }

        // ---------- 顶部拖动条：让鼠标穿透到窗体去做 HTCAPTION ----------
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_PAINT = 0x000F;
        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;

        /// <summary>本次 WM_PAINT 是否覆盖整个客户区（整页重绘）；由 WndProc 在绘制前算好。</summary>
        private bool _paintFull = true;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetUpdateRect(IntPtr hWnd, out RECT rect, bool erase);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        private bool PaintCoversClient()
        {
            RECT u, c;
            if (!GetUpdateRect(Handle, out u, false)) return true;   // 没有更新区就当整页
            if (!GetClientRect(Handle, out c)) return true;
            return u.Left <= 0 && u.Top <= 0 && u.Right >= c.Right && u.Bottom >= c.Bottom;
        }

        /// <summary>该点是否落在页面内某个可点区域上（是的话拖动条让位，按钮照常可点）。</summary>
        internal bool HasHitAt(PointF lp)
        {
            for (int i = 0; i < Hits.Count; i++)
                if (Hits[i].Rect.Contains(lp)) return true;
            return false;
        }

        internal int HitCount { get { return Hits.Count; } }

        internal string HitDebug(PointF lp)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("hits=" + Hits.Count);
            for (int i = 0; i < Hits.Count && i < 6; i++)
                sb.Append(" [" + Hits[i].Id + " " + Hits[i].Rect.X.ToString("F0") + "," + Hits[i].Rect.Y.ToString("F0") + " " + Hits[i].Rect.Width.ToString("F0") + "x" + Hits[i].Rect.Height.ToString("F0") + "]");
            sb.Append(" lp=" + lp.X.ToString("F0") + "," + lp.Y.ToString("F0"));
            return sb.ToString();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PAINT)
            {
                // 绘制前问一下这次要盖多大：只有整页重绘才重建命中区。
                _paintFull = PaintCoversClient();
                base.WndProc(ref m);
                _paintFull = true;
                return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                var cp = PointToClient(ScreenPoint(m.LParam));
                if (cp.Y <= T.RS(UiDrag.StripHeight))
                {
                    // 拖动条里的按钮（如日志页右上「清空/复制全部/打开报告」）优先响应点击
                    m.Result = (IntPtr)(HasHitAt(T.LP(cp)) ? HTCLIENT : HTTRANSPARENT);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        internal static Point ScreenPoint(IntPtr lParam)
        {
            long v = lParam.ToInt64();
            return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
        }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            OnKey(e);
        }
    }

    // ---------------- 侧边导航 ----------------
    internal class NavRail : Control
    {
        public string Selected = "dash";
        public event Action<string> Picked;
        // 菜单项来自 Nav 注册表，不再硬编码：加页面只改 Nav.cs 一处
        private readonly Dictionary<string, float> _h = new Dictionary<string, float>();
        private string _hover = "";
        private readonly Timer _t;

        public NavRail()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = T.Bg;
            _t = new Timer { Interval = 16 };
            _t.Tick += (s, e) =>
            {
                bool busy = false;
                foreach (var k in new List<string>(_h.Keys))
                {
                    float to = (k == _hover || k == Selected) ? 1f : 0f;
                    float c = _h[k];
                    float n = c + (to - c) * 0.3f;
                    if (Math.Abs(to - n) < 0.01f) n = to; else busy = true;
                    _h[k] = n;
                }
                if (!busy) _t.Stop();
                Invalidate();
            };
        }

        /// <summary>按注册表算菜单项位置：每个分组先占 24px 标题行，菜单项 44 高、间距 6。</summary>
        private RectangleF ItemRect(int i)
        {
            float y = 96f;
            string group = "";
            for (int k = 0; k <= i && k < Nav.Items.Count; k++)
            {
                if (Nav.Items[k].Group != group) { group = Nav.Items[k].Group; y += 24f; }
                if (k == i) return new RectangleF(14, y, Width - 28, 44);
                y += 50f;
            }
            return RectangleF.Empty;
        }

        private string GroupHeaderAt(int i)
        {
            if (i < 0 || i >= Nav.Items.Count) return "";
            var g = Nav.Items[i].Group;
            if (i == 0) return g;
            return Nav.Items[i - 1].Group == g ? "" : g;    // 同组只画一次
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 左侧栏极淡的竖向渐变
            using (var lg = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), T.RailTop, T.Bg, 90f))
                g.FillRectangle(lg, ClientRectangle);
            using (var p = new Pen(T.LineSoft, 1)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);

            // Logo
            T.Mark(g, new RectangleF(21, 27, 26, 26));

            T.Draw(g, "AUTO", T.Num(17, FontStyle.Bold), T.Text, 56, 22);
            T.Draw(g, "APPLY", T.Num(17, FontStyle.Bold), T.Alpha(T.Gold, 220), 56, 42);
            T.Draw(g, "v1.0 · 桌面版", T.F(11.5f), T.Text3, 56, 65);

            for (int i = 0; i < Nav.Items.Count; i++)
            {
                var item = Nav.Items[i];
                var r = ItemRect(i);
                var head = GroupHeaderAt(i);
                if (head.Length > 0)
                    T.Draw(g, head, T.F(10.5f), T.Text3, 20, r.Y - 20);

                float hv = _h.ContainsKey(item.Key) ? _h[item.Key] : 0f;
                bool sel = Selected == item.Key;
                if (sel)
                {
                    T.FillRound(g, r, 10f, T.RailSel);
                    T.FillRound(g, new RectangleF(r.X + 4, r.Y + 11, 3, 22), 1.5f, T.Jade);
                }
                else if (hv > 0.01f)
                {
                    T.FillRound(g, r, 10f, T.Alpha(T.HoverSoft, (int)(hv * 255)));
                }
                var col = sel ? T.Text : T.Mix(T.Text2, T.Text, hv);
                T.Draw(g, item.Title, T.F(15f, sel ? FontStyle.Bold : FontStyle.Regular), col, r.X + 18, r.Y + 11);
            }

            // 底部：状态
            T.Hairline(g, 18, Width - 18, Height - 74);
            T.Draw(g, "四平台自动投递", T.F(11f), T.Text3, 20, Height - 58);
            T.Draw(g, "BOSS · 智联 · 应届生 · 前程", T.F(10f), T.Text3, 20, Height - 40);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string hit = "";
            for (int i = 0; i < Nav.Items.Count; i++) if (ItemRect(i).Contains(e.Location)) hit = Nav.Items[i].Key;
            if (hit != _hover)
            {
                _hover = hit;
                foreach (var it in Nav.Items) if (!_h.ContainsKey(it.Key)) _h[it.Key] = 0f;
                Cursor = hit.Length > 0 ? Cursors.Hand : Cursors.Default;
                if (!_t.Enabled) _t.Start();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = "";
            if (!_t.Enabled) _t.Start();
        }

        public void RefreshTheme()
        {
            BackColor = T.Bg;
            Invalidate(true);
        }

        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                var cp = PointToClient(Page.ScreenPoint(m.LParam));
                if (cp.Y <= T.RS(UiDrag.StripHeight))
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            for (int i = 0; i < Nav.Items.Count; i++)
            {
                if (ItemRect(i).Contains(e.Location))
                {
                    Selected = Nav.Items[i].Key;
                    if (!_t.Enabled) _t.Start();
                    var h = Picked; if (h != null) h(Selected);
                    Invalidate();
                    return;
                }
            }
        }
    }

    // ---------------- 日志控制台 ----------------
    internal class LogView : Control
    {
        public struct Line { public string Time; public string Level; public string Text; }
        private readonly List<Line> _lines = new List<Line>();
        private int _scroll;          // 起始行号：0 = 最上面一行
        private const int LH = 19;
        private int _dragOff = -1;    // >= 0 表示正在拖滑块：按下时鼠标到滑块顶部的距离
        private bool _barHot;

        public LogView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = T.Console;
        }

        public void RefreshTheme()
        {
            BackColor = T.Console;
            Invalidate(true);
        }

        public void Add(string level, string text)
        {
            // 加之前就在底部，加完继续贴着底走；用户往上翻着看历史时不要把他拽回去
            bool stick = _scroll >= MaxScroll;
            _lines.Add(new Line { Time = DateTime.Now.ToString("HH:mm:ss"), Level = level, Text = text });
            if (_lines.Count > 4000)
            {
                _lines.RemoveRange(0, 1000);
                _scroll = Math.Max(0, _scroll - 1000);
            }
            if (stick) ScrollToEnd();
            Invalidate();
        }

        public void Clear() { _lines.Clear(); _scroll = 0; _dragOff = -1; Invalidate(); }
        public string AllText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in _lines) sb.AppendLine(l.Time + "  " + l.Text);
            return sb.ToString();
        }

        private int VisibleRows { get { return Math.Max(1, (Height - 12) / LH); } }
        private int MaxScroll { get { return Math.Max(0, _lines.Count - VisibleRows); } }

        private void ScrollToEnd() { _scroll = MaxScroll; }

        // 滚动条几何：贴右边缘的一条 6px 轨道，滑块高度按"可见行 / 总行"算
        private RectangleF Track { get { return new RectangleF(Width - 12, 10, 6, Math.Max(8, Height - 20)); } }

        private RectangleF Thumb
        {
            get
            {
                var tr = Track;
                float h = tr.Height * Math.Min(1f, (float)VisibleRows / Math.Max(1, _lines.Count));
                if (h < 28f) h = 28f;
                if (h > tr.Height) h = tr.Height;
                float p = MaxScroll <= 0 ? 0f : (float)_scroll / MaxScroll;
                return new RectangleF(tr.X, tr.Y + (tr.Height - h) * p, tr.Width, h);
            }
        }

        private RectangleF BarZone
        {
            get { var tr = Track; return new RectangleF(tr.X - 6f, tr.Y, tr.Width + 10f, tr.Height); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            T.FillRound(g, new RectangleF(0, 0, Width, Height), 12f, T.Console);
            T.StrokeRound(g, new RectangleF(0, 0, Width, Height), 12f, T.LineSoft);

            var f = T.Mono(12f);
            int rows = VisibleRows;
            int start = Math.Max(0, Math.Min(_scroll, MaxScroll));
            bool bar = MaxScroll > 0;

            // 长行别钻到滚动条底下：正文单独裁一块
            var clip = g.Save();
            g.SetClip(new RectangleF(0, 0, Width - (bar ? 16f : 2f), Height));
            float y = 8;
            for (int i = start; i < Math.Min(_lines.Count, start + rows); i++)
            {
                var l = _lines[i];
                var col = T.Text2;
                if (l.Level == "ok") col = T.Jade;
                else if (l.Level == "warn") col = T.Warn;
                else if (l.Level == "err") col = T.Danger;
                else if (l.Level == "sys") col = T.Gold;
                else if (l.Level == "dim") col = T.Text3;
                T.Draw(g, l.Time, f, T.Text3, 12, y);
                T.Draw(g, l.Text, f, col, 78, y);
                y += LH;
            }
            g.Restore(clip);

            if (_lines.Count == 0)
                T.Draw(g, "等待运行 …", T.Mono(12f), T.Text3, 14, 10);

            if (bar)
            {
                T.FillRound(g, Track, 3f, T.Alpha(T.LineSoft, 190));
                bool active = _dragOff >= 0 || _barHot;
                T.FillRound(g, Thumb, 3f, active ? T.LineHi : T.Line2);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScroll <= 0) return;
            _scroll -= Math.Sign(e.Delta) * 3;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > MaxScroll) _scroll = MaxScroll;
            Invalidate();
        }

        private void DragTo(int mouseY)
        {
            var tr = Track;
            float span = tr.Height - Thumb.Height;
            if (span < 1f) return;
            float p = (mouseY - tr.Y - _dragOff) / span;
            if (p < 0f) p = 0f; else if (p > 1f) p = 1f;
            _scroll = (int)Math.Round(p * MaxScroll);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || MaxScroll <= 0) return;
            if (!BarZone.Contains(e.Location)) return;
            var th = Thumb;
            if (e.Y >= th.Top && e.Y <= th.Bottom)
            {
                _dragOff = (int)(e.Y - th.Top);      // 抓着滑块拖：保持按下的那个点不跳
            }
            else
            {
                _dragOff = (int)(th.Height / 2f);    // 点轨道空白：滑块中心跳过来，然后可以接着拖
                DragTo(e.Y);
            }
            Capture = true;                          // 拖出控件也继续跟手
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragOff >= 0) { DragTo(e.Y); return; }
            bool hot = MaxScroll > 0 && BarZone.Contains(e.Location);
            if (hot != _barHot)
            {
                _barHot = hot;
                Cursor = hot ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragOff < 0) return;
            _dragOff = -1;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragOff >= 0) return;               // 拖动中不算离开
            if (_barHot) { _barHot = false; Invalidate(); }
        }
    }

    // ---------------- 搜索框 ----------------
    /// <summary>
    /// 带占位字的单行输入框：真的 TextBox（中文输入法可用），只是把边框交给外面画。
    /// 空且没焦点时补一行占位字；Esc 清空，省得用户去按右下角那个 ✕。
    /// </summary>
    internal class SearchField : TextBox
    {
        public string Placeholder = "";

        public SearchField()
        {
            BorderStyle = BorderStyle.None;
            BackColor = T.Field;
            ForeColor = T.Text;
            Font = T.F(12.5f);
        }

        public void RefreshTheme()
        {
            BackColor = T.Field;
            ForeColor = T.Text;
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // WM_PAINT：基类画完（空的时候什么都没有）再补占位字，不然得套一个假的 Label
            if (m.Msg == 0x000F && Text.Length == 0 && !Focused)
            {
                using (var g = Graphics.FromHwnd(Handle))
                    TextRenderer.DrawText(g, Placeholder, Font, ClientRectangle, T.Text3,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && Text.Length > 0)
            {
                Text = "";
                e.Handled = e.SuppressKeyPress = true;
            }
        }
    }

    // ---------------- 时间输入 ----------------
    internal class TimeField : Control
    {
        public int Hour = 9, Min = 30;
        public event Action Changed;
        private int _pad = -1;   // 0 hour 1 min
        private readonly Dictionary<string, float> _h = new Dictionary<string, float>();
        private readonly Timer _t;

        public TimeField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = T.Bg;
            _t = new Timer { Interval = 16 };
            _t.Tick += (s, e) => { bool busy = false; foreach (var k in new List<string>(_h.Keys)) { float to = k == _hover ? 1f : 0f; float c = _h[k]; float n = c + (to - c) * 0.3f; if (Math.Abs(to - n) < 0.01f) n = to; else busy = true; _h[k] = n; } if (!busy) _t.Stop(); Invalidate(); };
        }
        private string _hover = "";

        public void RefreshTheme()
        {
            BackColor = T.Bg;
            Invalidate(true);
        }

        private RectangleF Box { get { return new RectangleF(0, 0, Width, Height); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            T.FillRound(g, Box, 9f, T.Field);
            T.StrokeRound(g, Box, 9f, T.Line);
            var s = string.Format("{0:00}:{1:00}", Hour, Min);
            var f = T.Num(15f, FontStyle.Bold);
            var sz = g.MeasureString(s, f);
            T.Draw(g, s, f, T.Text, (Width - sz.Width - 16) / 2f, (Height - sz.Height) / 2f + 1);
            // 上下箭头
            float ax = Width - 22;
            T.Draw(g, "▲", T.F(7f), _hover == "up" ? T.Jade : T.Text3, ax, 5);
            T.Draw(g, "▼", T.F(7f), _hover == "dn" ? T.Jade : T.Text3, ax, Height - 15);
        }

        private void Nudge(int d)
        {
            int total = Hour * 60 + Min + d;
            if (total < 0) total += 24 * 60;
            total %= 24 * 60;
            Hour = total / 60; Min = total % 60;
            Invalidate();
            var h = Changed; if (h != null) h();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string hh = "";
            if (e.Y < Height / 2) hh = "up"; else hh = "dn";
            if (hh != _hover) { _hover = hh; if (!_h.ContainsKey(hh)) _h[hh] = 0f; if (!_t.Enabled) _t.Start(); }
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = ""; if (!_t.Enabled) _t.Start(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int step = 1;
            if ((ModifierKeys & Keys.Shift) == Keys.Shift) step = 5;
            if (e.Y < Height / 2) Nudge(step); else Nudge(-step);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Nudge(e.Delta > 0 ? 1 : -1);
        }
    }
}
