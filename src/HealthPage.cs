using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 平台健康：四站标签页 / 登录态 / 验证码一屏看完，异常可一键重开。
    /// 只读检查，不改账号任何设置。
    ///
    /// v1.8.6 起这里有一道硬规矩：**OnPaint 里绝不做网络 I/O**。
    /// 以前每次重绘都要同步问三次 CDP（ChromeAlive / Tabs / ChromeVersion），
    /// Chrome 没开时代理探测 + 超时能把一次重绘拖到 4 秒，鼠标一动就整窗卡死。
    /// 现在改由后台每 4 秒刷一次缓存，绘制只读缓存。
    /// </summary>
    internal class HealthPage : Page
    {
        private readonly Dictionary<string, SiteHealth> _h = new Dictionary<string, SiteHealth>();
        private int _busy;      // 用户点的检查/重开：同一时间只做一件
        private int _autoScan;  // 进页面时的自动巡检：只读，不挡按钮
        private bool _checkedOnce;
        private string _summary = "";

        // ---- CDP 快照（后台线程写，绘制线程读）----
        private readonly object _gate = new object();
        private bool _chrome;
        private bool _anyChrome;   // 系统里有没有 Chrome 在跑（就算没开调试口）
        private string _chromeVer = "";
        private List<string> _tabs = new List<string>();
        private string _snapAt = "";
        private int _probing;
        private readonly System.Windows.Forms.Timer _watch;

        public HealthPage(MainForm app) : base(app)
        {
            BackColor = T.Bg;
            _watch = new System.Windows.Forms.Timer { Interval = 4000 };
            _watch.Tick += delegate { RefreshSnapshot(); };
        }

        public override void RefreshTheme()
        {
            BackColor = T.Bg;
            Invalidate(true);
        }

        private void Ui(Action a)
        {
            try { App.BeginInvoke(a); } catch { }
        }

        /// <summary>页面可见才轮询；切走就停，别在后台白问。</summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { _watch.Stop(); return; }
            RefreshSnapshot();
            _watch.Start();
            if (!_checkedOnce)
            {
                _checkedOnce = true;
                // 只是看着一页，别顺手把浏览器拉起来；要看全部状态点「全部检查」。
                CheckAll(false, true);
            }
        }

        /// <summary>问一次 CDP：Chrome 在不在、版本、所有标签页。走后台线程，带 1.2 秒超时。</summary>
        private void RefreshSnapshot()
        {
            if (Interlocked.CompareExchange(ref _probing, 1, 0) != 0) return;
            Health.Port = App.Cfg.Port;
            Health.ScriptDir = App.Engine.ScriptDir;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    bool alive = Health.ChromeAlive();
                    string ver = alive ? Health.ChromeVersion() : "";
                    List<string> tabs = alive ? Health.Tabs() : new List<string>();
                    lock (_gate)
                    {
                        _chrome = alive;
                        _anyChrome = Engine.AnyChromeRunning();
                        _chromeVer = ver;
                        _tabs = tabs;
                        _snapAt = DateTime.Now.ToString("HH:mm:ss");
                    }
                }
                catch { }
                finally
                {
                    Volatile.Write(ref _probing, 0);
                    Ui(delegate { Invalidate(); });
                }
            });
        }

        /// <summary>取一份快照给绘制用（锁只圈住拷贝，不圈绘制）。</summary>
        private void Snapshot(out bool chrome, out string ver, out List<string> tabs, out string at)
        {
            lock (_gate)
            {
                chrome = _chrome;
                ver = _chromeVer;
                tabs = _tabs;
                at = _snapAt;
            }
            if (tabs == null) tabs = new List<string>();
        }

        public void CheckAll() { CheckAll(false, false); }

        private bool AnyChromeNow { get { lock (_gate) return _anyChrome; } }

        public void CheckAll(bool openBrowser) { CheckAll(openBrowser, false); }

        /// <summary>正在检查（用户点的或自动巡检都算），界面显示"检查中…"用。</summary>
        private bool BusyNow { get { return Volatile.Read(ref _busy) != 0 || Volatile.Read(ref _autoScan) != 0; } }

        /// <summary>
        /// 检查四站。openBrowser=true（用户点了「全部检查」）时先把浏览器和标签页都准备好。
        /// passive=true 是"刚切到这一页顺手看一眼"：它不占 _busy，所以按钮照常能点——
        /// 以前两者共用一个闸，自动巡检还没跑完时点「重开」会静默什么都不做。
        /// </summary>
        private void CheckAll(bool openBrowser, bool passive)
        {
            if (passive)
            {
                if (Volatile.Read(ref _busy) != 0) return;
                if (Interlocked.CompareExchange(ref _autoScan, 1, 0) != 0) return;
            }
            else if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                try { App.Toast("还在检查，等这一下完"); } catch { }
                return;
            }
            Health.Port = App.Cfg.Port;
            Health.ScriptDir = App.Engine.ScriptDir;
            var sites = App.Cfg.Sites;
            _summary = openBrowser ? "正在打开浏览器 …" : "正在检查 …";
            Ui(delegate { Invalidate(); });
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    bool alive = Health.ChromeAlive();
                    if (!alive && openBrowser)
                    {
                        string msg;
                        alive = App.Engine.EnsureChrome(out msg);
                        if (!alive) { _summary = msg; return; }
                        _summary = "浏览器已打开，正在打开四个平台 …";
                        Ui(delegate { Invalidate(); });
                    }
                    if (!alive)
                    {
                        lock (_h)
                        {
                            _h.Clear();
                            for (int i = 0; i < sites.Count; i++)
                                _h[sites[i].Key] = new SiteHealth
                                {
                                    Key = sites[i].Key,
                                    Name = sites[i].Name,
                                    Note = "没连上浏览器调试端口",
                                    Checked = DateTime.Now.ToString("HH:mm:ss")
                                };
                        }
                        _summary = "浏览器没连上：点「重开」会打开它";
                        return;
                    }
                    int issues = 0;
                    for (int i = 0; i < sites.Count; i++)
                    {
                        var s = sites[i];
                        if (!s.Enabled) continue;
                        if (openBrowser) App.Engine.EnsureTab(s);
                        SiteHealth h;
                        try { h = Health.Probe(s); }
                        catch { h = new SiteHealth(); h.Key = s.Key; h.Name = s.Name; h.Note = "检查失败"; }
                        if (h.Note != "正常") issues++;
                        lock (_h) { _h[s.Key] = h; }
                        _summary = "已检查 " + (i + 1) + " / " + sites.Count + " 站";
                        Ui(delegate { Invalidate(); });
                    }
                    _summary = issues == 0 ? "四站都正常" : ("有 " + issues + " 站需要处理");
                }
                catch (Exception ex) { _summary = "检查出错：" + ex.Message; }
                finally
                {
                    if (passive) Volatile.Write(ref _autoScan, 0); else Volatile.Write(ref _busy, 0);
                    Ui(delegate { Invalidate(); }); RefreshSnapshot();
                }
            });
        }

        /// <summary>只查这一站（会先把浏览器 / 标签页准备好）。</summary>
        private void CheckOne(SiteCfg s)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            Health.Port = App.Cfg.Port;
            Health.ScriptDir = App.Engine.ScriptDir;
            _summary = "正在检查 " + s.Name + " …";
            Ui(delegate { Invalidate(); });
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string msg;
                    if (!Health.ChromeAlive() && !App.Engine.EnsureChrome(out msg)) { _summary = msg; return; }
                    App.Engine.EnsureTab(s);
                    var h = Health.Probe(s);
                    lock (_h) { _h[s.Key] = h; }
                    _summary = s.Name + "：" + h.Note;
                }
                catch (Exception ex) { _summary = "检查失败：" + ex.Message; }
                finally { Volatile.Write(ref _busy, 0); Ui(delegate { Invalidate(); }); RefreshSnapshot(); }
            });
        }

        /// <summary>「全部重开」：打开浏览器 → 四站标签页都开 → 直接开跑（没登录会自动等）。</summary>
        private void ReopenAll()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            Health.Port = App.Cfg.Port;
            Health.ScriptDir = App.Engine.ScriptDir;
            _summary = "正在全部重开，随后自动投递 …";
            Ui(delegate { Invalidate(); });
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var list = new List<SiteCfg>();
                    foreach (var s in App.Cfg.Sites) if (s.Enabled) list.Add(s);
                    if (list.Count == 0) { _summary = "四个平台都停用了，先去「平台与关键词」打开一个"; return; }
                    Ui(delegate { App.StartRunSites(list, "全部重开 · 自动投递", false); });
                    _summary = "四站已开始（打开浏览器 → 登录 → 自动投递）";
                }
                catch (Exception ex) { _summary = "重开失败：" + ex.Message; }
                finally { Volatile.Write(ref _busy, 0); Ui(delegate { Invalidate(); }); RefreshSnapshot(); }
            });
        }

        /// <summary>
        /// 「重开」= 打开浏览器（没开就开）→ 打开这一站的标签页 → 直接开跑。
        /// 跑起来之后引擎会自己等登录：页面就在用户眼前，登录 / 拖完滑块就继续投。
        /// </summary>
        private void Reopen(SiteCfg s)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            Health.Port = App.Cfg.Port;
            Health.ScriptDir = App.Engine.ScriptDir;
            _summary = "正在重开 " + s.Name + "，随后自动投递 …";
            Ui(delegate { Invalidate(); });
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // 只关掉挂死的旧标签；开浏览器 / 开新标签 / 等登录都交给引擎，
                    // 这样"浏览器起不来"这种失败会进运行日志，而不是只在这页闪一行字。
                    try { Health.Reopen(s); } catch { }
                    var one = new List<SiteCfg>();
                    one.Add(s);
                    Ui(delegate { App.StartRunSites(one, s.Name + " · 重开后自动投递", false); });
                    _summary = s.Name + "：已开始（打开浏览器 → 登录 → 自动投递）";
                }
                catch (Exception ex) { _summary = "重开失败：" + ex.Message; }
                finally { Volatile.Write(ref _busy, 0); Ui(delegate { Invalidate(); }); RefreshSnapshot(); }
            });
        }

        private SiteHealth Get(string key)
        {
            lock (_h) { SiteHealth h; return _h.TryGetValue(key, out h) ? h : null; }
        }

        private string LastRunOf(string key)
        {
            if (App.Records == null || App.Records.LastRun == null) return "今天还没跑过";
            for (int i = 0; i < App.Records.LastRun.Count; i++)
            {
                var r = App.Records.LastRun[i];
                if (r.SiteKey != key) continue;
                return r.Sent + " 条 · " + r.StopText;
            }
            return "本轮没跑这一站";
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
            T.Draw(g, "平台健康", Fonts.H1, T.Text, 32, 24);
            // 正在跑的时候把这行副标题换成实时阶段：点了「重开」就不用再切页看进度
            if (App.Engine != null && App.Engine.Running)
            {
                string st = App.Engine.Stage;
                if (st == null || st.Length == 0) st = "准备中";
                string who = App.Engine.CurrentSiteName;
                string line = "● 正在运行 · " + (who != null && who.Length > 0 ? who + " · " : "") + st;
                T.Draw(g, Fit(g, line, Fonts.Small, W - 200), Fonts.Small, T.Jade, 32, 62);
                Ghost(g, new RectangleF(W - 452, 56, 96, 26), "运行日志", "health:log", T.Text2);
            }
            else if (App.Engine != null && !string.IsNullOrEmpty(App.Engine.LastChromeError))
            {
                // 起浏览器失败时，直接把原因写在这行，别让人再去翻日志
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    using (var br = new SolidBrush(T.Warn))
                        g.DrawString("✕ " + App.Engine.LastChromeError, Fonts.Small, br, new RectangleF(32, 55, W - 200, 22), sf);
            }
            else
            {
                T.Draw(g, "「重开」会拉起程序专用浏览器（不是平时那个 Chrome），登录 / 拖完滑块后自动开始投递", Fonts.Small, T.Text3, 32, 62);
            }

            // 右上操作按钮（右边留出窗口三键的位置）
            Ghost(g, new RectangleF(W - 452, 26, 108, 32), "全部检查", "health:check", T.Jade);
            Ghost(g, new RectangleF(W - 334, 26, 108, 32), "全部重开", "health:reopenall", T.Text2);

            // 顶部 KPI —— 全部读缓存，不在这里发任何网络请求
            bool chrome;
            string ver;
            List<string> tabs;
            string at;
            Snapshot(out chrome, out ver, out tabs, out at);
            int issues = 0;
            foreach (var s in App.Cfg.Sites) { var hh = Get(s.Key); if (hh != null && hh.Note != "正常") issues++; }
            float kw = (W - 64f - 3 * 14f) / 4f;
            float ky = 100f, kh = 96f;
            Kpi(g, new RectangleF(32, ky, kw, kh), "CDP 端口 " + App.Cfg.Port, chrome ? "已连接" : "未连接",
                chrome ? T.Jade : T.Danger, chrome ? "浏览器调试通道正常" : (AnyChromeNow ? "你的 Chrome 开着，但不是程序用的那个窗口" : "还没打开浏览器 · 点「重开」"));
            Kpi(g, new RectangleF(32 + kw + 14, ky, kw, kh), "Chrome", chrome && ver.Length > 0 ? ver : "—",
                chrome ? T.Text : T.Text3, chrome ? "连接正常" : "还没连上调试端口");
            Kpi(g, new RectangleF(32 + (kw + 14) * 2, ky, kw, kh), "标签页", tabs.Count.ToString(),
                T.Text, App.Cfg.Sites.Count + " 站需要各自的页面");
            Kpi(g, new RectangleF(32 + (kw + 14) * 3, ky, kw, kh), "待处理", issues.ToString(),
                issues == 0 ? T.Jade : T.Warn, BusyNow ? "检查中…" : (_summary.Length > 0 ? _summary : "尚未检查"));

            // 四站卡片 2×2
            float top = ky + kh + 18f;
            float cw = (W - 64f - 18f) / 2f;
            float chh = Math.Max(150f, Math.Min(196f, (H - top - 40f - 18f) / 2f));
            var sites = App.Cfg.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                float x = 32f + (i % 2) * (cw + 18f);
                float y = top + (i / 2) * (chh + 18f);
                var card = new RectangleF(x, y, cw, chh);
                T.Card(g, card);
                var h = Get(s.Key);
                Color dot = !s.Enabled ? T.Text3 : (h == null ? T.Text3 : (h.Note == "正常" ? T.Jade : (h.Captcha ? T.Danger : T.Warn)));
                T.Dot(g, x + 22, y + 26, 4.5f, dot);
                T.Draw(g, s.Name, Fonts.H2, T.Text, x + 36, y + 16);
                T.Draw(g, s.Enabled ? "已启用" : "已停用", Fonts.Tiny, T.Text3, x + 36, y + 42);

                float ly = y + 68f;
                string tabText = h == null ? (BusyNow ? "检查中…" : "未检查") : (h.HasTab ? (h.Tabs + " 个标签页") : "没有标签页");
                string loginText = h == null ? (BusyNow ? "检查中…" : "—") : (h.Login == "yes" ? "已登录" : h.Login == "no" ? "未登录" : "无法判断");
                string capText = h == null ? (BusyNow ? "检查中…" : "—") : (h.Captcha ? "有验证码" : "无");
                T.Draw(g, "标签页", Fonts.Small, T.Text3, x + 36, ly);
                T.Draw(g, tabText, Fonts.Small, h != null && !h.HasTab ? T.Warn : T.Text2, x + 110, ly);
                ly += 24;
                T.Draw(g, "登录态", Fonts.Small, T.Text3, x + 36, ly);
                T.Draw(g, loginText, Fonts.Small, h != null && h.Login == "no" ? T.Warn : T.Text2, x + 110, ly);
                ly += 24;
                T.Draw(g, "验证码", Fonts.Small, T.Text3, x + 36, ly);
                T.Draw(g, capText, Fonts.Small, h != null && h.Captcha ? T.Danger : T.Text2, x + 110, ly);
                ly += 24;
                T.Draw(g, "最近一轮", Fonts.Small, T.Text3, x + 36, ly);
                T.Draw(g, LastRunOf(s.Key), Fonts.Small, T.Text2, x + 110, ly);

                string note = h != null && h.Note.Length > 0 && h.Note != "正常" ? h.Note : "";
                if (note.Length == 0 && !chrome) note = AnyChromeNow
                    ? "你的 Chrome 开着，但没开调试通道 · 点「重开」用程序自己的窗口"
                    : "还没连上调试端口 · 点「重开」";
                if (h != null && h.Login == "no") note = "未登录 · 点「重开」后在弹出的浏览器窗口里登录";
                if (note.Length > 0)
                    T.Draw(g, Fit(g, note, Fonts.Tiny, cw - 48f), Fonts.Tiny,
                           (h != null && h.Captcha) ? T.Danger : T.Warn, x + 36, y + chh - 34);

                Ghost(g, new RectangleF(x + cw - 176, y + chh - 40, 72, 28), "检查", "health:check:" + s.Key, T.Text2);
                Ghost(g, new RectangleF(x + cw - 96, y + chh - 40, 72, 28), "重开", "health:reopen:" + s.Key, T.Jade);
            }
        }

        /// <summary>一行字超宽就截断加省略号：卡片底部的提示不能糊到按钮上去。</summary>
        private static string Fit(Graphics g, string s, Font f, float maxW)
        {
            if (maxW <= 20 || s == null || s.Length == 0) return s;
            if (g.MeasureString(s, f).Width <= maxW) return s;
            while (s.Length > 3 && g.MeasureString(s + "…", f).Width > maxW)
                s = s.Substring(0, s.Length - 1);
            return s + "…";
        }

        /// <summary>KPI 卡：数字用大号数字字体，文字（"已连接"/版本号）用小一号，别撑破卡片。</summary>
        private void Kpi(Graphics g, RectangleF r, string label, string value, Color vc, string sub)
        {
            T.Card(g, r);
            T.Draw(g, label, Fonts.Small, T.Text3, r.X + 18, r.Y + 14);
            Font vf = SizeOf(g, value, r.Width - 36f);
            T.Draw(g, value, vf, vc, r.X + 18, r.Y + 38);
            // 副标题只有一行，超出卡片就截断加省略号，别糊到隔壁卡上
            float maxW = r.Width - 36f;
            if (maxW > 40 && g.MeasureString(sub, Fonts.Tiny).Width > maxW)
            {
                while (sub.Length > 4 && g.MeasureString(sub + "…", Fonts.Tiny).Width > maxW)
                    sub = sub.Substring(0, sub.Length - 1);
                sub = sub.TrimEnd('，', '、', '。', ' ', '：') + "…";
            }
            T.Draw(g, sub, Fonts.Tiny, T.Text3, r.X + 18, r.Y + r.Height - 22);
        }

        private static Font SizeOf(Graphics g, string value, float maxW)
        {
            bool num = value.Length > 0;
            for (int i = 0; i < value.Length; i++) if (!char.IsDigit(value[i])) { num = false; break; }
            Font[] ladder = num
                ? new[] { Fonts.Big, Fonts.H1, Fonts.H2, Fonts.Body }
                : new[] { Fonts.Value, Fonts.H2, Fonts.Body, Fonts.Small };
            for (int i = 0; i < ladder.Length; i++)
                if (g.MeasureString(value, ladder[i]).Width <= maxW) return ladder[i];
            return ladder[ladder.Length - 1];
        }

        private void Ghost(Graphics g, RectangleF r, string text, string id, Color accent)
        {
            bool hot = HoverCur.ContainsKey(id) && HoverCur[id] > 0.5f;
            T.FillRound(g, r, 8f, hot ? T.HoverSoftHi : T.HoverSoft);
            T.StrokeRound(g, r, 8f, T.LineSoft);
            var sz = g.MeasureString(text, Fonts.Small);
            T.Draw(g, text, Fonts.Small, accent, r.X + (r.Width - sz.Width) / 2f, r.Y + (r.Height - sz.Height) / 2f);
            AddHit(id, r);
        }

        protected override void OnHit(string id)
        {
            if (id == "health:log") { App.GotoPage("log"); return; }
            if (id == "health:check") { CheckAll(true); return; }
            if (id == "health:reopenall") { ReopenAll(); return; }
            if (id.StartsWith("health:check:"))
            {
                string key = id.Substring("health:check:".Length);
                foreach (var s in App.Cfg.Sites) if (s.Key == key) { CheckOne(s); return; }
                return;
            }
            if (id.StartsWith("health:reopen:"))
            {
                string key = id.Substring("health:reopen:".Length);
                foreach (var s in App.Cfg.Sites) if (s.Key == key) { Reopen(s); return; }
            }
        }
    }
}
