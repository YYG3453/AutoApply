using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 招呼语实验页。
    ///
    /// 为什么是这个形态：四个平台的招呼语都存在**账号设置**里，由平台在"立即沟通/投递"时
    /// 自动带出去（实测智联的投递弹窗里连输入框都没有，纯展示）。所以程序没法替用户改文字，
    /// 能做的是三件事：① 把三版文本放在这里，点一下复制；② 记住"从哪一刻起用的是哪一版"；
    /// ③ 用这条时间线把每一条投递归到某一版，算出各版回复率。
    /// </summary>
    internal class GreetPage : Page
    {
        private class Stat
        {
            public string Id = "";
            public int Apps;
            public int Replied;
            public double Rate { get { return Apps == 0 ? -1 : (double)Replied / Apps; } }
        }

        private List<Stat> _stats = new List<Stat>();
        private int _unassigned;
        private int _repliedTotal;
        private string _stamp = "";

        public GreetPage(MainForm app) : base(app)
        {
            BackColor = T.Bg;
        }

        public override void RefreshTheme()
        {
            BackColor = T.Bg;
            Invalidate(true);
        }

        // ---------- 统计 ----------

        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) continue;
                b.Append(char.ToLowerInvariant(c));
            }
            return b.ToString().Trim('（', '）', '(', ')', '·', '-', '_');
        }

        private void Recompute()
        {
            var cfg = App.Cfg;
            var list = new List<Stat>();
            if (cfg.Greetings != null)
                for (int i = 0; i < cfg.Greetings.Count; i++)
                    if (cfg.Greetings[i] != null) list.Add(new Stat { Id = cfg.Greetings[i].Id });

            // 回复按公司名归一化后建索引：一条回复算到"该公司最早那条投递"所属的版本上
            var repliedCompanies = new Dictionary<string, bool>();
            var recs = App.Records;
            if (recs != null && recs.Replies != null)
            {
                for (int i = 0; i < recs.Replies.Count; i++)
                {
                    var r = recs.Replies[i];
                    if (r == null || !r.Replied) continue;
                    var k = Norm(r.Company);
                    if (k.Length > 0) repliedCompanies[k] = true;
                }
            }

            _unassigned = 0;
            _repliedTotal = 0;
            var counted = new Dictionary<string, bool>();   // 同一家公司只算一次回复（避免重复投递放大分子）
            if (recs != null && recs.All != null)
            {
                for (int i = 0; i < recs.All.Count; i++)
                {
                    var j = recs.All[i];
                    if (j == null) continue;
                    var id = cfg.GreetAt(j.Ts);
                    Stat st = null;
                    if (id.Length > 0)
                        for (int k = 0; k < list.Count; k++) if (list[k].Id == id) { st = list[k]; break; }
                    if (st == null) { _unassigned++; continue; }
                    st.Apps++;
                    var ck = Norm(j.Company);
                    if (ck.Length > 0 && repliedCompanies.ContainsKey(ck) && !counted.ContainsKey(ck))
                    {
                        counted[ck] = true;
                        st.Replied++;
                        _repliedTotal++;
                    }
                }
            }
            _stats = list;
            _stamp = DateTime.Now.ToString("HH:mm:ss");
        }

        // ---------- 绘制 ----------

        private static List<string> Wrap(Graphics g, string s, Font f, float maxW, int maxLines)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(s)) return lines;
            var cur = "";
            for (int i = 0; i < s.Length; i++)
            {
                var next = cur + s[i];
                if (g.MeasureString(next, f).Width > maxW && cur.Length > 0)
                {
                    lines.Add(cur);
                    cur = s[i].ToString();
                    if (lines.Count >= maxLines - 1) break;
                }
                else cur = next;
            }
            if (lines.Count < maxLines && cur.Length > 0) lines.Add(cur);
            if (lines.Count == maxLines)
            {
                // 被截断时给最后一行加省略号（宽度够才加）
                var last = lines[lines.Count - 1];
                if (g.MeasureString(last + "…", f).Width <= maxW) lines[lines.Count - 1] = last + "…";
            }
            return lines;
        }

        private void Btn(Graphics g, RectangleF r, string text, string id, bool primary)
        {
            float hv = H(id);
            Color bg = primary
                ? (hv > 0.5f ? T.JadeHi : T.Jade)
                : (hv > 0.5f ? T.HoverSoftHi : T.HoverSoft);
            T.FillRound(g, r, 8f, bg);
            if (!primary) T.StrokeRound(g, r, 8f, T.LineSoft);
            var f = Fonts.Small;
            var sz = g.MeasureString(text, f);
            T.Draw(g, text, f, primary ? T.JadeInk : T.Text2, r.X + (r.Width - sz.Width) / 2f, r.Y + (r.Height - sz.Height) / 2f);
            AddHit(id, r);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(T.Bg);
            g.ScaleTransform(T.S, T.S);
            ClearHits();

            float W = Width / T.S, H = Height / T.S;
            T.Draw(g, "招呼语实验", Fonts.H1, T.Text, 32, 24);
            T.Draw(g, "招呼语由平台在投递时自动带出，程序改不了文字；这里管三版文案、切换时间和各版回复率", Fonts.Small, T.Text3, 32, 62);

            if (_stats.Count == 0) Recompute();
            var cfg = App.Cfg;

            // ---- 摘要卡 ----
            var head = new RectangleF(32, 96, W - 64, 78);
            T.Card(g, head, 14f);
            var act = cfg.GreetById(cfg.GreetActive);
            T.Dot(g, head.X + 26, head.Y + 28, 5f, T.Jade);
            T.Draw(g, "当前：" + (act == null ? "未设置" : act.Id + " · " + act.Name), Fonts.H2, T.Text, head.X + 42, head.Y + 16);
            int since = 0;
            string sinceText = "—";
            if (cfg.GreetHistory != null && cfg.GreetHistory.Count > 0)
            {
                var last = cfg.GreetHistory[cfg.GreetHistory.Count - 1];
                sinceText = last.From.ToString("MM-dd HH:mm");
                since = Math.Max(0, (int)(DateTime.Now - last.From).TotalHours);
            }
            T.Draw(g, "从 " + sinceText + " 起生效（已 " + since + " 小时）· 全部投递 " + (App.Records == null ? 0 : App.Records.Total)
                + " 条 · 能归属到某一版 " + CountAssigned() + " 条 · 收到回复 " + _repliedTotal + " 家 · 更新 " + _stamp,
                Fonts.Small, T.Text2, head.X + 42, head.Y + 46);

            // ---- 三版 ----
            float top = head.Bottom + 14f;
            int n = Math.Max(1, _stats.Count);
            float gap = 12f;
            float noteH = 96f;
            float cardH = Math.Min(128f, (H - 30f - noteH - 14f - top - gap * 2) / n);
            if (cardH < 80f) cardH = 80f;

            for (int i = 0; i < _stats.Count; i++)
            {
                var st = _stats[i];
                var vc = cfg.GreetById(st.Id);
                var card = new RectangleF(32, top + i * (cardH + gap), W - 64, cardH);
                bool isActive = vc != null && vc.Id == cfg.GreetActive;
                T.Card(g, card, 14f, false);
                if (isActive) T.StrokeRound(g, card, 14f, T.Jade, 1.5f);

                // 版本徽标
                var badge = new RectangleF(card.X + 18, card.Y + 14, 30, 24);
                T.FillRound(g, badge, 7f, isActive ? T.Jade : T.Panel3);
                T.TextC(g, st.Id, Fonts.H2, isActive ? T.JadeInk : T.Text2, badge);

                T.Draw(g, vc == null ? "（已删除）" : vc.Name, Fonts.H2, T.Text, card.X + 58, card.Y + 12);
                if (isActive)
                {
                    var chip = new RectangleF(card.X + 58 + g.MeasureString(vc == null ? "" : vc.Name, Fonts.H2).Width + 10, card.Y + 15, 46, 19);
                    T.FillRound(g, chip, 9f, T.JadeDim);
                    T.TextC(g, "使用中", Fonts.Tiny, T.Jade, chip);
                }

                // 文案
                var lines = Wrap(g, vc == null ? "" : vc.Text, Fonts.Small, card.Width - 360, cardH > 120 ? 3 : 2);
                float ty = card.Y + 40;
                for (int k = 0; k < lines.Count; k++) { T.Draw(g, lines[k], Fonts.Small, T.Text2, card.X + 18, ty); ty += 17f; }

                // 右侧统计
                float sx = card.Right - 300;
                T.Draw(g, "投递", Fonts.Tiny, T.Text3, sx, card.Y + 16);
                T.Draw(g, st.Apps.ToString(), Fonts.Num, T.Text, sx, card.Y + 32);
                T.Draw(g, "回复", Fonts.Tiny, T.Text3, sx + 78, card.Y + 16);
                T.Draw(g, st.Replied.ToString(), Fonts.Num, st.Replied > 0 ? T.Jade : T.Text3, sx + 78, card.Y + 32);
                T.Draw(g, "回复率", Fonts.Tiny, T.Text3, sx + 148, card.Y + 16);
                T.Draw(g, st.Rate < 0 ? "—" : (st.Rate * 100).ToString("0.0") + "%", Fonts.Num,
                    st.Rate < 0 ? T.Text3 : (st.Rate > 0 ? T.Jade : T.Text2), sx + 148, card.Y + 32);

                // 按钮
                float by = card.Bottom - 42f;
                Btn(g, new RectangleF(card.Right - 118, by, 48, 28), "复制", "copy:" + st.Id, false);
                if (!isActive) Btn(g, new RectangleF(card.Right - 208, by, 84, 28), "设为当前", "use:" + st.Id, true);
                else
                {
                    var ok = new RectangleF(card.Right - 208, by, 84, 28);
                    T.StrokeRound(g, ok, 8f, T.LineSoft);
                    T.TextC(g, "正在使用", Fonts.Small, T.Text3, ok);
                }
            }

            // ---- 底部两张说明卡 ----
            float ny = top + n * cardH + (n - 1) * gap + 14f;
            float nH = Math.Max(84f, H - 30f - ny);
            if (ny + nH > H - 12f) nH = H - 12f - ny;
            if (nH >= 70f)
            {
                float lw = (W - 64f) * 0.56f;
                var cl = new RectangleF(32, ny, lw, nH);
                var cr = new RectangleF(32 + lw + 12f, ny, W - 64f - lw - 12f, nH);
                T.Card(g, cl, 14f);
                T.Draw(g, "怎么用（三步）", Fonts.H2, T.Text, cl.X + 18, cl.Y + 14);
                T.Draw(g, "① 平台里换成 B 版或 C 版 → ② 回来点「设为当前」→ ③ 一周后看这页的回复率", Fonts.Small, T.Text2, cl.X + 18, cl.Y + 42);
                T.Draw(g, "同一句话四个平台通用，换的时候四个都要改；标题党式招呼语不要用，HR 一眼能看出来。", Fonts.Small, T.Text3, cl.X + 18, cl.Y + 66);

                T.Card(g, cr, 14f);
                T.Draw(g, "统计口径", Fonts.H2, T.Text, cr.X + 18, cr.Y + 14);
                T.Draw(g, "回复按公司名对上投递，同一家公司只算一次回复。", Fonts.Small, T.Text2, cr.X + 18, cr.Y + 42);
                T.Draw(g, "早于时间线起点的投递不归属任何版本" + (_unassigned > 0 ? "（现在有 " + _unassigned + " 条）" : "") + "。", Fonts.Small, T.Text3, cr.X + 18, cr.Y + 66);
            }
        }

        private int CountAssigned()
        {
            int n = 0;
            for (int i = 0; i < _stats.Count; i++) n += _stats[i].Apps;
            return n;
        }

        protected override void OnHit(string id)
        {
            if (id == null) return;
            if (id.StartsWith("copy:"))
            {
                var vc = App.Cfg.GreetById(id.Substring(5));
                if (vc == null) return;
                try { Clipboard.SetText(vc.Text); App.Toast("已复制 " + vc.Id + " 版招呼语，去平台粘贴"); }
                catch { App.Toast("复制失败：剪贴板被占用"); }
                return;
            }
            if (id.StartsWith("use:"))
            {
                var vid = id.Substring(4);
                if (App.Cfg.GreetActive == vid) return;
                App.Cfg.SetGreetActive(vid);
                App.SaveCfg();
                Recompute();
                Invalidate();
                App.Toast("已切到 " + vid + " 版：从现在起的投递都算这一版");
            }
        }
    }
}
