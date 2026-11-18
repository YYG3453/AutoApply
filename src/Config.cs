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
        /// <summary>运行时状态，不落盘。</summary>
        [ScriptIgnore]
        public bool RanToday_ = false;

        // 下面三个是计算属性，只给界面用，绝不写进 config.json
        [ScriptIgnore]
        public int StartMins { get { return StartHour * 60 + StartMin; } }
        [ScriptIgnore]
        public int EndMins { get { return EndHour * 60 + EndMin; } }
        [ScriptIgnore]
        public string Range { get { return string.Format("{0:00}:{1:00} – {2:00}:{3:00}", StartHour, StartMin, EndHour, EndMin); } }
    }

    internal class AppCfg
    {
        /// <summary>
        /// 配置契约版本。改字段含义/结构时 +1，并在 Load 里写迁移分支。
        /// 2：计算属性移出落盘、加原子写与 .bak 备份。
        /// </summary>
        public int SchemaVersion = 2;

        public List<SiteCfg> Sites = new List<SiteCfg>();
        public List<WindowCfg> Windows = new List<WindowCfg>();
        public List<string> Cities = new List<string>();
        public List<string> Keywords = new List<string>();
        public bool ScheduleEnabled = true;
        public int Port = 9333;
        public string ChromePath = "";
        public string ProfileDir = "browser-profile";
        public bool MinimizeToTray = true;
        /// <summary>"night" = 夜晚模式（默认），"day" = 日间模式。</summary>
        public string Theme = "night";
        public int NightStyle = 4;
        /// <summary>旧版 boss-apply 目录（相对程序目录或绝对路径），用于收编计划任务/历史记录/登录态。</summary>
        public string LegacyDir = "..\\_automation\\boss-apply";
        /// <summary>启动时自动禁用旧的 BOSSTouDi-* 计划任务（避免和本程序重复投递）。</summary>
        public bool LegacyTakeover = true;
        /// <summary>出问题时弹托盘气泡（需要人工登录/过验证码时）。</summary>
        public bool Notify = true;
        public bool WizardDone = false;

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
            var c = TryRead(path);
            if (c == null) c = TryRead(path + ".bak");     // 主文件坏了就退回备份
            if (c != null)
            {
                if (c.Windows == null || c.Windows.Count == 0) c.Windows = Default().Windows;
                if (c.Cities == null) c.Cities = new List<string>();
                if (c.Keywords == null || c.Keywords.Count == 0) c.Keywords = Default().Keywords;
                return c;
            }
            var d = Default();
            d.Save(path);
            return d;
        }

        private static AppCfg TryRead(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length < 2) return null;
                var json = File.ReadAllText(path, Encoding.UTF8);
                var c = new JavaScriptSerializer().Deserialize<AppCfg>(json);
                if (c != null && c.Sites != null && c.Sites.Count > 0) return c;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 原子写：先写 .tmp，再整体替换，旧文件留成 .bak。
        /// 这样写到一半断电/崩溃，配置文件不会变成半截垃圾。
        /// </summary>
        public void Save(string path)
        {
            var json = new JavaScriptSerializer().Serialize(this);
            var dir = Path.GetDirectoryName(path);
            var tmp = path + ".tmp";
            var bak = path + ".bak";
            try
            {
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    sw.Write(json);
                    sw.Flush();
                    fs.Flush(true);                     // 落盘后再替换
                }
                if (File.Exists(path)) File.Replace(tmp, path, bak, true);
                else File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                try { File.WriteAllText(path, json, new UTF8Encoding(false)); } catch { }
            }
        }
    }
}
