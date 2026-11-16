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
        public DateTime Started;
        public DateTime Ended;
        public int Elapsed { get { return Math.Max(0, (int)(Ended - Started).TotalSeconds); } }
    }

    /// <summary>投递引擎：找/起 Chrome → 保证标签页 → 顺序跑四个平台的 node 脚本 → 汇总报告。</summary>
    internal class Engine
    {
        public readonly string BaseDir;
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

        private Thread _worker;
        private Process _node;
        private volatile bool _stop;
        private readonly object _lock = new object();

        public Engine(string baseDir)
        {
            BaseDir = baseDir;
            ScriptDir = Path.Combine(baseDir, "scripts");
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
            try
            {
                var psi = new ProcessStartInfo(NodePath(), "--experimental-websocket \"" + script + "\" " + args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = ScriptDir;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.EnvironmentVariables["CDP_PORT"] = Cfg.Port.ToString();
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

                foreach (var s in sites)
                {
                    if (_stop) { L("warn", "已停止，跳过剩余的站"); break; }
                    if (!s.Enabled) continue;

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

                    RunOneSite(s, r);
                    r.Ended = DateTime.Now;
                    Changed();
                }

                FinishReport();
            }
            catch (Exception ex)
            {
                L("err", "运行异常：" + ex.Message);
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
                var psi = new ProcessStartInfo(NodePath(), "--experimental-websocket \"" + s.Script + "\" " + args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = ScriptDir;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.EnvironmentVariables["CDP_PORT"] = Cfg.Port.ToString();

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
