using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 匹配规则页：阈值、标题必含/排除、公司黑名单、技能词，全部可改、改完保存即生效。
    /// 真正跑起来时由 scripts\score.js 读同一份 rules.json 打分。
    /// </summary>
    internal class RulesPage : Page
    {
        private RulesCfg _r;
        private readonly DarkText _add;
        private readonly DarkText _pre;

        public RulesPage(MainForm app) : base(app)
        {
            _r = RulesCfg.Load(app.Engine.ScriptDir);
            _add = new DarkText();
            _pre = new DarkText();
            Controls.Add(_add);
            Controls.Add(_pre);
            _pre.Text = "实施工程师";
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        private void Relayout()
        {
            var w = Width / T.S;
            _add.Bounds = new Rectangle(T.RS(32 + 68), T.RS(92 + 68), T.RS(280), T.RS(26));
            _pre.Bounds = new Rectangle(T.RS(32 + 68), T.RS(92 + 110), T.RS(280), T.RS(26));
        }

        private void DrawGhost(Graphics g, RectangleF r, string text, string id, Color? tint = null)
        {
            float hv = H(id);
            var baseCol = tint.HasValue ? tint.Value : T.HoverSoft;
            T.FillRound(g, r, 8f, T.Mix(baseCol, T.HoverSoftHi, hv));
            T.StrokeRound(g, r, 8f, T.Mix(T.Line, T.Jade, hv * 0.6f));
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

            T.Draw(g, "匹配规则", Fonts.H1, T.Text, 32, 20);
            T.Draw(g, "投递前先打分；低于分数线就跳过，被跳过的会记进「被过滤」", Fonts.Small, T.Text2, 32, 55);

            var saveR = new RectangleF(W - 300, 26, 132, 32);
            float shv = H("save");
            T.FillRound(g, saveR, 10f, T.Mix(T.Jade, T.JadeHi, shv));
            T.TextC(g, "保存并生效", T.F(12.5f, System.Drawing.FontStyle.Bold), T.JadeInk, saveR);
            AddHit("save", saveR);

            // ---------- 卡片 1：分数线 / 标题必含开关 / 新词 / 试算 ----------
            var c1 = new RectangleF(32, 92, W - 64, 190);
            T.Card(g, c1, 16f);

            T.Draw(g, "分数线", Fonts.Body, T.Text, c1.X + 20, c1.Y + 18);
            DrawGhost(g, new RectangleF(c1.X + 96, c1.Y + 12, 34, 30), "−", "th:-");
            T.TextC(g, _r.Threshold.ToString(), T.F(15f, FontStyle.Bold), T.Text, new RectangleF(c1.X + 134, c1.Y + 12, 56, 30));
            DrawGhost(g, new RectangleF(c1.X + 194, c1.Y + 12, 34, 30), "＋", "th:+");
            T.Draw(g, "建议 50–65", Fonts.Tiny, T.Text3, c1.X + 240, c1.Y + 22);

            // 标题必须命中开关
            var tg = new RectangleF(c1.X + c1.Width - 268, c1.Y + 14, 244, 28);
            bool must = _r.TitleMust;
            T.FillRound(g, tg, 14f, must ? T.JadeDim : T.HoverSoft);
            T.StrokeRound(g, tg, 14f, must ? T.Jade : T.Line);
            float knobX = must ? tg.Right - 26 : tg.X + 4;
            T.FillRound(g, new RectangleF(knobX, tg.Y + 4, 22, 20), 10f, must ? T.Jade : T.Text3);
            T.Draw(g, "标题必须命中指定词", Fonts.Small, must ? T.Text : T.Text2, tg.X - 150, tg.Y + 5);
            AddHit("must", tg);

            // 新词输入
            T.Draw(g, "新词", Fonts.Small, T.Text2, c1.X + 20, c1.Y + 72);
            T.FillRound(g, new RectangleF(c1.X + 62, c1.Y + 62, 292, 38), 9f, T.Field);
            T.StrokeRound(g, new RectangleF(c1.X + 62, c1.Y + 62, 292, 38), 9f, T.LineSoft);
            T.Draw(g, "输入后点下面每组的「＋」加进去", Fonts.Tiny, T.Text3, c1.X + 364, c1.Y + 72);

            // 试算
            T.Draw(g, "试算", Fonts.Small, T.Text2, c1.X + 20, c1.Y + 114);
            T.FillRound(g, new RectangleF(c1.X + 62, c1.Y + 104, 292, 38), 9f, T.Field);
            T.StrokeRound(g, new RectangleF(c1.X + 62, c1.Y + 104, 292, 38), 9f, T.LineSoft);
            DrawGhost(g, new RectangleF(c1.X + 364, c1.Y + 108, 72, 30), "试算", "test");

            int sc; bool pass;
            var res = _r.Preview(_pre.Text, "", "", out sc, out pass);
            T.Draw(g, res, Fonts.Tiny, pass ? T.Jade : T.Warn, c1.X + 20, c1.Y + 152);
            T.Draw(g, "（试算只按标题判断；命令行的真实打分还会看公司和关键词）", Fonts.Tiny, T.Text3, c1.X + 20, c1.Y + 170);

            // ---------- 2×2 四个词表 ----------
            float cw = (W - 64 - 16) / 2f;
            DrawListCard(g, new RectangleF(32, 296, cw, 190), "标题必须包含", "include", _r.TitleInclude, "命不中就不会投");
            DrawListCard(g, new RectangleF(32 + cw + 16, 296, cw, 190), "标题排除词", "exclude", _r.TitleExclude, "命中直接跳过（0 分）");
            DrawListCard(g, new RectangleF(32, 500, cw, 190), "公司黑名单", "company", _r.CompanyBlacklist, "派遣 / 外包这类直接排除");
            DrawListCard(g, new RectangleF(32 + cw + 16, 500, cw, 190), "技能词（加分）", "skill", _r.SkillWords, "标题里出现就加分");

            // 保存提示
            T.Draw(g, "改完点右上「保存并生效」，下一轮投递就按新规则走（rules.json 存在 scripts 目录）", Fonts.Tiny, T.Text3, 34, 706);
        }

        private void DrawListCard(Graphics g, RectangleF card, string title, string key, List<string> words, string hint)
        {
            T.Card(g, card, 16f);
            T.Draw(g, title, Fonts.H2, T.Text, card.X + 20, card.Y + 16);
            T.TextR(g, words.Count + " 个", Fonts.Tiny, T.Text3, card.Right - 76, card.Y + 20);
            DrawGhost(g, new RectangleF(card.Right - 66, card.Y + 14, 46, 26), "＋", "add:" + key, T.JadeDim);
            T.Draw(g, hint, Fonts.Tiny, T.Text3, card.X + 20, card.Y + 44);

            float x = card.X + 20, y = card.Y + 66;
            float maxX = card.Right - 20;
            for (int i = 0; i < words.Count; i++)
            {
                var t = words[i];
                var sz = g.MeasureString(t + " ×", Fonts.Small);
                float cw = sz.Width + 22;
                if (x + cw > maxX) { x = card.X + 20; y += 32; }
                if (y + 26 > card.Bottom - 12) { T.Draw(g, "…还有 " + (words.Count - i) + " 个（点＋/× 可继续调）", Fonts.Tiny, T.Text3, x, y); return; }
                var rc = new RectangleF(x, y, cw, 26);
                bool hov = H("rm:" + key + ":" + i) > 0.02f;
                T.FillRound(g, rc, 13f, hov ? T.HoverSoftHi : T.HoverSoft);
                T.StrokeRound(g, rc, 13f, hov ? T.Jade : T.LineSoft);
                T.TextC(g, t + " ×", Fonts.Small, hov ? T.Text : T.Text2, rc);
                AddHit("rm:" + key + ":" + i, rc);
                x += cw + 8;
            }
        }

        private static List<string> ListOf(RulesCfg r, string key)
        {
            switch (key)
            {
                case "include": return r.TitleInclude;
                case "exclude": return r.TitleExclude;
                case "company": return r.CompanyBlacklist;
                default: return r.SkillWords;
            }
        }

        protected override void OnHit(string id)
        {
            if (id == "save")
            {
                _r.Save(App.Engine.ScriptDir);
                App.Toast("匹配规则已保存（分数线 " + _r.Threshold + "），下一轮生效");
                Invalidate();
                return;
            }
            if (id == "th:-") { if (_r.Threshold > 0) _r.Threshold -= 5; Invalidate(); return; }
            if (id == "th:+") { if (_r.Threshold < 100) _r.Threshold += 5; Invalidate(); return; }
            if (id == "must") { _r.TitleMust = !_r.TitleMust; Invalidate(); return; }
            if (id == "test") { Invalidate(); return; }

            if (id.StartsWith("add:"))
            {
                var key = id.Substring(4);
                var w = _add.Text.Trim();
                if (w.Length == 0) { App.Toast("先在「新词」框里输入要加的词"); return; }
                var list = ListOf(_r, key);
                if (!list.Contains(w)) list.Add(w);
                _add.Text = "";
                Invalidate();
                return;
            }
            if (id.StartsWith("rm:"))
            {
                var parts = id.Split(':');
                if (parts.Length == 3)
                {
                    int i;
                    if (int.TryParse(parts[2], out i))
                    {
                        var list = ListOf(_r, parts[1]);
                        if (i >= 0 && i < list.Count) list.RemoveAt(i);
                        Invalidate();
                    }
                }
            }
        }
    }
}
