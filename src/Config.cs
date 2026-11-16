using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AutoApply
{
    internal class SiteCfg
    {
        public string Key = "boss";
        public string Name = "BOSS直聘";
        public string Script = "boss.js";
        public string Url = "https://www.zhipin.com/web/geek/job";
        public string Match = "zhipin";      // 用来在 /json/list 里找标签页
        public bool Enabled = true;
        public int Minutes = 30;
        public int Max = 200;
        public int Pace = 12;
        public int Hue = 0;                  // 0 jade 1 blue 2 gold 3 amber
    }

    internal class WindowCfg
    {
        public string Key = "am";
        public string Name = "上午场";
        public bool Enabled = true;
        public int StartHour = 9, StartMin = 30;
        public int EndHour = 10, EndMin = 30;
        public bool RanToday_ = false;

        public int StartMins { get { return StartHour * 60 + StartMin; } }
        public int EndMins { get { return EndHour * 60 + EndMin; } }
        public string Range { get { return string.Format("{0:00}:{1:00} – {2:00}:{3:00}", StartHour, StartMin, EndHour, EndMin); } }
    }

    internal class AppCfg
    {
        public List<SiteCfg> Sites = new List<SiteCfg>();
        public List<WindowCfg> Windows = new List<WindowCfg>();
        public List<string> Cities = new List<string>();
        public List<string> Keywords = new List<string>();
        public bool ScheduleEnabled = true;
        public int Port = 9333;
        public string ChromePath = "";
        public string ProfileDir = "browser-profile";
        public bool MinimizeToTray = true;

        public static string[] DefaultCities()
        {
            return new[] { "北京", "上海", "广州", "深圳", "成都", "杭州", "重庆", "西安", "苏州", "武汉", "南京", "天津", "郑州", "长沙", "东莞", "佛山", "宁波", "青岛", "合肥" };
        }

        public static AppCfg Default()
        {
            var c = new AppCfg();
            c.Cities.AddRange(DefaultCities());
            c.Keywords.AddRange(new[] { "实施工程师", "实施顾问", "软件实施", "交付实施", "技术支持工程师", "售后实施" });
            c.Sites.Add(new SiteCfg { Key = "boss", Name = "BOSS直聘", Script = "boss.js", Url = "https://www.zhipin.com/web/geek/job", Match = "zhipin", Hue = 0, Minutes = 34, Max = 400, Pace = 12 });
            c.Sites.Add(new SiteCfg { Key = "zp", Name = "智联招聘", Script = "zhaopin.js", Url = "https://www.zhaopin.com/", Match = "zhaopin", Hue = 1, Minutes = 8, Max = 80, Pace = 11 });
            c.Sites.Add(new SiteCfg { Key = "yjs", Name = "应届生求职", Script = "yingjiesheng.js", Url = "https://q.yingjiesheng.com/", Match = "yingjiesheng", Hue = 2, Minutes = 10, Max = 100, Pace = 8 });
            c.Sites.Add(new SiteCfg { Key = "q51", Name = "前程无忧", Script = "q51job.js", Url = "https://we.51job.com/pc/search?keyword=%E5%AE%9E%E6%96%BD%E5%B7%A5%E7%A8%8B%E5%B8%88", Match = "51job", Hue = 3, Minutes = 14, Max = 120, Pace = 9 });
            c.Windows.Add(new WindowCfg { Key = "am", Name = "上午场", StartHour = 9, StartMin = 30, EndHour = 10, EndMin = 30 });
            c.Windows.Add(new WindowCfg { Key = "pm", Name = "下午场", StartHour = 14, StartMin = 0, EndHour = 16, EndMin = 0 });
            return c;
        }

        // ---------- 持久化 ----------
        public static AppCfg Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var ser = new JavaScriptSerializer();
                    var c = ser.Deserialize<AppCfg>(json);
                    if (c != null && c.Sites != null && c.Sites.Count > 0)
                    {
                        if (c.Windows == null || c.Windows.Count == 0) c.Windows = Default().Windows;
                        if (c.Cities == null) c.Cities = new List<string>();
                        if (c.Keywords == null || c.Keywords.Count == 0) c.Keywords = Default().Keywords;
                        return c;
                    }
                }
            }
            catch { }
            var d = Default();
            d.Save(path);
            return d;
        }

        public void Save(string path)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, ser.Serialize(this), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    /// <summary>投递统计：直接读四个 state-*.jsonl 的行数与今日条数，不额外维护状态。</summary>
    internal class Stats
    {
        public Dictionary<string, int> TodayBySite = new Dictionary<string, int>();
        public Dictionary<string, int> TotalBySite = new Dictionary<string, int>();
        public Dictionary<string, int> TodayByCity = new Dictionary<string, int>();
        public int Today, Total;
        public string LastDay = "";

        public int CountToday(string key) { int v; return TodayBySite.TryGetValue(key, out v) ? v : 0; }
        public int CountTotal(string key) { int v; return TotalBySite.TryGetValue(key, out v) ? v : 0; }

        private static string TodayStr() { return DateTime.Now.ToString("yyyy-MM-dd"); }

        /// <summary>四个 state 文件：state.jsonl + state-zp.jsonl + state-yjs.jsonl + state-q51.jsonl。</summary>
        public static Stats Load(string scriptDir)
        {
            var s = new Stats();
            var files = new[] { "state.jsonl", "state-zp.jsonl", "state-yjs.jsonl", "state-q51.jsonl" };
            var keys = new[] { "boss", "zp", "yjs", "q51" };
            var day = TodayStr();
            for (int i = 0; i < files.Length; i++)
            {
                int today = 0, total = 0;
                var p = Path.Combine(scriptDir, files[i]);
                if (File.Exists(p))
                {
                    try
                    {
                        using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs, Encoding.UTF8))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (line.Trim().Length == 0) continue;
                                total++;
                                if (line.IndexOf(day, StringComparison.Ordinal) >= 0) today++;
                            }
                        }
                    }
                    catch { }
                }
                s.TodayBySite[keys[i]] = today;
                s.TotalBySite[keys[i]] = total;
                s.Today += today;
                s.Total += total;
            }
            s.LastDay = day;

            // 今日按城市（只统计 BOSS 的 state.jsonl 里的 city 字段，够用且轻量）
            try
            {
                var p = Path.Combine(scriptDir, "state.jsonl");
                if (File.Exists(p))
                {
                    using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.IndexOf(day, StringComparison.Ordinal) < 0) continue;
                            var city = Grab(line, "city");
                            if (city.Length == 0) continue;
                            int v; s.TodayByCity.TryGetValue(city, out v);
                            s.TodayByCity[city] = v + 1;
                        }
                    }
                }
            }
            catch { }
            return s;
        }

        private static string Grab(string line, string field)
        {
            var k = "\"" + field + "\":\"";
            var i = line.IndexOf(k, StringComparison.Ordinal);
            if (i < 0) return "";
            i += k.Length;
            var j = line.IndexOf('"', i);
            if (j < 0) return "";
            return line.Substring(i, j - i);
        }
    }
}
