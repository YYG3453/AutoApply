using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 投递明细：把四个 state-*.jsonl 里每一条投递摊开给人看——哪天投的、投给谁、什么岗位、多少钱。
    /// 这是"可解释"的第一步：能查到每一条，才谈得上复盘为什么投/为什么没投。
    /// </summary>
    internal class RecordsPage : Page
    {
        private readonly List<JobRec> _view = new List<JobRec>();

        private int _range = 3;          // 0 今天 / 1 近 7 天 / 2 近 30 天 / 3 全部
        private string _site = "";       // "" 全部
        private int _sortCol = 0;        // 0 时间 1 平台 2 城市 3 公司 4 岗位
        private bool _sortAsc;
        private float _scroll;
        private int _builtCount = -1;
        private int _hoverRow = -1;
        private string _day = "";        // yyyy-MM-dd：从仪表盘点某一天下来时只看这天
        private string _city = "";       // 从城市环形下来的

        private const float RowH = 30f;
        private const float HeadH = 30f;

        public RecordsPage(MainForm app) : base(app)
        {
            BackColor = T.Bg;
        }

        // ---------------- 数据 ----------------
        private static string SiteNameOf(string key)
        {
            if (key == "boss") return "BOSS直聘";
            if (key == "zp") return "智联招聘";
            if (key == "yjs") return "应届生";
            if (key == "q51") return "前程无忧";
            return key;
        }

        private void Rebuild()
        {
            _view.Clear();
            var all = App.Records == null ? null : App.Records.All;
            if (all == null) return;

            DateTime from = DateTime.MinValue;
            if (_range == 0) from = DateTime.Today;
            else if (_range == 1) from = DateTime.Today.AddDays(-6);
            else if (_range == 2) from = DateTime.Today.AddDays(-29);

            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (_site.Length > 0 && r.SiteKey != _site) continue;
                if (_day.Length > 0 && (!r.HasTs || r.Ts.ToString("yyyy-MM-dd") != _day)) continue;
                if (_city.Length > 0 && r.City != _city) continue;
                if (from > DateTime.MinValue)
                {
                    if (!r.HasTs || r.Ts < from) continue;
                }
                _view.Add(r);
            }

            _view.Sort(delegate (JobRec a, JobRec b)
            {
                int c;
                switch (_sortCol)
                {
                    case 1: c = string.CompareOrdinal(a.SiteName, b.SiteName); break;
                    case 2: c = string.CompareOrdinal(a.City, b.City); break;
                    case 3: c = string.CompareOrdinal(a.Company, b.Company); break;
                    case 4: c = string.CompareOrdinal(a.Title, b.Title); break;
                    default: c = a.Ts.CompareTo(b.Ts); break;
                }
                if (c == 0) c = b.Ts.CompareTo(a.Ts);
                return _sortAsc ? c : -c;
            });

            _scroll = 0f;
            _hoverRow = -1;
        }

        private void EnsureBuilt()
        {
            int n = App.Records == null ? 0 : App.Records.All.Count;
            if (n == _builtCount) return;
            _builtCount = n;
            Rebuild();
        }

        private void ResetAndRefresh()
        {
            _builtCount = App.Records == null ? 0 : App.Records.All.Count;
            Rebuild();
            Invalidate();
        }

        /// <summary>从别的页面下钻：只筛这一天 / 这个平台 / 这个城市。传 null 表示这一项不动，传 "" 表示清掉。</summary>
        public void SetFilter(string day, string site, string city)
        {
            if (day != null) { _day = day; if (day.Length > 0) _range = 3; }
            if (site != null) _site = site;
            if (city != null) _city = city;
            ResetAndRefresh();
        }

        // ---------------- 绘制 ----------------
        private static void Cell(Graphics g, string text, Font f, Color c, RectangleF rc, StringAlignment align)
        {
            if (text == null) text = "";
            using (var fmt = new StringFormat())
            using (var br = new SolidBrush(c))
            {
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.FormatFlags = StringFormatFlags.NoWrap;
                fmt.Alignment = align;
                fmt.LineAlignment = StringAlignment.Center;
                g.DrawString(text, f, br, rc, fmt);
            }
        }

        private static Color SiteColor(string key)
        {
            switch (key)
            {
                case "zp": return T.Blue;
                case "yjs": return T.Gold;
                case "q51": return T.Warn;
                default: return T.Jade;
            }
        }

        private void Chip(Graphics g, RectangleF r, string id, string text, bool on, Color accent)
        {
            bool hv = HoverCur.ContainsKey(id) && HoverCur[id] > 0.5f;
            var bg = on ? T.Alpha(accent, 40) : (hv ? T.HoverSoft : T.Field);
            T.FillRound(g, r, 9f, bg);
            if (on) T.StrokeRound(g, r, 9f, T.Alpha(accent, 150), 1f);
            Cell(g, text, Fonts.Small, on ? accent : T.Text2, r, StringAlignment.Center);
            AddHit(id, r);
        }

        private void Button(Graphics g, RectangleF r, string id, string text)
        {
            bool hv = HoverCur.ContainsKey(id) && HoverCur[id] > 0.5f;
            T.FillRound(g, r, 9f, hv ? T.BtnHover : T.Field);
            T.StrokeRound(g, r, 9f, T.Line, 1f);
            Cell(g, text, Fonts.Small, hv ? T.Text : T.Text2, r, StringAlignment.Center);
            AddHit(id, r);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            EnsureBuilt();
            ClearHits();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(T.S, T.S);                 // 与其他页面一致：按逻辑像素布局
            float W = Width / T.S, H = Height / T.S;

            var st = App.Records;
            T.Draw(g, "投递明细", Fonts.H1, T.Text, 32, 20);
            string sub = "共 " + (st == null ? 0 : st.Total) + " 条记录 · 当前筛选 " + _view.Count + " 条";
            if (st != null && st.BadLines > 0) sub += " · 跳过无法解析的行 " + st.BadLines;
            T.Draw(g, sub, Fonts.Small, T.Text2, 32, 55);

            // 下钻进来的筛选单独标出来，点 ✕ 就撤掉——不然用户不知道自己在看"被筛过"的列表
            float fx = 32 + g.MeasureString(sub, Fonts.Small).Width + 16f;
            if (_day.Length > 0)
            {
                Chip(g, new RectangleF(fx, 53, 136, 21), "clr:day", "日期 " + _day.Substring(5) + "  ✕", true, T.Blue);
                fx += 142;
            }
            if (_city.Length > 0)
            {
                Chip(g, new RectangleF(fx, 53, 112, 21), "clr:city", "城市 " + _city + "  ✕", true, T.Gold);
                fx += 118;
            }

            // 工具栏
            float ty = 96f;
            float x = 32f;
            var ranges = new[] { "今天", "近 7 天", "近 30 天", "全部" };
            for (int i = 0; i < ranges.Length; i++)
            {
                var r = new RectangleF(x, ty, 78f, 30f);
                Chip(g, r, "range:" + i, ranges[i], _range == i && _day.Length == 0, T.Jade);
                x += 84f;
            }
            x += 14f;
            T.Draw(g, "|", Fonts.Small, T.LineHi, x - 4, ty + 8);
            x += 12f;
            var sites = new[] { "", "boss", "zp", "yjs", "q51" };
            var names = new[] { "全部站", "BOSS", "智联", "应届生", "前程" };
            for (int i = 0; i < sites.Length; i++)
            {
                var r = new RectangleF(x, ty, 72f, 30f);
                var acc = sites[i].Length == 0 ? T.Text2 : SiteColor(sites[i]);
                Chip(g, r, "site:" + sites[i], names[i], _site == sites[i], acc);
                x += 78f;
            }

            var expR = new RectangleF(W - 32f - 96f, ty, 96f, 30f);
            Button(g, expR, "export", "导出 CSV");

            // 表格卡片
            var card = new RectangleF(32f, ty + 44f, W - 64f, H - (ty + 44f) - 22f);
            T.Card(g, card);

            float padL = 18f;
            float innerW = card.Width - padL * 2 - 10f;
            float cTime = 92f, cSite = 92f, cCity = 76f, cSal = 96f;
            float rest = Math.Max(160f, innerW - cTime - cSite - cCity - cSal);
            float cComp = rest * 0.46f, cTitle = rest - cComp;

            var cols = new[] { "时间", "平台", "城市", "公司", "岗位", "薪资" };
            var widths = new[] { cTime, cSite, cCity, cComp, cTitle, cSal };
            var aligns = new[] { StringAlignment.Near, StringAlignment.Near, StringAlignment.Near, StringAlignment.Near, StringAlignment.Near, StringAlignment.Far };

            float hx = card.X + padL;
            float hy = card.Y + 8f;
            for (int i = 0; i < cols.Length; i++)
            {
                var hrc = new RectangleF(hx, hy, widths[i] - 8f, HeadH);
                Cell(g, cols[i] + (_sortCol == i ? (_sortAsc ? " ▲" : " ▼") : ""), Fonts.Small, _sortCol == i ? T.Text : T.Text3, hrc, aligns[i]);
                AddHit("sort:" + i, new RectangleF(hx, hy, widths[i] - 4f, HeadH));
                hx += widths[i];
            }
            T.Hairline(g, card.X + padL, card.Right - padL - 6f, card.Y + 8f + HeadH);

            // 表体
            float listTop = card.Y + 8f + HeadH + 4f;
            float listH = card.Bottom - 8f - listTop;
            var body = new RectangleF(card.X + padL, listTop, innerW, listH);

            if (_view.Count == 0)
            {
                string msg = _day.Length > 0 ? "这一天没有投递记录"
                           : ((_range == 0) ? "今天还没有投递记录" : "这个筛选条件下没有记录");
                T.TextC(g, msg, Fonts.Small, T.Text3, new RectangleF(body.X, body.Y + Math.Min(60f, listH / 3f), body.Width, 24f));
                T.TextC(g, "换个时间范围或平台再看", Fonts.Tiny, T.Text3, new RectangleF(body.X, body.Y + Math.Min(84f, listH / 3f) + 6f, body.Width, 20f));
            }
            else
            {
                int total = _view.Count;
                float maxScroll = Math.Max(0f, total * RowH - listH);
                if (_scroll > maxScroll) _scroll = maxScroll;
                if (_scroll < 0f) _scroll = 0f;

                int first = (int)(_scroll / RowH);
                int last = Math.Min(total - 1, first + (int)(listH / RowH) + 1);

                var clip = g.Save();
                g.SetClip(body);
                float y = body.Y - (_scroll - first * RowH);
                for (int i = first; i <= last; i++, y += RowH)
                {
                    var rec = _view[i];
                    var rowRc = new RectangleF(body.X, y, body.Width, RowH);
                    if (i == _hoverRow) T.FillRound(g, rowRc, 7f, T.HoverSoft);

                    float cx = body.X + 2f;
                    Cell(g, rec.TimeText, Fonts.Tiny, T.Text3, new RectangleF(cx, y, cTime - 8f, RowH), StringAlignment.Near); cx += cTime;
                    T.Dot(g, cx + 4f, y + RowH / 2f, 3f, SiteColor(rec.SiteKey));
                    Cell(g, rec.SiteName, Fonts.Small, T.Text2, new RectangleF(cx + 12f, y, cSite - 14f, RowH), StringAlignment.Near); cx += cSite;
                    Cell(g, rec.City.Length > 0 ? rec.City : "—", Fonts.Small, T.Text2, new RectangleF(cx, y, cCity - 8f, RowH), StringAlignment.Near); cx += cCity;
                    Cell(g, rec.Company.Length > 0 ? rec.Company : "—", Fonts.Small, T.Text, new RectangleF(cx, y, cComp - 10f, RowH), StringAlignment.Near); cx += cComp;
                    Cell(g, rec.Title, Fonts.Small, T.Text2, new RectangleF(cx, y, cTitle - 10f, RowH), StringAlignment.Near); cx += cTitle;
                    Cell(g, rec.Salary.Length > 0 ? rec.Salary : "—", Fonts.Tiny, T.Text2, new RectangleF(cx, y, cSal - 6f, RowH), StringAlignment.Far);
                }
                g.Restore(clip);

                // 滚动条
                if (maxScroll > 0.5f)
                {
                    float trackH = listH;
                    float thumbH = Math.Max(24f, trackH * listH / (total * RowH));
                    float thumbY = body.Y + (trackH - thumbH) * (_scroll / maxScroll);
                    T.FillRound(g, new RectangleF(card.Right - 12f, body.Y, 4f, trackH), 2f, T.LineSoft);
                    T.FillRound(g, new RectangleF(card.Right - 12f, thumbY, 4f, thumbH), 2f, T.LineHi);
                }
            }
        }

        // ---------------- 交互 ----------------
        protected override void OnHit(string id)
        {
            if (id.StartsWith("range:"))
            {
                _range = int.Parse(id.Substring(6));
                _day = "";
                ResetAndRefresh();
                return;
            }
            if (id == "clr:day") { _day = ""; ResetAndRefresh(); return; }
            if (id == "clr:city") { _city = ""; ResetAndRefresh(); return; }
            if (id.StartsWith("site:"))
            {
                _site = id.Substring(5);
                ResetAndRefresh();
                return;
            }
            if (id.StartsWith("sort:"))
            {
                int c = int.Parse(id.Substring(5));
                if (_sortCol == c) _sortAsc = !_sortAsc;
                else { _sortCol = c; _sortAsc = (c != 0); }
                ResetAndRefresh();
                return;
            }
            if (id == "export") { ExportCsv(); return; }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float listH = Height / T.S - 190f;
            float maxScroll = Math.Max(0f, _view.Count * RowH - listH);
            _scroll -= e.Delta / 120f * RowH * 3f;
            if (_scroll < 0f) _scroll = 0f;
            if (_scroll > maxScroll) _scroll = maxScroll;
            _hoverRow = -1;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = RowAt(e.Location);
            if (row != _hoverRow) { _hoverRow = row; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverRow >= 0) { _hoverRow = -1; Invalidate(); }
        }

        private int RowAt(Point p)
        {
            var lp = T.LP(p);
            float W = Width / T.S, H = Height / T.S;
            var card = new RectangleF(32f, 96f + 44f, W - 64f, H - (96f + 44f) - 22f);
            float listTop = card.Y + 8f + HeadH + 4f;
            float listH = card.Bottom - 8f - listTop;
            if (lp.Y < listTop || lp.Y > listTop + listH) return -1;
            if (lp.X < card.X + 18f || lp.X > card.Right - 18f) return -1;
            int idx = (int)((lp.Y - listTop + _scroll) / RowH);
            return (idx >= 0 && idx < _view.Count) ? idx : -1;
        }

        // ---------------- 导出 ----------------
        private void ExportCsv()
        {
            try
            {
                var dir = App.Engine.ScriptDir;
                var path = Path.Combine(dir, "records-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("时间,平台,城市,公司,岗位,薪资,关键词,岗位ID");
                for (int i = 0; i < _view.Count; i++)
                {
                    var r = _view[i];
                    sb.AppendLine(string.Join(",", new[]
                    {
                        r.HasTs ? r.Ts.ToString("yyyy-MM-dd HH:mm") : "",
                        Csv(r.SiteName), Csv(r.City), Csv(r.Company), Csv(r.Title), Csv(r.Salary), Csv(r.Kw), Csv(r.Key)
                    }));
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                App.Toast("已导出 " + _view.Count + " 条 → " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                App.Toast("导出失败：" + ex.Message);
            }
        }

        private static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
