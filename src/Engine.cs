using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using System.Management;

namespace AutoApply
{
    internal class SiteResult
    {
        public string Key;
        public string Name;
        public int Sent;
        public int Skipped;
        public int Errs;
        public string Stop = "";
        public string Error = "";
        /// <summary>需要人工处理的原因（未登录 / 验证码）；空 = 一切正常。</summary>
        public string Needs = "";
        public DateTime Started;
        public DateTime Ended;
        public int Elapsed { get { return Math.Max(0, (int)(Ended - Started).TotalSeconds); } }
    }

    /// <summary>投递引擎：找/起 Chrome → 保证标签页 → 顺序跑四个平台的 node 脚本 → 汇总报告。</summary>
    internal class Engine
    {
        public readonly string BaseDir;
        /// <summary>脚本目录（exe 旁边）：.js 代码在这里。</summary>
        public readonly string BinDir;
        /// <summary>数据目录（用户目录）：state-*.jsonl / runs.jsonl / rules.json 在这里。</summary>
        public readonly string ScriptDir;
        public AppCfg Cfg;
        public bool Running { get; private set; }
        public string LastStop = "done";
        public string CurrentSite = "";
        public string CurrentSiteName = "";
        public readonly List<SiteResult> Results = new List<SiteResult>();
        public DateTime RunStarted;
        public string RunWindowName = "手动运行";

        public event Action<string, string> Log;                 // level, text
        public event Action StateChanged;
        public event Action<string, int> SiteSent;               // siteKey, sent so far
        public event Action<string> SiteSwith;
        /// <summary>某一站需要人工处理：siteName, reason。</summary>
        public event Action<string, string> SiteAttention;

        /// <summary>当前阶段（健康页显示：打开浏览器 / 准备标签页 / 等待登录 / 投递中）。</summary>
        public string Stage = "";

        /// <summary>最近一次"起浏览器"失败的原因，健康页直接显示（空 = 没失败）。</summary>
        public string LastChromeError = "";

        private Thread _worker;
        private Process _node;
        private volatile bool _stop;
        /// <summary>这一轮已经等过一次登录且没等到：后面几站就别再各等一遍了。</summary>
        private bool _loginWaitFailed;
        private static readonly object _chromeGate = new object();
        /// <summary>我们自己起的那个浏览器进程（用来把它提到最前，别提到用户自己开的窗口上）。</summary>
        private int _chromePid;
        // 9333 端口归属检查（15 秒缓存，免得每站都吃一次 WMI；null = 归属正常）
        private DateTime _ownerCheckAt = DateTime.MinValue;
        private string _ownerProfile = "";
        private int _ownerPort;
        private string _ownerProblem;
        private string _ownerWarned = "";
        private readonly object _lock = new object();

        public Engine(string baseDir)
        {
            BaseDir = baseDir;
            BinDir = Paths.BinDir.Length > 0 ? Paths.BinDir : Path.Combine(baseDir, "scripts");
            ScriptDir = Paths.DataDir.Length > 0 ? Paths.DataDir : BinDir;
        }

        private void L(string level, string text)
        {
            var h = Log;
            if (h != null) { try { h(level, text); } catch { } }
        }

        private void Changed() { var h = StateChanged; if (h != null) { try { h(); } catch { } } }

        public string NodePath()
        {
            var bundled = Path.Combine(BaseDir, "node", "node.exe");
            if (File.Exists(bundled)) return bundled;
            var side = Path.Combine(BaseDir, "node.exe");
            if (File.Exists(side)) return side;
            var sys = FindOnPath("node.exe");
            return sys ?? "node.exe";
        }

        private static string FindOnPath(string exe)
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var d in path.Split(';'))
                {
                    if (d.Trim().Length == 0) continue;
                    var p = Path.Combine(d.Trim(), exe);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr h, int cmd);

        /// <summary>
        /// 把浏览器窗口提到最前。等用户登录时必须做这一步：
        /// 用户点完按钮如果浏览器在背后，他会以为程序没反应。
        /// </summary>
        public static void FocusBrowser() { FocusBrowser(0); }

