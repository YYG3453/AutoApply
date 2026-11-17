using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 被过滤页：把规则拦下来的岗位和原因摊开，回答"为什么没投这家"。
    /// 数据来自 scripts\state-skip.jsonl。
    /// </summary>
    internal class FilteredPage : Page
    {
        private float _scroll;
        private int _hoverRow = -1;
        private const float RowH = 30f;
        private const float HeadH = 30f;

        public FilteredPage(MainForm app) : base(app) { }

        private static string SiteName(SkipRec r)
        {
            if (r.SiteName == null) return r.SiteKey;
            return r.SiteName.Length > 0 ? r.SiteName : r.SiteKey;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            var W = Width / T.S;
            var H = Height / T.S;
            ClearHits();

            T.Draw(g, "被过滤", Fonts.H1, T.Text, 32, 20);
            var all = App.Records == null ? new List<SkipRec>() : App.Records.Skips;
            int today = App.Records == null ? 0 : App.Records.SkippedToday;
            int tot = App.Records == null ? 0 : App.Records.SkippedTotal;
            T.Draw(g, "规则拦下来的岗位 · 今天 " + today + " 条 · 累计 " + tot + " 条（规则在「匹配规则」里改）", Fonts.Small, T.Text2, 32, 55);

            // 主因 Top（最多 3 个）
            float cx = 32;
            if (App.Records != null && App.Records.SkipByReason.Count > 0)
            {
                var kv = new List<KeyValuePair<string, int>>(App.Records.SkipByReason);
                kv.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
                int n = Math.Min(3, kv.Count);
                for (int i = 0; i < n; i++)
                {
                    var txt = kv[i].Key + " " + kv[i].Value;
                    var sz = g.MeasureString(txt, Fonts.Small);
                    var rc = new RectangleF(cx, 82, sz.Width + 22, 26);
                    T.FillRound(g, rc, 13f, T.HoverSoft);
                    T.StrokeRound(g, rc, 13f, T.LineSoft);
                    T.TextC(g, txt, Fonts.Small, T.Text2, rc);
                    cx = rc.Right + 8;
                }
            }

            float ty = 118;
            var card = new RectangleF(32, ty, W - 64, H - ty - 22);
            T.Card(g, card, 16f);

            float cTime = 92f, cSite = 92f, cScore = 64f, cRea = 250f;
            float rest = card.Width - 40 - cTime - cSite - cScore - cRea;
            float cTitle = rest * 0.62f;
            float cComp = rest - cTitle;

            var hdr = new RectangleF(card.X + 20, card.Y + 8, card.Width - 40, HeadH);
            float hx = hdr.X;
            Cell(g, "时间", Fonts.Small, T.Text3, new RectangleF(hx, hdr.Y, cTime - 8, HeadH), StringAlignment.Near); hx += cTime;
            Cell(g, "平台", Fonts.Small, T.Text3, new RectangleF(hx, hdr.Y, cSite - 8, HeadH), StringAlignment.Near); hx += cSite;
            Cell(g, "分数", Fonts.Small, T.Text3, new RectangleF(hx, hdr.Y, cScore - 8, HeadH), StringAlignment.Near); hx += cScore;
            Cell(g, "标题", Fonts.Small, T.Text3, new RectangleF(hx, hdr.Y, cTitle - 10, HeadH), StringAlignment.Near); hx += cTitle;
            Cell(g, "公司", Fonts.Small, T.Text3, new RectangleF(hx, hdr.Y, cComp - 10, HeadH), StringAlignment.Near); hx += cComp;
            Cell(g, "原因", Fonts.Small, T.Text3, new RectangleF(hx, hdr.Y, cRea - 8, HeadH), StringAlignment.Near);

            var body = new RectangleF(card.X + 20, hdr.Bottom + 2, card.Width - 40, card.Bottom - hdr.Bottom - 14);
            g.SetClip(body);

            if (all.Count == 0)
            {
                T.TextC(g, "还没有被过滤的记录", Fonts.Small, T.Text3, new RectangleF(body.X, body.Y + Math.Min(60f, body.Height / 3f), body.Width, 24f));
                T.TextC(g, "等下一轮投递跑起来，规则拦下来的岗位会出现在这里", Fonts.Tiny, T.Text3, new RectangleF(body.X, body.Y + Math.Min(84f, body.Height / 3f) + 6f, body.Width, 20f));
                g.ResetClip();
                return;
            }

            float maxScroll = Math.Max(0, all.Count * RowH - body.Height);
            if (_scroll < 0) _scroll = 0;
            if (_scroll > maxScroll) _scroll = maxScroll;
            int first = (int)(_scroll / RowH);
            float y = body.Y - (_scroll % RowH);

            for (int i = first; i < all.Count; i++)
            {
                if (y > body.Bottom) break;
                var r = all[i];
                if (i == _hoverRow)
                {
                    T.FillRound(g, new RectangleF(body.X - 8, y, body.Width + 16, RowH - 2), 8f, T.HoverSoft);
                }
                float x = hdr.X;
                Cell(g, r.TimeText, Fonts.Tiny, T.Text3, new RectangleF(x, y, cTime - 8, RowH), StringAlignment.Near); x += cTime;
                Cell(g, SiteName(r), Fonts.Small, T.Text2, new RectangleF(x, y, cSite - 8, RowH), StringAlignment.Near); x += cSite;
                Cell(g, r.Score.ToString(), Fonts.Num, r.Score >= 40 ? T.Warn : T.Text3, new RectangleF(x, y, cScore - 8, RowH), StringAlignment.Near); x += cScore;
                Cell(g, r.Title.Length > 0 ? r.Title : "—", Fonts.Small, T.Text, new RectangleF(x, y, cTitle - 10, RowH), StringAlignment.Near); x += cTitle;
                Cell(g, r.Company.Length > 0 ? r.Company : (r.Loc.Length > 0 ? r.Loc : "—"), Fonts.Small, T.Text2, new RectangleF(x, y, cComp - 10, RowH), StringAlignment.Near); x += cComp;
                Cell(g, r.Reason, Fonts.Tiny, T.Text3, new RectangleF(x, y, cRea - 8, RowH), StringAlignment.Near);
                y += RowH;
            }
            g.ResetClip();

            // 滚动条
            if (maxScroll > 0)
            {
                var track = new RectangleF(card.Right - 12, body.Y, 4, body.Height);
                T.FillRound(g, track, 2f, T.LineSoft);
                float th = Math.Max(24f, body.Height * body.Height / (all.Count * RowH));
                float tp = body.Y + (body.Height - th) * (_scroll / maxScroll);
                T.FillRound(g, new RectangleF(track.X, tp, 4, th), 2f, T.LineHi);
            }
        }

        private static void Cell(Graphics g, string text, Font f, Color c, RectangleF r, StringAlignment a)
        {
            var sf = new StringFormat { Alignment = a, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (var b = new SolidBrush(c)) g.DrawString(text ?? "", f, b, r, sf);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll -= e.Delta / 120f * RowH * 3f;
            if (_scroll < 0) _scroll = 0;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var lp = T.LP(e.Location);
            float ty = 118;
            var bodyTop = ty + 8 + HeadH + 2;
            int row = (int)((lp.Y - bodyTop + _scroll) / RowH);
            if (lp.Y < bodyTop || row < 0) row = -1;
            if (row != _hoverRow) { _hoverRow = row; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverRow != -1) { _hoverRow = -1; Invalidate(); }
        }
    }
}
