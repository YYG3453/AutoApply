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
        public const string Version = "1.10.0";

        /// <summary>--tray：启动后只呆在托盘里，不弹窗口（给计划任务用）。</summary>
        public bool StartInTray;

        /// <summary>--run-now：启动后立刻跑一轮（自测/命令行触发用）。</summary>
        public bool StartRunNow;
        public bool SuppressWizard;
        private bool _wasRunning;
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
        /// <summary>启动时一次性产生、要等窗口显示后再 Toast 的提示（登录态搬移结果）。</summary>
        private string _startupNote = "";
        private readonly Timer _toastTimer = new Timer { Interval = 2600 };
        private readonly Timer _pulse = new Timer { Interval = 400 };
        private float _pulseT;
        private NotifyIcon _tray;
        private bool _ready;

        /// <summary>手机看板（局域网 HTTP）。Start 失败时 Err 里是原因，健康页上照样显示。</summary>
        public PhoneWeb Web;


        // v1.8.6：窗口圆角交给 DWM（Win11 22000+ 原生圆角，和爱奇艺/资源管理器同一套），
        // 老系统没有这个属性，退回给窗口设一个圆角 Region。
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCorners();
        }

        private void ApplyCorners()
        {
            try
            {
                int pref = 2;                       // DWMWCP_ROUND
                // 边框色：Win11 默认会画一条 1px 浅色边，深色主题下很扎眼；染成窗口底色就看不见了
                int border = (T.Bg.R) | (T.Bg.G << 8) | (T.Bg.B << 16);
                DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
                if (Environment.OSVersion.Version.Build >= 22000 &&
                    DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int)) == 0) return;
            }
            catch { }
            try
            {
                using (var path = T.Round(new RectangleF(0, 0, Width, Height), 12f))
                    Region = new Region(path);
            }
            catch { }
        }

        public MainForm()
        {
            BaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            Paths.Init(BaseDir);
            Cfg = AppCfg.Load(Paths.CfgPath.Length > 0 ? Paths.CfgPath : Paths.CfgPath);
            // 登录态目录：v1.10 起放 %LOCALAPPDATA%\AutoApply\browser-profile（只有当前账户可读）。
            // 必须在 new Engine 之前做：Engine.ProfilePath 取的就是这里定下的位置。
            try
            {
                string profileNote;
                ProfileHome.Ensure(BaseDir, Cfg, out profileNote);
                if (!string.IsNullOrEmpty(profileNote)) { _startupNote = profileNote; Program.Trace("profile: " + profileNote); }
            }
            catch (Exception ex) { Program.Trace("profile home failed: " + ex.Message); }
            Engine = new Engine(BaseDir) { Cfg = Cfg };
            try { Program.Trace("浏览器开机自启：" + Engine.EnsureBrowserAutoStart()); }
            catch { }
            Records = RecordStore.Load(Engine.ScriptDir);

            // 手机看板：同一个 Wi-Fi 下用手机浏览器看状态、点按钮。
            // 起不来（端口被占 / 配置关掉）不影响主程序，原因留在 Web.Err 里给健康页显示。
            try
            {
                Web = new PhoneWeb(this, Engine, Cfg);
                string webMsg;
                if (Web.Start(out webMsg)) Program.Trace("phone web: " + Web.Url + "  pin=" + Web.Pin);
                else Program.Trace("phone web: 没起来 —— " + webMsg);
            }
            catch (Exception ex) { Program.Trace("phone web failed: " + ex.Message); }
            // 重启时把"今天已经跑过的时段"标上：否则 10:00 重启会让上午场从 09:30 再投一遍，
            // 重启时把"今天已经跑过的时段"标上：否则 10:00 重启会让上午场从 09:30 再投一遍，
            // 平台额度白烧一轮，还可能在两轮之间把同一批岗位点第二遍。
            try
            {
                string todayKey = DateTime.Now.ToString("yyyy-MM-dd");
                foreach (var w in Cfg.Windows)
                {
                    string tag = w.Name + " " + w.Range;
                    for (int i = 0; i < Records.Runs.Count; i++)
                    {
                        var rr = Records.Runs[i];
                        if (rr.Window != tag) continue;
                        if (!rr.Ts.ToString("yyyy-MM-dd").Equals(todayKey)) continue;
                        _ranWindows.Add(todayKey + ":" + w.Key);
                        break;
                    }
                }
            }
            catch (Exception ex) { Program.Trace("ranWindows preload failed: " + ex.Message); }

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
                    if (int.TryParse(rawArgs[i + 1], out ns)) T.NightStyle = ns < 0 ? 0 : (ns > 5 ? 5 : ns);
                }
            }
            T.SetNight(theme != "day");
            Program.Trace("theme resolved = '" + theme + "' cfg='" + Cfg.Theme + "' night=" + T.Night + " bg=" + T.Bg.ToArgb().ToString("X8"));
            Program.Trace("integrity = " + Engine.IntegrityLevel() + "  (low/untrusted 时浏览器拉不起来)");

            // v1.8.6：保留系统边框样式（Sizable → WS_THICKFRAME），再用 WM_NCCALCSIZE 把非客户区清零。
            // 这样窗口在 Win11 上才算"有框窗口"，DWM 才会给圆角 + 投影；
            // 纯 WS_POPUP（FormBorderStyle.None）加上 DWMWA 圆角属性是不生效的（实测仍是直角）。
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            Shown += (s, e) =>
            {
                if (StartRunNow)
                    BeginInvoke(new Action(delegate { try { StartRunManual(); } catch (Exception ex) { Program.Trace("run-now delegate throw: " + ex.Message); } }));
                // DWM 的圆角/边框属性在窗口真正合成之后再设一次才稳（HandleCreated 时还太早）
                BeginInvoke(new Action(delegate { ApplyCorners(); }));
                if (!string.IsNullOrEmpty(_startupNote))
                    BeginInvoke(new Action(delegate { if (Visible) Toast(_startupNote); }));
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
                if (_wasRunning && !Engine.Running) OnRunFinished();
                _wasRunning = Engine.Running;
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
                    using (var wf = new WizardForm(Cfg, Engine.ScriptDir, Engine.BinDir)) wf.ShowDialog(this);
                    Cfg = AppCfg.Load(Paths.CfgPath);
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
        /// <summary>一轮跑完后的兜底检查：0 条告警 + 数据自检。</summary>
        private void OnRunFinished()
        {
            try
            {
                Records = RecordStore.Load(Engine.ScriptDir);
                var zero = new List<string>();
                if (Cfg != null)
                    for (int i = 0; i < Cfg.Sites.Count; i++)
                    {
                        var s = Cfg.Sites[i];
                        if (!s.Enabled) continue;
                        if (Records.CountToday(s.Key) == 0) zero.Add(s.Name);
                    }
                if (zero.Count > 0)
                {
                    string txt = string.Join("、", zero.ToArray()) + " 今天一条都没投出去。多半是标签页挂了或没登录，去「平台健康」看一眼。";
                    Notify("今天有站 0 条", txt);
                    _log.View.Add("warn", "!! 零投递：" + string.Join("、", zero.ToArray()));
                }
                var all = SelfCheck.Run(Records, Cfg);
                var bad = SelfCheck.Failures(all);
                if (bad.Count > 0)
                {
                    Notify("数据自检有 " + bad.Count + " 项对不上", bad[0].Title + "：" + bad[0].Detail);
                    _log.View.Add("warn", "!! 数据自检：" + bad[0].Title + " — " + bad[0].Detail);
                }
                _dash.Invalidate();
            }
            catch (Exception ex) { Program.Trace("run finished check failed: " + ex.Message); }
        }

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
            Cfg.Save(Paths.CfgPath);
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
            ApplyCorners();   // 窗口边框色跟着底色走，不然换皮肤后 Win11 会留一条旧色的边
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

        /// <summary>
        /// 换皮肤（点右上角月亮/太阳弹出的那张菜单）。深色皮肤只在夜晚模式成立，
        /// 所以白天选了别的皮肤会顺手切到夜晚，免得"选了没反应"。
        /// </summary>
        public void ApplySkin(int style)
        {
            if (style < 0) style = 0;
            if (style > T.StyleNames.Length - 1) style = T.StyleNames.Length - 1;
            bool wantNight = T.Night || style != 5;
            T.NightStyle = style;
            ApplyTheme(wantNight ? "night" : "day", true);
            Toast("外观：" + T.StyleName(style) + (style == 5 ? (T.Night ? "（深色）" : "（浅色）") : ""));
        }

        /// <summary>点主题按钮弹出皮肤面板。自己画的浮层，跟着当前皮肤换色，还带每套的配色缩略图。</summary>
        public void ShowSkinMenu()
        {
            if (_skinPicker != null && !_skinPicker.IsDisposed)
            {
                if (_skinPicker.Visible) { _skinPicker.Close(); return; }
                _skinPicker.Dispose();   // 关掉的浮层别攒着，每次打开一个新实例
            }
            _skinPicker = new SkinPicker(this);
            _skinPicker.ShowAt(_winBtns.ThemeScreenRect, RectangleToScreen(ClientRectangle));
        }

        private SkinPicker _skinPicker;

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

        /// <summary>下一次自动开始的时间。已经过了今天的点就带"明天"，不然只看时分分不清是哪天。</summary>
        public string NextRunText()
        {
            if (!Cfg.ScheduleEnabled) return "—";
            DateTime t;
            var w = NextWindow(out t);
            if (w == null) return "—";
            string hhmm = string.Format("{0:00}:{1:00}", w.StartHour, w.StartMin);
            return t.Date == DateTime.Today ? hhmm : "明天 " + hhmm;
        }

        public string NextRunSub()
        {
            if (!Cfg.ScheduleEnabled) return "定时已关闭";
            var now = DateTime.Now; int m = now.Hour * 60 + now.Minute;
            foreach (var w in Cfg.Windows)
                if (w.Enabled && m >= w.StartMins && m < w.EndMins) return "本时段进行中";
            DateTime t;
            var nw = NextWindow(out t);
            if (nw == null) return "没有启用的时段";
            int mins = (int)Math.Round((t - now).TotalMinutes);
            if (mins <= 1) return "1 分钟内自动开始";
            if (mins < 60) return mins + " 分钟后自动开始";
            return (t.Date == DateTime.Today ? "今天 " : "明天 ") + t.ToString("HH:mm") + " 自动开始";
        }

        /// <summary>
        /// 最近的一场：正在跑的这一场不算"下次"，已过的顺延到明天。
        /// 旧写法只看"几点几分"，过了今天的点还写"今天到点自动开始"，跨天的时候是错的。
        /// </summary>
        private WindowCfg NextWindow(out DateTime start)
        {
            var now = DateTime.Now;
            int m = now.Hour * 60 + now.Minute;
            WindowCfg best = null;
            DateTime bestT = DateTime.MaxValue;
            foreach (var w in Cfg.Windows)
            {
                if (!w.Enabled) continue;
                var t = now.Date.AddMinutes(w.StartMins);
                bool inThisWindow = m >= w.StartMins && m < w.EndMins;
                if (t <= now || inThisWindow) t = t.AddDays(1);
                if (t < bestT) { bestT = t; best = w; }
            }
            start = best == null ? DateTime.MinValue : bestT;
            return best;
        }

        public SiteResult ResultOf(string key)
        {
            foreach (var r in Engine.Results) if (r.Key == key) return r;
            return null;
        }

        public void StartRunManual()
        {
            var all = new List<SiteCfg>();
            foreach (var s in Cfg.Sites) if (s.Enabled) all.Add(s);
            StartRunSites(all, "手动运行");
        }

        /// <summary>
        /// 指定几站开跑。开跑后引擎自己会做三件事：浏览器没开就开、
        /// 站点标签页没有就开、没登录就等你登（不会像以前那样直接跳过）。
        /// </summary>
        public void StartRunSites(List<SiteCfg> sites, string windowName)
        {
            StartRunSites(sites, windowName, true);
        }

        /// <summary>jumpToLog=false 时留在当前页（平台健康的「重开」用），不抢用户的视线。</summary>
        public void StartRunSites(List<SiteCfg> sites, string windowName, bool jumpToLog)
        {
            Program.Trace("StartRunSites enter running=" + (Engine == null ? "null-engine" : Engine.Running.ToString()) + " want=" + (sites == null ? -1 : sites.Count));
            if (Engine.Running) { Toast("已经在跑了，先点「停止」或等这一轮结束"); return; }
            var list = new List<SiteCfg>();
            if (sites != null) foreach (var s in sites) if (s != null && s.Enabled) list.Add(s);
            if (list.Count == 0) { Toast("要跑的平台都被停用了，先去「平台与关键词」打开一个"); return; }
            Records = RecordStore.Load(Engine.ScriptDir);
            try
            {
                Engine.StartRun(list, windowName);
                Program.Trace("StartRunSites started sites=" + list.Count + " first=" + list[0].Key);
            }
            catch (Exception ex) { Program.Trace("StartRunSites throw: " + ex.GetType().Name + ": " + ex.Message); return; }
            if (jumpToLog) ShowPage("log");
            _log.View.Add("sys", "=== " + windowName + " · 先检查浏览器和登录状态");
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

        /// <summary>从仪表盘下钻到投递明细：带上筛选条件，并在页面上留一个能点掉的筛选标签。</summary>
        public void ShowRecords(string day, string site, string city)
        {
            Page p;
            if (_pages.TryGetValue("records", out p))
            {
                var rp = p as RecordsPage;
                if (rp != null) rp.SetFilter(day, site, city);
            }
            ShowPage("records");
        }

        private T PageOf<T>(string key) where T : Page
        {
            Page p;
            return _pages.TryGetValue(key, out p) ? p as T : null;
        }

        internal void ShowPage(string key)
        {
            _page = key;
            _rail.Selected = key;
            _rail.Invalidate();
            foreach (var kv in _pages) kv.Value.Visible = kv.Key == key;
            Page pg;
            if (_pages.TryGetValue(key, out pg)) pg.StartAppear();
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
            var sf = T.F(10.5f);
            // 提示文本可能是整句（比如"读不到任务计划程序…"），状态栏只有一行：放不下就截断加省略号，
            // 否则会盖住左边的图例和右边的版本号。
            float maxW = W - 228 - 190;
            if (maxW > 80 && g.MeasureString(left, sf).Width > maxW)
            {
                while (left.Length > 6 && g.MeasureString(left + "…", sf).Width > maxW)
                    left = left.Substring(0, left.Length - 1);
                left = left.TrimEnd('，', '、', '。', ' ') + "…";
            }
            T.Draw(g, left, sf, _toast.Length > 0 ? T.Jade : T.Text3, 228, fy + 7);
            if (Engine.Running)
            {
                float a = 0.35f + 0.65f * (float)((Math.Sin(_pulseT) + 1) / 2);
                T.Dot(g, W - 150, fy + 13, 3.5f, T.Alpha(T.Jade, (int)(a * 255)));
            }
            T.TextR(g, "v" + Version + "  ·  数据：" + (Paths.DataDir.Length > 0 ? System.IO.Path.GetFileName(Paths.DataDir) : "scripts") + "\\report.md", T.F(10.5f), T.Text3, W - 24, fy + 7);
        }

        // ---------- 窗口拖动 ----------
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCAPTION = 2;

        private const int WM_NCCALCSIZE = 0x0083;

        /// <summary>无边框窗口：顶部 44px 与四周 6px 边缘都当作标题栏，按住即可拖动。</summary>
        protected override void WndProc(ref Message m)
        {
            // 把非客户区清零：客户区铺满整个窗口，标题栏/边框由我们自己画
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                m.Result = IntPtr.Zero;
                return;
            }
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
            // 焦点在搜索框里时，Esc 是「清空输入」，不该顺手把投递停了
            if (ActiveControl is TextBoxBase) return;
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
            try { if (Web != null) Web.Stop(); } catch { }
            base.OnFormClosing(e);
        }

        // ---------- 右上角窗口按钮 ----------
        private class WinButtons : Control
        {
            // 主题入口做成一颗胶囊：月亮/太阳 + 当前皮肤名 + 下拉箭头。
            // 原来只是一个 48px 图标格里 12px 的月亮，再贴一个小三角，既小又说不清点它会发生什么。
            public const int WidthLogical = 200;
            private const float ChipX = 8f, ChipY = 8f, ChipW = 100f, ChipH = 28f;
            private const float ThemeZone = 112f;      // 点这一整条都能弹出面板，比只认胶囊本身好点
            private const float BtnW = 44f;

            /// <summary>胶囊在屏幕上的位置：皮肤面板贴着它往下弹。</summary>
            public Rectangle ThemeScreenRect
            {
                get { return RectangleToScreen(new Rectangle(T.RS(ChipX), T.RS(ChipY), T.RS(ChipW), T.RS(ChipH))); }
            }

            private readonly MainForm _f;
            private readonly ToolTip _tip = new ToolTip();
            private string _hover = "";
            public WinButtons(MainForm f)
            {
                _f = f;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                _tip.SetToolTip(this, "点一下换皮肤 · 共 " + T.StyleNames.Length + " 套");
                ApplyRegion();
            }

            // 这个控件只在这三块形状上存在。要是不裁，它铺满 200×44，
            // 透明背景只会重画父窗体、不会合成本页的兄弟控件 ——
            // 页面右上角的按钮会被抹掉一截，点上去也被它吃掉。
            private void ApplyRegion()
            {
                if (Width <= 0 || Height <= 0) return;
                using (var p = new GraphicsPath())
                {
                    float pad = T.RS(8f), pillH = T.RS(28f), pillW = T.RS(BtnW - 16f);
                    p.AddPath(T.Round(new RectangleF(T.RS(ChipX), T.RS(ChipY), T.RS(ChipW), T.RS(ChipH)), T.RS(ChipH) / 2f), false);
                    p.AddPath(T.Round(new RectangleF(T.RS(ThemeZone) + pad, pad, pillW, pillH), T.RS(8f)), false);
                    p.AddPath(T.Round(new RectangleF(T.RS(ThemeZone + BtnW) + pad, pad, pillW, pillH), T.RS(8f)), false);
                    var old = Region;
                    Region = new Region(p);
                    if (old != null) old.Dispose();
                }
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                ApplyRegion();
            }

            private RectangleF ChipR { get { return new RectangleF(ChipX, ChipY, ChipW, ChipH); } }
            private RectangleF MinR { get { return new RectangleF(ThemeZone, 0, BtnW, Height / T.S); } }
            private RectangleF CloseR { get { return new RectangleF(ThemeZone + BtnW, 0, Width / T.S - ThemeZone - BtnW, Height / T.S); } }

            private void Pill(Graphics g, RectangleF r, Color c)
            {
                T.FillRound(g, new RectangleF(r.X + 8, r.Y + 8, r.Width - 16, r.Height - 16), 8f, c);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(T.S, T.S);

                if (_hover == "min") Pill(g, MinR, T.BtnHover);
                if (_hover == "close") Pill(g, CloseR, T.Alpha(T.Danger, 60));
                DrawChip(g, ChipR, _hover == "theme");

                using (var p = new Pen(_hover == "min" ? T.Text : T.Text3, 1.2f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, MinR.X + MinR.Width / 2 - 5.5f, MinR.Y + MinR.Height / 2 + 3,
                                  MinR.X + MinR.Width / 2 + 5.5f, MinR.Y + MinR.Height / 2 + 3);
                }
                using (var p = new Pen(_hover == "close" ? T.Danger : T.Text3, 1.2f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    float cx = CloseR.X + CloseR.Width / 2, cy = CloseR.Y + CloseR.Height / 2 + 2;
                    g.DrawLine(p, cx - 4.6f, cy - 4.6f, cx + 4.6f, cy + 4.6f);
                    g.DrawLine(p, cx + 4.6f, cy - 4.6f, cx - 4.6f, cy + 4.6f);
                }
            }

            /// <summary>胶囊本体：细边、悬停时提亮；里面是日月图标、当前皮肤名、下拉箭头。</summary>
            private void DrawChip(Graphics g, RectangleF r, bool hov)
            {
                float rad = r.Height / 2f;
                if (hov) T.FillRound(g, r, rad, T.Alpha(T.Text, 22));
                T.StrokeRound(g, r, rad, hov ? T.Alpha(T.Text, 90) : T.Alpha(T.Text, 34), 1f);

                float cy = r.Y + r.Height / 2f;
                float ix = r.X + 16f;
                SunMoon(g, ix, cy, hov ? T.Jade : T.Text2);

                // 白天只有 GitHub 有专属浅色皮肤，其余都走宣纸日景，所以直接写"日间"。
                string label = T.Night ? T.StyleNames[T.NightStyle] : (T.NightStyle == 5 ? "GitHub" : "日间");
                float tx = ix + 9f;
                float maxW = r.Right - 25f - tx;
                using (var f = T.F(11.5f))
                {
                    var sz = g.MeasureString(label, f);
                    if (sz.Width > maxW)
                    {
                        string cut = label;
                        while (cut.Length > 1 && g.MeasureString(cut + "…", f).Width > maxW)
                            cut = cut.Substring(0, cut.Length - 1);
                        label = cut + "…";
                        sz = g.MeasureString(label, f);
                    }
                    T.Draw(g, label, f, hov ? T.Text : T.TextSoft, tx, cy - sz.Height / 2f + 1f);
                }

                float ax = r.Right - 15f;
                using (var p = new Pen(hov ? T.Text2 : T.Text3, 1.4f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLines(p, new[]
                    {
                        new PointF(ax - 3.4f, cy - 2.1f),
                        new PointF(ax, cy + 1.5f),
                        new PointF(ax + 3.4f, cy - 2.1f)
                    });
                }
            }

            /// <summary>夜 = 月亮，日 = 太阳（和皮肤面板里那个是同一套画法）。</summary>
            private static void SunMoon(Graphics g, float cx, float cy, Color c)
            {
                if (T.Night)
                {
                    using (var path = new GraphicsPath())
                    using (var reg = new System.Drawing.Region())
                    {
                        path.AddEllipse(cx - 5.4f, cy - 5.4f, 10.8f, 10.8f);
                        reg.MakeEmpty();
                        reg.Union(path);
                        using (var cut = new GraphicsPath())
                        {
                            cut.AddEllipse(cx - 1.1f, cy - 7.4f, 10.8f, 10.8f);
                            reg.Exclude(cut);
                        }
                        using (var b = new SolidBrush(c)) g.FillRegion(b, reg);
                    }
                }
                else
                {
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - 3.2f, cy - 3.2f, 6.4f, 6.4f);
                    using (var p = new Pen(c, 1.1f))
                        for (int i = 0; i < 8; i++)
                        {
                            double a = Math.PI * i / 4.0;
                            g.DrawLine(p,
                                cx + (float)Math.Cos(a) * 5.4f, cy + (float)Math.Sin(a) * 5.4f,
                                cx + (float)Math.Cos(a) * 7.6f, cy + (float)Math.Sin(a) * 7.6f);
                        }
                }
            }

            private string Hit(PointF lp)
            {
                if (lp.X < ThemeZone) return "theme";
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
                // 主题格：左右键都直接列出 6 套皮肤（菜单里还有日夜切换）。
                // 之前只把菜单挂在右键上，很多人点了左键只看到日夜翻转，以为没有皮肤。
                if (h == "theme")
                {
                    _f.ShowSkinMenu();
                    Invalidate();
                    return;
                }
                if (e.Button == MouseButtons.Right) return;
                if (h == "min") _f.WindowState = FormWindowState.Minimized;
                else if (h == "close") _f.Close();
                Invalidate();
            }
        }
    }
}
