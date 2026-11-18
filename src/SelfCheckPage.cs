using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 数据自检页：把"几个数字之间对不对得上"摊开给人看。
    /// 全绿就说明统计可信；有一条红就说明那个数字先别信。
    /// </summary>
    internal class SelfCheckPage : Page
    {
        private List<CheckItem> _items = new List<CheckItem>();
        private string _stamp = "";

        public SelfCheckPage(MainForm app) : base(app)
        {
            BackColor = T.Bg;
        }

        public override void RefreshTheme()
        {
            BackColor = T.Bg;
            Invalidate(true);
        }

        private void Recheck()
        {
            try
            {
                _items = SelfCheck.Run(App.Records, App.Cfg);
                _stamp = DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                _items = new List<CheckItem>();
                _stamp = "检查出错：" + ex.Message;
            }
            Invalidate();
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
            T.Draw(g, "数据自检", Fonts.H1, T.Text, 32, 24);
            T.Draw(g, "把几个互相独立的来源对一遍；有一项不通过，相关数字就先别当真", Fonts.Small, T.Text3, 32, 62);

            if (_items.Count == 0) Recheck();

            int bad = 0;
            for (int i = 0; i < _items.Count; i++) if (!_items[i].Ok) bad++;

            // 摘要
            var head = new RectangleF(32, 96, W - 64, 76);
            T.Card(g, head, 14f);
            T.Dot(g, head.X + 24, head.Y + 26, 5f, bad == 0 ? T.Jade : T.Danger);
            T.Draw(g, bad == 0 ? "全部通过" : (bad + " 项没通过"), Fonts.H2, bad == 0 ? T.Jade : T.Danger, head.X + 40, head.Y + 14);
            T.Draw(g, "共 " + _items.Count + " 项 · 最后一次检查 " + (_stamp.Length > 0 ? _stamp : "—"), Fonts.Small, T.Text2, head.X + 40, head.Y + 42);
            var btn = new RectangleF(head.Right - 286, head.Y + 22, 126, 32);
            T.FillRound(g, btn, 8f, HoverCur.ContainsKey("recheck") && HoverCur["recheck"] > 0.5f ? T.HoverSoftHi : T.HoverSoft);
            T.StrokeRound(g, btn, 8f, T.LineSoft);
            var sz = g.MeasureString("重新检查", Fonts.Small);
            T.Draw(g, "重新检查", Fonts.Small, T.Jade, btn.X + (btn.Width - sz.Width) / 2f, btn.Y + (btn.Height - sz.Height) / 2f);
            AddHit("recheck", btn);

            // 列表
            float y = head.Bottom + 16f;
            float rowH = Math.Min(68f, Math.Max(52f, (H - y - 30f) / Math.Max(1f, _items.Count)));
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                var row = new RectangleF(32, y, W - 64, rowH - 8f);
                T.Card(g, row, 12f);
                T.Dot(g, row.X + 24, row.Y + 22, 4.5f, it.Ok ? T.Jade : T.Danger);
                T.Draw(g, it.Title, Fonts.Body, it.Ok ? T.Text : T.Warn, row.X + 40, row.Y + 12);
                T.Draw(g, it.Detail, Fonts.Small, T.Text2, row.X + 40, row.Y + 36);
                y += rowH;
            }
        }

        protected override void OnHit(string id)
        {
            if (id == "recheck") { Recheck(); App.Toast("已重新检查"); }
        }
    }
}
