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

        private static Color Hue(int hue)
        {
            switch (hue) { case 1: return T.Blue; case 2: return T.Gold; case 3: return T.Warn; default: return T.Jade; }
        }

        /// <summary>数据目录太长了，副标题里只留最后两级，鼠标悬停（关于页）看全路径。</summary>
        private static string ShortPath(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            try
            {
                var d = new DirectoryInfo(dir);
                if (d.Parent != null) return "…\\" + d.Parent.Name + "\\" + d.Name;
                return d.Name;
            }
            catch { return dir; }
        }

        /// <summary>KPI 卡右上角的小色块图标（0 箭头 / 1 列表 / 2 网格 / 3 时钟）。</summary>
        private static void Chip(Graphics g, RectangleF rc, int kind)
        {
            Color c;
            switch (kind)
            {
                case 1: c = T.Blue; break;
                case 2: c = Color.FromArgb(0xA7, 0x8B, 0xFA); break;
                case 3: c = T.Gold; break;
                default: c = T.Jade; break;
            }
            T.FillRound(g, rc, 9f, T.Alpha(c, 34));
            T.StrokeRound(g, rc, 9f, T.Alpha(c, 92));
            float cx = rc.X + rc.Width / 2f, cy = rc.Y + rc.Height / 2f;
            using (var p = new Pen(c, 1.8f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case 1:
                        for (int i = -1; i <= 1; i++) g.DrawLine(p, cx - 7, cy + i * 5.5f, cx + 7, cy + i * 5.5f);
                        break;
                    case 2:
                        for (int i = 0; i < 2; i++)
                            for (int j = 0; j < 2; j++)
                                T.FillRound(g, new RectangleF(cx - 7 + i * 8.5f, cy - 7 + j * 8.5f, 5.5f, 5.5f), 1.6f, c);
                        break;
                    case 3:
                        g.DrawEllipse(p, cx - 7.5f, cy - 7.5f, 15f, 15f);
                        g.DrawLine(p, cx, cy - 4, cx, cy);
                        g.DrawLine(p, cx, cy, cx + 4.5f, cy + 3);
                        break;
                    default:
                        g.DrawLine(p, cx, cy + 7.5f, cx, cy - 7.5f);
                        g.DrawLine(p, cx - 5.5f, cy - 2f, cx, cy - 7.5f);
                        g.DrawLine(p, cx + 5.5f, cy - 2f, cx, cy - 7.5f);
                        break;
                }
            }
        }

        /// <summary>卡片标题 + 右上角副标题（统一的小标题样式，避免每张卡各写一套）。</summary>
        private static void Head(Graphics g, RectangleF card, string title, string right)
        {
            T.Draw(g, title, Fonts.H2, T.Text, card.X + 20, card.Y + 16);
            if (!string.IsNullOrEmpty(right)) T.TextR(g, right, Fonts.Tiny, T.Text3, card.Right - 20, card.Y + 19);
        }

        /// <summary>一条细的横向比例条：底槽 + 填充 + 可选的右端数值。</summary>
        private static void Meter(Graphics g, RectangleF r, float pct, Color c)
        {
            T.FillRound(g, r, r.Height / 2f, T.BtnHover2);
            if (pct <= 0.002f) return;
            if (pct > 1f) pct = 1f;
            var fill = new RectangleF(r.X, r.Y, Math.Max(r.Height, r.Width * pct), r.Height);
            T.FillRound(g, fill, r.Height / 2f, c);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            var W = Width / T.S; var Hh = Height / T.S;
            ClearHits();

            var eng = App.Engine;
            var st = App.Records;
            if (st == null) return;
            bool running = eng != null && eng.Running;
            var today = DateTime.Now.Date;

            // ---------- 标题 ----------
            T.Draw(g, "仪表盘", Fonts.H1, T.Text, 32, 20);
            T.Draw(g, "四平台自动投递 · 城市 " + App.Cfg.Cities.Count + " 个 · 关键词 " + App.Cfg.Keywords.Count + " 组 · 数据 " + ShortPath(Paths.DataDirText),
                   Fonts.Small, T.Text2, 32, 55);

            var pr = new RectangleF(W - 148 - 18 - 126, 24, 126, 30);
            T.FillRound(g, pr, 15f, T.Panel3);
            T.StrokeRound(g, pr, 15f, running ? T.Alpha(T.Jade, 90) : T.Line);
            T.Dot(g, pr.X + 18, pr.Y + 15, 4.5f, running ? T.Jade : T.Text3);
            T.Draw(g, running ? "运行中" : "待命中", Fonts.Small, running ? T.Jade : T.Text2, pr.X + 30, pr.Y + 7);

            // ---------- 统计口径 ----------
            int todayReplied = 0, todayInterview = 0;
            for (int i = 0; i < st.Replies.Count; i++)
            {
                var rp = st.Replies[i];
                if (!rp.HasTs || rp.Ts.Date != today) continue;
                if (rp.Replied) todayReplied++;
                if (rp.Interview) todayInterview++;
            }
            double rate = st.Total > 0 ? st.RepliedCount * 100.0 / st.Total : 0;

            // ---------- KPI 行（6 张）----------
            float gap = 14, y = 92, kpiH = 88;
            float cw = (W - 64 - gap * 5) / 6f;
            string[] labels = { "今日投递", "累计投递", "今日回复", "总回复率", "今日被过滤", "下次运行" };
            string[] values = {
                st.Today.ToString(),
                st.Total.ToString(),
                todayReplied.ToString(),
                st.Total > 0 ? rate.ToString("0.0") + "%" : "—",
                st.SkippedToday.ToString(),
                App.NextRunText()
            };
            string[] subs = {
                "今天已发出",
                "历史总计",
                "今天有回音的",
                "累计 " + st.RepliedCount + " 回复",
                "规则挡下的",
                App.NextRunSub()
            };
            Color[] vcols = { T.Jade, T.Text, T.Blue, T.Gold, T.Warn, T.Text };
            for (int i = 0; i < 6; i++)
            {
                var r = new RectangleF(32 + i * (cw + gap), y, cw, kpiH);
                T.Card(g, r, 14f);
                T.Draw(g, labels[i], Fonts.Small, T.Text2, r.X + 16, r.Y + 14);
                T.Draw(g, values[i], Fonts.Big, vcols[i], r.X + 15, r.Y + 34);
                T.Draw(g, subs[i], Fonts.Tiny, T.Text3, r.X + 16, r.Y + 68);
                Chip(g, new RectangleF(r.Right - 44, r.Y + 12, 28, 28), i == 5 ? 3 : (i == 2 || i == 3 ? 2 : (i == 4 ? 1 : i)));
            }

            // ---------- 行高分配 ----------
            float top = y + kpiH + gap;
            float rest = Hh - top - 18f;
            float rightW = 296;
            float leftW = W - 64 - rightW - gap;
            float r1 = Math.Max(120f, rest * 0.34f);
            float r2 = Math.Max(110f, rest * 0.28f);
            float r3 = Math.Max(110f, rest - r1 - r2 - gap * 2f);
            float rightH = r1 + r2 + r3 + gap * 2f;
            float cityH = Math.Max(128f, Math.Min(186f, rightH * 0.24f));
            float funH = Math.Max(146f, Math.Min(226f, rightH * 0.30f));
            float runH = Math.Max(196f, rightH - funH - cityH - gap * 2f);

            // ---------- 近 14 天：投递 vs 回复 ----------
            var trendCard = new RectangleF(32, top, leftW, r1);
            T.Card(g, trendCard, 16f);
            Head(g, trendCard, "近 14 天 · 投递 / 回复", "");
            // 图例
            float lgx = trendCard.Right - 300;
            string kRepLegend = "回复";
            T.Dot(g, lgx, trendCard.Y + 24, 3.4f, T.Jade);
            T.Draw(g, "投递", Fonts.Tiny, T.Text3, lgx + 8, trendCard.Y + 17);
            T.Dot(g, lgx + 48, trendCard.Y + 24, 3.4f, T.Blue);
            T.Draw(g, kRepLegend, Fonts.Tiny, T.Text3, lgx + 56, trendCard.Y + 17);

            var dayLabels = new List<string>();
            var sentVals = new List<float>();
            var repVals = new List<float>();
            var dayKeys = new List<string>();
            var repByDay = new Dictionary<string, int>();
            for (int i = 0; i < st.Replies.Count; i++)
            {
                var rp = st.Replies[i];
                if (!rp.HasTs || !rp.Replied) continue;
                var k = rp.Ts.ToString("yyyy-MM-dd");
                int v; repByDay.TryGetValue(k, out v);
                repByDay[k] = v + 1;
            }
            for (int i = 13; i >= 0; i--)
            {
                var d = today.AddDays(-i);
                string key = d.ToString("yyyy-MM-dd");
                dayKeys.Add(key);
                dayLabels.Add(d.ToString("MM-dd"));
                int v; st.ByDay.TryGetValue(key, out v);
                sentVals.Add(v);
                int rv; repByDay.TryGetValue(key, out rv);
                repVals.Add(rv);
            }
            bool compact = r1 < 190f;
            var tplot = compact
                ? new RectangleF(trendCard.X + 16, trendCard.Y + 40, trendCard.Width - 32, Math.Max(20f, trendCard.Height - 46f))
                : new RectangleF(trendCard.X + 42, trendCard.Y + 48, trendCard.Width - 62, trendCard.Height - 48 - 26);
            _trendPlot = tplot;
            float maxSentV = 1f, maxRepV = 0f;
            for (int i = 0; i < sentVals.Count; i++) maxSentV = Math.Max(maxSentV, sentVals[i]);
            for (int i = 0; i < repVals.Count; i++) maxRepV = Math.Max(maxRepV, repVals[i]);
            float kRep = 1f;
            if (maxRepV > 0 && maxRepV < maxSentV * 0.55f) kRep = (float)Math.Ceiling(maxSentV * 0.55f / maxRepV);
            var repScaled = new float[repVals.Count];
            for (int i = 0; i < repVals.Count; i++) repScaled[i] = repVals[i] * kRep;
            float[][] series = { sentVals.ToArray(), repScaled };
            Color[] scols = { T.Jade, T.Blue };
            string[] snames = { "投递", "回复" };
            float[] sscale = { 1f, kRep };
            _trendPts = Chart.MultiLine(g, tplot, series, scols, snames,
                                        compact ? null : dayLabels.ToArray(), _hoverDay, !compact, sscale);
            // 汇总一行
            int sumSent = 0, sumRep = 0;
            for (int i = 0; i < sentVals.Count; i++) sumSent += (int)sentVals[i];
            for (int i = 0; i < repVals.Count; i++) sumRep += (int)repVals[i];
            if (!compact)
                T.Draw(g, "14 天合计 " + sumSent + " 条投递 · " + sumRep + " 条回复"
                         + (sumSent > 0 ? " · 回复率 " + (sumRep * 100.0 / sumSent).ToString("0.0") + "%" : ""),
                       Fonts.Tiny, T.Text3, trendCard.X + 20, trendCard.Bottom - 20);

            // ---------- 平台对比 ----------
            var platCard = new RectangleF(32, top + r1 + gap, leftW, r2);
            T.Card(g, platCard, 16f);
            Head(g, platCard, "平台对比 · 累计", "横条 = 投递量，亮段 = 有回复");
            var sites = App.Cfg.Sites;
            float rowH = Math.Min(30f, (platCard.Height - 52f) / Math.Max(1, sites.Count));
            float nameW = 92f;
            float barX = platCard.X + 20 + nameW;
            float barW = platCard.Width - 40 - nameW - 150f;
            int maxTotal = 1;
            for (int i = 0; i < sites.Count; i++) maxTotal = Math.Max(maxTotal, st.CountTotal(sites[i].Key));
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                float ry = platCard.Y + 44 + i * rowH;
                var sz = new RectangleF(platCard.X + 20, ry, nameW - 8, rowH - 6);
                T.Dot(g, sz.X + 4, sz.Y + sz.Height / 2f, 3.6f, s.Enabled ? Hue(s.Hue) : T.Text3);
                T.Draw(g, s.Name, Fonts.Tiny, s.Enabled ? T.Text2 : T.Text3, sz.X + 13, sz.Y + 2);

                int tot = st.CountTotal(s.Key);
                int rep = st.CountReplied(s.Key);
                var bar = new RectangleF(barX, ry + 6, barW, Math.Max(8f, rowH - 16f));
                T.FillRound(g, bar, bar.Height / 2f, T.BtnHover2);
                float wTot = barW * (tot / (float)maxTotal);
                if (tot > 0)
                {
                    var fill = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, wTot), bar.Height);
                    using (var lg = new LinearGradientBrush(new RectangleF(fill.X, fill.Y, Math.Max(2f, fill.Width), Math.Max(2f, fill.Height)),
                                                           T.Alpha(Hue(s.Hue), 190), T.Alpha(Hue(s.Hue), 105), 0f))
                        T.FillRound(g, fill, bar.Height / 2f, lg);
                }
                if (rep > 0)
                {
                    float wRep = Math.Max(6f, wTot * Math.Min(1f, rep / (float)Math.Max(1, tot)) * 3.2f);
                    wRep = Math.Min(wTot, Math.Max(6f, wRep));
                    var fill2 = new RectangleF(bar.X, bar.Y, wRep, bar.Height);
                    T.FillRound(g, fill2, bar.Height / 2f, T.Mix(Hue(s.Hue), Color.White, 0.45f));
                }
                T.TextR(g, tot.ToString(), Fonts.Tiny, T.Text2, platCard.Right - 96, ry + 3);
                T.TextR(g, rep > 0 ? "回复 " + rep : "—", Fonts.Tiny, rep > 0 ? T.Jade : T.Text3, platCard.Right - 20, ry + 3);
            }

            // ---------- 评分分布 + 今日时段 ----------
            float halfW = (leftW - gap) / 2f;
            T.TextR(g, kRep > 1.001f ? "回复按 " + kRep + "× 放大显示 · 悬停看当天" : "悬停看当天", Fonts.Tiny, T.Text3, trendCard.Right - 20, trendCard.Y + 17);

            var histCard = new RectangleF(32, top + r1 + r2 + gap * 2f, halfW, r3);
            T.Card(g, histCard, 16f);
            Head(g, histCard, "匹配分分布 · 全部投递", "低于 60 分不会被投");
            string[] buckets = { "<60", "60-69", "70-79", "80-89", "90+" };
            int[] bcount = new int[5];
            for (int i = 0; i < st.All.Count; i++)
            {
                int sc = st.All[i].Score;
                if (sc < 0) continue;
                if (sc < 60) bcount[0]++;
                else if (sc < 70) bcount[1]++;
                else if (sc < 80) bcount[2]++;
                else if (sc < 90) bcount[3]++;
                else bcount[4]++;
            }
            var histPlot = new RectangleF(histCard.X + 38, histCard.Y + 46, histCard.Width - 58, Math.Max(30f, histCard.Height - 46 - 30));
            _histPlot = histPlot;
            _histBars = Chart.VBars(g, histPlot, new float[] { bcount[0], bcount[1], bcount[2], bcount[3], bcount[4] },
                                     buckets, T.Gold, buckets, _hoverHist, true);

            var hourCard = new RectangleF(32 + halfW + gap, histCard.Y, halfW, r3);
            T.Card(g, hourCard, 16f);
            Head(g, hourCard, "今日投递时段", "按发出时间统计");
            int hFrom = 8, hTo = 20;
            var hourVals = new float[hTo - hFrom + 1];
            for (int i = 0; i < st.All.Count; i++)
            {
                var jr = st.All[i];
                if (!jr.HasTs || jr.Ts.Date != today) continue;
                int h = jr.Ts.Hour;
                if (h < hFrom || h > hTo) continue;
                hourVals[h - hFrom]++;
            }
            var hourLabels = new string[hTo - hFrom + 1];
            var hourTips = new string[hTo - hFrom + 1];
            for (int i = 0; i < hourLabels.Length; i++) { hourLabels[i] = (hFrom + i) + ""; hourTips[i] = (hFrom + i) + ":00"; }
            var hourPlot = new RectangleF(hourCard.X + 38, hourCard.Y + 46, hourCard.Width - 58, Math.Max(30f, hourCard.Height - 46 - 30));
            _hourPlot = hourPlot;
            _hourBars = Chart.VBars(g, hourPlot, hourVals, hourLabels, T.Blue, hourTips, _hoverHour, true);

            // ---------- 本次运行 ----------
            var run = new RectangleF(W - 32 - rightW, top, rightW, runH);
            T.Card(g, run, 16f);
            T.Draw(g, "本次运行", Fonts.H2, T.Text, run.X + 20, run.Y + 16);
            var statusText = running ? "进行中 · " + (eng.CurrentSiteName ?? "") : (eng != null && eng.Results.Count > 0 ? eng.RunWindowName : "尚未运行");
            T.TextR(g, statusText, Fonts.Tiny, running ? T.Jade : T.Text3, run.Right - 20, run.Y + 18);

            float rowsTop = run.Y + 50f;
            float rowsBottom = run.Bottom - 60f;
            float rowGap = Math.Max(26f, Math.Min(44f, (rowsBottom - rowsTop) / Math.Max(1, sites.Count)));
            float ly = rowsTop;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                var row = new RectangleF(run.X + 14, ly + i * rowGap, run.Width - 28, rowGap - 8);
                T.FillRound(g, row, 10f, T.Panel2);
                T.Dot(g, row.X + 15, row.Y + 17, 4f, s.Enabled ? Hue(s.Hue) : T.Text3);
                T.Draw(g, s.Name, Fonts.Body, s.Enabled ? T.Text : T.Text3, row.X + 28, row.Y + 8);

                var res = App.ResultOf(s.Key);
                string right = ""; Color rc = T.Text3;
                if (res != null)
                {
                    if (res.Stop == "notab") { right = "无标签页"; rc = T.Warn; }
                    else if (res.Sent > 0) { right = "已投 " + res.Sent; rc = T.Jade; }
                    else if (res.Stop == "limit") { right = "撞上限"; rc = T.Warn; }
                    else if (res.Stop == "captcha") { right = "需验证"; rc = T.Warn; }
                    else if (res.Stop == "login") { right = "未登录"; rc = T.Warn; }
                    else right = "0";
                }
                else if (running && eng.CurrentSite == s.Key) { right = "运行中…"; rc = T.Jade; }
                else if (!s.Enabled) { right = "已停用"; }
                else { int tc2 = st.CountToday(s.Key); if (tc2 > 0) { right = "今天 " + tc2; rc = T.Text2; } }
                if (right.Length > 0) T.TextR(g, right, Fonts.Tiny, rc, row.Right - 12, row.Y + 9);
            }

            var b1 = new RectangleF(run.X + 14, run.Bottom - 48, 128, 34);
            var b2 = new RectangleF(run.X + 150, run.Bottom - 48, 88, 34);
            DrawPrimary(g, b1, "开始投递", "start", !running);
            DrawGhost(g, b2, "停止", "stop", running);

            // ---------- 漏斗 ----------
            var fun = new RectangleF(W - 32 - rightW, top + runH + gap, rightW, funH);
            T.Card(g, fun, 16f);
            Head(g, fun, "投递漏斗 · 累计", "回复之后靠人跟进");
            string[] fname = { "投递", "有回复", "要简历", "约面试" };
            int[] fval = { st.Total, st.RepliedCount, st.AskResumeCount, st.InterviewCount };
            Color[] fcol = { T.Jade, T.Blue, Color.FromArgb(0xA7, 0x8B, 0xFA), T.Gold };
            float fy = fun.Y + 44;
            float fh = Math.Min(26f, Math.Max(16f, (fun.Height - 54f - 6 * (fname.Length - 1)) / fname.Length));
            int fmax = Math.Max(1, fval[0]);
            for (int i = 0; i < fname.Length; i++)
            {
                float bw = (fun.Width - 40) * Math.Max(0.06f, fval[i] / (float)fmax);
                var bar = new RectangleF(fun.X + 20, fy, bw, fh);
                using (var lg = new LinearGradientBrush(new RectangleF(bar.X, bar.Y, Math.Max(2f, bar.Width), Math.Max(2f, bar.Height)),
                                                       T.Alpha(fcol[i], 215), T.Alpha(fcol[i], 120), 0f))
                    T.FillRound(g, bar, 7f, lg);
                T.Draw(g, fname[i], Fonts.Tiny, T.Text2, bar.X + 10, bar.Y + (fh - 14) / 2f);
                string pv = fval[i].ToString();
                if (i > 0 && fval[0] > 0) pv += "  ·  " + (fval[i] * 100.0 / fval[0]).ToString("0.0") + "%";
                T.TextR(g, pv, Fonts.Tiny, fval[i] > 0 ? T.Text : T.Text3, fun.Right - 20, bar.Y + (fh - 14) / 2f);
                fy += fh + 6;
            }

            // ---------- 城市环形（右下角）----------
            var cityCard = new RectangleF(W - 32 - rightW, fun.Bottom + gap, rightW, Math.Max(110f, Hh - fun.Bottom - gap - 18f));
            {
                T.Card(g, cityCard, 16f);
                Head(g, cityCard, "今日城市分布", "");
                var cityTop = new List<KeyValuePair<string, int>>(st.TodayByCity);
                cityTop.Sort((a, b) => b.Value.CompareTo(a.Value));
                if (cityTop.Count == 0)
                {
                    _donutVals = new float[0];
                    T.Draw(g, "今天还没有记录", Fonts.Small, T.Text3, cityCard.X + 20, cityCard.Y + 56);
                }
                else
                {
                    var names = new List<string>();
                    var cvals = new List<float>();
                    var cols = new List<Color>();
                    Color[] palette = { T.Jade, T.Blue, Color.FromArgb(0xA7, 0x8B, 0xFA), T.Gold, T.Warn };
                    int topN = Math.Min(3, cityTop.Count);
                    for (int i = 0; i < topN; i++) { names.Add(cityTop[i].Key); cvals.Add(cityTop[i].Value); cols.Add(palette[i % palette.Length]); }
                    int restv = 0;
                    for (int i = topN; i < cityTop.Count; i++) restv += cityTop[i].Value;
                    if (restv > 0) { names.Add("其他"); cvals.Add(restv); cols.Add(T.Text3); }

                    float rNeed = (cityCard.Height - 44f) / 3.4f;
                    _donutR = Math.Min(Math.Min(cityCard.Width * 0.20f, 52f), Math.Max(24f, rNeed));
                    _donutC = new PointF(cityCard.X + 20 + _donutR + 8, cityCard.Y + 46 + _donutR);
                    _donutVals = cvals.ToArray();
                    Chart.Donut(g, _donutC, _donutR, 14f, _donutVals, cols.ToArray(), _hoverCity);
                    T.TextC(g, st.Today.ToString(), Fonts.Body, T.Text,
                            new RectangleF(_donutC.X - 60, _donutC.Y - 10, 120, 20));

                    float lx = _donutC.X + _donutR + 14f;
                    float lw = cityCard.Right - 20 - lx;
                    for (int i = 0; i < names.Count; i++)
                    {
                        float yy = cityCard.Y + 44 + i * 21f;
                        if (yy > cityCard.Bottom - 18) break;
                        T.Dot(g, lx + 5, yy + 8, 4f, cols[i]);
                        T.Draw(g, names[i], Fonts.Tiny, i == _hoverCity ? T.Text : T.Text2, lx + 14, yy);
                        T.TextR(g, cvals[i].ToString(), Fonts.Tiny, T.Text3, cityCard.Right - 20, yy);
                    }
                }
            }
            if (cityCard.Height < 100f) _donutVals = new float[0];
        }

        private PointF[][] _trendPts = new PointF[0][];
        private RectangleF _trendPlot;
        private int _hoverDay = -1;
        private PointF _donutC;
        private float _donutR;
        private float[] _donutVals = new float[0];
        private int _hoverCity = -1;
        private RectangleF[] _histBars = new RectangleF[0];
        private RectangleF _histPlot;
        private int _hoverHist = -1;
        private RectangleF[] _hourBars = new RectangleF[0];
        private RectangleF _hourPlot;
        private int _hoverHour = -1;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = T.LP(e.Location);
            int hd = _trendPts.Length > 0 ? Chart.HitMulti(_trendPts, p.X, 26f) : -1;
            if (_trendPts.Length > 0 && p.Y < _trendPlot.Y - 10) hd = -1;
            int hc = _donutVals.Length > 0 ? Chart.HitDonut(_donutC, _donutR + 4f, 20f, _donutVals, p) : -1;
            int hh = _histBars.Length > 0 ? Chart.HitBars(_histBars, _histPlot, p) : -1;
            int ho = _hourBars.Length > 0 ? Chart.HitBars(_hourBars, _hourPlot, p) : -1;
            if (hd != _hoverDay || hc != _hoverCity || hh != _hoverHist || ho != _hoverHour)
            {
                _hoverDay = hd; _hoverCity = hc; _hoverHist = hh; _hoverHour = ho;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverDay != -1 || _hoverCity != -1 || _hoverHist != -1 || _hoverHour != -1)
            {
                _hoverDay = -1; _hoverCity = -1; _hoverHist = -1; _hoverHour = -1;
                Invalidate();
            }
        }

        private void DrawPrimary(Graphics g, RectangleF r, string text, string id, bool enabled)
        {
            float hv = H(id);
            var col1 = enabled ? T.Mix(T.JadeHi, T.JadeLight, hv) : T.Line2;
            var col2 = enabled ? T.Mix(T.JadeMid, T.Jade2, hv) : T.HoverSoft2;
            using (var lg = new LinearGradientBrush(new RectangleF(r.X, r.Y, Math.Max(2, r.Width), Math.Max(2, r.Height)), col1, col2, 35f))
                T.FillRound(g, r, 11f, lg);
            T.TextC(g, text, T.F(13f, FontStyle.Bold), enabled ? T.JadeInk2 : T.Text3, r);
            AddHit(id, r);
        }

        private void DrawGhost(Graphics g, RectangleF r, string text, string id, bool active)
        {
            float hv = H(id);
            T.FillRound(g, r, 11f, T.Mix(T.Panel4, T.BtnHover, hv));
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
            var track = on ? T.Mix(T.JadeDim, T.Jade, hv * 0.35f) : T.Mix(T.Line3, T.LineHi, hv);
            T.FillRound(g, r, r.Height / 2f, track);
            float knobR = r.Height - 8;
            float kx = on ? r.Right - knobR - 4 : r.X + 4;
            using (var b = new SolidBrush(on ? T.JadeInk : T.TextSoft))
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
                T.FillRound(g, row, 12f, T.Panel2);
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
            T.FillRound(g, r, 8f, T.Mix(T.HoverSoft, T.HoverSoftHi, hv));
            T.StrokeRound(g, r, 8f, T.Mix(T.Line, T.Jade, hv * 0.5f));
            T.TextC(g, text, T.F(11.5f), T.Mix(T.Text2, T.Text, hv), r);
            AddHit(id, r);
        }

        private void DrawStepper(Graphics g, float x, float y, string id, int value, string unit, int step, int max)
        {
            var box = new RectangleF(x, y, 150, 34);
            T.FillRound(g, box, 9f, T.Field);
            T.StrokeRound(g, box, 9f, T.Line);
            var minus = new RectangleF(box.X + 3, box.Y + 3, 30, box.Height - 6);
            var plus = new RectangleF(box.Right - 33, box.Y + 3, 30, box.Height - 6);
            float hm = H(id + "-"), hp = H(id + "+");
            T.FillRound(g, minus, 7f, T.Alpha(T.Track, (int)(60 + hm * 140)));
            T.FillRound(g, plus, 7f, T.Alpha(T.Track, (int)(60 + hp * 140)));
            T.TextC(g, "−", T.F(14f), T.Mix(T.Text2, T.Danger, hm), minus);
            T.TextC(g, "+", T.F(14f), T.Mix(T.Text2, T.Jade, hp), plus);
            var val = value.ToString() + " " + unit;
            T.TextC(g, val, Fonts.Small, T.Text, new RectangleF(box.X + 30, box.Y, box.Width - 66, box.Height));
            AddHit(id + "-", minus);
            AddHit(id + "+", plus);
        }

        protected override void OnHit(string id)
        {
            if (!App.CanEdit) { App.Toast("投递进行中，先点「停止」再改计划"); return; }
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
            _kw.BackColor = T.RailTop;
            _kw.Text = string.Join("，", app.Cfg.Keywords.ToArray());
            _kw.TextChanged += (s, e) =>
            {
                if (!app.CanEdit) return;
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
            _chrome.BackColor = T.RailTop;
            _chrome.Text = app.Cfg.ChromePath;
            _chrome.TextChanged += (s, e) => { if (app.CanEdit) app.Cfg.ChromePath = _chrome.Text.Trim(); };
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
            T.FillRound(g, outer, 10f, T.RailTop);
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

            // 投递进行中把两个输入框锁成只读，避免改到一半
            bool edit = App.CanEdit;
            if (_kw.ReadOnly != !edit) _kw.ReadOnly = !edit;
            if (_chrome.ReadOnly != !edit) _chrome.ReadOnly = !edit;

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
                T.FillRound(g, r, 14f, on ? T.Alpha(T.JadeDim, 70 + (int)(hv * 40)) : T.Field);
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
            T.FillRound(g, r, 8f, T.Mix(T.HoverSoft, T.HoverSoftHi, hv));
            T.StrokeRound(g, r, 8f, T.Mix(T.Line, T.Jade, hv * 0.5f));
            T.TextC(g, text, T.F(11.5f), T.Mix(T.Text2, T.Text, hv), r);
            AddHit(id, r);
        }

        protected override void OnHit(string id)
        {
            var cfg = App.Cfg;
            if (id.StartsWith("city:") || id == "all" || id == "none")
            {
                if (!App.CanEdit) { App.Toast("投递进行中，先点「停止」再改城市"); return; }
            }
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
            BackColor = T.Field;
            ForeColor = T.Text;
            Font = T.F(13f);
        }

        public void RefreshTheme()
        {
            BackColor = T.Field;
            ForeColor = T.Text;
            Invalidate();
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
            T.FillRound(g, r, 8f, T.Mix(T.HoverSoft, T.HoverSoftHi, hv));
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
            DrawSmallGhost(g, new RectangleF(W - 428, 26, 74, 30), "清空", "clear");
            DrawSmallGhost(g, new RectangleF(W - 346, 26, 84, 30), "复制全部", "copy");
            DrawSmallGhost(g, new RectangleF(W - 254, 26, 94, 30), "打开报告", "report");
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
            T.Mark(g, new RectangleF(cx, cy, 46, 46));

            T.Draw(g, "AUTO APPLY", T.Num(20f, FontStyle.Bold), T.Text, cx + 62, cy - 2);
            T.Draw(g, "四平台自动投递 · 桌面版 v" + MainForm.Version, Fonts.Small, T.Text2, cx + 62, cy + 26);

            string[] lines =
            {
                "把 BOSS直聘 / 智联招聘 / 应届生求职 / 前程无忧 的投递动作串成一条自动化流水线。",
                "点「开始投递」或开着定时，程序会接管浏览器逐站投简历，并把每一条记录写进本地记录文件。",
                "",
                "· 单文件 exe，自带 node 运行时，解压即用，不用装任何环境",
                "· 登录态保存在 browser-profile 目录，换机器重新登录一次即可",
                "· 报告在 " + Paths.DataDirText + "\\report.md，可用「运行日志 → 打开报告」直接看",
            };
            float y = card.Y + 132;
            foreach (var l in lines) { T.Draw(g, l, Fonts.Body, l.StartsWith("·") ? T.Text2 : T.Text2, card.X + 36, y); y += 30; }

            var c2 = new RectangleF(32, 408, W - 64, 120);
            T.Card(g, c2, 16f);
            T.Draw(g, "快捷键与提示", Fonts.H2, T.Text, c2.X + 20, c2.Y + 14);
            T.Draw(g, "F5 立即开始一轮    Esc 停止    最小化后可继续在托盘运行", Fonts.Small, T.Text2, c2.X + 20, c2.Y + 46);
            T.Draw(g, "若某个平台提示需要验证（滑块 / 扫码），点「打开浏览器」手动处理一次即可。", Fonts.Small, T.Text2, c2.X + 20, c2.Y + 74);

            DrawSmallGhost(g, new RectangleF(c2.Right - 150, c2.Y + 60, 132, 32), "打开程序目录", "dir");

            var c3 = new RectangleF(32, 540, W - 64, 128);
            T.Card(g, c3, 16f);
            T.Draw(g, "版本与更新", Fonts.H2, T.Text, c3.X + 20, c3.Y + 14);
            string ver = "当前版本 v" + MainForm.Version;
            if (Updater.Latest.Length > 0)
                ver += Updater.IsNewer(Updater.Latest, MainForm.Version) ? ("    ·    最新 v" + Updater.Latest + "（有新版本）") : "    ·    已是最新";
            else if (Updater.Checked) ver += "    ·    检查失败（网络或仓库未发布 Release）";
            else ver += "    ·    尚未检查";
            T.Draw(g, ver, Fonts.Small, Updater.Latest.Length > 0 && Updater.IsNewer(Updater.Latest, MainForm.Version) ? T.Warn : T.Text2, c3.X + 20, c3.Y + 46);
            T.Draw(g, "更新方式：下载新的 zip 解压覆盖 exe；browser-profile 与 scripts 里的记录不会被覆盖。", Fonts.Tiny, T.Text3, c3.X + 20, c3.Y + 76);
            DrawSmallGhost(g, new RectangleF(c3.Right - 150, c3.Y + 62, 132, 32), "检查更新", "chk");
            if (Updater.Latest.Length > 0 && Updater.IsNewer(Updater.Latest, MainForm.Version))
                DrawSmallGhost(g, new RectangleF(c3.Right - 300, c3.Y + 62, 132, 32), "打开下载页", "dl");
        }

        private void DrawSmallGhost(Graphics g, RectangleF r, string text, string id)
        {
            float hv = H(id);
            T.FillRound(g, r, 8f, T.Mix(T.HoverSoft, T.HoverSoftHi, hv));
            T.StrokeRound(g, r, 8f, T.Mix(T.Line, T.Jade, hv * 0.5f));
            T.TextC(g, text, T.F(11.5f), T.Mix(T.Text2, T.Text, hv), r);
            AddHit(id, r);
        }

        protected override void OnHit(string id)
        {
            if (id == "dir")
            {
                Process.Start("explorer.exe", App.BaseDir);
                return;
            }
            if (id == "chk")
            {
                App.Toast("正在检查更新…");
                Updater.CheckAsync(delegate (string v)
                {
                    try
                    {
                        App.BeginInvoke(new Action(delegate
                        {
                            if (v.Length == 0) App.Toast("检查失败（网络不通，或仓库还没发布 Release）");
                            else if (Updater.IsNewer(v, MainForm.Version)) App.Toast("发现新版本 v" + v + "，点「打开下载页」");
                            else App.Toast("已是最新版本 v" + MainForm.Version);
                            Invalidate();
                        }));
                    }
                    catch { }
                });
                return;
            }
            if (id == "dl") { try { Process.Start(Updater.Page); } catch { App.Toast("打不开浏览器，手动访问 " + Updater.Page); } }
        }
    }
}