        /// <summary>pid = 0 时退回"随便哪个浏览器窗口"。</summary>
        public static void FocusBrowser(int pid)
        {
            try
            {
                if (pid > 0)
                {
                    try
                    {
                        var p0 = Process.GetProcessById(pid);
                        if (p0 != null && p0.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(p0.MainWindowHandle, 9);   // SW_RESTORE
                            SetForegroundWindow(p0.MainWindowHandle);
                            return;
                        }
                    }
                    catch { }
                }
                foreach (var name in new[] { "chrome", "msedge" })
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            if (p.MainWindowHandle == IntPtr.Zero) continue;
                            ShowWindow(p.MainWindowHandle, 9);   // SW_RESTORE
                            SetForegroundWindow(p.MainWindowHandle);
                            return;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>没登录时最多等多久（秒）。配置里没写就用 4 分钟。</summary>
        public int LoginWaitSec()
        {
            int m = Cfg == null ? 0 : Cfg.LoginWaitMins;
            if (m <= 0) m = 4;
            if (m > 15) m = 15;
            return m * 60;
        }

        // ---------------- Chrome ----------------
        public string FindChrome()
        {
            if (!string.IsNullOrEmpty(Cfg.ChromePath) && File.Exists(Cfg.ChromePath)) return Cfg.ChromePath;
            var candidates = new List<string>();
            try
            {
                foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"))
                    {
                        if (k != null)
                        {
                            var v = k.GetValue("") as string;
                            if (!string.IsNullOrEmpty(v)) candidates.Add(v.Trim('"'));
                        }
                    }
                }
            }
            catch { }
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"));
            candidates.Add(Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"));
            candidates.Add(Path.Combine(local, @"Google\Chrome\Application\chrome.exe"));
            candidates.Add(Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"));
            candidates.Add(Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"));
            foreach (var c in candidates) if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
            return null;
        }

        public bool ChromeUp()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Cfg.Port + "/json/version");
                // 端口没人听就该立刻返回：以前用 1200ms，40 次轮询能白等 70 秒，
                // 用户点完「重开」只看到"正在打开浏览器…"一直转。
                req.Timeout = 350; req.Proxy = null;
                using (var r = (HttpWebResponse)req.GetResponse())
                using (var s = r.GetResponseStream())
                using (var sr = new StreamReader(s, Encoding.UTF8)) { sr.ReadToEnd(); }
                return true;
            }
            catch { return false; }
        }

        /// <summary>本程序专用浏览器的登录态目录（%LOCALAPPDATA%\AutoApply\browser-profile，见 Security.cs）。</summary>
        public string ProfilePath { get { return ProfileHome.Resolve(BaseDir, Cfg); } }

        /* ------------------------------------------------------------------
         * 9333 端口归属：连上去的那只浏览器，必须是用本程序登录态目录起的。
         * 为什么较真：脚本在"调试口上的那个浏览器"里点投递，如果端口被日常浏览器/旧副本占了，
         * 点击就会落在毫不相干的窗口里。确认不是我们的 → 拒绝跑，并把原因说清楚。
         * WMI 拿不到命令行（权限不足）时无法确认，只记一次日志放行，不阻塞正常使用。
         * ------------------------------------------------------------------ */
        public string ChromeOwnerProblem()
        {
            string profile = ProfilePath;
            if (_ownerCheckAt != DateTime.MinValue &&
                (DateTime.Now - _ownerCheckAt).TotalSeconds < 15 &&
                _ownerPort == Cfg.Port &&
                string.Equals(_ownerProfile, profile, StringComparison.OrdinalIgnoreCase))
                return _ownerProblem;

            bool confirmed;
            string detail;
            bool ours = BrowserOwner.Matches(profile, Cfg.Port, out confirmed, out detail);
            _ownerCheckAt = DateTime.Now;
            _ownerProfile = profile;
            _ownerPort = Cfg.Port;

            if (ours) { _ownerProblem = null; return null; }
            if (confirmed)
            {
                _ownerProblem = "端口 " + Cfg.Port + " 上跑的不是本程序的浏览器（它的配置目录是 " + detail +
                                "）。为免把「投递」点进别的窗口，已停下；请关掉那个浏览器，或在「平台与关键词」里换一个端口。";
                Program.Trace("ChromeOwner: " + _ownerProblem);
                return _ownerProblem;
            }

            _ownerProblem = null;
            if (_ownerWarned != profile)
            {
                _ownerWarned = profile;
                Program.Trace("ChromeOwner: 无法确认端口 " + Cfg.Port + " 的归属（WMI 没有给出命令行），按正常情况继续");
            }
            return null;
        }

        public bool EnsureChrome(out string msg)
        {
            lock (_chromeGate) { return EnsureChromeLocked(out msg); }
        }

        // ------------------------------------------------------------------
        // 起浏览器。以前这里只会说"浏览器启动后马上退出了"，真正的原因全靠猜；
        // 现在：① 先清掉"占着配置目录但没开调试口"的旧 Chrome；
        //       ② 把 Chrome 的 stderr / 退出码抓下来写进 autoapply.log；
        //       ③ 按实际原因给三种不同的说法。
        // ------------------------------------------------------------------
        private bool EnsureChromeLocked(out string msg)
        {
            msg = "";
            if (ChromeUp())
            {
                // 端口有人应 ≠ 那是我们的浏览器：先验归属，再决定能不能在它里面点投递
                string problem = ChromeOwnerProblem();
                if (problem != null) { msg = problem; LastChromeError = problem; return false; }
                msg = "浏览器已就绪（端口 " + Cfg.Port + "）";
                return true;
            }
            var chrome = FindChrome();
            if (chrome == null) { msg = "没找到 Chrome / Edge，请在「平台与关键词」里手动指定浏览器路径"; return false; }

            // 低完整性/不受信任的进程起不了 Chrome（crashpad 写不进配置目录，Chrome 以 0x80000003 退出）。
            // 与其每站白等两次重试，不如第一次就说清楚 —— 用户看到的一句话才是能行动的。
            string il = IntegrityLevel();
            if (il != "medium" && il != "high")
            {
                msg = "本程序当前是受限权限（" + il + "），自己启动浏览器会被系统拒绝。原因多半是 AutoApply.exe 所在文件夹被打了「低完整性」标签，" +
                      "右键 tools\\修复低权限标签.cmd →「以管理员身份运行」即可修复；浏览器已经在跑时，可以直接继续投递。";
                LastChromeError = msg;
                Program.Trace("EnsureChrome: 权限 " + il + "，直接放弃拉浏览器");
                return false;
            }
            var profile = ProfilePath;
            try { if (!Directory.Exists(profile)) Directory.CreateDirectory(profile); }
            catch (Exception ex) { Program.Trace("EnsureChrome: 建配置目录失败 " + ex.Message); }

            // 上一轮留下的 Chrome 还占着这份配置目录时，新进程只会把请求转交给它、然后自己退出，
            // 而它当初没带调试端口 —— 端口就永远起不来。先把"用着这份配置"的那几个关掉。
            int killed = KillChromeOnProfile(profile);
            if (killed > 0)
            {
                Program.Trace("EnsureChrome: 关掉了 " + killed + " 个占着配置目录但没开调试端口的 Chrome");
                Thread.Sleep(1000);
            }

            string why = "";
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                if (LaunchChrome(chrome, profile, out why))
                {
                    msg = "已打开浏览器（端口 " + Cfg.Port + "）";
                    FocusBrowser(_chromePid);
                    return true;
                }
                Program.Trace("EnsureChrome 第 " + attempt + " 次失败：" + why);
                if (attempt == 1)
                {
                    int k2 = KillChromeOnProfile(profile);
                    if (k2 > 0) { Program.Trace("EnsureChrome: 重试前再清掉 " + k2 + " 个 Chrome"); Thread.Sleep(1200); }
                    else Thread.Sleep(600);
                }
            }
            msg = why;
            Program.Trace("EnsureChrome 放弃：" + why);
            return false;
        }

