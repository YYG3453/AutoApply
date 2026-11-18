using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace AutoApply
{
    /// <summary>
    /// 检查 GitHub 上有没有新版本。只用公开 API，失败就当"没检查到"，绝不弹错误框。
    /// </summary>
    internal static class Updater
    {
        public const string Repo = "YYG3453/AutoApply";
        public const string Page = "https://github.com/YYG3453/AutoApply/releases";

        public static string Latest = "";
        public static string Error = "";
        public static bool Checked;

        private static string Get(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "AutoApply/" + MainForm.Version;
                req.Timeout = 9000;
                req.ReadWriteTimeout = 9000;
                req.Accept = "application/vnd.github+json";
                using (var resp = req.GetResponse())
                using (var sr = new System.IO.StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception ex) { Error = ex.Message; return ""; }
        }

        /// <summary>同步查询最新 tag；返回 "1.8.0" 或空串。</summary>
        public static string Query()
        {
            Error = "";
            string json = Get("https://api.github.com/repos/" + Repo + "/releases/latest");
            string tag = TagOf(json);
            if (tag.Length == 0)
            {
                json = Get("https://api.github.com/repos/" + Repo + "/tags");
                tag = TagOf(json);
            }
            Latest = Clean(tag);
            Checked = true;
            return Latest;
        }

        private static string TagOf(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            int i = json.IndexOf("\"tag_name\"");
            if (i < 0) i = json.IndexOf("\"name\"");
            if (i < 0) return "";
            int a = json.IndexOf('"', i + 10);
            int b = json.IndexOf('"', a + 1);
            if (a < 0 || b <= a) return "";
            return json.Substring(a + 1, b - a - 1);
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (s.StartsWith("v") || s.StartsWith("V")) s = s.Substring(1);
            return s;
        }

        /// <summary>latest 是否比 current 新。</summary>
        public static bool IsNewer(string latest, string current)
        {
            int[] a = Parse(latest), b = Parse(current);
            if (a == null || b == null) return false;
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x > y) return true;
                if (x < y) return false;
            }
            return false;
        }

        private static int[] Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            var parts = v.Split('.');
            var list = new List<int>();
            for (int i = 0; i < parts.Length && i < 4; i++)
            {
                int n;
                int cut = parts[i].IndexOfAny(new[] { '-', '+' });
                string s = cut > 0 ? parts[i].Substring(0, cut) : parts[i];
                if (!int.TryParse(s, out n)) return null;
                list.Add(n);
            }
            return list.Count == 0 ? null : list.ToArray();
        }

        /// <summary>后台检查，结果回调到 UI 线程。</summary>
        public static void CheckAsync(Action<string> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string v = "";
                try { v = Query(); } catch { }
                if (done != null) { try { done(v); } catch { } }
            });
        }
    }
}
