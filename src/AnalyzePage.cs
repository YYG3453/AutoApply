using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 运行分析页：回答三个问题——"投出去的都是多少分的岗位""被拦的和平台失败的分别是什么原因""每站各花了多久"。
    /// 数据来源：评分来自 state-*.jsonl / state-skip.jsonl 的 score 字段；
    /// 每站耗时与平台失败来自 runs.jsonl（Engine 每跑完一站写一行）。
    /// </summary>
    internal class AnalyzePage : Page
    {
        private const int Buckets = 10;

        public AnalyzePage(MainForm app) : base(app) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            var W = Width / T.S;
            var H = Height / T.S;
            ClearHits();

            T.Draw(g, "运行分析", Fonts.H1, T.Text, 32, 20);
            T.Draw(g, "投递质量与运行效率 · 评分为岗位匹配分（0–100），每站耗时来自每轮运行记录", Fonts.Small, T.Text2, 32, 55);

            var rec = App.Records;

            // ---------- 四张小卡 ----------
            float top = 88f, ch = 96f, gap = 16f;
            float cw = (W - 64f - gap * 3f) / 4f;
            int skippedToday = rec == null ? 0 : rec.SkippedToday;
            int platformFails = 0;
            if (rec != null) foreach (var kv in rec.StopByReason) platformFails += kv.Value;
            double lastMins = rec == null ? 0 : rec.LastRunMins;
            string lastWhen = rec == null ? "" : rec.LastRunTime;
            int replied = rec == null ? 0 : rec.RepliedCount;
            int interviews = rec == null ? 0 : rec.InterviewCount;
            int convs = rec == null ? 0 : rec.Replies.Count;

            Kpi(g, new RectangleF(32, top, cw, ch), "今日被拦", skippedToday.ToString(), "规则判掉的岗位", T.Jade);
            Kpi(g, new RectangleF(32 + (cw + gap), top, cw, ch), "平台失败", platformFails.ToString(), "撞限额 / 验证码 / 未登录", T.Warn);
            Kpi(g, new RectangleF(32 + (cw + gap) * 2f, top, cw, ch), "有回复", replied.ToString(), "共 " + convs + " 个会话", T.Blue);
            Kpi(g, new RectangleF(32 + (cw + gap) * 3f, top, cw, ch), "约面试", interviews.ToString(), convs > 0 ? "从 " + convs + " 个会话里筛出" : "还没采集过会话", T.Gold);

            // ---------- 评分分布 + 失败原因 ----------
            float midTop = top + ch + gap;
            float midH = Math.Max(180f, (H - midTop - 22f - gap) * 0.52f);
            float lw = (W - 64f) * 0.58f;
            var scoreCard = new RectangleF(32, midTop, lw, midH);
            var failCard = new RectangleF(scoreCard.Right + gap, midTop, W - 64f - lw - gap, midH);
            T.Card(g, scoreCard, 16f);
            T.Card(g, failCard, 16f);

            ScoreHistogram(g, scoreCard);
            FailReasons(g, failCard);

            // ---------- 每站耗时 ----------
            float botTop = midTop + midH + gap;
            var botCard = new RectangleF(32, botTop, W - 64f, Math.Max(120f, H - botTop - 22f));
            T.Card(g, botCard, 16f);
            SiteTiming(g, botCard);
        }

        // ---------------- 小卡 ----------------
        private void Kpi(Graphics g, RectangleF r, string label, string value, string sub, Color accent)
        {
            T.Card(g, r, 14f);
            T.Draw(g, label, Fonts.Small, T.Text3, r.X + 18, r.Y + 12);
            T.Draw(g, value, value.Length > 5 ? Fonts.H1 : Fonts.Big, T.Text, r.X + 18, r.Y + 36);
            T.Draw(g, sub, Fonts.Tiny, T.Text3, r.X + 18, r.Y + r.Height - 20);
            var dot = new RectangleF(r.Right - 30, r.Y + 16, 10, 10);
            using (var b = new SolidBrush(accent)) g.FillEllipse(b, dot);
        }

        // ---------------- 评分分布 ----------------
        private void ScoreHistogram(Graphics g, RectangleF card)
        {
            T.Draw(g, "评分分布", Fonts.H2, T.Text, card.X + 20, card.Y + 14);
            var rec = App.Records;
            var applied = new int[Buckets];
            var skipped = new int[Buckets];
            int appliedN = 0, skippedN = 0, unknown = 0;

            if (rec != null)
            {
                for (int i = 0; i < rec.All.Count; i++)
                {
                    var s = rec.All[i].Score;
                    if (s < 0) { unknown++; continue; }
                    applied[BucketOf(s)]++; appliedN++;
                }
                for (int i = 0; i < rec.Skips.Count; i++)
                {
                    skipped[BucketOf(rec.Skips[i].Score)]++; skippedN++;
                }
            }

            var plot = new RectangleF(card.X + 20, card.Y + 62, card.Width - 40, card.Height - 62 - 46);
            T.Draw(g, appliedN + " 条投出（绿） · " + skippedN + " 条被拦（灰）" + (unknown > 0 ? " · " + unknown + " 条早期无分数" : ""),
                Fonts.Tiny, T.Text3, card.X + 20 + 76, card.Y + 18);

            int max = 1;
            for (int i = 0; i < Buckets; i++) max = Math.Max(max, Math.Max(applied[i], skipped[i]));

            float bw = plot.Width / Buckets;
            using (var line = new Pen(T.Line, 1f)) g.DrawLine(line, plot.X, plot.Bottom, plot.Right, plot.Bottom);

            for (int i = 0; i < Buckets; i++)
            {
                float x = plot.X + i * bw;
                float total = applied[i] + skipped[i];
                float hAll = total / max * (plot.Height - 8f);
                float hAp = applied[i] / max * (plot.Height - 8f);
                var rectAll = new RectangleF(x + bw * 0.18f, plot.Bottom - hAll, bw * 0.64f, hAll);
                if (hAll > 0.5f)
                {
                    using (var b = new SolidBrush(Color.FromArgb(58, T.Text2))) g.FillRectangle(b, rectAll);
                }
                if (hAp > 0.5f)
                {
                    var rectAp = new RectangleF(x + bw * 0.18f, plot.Bottom - hAp, bw * 0.64f, hAp);
                    using (var b = new LinearGradientBrush(new RectangleF(rectAp.X, rectAp.Y - 1, rectAp.Width, rectAp.Height + 2), T.JadeHi, T.JadeDeep, 90f))
                        g.FillRectangle(b, rectAp);
                }
                if (total > 0)
                    T.TextC(g, ((int)total).ToString(), Fonts.Tiny, T.Text3, new RectangleF(x, plot.Bottom - hAll - 16, bw, 14));
                T.TextC(g, (i * 10).ToString(), Fonts.Tiny, T.Text3, new RectangleF(x, plot.Bottom + 4, bw, 14));
            }
            T.Draw(g, "分", Fonts.Tiny, T.Text3, plot.Right - 12, plot.Bottom + 20);

            // 55 分阈值线（读 rules.json 里的真实阈值）
            int thr = App.Rules != null ? App.Rules.Threshold : 55;
            float tx = plot.X + (thr / 100f) * plot.Width;
            using (var dash = new Pen(Color.FromArgb(150, T.Warn), 1f)) { dash.DashStyle = DashStyle.Dash; g.DrawLine(dash, tx, plot.Y + 6, tx, plot.Bottom); }
            T.Draw(g, "阈值 " + thr, Fonts.Tiny, T.Warn, tx + 4, plot.Y + 40);
        }

        private static int BucketOf(int score)
        {
            if (score < 0) return 0;
            int b = score / 10;
            if (b > Buckets - 1) b = Buckets - 1;
            return b;
        }

        // ---------------- 失败原因 ----------------
        private void FailReasons(Graphics g, RectangleF card)
        {
            T.Draw(g, "失败原因 Top", Fonts.H2, T.Text, card.X + 20, card.Y + 14);
            var rec = App.Records;

            var rows = new List<KeyValuePair<string, int>>();
            var labels = new List<string>();
            if (rec != null)
            {
                var pf = new List<KeyValuePair<string, int>>(rec.StopByReason);
                pf.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
                for (int i = 0; i < Math.Min(3, pf.Count); i++) { rows.Add(pf[i]); labels.Add(NameOfStop(pf[i].Key)); }
                var sk = new List<KeyValuePair<string, int>>(rec.SkipByReason);
                sk.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
                for (int i = 0; i < Math.Min(3, sk.Count); i++) { rows.Add(sk[i]); labels.Add(sk[i].Key); }
            }

            float y = card.Y + 50;
            if (rows.Count == 0)
            {
                T.Draw(g, "还没有失败记录", Fonts.Small, T.Text3, card.X + 20, y);
                return;
            }
            int max = 1;
            for (int i = 0; i < rows.Count; i++) max = Math.Max(max, rows[i].Value);
            float barMax = Math.Max(60f, card.Width - 190f);
            for (int i = 0; i < rows.Count; i++)
            {
                var v = rows[i].Value;
                var rc = new RectangleF(card.X + 20, y, card.Width - 40, 22);
                var lab = labels[i].Length > 9 ? labels[i].Substring(0, 9) + "…" : labels[i];
                T.Draw(g, lab, Fonts.Small, T.Text2, rc.X, rc.Y + 2);
                float labelW = 124f;
                var track = new RectangleF(rc.X + labelW, rc.Y + 6, Math.Max(40f, card.Width - 40f - labelW - 46f), 10);
                T.FillRound(g, track, 5f, T.HoverSoft);
                float w = Math.Max(4f, barMax * v / max);
                var fill = new RectangleF(track.X, track.Y, w, track.Height);
                using (var b = new SolidBrush(i < 3 ? Color.FromArgb(200, T.Warn) : Color.FromArgb(190, T.Jade))) T.FillRound(g, fill, 5f, b);
                T.TextR(g, v.ToString(), Fonts.Small, T.Text3, rc.Right, rc.Y + 2);
                y += 30;
            }
        }

        private static string NameOfStop(string k)
        {
            switch (k)
            {
                case "limit": return "平台限额";
                case "exhausted": return "没有更多岗位";
                case "tabdead": return "标签页挂死";
                case "nosent": return "发不出去";
                case "time": return "时间到";
                case "login": return "未登录";
                case "captcha": return "验证码";
                case "notab": return "没有标签页";
                case "err": return "出错";
                default: return k;
            }
        }

        // ---------------- 每站耗时 ----------------
        private void SiteTiming(Graphics g, RectangleF card)
        {
            T.Draw(g, "每站耗时", Fonts.H2, T.Text, card.X + 20, card.Y + 14);
            var rec = App.Records;
            if (rec == null || rec.LastRun.Count == 0)
            {
                T.Draw(g, "还没有运行记录 —— 跑一轮之后这里会显示每站实际花了多久。", Fonts.Small, T.Text3, card.X + 20, card.Y + 52);
                return;
            }
            T.Draw(g, "最近一轮 " + rec.LastRunTime + " 起 · 合计 " + rec.LastRunMins.ToString("0") + " 分钟",
                Fonts.Tiny, T.Text3, card.X + 20 + 76, card.Y + 18);

            // 按站聚合（同一站可能跑了多段，例如被限额后重跑）
            var order = new List<string>();
            var mins = new Dictionary<string, double>();
            var sent = new Dictionary<string, int>();
            var stop = new Dictionary<string, string>();
            var name = new Dictionary<string, string>();
            for (int i = 0; i < rec.LastRun.Count; i++)
            {
                var r = rec.LastRun[i];
                var k = r.SiteKey;
                if (!mins.ContainsKey(k)) { order.Add(k); mins[k] = 0; sent[k] = 0; stop[k] = ""; name[k] = r.SiteName; }
                mins[k] += r.Mins; sent[k] += r.Sent;
                if (r.Stop.Length > 0) stop[k] = r.Stop;
            }

            double maxM = 1;
            for (int i = 0; i < order.Count; i++) maxM = Math.Max(maxM, mins[order[i]]);

            float y = card.Y + 50;
            bool hasReplies = rec.Replies.Count > 0;
            float rowH = Math.Min(30f, Math.Max(20f, (card.Height - (hasReplies ? 168f : 66f)) / Math.Max(1, order.Count)));
            float barMax = card.Width - 330f;
            for (int i = 0; i < order.Count; i++)
            {
                var k = order[i];
                T.Draw(g, name[k], Fonts.Small, T.Text2, card.X + 20, y + 3);
                var track = new RectangleF(card.X + 150, y + 6, barMax, 12);
                T.FillRound(g, track, 6f, T.HoverSoft);
                float w = Math.Max(6f, (float)(barMax * mins[k] / maxM));
                var fill = new RectangleF(track.X, track.Y, w, track.Height);
                Color c = stop[k] == "limit" || stop[k] == "login" || stop[k] == "captcha" || stop[k] == "notab" ? T.Warn : T.Jade;
                using (var b = new LinearGradientBrush(new RectangleF(fill.X, fill.Y - 1, fill.Width, fill.Height + 2), c, Color.FromArgb(140, c), 0f))
                    T.FillRound(g, fill, 6f, b);
                T.Draw(g, mins[k].ToString("0.#") + " 分", Fonts.Small, T.Text, track.Right + 10, y + 3);
                T.Draw(g, sent[k] + " 条 · " + NameOfStop(stop[k].Length > 0 ? stop[k] : ""), Fonts.Tiny, T.Text3, track.Right + 72, y + 4);
                y += rowH;
            }

            DrawReplyList(g, card, y + 10);
        }

        /// <summary>每站耗时卡的下半部分：最近有动静的会话（约面优先，其次要简历/普通回复）。</summary>
        private void DrawReplyList(Graphics g, RectangleF card, float y)
        {
            var rec = App.Records;
            if (rec == null || rec.Replies.Count == 0) return;
            var picks = new List<ReplyRec>();
            for (int i = 0; i < rec.Replies.Count; i++)
            {
                var r = rec.Replies[i];
                if (r.Status == "interview" || r.Status == "askResume") picks.Add(r);
            for (int a = 0; a < picks.Count; a++)
                for (int b = picks.Count - 1; b > a; b--)
                    if (picks[b].Interview && !picks[b - 1].Interview)
                    { var tmp = picks[b]; picks[b] = picks[b - 1]; picks[b - 1] = tmp; }
            }
            if (picks.Count == 0) return;
            float room = card.Bottom - y - 10f;
            int max = (int)Math.Min(3f, Math.Max(0f, room / 22f));
            if (max <= 0) return;

            T.Draw(g, "最近有动静的会话", Fonts.Small, T.Text2, card.X + 20, y);
            T.Draw(g, "共 " + rec.RepliedCount + " 个有回复 · " + rec.InterviewCount + " 个约面", Fonts.Tiny, T.Text3, card.X + 140, y + 2);
            y += 21;
            for (int i = 0; i < picks.Count && i < max; i++)
            {
                var r = picks[i];
                var dot = new RectangleF(card.X + 22, y + 6, 7, 7);
                using (var b = new SolidBrush(r.Interview ? T.Gold : T.Blue)) g.FillEllipse(b, dot);
                string who = r.Company.Length > 0 ? r.Company : r.Name;
                if (who.Length > 20) who = who.Substring(0, 20) + "…";
                T.Draw(g, r.StatusText, Fonts.Small, r.Interview ? T.Gold : T.Blue, card.X + 36, y);
                T.Draw(g, who, Fonts.Small, T.Text, card.X + 100, y);
                string tail = r.Job.Length > 0 ? r.Job : r.LastText;
                if (tail.Length > 34) tail = tail.Substring(0, 34) + "…";
                T.Draw(g, tail, Fonts.Tiny, T.Text3, card.X + 340, y + 1);
                y += 22;
            }
        }
    }
}
