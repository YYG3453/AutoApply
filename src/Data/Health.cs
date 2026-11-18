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

        private const string PROBE_JS =
            "(function(){var t=document.body?document.body.innerText:'';" +
            "var cap=/滑动验证|访问验证|请按住滑块|拖动到最右边|安全验证|验证码/.test(t);" +
            "var login=/扫码登录|登录\\/注册|立即登录|请先登录|登录后查看/.test(t);" +
            "var ok=/我的简历|在线简历|个人中心|我的申请|消息|退出登录|已投递|职位|搜索/.test(t);" +
            "return JSON.stringify({url:location.href,title:document.title,len:t.length,cap:cap,login:login&&!ok});})()";

        public static int Port = 9333;
        public static string ScriptDir = "";

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

        /// <summary>跑一次 cdp.js 命令，返回 (exit, stdout, stderr)。</summary>
        private static string RunTool(string script, string args, string match)
        {
            try
            {
                var psi = new ProcessStartInfo(AssignedNode(), "--experimental-websocket \"" + Path.Combine(ScriptDir, script) + "\" " + args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = ScriptDir;
                psi.EnvironmentVariables["CDP_PORT"] = Port.ToString();
                psi.EnvironmentVariables["CDP_MATCH"] = match;
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(20000))
                    {
                        try { p.Kill(); } catch { }
                        return "";
                    }
                    return p.ExitCode == 0 ? o.Trim() : "";
                }
            }
            catch { return ""; }
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
                h.Note = "页面没响应";
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
