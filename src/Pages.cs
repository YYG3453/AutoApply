using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace AutoApply
{
    internal static class Fonts
    {
        public static readonly Font H1 = T.F(23f, FontStyle.Bold);
        public static readonly Font H2 = T.F(14f, FontStyle.Bold);
        public static readonly Font Body = T.F(12.5f);
        public static readonly Font Small = T.F(11f);
        public static readonly Font Tiny = T.F(10f);
        public static readonly Font Big = T.Num(31f, FontStyle.Bold);
        public static readonly Font Num = T.Num(13f, FontStyle.Bold);
        public static readonly Font Mono = T.Mono(11.5f);
    }

    // ======================= 仪表盘 =======================
    internal class DashboardPage : Page
    {
        public DashboardPage(MainForm app) : base(app) { }

        private static string SiteColorName(int hue) { return hue == 0 ? "BOSS直聘" : hue == 1 ? "智联招聘" : hue == 2 ? "应届生" : "前程无忧"; }
        private static Color Hue(int hue)
        {
            switch (hue) { case 1: return T.Blue; case 2: return T.Gold; case 3: return T.Warn; default: return T.Jade; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            var W = Width / T.S; var Hh = Height / T.S;
            ClearHits();

            var eng = App.Engine;
            var st = App.Stats;
            bool running = eng != null && eng.Running;

            // 标题
            T.Draw(g, "仪表盘", Fonts.H1, T.Text, 32, 20);
            T.Draw(g, "四平台自动投递 · 城市 " + App.Cfg.Cities.Count + " 个 · 关键词 " + App.Cfg.Keywords.Count + " 组", Fonts.Small, T.Text2, 32, 55);

            // 状态胶囊
            var pr = new RectangleF(W - 112 - 18 - 126, 24, 126, 30);
            T.FillRound(g, pr, 15f, Color.FromArgb(0x18, 0x1D, 0x24));
            T.StrokeRound(g, pr, 15f, running ? T.Alpha(T.Jade, 90) : T.Line);
            T.Dot(g, pr.X + 18, pr.Y + 15, 4.5f, running ? T.Jade : (eng != null && eng.LastStop == "done" ? T.Text3 : T.Text3));
            T.Draw(g, running ? "运行中" : "待命中", Fonts.Small, running ? T.Jade : T.Text2, pr.X + 30, pr.Y + 7);

            // ---- 四个统计卡 ----
            float gap = 14, top = 92, ch = 96;
            float cw = (W - 64 - gap * 3) / 4f;
            string[] labels = { "今日投递", "累计投递", "参与平台", "下次运行" };
            string[] values = { st.Today.ToString(), st.Total.ToString(), App.EnabledSiteCount().ToString(), App.NextRunText() };
            string[] subs = { "今天已发出", "历史总计", "四站中已启用", App.NextRunSub() };
            for (int i = 0; i < 4; i++)
            {
                var r = new RectangleF(32 + i * (cw + gap), top, cw, ch);
                T.Card(g, r, 14f);
                T.Draw(g, labels[i], Fonts.Small, T.Text2, r.X + 18, r.Y + 15);
                T.Draw(g, values[i], Fonts.Big, i == 0 ? T.Jade : T.Text, r.X + 17, r.Y + 36);
                T.Draw(g, subs[i], Fonts.Tiny, T.Text3, r.X + 18, r.Y + 70);
                // 右上角装饰细线
                using (var p = new Pen(T.Alpha(T.Gold, 40), 1)) g.DrawLine(p, r.Right - 54, r.Y + 22, r.Right - 18, r.Y + 22);
            }

            // ---- 本次运行 ----
            var run = new RectangleF(32, 204, W - 64 - 296 - 14, 300);
            T.Card(g, run, 16f);
            T.Draw(g, "本次运行", Fonts.H2, T.Text, run.X + 20, run.Y + 18);
            var statusText = running ? "进行中 · " + (eng.CurrentSiteName ?? "") : (eng != null && eng.Results.Count > 0 ? "上一轮：" + eng.RunWindowName : "尚未运行");
            T.TextR(g, statusText, Fonts.Small, running ? T.Jade : T.Text3, run.Right - 20, run.Y + 21);

            float ly = run.Y + 58;
            var sites = App.Cfg.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                var row = new RectangleF(run.X + 16, ly + i * 46, run.Width - 32, 38);
                T.FillRound(g, row, 10f, Color.FromArgb(0x12, 0x16, 0x1C));
                T.Dot(g, row.X + 16, row.Y + 19, 4f, s.Enabled ? Hue(s.Hue) : T.Text3);
                T.Draw(g, s.Name, Fonts.Body, s.Enabled ? T.Text : T.Text3, row.X + 30, row.Y + 10);

                var res = App.ResultOf(s.Key);
                string right = "";
                Color rc = T.Text3;
                if (res != null)
                {
                    if (res.Stop == "notab") { right = "无标签页"; rc = T.Warn; }
                    else if (res.Sent > 0) { right = "已投 " + res.Sent; rc = T.Jade; }
                    else if (res.Stop == "limit") { right = "撞上限"; rc = T.Warn; }
                    else if (res.Stop == "captcha") { right = "需验证"; rc = T.Warn; }
                    else right = "0";
                }
                else if (running && eng.CurrentSite == s.Key) { right = "运行中…"; rc = T.Jade; }
                else if (!s.Enabled) { right = "已停用"; }
                if (right.Length > 0) T.TextR(g, right, Fonts.Small, rc, row.Right - 14, row.Y + 10);
            }

            // 开始 / 停止
            var b1 = new RectangleF(run.X + 16, run.Bottom - 56, 132, 40);
            var b2 = new RectangleF(run.X + 158, run.Bottom - 56, 96, 40);
            DrawPrimary(g, b1, "开始投递", "start", !running);
            DrawGhost(g, b2, "停止", "stop", running);
            T.Draw(g, "开始后按 计划 里设定的时长，逐站跑完自动结束", Fonts.Tiny, T.Text3, b2.Right + 14, run.Bottom - 38);

            // ---- 平台列 ----
            var side = new RectangleF(run.Right + 14, 204, 296, 300);
            T.Card(g, side, 16f);
            T.Draw(g, "平台 · 今天", Fonts.H2, T.Text, side.X + 20, side.Y + 18);
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                float y = side.Y + 56 + i * 58;
                T.Dot(g, side.X + 22, y + 8, 4f, Hue(s.Hue));
                T.Draw(g, s.Name, Fonts.Body, T.Text, side.X + 34, y - 2);
                T.TextR(g, App.Stats.CountToday(s.Key).ToString(), Fonts.Num, T.Text, side.Right - 20, y - 2);
                // 细进度条：以 max 为刻度
                var bar = new RectangleF(side.X + 20, y + 26, side.Width - 40, 3);
                T.FillRound(g, bar, 1.5f, Color.FromArgb(0x22, 0x27, 0x30));
                float pct = s.Max > 0 ? Math.Min(1f, App.Stats.CountToday(s.Key) / (float)Math.Max(1, s.Max / 4)) : 0f;
                if (pct > 0.02f)
                {
                    var fill = new RectangleF(bar.X, bar.Y, bar.Width * pct, bar.Height);
                    T.FillRound(g, fill, 1.5f, Hue(s.Hue));
                }
            }

            // ---- 今日城市 ----
            var cityCard = new RectangleF(32, 520, W - 64, Math.Max(120, Hh - 520 - 20));
            T.Card(g, cityCard, 16f);
            T.Draw(g, "今日城市分布", Fonts.H2, T.Text, cityCard.X + 20, cityCard.Y + 18);
            T.TextR(g, "hover 查看 · 来自 BOSS 记录", Fonts.Tiny, T.Text3, cityCard.Right - 20, cityCard.Y + 21);
            var cityTop = new List<KeyValuePair<string, int>>(App.Stats.TodayByCity);
            cityTop.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (cityTop.Count == 0)
                T.Draw(g, "今天还没有记录", Fonts.Small, T.Text3, cityCard.X + 20, cityCard.Y + 58);
            int maxV = 1;
            foreach (var kv in cityTop) if (kv.Value > maxV) maxV = kv.Value;
            for (int i = 0; i < Math.Min(9, cityTop.Count); i++)
            {
                float bw = (cityCard.Width - 40) / 9f;
                float x = cityCard.X + 20 + i * bw;
                float hBar = Math.Max(4, (cityCard.Height - 92) * (cityTop[i].Value / (float)maxV));
                var br = new RectangleF(x + 6, cityCard.Bottom - 34 - hBar, bw - 18, hBar);
                using (var lg = new LinearGradientBrush(new RectangleF(br.X, br.Y, Math.Max(2, br.Width), Math.Max(2, br.Height)), T.Alpha(T.Jade, 200), T.Alpha(T.JadeDim, 90), 90f))
                    T.FillRound(g, br, 4f, lg);
                T.TextC(g, cityTop[i].Value.ToString(), Fonts.Tiny, T.Text2, new RectangleF(x, cityCard.Bottom - 60, bw, 16));
                T.TextC(g, cityTop[i].Key, Fonts.Tiny, T.Text3, new RectangleF(x, cityCard.Bottom - 26, bw, 16));
            }
        }

        private void DrawPrimary(Graphics g, RectangleF r, string text, string id, bool enabled)
        {
            float hv = H(id);
            var col1 = enabled ? T.Mix(Color.FromArgb(0x8A, 0xEC, 0xC7), Color.FromArgb(0xA6, 0xF5, 0xD8), hv) : Color.FromArgb(0x2A, 0x30, 0x39);
            var col2 = enabled ? T.Mix(Color.FromArgb(0x50, 0xC6, 0xA4), Color.FromArgb(0x5F, 0xD8, 0xB4), hv) : Color.FromArgb(0x24, 0x29, 0x31);
            using (var lg = new LinearGradientBrush(new RectangleF(r.X, r.Y, Math.Max(2, r.Width), Math.Max(2, r.Height)), col1, col2, 35f))
                T.FillRound(g, r, 11f, lg);
            T.TextC(g, text, T.F(13f, FontStyle.Bold), enabled ? Color.FromArgb(0x08, 0x2A, 0x22) : T.Text3, r);
            AddHit(id, r);
        }

        private void DrawGhost(Graphics g, RectangleF r, string text, string id, bool active)
        {
            float hv = H(id);
            T.FillRound(g, r, 11f, T.Mix(Color.FromArgb(0x15, 0x19, 0x20), Color.FromArgb(0x22, 0x28, 0x31), hv));
            T.StrokeRound(g, r, 11f, active ? T.Mix(T.Line, T.Danger, hv * 0.6f) : T.LineSoft);
            T.TextC(g, text, T.F(12.5f), active ? T.Mix(T.Text2, T.Danger, hv) : T.Text3, r);
            if (active) AddHit(id, r);
        }

        protected override void OnHit(string id)
        {
            if (id == "start") App.StartRunManual();
            else if (id == "stop") App.StopRun();
        }
    }

    // ======================= 投递计划 =======================
    internal class PlanPage : Page
    {
        private readonly List<TimeField> _fields = new List<TimeField>();

        public PlanPage(MainForm app) : base(app)
        {
            BuildFields();
            App.CfgChanged += Rebuild;
        }

        private void BuildFields()
        {
            foreach (var f in _fields) { Controls.Remove(f); f.Dispose(); }
            _fields.Clear();
            var w = App.Cfg.Windows;
            for (int i = 0; i < w.Count; i++)
            {
                var a = new TimeField { Hour = w[i].StartHour, Min = w[i].StartMin };
                var b = new TimeField { Hour = w[i].EndHour, Min = w[i].EndMin };
                int idx = i;
                a.Changed += () => { w[idx].StartHour = a.Hour; w[idx].StartMin = a.Min; App.SaveCfg(); };
                b.Changed += () => { w[idx].EndHour = b.Hour; w[idx].EndMin = b.Min; App.SaveCfg(); };
                Controls.Add(a); Controls.Add(b);
                _fields.Add(a); _fields.Add(b);
            }
            LayoutFields();
        }

        private void Rebuild() { }

        private void LayoutFields()
        {
            var w = App.Cfg.Windows;
            for (int i = 0; i < w.Count; i++)
            {
                float rowY = 88 + 66 + i * 62;
                if (_fields.Count < (i + 1) * 2) break;
                _fields[i * 2].Bounds = new Rectangle(T.RS(112), T.RS(rowY), T.RS(74), T.RS(34));
                _fields[i * 2 + 1].Bounds = new Rectangle(T.RS(212), T.RS(rowY), T.RS(74), T.RS(34));
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutFields(); }

        private void DrawSwitch(Graphics g, RectangleF r, bool on, string id)
        {
            float hv = H(id);
            var track = on ? T.Mix(T.JadeDim, T.Jade, hv * 0.35f) : T.Mix(Color.FromArgb(0x2A, 0x30, 0x3A), Color.FromArgb(0x33, 0x3A, 0x46), hv);
            T.FillRound(g, r, r.Height / 2f, track);
            float knobR = r.Height - 8;
            float kx = on ? r.Right - knobR - 4 : r.X + 4;
            using (var b = new SolidBrush(on ? Color.FromArgb(0x07, 0x24, 0x1C) : Color.FromArgb(0xC9, 0xCF, 0xDA)))
                g.FillEllipse(b, kx, r.Y + 4, knobR, knobR);
            AddHit(id, new RectangleF(r.X - 6, r.Y - 6, r.Width + 12, r.Height + 12));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            var W = Width / T.S;
            ClearHits();
            var cfg = App.Cfg;

            T.Draw(g, "投递计划", Fonts.H1, T.Text, 32, 20);
            T.Draw(g, "到点自动开始；程序最小化到托盘也会照常执行", Fonts.Small, T.Text2, 32, 55);

            // 定时卡片
            var card = new RectangleF(32, 88, W - 64, 250);
            T.Card(g, card, 16f);
            T.Draw(g, "定时投递总开关", Fonts.H2, T.Text, card.X + 20, card.Y + 18);
            T.Draw(g, cfg.ScheduleEnabled ? "已开启 · 每天到点自动跑" : "已关闭 · 只能手动开始", Fonts.Small, cfg.ScheduleEnabled ? T.Jade : T.Text3, card.X + 150, card.Y + 21);
            DrawSwitch(g, new RectangleF(card.Right - 82, card.Y + 14, 58, 30), cfg.ScheduleEnabled, "sched");

            // 两个时间窗
            for (int i = 0; i < cfg.Windows.Count; i++)
            {
                var w = cfg.Windows[i];
                float y = 88 + 66 + i * 62;
                var row = new RectangleF(32 + 12, y - 8, W - 64 - 24, 50);
                T.FillRound(g, row, 12f, Color.FromArgb(0x12, 0x16, 0x1C));
                T.Draw(g, w.Name, Fonts.Body, w.Enabled ? T.Text : T.Text3, row.X + 16, row.Y + 16);
                T.Draw(g, "从", Fonts.Small, T.Text3, row.X + 82, row.Y + 17);
                T.Draw(g, "到", Fonts.Small, T.Text3, row.X + 192, row.Y + 17);
                DrawSwitch(g, new RectangleF(row.X + 296, row.Y + 12, 52, 26), w.Enabled, "w" + i);
                DrawSmallGhost(g, new RectangleF(row.Right - 116, row.Y + 11, 100, 28), "立即执行一次", "run" + i);
                T.TextR(g, w.Enabled ? "每天 " + w.Range : "未启用", Fonts.Tiny, T.Text3, row.Right - 128, row.Y + 19);
            }

            // 每站时长
            var card2 = new RectangleF(32, 356, W - 64, 236);
            T.Card(g, card2, 16f);
            T.Draw(g, "每站时长与上限", Fonts.H2, T.Text, card2.X + 20, card2.Y + 18);
            T.Draw(g, "时长到点会自己收工；上限是兜底，防止某个站卡住", Fonts.Tiny, T.Text3, card2.X + 150, card2.Y + 22);
            for (int i = 0; i < cfg.Sites.Count; i++)
            {
                var s = cfg.Sites[i];
                float y = card2.Y + 58 + i * 44;
                T.Dot(g, card2.X + 26, y + 18, 4f, s.Enabled ? new[] { T.Jade, T.Blue, T.Gold, T.Warn }[s.Hue] : T.Text3);
                T.Draw(g, s.Name, Fonts.Body, T.Text, card2.X + 40, y + 9);
                DrawStepper(g, card2.X + 200, y + 4, "m" + i, s.Minutes, "分钟", 1, 120);
                DrawStepper(g, card2.X + 400, y + 4, "x" + i, s.Max, "上限", 5, 500);
                DrawSwitch(g, new RectangleF(card2.Right - 78, y + 7, 52, 26), s.Enabled, "en" + i);
            }

            T.Draw(g, "提示：每天 0 点各平台额度重置；BOSS 每日沟通上限约 150 次，撞上限会自动让位给后面三站。",
                Fonts.Tiny, T.Text3, 34, card2.Bottom + 16);
        }

        private void DrawSmallGhost(Graphics g, RectangleF r, string text, string id)
        {
            float hv = H(id);
            T.FillRound(g, r, 8f, T.Mix(Color.FromArgb(0x18, 0x1D, 0x25), Color.FromArgb(0x24, 0x2B, 0x35), hv));
            T.StrokeRound(g, r, 8f, T.Mix(T.Line, T.Jade, hv * 0.5f));
            T.TextC(g, text, T.F(11.5f), T.Mix(T.Text2, T.Text, hv), r);
            AddHit(id, r);
        }

        private void DrawStepper(Graphics g, float x, float y, string id, int value, string unit, int step, int max)
        {
            var box = new RectangleF(x, y, 150, 34);
            T.FillRound(g, box, 9f, Color.FromArgb(0x18, 0x1C, 0x23));
            T.StrokeRound(g, box, 9f, T.Line);
            var minus = new RectangleF(box.X + 3, box.Y + 3, 30, box.Height - 6);
            var plus = new RectangleF(box.Right - 33, box.Y + 3, 30, box.Height - 6);
            float hm = H(id + "-"), hp = H(id + "+");
            T.FillRound(g, minus, 7f, T.Alpha(Color.FromArgb(0x26, 0x2D, 0x37), (int)(60 + hm * 140)));
            T.FillRound(g, plus, 7f, T.Alpha(Color.FromArgb(0x26, 0x2D, 0x37), (int)(60 + hp * 140)));
            T.TextC(g, "−", T.F(14f), T.Mix(T.Text2, T.Danger, hm), minus);
            T.TextC(g, "+", T.F(14f), T.Mix(T.Text2, T.Jade, hp), plus);
            var val = value.ToString() + " " + unit;
            T.TextC(g, val, Fonts.Small, T.Text, new RectangleF(box.X + 30, box.Y, box.Width - 66, box.Height));
            AddHit(id + "-", minus);
            AddHit(id + "+", plus);
        }

        protected override void OnHit(string id)
        {
            var cfg = App.Cfg;
            if (id == "sched") { cfg.ScheduleEnabled = !cfg.ScheduleEnabled; App.SaveCfg(); Invalidate(); return; }
            if (id.StartsWith("w"))
            {
                int i = int.Parse(id.Substring(1));
                cfg.Windows[i].Enabled = !cfg.Windows[i].Enabled;
                App.SaveCfg(); Invalidate(); return;
            }
            if (id.StartsWith("run"))
            {
                int i = int.Parse(id.Substring(3));
                App.StartRunManual();
                return;
            }
            if (id.StartsWith("en"))
            {
                int i = int.Parse(id.Substring(2));
                cfg.Sites[i].Enabled = !cfg.Sites[i].Enabled;
                App.SaveCfg(); Invalidate(); return;
            }
            if (id.StartsWith("m") || id.StartsWith("x"))
            {
                int i = int.Parse(id.Substring(1, id.Length - 2));
                int dir = id.EndsWith("+") ? 1 : -1;
                var s = cfg.Sites[i];
                if (id[0] == 'm') s.Minutes = Math.Max(1, Math.Min(120, s.Minutes + dir));
                else s.Max = Math.Max(5, Math.Min(500, s.Max + dir * 5));
                App.SaveCfg(); Invalidate();
            }
        }
    }

    // ======================= 平台与关键词 =======================
    internal class SitesPage : Page
    {
        private readonly TextBox _kw;
        private readonly TextBox _chrome;
        private RectangleF _cityArea;

        public SitesPage(MainForm app) : base(app)
        {
            _kw = new DarkText();
            _kw.BackColor = Color.FromArgb(0x14, 0x18, 0x1F);
            _kw.Text = string.Join("，", app.Cfg.Keywords.ToArray());
            _kw.TextChanged += (s, e) =>
            {
                var list = new List<string>();
                foreach (var part in _kw.Text.Split(new[] { ',', '，', '、', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = part.Trim();
                    if (t.Length > 0 && !list.Contains(t)) list.Add(t);
                }
                app.Cfg.Keywords = list;
            };
            Controls.Add(_kw);

            _chrome = new DarkText();
            _chrome.BackColor = Color.FromArgb(0x14, 0x18, 0x1F);
            _chrome.Text = app.Cfg.ChromePath;
            _chrome.TextChanged += (s, e) => { app.Cfg.ChromePath = _chrome.Text.Trim(); };
            Controls.Add(_chrome);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }
        private void Relayout()
        {
            float w = Width / T.S;
            _kw.Bounds = new Rectangle(T.RS(52), T.RS(136), T.RS(w - 104), T.RS(32));
            _chrome.Bounds = new Rectangle(T.RS(52), T.RS(452), T.RS(w - 260), T.RS(30));
        }

        private void DrawField(Graphics g, Control c)
        {
            if (c == null) return;
            var r = new RectangleF(c.Left / T.S, c.Top / T.S, c.Width / T.S, c.Height / T.S);
            var outer = new RectangleF(r.X - 6, r.Y - 6, r.Width + 12, r.Height + 12);
            T.FillRound(g, outer, 10f, Color.FromArgb(0x14, 0x18, 0x1F));
            T.StrokeRound(g, outer, 10f, T.Line);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            var W = Width / T.S;
            ClearHits();
            var cfg = App.Cfg;

            T.Draw(g, "平台与关键词", Fonts.H1, T.Text, 32, 20);
            T.Draw(g, "关键词会轮流搜；城市决定筛选范围", Fonts.Small, T.Text2, 32, 55);

            var c1 = new RectangleF(32, 92, W - 64, 118);
            T.Card(g, c1, 16f);
            T.Draw(g, "搜索关键词", Fonts.H2, T.Text, c1.X + 20, c1.Y + 16);
            T.Draw(g, "用逗号或空格分隔", Fonts.Tiny, T.Text3, c1.X + 108, c1.Y + 20);
            T.Draw(g, "例如：实施工程师，实施顾问，软件实施，交付实施", Fonts.Tiny, T.Text3, c1.X + 20, c1.Y + 96);
            DrawField(g, _kw);

            var c2 = new RectangleF(32, 224, W - 64, 168);
            T.Card(g, c2, 16f);
            T.Draw(g, "目标城市", Fonts.H2, T.Text, c2.X + 20, c2.Y + 16);
            T.Draw(g, "已选 " + cfg.Cities.Count + " 个", Fonts.Tiny, T.Text3, c2.X + 92, c2.Y + 20);
            DrawSmallGhost(g, new RectangleF(c2.Right - 178, c2.Y + 12, 74, 26), "全选", "all");
            DrawSmallGhost(g, new RectangleF(c2.Right - 96, c2.Y + 12, 74, 26), "清空", "none");

            var all = AppCfg.DefaultCities();
            float x = c2.X + 20, y = c2.Y + 58;
            for (int i = 0; i < all.Length; i++)
            {
                var name = all[i];
                bool on = cfg.Cities.Contains(name);
                var sz = g.MeasureString(name, Fonts.Small);
                float wChip = sz.Width + 26;
                if (x + wChip > c2.Right - 20) { x = c2.X + 20; y += 36; }
                var r = new RectangleF(x, y, wChip, 28);
                string id = "city:" + name;
                float hv = H(id);
                T.FillRound(g, r, 14f, on ? T.Alpha(T.JadeDim, 70 + (int)(hv * 40)) : Color.FromArgb(0x18, 0x1C, 0x23));
                T.StrokeRound(g, r, 14f, on ? T.Alpha(T.Jade, 150) : T.Mix(T.Line, T.Text3, hv * 0.6f));
                T.TextC(g, name, Fonts.Small, on ? T.Jade : T.Mix(T.Text2, T.Text, hv), r);
                AddHit(id, r);
                x += wChip + 8;
            }
            _cityArea = new RectangleF(c2.X, c2.Y, c2.Width, c2.Height);

            var c3 = new RectangleF(32, 408, W - 64, 112);
            T.Card(g, c3, 16f);
            T.Draw(g, "浏览器", Fonts.H2, T.Text, c3.X + 20, c3.Y + 14);
            DrawField(g, _chrome);
            T.Draw(g, "留空自动查找 Chrome / Edge；换机器时也可以手动填路径", Fonts.Tiny, T.Text3, c3.X + 20, c3.Y + 84);
            DrawSmallGhost(g, new RectangleF(c3.Right - 168, c3.Y + 46, 66, 30), "自动查找", "find");
            DrawSmallGhost(g, new RectangleF(c3.Right - 94, c3.Y + 46, 74, 30), "打开浏览器", "open");

            T.Draw(g, "登录状态保存在程序目录的 browser-profile 文件夹里；换电脑后需要重新登录一次四个平台。",
                Fonts.Tiny, T.Text3, 34, c3.Bottom + 14);
        }

        private void DrawSmallGhost(Graphics g, RectangleF r, string text, string id)
        {
            float hv = H(id);
            T.FillRound(g, r, 8f, T.Mix(Color.FromArgb(0x18, 0x1D, 0x25), Color.FromArgb(0x24, 0x2B, 0x35), hv));
            T.StrokeRound(g, r, 8f, T.Mix(T.Line, T.Jade, hv * 0.5f));
            T.TextC(g, text, T.F(11.5f), T.Mix(T.Text2, T.Text, hv), r);
            AddHit(id, r);
        }

        protected override void OnHit(string id)
        {
            var cfg = App.Cfg;
            if (id.StartsWith("city:"))
            {
                var name = id.Substring(5);
                if (cfg.Cities.Contains(name)) cfg.Cities.Remove(name); else cfg.Cities.Add(name);
                App.SaveCfg(); Invalidate(); return;
            }
            if (id == "all") { cfg.Cities = new List<string>(AppCfg.DefaultCities()); App.SaveCfg(); Invalidate(); return; }
            if (id == "none") { cfg.Cities = new List<string>(); App.SaveCfg(); Invalidate(); return; }
            if (id == "find")
            {
                var p = App.Engine.FindChrome();
                if (p != null) { _chrome.Text = p; cfg.ChromePath = p; App.SaveCfg(); App.Toast("已找到：" + p); }
                else App.Toast("没找到浏览器，请手动填写路径");
                Invalidate(); return;
            }
            if (id == "open")
            {
                string msg;
                App.Engine.EnsureChrome(out msg);
                App.Toast(msg);
            }
        }
    }

    internal class DarkText : TextBox
    {
        public DarkText()
        {
            BorderStyle = BorderStyle.None;
            BackColor = Color.FromArgb(0x18, 0x1C, 0x23);
            ForeColor = T.Text;
            Font = T.F(13f);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(T.LineSoft)) { }
        }
    }

    // ======================= 日志 =======================
    internal class LogPage : Page
    {
        public readonly LogView View;

        public LogPage(MainForm app) : base(app)
        {
            View = new LogView();
            Controls.Add(View);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }
        private void Relayout()
        {
            var w = Width / T.S;
            View.Bounds = new Rectangle(T.RS(32), T.RS(100), T.RS(w - 64), T.RS(Height / T.S - 132));
        }

        private void DrawSmallGhost(Graphics g, RectangleF r, string text, string id)
        {
            float hv = H(id);
            T.FillRound(g, r, 8f, T.Mix(Color.FromArgb(0x18, 0x1D, 0x25), Color.FromArgb(0x24, 0x2B, 0x35), hv));
            T.StrokeRound(g, r, 8f, T.Mix(T.Line, T.Jade, hv * 0.5f));
            T.TextC(g, text, T.F(11.5f), T.Mix(T.Text2, T.Text, hv), r);
            AddHit(id, r);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            var W = Width / T.S;
            ClearHits();
            T.Draw(g, "运行日志", Fonts.H1, T.Text, 32, 20);
            T.Draw(g, "实时输出 · 鼠标滚轮翻看历史", Fonts.Small, T.Text2, 32, 55);
            DrawSmallGhost(g, new RectangleF(W - 268, 26, 74, 30), "清空", "clear");
            DrawSmallGhost(g, new RectangleF(W - 186, 26, 84, 30), "复制全部", "copy");
            DrawSmallGhost(g, new RectangleF(W - 94, 26, 94, 30), "打开报告", "report");
        }

        protected override void OnHit(string id)
        {
            if (id == "clear") View.Clear();
            else if (id == "copy") { try { if (View.AllText().Length > 0) Clipboard.SetText(View.AllText()); } catch { } App.Toast("日志已复制"); }
            else if (id == "report") App.OpenReport();
        }
    }

    // ======================= 关于 =======================
    internal class AboutPage : Page
    {
        public AboutPage(MainForm app) : base(app) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            var W = Width / T.S;
            ClearHits();

            var card = new RectangleF(32, 92, W - 64, 300);
            T.Card(g, card, 18f);
            float cx = card.X + 36, cy = card.Y + 34;
            using (var br = new LinearGradientBrush(new RectangleF(cx, cy, 46, 46), T.Jade, T.JadeDim, 45f))
                g.FillEllipse(br, cx, cy, 46, 46);
            T.Dot(g, cx + 23, cy + 23, 9, T.Bg);
            using (var p = new Pen(T.Alpha(T.Gold, 120), 1.2f)) g.DrawArc(p, cx - 8, cy - 8, 62, 62, 200, 140);

            T.Draw(g, "AUTO APPLY", T.Num(20f, FontStyle.Bold), T.Text, cx + 62, cy - 2);
            T.Draw(g, "四平台自动投递 · 桌面版 v" + MainForm.Version, Fonts.Small, T.Text2, cx + 62, cy + 26);

            string[] lines =
            {
                "把 BOSS直聘 / 智联招聘 / 应届生求职 / 前程无忧 的投递动作串成一条自动化流水线。",
                "点「开始投递」或开着定时，程序会接管浏览器逐站投简历，并把每一条记录写进本地记录文件。",
                "",
                "· 单文件 exe，自带 node 运行时，解压即用，不用装任何环境",
                "· 登录态保存在 browser-profile 目录，换机器重新登录一次即可",
                "· 报告在 scripts\\report.md，可用「运行日志 → 打开报告」直接看",
            };
            float y = card.Y + 132;
            foreach (var l in lines) { T.Draw(g, l, Fonts.Body, l.StartsWith("·") ? T.Text2 : T.Text2, card.X + 36, y); y += 30; }

            var c2 = new RectangleF(32, 408, W - 64, 120);
            T.Card(g, c2, 16f);
            T.Draw(g, "快捷键与提示", Fonts.H2, T.Text, c2.X + 20, c2.Y + 14);
            T.Draw(g, "F5 立即开始一轮    Esc 停止    最小化后可继续在托盘运行", Fonts.Small, T.Text2, c2.X + 20, c2.Y + 46);
            T.Draw(g, "若某个平台提示需要验证（滑块 / 扫码），点「打开浏览器」手动处理一次即可。", Fonts.Small, T.Text2, c2.X + 20, c2.Y + 74);

            DrawSmallGhost(g, new RectangleF(c2.Right - 150, c2.Y + 60, 132, 32), "打开程序目录", "dir");
        }

        private void DrawSmallGhost(Graphics g, RectangleF r, string text, string id)
        {
            float hv = H(id);
            T.FillRound(g, r, 8f, T.Mix(Color.FromArgb(0x18, 0x1D, 0x25), Color.FromArgb(0x24, 0x2B, 0x35), hv));
            T.StrokeRound(g, r, 8f, T.Mix(T.Line, T.Jade, hv * 0.5f));
            T.TextC(g, text, T.F(11.5f), T.Mix(T.Text2, T.Text, hv), r);
            AddHit(id, r);
        }

        protected override void OnHit(string id)
        {
            if (id == "dir") Process.Start("explorer.exe", App.BaseDir);
        }
    }
}