        /// <summary>拉一个 Chrome 起来并等调试端口。失败时 why 里是能看懂的原因。</summary>
        private bool LaunchChrome(string chrome, string profile, out string why)
        {
            why = "";
            string first = "https://www.zhipin.com/web/geek/job";
            foreach (var s in Cfg.Sites) { if (s.Enabled) { first = s.Url; break; } }
            var args = "--user-data-dir=\"" + profile + "\" --remote-debugging-port=" + Cfg.Port +
                       " --no-first-run --no-default-browser-check --disable-component-update" +
                       " --disable-background-networking --disable-crash-reporter" +
                       " --window-size=1280,900 --window-position=180,60" +
                       " \"" + first + "\"";
            var err = new StringBuilder();
            Process proc;
            try
            {
                var psi = new ProcessStartInfo(chrome, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = BaseDir;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                proc = new Process();
                proc.StartInfo = psi;
                proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { Collect(err, e.Data); };
                proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { Collect(err, e.Data); };
                proc.Start();
                try { proc.BeginErrorReadLine(); proc.BeginOutputReadLine(); } catch { }
            }
            catch (Exception ex)
            {
                why = "启动浏览器失败：" + ex.Message;
                return false;
            }

            for (int i = 0; i < 75; i++)      // 最长 30 秒
            {
                Thread.Sleep(400);
                if (ChromeUp())
                {
                    try { if (!proc.HasExited) _chromePid = proc.Id; } catch { }
                    return true;
                }
                if (i < 2) continue;
                bool exited = false;
                try { exited = proc.HasExited; } catch { }
                if (!exited) continue;

                int code = -1;
                try { code = proc.ExitCode; } catch { }
                string tail = SnipText(err);
                string il = IntegrityLevel();
                Program.Trace("Chrome 退出：code=" + code + " 权限=" + il + " stderr=" + tail);
                if (tail.IndexOf("crash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tail.IndexOf("拒绝访问", StringComparison.Ordinal) >= 0)
                {
                    why = "Chrome 被系统拒绝启动（退出码 " + code + "，本程序权限：" + il + "）。" +
                          "这通常是本程序以受限权限跑起来的（沙箱 / 低完整性 / 计划任务），浏览器拉不起来。" +
                          "请关掉它，正常双击 AutoApply.exe 再点一次。";
                }
                else if (code == 0 && tail.Length == 0)
                {
                    why = "浏览器把启动请求交给了一个已经在跑的 Chrome，然后自己退出了" +
                          "（那个窗口没开调试端口 " + Cfg.Port + "）。已经自动清了一次还是这样 —— " +
                          "把任务栏里的 Chrome 窗口全部关掉，再点一次「重开」。";
                }
                else
                {
                    why = "Chrome 启动后立刻退出（退出码 " + code + (il != "unknown" ? "，本程序权限：" + il : "") + "）" +
                          (tail.Length > 0 ? "：" + tail : "");
                }
                return false;
            }
            why = "浏览器启动超时（30 秒没连上端口 " + Cfg.Port + "），检查一下 Chrome 是否被安全软件拦住";
            return false;
        }

        private static void Collect(StringBuilder sb, string line)
        {
            if (line == null || line.Length == 0) return;
            lock (sb) { if (sb.Length < 1600) sb.AppendLine(line); }
        }

        /// <summary>把 Chrome 的报错压成一行，别把整段 stderr 塞进提示里。</summary>
        private static string SnipText(StringBuilder sb)
        {
            string s;
            lock (sb) { s = sb.ToString(); }
            if (s.Length == 0) return "";
            var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string t = lines[i].Trim();
                int k = t.LastIndexOf("] ");
                if (k > 0 && k + 2 < t.Length) t = t.Substring(k + 2);
                return t.Length > 120 ? t.Substring(0, 120) : t;
            }
            string all = lines.Length > 0 ? lines[lines.Length - 1].Trim() : s.Trim();
            return all.Length > 120 ? all.Substring(0, 120) : all;
        }

        /// <summary>
        /// 关掉"命令行里带着我们这份配置目录"的 Chrome / Edge。
        /// 只认配置目录：用户平时那个 Chrome 用的是默认目录，绝不会被误杀。
        /// </summary>
        private static int KillChromeOnProfile(string profile)
        {
            // 交给 BrowserOwner：按 --user-data-dir 精确比较，不再用"命令行里含有这个字符串"
            // （旧写法在 owner=...\browser-profile、profile=...\browser-profile2 时会误杀）
            return BrowserOwner.KillBrowsersOn(profile);
        }

        // ---- 当前进程的完整性级别：low / untrusted 时 Chrome 一定拉不起来 ----
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr p, int acc, out IntPtr tok);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr tok, int cls, IntPtr buf, int len, out int ret);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, int idx);

        // ---- 系统里有没有 Chrome 在跑（不看调试端口）。给界面用：区分"没开浏览器"和"开着但不是那个窗口" ----
        private static bool _anyChromeSeen;
        private static DateTime _anyChromeAt = DateTime.MinValue;

        /// <summary>
        /// 开机自启浏览器：把「带调试端口的 Chrome」写进启动文件夹，由资源管理器在登录时拉起。
        /// 为什么要这样：程序自己（低权限进程）启动 Chrome 会被 crashpad 拒绝而自杀
        /// （0x80000003），但系统在登录时启动的没有这个问题。
        /// </summary>
        public string EnsureBrowserAutoStart()
        {
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (dir == null || dir.Length == 0) return "拿不到启动文件夹";
                string chrome = FindChrome();
                if (chrome.Length == 0) return "没找到 chrome.exe";
                string profile = ProfilePath;
                string path = Path.Combine(dir, "AutoApply 浏览器.cmd");
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("rem 由 AutoApply 生成：登录 Windows 后自动启动带调试端口的浏览器");
                sb.AppendLine("rem 删掉这个文件就不再开机自启。");
                sb.AppendLine("start \"\" \"" + chrome + "\" --user-data-dir=\"" + profile + "\" --remote-debugging-port=" + Cfg.Port + " --no-first-run --no-default-browser-check --window-size=1280,900 --window-position=180,60");
                sb.AppendLine("exit /b");
                File.WriteAllText(path, sb.ToString(), Encoding.Default);
                return path;
            }
            catch (Exception ex) { return "开机自启没设上：" + ex.Message; }
        }

        public static bool AnyChromeRunning()
        {
            if ((DateTime.Now - _anyChromeAt).TotalSeconds < 20) return _anyChromeSeen;
            bool found = false;
            try
            {
                using (var q = new ManagementObjectSearcher(
                    "SELECT ProcessId FROM Win32_Process WHERE Name='chrome.exe' OR Name='msedge.exe'"))
                using (var rs = q.Get())
                {
                    foreach (ManagementObject mo in rs) { found = true; break; }
                }
            }
            catch (Exception ex) { Program.Trace("AnyChromeRunning: " + ex.Message); }
            _anyChromeSeen = found;
            _anyChromeAt = DateTime.Now;
            return found;
        }

        public static string IntegrityLevel()
        {
            IntPtr tok = IntPtr.Zero, buf = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out tok)) return "unknown";
                int len;
                GetTokenInformation(tok, 25, IntPtr.Zero, 0, out len);
                if (len <= 0) return "unknown";
                buf = Marshal.AllocHGlobal(len);
                if (!GetTokenInformation(tok, 25, buf, len, out len)) return "unknown";
                IntPtr sid = Marshal.ReadIntPtr(buf);
                int cnt = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (cnt <= 0) return "unknown";
                int rid = Marshal.ReadInt32(GetSidSubAuthority(sid, cnt - 1));
                if (rid < 0x1000) return "untrusted";
                if (rid < 0x2000) return "low";
                if (rid < 0x3000) return "medium";
                return "high";
            }
            catch { return "unknown"; }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                if (tok != IntPtr.Zero) CloseHandle(tok);
            }
        }

        private List<string> ListUrls()
        {
            var urls = new List<string>();
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Cfg.Port + "/json/list");
                req.Timeout = 1500; req.Proxy = null;
                using (var r = (HttpWebResponse)req.GetResponse())
                using (var s = r.GetResponseStream())
                using (var sr = new StreamReader(s, Encoding.UTF8))
                {
                    var json = sr.ReadToEnd();
                    var ser = new JavaScriptSerializer();
                    var list = ser.Deserialize<List<Dictionary<string, object>>>(json);
                    foreach (var t in list)
                    {
                        object u; if (t.TryGetValue("url", out u) && u != null) urls.Add(u.ToString());
                    }
                }
            }
            catch { }
            return urls;
        }

        public bool EnsureTab(SiteCfg site)
        {
            var urls = ListUrls();
            foreach (var u in urls)
                if (u.IndexOf(site.Match, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            L("info", "打开 " + site.Name + " 标签页 …");
            RunNodeTool("cdp.js", "newtab " + site.Url, 20000);
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(900);
                foreach (var u in ListUrls())
                    if (u.IndexOf(site.Match, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            L("warn", site.Name + " 标签页没打开（脚本会跳过这一站）");
            return false;
        }

        /// <summary>
        /// 把这一站准备到"能跑"：浏览器没开就开，标签页没有就开。
        /// 「平台健康 → 重开」走的就是这条，所以它必须真能打开浏览器。
        /// </summary>
        public bool PrepareSite(SiteCfg s, out string msg)
        {
            msg = "";
            if (!EnsureChrome(out msg)) return false;
            if (!EnsureTab(s)) { msg = s.Name + " 的标签页没打开"; return false; }
            msg = "已打开 " + s.Name;
            return true;
        }

        /// <summary>
        /// 等用户把登录 / 滑块处理完再继续（浏览器已经打开、就在他眼前）。
        /// waitSec = 0 时只探一次，返回当前是否就绪。
        /// 页面没响应属于"死了"，不等；其它（未登录 / 验证码 / 还在加载）都等。
        /// </summary>
        public bool WaitSiteReady(SiteCfg s, int waitSec, out string reason)
        {
            reason = "";
            if (waitSec <= 0) return CheckSiteReady(s, out reason);
            var until = DateTime.Now.AddSeconds(waitSec);
            bool told = false;
            while (!_stop)
            {
                if (CheckSiteReady(s, out reason)) { if (told) L("ok", s.Name + " 已就绪，继续"); return true; }
                if (reason.IndexOf("页面没响应", StringComparison.Ordinal) >= 0) return false;
                if (!told)
                {
                    told = true;
                    L("warn", "!! " + s.Name + "：" + reason + "；浏览器已打开，处理好后会自动继续（最多等 " + (waitSec / 60) + " 分钟）");
                    FocusBrowser(_chromePid);
                    var at = SiteAttention;
                    if (at != null) { try { at(s.Name, reason + "（在浏览器里处理，程序会自动继续）"); } catch { } }
                }
                if (DateTime.Now > until) { L("warn", s.Name + " 等待超时（" + (waitSec / 60) + " 分钟），这一站先跳过"); return false; }
                Thread.Sleep(3000);
            }
            return false;
        }

        private string RunNodeTool(string script, string args, int timeoutMs)
        {
            return RunNodeTool(script, args, timeoutMs, null);
        }

        private string RunNodeTool(string script, string args, int timeoutMs, string cdpMatch)
        {
            try
            {
                var psi = new ProcessStartInfo(NodePath(), "--experimental-websocket \"" + Path.Combine(BinDir, script) + "\" " + args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = BinDir;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.EnvironmentVariables["CDP_PORT"] = Cfg.Port.ToString();
                psi.EnvironmentVariables["AUTOAPPLY_DATA"] = ScriptDir;
                if (!string.IsNullOrEmpty(cdpMatch)) psi.EnvironmentVariables["CDP_MATCH"] = cdpMatch;
                using (var p = Process.Start(psi))
                {
                    var so = p.StandardOutput.ReadToEndAsync();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return ""; }
                    if (so.Wait(2000)) return so.Result;
                }
            }
            catch { }
            return "";
        }

        // ---------------- 开跑前自检：登录态 / 验证码 ----------------
        /// <summary>在页面里判断：loading 页面还没渲染完 / ok / nologin 未登录 / loginurl 被踢到登录页 / captcha 有验证码。</summary>
        private const string READY_JS =
            "(()=>{var t=(document.body&&document.body.innerText)||'';var u=location.href;" +
            "if(/passport|\\/login|login\\.php/i.test(u)&&t.length<3000)return 'loginurl';" +
            "if(/滑动验证|访问验证|请按住滑块|拖动到最右边|安全验证/.test(t))return 'captcha';" +
            "if(t.length<200)return 'loading';" +
            "var needLogin=/扫码登录|登录\\/注册|立即登录|请先登录|登录后查看|登录查看/.test(t);" +
            "var loggedIn=/我的简历|在线简历|个人中心|我的申请|消息|退出登录|已投递/.test(t);" +
            "if(needLogin&&!loggedIn)return 'nologin';" +
            "return 'ok';})()";

        private bool CheckSiteReady(SiteCfg s, out string reason)
        {
            reason = "";
            for (int i = 0; i < 5; i++)
            {
                var r = RunNodeTool("cdp.js", "eval " + READY_JS, 15000, s.Match).Trim().Trim('"', '\'', '\r', '\n');
                if (r == "ok") return true;
                if (r == "loading") { Thread.Sleep(1500); continue; }
                if (r == "captcha") { reason = "页面上有验证码，需要人工拖一次滑块"; return false; }
                if (r == "nologin" || r == "loginurl") { reason = "还没登录"; return false; }
                if (r.Length == 0) { reason = "页面没响应（可能标签页挂了）"; return false; }
                return true;    // 其它返回值就当作可用，别误杀
            }
            reason = "页面一直加载不完，先跳过";
            return false;
        }

        /// <summary>
        /// 标签页挂死时自愈：关掉这个站的标签页，再开一个新的，等它加载。
        /// 今早（11-18）智联/前程就是因为标签页挂死被整站跳过，一行没投。
        /// </summary>
        private bool ReopenTab(SiteCfg s)
        {
            try
            {
                L("warn", s.Name + " 页面没响应 → 关掉标签页重开一次");
                RunNodeTool("cdp.js", "closetab " + s.Match, 15000, s.Match);
                Thread.Sleep(1200);
                RunNodeTool("cdp.js", "newtab " + s.Url, 15000, s.Match);
                Thread.Sleep(6000);
                return true;
            }
            catch (Exception ex)
            {
                L("warn", "重开标签页失败：" + ex.Message);
                return false;
            }
        }

        // ---------------- 运行 ----------------
        public void StartRun(List<SiteCfg> sites, string windowName)
        {
            lock (_lock)
            {
                if (Running) { L("warn", "已经在跑了，先等这一轮结束"); return; }
                Running = true;
                _stop = false;
                _loginWaitFailed = false;
            }
            RunStarted = DateTime.Now;
            RunWindowName = string.IsNullOrEmpty(windowName) ? "手动运行" : windowName;
            Results.Clear();
            Changed();

            var snapshot = new List<SiteCfg>();
            foreach (var s in sites) snapshot.Add(s);

            _worker = new Thread(() => Worker(snapshot));
            _worker.IsBackground = true;
            _worker.Start();
        }

        public void Stop()
        {
            _stop = true;
            try { if (_node != null && !_node.HasExited) KillTree(_node); } catch { }
            L("warn", "已请求停止");
            Changed();
        }

        /// <summary>
        /// 把这一轮每站的耗时/条数/结束原因写成 facts（runs.jsonl，一行一站）。
        /// 为什么要单独落这个文件：界面上的"每站耗时"和"失败原因排行"需要一个结构化来源，
        /// 不能靠正则去啃 task.log 的文本行。
        /// </summary>
        private void WriteRunLog()
        {
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < Results.Count; i++)
                {
                    var r = Results[i];
                    if (r.Started == DateTime.MinValue) continue;
                    var ended = r.Ended == DateTime.MinValue ? DateTime.Now : r.Ended;
                    var mins = Math.Max(0.0, (ended - r.Started).TotalMinutes);
                    sb.Append("{\"run\":\"").Append(RunStarted.ToString("yyyy-MM-ddTHH:mm:ss", inv)).Append('"');
                    sb.Append(",\"ts\":\"").Append(r.Started.ToString("yyyy-MM-ddTHH:mm:ss", inv)).Append('"');
                    sb.Append(",\"window\":\"").Append(JsonStr(RunWindowName)).Append('"');
                    sb.Append(",\"site\":\"").Append(JsonStr(r.Key)).Append('"');
                    sb.Append(",\"name\":\"").Append(JsonStr(r.Name)).Append('"');
                    sb.Append(",\"mins\":").Append(mins.ToString("0.0", inv));
                    sb.Append(",\"sent\":").Append(r.Sent.ToString(inv));
                    sb.Append(",\"stop\":\"").Append(JsonStr(r.Stop)).Append("\"}");
                    sb.Append('\n');
                }
                if (sb.Length == 0) return;
                File.AppendAllText(Path.Combine(ScriptDir, "runs.jsonl"), sb.ToString(), new System.Text.UTF8Encoding(false));
                Program.Trace("runs.jsonl appended rows=" + Results.Count);
                // 每轮结束后顺手只读采集一次「对方回没回」，失败不影响投递结果
                try
                {
                    L("sys", "=== 采集回复状态（只读，不发消息）");
                    RunNodeTool("replies.js", "--max 30", 180000);
                    Program.Trace("replies collected");
                }
                catch (Exception ex2) { Program.Trace("replies failed: " + ex2.Message); }
            }
            catch (Exception ex) { L("warn", "写运行记录失败：" + ex.Message); }
        }

        private static string JsonStr(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        private static void KillTree(Process p)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F");
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (var k = Process.Start(psi)) { k.WaitForExit(5000); }
            }
            catch { }
        }

        private void Worker(List<SiteCfg> sites)
        {
            try
            {
                L("sys", "=== 开始运行 · " + RunWindowName + " · " + RunStarted.ToString("yyyy-MM-dd HH:mm:ss"));
                string msg;
                Stage = "打开浏览器";
                bool chromeOk = EnsureChrome(out msg);
                Stage = chromeOk ? "准备标签页" : "";
                if (chromeOk)
                {
                    L("info", msg);
                    LastChromeError = "";
                    L("info", "提示：如果浏览器里还没登录四个平台，请先手动登录一次（登录会记住）");
                }
                else
                {
                    // 浏览器起不来，后面每一站都只会空转（开标签页还要各等 30 秒），直接收尾。
                    L("err", msg);
                    LastChromeError = msg;
                    var at0 = SiteAttention;
                    if (at0 != null) { try { at0("浏览器", msg); } catch { } }
                }

                int carry = 0;
                foreach (var s in sites)
                {
                    if (_stop) { L("warn", "已停止，跳过剩余的站"); break; }
                    if (!s.Enabled) continue;
                    if (!chromeOk)
                    {
                        var rb = new SiteResult
                        {
                            Key = s.Key, Name = s.Name, Started = DateTime.Now, Ended = DateTime.Now,
                            Stop = "nochrome", Needs = msg
                        };
                        Results.Add(rb);
                        L("warn", s.Name + " 跳过：" + msg);
                        Changed();
                        continue;
                    }
                    if (CappedToday(s))
                    {
                        var rc = new SiteResult { Key = s.Key, Name = s.Name, Started = DateTime.Now, Ended = DateTime.Now, Stop = "capped", Needs = "今日已达上限，本轮跳过" };
                        Results.Add(rc);
                        carry += s.Minutes;
                        L("warn", "!! " + s.Name + "：今天已经撞过上限（limit / 发不出去），本轮跳过，时间让给后面的站");
                        Changed();
                        continue;
                    }

                    CurrentSite = s.Key; CurrentSiteName = s.Name;
                    var sw = SiteSwith; if (sw != null) { try { sw(s.Key); } catch { } }
                    Changed();

                    var r = new SiteResult { Key = s.Key, Name = s.Name, Started = DateTime.Now };
                    Results.Add(r);

                    if (!EnsureTab(s))
                    {
                        r.Stop = "notab";
                        r.Ended = DateTime.Now;
                        L("warn", s.Name + " 跳过（没有可用标签页）");
                        Changed();
                        continue;
                    }

                    string reason;
                    bool ready = CheckSiteReady(s, out reason);
                    if (!ready && reason.IndexOf("页面没响应", StringComparison.Ordinal) >= 0)
                    {
                        if (ReopenTab(s) && EnsureTab(s)) ready = CheckSiteReady(s, out reason);
                        if (ready) L("ok", s.Name + " 重开标签页后恢复正常");
                    }
                    // 没登录 / 有滑块：浏览器就开在用户眼前，等他处理完再继续，
                    // 而不是像以前那样直接跳过这一站（重启电脑后最常撞的就是这条）。
                    if (!ready)
                    {
                        int waitSec = _loginWaitFailed ? 0 : LoginWaitSec();
                        Stage = waitSec > 0 ? ("等待登录：" + s.Name) : ("检查 " + s.Name);
                        ready = WaitSiteReady(s, waitSec, out reason);
                        if (!ready && waitSec > 0) _loginWaitFailed = true;
                    }
                    if (!ready)
                    {
                        r.Stop = reason.IndexOf("验证码", StringComparison.Ordinal) >= 0 ? "captcha"
                               : reason.IndexOf("页面没响应", StringComparison.Ordinal) >= 0 ? "tabdead" : "login";
                        r.Needs = reason;
                        r.Ended = DateTime.Now;
                        L("warn", "!! " + s.Name + "：" + reason + "（这一站跳过）");
                        var at = SiteAttention;
                        if (at != null) { try { at(s.Name, reason); } catch { } }
                        Changed();
                        continue;
                    }

                    Stage = "投递中：" + s.Name;
                    if (carry > 0)
                    {
                        int oldMin = s.Minutes;
                        s.Minutes = oldMin + carry;
                        L("info", s.Name + " 继承上一站让出的 " + carry + " 分钟（" + oldMin + " → " + s.Minutes + "）");
                        RunOneSite(s, r);
                        s.Minutes = oldMin;
                        carry = 0;
                    }
                    else RunOneSite(s, r);
                    if (r.Stop == "captcha" || r.Stop == "login")
                    {
                        r.Needs = r.Stop == "captcha" ? "页面上有验证码，需要人工拖一次滑块" : "未登录，需要人工登录一次";
                        L("warn", "!! " + s.Name + " 需要人工处理：" + r.Needs);
                        var at2 = SiteAttention;
                        if (at2 != null) { try { at2(s.Name, r.Needs); } catch { } }
                    }
                    r.Ended = DateTime.Now;
                    Changed();
                }

                WriteRunLog();
                FinishReport();
            }
            catch (Exception ex)
            {
                L("err", "运行异常：" + ex.Message);
                Program.Trace("run worker exception: " + ex.Message);
            }
            finally
            {
                if (Results.Count > 0) LastStop = Results[Results.Count - 1].Stop;
                Running = false;
                CurrentSite = ""; CurrentSiteName = "";
                var h = Log; if (h != null) { try { h("sys", "=== 本轮结束"); } catch { } }
                Changed();
            }
        }

        /// <summary>今天这一站是否已经撞过平台上限（limit / 发不出去）。</summary>
        private bool CappedToday(SiteCfg s)
        {
            try
            {
                string p = Path.Combine(ScriptDir, "runs.jsonl");
                if (!File.Exists(p)) return false;
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                string[] all = File.ReadAllLines(p);
                for (int i = all.Length - 1; i >= 0 && i > all.Length - 200; i--)
                {
                    string ln = all[i];
                    if (ln == null || ln.Length == 0) continue;
                    if (ln.IndexOf(today, StringComparison.Ordinal) < 0) continue;
                    if (ln.IndexOf("\"site\":\"" + s.Key + "\"", StringComparison.Ordinal) < 0) continue;
                    if (ln.IndexOf("\"stop\":\"limit\"", StringComparison.Ordinal) >= 0) return true;
                    if (ln.IndexOf("\"stop\":\"nosent\"", StringComparison.Ordinal) >= 0) return true;
                }
            }
            catch { }
            return false;
        }

        private void RunOneSite(SiteCfg s, SiteResult r)
        {
            var args = new StringBuilder();
            args.Append("--minutes ").Append(s.Minutes);
            args.Append(" --max ").Append(s.Max);
            args.Append(" --pace ").Append(s.Pace);
            if (Cfg.Keywords != null && Cfg.Keywords.Count > 0 && s.Key != "q51" && s.Key != "zp" && s.Key != "yjs")
                args.Append(" --keywords \"").Append(string.Join(",", Cfg.Keywords.ToArray())).Append("\"");

            L("info", "— " + s.Name + "：最多 " + s.Minutes + " 分钟 / " + s.Max + " 个");
            int sent = 0;
            try
            {
                var psi = new ProcessStartInfo(NodePath(), "--experimental-websocket \"" + Path.Combine(BinDir, s.Script) + "\" " + args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = BinDir;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.EnvironmentVariables["CDP_PORT"] = Cfg.Port.ToString();
                psi.EnvironmentVariables["AUTOAPPLY_DATA"] = ScriptDir;

                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += (a, b) => { if (b.Data != null) sent = OnLine(s, r, b.Data, sent); };
                    p.ErrorDataReceived += (a, b) => { if (b.Data != null) L("err", b.Data); };
                    p.Start();
                    _node = p;
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    var limit = DateTime.Now.AddMinutes(s.Minutes + 4);
                    while (!p.HasExited)
                    {
                        if (_stop) { KillTree(p); break; }
                        if (DateTime.Now > limit) { L("warn", s.Name + " 超时，结束这一站"); KillTree(p); break; }
                        Thread.Sleep(300);
                    }
                    try { p.WaitForExit(6000); } catch { }
                    _node = null;
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                L("err", s.Name + " 启动失败：" + ex.Message);
            }
        }

        private int OnLine(SiteCfg s, SiteResult r, string raw, int sent)
        {
            var line = raw.Trim();
            if (line.Length == 0) return sent;
            var text = line;
            // 去掉 `[2026/11/16 23:02:59] ` 前缀，自己加时间更统一
            if (text.StartsWith("["))
            {
                var end = text.IndexOf(']');
                if (end > 0 && end < 30) text = text.Substring(end + 1).Trim();
            }

            string level = "info";
            if (text.StartsWith("OK ")) { level = "ok"; sent++; r.Sent = sent; var eh = SiteSent; if (eh != null) { try { eh(s.Key, sent); } catch { } } }
            else if (text.StartsWith("SKIP")) { level = "dim"; r.Skipped++; }
            else if (text.StartsWith("!!")) { level = "warn"; r.Errs++; }
            else if (text.StartsWith("===")) { level = "sys"; ParseEnd(r, text); }
            else if (text.StartsWith("---")) level = "dim";

            L(level, text);
            return sent;
        }

        private static void ParseEnd(SiteResult r, string text)
        {
            // === Q51 end stop=limit sent=0 skipped=0 errs=0 elapsed=0min
            var stop = Grab(text, "stop=");
            if (stop.Length > 0) r.Stop = stop;
            var sent = Grab(text, "sent=");
            int v;
            if (int.TryParse(sent, out v) && v > r.Sent) r.Sent = v;
            var sk = Grab(text, "skipped=");
            if (int.TryParse(sk, out v)) r.Skipped = v;
            var er = Grab(text, "errs=");
            if (int.TryParse(er, out v)) r.Errs = v;
        }

        private static string Grab(string text, string key)
        {
            var i = text.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "";
            i += key.Length;
            var j = i;
            while (j < text.Length && !char.IsWhiteSpace(text[j])) j++;
            return text.Substring(i, j - i);
        }

        private void FinishReport()
        {
            try
            {
                L("info", "生成报告 report.md …");
                RunNodeTool("make-report.js", "", 20000);
            }
            catch { }
        }

        public int TotalSentInRun()
        {
            int n = 0;
            foreach (var r in Results) n += r.Sent;
            return n;
        }
    }
}
