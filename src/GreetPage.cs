using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 招呼语版本页。
    ///
    /// 为什么是这个形态：四个平台的招呼语都存在**账号设置**里，由平台在"立即沟通/投递"时
    /// 自动带出去（实测智联的投递弹窗里连输入框都没有，纯展示）。程序改不了那一句，
    /// 所以这页只管三件事：① 存用户自己写的几版文本，点一下复制；② 记住"从哪一刻起用的是哪一版"；
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

        private const float CardH = 118f;
        private const float CardGap = 10f;

        private List<Stat> _stats = new List<Stat>();
        private int _unassigned;
        private int _repliedTotal;
        private string _stamp = "";
        private string _sig = "";
        private float _scroll;

        private readonly DarkText _edName;
        private readonly DarkText _edText;
        private bool _editing;
        private string _delId = "";        // 已点过一次删除、等第二次确认的版本
        private DateTime _delAt = DateTime.MinValue;
        private string _editId = "";        // 空串 = 新建

        public GreetPage(MainForm app) : base(app)
        {
            BackColor = T.Bg;

            _edName = new DarkText();
            _edName.Visible = false;
            Controls.Add(_edName);

            _edText = new DarkText();
            _edText.Multiline = true;
            _edText.ScrollBars = ScrollBars.Vertical;
            _edText.AcceptsReturn = true;
            _edText.Visible = false;
            Controls.Add(_edText);

            _edName.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseEditor(); }
                else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _edText.Focus(); }
            };
            _edText.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseEditor(); }
                else if (e.KeyCode == Keys.Enter && e.Control) { e.SuppressKeyPress = true; SaveEditor(); }
            };
            _edText.TextChanged += delegate { if (_editing) Invalidate(); };
        }

        /// <summary>数据/版本变了才重算（每帧都算也能跑，但没必要）。</summary>
        private string SigOf(AppCfg cfg)
        {
            var b = new System.Text.StringBuilder();
            var recs = App.Records;
            b.Append(recs == null ? 0 : recs.Total).Append('|');
            b.Append(recs == null || recs.Replies == null ? 0 : recs.Replies.Count).Append('|');
            b.Append(cfg.GreetActive).Append('|');
            b.Append(cfg.GreetHistory == null ? 0 : cfg.GreetHistory.Count).Append('|');
            if (cfg.Greetings != null)
                for (int i = 0; i < cfg.Greetings.Count; i++)
                {
                    var g = cfg.Greetings[i];
                    if (g == null) { b.Append("null;"); continue; }
                    b.Append(g.Id).Append(':').Append(g.Name == null ? 0 : g.Name.Length)
                     .Append(':').Append(g.Text == null ? 0 : g.Text.Length).Append(";");
                }
            return b.ToString();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) return;
            _sig = SigOf(App.Cfg);
            Recompute();
            Invalidate();
        }

        public override void RefreshTheme()
        {
            BackColor = T.Bg;
            _edName.RefreshTheme();
            _edText.RefreshTheme();
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

            // 回复按公司名归一化后建索引：一条回复算到"该公司那条投递"所属的版本上
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

        private Stat StatOf(string id)
        {
            for (int i = 0; i < _stats.Count; i++) if (_stats[i].Id == id) return _stats[i];
            return null;
        }

        private int CountAssigned()
        {
            int n = 0;
            for (int i = 0; i < _stats.Count; i++) n += _stats[i].Apps;
            return n;
        }

        // ---------- 画面 ----------

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
            PaintBody(g, W, H);
            if (_editing) PaintEditor(g, W, H);
        }

        private void PaintBody(Graphics g, float W, float H)
        {
            var cfg0 = App.Cfg;
            var sig = SigOf(cfg0);
            if (sig != _sig) { _sig = sig; Recompute(); }
            var cfg = App.Cfg;

            T.Draw(g, "招呼语版本", Fonts.H1, T.Text, 32, 22);
            T.Draw(g, "平台里那一句程序改不了；这页存你自己写的版本，记下切换到哪一版的时刻，算出每版的回复率", Fonts.Small, T.Text3, 32, 60);
            Btn(g, new RectangleF(W - 32 - 104, 24, 104, 30), "新建一版", "new", true);

            // ---- 摘要卡 ----
            var head = new RectangleF(32, 94, W - 64, 76);
            T.Card(g, head, 14f);
            var act = cfg.GreetById(cfg.GreetActive);
            T.Dot(g, head.X + 26, head.Y + 27, 5f, T.Jade);
            string actName = act == null ? "当前：未设置"
                : "当前：" + act.Id + " · " + (string.IsNullOrEmpty(act.Name) ? "未命名" : act.Name);
            T.Draw(g, actName, Fonts.H2, T.Text, head.X + 42, head.Y + 15);

            string sinceText = "—";
            int hours = 0;
            if (cfg.GreetHistory != null && cfg.GreetHistory.Count > 0)
            {
                var last = cfg.GreetHistory[cfg.GreetHistory.Count - 1];
                sinceText = last.From.ToString("MM-dd HH:mm");
                hours = Math.Max(0, (int)(DateTime.Now - last.From).TotalHours);
            }
            int total = App.Records == null ? 0 : App.Records.Total;
            T.Draw(g, "从 " + sinceText + " 起生效（" + hours + " 小时）· 全部投递 " + total + " 条 · 能归属 " + CountAssigned()
                + " 条 · 回复 " + _repliedTotal + " 家 · 共 " + _stats.Count + " 版 · 更新 " + _stamp,
                Fonts.Small, T.Text2, head.X + 42, head.Y + 45);

            // ---- 版本列表（超出可滚动）----
            float listTop = head.Bottom + 12f;
            float notesH = 88f;
            float notesY = H - 22f - notesH;
            float listBottom = notesY - 12f;
            var clip = new RectangleF(32, listTop, W - 64, Math.Max(20f, listBottom - listTop));

            // 至少摆 3 个位置：用掉的画版本卡，没用掉的画"新建"槽位
            int slots = Math.Max(3, _stats.Count);
            float contentH = slots * CardH + Math.Max(0, slots - 1) * CardGap;
            float maxScroll = Math.Max(0f, contentH - clip.Height);
            if (_scroll > maxScroll) _scroll = maxScroll;
            if (_scroll < 0f) _scroll = 0f;

            g.SetClip(clip, CombineMode.Replace);
            for (int i = 0; i < _stats.Count; i++)
            {
                var card = new RectangleF(32, listTop + i * (CardH + CardGap) - _scroll, W - 64, CardH);
                if (card.Bottom < clip.Top - 8f || card.Y > clip.Bottom + 8f) continue;
                bool whole = card.Y >= clip.Top - 0.5f && card.Bottom <= clip.Bottom + 0.5f;
                PaintCard(g, card, _stats[i], cfg, whole);
            }
            for (int i = _stats.Count; i < slots; i++)
            {
                var card = new RectangleF(32, listTop + i * (CardH + CardGap) - _scroll, W - 64, CardH);
                if (card.Bottom < clip.Top - 8f || card.Y > clip.Bottom + 8f) continue;
                bool whole = card.Y >= clip.Top - 0.5f && card.Bottom <= clip.Bottom + 0.5f;
                PaintSlot(g, card, i, whole);
            }
            g.ResetClip();

            if (maxScroll > 0f)
            {
                // 右侧细滚动条
                float trackX = W - 12f, trackY = listTop, trackH = clip.Height;
                T.FillRound(g, new RectangleF(trackX, trackY, 4f, trackH), 2f, T.LineSoft);
                float thumbH = Math.Max(28f, trackH * (trackH / contentH));
                float thumbY = trackY + (trackH - thumbH) * (_scroll / maxScroll);
                T.FillRound(g, new RectangleF(trackX, thumbY, 4f, thumbH), 2f, T.Text3);
            }

            // ---- 底部说明 ----
            if (notesY + notesH <= H - 8f)
            {
                float lw = (W - 64f) * 0.58f;
                var cl = new RectangleF(32, notesY, lw, notesH);
                var cr = new RectangleF(32 + lw + 12f, notesY, W - 64f - lw - 12f, notesH);
                T.Card(g, cl, 14f);
                T.Draw(g, "怎么用（三步）", Fonts.H2, T.Text, cl.X + 18, cl.Y + 12);
                T.Draw(g, "① 这里写好一版 → ② 复制，粘到四个平台的打招呼设置里 → ③ 回来点「设为当前」", Fonts.Small, T.Text2, cl.X + 18, cl.Y + 38);
                T.Draw(g, "四个平台各粘一次；粘贴前的投递不算这一版，所以换完再点「设为当前」。", Fonts.Tiny, T.Text3, cl.X + 18, cl.Y + 60);

                T.Card(g, cr, 14f);
                T.Draw(g, "统计口径", Fonts.H2, T.Text, cr.X + 18, cr.Y + 12);
                T.Draw(g, "回复按公司名对上投递，同一家公司只算一次。", Fonts.Small, T.Text2, cr.X + 18, cr.Y + 38);
                T.Draw(g, "早于时间线起点的投递不归属任何版本" + (_unassigned > 0 ? "（现在 " + _unassigned + " 条）" : "") + "。", Fonts.Tiny, T.Text3, cr.X + 18, cr.Y + 60);
            }
        }

        /// <summary>空槽位：虚线框 + 加号，点一下开编辑器新建。给"还能不能再加两版"一个看得见的位置。</summary>
        private void PaintSlot(Graphics g, RectangleF r, int idx, bool addHits)
        {
            float hv = addHits ? H("new") : 0f;
            T.FillRound(g, r, 14f, T.Mix(T.Panel2, T.Alpha(T.Jade, 16), hv));
            var rr = new RectangleF(r.X + 0.6f, r.Y + 0.6f, r.Width - 1.2f, r.Height - 1.2f);
            using (var path = T.Round(rr, 14f))
            using (var pen = new Pen(T.Mix(T.Line, T.Alpha(T.Jade, 170), Math.Max(0.35f, hv)), 1f))
            {
                pen.DashStyle = DashStyle.Dash;
                pen.DashPattern = new float[] { 3.2f, 2.6f };
                g.DrawPath(pen, path);
            }

            // 中间的加号 + 文案
            float cx = r.X + r.Width / 2f;
            var circ = new RectangleF(cx - 15, r.Y + 26, 30, 30);
            T.FillRound(g, circ, 15f, T.Mix(T.Panel3, T.Alpha(T.Jade, 46), hv));
            using (var p = new Pen(T.Mix(T.Text3, T.Jade, hv), 1.6f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx - 7, circ.Y + 15, cx + 7, circ.Y + 15);
                g.DrawLine(p, cx, circ.Y + 8, cx, circ.Y + 22);
            }
            bool hot = hv > 0.5f;
            T.TextC(g, "新建一版（第 " + (idx + 1) + " 个位置）", Fonts.Body, hot ? T.Text : T.Text2,
                    new RectangleF(r.X + 20, circ.Bottom + 10, r.Width - 40, 22));
            T.TextC(g, "写你自己的话；保存后可以复制、可以设为当前", Fonts.Tiny, T.Text3,
                    new RectangleF(r.X + 20, circ.Bottom + 32, r.Width - 40, 18));

            if (addHits) AddHit("new", r);
        }

        private void PaintCard(Graphics g, RectangleF card, Stat st, AppCfg cfg, bool addHits)
        {
            var vc = cfg.GreetById(st.Id);
            bool isActive = vc != null && vc.Id == cfg.GreetActive;
            T.Card(g, card, 14f, addHits && H("use:" + st.Id) > 0.5f);
            if (isActive) T.StrokeRound(g, card, 14f, T.Jade, 1.5f);

            // 版本徽标
            var badge = new RectangleF(card.X + 16, card.Y + 13, 30, 24);
            T.FillRound(g, badge, 7f, isActive ? T.Jade : T.Panel3);
            T.TextC(g, st.Id, Fonts.H2, isActive ? T.JadeInk : T.Text2, badge);

            string nm = vc == null ? "（已删除）" : (string.IsNullOrEmpty(vc.Name) ? "未命名" : vc.Name);
            T.Draw(g, nm, Fonts.H2, T.Text, card.X + 56, card.Y + 11);
            float nw = g.MeasureString(nm, Fonts.H2).Width;
            float chipX = card.X + 56 + nw + 10;
            if (isActive)
            {
                var chip = new RectangleF(chipX, card.Y + 14, 46, 19);
                T.FillRound(g, chip, 9f, T.JadeDim);
                T.TextC(g, "使用中", Fonts.Tiny, T.Jade, chip);
                chipX += 56;
            }
            if (vc != null && vc.Text != null)
                T.Draw(g, vc.Text.Length + " 字", Fonts.Tiny, T.Text3, chipX, card.Y + 17);

            // 文案两行
            var lines = Wrap(g, vc == null ? "" : vc.Text, Fonts.Small, card.Width - 330f, 2);
            float ty = card.Y + 40f;
            for (int k = 0; k < lines.Count; k++) { T.Draw(g, lines[k], Fonts.Small, T.Text2, card.X + 18, ty); ty += 17f; }

            // 右侧统计
            float sx = card.Right - 306;
            T.Draw(g, "投递", Fonts.Tiny, T.Text3, sx, card.Y + 14);
            T.Draw(g, st.Apps.ToString(), Fonts.Num, T.Text, sx, card.Y + 30);
            T.Draw(g, "回复", Fonts.Tiny, T.Text3, sx + 78, card.Y + 14);
            T.Draw(g, st.Replied.ToString(), Fonts.Num, st.Replied > 0 ? T.Jade : T.Text3, sx + 78, card.Y + 30);
            T.Draw(g, "回复率", Fonts.Tiny, T.Text3, sx + 148, card.Y + 14);
            T.Draw(g, st.Rate < 0 ? "—" : (st.Rate * 100).ToString("0.0") + "%", Fonts.Num,
                st.Rate < 0 ? T.Text3 : (st.Rate > 0 ? T.Jade : T.Text2), sx + 148, card.Y + 30);

            // 按钮行：删除 | 编辑 | 复制 | 设为当前
            float by = card.Bottom - 42f;
            float rx = card.Right - 14f;
            rx -= 84f;
            var actR = new RectangleF(rx, by, 84f, 28f);
            rx -= 8f + 48f;
            var copyR = new RectangleF(rx, by, 48f, 28f);
            rx -= 8f + 48f;
            var editR = new RectangleF(rx, by, 48f, 28f);
            rx -= 8f + 48f;
            var delR = new RectangleF(rx, by, 48f, 28f);

            if (!addHits) return;

            if (isActive)
            {
                T.StrokeRound(g, actR, 8f, T.LineSoft);
                T.TextC(g, "正在使用", Fonts.Small, T.Text3, actR);
            }
            else Btn(g, actR, "设为当前", "use:" + st.Id, true);
            Btn(g, copyR, "复制", "copy:" + st.Id, false);
            Btn(g, editR, "编辑", "edit:" + st.Id, false);
            if (_delId == st.Id && (DateTime.Now - _delAt).TotalSeconds < 6d)
            {
                T.FillRound(g, delR, 8f, T.Mix(T.HoverSoft, T.Danger, 0.22f));
                T.StrokeRound(g, delR, 8f, T.Danger);
                T.TextC(g, "确认", Fonts.Small, T.Danger, delR);
                AddHit("del:" + st.Id, delR);
            }
            else Btn(g, delR, "删除", "del:" + st.Id, false);
        }

        // ---------- 编辑器 ----------

        private RectangleF EditorRect(float W, float H)
        {
            float w = Math.Min(760f, W - 100f);
            float h = Math.Min(400f, H - 100f);
            return new RectangleF((W - w) / 2f, (H - h) / 2f, w, h);
        }

        private void FieldRects(RectangleF p, out RectangleF nr, out RectangleF br)
        {
            nr = new RectangleF(p.X + 20f, p.Y + 84f, p.Width - 40f, 30f);
            br = new RectangleF(p.X + 20f, p.Y + 142f, p.Width - 40f, Math.Max(80f, p.Height - 142f - 84f));
        }

        private void RelayoutEditor()
        {
            if (!_editing) return;
            var p = EditorRect(Width / T.S, Height / T.S);
            RectangleF nr, br;
            FieldRects(p, out nr, out br);
            _edName.Bounds = new Rectangle(T.RS(nr.X), T.RS(nr.Y), T.RS(nr.Width), T.RS(nr.Height));
            _edText.Bounds = new Rectangle(T.RS(br.X), T.RS(br.Y), T.RS(br.Width), T.RS(br.Height));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RelayoutEditor();
        }

        private void PaintEditor(Graphics g, float W, float H)
        {
            ClearHits();     // 编辑时只有编辑层可点，底下的卡片不能误触

            using (var mask = new SolidBrush(T.Alpha(Color.Black, 150)))
                g.FillRectangle(mask, 0, 0, W, H);

            var p = EditorRect(W, H);
            T.Card(g, p, 16f);

            T.Draw(g, _editId.Length == 0 ? "新建一版招呼语" : "编辑 " + _editId + " 版", Fonts.H2, T.Text, p.X + 20, p.Y + 16);
            T.Draw(g, _editId.Length == 0 ? "写你自己想发的那句；程序不会替你编字" : "改文字不会新建版本，投递还都算在这一版", Fonts.Tiny, T.Text3, p.X + 20, p.Y + 42);

            RectangleF nr, br;
            FieldRects(p, out nr, out br);

            T.Draw(g, "名称（只在这页里区分版本，不会发出去）", Fonts.Tiny, T.Text3, p.X + 20, p.Y + 66);
            T.FillRound(g, new RectangleF(nr.X - 6, nr.Y - 6, nr.Width + 12, nr.Height + 12), 10f, T.RailTop);
            T.StrokeRound(g, new RectangleF(nr.X - 6, nr.Y - 6, nr.Width + 12, nr.Height + 12), 10f, T.Line);

            T.Draw(g, "正文（粘到平台里的就是这段）", Fonts.Tiny, T.Text3, p.X + 20, p.Y + 124);
            T.FillRound(g, new RectangleF(br.X - 6, br.Y - 6, br.Width + 12, br.Height + 12), 10f, T.RailTop);
            T.StrokeRound(g, new RectangleF(br.X - 6, br.Y - 6, br.Width + 12, br.Height + 12), 10f, T.Line);

            int n = _edText.Text == null ? 0 : _edText.Text.Length;
            var vc = App.Cfg.GreetById(_editId);
            bool editingActive = vc != null && vc.Id == App.Cfg.GreetActive;
            string warn = editingActive
                ? "这是当前在用的一版：改完文字，回复率还是连着旧文字一起算；要分开比较请新建一版。"
                : "建议 60–120 字；太长平台会截断，换行粘进平台后可能被吃掉。";
            T.Draw(g, n + " 字 · " + warn, Fonts.Tiny, editingActive ? T.Warn : T.Text3, p.X + 20, br.Bottom + 9);

            float by = p.Bottom - 52f;
            Btn(g, new RectangleF(p.Right - 20 - 84, by, 84, 30), "保存", "edsave", true);
            Btn(g, new RectangleF(p.Right - 20 - 84 - 8 - 76, by, 76, 30), "取消", "edcancel", false);
            T.Draw(g, "Ctrl+Enter 保存 · Esc 取消", Fonts.Tiny, T.Text3, p.X + 20, by + 8);
        }

        private void OpenEditor(string id)
        {
            _editId = id == null ? "" : id;
            var cfg = App.Cfg;
            var vc = cfg.GreetById(_editId);
            if (vc != null)
            {
                _edName.Text = vc.Name;
                _edText.Text = vc.Text;
            }
            else
            {
                _edName.Text = "自定义 " + cfg.NextGreetId();
                _edText.Text = "";
            }
            _editing = true;
            _edName.Visible = true;
            _edText.Visible = true;
            RelayoutEditor();
            _edName.BringToFront();
            _edText.BringToFront();
            _edName.Focus();
            if (vc == null) _edName.SelectAll();
            else { _edName.SelectionStart = _edName.Text.Length; _edName.SelectionLength = 0; }
            Invalidate();
        }

        private void CloseEditor()
        {
            _editing = false;
            _edName.Visible = false;
            _edText.Visible = false;
            _editId = "";
            Invalidate();
        }

        private void SaveEditor()
        {
            var text = (_edText.Text ?? "").Trim();
            var name = (_edName.Text ?? "").Trim();
            if (text.Length < 8)
            {
                App.Toast("正文太短了，至少写一句完整的话");
                _edText.Focus();
                return;
            }
            if (name.Length == 0) name = "自定义";
            var cfg = App.Cfg;
            var vc = cfg.GreetById(_editId);
            if (vc != null)
            {
                vc.Name = name;
                vc.Text = text;
                App.Toast(vc.Id == cfg.GreetActive ? "已改 " + vc.Id + " 版（平台里也记得同步改）" : "已改 " + vc.Id + " 版");
            }
            else
            {
                var g = cfg.AddGreeting(name, text);
                App.Toast("已新建 " + g.Id + " 版；点「设为当前」才开始按它算");
            }
            App.SaveCfg();
            CloseEditor();
            Recompute();
            Invalidate();
        }

        // ---------- 交互 ----------

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_editing) return;
            _scroll -= e.Delta / 120f * (CardH + CardGap) * 0.6f;
            Invalidate();
        }

        protected override void OnHit(string id)
        {
            if (id == null) return;

            if (!id.StartsWith("del:")) _delId = "";
            if (id == "new") { OpenEditor(""); return; }
            if (id == "edsave") { SaveEditor(); return; }
            if (id == "edcancel") { CloseEditor(); return; }

            if (id.StartsWith("copy:"))
            {
                var vc = App.Cfg.GreetById(id.Substring(5));
                if (vc == null) return;
                try { Clipboard.SetText(vc.Text); App.Toast("已复制 " + vc.Id + " 版，去平台粘贴"); }
                catch { App.Toast("复制失败：剪贴板被占用"); }
                return;
            }
            if (id.StartsWith("edit:")) { OpenEditor(id.Substring(5)); return; }
            if (id.StartsWith("del:"))
            {
                var vid = id.Substring(4);
                var cfg = App.Cfg;
                if (!(_delId == vid && (DateTime.Now - _delAt).TotalSeconds < 6d))
                {
                    _delId = vid;
                    _delAt = DateTime.Now;
                    App.Toast("再点一次「确认」就删掉 " + vid + " 版（6 秒内有效）");
                    Invalidate();
                    return;
                }
                _delId = "";
                if (cfg.Greetings == null || cfg.Greetings.Count <= 1) { App.Toast("至少留一版"); return; }
                var st = StatOf(vid);
                if (st != null && st.Apps > 0)
                {
                    App.Toast(vid + " 版已有 " + st.Apps + " 条投递，删了这几条的归属就没了；要改文字请点「编辑」");
                    return;
                }
                cfg.RemoveGreeting(vid);
                App.SaveCfg();
                Recompute();
                Invalidate();
                App.Toast("已删除 " + vid + " 版");
                return;
            }
            if (id.StartsWith("use:"))
            {
                var vid = id.Substring(4);
                if (App.Cfg.GreetActive == vid) { App.Toast(vid + " 版已经是当前版本了"); return; }
                if (!App.CanEdit) { App.Toast("投递进行中，跑完再切版本，否则这一轮算不清归属"); return; }
                App.Cfg.SetGreetActive(vid);
                App.SaveCfg();
                Recompute();
                Invalidate();
                App.Toast("已切到 " + vid + " 版：从现在起的投递都算这一版");
            }
        }
    }
}
