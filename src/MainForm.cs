using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace AutoApply
{
    internal class MainForm : Form
    {
        public const string Version = "1.7.0";

        /// <summary>--tray：启动后只呆在托盘里，不弹窗口（给计划任务用）。</summary>
        public bool StartInTray;

        /// <summary>--run-now：启动后立刻跑一轮（自测/命令行触发用）。</summary>
        public bool StartRunNow;
        public bool SuppressWizard;
        public readonly string BaseDir;
        public AppCfg Cfg;
        public Engine Engine;
        public RecordStore Records;
        /// <summary>匹配规则（界面上的阈值/词表），页面只读它做展示。</summary>
        public RulesCfg Rules;
        /// <summary>已注册的页面 key（顺序沿用导航注册表），给 --render 之类工具用。</summary>
        public System.Collections.Generic.List<string> Pages
        {
            get
            {
                var k = new System.Collections.Generic.List<string>();
                foreach (var it in Nav.Items) if (_pages.ContainsKey(it.Key)) k.Add(it.Key);
                return k;
            }
        }
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
            Records = RecordStore.Load(Engine.ScriptDir);

            // 主题：--theme day|night 优先（渲染截图用），否则用配置里保存的
            var rawArgs = Environment.GetCommandLineArgs();
            string theme = string.IsNullOrEmpty(Cfg.Theme) ? "night" : Cfg.Theme;
            T.NightStyle = Cfg.NightStyle;
            for (int i = 0; i + 1 < rawArgs.Length; i++)
            {
                if (rawArgs[i] == "--theme" && (rawArgs[i + 1] == "day" || rawArgs[i + 1] == "night")) theme = rawArgs[i + 1];
                if (rawArgs[i] == "--nightstyle")
                {
                    int ns;
                    if (int.TryParse(rawArgs[i + 1], out ns)) T.NightStyle = ns < 0 ? 0 : (ns > 4 ? 4 : ns);
                }
            }
            T.SetNight(theme != "day");
            Program.Trace("theme resolved = '" + theme + "' cfg='" + Cfg.Theme + "' night=" + T.Night + " bg=" + T.Bg.ToArgb().ToString("X8"));

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Shown += (s, e) =>
            {
                if (StartRunNow)
                    BeginInvoke(new Action(delegate { try { StartRunManual(); } catch (Exception ex) { Program.Trace("run-now delegate throw: " + ex.Message); } }));
                if (!StartInTray) return;
                WindowState = FormWindowState.Minimized;
                Hide();
            };
            BackColor = T.Bg;
            Text = "AutoApply · 四平台自动投递";
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            // 窗口/任务栏图标用 exe 里内嵌的 app.ico（没有的话退回默认）
            try
            {
                var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) Icon = exeIcon;
            }
            catch { }
            // 默认 1184x776 逻辑像素；小屏（如 1366x768 @125%）按设备像素收进工作区
            int dw = T.RS(1184), dh = T.RS(776);
            try
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                int aw = wa.Width - 16, ah = wa.Height - 16;
                if (dw > aw) dw = aw;
                if (dh > ah) dh = ah;
                if (dw < T.RS(900)) dw = T.RS(900);
                if (dh < T.RS(520)) dh = T.RS(520);
            }
            catch { }
            ClientSize = new Size(dw, dh);
            MinimumSize = ClientSize;
            MaximumSize = ClientSize;

            _rail = new NavRail();
            _rail.Picked += ShowPage;
            Controls.Add(_rail);

            // 页面全部由 Nav 注册表创建：加页面只改 Nav.cs
            foreach (var item in Nav.Items) _pages[item.Key] = item.Create(this);
            foreach (var p in _pages.Values) Controls.Add(p);
            _dash = (DashboardPage)_pages["dash"];
            _plan = (PlanPage)_pages["plan"];
            _sites = (SitesPage)_pages["sites"];
            _log = (LogPage)_pages["log"];
            _about = (AboutPage)_pages["about"];

            _winBtns = new WinButtons(this);
            Controls.Add(_winBtns);
            _winBtns.BringToFront();

            Engine.Log += (lv, tx) => Ui(() => { _log.View.Add(lv, tx); if (lv == "ok" || lv == "warn" || lv == "err") _dash.Invalidate(); });
            Engine.StateChanged += () => Ui(() =>
            {
                Records = RecordStore.Load(Engine.ScriptDir);
                _dash.Invalidate();
                _plan.Invalidate();
                UpdateTray();
                if (Engine.Running && !_pulse.Enabled) _pulse.Start();
                if (!Engine.Running) { _pulse.Stop(); _pulseT = 0; }
            });
            Engine.SiteSent += (k, n) => Ui(() => _dash.Invalidate());
            Engine.SiteAttention += (site, reason) => Ui(() =>
            {
                Notify(site + " 需要你动手一下", reason);
                _dash.Invalidate();
            });

            _sched.Tick += (s, e) => Ui(TickSchedule);
            _sched.Start();
            _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); _toast = ""; Invalidate(new Rectangle(0, Height - T.RS(28), Width, T.RS(28))); };
            _pulse.Tick += (s, e) => { _pulseT += 0.16f; if (!Engine.Running) { _pulse.Stop(); } _dash.Invalidate(new Rectangle(T.RS(32), T.RS(200), T.RS(700), T.RS(60))); };

            BuildTray();
            ShowPage(Nav.DefaultKey);
            _ready = true;
            LayoutAll();

            var argv = Environment.GetCommandLineArgs();
            var joined = string.Join(" ", argv, 1, Math.Max(0, argv.Length - 1));
            var parts = joined.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "--page" && i + 1 < parts.Length && _pages.ContainsKey(parts[i + 1])) ShowPage(parts[i + 1]);
                if (parts[i] == "--tray") StartInTray = true;
                if (parts[i] == "--run-now") StartRunNow = true;
            }
            Program.Trace("argv=[" + joined + "] page=" + _page);

            var ws = new Timer { Interval = 900 };
            ws.Tick += (s, e) => { ws.Stop(); Records = RecordStore.Load(Engine.ScriptDir); Rules = RulesCfg.Load(Engine.ScriptDir); _dash.Invalidate(); };
            ws.Start();

            // 启动后台收编旧流程（禁用旧计划任务 / 合并历史记录 / 迁移登录态）
            var legacy = new Timer { Interval = 1200 };
            legacy.Tick += (s, e) => { legacy.Stop(); legacy.Dispose(); StartLegacyTakeover(); };
            legacy.Start();

            // 首次运行向导：等窗体真正可见后再弹（在 Shown 里开模态窗容易卡住）
            var wizt = new Timer { Interval = 900 };
            wizt.Tick += (s, e) =>
            {
                wizt.Stop();
                wizt.Dispose();
                if (SuppressWizard || StartInTray || Cfg.WizardDone) return;
                try
                {
                    using (var wf = new WizardForm(Cfg, Engine.ScriptDir)) wf.ShowDialog(this);
                    Cfg = AppCfg.Load(Path.Combine(BaseDir, "config.json"));
                    _dash.Invalidate();
                }
                catch (Exception ex) { Program.Trace("wizard failed: " + ex.Message); }
            };
            wizt.Start();
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

        /// <summary>投递进行中不许改配置（改了一半的时长/城市会让本轮语义混乱）。</summary>
        public bool CanEdit { get { return Engine == null || !Engine.Running; } }

        /// <summary>需要人处理的事情：底部提示 + 托盘气泡（窗口在托盘时也能看见）。</summary>
        public void Notify(string title, string text)
        {
            Toast(text);
            Program.Trace("notify: " + title + " / " + text);
            try
            {
                if (Cfg.Notify && _tray != null)
                    _tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Warning);
            }
            catch { }
        }

        public void RefreshStats()
        {
            Ui(() =>
            {
                Records = RecordStore.Load(Engine.ScriptDir);
                _dash.Invalidate();
                _plan.Invalidate();
            });
        }

        private void StartLegacyTakeover()
        {
            var t = new Thread(() =>
            {
                try
                {
                    Legacy.Takeover(this,
                        (lv, tx) => Ui(() => { _log.View.Add(lv, tx); }),
                        (title, text) => Ui(() => Notify(title, text)));
                    RefreshStats();
                }
                catch (Exception ex) { Program.Trace("legacy takeover failed: " + ex); }
            });
            t.IsBackground = true;
            t.Start();
        }

        public void SaveCfg()
        {
            Cfg.Save(Path.Combine(BaseDir, "config.json"));
            var h = CfgChanged; if (h != null) h();
        }

        /// <summary>应用主题（night / day）：换调色板 + 刷新所有子控件。</summary>
        public void ApplyTheme(string mode, bool persist)
        {
            var st = new System.Diagnostics.StackTrace(1, false);
            Program.Trace("ApplyTheme(" + mode + ", persist=" + persist + ") from " +
                          (st.FrameCount > 0 ? st.GetFrame(0).GetMethod().DeclaringType.Name + "." + st.GetFrame(0).GetMethod().Name : "?"));
            T.SetNight(mode != "day");
            BackColor = T.Bg;
            ApplyThemeTo(this);
            Invalidate(true);
            if (_tray != null) _tray.Icon = MakeIcon();
            if (persist)
            {
                Cfg.Theme = T.Night ? "night" : "day";
                Cfg.NightStyle = T.NightStyle;
                SaveCfg();
            }
        }

        public void ToggleTheme()
        {
            ApplyTheme(T.Night ? "day" : "night", true);
            Toast(T.Night ? "已切换到夜晚模式" : "已切换到日间模式");
        }

        private static void ApplyThemeTo(Control c)
        {
            if (c is Page) ((Page)c).RefreshTheme();
            else if (c is NavRail) ((NavRail)c).RefreshTheme();
            else if (c is LogView) ((LogView)c).RefreshTheme();
            else if (c is TimeField) ((TimeField)c).RefreshTheme();
            else if (c is DarkText) ((DarkText)c).RefreshTheme();
            foreach (Control ch in c.Controls) ApplyThemeTo(ch);
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
            Program.Trace("StartRunManual enter running=" + (Engine == null ? "null-engine" : Engine.Running.ToString()) + " sites=" + (Cfg == null ? -1 : Cfg.Sites.Count) + " enabled=" + (Cfg == null ? -1 : Cfg.Sites.FindAll(delegate (SiteCfg s) { return s.Enabled; }).Count));
            if (Engine.Running) { Toast("已经在跑了"); return; }
            var sites = new List<SiteCfg>();
            foreach (var s in Cfg.Sites) if (s.Enabled) sites.Add(s);
            if (sites.Count == 0) { Toast("四个平台都被停用了，先去「平台与关键词」打开一个"); return; }
            Records = RecordStore.Load(Engine.ScriptDir);
            try
            {
                Engine.StartRun(sites, "手动运行");
                Program.Trace("StartRunManual started sites=" + sites.Count + " first=" + sites[0].Key);
            }
            catch (Exception ex) { Program.Trace("StartRunManual throw: " + ex.GetType().Name + ": " + ex.Message); return; }
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
                Records = RecordStore.Load(Engine.ScriptDir);
                Engine.StartRun(sites, w.Name + " " + w.Range);
                ShowPage("log");
                _log.View.Add("sys", "=== " + w.Name + " 自动开始");
                Toast(w.Name + " 自动开始");
                return;
            }
        }

        public void GotoPage(string key) { ShowPage(key); }

        internal void ShowPage(string key)
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
            _winBtns.Bounds = new Rectangle(ClientSize.Width - T.RS(WinButtons.WidthLogical), 0, T.RS(WinButtons.WidthLogical), T.RS(44));
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
                T.BgTop, T.Bg, 60f))
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
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCAPTION = 2;

        /// <summary>无边框窗口：顶部 44px 与四周 6px 边缘都当作标题栏，按住即可拖动。</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                var cp = PointToClient(Page.ScreenPoint(m.LParam));
                int W = ClientSize.Width, H = ClientSize.Height;
                int edge = T.RS(UiDrag.Edge);
                int strip = T.RS(UiDrag.StripHeight);
                int btns = T.RS(WinButtons.WidthLogical);
                bool overButtons = cp.X >= W - btns && cp.Y <= strip;
                if (!overButtons)
                {
                    bool inStrip = cp.Y <= strip;
                    bool onEdge = cp.X <= edge || cp.Y <= edge || cp.X >= W - edge || cp.Y >= H - edge;
                    if (inStrip || onEdge)
                    {
                        m.Result = (IntPtr)HTCAPTION;
                        return;
                    }
                }
            }
            base.WndProc(ref m);
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
            // 托盘图标优先用 exe 内嵌的 app.ico，保证和桌面/任务栏一致
            try
            {
                var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) return exeIcon;
            }
            catch { }
            using (var bmp = new Bitmap(64, 64))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var br = new LinearGradientBrush(new Rectangle(0, 0, 64, 64), T.JadeHi, T.JadeDeep, 45f))
                        g.FillEllipse(br, 4, 4, 56, 56);
                    using (var b2 = new SolidBrush(T.Bg))
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
            public const int WidthLogical = 148;
            private const float ZoneW = 48f;

            private readonly MainForm _f;
            private string _hover = "";
            public WinButtons(MainForm f)
            {
                _f = f;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            private RectangleF ThemeR { get { return new RectangleF(0, 0, ZoneW, Height / T.S); } }
            private RectangleF MinR { get { return new RectangleF(ZoneW, 0, ZoneW, Height / T.S); } }
            private RectangleF CloseR { get { return new RectangleF(ZoneW * 2, 0, Width / T.S - ZoneW * 2, Height / T.S); } }

            private void Pill(Graphics g, RectangleF r, Color c)
            {
                T.FillRound(g, new RectangleF(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12), 8f, c);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(T.S, T.S);

                if (_hover == "theme") Pill(g, ThemeR, T.BtnHover);
                if (_hover == "min") Pill(g, MinR, T.BtnHover);
                if (_hover == "close") Pill(g, CloseR, T.Alpha(T.Danger, 60));

                // 主题：夜 = 月亮，日 = 太阳
                float tx = ThemeR.X + ThemeR.Width / 2, ty = ThemeR.Y + ThemeR.Height / 2 + 1;
                var tcol = _hover == "theme" ? T.Text : T.Text3;
                if (T.Night)
                {
                    using (var path = new GraphicsPath())
                    using (var reg = new System.Drawing.Region())
                    {
                        path.AddEllipse(tx - 6f, ty - 6f, 12f, 12f);
                        reg.MakeEmpty();
                        reg.Union(path);
                        using (var cut = new GraphicsPath())
                        {
                            cut.AddEllipse(tx - 1.2f, ty - 8.2f, 12f, 12f);
                            reg.Exclude(cut);
                        }
                        using (var b = new SolidBrush(tcol)) g.FillRegion(b, reg);
                    }
                }
                else
                {
                    using (var b = new SolidBrush(tcol)) g.FillEllipse(b, tx - 3.6f, ty - 3.6f, 7.2f, 7.2f);
                    using (var p = new Pen(tcol, 1.2f))
                        for (int i = 0; i < 8; i++)
                        {
                            double a = Math.PI * i / 4.0;
                            float x1 = tx + (float)Math.Cos(a) * 6.2f, y1 = ty + (float)Math.Sin(a) * 6.2f;
                            float x2 = tx + (float)Math.Cos(a) * 8.6f, y2 = ty + (float)Math.Sin(a) * 8.6f;
                            g.DrawLine(p, x1, y1, x2, y2);
                        }
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

            private string Hit(PointF lp)
            {
                if (ThemeR.Contains(lp)) return "theme";
                if (MinR.Contains(lp)) return "min";
                if (CloseR.Contains(lp)) return "close";
                return "";
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                string h = Hit(T.LP(e.Location));
                if (h != _hover) { _hover = h; Invalidate(); }
                Cursor = h.Length > 0 ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = ""; Invalidate(); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                var lp = T.LP(e.Location);
                string h = Hit(lp);
                if (h == "theme") _f.ToggleTheme();
                else if (h == "min") _f.WindowState = FormWindowState.Minimized;
                else if (h == "close") _f.Close();
                Invalidate();
            }
        }
    }
}
