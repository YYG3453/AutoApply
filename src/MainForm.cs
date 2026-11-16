using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoApply
{
    internal class MainForm : Form
    {
        public const string Version = "1.0.4";
        public readonly string BaseDir;
        public AppCfg Cfg;
        public Engine Engine;
        public Stats Stats;
        public event Action CfgChanged;

        private NavRail _rail;
        private readonly Dictionary<string, Page> _pages = new Dictionary<string, Page>();
        private DashboardPage _dash;
        private PlanPage _plan;
        private SitesPage _sites;
        private LogPage _log;
        private AboutPage _about;
        private WinButtons _winBtns;
        private string _page = "dash";

        private readonly Timer _sched = new Timer { Interval = 15000 };
        private readonly HashSet<string> _ranWindows = new HashSet<string>();
        private string _toast = "";
        private readonly Timer _toastTimer = new Timer { Interval = 2600 };
        private readonly Timer _pulse = new Timer { Interval = 400 };
        private float _pulseT;
        private NotifyIcon _tray;
        private bool _ready;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        public MainForm()
        {
            BaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            Cfg = AppCfg.Load(Path.Combine(BaseDir, "config.json"));
            Engine = new Engine(BaseDir) { Cfg = Cfg };
            Stats = Stats.Load(Engine.ScriptDir);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = T.Bg;
            Text = "AutoApply · 四平台自动投递";
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            ClientSize = new Size(T.RS(1184), T.RS(776));
            MinimumSize = ClientSize;
            MaximumSize = ClientSize;

            _rail = new NavRail();
            _rail.Picked += ShowPage;
            Controls.Add(_rail);

            _dash = new DashboardPage(this);
            _plan = new PlanPage(this);
            _sites = new SitesPage(this);
            _log = new LogPage(this);
            _about = new AboutPage(this);
            _pages["dash"] = _dash; _pages["plan"] = _plan; _pages["sites"] = _sites; _pages["log"] = _log; _pages["about"] = _about;
            foreach (var p in _pages.Values) Controls.Add(p);

            _winBtns = new WinButtons(this);
            Controls.Add(_winBtns);
            _winBtns.BringToFront();

            Engine.Log += (lv, tx) => Ui(() => { _log.View.Add(lv, tx); if (lv == "ok" || lv == "warn" || lv == "err") _dash.Invalidate(); });
            Engine.StateChanged += () => Ui(() =>
            {
                Stats = Stats.Load(Engine.ScriptDir);
                _dash.Invalidate();
                _plan.Invalidate();
                UpdateTray();
                if (Engine.Running && !_pulse.Enabled) _pulse.Start();
                if (!Engine.Running) { _pulse.Stop(); _pulseT = 0; }
            });
            Engine.SiteSent += (k, n) => Ui(() => _dash.Invalidate());

            _sched.Tick += (s, e) => Ui(TickSchedule);
            _sched.Start();
            _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); _toast = ""; Invalidate(new Rectangle(0, Height - T.RS(28), Width, T.RS(28))); };
            _pulse.Tick += (s, e) => { _pulseT += 0.16f; if (!Engine.Running) { _pulse.Stop(); } _dash.Invalidate(new Rectangle(T.RS(32), T.RS(200), T.RS(700), T.RS(60))); };

            BuildTray();
            ShowPage("dash");
            _ready = true;
            LayoutAll();

            var argv = Environment.GetCommandLineArgs();
            var joined = string.Join(" ", argv, 1, Math.Max(0, argv.Length - 1));
            var parts = joined.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i++)
                if (parts[i] == "--page" && _pages.ContainsKey(parts[i + 1])) ShowPage(parts[i + 1]);
            Program.Trace("argv=[" + joined + "] page=" + _page);

            var ws = new Timer { Interval = 900 };
            ws.Tick += (s, e) => { ws.Stop(); Stats = Stats.Load(Engine.ScriptDir); _dash.Invalidate(); };
            ws.Start();
        }

        private void Ui(Action a)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(a); else a();
            }
            catch { }
        }

        public void Toast(string msg)
        {
            _toast = msg ?? "";
            _toastTimer.Stop(); _toastTimer.Start();
            Invalidate(new Rectangle(0, Height - T.RS(28), Width, T.RS(28)));
        }

        public void SaveCfg()
        {
            Cfg.Save(Path.Combine(BaseDir, "config.json"));
            var h = CfgChanged; if (h != null) h();
        }

        public int EnabledSiteCount()
        {
            int n = 0;
            foreach (var s in Cfg.Sites) if (s.Enabled) n++;
            return n;
        }

        public string NextRunText()
        {
            if (!Cfg.ScheduleEnabled) return "—";
            var now = DateTime.Now; int m = now.Hour * 60 + now.Minute;
            WindowCfg best = null; int bestD = int.MaxValue;
            foreach (var w in Cfg.Windows)
            {
                if (!w.Enabled) continue;
                int d = w.StartMins - m;
                if (d < 0) d += 24 * 60;
                if (d < bestD) { bestD = d; best = w; }
            }
            if (best == null) return "—";
            return string.Format("{0:00}:{1:00}", best.StartHour, best.StartMin);
        }

        public string NextRunSub()
        {
            if (!Cfg.ScheduleEnabled) return "定时已关闭";
            var now = DateTime.Now; int m = now.Hour * 60 + now.Minute;
            foreach (var w in Cfg.Windows)
                if (w.Enabled && m >= w.StartMins && m < w.EndMins) return "本时段进行中";
            return "今天到点自动开始";
        }

        public SiteResult ResultOf(string key)
        {
            foreach (var r in Engine.Results) if (r.Key == key) return r;
            return null;
        }

        public void StartRunManual()
        {
            if (Engine.Running) { Toast("已经在跑了"); return; }
            var sites = new List<SiteCfg>();
            foreach (var s in Cfg.Sites) if (s.Enabled) sites.Add(s);
            if (sites.Count == 0) { Toast("四个平台都被停用了，先去「平台与关键词」打开一个"); return; }
            Stats = Stats.Load(Engine.ScriptDir);
            Engine.StartRun(sites, "手动运行");
            ShowPage("log");
            _log.View.Add("sys", "=== 手动开始一轮投递");
        }

        public void StopRun()
        {
            if (Engine.Running) { Engine.Stop(); Toast("已停止"); }
        }

        public void OpenReport()
        {
            var p = Path.Combine(Engine.ScriptDir, "report.md");
            if (File.Exists(p)) Process.Start(p);
            else Toast("还没有报告，先跑一轮");
        }

        private void TickSchedule()
        {
            if (!Cfg.ScheduleEnabled || Engine.Running) return;
            var now = DateTime.Now;
            int m = now.Hour * 60 + now.Minute;
            foreach (var w in Cfg.Windows)
            {
                if (!w.Enabled) continue;
                if (m < w.StartMins || m >= w.EndMins) continue;
                var key = now.ToString("yyyy-MM-dd") + ":" + w.Key;
                if (_ranWindows.Contains(key)) continue;
                _ranWindows.Add(key);
                var sites = new List<SiteCfg>();
                foreach (var s in Cfg.Sites) if (s.Enabled) sites.Add(s);
                if (sites.Count == 0) return;
                Stats = Stats.Load(Engine.ScriptDir);
                Engine.StartRun(sites, w.Name + " " + w.Range);
                ShowPage("log");
                _log.View.Add("sys", "=== " + w.Name + " 自动开始");
                Toast(w.Name + " 自动开始");
                return;
            }
        }

        public void GotoPage(string key) { ShowPage(key); }

        private void ShowPage(string key)
        {
            _page = key;
            _rail.Selected = key;
            _rail.Invalidate();
            foreach (var kv in _pages) kv.Value.Visible = kv.Key == key;
            if (key == "log") _log.View.BringToFront();
        }

        private void LayoutAll()
        {
            if (!_ready || _rail == null || _pages.Count == 0 || _winBtns == null) return;
            _rail.Bounds = new Rectangle(0, 0, T.RS(212), ClientSize.Height);
            int px = T.RS(212), py = 0, pw = ClientSize.Width - px, ph = ClientSize.Height - T.RS(28);
            foreach (var p in _pages.Values) p.Bounds = new Rectangle(px, py, pw, ph);
            _winBtns.Bounds = new Rectangle(ClientSize.Width - T.RS(112), 0, T.RS(112), T.RS(44));
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutAll(); }

        // ---------- 绘制 ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(T.S, T.S);
            float W = Width / T.S;
            float H = Height / T.S;

            using (var lg = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(2, W), Math.Max(2, H)),
                Color.FromArgb(0x0D, 0x10, 0x15), T.Bg, 60f))
                g.FillRectangle(lg, 0, 0, W, H);

            // 底部状态栏
            float fy = H - 28;
            using (var p = new Pen(T.LineSoft)) g.DrawLine(p, 214, fy, W, fy);
            string left = _toast.Length > 0 ? _toast : (Engine.Running ? "正在投递 · " + (Engine.CurrentSiteName ?? "") : "就绪 · " + Engine.TotalSentInRun() + " 个 / 本轮");
            T.Draw(g, left, T.F(10.5f), _toast.Length > 0 ? T.Jade : T.Text3, 228, fy + 7);
            if (Engine.Running)
            {
                float a = 0.35f + 0.65f * (float)((Math.Sin(_pulseT) + 1) / 2);
                T.Dot(g, W - 150, fy + 13, 3.5f, T.Alpha(T.Jade, (int)(a * 255)));
            }
            T.TextR(g, "v" + Version + "  ·  报告：scripts/report.md", T.F(10.5f), T.Text3, W - 24, fy + 7);
        }

        // ---------- 窗口拖动 ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var lp = T.LP(e.Location);
            if (lp.Y <= 44 && lp.X < Width / T.S - 112)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) StartRunManual();
            else if (e.KeyCode == Keys.Escape) StopRun();
        }

        // ---------- 托盘 ----------
        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "AutoApply · 四平台自动投递";
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开主界面", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("开始投递", null, (s, e) => { Show(); StartRunManual(); });
            menu.Items.Add("停止", null, (s, e) => StopRun());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => { _tray.Visible = false; Application.Exit(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
            _tray.Visible = true;
        }

        private void UpdateTray()
        {
            if (_tray == null) return;
            _tray.Text = Engine.Running ? ("AutoApply · 投递中（" + (Engine.CurrentSiteName ?? "") + "）") : "AutoApply · 待命";
        }

        private static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(64, 64))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var br = new LinearGradientBrush(new Rectangle(0, 0, 64, 64), Color.FromArgb(0x8A, 0xEC, 0xC7), Color.FromArgb(0x2F, 0x8E, 0x76), 45f))
                        g.FillEllipse(br, 4, 4, 56, 56);
                    using (var b2 = new SolidBrush(Color.FromArgb(0x0B, 0x0D, 0x11)))
                        g.FillEllipse(b2, 24, 24, 16, 16);
                }
                var h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (Cfg.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _tray.ShowBalloonTip(1200, "AutoApply", "还在后台运行，双击托盘图标可以叫回来", ToolTipIcon.Info);
                return;
            }
            _tray.Visible = false;
            base.OnFormClosing(e);
        }

        // ---------- 右上角窗口按钮 ----------
        private class WinButtons : Control
        {
            private readonly MainForm _f;
            private string _hover = "";
            public WinButtons(MainForm f)
            {
                _f = f;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }
            private RectangleF MinR { get { return new RectangleF(0, 0, Width / T.S / 2, Height / T.S); } }
            private RectangleF CloseR { get { return new RectangleF(Width / T.S / 2, 0, Width / T.S / 2, Height / T.S); } }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(T.S, T.S);
                if (_hover == "min")
                {
                    T.FillRound(g, new RectangleF(MinR.X + 6, MinR.Y + 6, MinR.Width - 12, MinR.Height - 12), 8f, Color.FromArgb(0x22, 0x28, 0x31));
                }
                if (_hover == "close")
                {
                    T.FillRound(g, new RectangleF(CloseR.X + 6, CloseR.Y + 6, CloseR.Width - 12, CloseR.Height - 12), 8f, T.Alpha(T.Danger, 60));
                }
                using (var p = new Pen(_hover == "min" ? T.Text : T.Text3, 1.2f))
                    g.DrawLine(p, MinR.X + MinR.Width / 2 - 6, MinR.Y + MinR.Height / 2 + 3, MinR.X + MinR.Width / 2 + 6, MinR.Y + MinR.Height / 2 + 3);
                using (var p = new Pen(_hover == "close" ? T.Danger : T.Text3, 1.2f))
                {
                    float cx = CloseR.X + CloseR.Width / 2, cy = CloseR.Y + CloseR.Height / 2 + 2;
                    g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5);
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                var lp = T.LP(e.Location);
                string h = MinR.Contains(lp) ? "min" : (CloseR.Contains(lp) ? "close" : "");
                if (h != _hover) { _hover = h; Invalidate(); }
                Cursor = h.Length > 0 ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = ""; Invalidate(); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                var lp = T.LP(e.Location);
                if (MinR.Contains(lp)) _f.WindowState = FormWindowState.Minimized;
                else if (CloseR.Contains(lp)) _f.Close();
            }
        }
    }
}
