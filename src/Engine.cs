using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

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

        private Thread _worker;
        private Process _node;
        private volatile bool _stop;
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
                req.Timeout = 1200; req.Proxy = null;
                using (var r = (HttpWebResponse)req.GetResponse())
                using (var s = r.GetResponseStream())
                using (var sr = new StreamReader(s, Encoding.UTF8)) { sr.ReadToEnd(); }
                return true;
            }
            catch { return false; }
        }

        public bool EnsureChrome(out string msg)
        {
            msg = "";
            if (ChromeUp()) { msg = "浏览器已就绪（端口 " + Cfg.Port + "）"; return true; }
            var chrome = FindChrome();
            if (chrome == null) { msg = "没找到 Chrome / Edge，请在设置里手动指定浏览器路径"; return false; }
            var profile = Path.Combine(BaseDir, Cfg.ProfileDir);
            if (!Directory.Exists(profile)) Directory.CreateDirectory(profile);
            var args = "--user-data-dir=\"" + profile + "\" --remote-debugging-port=" + Cfg.Port +
                       " --no-first-run --no-default-browser-check --disable-component-update" +
                       " --disable-background-networking --disable-crash-reporter" +
                       " --window-size=1280,900 --window-position=180,60" +
                       " \"https://www.zhipin.com/web/geek/job\"";
            try
            {
                var psi = new ProcessStartInfo(chrome, args);
                psi.UseShellExecute = false;
                psi.WorkingDirectory = BaseDir;
                Process.Start(psi);
            }
            catch (Exception ex) { msg = "启动浏览器失败：" + ex.Message; return false; }
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(500);
                if (ChromeUp()) { msg = "已启动浏览器"; return true; }
            }
            msg = "浏览器启动超时（端口 " + Cfg.Port + " 没响应）";
            return false;
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

        private bool EnsureTab(SiteCfg site)
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
                if (r == "nologin" || r == "loginurl") { reason = "还没登录，点「打开浏览器」登录一次就好"; return false; }
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
                if (!EnsureChrome(out msg)) L("err", msg);
                else L("info", msg);

                if (ChromeUp())
                {
                    L("info", "提示：如果浏览器里还没登录四个平台，请先手动登录一次（登录会记住）");
                }

                int carry = 0;
                foreach (var s in sites)
                {
                    if (_stop) { L("warn", "已停止，跳过剩余的站"); break; }
                    if (!s.Enabled) continue;
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
