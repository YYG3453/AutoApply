using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 匹配规则页：分数线、标题必含/排除、公司黑名单、技能词，
    /// 以及 v1.10.0 新增的学历 / 经验 / 福利 / 城市 / 薪资硬条件。
    /// 页面和每张词表卡片都有自己的滚动条；真正打分由 scripts\score.js 读同一份 rules.json。
    /// </summary>
    internal class RulesPage : Page
    {
        // 顶部三块是固定的：标题行（0–96）、分数线卡（100–290），只有下面的词表卡片滚动
        private const float ThTop = 100f;
        private const float ThH = 190f;
        private const float ScrollTop = ThTop + ThH + 14f;

        private RulesCfg _r;
        private CardBox _threshold;
        private readonly DarkText _add;
        private readonly DarkText _pre;

        private readonly Dictionary<string, float> _cardScroll = new Dictionary<string, float>();
        private readonly List<CardBox> _cards = new List<CardBox>();
        private float _pageScroll;
        private float _contentH;

        // 拖动滚动条时的状态
        private string _dragBar;
        private float _dragOff;
        private RectangleF _dragTrack;
        private float _dragThumbH;
        private float _dragMax;

        private class CardBox
        {
            public string Key = "";
            public string Title = "";
            public string Hint = "";
            public RectangleF Rect;
            public RectangleF Body;
            public bool Chips;
            public bool HasAdd = true;
            public List<string> Items;
            public float MaxScroll;
        }

        public RulesPage(MainForm app) : base(app)
        {
            _r = RulesCfg.Load(app.Engine.ScriptDir);

            // 城市只有一个来源：「平台与关键词 → 目标城市」。老 rules.json 里 CityExtra 记着的自定义城市
            // 补进目标城市，否则「只投这些」里选了它，脚本却根本不会去这个城市搜。
            bool cfgDirty = false;
            for (int i = 0; i < _r.CityExtra.Count; i++)
            {
                string c = _r.CityExtra[i];
                if (c.Length > 0 && !App.Cfg.Cities.Contains(c)) { App.Cfg.Cities.Add(c); cfgDirty = true; }
            }
            // 老数据：被「×」删掉的城市只记在 CityHidden 里，现在一并移出目标城市，保证「卡片上没有 = 不搜也不投」
            for (int i = 0; i < _r.CityHidden.Count; i++)
                if (App.Cfg.Cities.Remove(_r.CityHidden[i])) cfgDirty = true;
            if (cfgDirty) App.SaveCfg();

            _add = new DarkText();
            _pre = new DarkText();
            Controls.Add(_add);
            Controls.Add(_pre);
            _pre.Text = "实施工程师";
            Relayout();
        }

        // ---------------- 布局 ----------------

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        private float CardScroll(string key)
        {
            float v;
            return _cardScroll.TryGetValue(key, out v) ? v : 0f;
        }

        private float PageMax
        {
            get
            {
                float view = Height / T.S - ScrollTop - 6;
                return Math.Max(0f, _contentH - view);
            }
        }

        /// <summary>滚动区高度：分数线卡片以下是唯一的滚动视口。</summary>
        private float ViewH { get { return Math.Max(0f, Height / T.S - ScrollTop - 6); } }

        private CardBox CardByKey(string key)
        {
            for (int i = 0; i < _cards.Count; i++) if (_cards[i].Key == key) return _cards[i];
            return null;
        }

        /// <summary>只重排两个输入框：分数线卡固定，它们不再跟页面滚。</summary>
        private void Relayout()
        {
            float W = Width / T.S, pageH = Height / T.S;
            if (W <= 0 || pageH <= 0) return;
            var c1 = new RectangleF(32, ThTop, W - 64, ThH);
            PlaceField(_add, new RectangleF(c1.X + 68, c1.Y + 68, 280, 26), pageH);
            PlaceField(_pre, new RectangleF(c1.X + 68, c1.Y + 110, 280, 26), pageH);
        }

        private void PlaceField(Control c, RectangleF r, float pageH)
        {
            bool on = r.Top >= ThTop && r.Bottom <= pageH - 2;
            c.Visible = on;
            if (on) c.Bounds = new Rectangle(T.RS(r.X), T.RS(r.Y), T.RS(r.Width), T.RS(r.Height));
        }

        /// <summary>把卡片按页内局部坐标算好，再统一套上页面滚动量；顺便算内容总高。</summary>
        private void BuildCards(Graphics g, float W, float pageH)
        {
            // 分数线卡固定，不进滚动列表
            _threshold = new CardBox { Key = "threshold", Rect = new RectangleF(32, ThTop, W - 64, ThH) };

            var list = new List<CardBox>();
            float cw = (W - 64 - 16) / 2f;
            float y = 0;

            list.Add(new CardBox { Key = "include", Title = "标题必须包含", Hint = "命不中就不会投", Rect = new RectangleF(32, y, cw, 170), Chips = true, Items = _r.TitleInclude });
            list.Add(new CardBox { Key = "exclude", Title = "标题排除词", Hint = "命中直接跳过（0 分）", Rect = new RectangleF(32 + cw + 16, y, cw, 170), Chips = true, Items = _r.TitleExclude });
            y += 184;

            list.Add(new CardBox { Key = "company", Title = "公司黑名单", Hint = "派遣 / 外包这类直接排除", Rect = new RectangleF(32, y, cw, 170), Chips = true, Items = _r.CompanyBlacklist });
            list.Add(new CardBox { Key = "skill", Title = "技能词（加分）", Hint = "标题里出现就加分", Rect = new RectangleF(32 + cw + 16, y, cw, 170), Chips = true, Items = _r.SkillWords });
            y += 184;

            list.Add(new CardBox { Key = "edu", Title = "学历要求排除", Hint = "卡片出现「本科 / 硕士」就跳过", Rect = new RectangleF(32, y, cw, 170), Chips = true, Items = _r.EduExclude });
            list.Add(new CardBox { Key = "exp", Title = "经验要求排除", Hint = "出现「5年以上 / 10年以上」就跳过", Rect = new RectangleF(32 + cw + 16, y, cw, 170), Chips = true, Items = _r.ExpExclude });
            y += 184;

            list.Add(new CardBox { Key = "welfare", Title = "福利待遇排除（命中跳过）", Hint = "「单休 / 996 / 夜班」这类", Rect = new RectangleF(32, y, cw, 170), Chips = true, Items = _r.WelfareExclude });
            list.Add(new CardBox { Key = "welinc", Title = "福利待遇加分", Hint = "每项 +" + _r.WelfareBonus + "，最多 +" + _r.WelfareBonusCap, Rect = new RectangleF(32 + cw + 16, y, cw, 170), Chips = true, Items = _r.WelfareInclude });
            y += 184;

            // 城市（多选）+ 薪资范围：城市占满剩余宽度
            const float salW = 340f;
            float cityW = W - 64 - 16 - salW;
            var city = new CardBox { Key = "city", Title = "城市（只投这些）", Hint = "候选 ＝ 平台与关键词里的目标城市；点城市 = 只投它，一个不选 = 全投", Rect = new RectangleF(32, y, cityW, 226), Chips = true, HasAdd = true, Items = new List<string>() };
            for (int i = 0; i < App.Cfg.Cities.Count; i++)
            {
                string c0 = App.Cfg.Cities[i];
                if (_r.CityHidden.Contains(c0)) continue;   // 被「×」删掉的城市不再显示（等于移出目标城市）
                city.Items.Add(c0);
            }
            // 用户自己加的城市：候选池常驻，点「不限」只清选中，不会把这个词弄丢
            for (int i = 0; i < _r.CityExtra.Count; i++)
            {
                string c = _r.CityExtra[i];
                if (!city.Items.Contains(c)) city.Items.Add(c);
            }
            // 旧数据里自定义城市只存在 CityAllow 里，一并补进卡片
            for (int i = 0; i < _r.CityAllow.Count; i++)
            {
                string c = _r.CityAllow[i];
                if (!city.Items.Contains(c)) city.Items.Add(c);
            }
            city.Items.Insert(0, "不限");   // 排第一个，卡片滚起来也总能看见「不限」
            list.Add(city);
            list.Add(new CardBox { Key = "salary", Title = "薪资范围（K/月）", Hint = "0 = 不限；面议放行；年/日薪会自动换算", Rect = new RectangleF(32 + cityW + 16, y, salW, 226) });
            y += 184;

            _contentH = y + 44;

            // 页面滚动量夹紧（可视高度从 ScrollTop 起算）
            float maxPage = Math.Max(0f, _contentH - (pageH - ScrollTop - 6));
            if (_pageScroll > maxPage) _pageScroll = maxPage;
            if (_pageScroll < 0) _pageScroll = 0;
            float top = ScrollTop - _pageScroll;

            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                c.Rect = new RectangleF(c.Rect.X, c.Rect.Y + top, c.Rect.Width, c.Rect.Height);
                if (c.Chips) c.Body = new RectangleF(c.Rect.X + 20, c.Rect.Y + 64, c.Rect.Width - 40 - 12, c.Rect.Height - 64 - 12);
                else c.Body = c.Rect;
            }
            _cards.Clear();
            for (int i = 0; i < list.Count; i++) _cards.Add(list[i]);

            // 卡片内部滚动量夹紧（需要宽度来算词条高度）
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (!c.Chips) continue;
                float need = ChipTotalHeight(g, c.Items, c.Body.Width, c.Key == "city");
                c.MaxScroll = Math.Max(0f, need - c.Body.Height);
                float off = CardScroll(c.Key);
                if (off > c.MaxScroll) { off = c.MaxScroll; _cardScroll[c.Key] = off; }
            }
        }

        // ---------------- 绘制 ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            float W = Width / T.S, pageH = Height / T.S;
            ClearHits();

            // 固定头部：两个按钮放在副标题这一行、贴着右边缘；顶部 44px × 右侧 148px 是窗口按钮，不能占
            var saveR = new RectangleF(W - 32 - 132, 52, 132, 32);
            var defR = new RectangleF(saveR.X - 12 - 124, 52, 124, 32);

            T.Draw(g, "匹配规则", Fonts.H1, T.Text, 32, 18);

            string sub = "投递前先打分；命中硬条件就跳过，跳过记录进「被过滤」";
            float subMax = Math.Max(80f, defR.X - 32 - 16);
            if (g.MeasureString(sub, Fonts.Small).Width > subMax)
            {
                while (sub.Length > 6 && g.MeasureString(sub + "…", Fonts.Small).Width > subMax)
                    sub = sub.Substring(0, sub.Length - 1);
                sub = sub.TrimEnd('，', '、', '；') + "…";
            }
            T.Draw(g, sub, Fonts.Small, T.Text2, 32, 62);

            float shv = H("save");
            T.FillRound(g, saveR, 10f, T.Mix(T.Jade, T.JadeHi, shv));
            T.TextC(g, "保存并生效", T.F(12.5f, FontStyle.Bold), T.JadeInk, saveR);
            AddHit("save", saveR);

            float dhv = H("defaults");
            T.FillRound(g, defR, 10f, T.Mix(T.HoverSoft, T.HoverSoftHi, dhv));
            T.StrokeRound(g, defR, 10f, T.Mix(T.Line, T.Jade, dhv * 0.6f));
            T.TextC(g, "恢复默认", Fonts.Small, T.Mix(T.Text2, T.Text, dhv), defR);
            AddHit("defaults", defR);

            BuildCards(g, W, pageH);

            // 分数线卡固定在顶部，不随下面的词表卡片滚动
            DrawThresholdCard(g, _threshold);

            // 只有这条线以下的卡片和页脚滚动
            var clip = new RectangleF(0, ScrollTop, W, Math.Max(0f, pageH - ScrollTop));
            var st = g.Save();
            g.SetClip(clip);
            for (int i = 0; i < _cards.Count; i++) DrawCard(g, _cards[i], clip);
            float footerY = _cards[_cards.Count - 1].Rect.Bottom + 22;
            T.Draw(g, "改完点右上「保存并生效」，下一轮投递就按新规则走（和脚本共用同一份 rules.json）", Fonts.Tiny, T.Text3, 34, footerY);
            g.Restore(st);

            DrawPageBar(g, W, pageH);
            for (int i = 0; i < _cards.Count; i++)
                if (_cards[i].Chips) DrawCardBar(g, _cards[i]);

            PaintAppear(g, W, pageH);
        }

        private void DrawCard(Graphics g, CardBox c, RectangleF clip)
        {
            if (!c.Rect.IntersectsWith(clip)) return;
            if (c.Key == "salary") { DrawSalaryCard(g, c); return; }

            T.Card(g, c.Rect, 16f);

            // 卡片头部滚到固定区底下就看不见了，这时候别再挂命中区
            bool headOn = c.Rect.Y >= ScrollTop - 0.5f;
            if (headOn)
            {
                T.Draw(g, c.Title, Fonts.H2, T.Text, c.Rect.X + 20, c.Rect.Y + 16);
                string cnt = c.Key == "city"
                    ? (_r.CityAllow.Count == 0 ? "不限" : _r.CityAllow.Count + " 个已选")
                    : c.Items.Count + " 个";
                T.TextR(g, cnt, Fonts.Tiny, T.Text3, c.Rect.Right - 76, c.Rect.Y + 20);
                if (c.HasAdd) DrawGhost(g, new RectangleF(c.Rect.Right - 66, c.Rect.Y + 14, 46, 26), "＋", "add:" + c.Key, T.JadeDim);
                T.Draw(g, c.Hint, Fonts.Tiny, T.Text3, c.Rect.X + 20, c.Rect.Y + 44);
            }
            DrawChips(g, c, clip);
        }

        private float ChipW(Graphics g, string text, string suffix)
        {
            return g.MeasureString(text + suffix, Fonts.Small).Width + 22f;
        }

        /// <summary>城市按钮统一加宽，和 DrawChips 里保持同一口径。</summary>
        private float CityChipW(Graphics g, string label)
        {
            // 右边 28px 是「×」的地盘：文字和叉号之间空出距离，手滑点不到
            return Math.Max(ChipW(g, label, "") + 28f, 84f);
        }

        private float ChipTotalHeight(Graphics g, List<string> items, float width, bool wide)
        {
            float x = 0, y = 0;
            for (int i = 0; i < items.Count; i++)
            {
                float w = wide ? CityChipW(g, items[i]) : ChipW(g, items[i], " ×");
                if (x + w > width) { x = 0; y += 32; }
                x += w + 8;
            }
            return y + 26;
        }

        private void DrawChips(Graphics g, CardBox c, RectangleF clip)
        {
            var body = c.Body;
            float off = CardScroll(c.Key);
            var st = g.Save();
            g.SetClip(body, CombineMode.Intersect);   // 不能替换页面裁剪，否则滚出头的词会压到标题上
            float x = body.X, y = body.Y - off;
            for (int i = 0; i < c.Items.Count; i++)
            {
                var t = c.Items[i];
                bool isCity = c.Key == "city";
                bool isAll = isCity && t == "不限";
                bool withX = !isAll;                  // 每张卡的词条右侧都有独立的「×」（城市的「不限」除外）
                float xw = isCity ? 28f : 15f;        // 「×」独占的那一段：城市宽一点，其他卡 15px 够放下叉号
                string id, xid = null;
                bool selected = false;
                if (isCity)
                {
                    id = isAll ? "city:all" : "city:" + t;
                    selected = isAll ? _r.CityAllow.Count == 0 : _r.CityAllow.Contains(t);
                    if (!isAll) xid = "cx:" + t;
                }
                else
                {
                    id = "keep:" + c.Key + ":" + i;   // 点名字只提示，不删词；删词只认右边的「×」
                    xid = "cx:" + c.Key + ":" + i;
                }
                // 宽度照旧（仍按「词 + ×」的量算），按钮不会变宽；只是把文字和「×」拆成两块、中间留空
                float w = isCity ? CityChipW(g, t) : ChipW(g, t, " ×");
                if (x + w > body.Right) { x = body.X; y += 32; }
                var rc = new RectangleF(x, y, w, 26);
                bool xhov = withX && H(xid) > 0.02f;
                bool hov = H(id) > 0.02f || xhov;
                var fill = selected ? T.JadeDim : (hov ? T.HoverSoftHi : T.HoverSoft);
                var edge = selected ? T.Jade : (hov ? T.Jade : T.LineSoft);
                T.FillRound(g, rc, 13f, fill);
                T.StrokeRound(g, rc, 13f, edge);
                var txCol = selected ? T.Text : (hov ? T.Text : T.Text2);
                // 文字和叉号之间固定留 12px 空档；宽度不够时优先保空档、压缩左内边距
                float padL = 9f;
                if (!isCity)
                {
                    float tw = g.MeasureString(t, Fonts.Small).Width;
                    padL = Math.Max(6f, rc.Width - xw - 12f - tw);
                }
                if (isCity) T.TextC(g, t, Fonts.Small, txCol, new RectangleF(rc.X + 2f, rc.Y, rc.Width - xw - 2f, rc.Height));
                else
                {
                    var sz = g.MeasureString(t, Fonts.Small);
                    T.Draw(g, t, Fonts.Small, txCol, rc.X + padL, rc.Y + (rc.Height - sz.Height) / 2f);
                }
                if (withX)
                {
                    var xr = isCity
                        ? new RectangleF(rc.Right - xw + 4f, rc.Y, xw - 8f, rc.Height)
                        : new RectangleF(rc.Right - xw, rc.Y, xw, rc.Height);
                    T.TextC(g, "×", Fonts.Small, xhov ? T.Danger : T.Text3, xr);
                }
                if (rc.Top >= body.Top && rc.Bottom <= body.Bottom)
                {
                    // 只让看得见的那部分可点：滚到分数线下方的半个词不能隔着卡片吃到点击
                    var hr = RectangleF.Intersect(withX ? new RectangleF(rc.X, rc.Y, rc.Width - xw, rc.Height) : rc, clip);
                    // 悬停要重画的却是整颗胶囊：名字和 × 是同一块视觉，只脏半边会在胶囊上留下半截高亮
                    if (hr.Width > 2f && hr.Height > 2f) AddHit(id, hr, rc);
                    // 「×」单独一块命中区，后加优先：点名字 = 选中/删除，只有点在右边的叉上才走删除
                    if (withX)
                    {
                        var dr = isCity
                            ? new RectangleF(rc.Right - xw, rc.Y + 1, xw - 4f, rc.Height - 2f)
                            : new RectangleF(rc.Right - xw, rc.Y + 1, xw, rc.Height - 2f);
                        dr = RectangleF.Intersect(dr, clip);
                        if (dr.Width > 2f && dr.Height > 2f) AddHit(xid, dr, rc);
                    }
                }
                x += w + 8;
            }
            g.Restore(st);
        }

        private void DrawThresholdCard(Graphics g, CardBox c)
        {
            T.Card(g, c.Rect, 16f);
            T.Draw(g, "分数线", Fonts.Body, T.Text, c.Rect.X + 20, c.Rect.Y + 18);
            DrawGhost(g, new RectangleF(c.Rect.X + 96, c.Rect.Y + 12, 34, 30), "−", "th:-");
            T.TextC(g, _r.Threshold.ToString(), T.F(15f, FontStyle.Bold), T.Text, new RectangleF(c.Rect.X + 134, c.Rect.Y + 12, 56, 30));
            DrawGhost(g, new RectangleF(c.Rect.X + 194, c.Rect.Y + 12, 34, 30), "＋", "th:+");
            T.Draw(g, "建议 50–65", Fonts.Tiny, T.Text3, c.Rect.X + 240, c.Rect.Y + 22);

            // 标题必须命中开关
            var tg = new RectangleF(c.Rect.X + c.Rect.Width - 268, c.Rect.Y + 14, 244, 28);
            bool must = _r.TitleMust;
            T.FillRound(g, tg, 14f, must ? T.JadeDim : T.HoverSoft);
            T.StrokeRound(g, tg, 14f, must ? T.Jade : T.Line);
            float knobX = must ? tg.Right - 26 : tg.X + 4;
            T.FillRound(g, new RectangleF(knobX, tg.Y + 4, 22, 20), 10f, must ? T.Jade : T.Text3);
            // 标签右对齐贴在开关左边；窗口太窄就不画，免得压到「＋」和分数线上
            string mustLabel = "标题必须命中指定词";
            float mw = g.MeasureString(mustLabel, Fonts.Small).Width;
            float mx = tg.X - 12 - mw;
            if (mx >= c.Rect.X + 292) T.Draw(g, mustLabel, Fonts.Small, must ? T.Text : T.Text2, mx, tg.Y + 5);
            AddHit("must", tg);

            // 新词输入（输入框本体是 TextBox，这里只画底）
            T.Draw(g, "新词", Fonts.Small, T.Text2, c.Rect.X + 20, c.Rect.Y + 72);
            T.FillRound(g, new RectangleF(c.Rect.X + 62, c.Rect.Y + 62, 292, 38), 9f, T.Field);
            T.StrokeRound(g, new RectangleF(c.Rect.X + 62, c.Rect.Y + 62, 292, 38), 9f, T.LineSoft);
            T.Draw(g, "输入后点下面每组的「＋」加进去", Fonts.Tiny, T.Text3, c.Rect.X + 364, c.Rect.Y + 72);

            // 试算
            T.Draw(g, "试算", Fonts.Small, T.Text2, c.Rect.X + 20, c.Rect.Y + 114);
            T.FillRound(g, new RectangleF(c.Rect.X + 62, c.Rect.Y + 104, 292, 38), 9f, T.Field);
            T.StrokeRound(g, new RectangleF(c.Rect.X + 62, c.Rect.Y + 104, 292, 38), 9f, T.LineSoft);
            DrawGhost(g, new RectangleF(c.Rect.X + 364, c.Rect.Y + 108, 72, 30), "试算", "test");

            int sc; bool pass;
            var res = _r.Preview(_pre.Text, "", "", out sc, out pass);
            T.Draw(g, res, Fonts.Tiny, pass ? T.Jade : T.Warn, c.Rect.X + 20, c.Rect.Y + 152);
            T.Draw(g, "（试算只按标题+公司判断；城市和薪资要等真实卡片才能算）", Fonts.Tiny, T.Text3, c.Rect.X + 20, c.Rect.Y + 170);
        }

        private string KText(int v) { return v <= 0 ? "不限" : v + "K"; }

        private void DrawSalaryCard(Graphics g, CardBox c)
        {
            T.Card(g, c.Rect, 16f);
            T.Draw(g, c.Title, Fonts.H2, T.Text, c.Rect.X + 20, c.Rect.Y + 16);
            T.Draw(g, c.Hint, Fonts.Tiny, T.Text3, c.Rect.X + 20, c.Rect.Y + 42);

            // 最低月薪
            T.Draw(g, "最低月薪", Fonts.Small, T.Text2, c.Rect.X + 20, c.Rect.Y + 76);
            DrawGhost(g, new RectangleF(c.Rect.X + 90, c.Rect.Y + 70, 30, 28), "−", "sal:min:-");
            DrawGhost(g, new RectangleF(c.Rect.X + 126, c.Rect.Y + 70, 66, 28), KText(_r.SalaryMinK), "sal:min:0", T.Field);
            DrawGhost(g, new RectangleF(c.Rect.X + 198, c.Rect.Y + 70, 30, 28), "＋", "sal:min:+");
            T.Draw(g, _r.SalaryMinK > 0 ? "低于这个数的不投" : "不限（0）", Fonts.Tiny, T.Text3, c.Rect.X + 238, c.Rect.Y + 77);

            // 最高月薪
            T.Draw(g, "最高月薪", Fonts.Small, T.Text2, c.Rect.X + 20, c.Rect.Y + 114);
            DrawGhost(g, new RectangleF(c.Rect.X + 90, c.Rect.Y + 108, 30, 28), "−", "sal:max:-");
            DrawGhost(g, new RectangleF(c.Rect.X + 126, c.Rect.Y + 108, 66, 28), KText(_r.SalaryMaxK), "sal:max:0", T.Field);
            DrawGhost(g, new RectangleF(c.Rect.X + 198, c.Rect.Y + 108, 30, 28), "＋", "sal:max:+");
            T.Draw(g, _r.SalaryMaxK > 0 ? "高于这个数的不投" : "不限（0）", Fonts.Tiny, T.Text3, c.Rect.X + 238, c.Rect.Y + 115);

            T.Draw(g, "点数字可清零", Fonts.Tiny, T.Text3, c.Rect.X + 20, c.Rect.Y + 146);
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

        // ---------------- 滚动条 ----------------

        private void DrawPageBar(Graphics g, float W, float pageH)
        {
            float view = ViewH;
            if (_contentH <= view + 1) return;
            var track = new RectangleF(W - 17, ScrollTop + 6, 6, Math.Max(40f, pageH - ScrollTop - 18));
            float max = Math.Max(1f, _contentH - view);
            float thumbH = Math.Max(28f, track.Height * view / Math.Max(view + 1f, _contentH));
            float span = Math.Max(1f, track.Height - thumbH);
            float t = Math.Min(1f, Math.Max(0f, _pageScroll / max));
            var thumb = new RectangleF(track.X, track.Y + span * t, track.Width, thumbH);
            float hv = H("bar:page");
            T.FillRound(g, track, 3f, T.LineSoft);
            T.FillRound(g, thumb, 3f, T.Mix(T.Text3, T.Text, 0.2f + hv * 0.8f));
            AddHit("bar:page", RectangleF.Inflate(track, 6f, 4f));
        }

        private RectangleF CardBarTrack(CardBox c)
        {
            return new RectangleF(c.Rect.Right - 20, c.Body.Y, 4, c.Body.Height);
        }

        private void DrawCardBar(Graphics g, CardBox c)
        {
            if (c.MaxScroll <= 0.5f) return;
            float pageH = Height / T.S;
            if (c.Body.Bottom <= ScrollTop + 2 || c.Body.Y >= pageH) return;   // 整个卡片都被固定区挡住，别把滑块画到分数线上
            var track = CardBarTrack(c);
            float thumbH = Math.Max(20f, track.Height * c.Body.Height / Math.Max(c.Body.Height + 1f, c.Body.Height + c.MaxScroll));
            float span = Math.Max(1f, track.Height - thumbH);
            float off = CardScroll(c.Key);
            float t = Math.Min(1f, Math.Max(0f, off / Math.Max(1f, c.MaxScroll)));
            var thumb = new RectangleF(track.X, track.Y + span * t, track.Width, thumbH);
            float hv = H("bar:" + c.Key);
            T.FillRound(g, track, 2f, T.LineSoft);
            T.FillRound(g, thumb, 2f, T.Mix(T.Text3, T.Text, 0.2f + hv * 0.8f));
            AddHit("bar:" + c.Key, RectangleF.Inflate(track, 6f, 2f));
        }

        // ---------------- 交互 ----------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var p = T.LP(e.Location);
            var hit = HitAt(e.Location);
            if (hit != null && hit.Id != null && hit.Id.StartsWith("bar:"))
            {
                string key = hit.Id.Substring(4);
                _dragBar = key;
                var c = key == "page" ? null : CardByKey(key);
                float pageH = Height / T.S;
                RectangleF track;
                float view, max;
                if (key == "page")
                {
                    view = ViewH;
                    max = Math.Max(0f, _contentH - view);
                    track = new RectangleF(Width / T.S - 17, ScrollTop + 6, 6, Math.Max(40f, pageH - ScrollTop - 18));
                }
                else
                {
                    if (c == null) { _dragBar = null; return; }
                    view = c.Body.Height;
                    max = c.MaxScroll;
                    track = CardBarTrack(c);
                }
                float thumbH = Math.Max(key == "page" ? 28f : 20f, track.Height * view / Math.Max(view + 1f, view + max));
                float span = Math.Max(1f, track.Height - thumbH);
                float cur = key == "page" ? _pageScroll : CardScroll(key);
                float curT = Math.Min(1f, Math.Max(0f, cur / Math.Max(1f, max)));
                float thumbY = track.Y + span * curT;
                if (p.Y >= thumbY && p.Y <= thumbY + thumbH) _dragOff = p.Y - thumbY;
                else
                {
                    _dragOff = thumbH / 2f;
                    DragBarTo(p.Y);
                }
                _dragTrack = track;
                _dragThumbH = thumbH;
                _dragMax = max;
                Capture = true;
                Invalidate();
                return;
            }
            base.OnMouseDown(e);
        }

        private void DragBarTo(float mouseY)
        {
            var track = _dragTrack;
            float span = Math.Max(1f, track.Height - _dragThumbH);
            float t = (mouseY - track.Y - _dragOff) / span;
            if (t < 0) t = 0; if (t > 1) t = 1;
            float val = t * _dragMax;
            if (_dragBar == "page") _pageScroll = val;
            else if (_dragBar != null) _cardScroll[_dragBar] = val;
            Relayout();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragBar != null) { DragBarTo(T.LP(e.Location).Y); return; }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragBar != null)
            {
                _dragBar = null;
                Capture = false;
                Invalidate();
                return;
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var p = T.LP(e.Location);
            bool inScroll = p.Y >= ScrollTop - 1;   // 指针在固定区（标题/分数线）时滚整页，不碰卡片
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (!inScroll || !c.Chips || c.MaxScroll <= 0.5f) continue;
                if (!c.Body.Contains(p)) continue;
                float v = CardScroll(c.Key) - e.Delta / 120f * 48f;
                if (v < 0) v = 0; if (v > c.MaxScroll) v = c.MaxScroll;
                _cardScroll[c.Key] = v;
                Invalidate();
                return;
            }
            float page = _pageScroll - e.Delta / 120f * 60f;
            float max = PageMax;
            if (page < 0) page = 0; if (page > max) page = max;
            if (Math.Abs(page - _pageScroll) < 0.01f) return;
            _pageScroll = page;
            Relayout();
            Invalidate();
        }

        protected override void OnKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp)
            {
                float d = (e.KeyCode == Keys.PageDown ? 1f : -1f) * (ViewH * 0.9f);
                float max = PageMax;
                _pageScroll += d;
                if (_pageScroll < 0) _pageScroll = 0;
                if (_pageScroll > max) _pageScroll = max;
                Relayout();
                Invalidate();
                e.Handled = true;
            }
        }

        // ---------------- 操作 ----------------

        private static List<string> ListOf(RulesCfg r, string key)
        {
            switch (key)
            {
                case "include": return r.TitleInclude;
                case "exclude": return r.TitleExclude;
                case "company": return r.CompanyBlacklist;
                case "skill": return r.SkillWords;
                case "edu": return r.EduExclude;
                case "exp": return r.ExpExclude;
                case "welfare": return r.WelfareExclude;
                case "welinc": return r.WelfareInclude;
                default: return null;
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
            if (id == "defaults")
            {
                _r = RulesCfg.Default();
                // 城市是单一来源：恢复默认规则时，被「×」删掉的目标城市也一并放回来
                if (App.CanEdit)
                {
                    var all = AppCfg.DefaultCities();
                    bool dirty = false;
                    for (int i = 0; i < all.Length; i++)
                        if (!App.Cfg.Cities.Contains(all[i])) { App.Cfg.Cities.Add(all[i]); dirty = true; }
                    if (dirty) App.SaveCfg();
                }
                App.Toast("已恢复默认规则；点「保存并生效」后才会写入 rules.json");
                Relayout();
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
                if (key == "city")
                {
                    // 城市卡的「＋」：城市进候选池（常驻），同时选中它
                    if (w == "不限") { _add.Text = ""; Invalidate(); return; }
                    if (!App.CanEdit) { App.Toast("投递进行中，先点「停止」再改城市"); return; }
                    _r.CityHidden.Remove(w);   // 之前被「×」删掉的城市，再加回来就恢复显示
                    if (!App.Cfg.Cities.Contains(w)) { App.Cfg.Cities.Add(w); App.SaveCfg(); }   // 同步进目标城市，脚本才会去搜
                    if (!_r.CityExtra.Contains(w)) _r.CityExtra.Add(w);
                    if (!_r.CityAllow.Contains(w)) _r.CityAllow.Add(w);
                    _add.Text = "";
                    Relayout();
                    Invalidate();
                    return;
                }
                var list = ListOf(_r, key);
                if (list == null) return;
                if (!list.Contains(w)) list.Add(w);
                _add.Text = "";
                Invalidate();
                return;
            }
            if (id.StartsWith("keep:"))
            {
                // 词表词条的正文区：以前点整条就删词，现在只有右边的「×」才删，正文点一下只是提示
                App.Toast("要删这个词，点它右边的「×」");
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
                        if (list != null && i >= 0 && i < list.Count) list.RemoveAt(i);
                        Invalidate();
                    }
                }
                return;
            }
            if (id == "city:all") { _r.CityAllow.Clear(); Invalidate(); return; }
            if (id.StartsWith("cx:"))
            {
                // 普通词表词条上的「×」：id 形如 cx:<卡片key>:<下标>（城市用的是城市名，见下）
                var parts2 = id.Substring(3).Split(':');
                if (parts2.Length == 2)
                {
                    int idx;
                    if (int.TryParse(parts2[1], out idx))
                    {
                        var list2 = ListOf(_r, parts2[0]);
                        if (list2 != null && idx >= 0 && idx < list2.Count) list2.RemoveAt(idx);
                        Invalidate();
                        return;
                    }
                }
                // 城市词条上的「×」：把这个城市从卡片上删掉，同时移出目标城市（否则搜索范围里还留着它）
                var name = id.Substring(3);
                if (!App.CanEdit) { App.Toast("投递进行中，先点「停止」再改城市"); return; }
                _r.CityAllow.Remove(name);
                _r.CityExtra.Remove(name);
                if (App.Cfg.Cities.Remove(name)) App.SaveCfg();
                if (!_r.CityHidden.Contains(name)) _r.CityHidden.Add(name);
                Invalidate();
                return;
            }
            if (id.StartsWith("city:"))
            {
                var name = id.Substring(5);
                if (_r.CityAllow.Contains(name)) _r.CityAllow.Remove(name);
                else _r.CityAllow.Add(name);
                Invalidate();
                return;
            }
            if (id == "sal:min:-") { _r.SalaryMinK = Math.Max(0, _r.SalaryMinK - 1); Invalidate(); return; }
            if (id == "sal:min:+")
            {
                _r.SalaryMinK = Math.Min(80, _r.SalaryMinK + 1);
                if (_r.SalaryMaxK > 0 && _r.SalaryMinK > _r.SalaryMaxK) _r.SalaryMaxK = _r.SalaryMinK;
                Invalidate();
                return;
            }
            if (id == "sal:min:0") { _r.SalaryMinK = 0; Invalidate(); return; }
            if (id == "sal:max:-")
            {
                _r.SalaryMaxK = Math.Max(0, _r.SalaryMaxK - 1);
                if (_r.SalaryMaxK > 0 && _r.SalaryMinK > _r.SalaryMaxK) _r.SalaryMinK = _r.SalaryMaxK;
                Invalidate();
                return;
            }
            if (id == "sal:max:+") { _r.SalaryMaxK = Math.Min(80, _r.SalaryMaxK + 1); Invalidate(); return; }
            if (id == "sal:max:0") { _r.SalaryMaxK = 0; Invalidate(); return; }
        }
    }
}
