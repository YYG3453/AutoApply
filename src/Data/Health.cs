using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AutoApply
{
    /// <summary>一个站点的体检结果。</summary>
    internal class SiteHealth
    {
        public string Key = "";
        public string Name = "";
        public bool HasTab;
        public int Tabs;
        public string Url = "";
        public string Login = "unknown";   // yes / no / unknown
        public bool Captcha;
        public string Note = "";
        public string Checked = "";
        public int Sent;
        public string Stop = "";
    }

    /// <summary>
    /// 平台体检：只读检查「有没有标签页 / 是否登录 / 有没有验证码」。
    /// 检查动作全部走 cdp.js（与投递脚本同一套 CDP 客户端），不碰账号设置。
    /// </summary>
    internal static class Health
    {
        /// <summary>
        /// 问本机 CDP 用的小客户端：**禁用代理自动探测** + 1.2 秒超时 + 不保活。
        /// 默认的 WebClient 会因为系统代理设置（开机后 WPAD 探测）每个请求多等 2 秒，
        /// 之前平台健康页一次重绘问三次，就卡成 4 秒。本机 127.0.0.1 永远不该走代理。
        /// </summary>
        private class FastWc : System.Net.WebClient
        {
            protected override System.Net.WebRequest GetWebRequest(Uri address)
            {
                var r = base.GetWebRequest(address);
                r.Timeout = 1200;
                var h = r as System.Net.HttpWebRequest;
                if (h != null) { h.ReadWriteTimeout = 1200; h.KeepAlive = false; h.Proxy = null; }
                return r;
            }
        }

        public static int Port = 9333;

        /// <summary>数据目录（runs.jsonl / state-*.jsonl 所在）。</summary>
        public static string ScriptDir = "";

        /// <summary>
        /// .js 代码目录（scripts\，随版本走）。不能拿数据目录去拼 evalfile.js：
        /// 便携版的数据目录是 data\，里面没有 js，检查会永远"页面没响应"。
        /// </summary>
        public static string BinDir = "";

        /// <summary>上一次检查为什么没拿到结果——给界面一句能看懂的话。</summary>
        public static string LastError = "";

        private static string BaseDir
        {
            get { return Path.GetDirectoryName(typeof(Health).Assembly.Location); }
        }

        /// <summary>读 /json/list，返回所有标签页的 URL。</summary>
        public static List<string> Tabs()
        {
            var list = new List<string>();
            try
            {
                using (var wc = new FastWc())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers.Add("Cache-Control", "no-cache");
                    var json = wc.DownloadString("http://127.0.0.1:" + Port + "/json/list");
                    var arr = new JavaScriptSerializer().DeserializeObject(json) as object[];
                    if (arr == null) return list;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var d = arr[i] as Dictionary<string, object>;
                        if (d == null) continue;
                        object t;
                        if (!d.TryGetValue("type", out t) || (t as string) != "page") continue;
                        object u;
                        if (d.TryGetValue("url", out u)) list.Add(u as string ?? "");
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>定位 node.exe：随包 node\node.exe → 同级 node.exe → PATH。</summary>
        private static string AssignedNode()
        {
            var bundled = Path.Combine(BaseDir, "node", "node.exe");
            if (File.Exists(bundled)) return bundled;
            var side = Path.Combine(BaseDir, "node.exe");
            if (File.Exists(side)) return side;
            return "node.exe";
        }

        public static bool ChromeAlive()
        {
            try
            {
                using (var wc = new FastWc())
                {
                    wc.Headers.Add("Cache-Control", "no-cache");
                    var s = wc.DownloadString("http://127.0.0.1:" + Port + "/json/version");
                    return s != null && s.IndexOf("Browser", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        /// <summary>跑一次 cdp.js 命令，成功返回 stdout，失败返回空并把原因记进 LastError。</summary>
        private static string RunTool(string script, string args, string match)
        {
            LastError = "";
            try
            {
                string codeDir = BinDir.Length > 0 ? BinDir : ScriptDir;
                string scriptPath = Path.Combine(codeDir, script);
                if (!File.Exists(scriptPath))
                {
                    LastError = "程序不完整：找不到 " + scriptPath;
                    return "";
                }
                var psi = new ProcessStartInfo(AssignedNode(), "--experimental-websocket \"" + scriptPath + "\" " + args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = codeDir;
                psi.EnvironmentVariables["CDP_PORT"] = Port.ToString();
                psi.EnvironmentVariables["CDP_MATCH"] = match;
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEnd();
                    var e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(20000))
                    {
                        try { p.Kill(); } catch { }
                        LastError = "检查超时（20 秒没回应）";
                        return "";
                    }
                    if (p.ExitCode != 0)
                    {
                        LastError = FirstLine(e);
                        return "";
                    }
                    return o.Trim();
                }
            }
            catch (Exception ex)
            {
                LastError = "node 起不来：" + ex.Message;
                return "";
            }
        }

        /// <summary>脚本报错只取第一行，去掉 ERR 前缀并截断，避免把整段英文栈贴到界面上。</summary>
        private static string FirstLine(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length == 0) return "";
            int nl = s.IndexOfAny(new char[] { '\r', '\n' });
            if (nl > 0) s = s.Substring(0, nl);
            if (s.StartsWith("ERR ", StringComparison.Ordinal)) s = s.Substring(4);
            if (s.Length > 110) s = s.Substring(0, 110) + "…";
            return s;
        }

        /// <summary>把 CDP/脚本的英文错误翻成一句能照做的中文。</summary>
        private static string Explain(string e)
        {
            if (e == null || e.Length == 0) return "";
            string s = e.ToLowerInvariant();
            if (s.IndexOf("timeout") >= 0) return "标签页卡住了 · 点「重开」重开这一站";
            if (s.IndexOf("no page matching") >= 0) return "标签页正在跳转 · 稍后再点「检查」";
            if (s.IndexOf("ws ") >= 0 || s.IndexOf("connect") >= 0) return "连不上这个标签页 · 点「重开」";
            if (s.IndexOf("node") >= 0 || s.IndexOf("找不到") >= 0) return e;
            return "检查失败：" + e;
        }

        public static SiteHealth Probe(SiteCfg s)
        {
            var h = new SiteHealth();
            h.Key = s.Key;
            h.Name = s.Name;
            h.Checked = DateTime.Now.ToString("HH:mm:ss");
            List<string> tabs = Tabs();
            int n = 0;
            string first = "";
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i] == null) continue;
                if (tabs[i].IndexOf(s.Match, StringComparison.OrdinalIgnoreCase) < 0) continue;
                n++;
                if (first.Length == 0) first = tabs[i];
            }
            h.Tabs = n;
            h.HasTab = n > 0;
            h.Url = first;
            if (!h.HasTab)
            {
                h.Note = "没有标签页";
                return h;
            }
            string raw = RunTool("evalfile.js", "probe-health.js", s.Match);
            if (raw.Length == 0)
            {
                // 标签页可能正在跳转（刚点「重开」时最常见），等一下再问一次
                System.Threading.Thread.Sleep(900);
                raw = RunTool("evalfile.js", "probe-health.js", s.Match);
            }
            if (raw.Length == 0)
            {
                string u = h.Url == null ? "" : h.Url.ToLowerInvariant();
                if (u.IndexOf("login") >= 0 || u.IndexOf("passport") >= 0)
                    h.Note = "未登录 · 点「重开」后在这个窗口里登录";
                else if (LastError.Length > 0)
                    h.Note = Explain(LastError);
                else
                    h.Note = "页面没响应（点「重开」重建标签页）";
                return h;
            }
            // cdp.js eval 会把返回值原样打印，这里可能是带引号的 JSON 字符串
            string json = raw.Trim();
            if (json.Length > 1 && json[0] == '"') json = json.Substring(1, json.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
            try
            {
                var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (d != null)
                {
                    object v;
                    if (d.TryGetValue("cap", out v) && v is bool) h.Captcha = (bool)v;
                    if (d.TryGetValue("login", out v) && v is bool) h.Login = ((bool)v) ? "no" : "yes";
                    if (d.TryGetValue("url", out v)) h.Url = v as string ?? h.Url;
                }
            }
            catch { h.Note = "结果解析失败"; }
            if (h.Captcha) h.Note = "需要人工拖滑块";
            else if (h.Login == "no") h.Note = "未登录";
            else if (h.Note.Length == 0) h.Note = "正常";
            return h;
        }

        /// <summary>重开某一站的标签页（关掉全部同名标签 + 开一个新的）。</summary>
        public static bool Reopen(SiteCfg s)
        {
            RunTool("cdp.js", "closetab " + s.Match, s.Match);
            System.Threading.Thread.Sleep(900);
            string r = RunTool("cdp.js", "newtab " + s.Url, s.Match);
            System.Threading.Thread.Sleep(5000);
            return r.Length > 0 || Tabs().Exists(delegate (string u) { return u != null && u.IndexOf(s.Match, StringComparison.OrdinalIgnoreCase) >= 0; });
        }

        public static string ChromeVersion()
        {
            try
            {
                using (var wc = new FastWc())
                {
                    wc.Headers.Add("Cache-Control", "no-cache");
                    var s = wc.DownloadString("http://127.0.0.1:" + Port + "/json/version");
                    int i = s.IndexOf("\"Browser\"");
                    if (i < 0) return "";
                    int a = s.IndexOf('"', i + 9);
                    int b = s.IndexOf('"', a + 1);
                    if (a > 0 && b > a) return s.Substring(a + 1, b - a - 1).Replace("Chrome/", "");
                }
            }
            catch { }
            return "";
        }
    }
}
