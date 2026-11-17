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

        protected class Hit
        {
            public string Id;
            public RectangleF Rect;
        }

        protected Page(MainForm app)
        {
            App = app;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = T.Bg;
            _anim = new Timer { Interval = 16 };
            _anim.Tick += (s, e) => StepAnim();
        }

        protected void AddHit(string id, RectangleF r) { Hits.Add(new Hit { Id = id, Rect = r }); }
        protected void ClearHits() { Hits.Clear(); }

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
            var keys = new List<string>(HoverTo.Keys);
            foreach (var k in keys)
            {
                float cur = HoverCur.ContainsKey(k) ? HoverCur[k] : 0f;
                float to = HoverTo[k];
                float nxt = cur + (to - cur) * 0.28f;
                if (Math.Abs(to - nxt) < 0.01f) nxt = to; else busy = true;
                HoverCur[k] = nxt;
            }
            Animating = busy;
            if (!busy) _anim.Stop();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var over = HitAt(e.Location);
            foreach (var h in Hits) SetTarget(h.Id, h == over);
            Cursor = over != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            foreach (var h in Hits) SetTarget(h.Id, false);
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
        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;

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
        private int _scroll;          // 0 = 最底部
        private const int LH = 19;

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
            _lines.Add(new Line { Time = DateTime.Now.ToString("HH:mm:ss"), Level = level, Text = text });
            if (_lines.Count > 4000) _lines.RemoveRange(0, 1000);
            if (_scroll == 0) ScrollToEnd();
            Invalidate();
        }

        public void Clear() { _lines.Clear(); _scroll = 0; Invalidate(); }
        public string AllText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in _lines) sb.AppendLine(l.Time + "  " + l.Text);
            return sb.ToString();
        }

        private void ScrollToEnd()
        {
            int rows = Math.Max(1, (Height - 12) / LH);
            _scroll = Math.Max(0, _lines.Count - rows);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            T.FillRound(g, new RectangleF(0, 0, Width, Height), 12f, T.Console);
            T.StrokeRound(g, new RectangleF(0, 0, Width, Height), 12f, T.LineSoft);

            var f = T.Mono(12f);
            int rows = Math.Max(1, (Height - 12) / LH);
            int start = Math.Max(0, Math.Min(_scroll, Math.Max(0, _lines.Count - rows)));
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
            if (_lines.Count == 0)
                T.Draw(g, "等待运行 …", T.Mono(12f), T.Text3, 14, 10);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int rows = Math.Max(1, (Height - 12) / LH);
            _scroll -= Math.Sign(e.Delta) * 3;
            _scroll = Math.Max(0, Math.Min(_scroll, Math.Max(0, _lines.Count - rows)));
            Invalidate();
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
