using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 首次运行向导：三步确认「浏览器 → 四站登录 → 城市关键词」，做完把 WizardDone 写进配置。
    /// 只是一个引导，不爽可以随时点「跳过」，功能和手动配置完全一样。
    /// </summary>
    internal class WizardForm : Form
    {
        private readonly AppCfg _cfg;
        private readonly string _scriptDir;
        private int _step;
        private readonly Dictionary<string, string> _health = new Dictionary<string, string>();
        private readonly List<string> _cities;
        private readonly List<string> _keywords;
        private string _note = "";
        private int _probing;

        public WizardForm(AppCfg cfg, string scriptDir)
        {
            _cfg = cfg;
            _scriptDir = scriptDir;
            _cities = new List<string>(cfg.Cities);
            _keywords = new List<string>(cfg.Keywords);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "AutoApply 首次向导";
            BackColor = T.Bg;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            ClientSize = new Size(T.RS(760), T.RS(520));
            KeyPreview = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Probe();
        }

        private void Ui(Action a) { try { if (IsHandleCreated) BeginInvoke(a); } catch { } }

        private void Probe()
        {
            if (Interlocked.CompareExchange(ref _probing, 1, 0) != 0) return;
            Health.Port = _cfg.Port;
            Health.ScriptDir = _scriptDir;
            var sites = _cfg.Sites;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    for (int i = 0; i < sites.Count; i++)
                    {
                        var s = sites[i];
                        var h = Health.Probe(s);
                        lock (_health) { _health[s.Key] = h.Note; }
                        Ui(delegate { Invalidate(); });
                    }
                }
                catch { }
                finally { Volatile.Write(ref _probing, 0); Ui(delegate { Invalidate(); }); }
            });
        }

        private string HealthOf(string key)
        {
            lock (_health) { string v; return _health.TryGetValue(key, out v) ? v : "检查中…"; }
        }

        // ---------- 绘制 ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(T.S, T.S);
            float W = ClientSize.Width / T.S, H = ClientSize.Height / T.S;
            g.Clear(T.Bg);

            // 卡片底
            var card = new RectangleF(0, 0, W, H);
            T.Card(g, card, 0f);

            // 顶部：步骤条
            T.Mark(g, new RectangleF(28, 26, 30, 30));
            T.Draw(g, "首次使用向导", Fonts.H1, T.Text, 68, 22);
            T.Draw(g, "三步确认，之后一切自动；看不懂的可以直接跳过，回头在「平台健康」里再看", Fonts.Small, T.Text3, 68, 52);

            string[] names = { "环境与登录", "目标城市", "搜索关键词" };
            for (int i = 0; i < names.Length; i++)
            {
                float x = 28 + i * 200f;
                float y = 92f;
                bool on = i == _step, done = i < _step;
                Color c = on ? T.Jade : (done ? T.JadeDim : T.Line2);
                T.FillRound(g, new RectangleF(x, y, 176, 34), 8f, on ? T.HoverSoft : T.Bg);
                T.Dot(g, x + 16, y + 17, 5f, c);
                T.Draw(g, (i + 1) + "  " + names[i], Fonts.Small, on ? T.Text : T.Text2, x + 30, y + 10);
            }

            float top = 146f;
            if (_step == 0) DrawEnv(g, W, top);
            else if (_step == 1) DrawCities(g, W, top);
            else DrawKeywords(g, W, top);

            // 底部按钮
            float by = H - 66f;
            Btn(g, new RectangleF(28, by, 96, 38), "跳过", false, delegate { Skip(); });
            if (_step > 0) Btn(g, new RectangleF(W - 300, by, 120, 38), "上一步", false, delegate { _step--; Invalidate(); });
            if (_step < 2) Btn(g, new RectangleF(W - 164, by, 136, 38), "下一步", true, delegate { _step++; Invalidate(); });
            else Btn(g, new RectangleF(W - 164, by, 136, 38), "完成", true, delegate { Finish(); });

            if (_note.Length > 0) T.Draw(g, _note, Fonts.Tiny, T.Text3, 140, by + 12);
        }

        private void DrawEnv(Graphics g, float W, float top)
        {
            var c = new RectangleF(28, top, W - 56, 240);
            T.Card(g, c, 16f);
            T.Draw(g, "1. 浏览器", Fonts.H2, T.Text, c.X + 24, c.Y + 18);

            string chrome = FindChrome();
            T.Draw(g, chrome.Length > 0 ? "· 找到 Chrome：" + chrome : "· 没找到 Chrome（点下面按钮装一个，或手动填路径）", Fonts.Small, chrome.Length > 0 ? T.Text2 : T.Warn, c.X + 24, c.Y + 52);
            bool alive = Health.ChromeAlive();
            T.Draw(g, alive ? "· 浏览器通道已连上（端口 " + _cfg.Port + "）" : "· 浏览器通道尚未启动，投递时会自动启动", Fonts.Small, alive ? T.Jade : T.Text2, c.X + 24, c.Y + 80);

            T.Draw(g, "2. 四个平台登录", Fonts.H2, T.Text, c.X + 24, c.Y + 116);
            float y = c.Y + 150;
            foreach (var s in _cfg.Sites)
            {
                string st = HealthOf(s.Key);
                Color col = st == "正常" ? T.Jade : (st == "检查中…" ? T.Text3 : T.Warn);
                T.Dot(g, c.X + 30, y + 8, 4.5f, col);
                T.Draw(g, s.Name, Fonts.Small, T.Text2, c.X + 44, y);
                T.Draw(g, st, Fonts.Small, col, c.X + 150, y);
                y += 24;
            }
            Btn(g, new RectangleF(c.Right - 190, c.Y + 44, 166, 34), "打开浏览器登录", false, delegate
            {
                try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(Application.ExecutablePath) + "\\node\\node.exe", "cdp.js newtab " + _cfg.Sites[0].Url) { WorkingDirectory = _scriptDir, UseShellExecute = false, CreateNoWindow = true }); } catch { }
                _note = "已尝试打开浏览器；登录一次后回来点「重新检查」";
                ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(6000); Probe(); });
            });
            Btn(g, new RectangleF(c.Right - 190, c.Y + 84, 166, 34), "重新检查", false, delegate { Probe(); });
        }

        private void DrawCities(Graphics g, float W, float top)
        {
            var c = new RectangleF(28, top, W - 56, 240);
            T.Card(g, c, 16f);
            T.Draw(g, "2. 想在哪些城市找工作", Fonts.H2, T.Text, c.X + 24, c.Y + 18);
            T.Draw(g, "点一下切换选中。一线 + 新一线共 19 城，选得越多投递越分散。", Fonts.Small, T.Text3, c.X + 24, c.Y + 48);
            float x = c.X + 24, y = c.Y + 80;
            foreach (var city in AppCfg.DefaultCities())
            {
                float w = 62f;
                if (x + w > c.Right - 24) { x = c.X + 24; y += 40; }
                bool on = _cities.Contains(city);
                T.FillRound(g, new RectangleF(x, y, w, 32), 8f, on ? T.HoverSoftHi : T.HoverSoft);
                T.StrokeRound(g, new RectangleF(x, y, w, 32), 8f, on ? T.JadeDim : T.LineSoft);
                T.TextC(g, city, Fonts.Small, on ? T.Text : T.Text3, new RectangleF(x, y, w, 32));
                string cap = city;
                BtnHit(new RectangleF(x, y, w, 32), delegate { if (_cities.Contains(cap)) _cities.Remove(cap); else _cities.Add(cap); Invalidate(); });
                x += w + 8;
            }
        }

        private void DrawKeywords(Graphics g, float W, float top)
        {
            var c = new RectangleF(28, top, W - 56, 240);
            T.Card(g, c, 16f);
            T.Draw(g, "3. 搜索哪些岗位关键词", Fonts.H2, T.Text, c.X + 24, c.Y + 18);
            T.Draw(g, "关键词会轮流搜；一个词搜完换下一个。后面在「平台与关键词」里随时能改。", Fonts.Small, T.Text3, c.X + 24, c.Y + 48);
            float y = c.Y + 84;
            foreach (var k in _keywords)
            {
                T.FillRound(g, new RectangleF(c.X + 24, y, 150, 30), 8f, T.HoverSoft);
                T.TextC(g, k, Fonts.Small, T.Text2, new RectangleF(c.X + 24, y, 150, 30));
                y += 36;
            }
            T.Draw(g, "共 " + _keywords.Count + " 个关键词 · 建议保留「实施工程师 / 交付实施 / 售后实施 / 技术支持」", Fonts.Tiny, T.Text3, c.X + 200, c.Y + 96);
            T.Draw(g, "点「完成」会写入 config.json 并开始使用；投递时间默认上午 09:30–10:30、下午 14:00–16:00。", Fonts.Tiny, T.Text3, c.X + 200, c.Y + 124);
        }

        // ---------- 小型控件 ----------
        private readonly List<KeyValuePair<RectangleF, Action>> _hits = new List<KeyValuePair<RectangleF, Action>>();

        private void BtnHit(RectangleF r, Action a) { _hits.Add(new KeyValuePair<RectangleF, Action>(r, a)); }

        private void Btn(Graphics g, RectangleF r, string text, bool primary, Action a)
        {
            T.FillRound(g, r, 9f, primary ? T.Jade : T.HoverSoft);
            T.StrokeRound(g, r, 9f, primary ? T.Jade : T.LineSoft);
            T.TextC(g, text, Fonts.Small, primary ? T.JadeInk : T.Text2, r);
            BtnHit(r, a);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var p = new PointF(e.X / T.S, e.Y / T.S);
            for (int i = _hits.Count - 1; i >= 0; i--)
                if (_hits[i].Key.Contains(p)) { _hits[i].Value(); return; }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_hits.Count == 0) return;
            Cursor = _hits.Exists(delegate (KeyValuePair<RectangleF, Action> kv) { return kv.Key.Contains(e.X / T.S, e.Y / T.S); }) ? Cursors.Hand : Cursors.Default;
        }


        private void ClearHits() { _hits.Clear(); }

        // 覆写绘制入口，保证先清命中区再画
        public new void Invalidate()
        {
            ClearHits();
            base.Invalidate();
        }

        private string FindChrome()
        {
            string[] cands =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Google\\Chrome\\Application\\chrome.exe",
                "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
                "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
                "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
                "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
            };
            foreach (var p in cands) if (File.Exists(p)) return p;
            return "";
        }

        private void Skip()
        {
            _cfg.WizardDone = true;
            _cfg.Save(Paths.CfgPath);
            Close();
        }

        private void Finish()
        {
            if (_cities.Count > 0) _cfg.Cities = new List<string>(_cities);
            _cfg.WizardDone = true;
            _cfg.Save(Paths.CfgPath);
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Skip();
        }

    }
}
