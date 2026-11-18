using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 平台健康：四站标签页 / 登录态 / 验证码一屏看完，异常可一键重开。
    /// 只读检查，不改账号任何设置。
    /// </summary>
    internal class HealthPage : Page
    {
        private readonly Dictionary<string, SiteHealth> _h = new Dictionary<string, SiteHealth>();
        private int _busy;
        private bool _started;
        private string _summary = "";
        private int _hover;

        public HealthPage(MainForm app) : base(app)
        {
            BackColor = T.Bg;
        }

        public override void RefreshTheme()
        {
            BackColor = T.Bg;
            Invalidate(true);
        }

        private void Ui(Action a)
        {
            try { App.BeginInvoke(a); } catch { }
        }

        public void CheckAll()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            Health.Port = App.Cfg.Port;
            Health.ScriptDir = App.Engine.ScriptDir;
            var sites = App.Cfg.Sites;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    int issues = 0;
                    for (int i = 0; i < sites.Count; i++)
                    {
                        var s = sites[i];
                        if (!s.Enabled) continue;
                        SiteHealth h;
                        try { h = Health.Probe(s); }
                        catch { h = new SiteHealth(); h.Key = s.Key; h.Name = s.Name; h.Note = "检查失败"; }
                        if (h.Note != "正常") issues++;
                        lock (_h) { _h[s.Key] = h; }
                        Ui(delegate { Invalidate(); });
                    }
                    _summary = issues == 0 ? "四站都正常" : ("有 " + issues + " 站需要处理");
                }
                catch (Exception ex) { _summary = "检查出错：" + ex.Message; }
                finally { Volatile.Write(ref _busy, 0); Ui(delegate { Invalidate(); }); }
            });
        }

        private void Reopen(SiteCfg s)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            Health.Port = App.Cfg.Port;
            Health.ScriptDir = App.Engine.ScriptDir;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Health.Reopen(s);
                    var h = Health.Probe(s);
                    lock (_h) { _h[s.Key] = h; }
                    _summary = s.Name + "：" + h.Note;
                }
                catch (Exception ex) { _summary = "重开失败：" + ex.Message; }
                finally { Volatile.Write(ref _busy, 0); Ui(delegate { Invalidate(); }); }
            });
        }

        private SiteHealth Get(string key)
        {
            lock (_h) { SiteHealth h; return _h.TryGetValue(key, out h) ? h : null; }
        }

        private string LastRunOf(string key)
        {
            if (App.Records == null || App.Records.LastRun == null) return "今天还没跑过";
            for (int i = 0; i < App.Records.LastRun.Count; i++)
            {
                var r = App.Records.LastRun[i];
                if (r.SiteKey != key) continue;
                return r.Sent + " 条 · " + r.StopText;
            }
            return "本轮没跑这一站";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(T.Bg);
            g.ScaleTransform(T.S, T.S);
            ClearHits();

            float W = Width / T.S, H = Height / T.S;
            T.Draw(g, "平台健康", Fonts.H1, T.Text, 32, 24);
            T.Draw(g, "四站的标签页 / 登录态 / 验证码一屏看完；异常点右边「重开」即可", Fonts.Small, T.Text3, 32, 62);

            // 右上操作按钮（右边留出窗口三键的位置）
            Ghost(g, new RectangleF(W - 452, 26, 108, 32), "全部检查", "health:check", T.Jade);
            Ghost(g, new RectangleF(W - 334, 26, 108, 32), "全部重开", "health:reopenall", T.Text2);

            if (!_started) { _started = true; CheckAll(); }

            // 顶部 KPI
            bool chrome = Health.ChromeAlive();
            List<string> tabs = Health.Tabs();
            int issues = 0;
            foreach (var s in App.Cfg.Sites) { var h = Get(s.Key); if (h != null && h.Note != "正常") issues++; }
            float kw = (W - 64f - 3 * 14f) / 4f;
            float ky = 100f, kh = 96f;
            Kpi(g, new RectangleF(32, ky, kw, kh), "CDP 端口 " + App.Cfg.Port, chrome ? "已连接" : "连不上",
                chrome ? T.Jade : T.Danger, "浏览器调试通道");
            Kpi(g, new RectangleF(32 + kw + 14, ky, kw, kh), "Chrome", chrome ? Health.ChromeVersion() : "—",
                chrome ? T.Text : T.Text3, chrome ? "连接正常" : "点「打开浏览器」");
            Kpi(g, new RectangleF(32 + (kw + 14) * 2, ky, kw, kh), "标签页", tabs.Count.ToString(),
                T.Text, App.Cfg.Sites.Count + " 站需要各自的页面");
            Kpi(g, new RectangleF(32 + (kw + 14) * 3, ky, kw, kh), "待处理", issues.ToString(),
                issues == 0 ? T.Jade : T.Warn, _busy != 0 ? "检查中…" : (_summary.Length > 0 ? _summary : "尚未检查"));

            // 四站卡片 2×2
            float top = ky + kh + 18f;
            float cw = (W - 64f - 18f) / 2f;
            float chh = Math.Max(150f, Math.Min(196f, (H - top - 40f - 18f) / 2f));
            var sites = App.Cfg.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                float x = 32f + (i % 2) * (cw + 18f);
                float y = top + (i / 2) * (chh + 18f);
                var card = new RectangleF(x, y, cw, chh);
                T.Card(g, card);
                var h = Get(s.Key);
                Color dot = !s.Enabled ? T.Text3 : (h == null ? T.Text3 : (h.Note == "正常" ? T.Jade : (h.Captcha ? T.Danger : T.Warn)));
                T.Dot(g, x + 22, y + 26, 4.5f, dot);
                T.Draw(g, s.Name, Fonts.H2, T.Text, x + 36, y + 16);
                T.Draw(g, s.Enabled ? "已启用" : "已停用", Fonts.Tiny, T.Text3, x + 36, y + 42);

                float ly = y + 68f;
                string tabText = h == null ? (_busy != 0 ? "检查中…" : "未检查") : (h.HasTab ? (h.Tabs + " 个标签页") : "没有标签页");
                string loginText = h == null ? (_busy != 0 ? "检查中…" : "—") : (h.Login == "yes" ? "已登录" : h.Login == "no" ? "未登录" : "无法判断");
                string capText = h == null ? (_busy != 0 ? "检查中…" : "—") : (h.Captcha ? "有验证码" : "无");
                T.Draw(g, "标签页", Fonts.Small, T.Text3, x + 36, ly);
                T.Draw(g, tabText, Fonts.Small, h != null && !h.HasTab ? T.Warn : T.Text2, x + 110, ly);
                ly += 24;
                T.Draw(g, "登录态", Fonts.Small, T.Text3, x + 36, ly);
                T.Draw(g, loginText, Fonts.Small, h != null && h.Login == "no" ? T.Warn : T.Text2, x + 110, ly);
                ly += 24;
                T.Draw(g, "验证码", Fonts.Small, T.Text3, x + 36, ly);
                T.Draw(g, capText, Fonts.Small, h != null && h.Captcha ? T.Danger : T.Text2, x + 110, ly);
                ly += 24;
                T.Draw(g, "最近一轮", Fonts.Small, T.Text3, x + 36, ly);
                T.Draw(g, LastRunOf(s.Key), Fonts.Small, T.Text2, x + 110, ly);

                if (h != null && h.Note.Length > 0 && h.Note != "正常")
                    T.Draw(g, h.Note, Fonts.Tiny, h.Captcha ? T.Danger : T.Warn, x + 36, y + chh - 34);

                Ghost(g, new RectangleF(x + cw - 176, y + chh - 40, 72, 28), "检查", "health:check:" + s.Key, T.Text2);
                Ghost(g, new RectangleF(x + cw - 96, y + chh - 40, 72, 28), "重开", "health:reopen:" + s.Key, T.Jade);
                AddHit("health:check:" + s.Key, new RectangleF(x + cw - 176, y + chh - 40, 72, 28));
                AddHit("health:reopen:" + s.Key, new RectangleF(x + cw - 96, y + chh - 40, 72, 28));
            }
        }

        private void Kpi(Graphics g, RectangleF r, string label, string value, Color vc, string sub)
        {
            T.Card(g, r);
            T.Draw(g, label, Fonts.Small, T.Text3, r.X + 18, r.Y + 14);
            Font vf = value.Length > 9 ? Fonts.H2 : (value.Length > 4 ? Fonts.H1 : Fonts.Big);
            T.Draw(g, value, vf, vc, r.X + 18, r.Y + 36);
            T.Draw(g, sub, Fonts.Tiny, T.Text3, r.X + 18, r.Y + r.Height - 22);
        }

        private void Ghost(Graphics g, RectangleF r, string text, string id, Color accent)
        {
            bool hot = HoverCur.ContainsKey(id) && HoverCur[id] > 0.5f;
            T.FillRound(g, r, 8f, hot ? T.HoverSoftHi : T.HoverSoft);
            T.StrokeRound(g, r, 8f, T.LineSoft);
            var sz = g.MeasureString(text, Fonts.Small);
            T.Draw(g, text, Fonts.Small, accent, r.X + (r.Width - sz.Width) / 2f, r.Y + (r.Height - sz.Height) / 2f);
            AddHit(id, r);
        }

        protected override void OnHit(string id)
        {
            if (id == "health:check") { CheckAll(); return; }
            if (id == "health:reopenall")
            {
                foreach (var s in App.Cfg.Sites) { if (s.Enabled) Reopen(s); }
                return;
            }
            if (id.StartsWith("health:check:")) { CheckAll(); return; }
            if (id.StartsWith("health:reopen:"))
            {
                string key = id.Substring("health:reopen:".Length);
                foreach (var s in App.Cfg.Sites) if (s.Key == key) { Reopen(s); return; }
            }
        }
    }
}
