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

    /// <summary>
    /// 一版招呼语。文本要靠人工贴到各平台账号设置里（平台在投递时自动带出，程序改不了正文），
    /// 程序负责的是记录「从什么时候起用的是这一版」，这样才能算每版回复率。
    /// </summary>
    internal class GreetCfg
    {
        public string Id = "A";
        public string Name = "";
        public string Text = "";
    }

    /// <summary>切换记录：从 From 这一刻起，投出去的岗位都属于 Id 这一版。</summary>
    internal class GreetSwitch
    {
        public string Id = "A";
        public DateTime From = DateTime.MinValue;
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

        /// <summary>
        /// 招呼语实验：平台侧的招呼语是账号设置里的一句固定话，投递时由平台自动带出，
        /// 程序改不了正文。所以这里只做三件事——存三版文本、记下每次切换的时间、
        /// 按时间把投递和回复对上号算各版回复率。
        /// </summary>
        public List<GreetCfg> Greetings = new List<GreetCfg>();
        public string GreetActive = "A";
        public List<GreetSwitch> GreetHistory = new List<GreetSwitch>();

        /// <summary>旧版 boss-apply 目录（相对程序目录或绝对路径），用于收编计划任务/历史记录/登录态。</summary>
        public string LegacyDir = "..\\_automation\\boss-apply";
        /// <summary>启动时自动禁用旧的 BOSSTouDi-* 计划任务（避免和本程序重复投递）。</summary>
        public bool LegacyTakeover = true;
        /// <summary>出问题时弹托盘气泡（需要人工登录/过验证码时）。</summary>
        public bool Notify = true;
        public bool WizardDone = false;

        /// <summary>v1.8.2 内置那版"现状"文案里和真实平台不一致的一句（旧文本，只用来识别"没改过"）。</summary>
        private const string OldA = "您好，我有洗浴管理软件实施售后经验，懂MySQL和Linux，能看日志排查，可接受出差，想应聘贵司实施岗，期待和您聊聊。";
        private const string OldB = "您好，我做水疗/洗浴类管理软件的实施和售后：上门部署、数据初始化、现场培训、上线验收、后续运维都是我一个人跟下来的，MySQL 和日志排查没问题，也能长期出差。看到贵司在招实施，想聊聊。";
        private const string OldC = "您好，想请教一下：贵司实施岗现在主要交付哪类系统、出差大概什么频率？我做过水疗/洗浴类管理软件的实施与售后，部署、培训、验收都能独立完成，MySQL 和看日志排查也熟。";

        /// <summary>平台账号里此刻真实在用的那句（2026-11-18 从智联投递弹窗里抄下来的原文）。</summary>
        public const string RealGreet = "您好，我有洗浴管理软件实施售后经验，懂MySQL，能看日志排查，可接受出差，想应聘贵司实施岗，期待和您聊聊。";

        /// <summary>新装只有一版，就是平台里现在真用的那句；其余版本由用户自己写。</summary>
        public static List<GreetCfg> DefaultGreetings()
        {
            var l = new List<GreetCfg>();
            l.Add(new GreetCfg { Id = "A", Name = "当前在用", Text = RealGreet });
            return l;
        }

        /// <summary>把 v1.8.2 内置的 B/C 文案（用户没改过）清掉，A 版换成平台里的真实原文。</summary>
        private void MigrateGreetings()
        {
            if (Greetings == null) return;
            for (int i = Greetings.Count - 1; i >= 0; i--)
            {
                var g = Greetings[i];
                if (g == null) { Greetings.RemoveAt(i); continue; }
                if (g.Id == "A" && g.Text == OldA)
                {
                    g.Text = RealGreet;
                    if (g.Name == "现状版（经验直达）") g.Name = "当前在用";
                    continue;
                }
                if ((g.Id == "B" && g.Text == OldB) || (g.Id == "C" && g.Text == OldC)) RemoveGreeting(g.Id);
            }
            if (Greetings.Count == 0)
            {
                Greetings = DefaultGreetings();
                GreetActive = "A";
                GreetHistory = new List<GreetSwitch>();
                GreetHistory.Add(new GreetSwitch { Id = "A", From = DateTime.Now });
            }
            if (GreetById(GreetActive) == null) GreetActive = Greetings[0].Id;
        }

        /// <summary>新版本编号：先用没占用的 A–Z，用完接 V1、V2…（编号不能重复，否则时间线算不准）。</summary>
        public string NextGreetId()
        {
            var used = new Dictionary<string, bool>();
            if (Greetings != null)
                for (int i = 0; i < Greetings.Count; i++)
                    if (Greetings[i] != null) used[Greetings[i].Id] = true;
            for (char ch = 'A'; ch <= 'Z'; ch++) { var id = ch.ToString(); if (!used.ContainsKey(id)) return id; }
            for (int i = 1; i < 999; i++) { var id = "V" + i; if (!used.ContainsKey(id)) return id; }
            return "V";
        }

        public GreetCfg AddGreeting(string name, string text)
        {
            if (Greetings == null) Greetings = new List<GreetCfg>();
            var g = new GreetCfg { Id = NextGreetId(), Name = name, Text = text };
            Greetings.Add(g);
            return g;
        }

        /// <summary>删一版：连它在时间线上的那一段一起删，那段投递会变成"未归属"（页面底部会显示条数）。</summary>
        public void RemoveGreeting(string id)
        {
            if (Greetings != null)
                for (int i = Greetings.Count - 1; i >= 0; i--)
                    if (Greetings[i] == null || Greetings[i].Id == id) Greetings.RemoveAt(i);
            if (GreetHistory != null)
                for (int i = GreetHistory.Count - 1; i >= 0; i--)
                    if (GreetHistory[i] == null || GreetHistory[i].Id == id) GreetHistory.RemoveAt(i);
            if (GreetActive == id) GreetActive = (Greetings != null && Greetings.Count > 0) ? Greetings[0].Id : "";
        }


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
            c.Greetings = DefaultGreetings();
            c.GreetActive = "A";
            c.GreetHistory.Add(new GreetSwitch { Id = "A", From = DateTime.Now });
            return c;
        }

        /// <summary>某个时刻生效的招呼语版本；早于时间线起点的记录返回空串（= 无法归属）。</summary>
        public string GreetAt(DateTime ts)
        {
            if (GreetHistory == null || GreetHistory.Count == 0) return "";
            string id = "";
            DateTime best = DateTime.MinValue;
            for (int i = 0; i < GreetHistory.Count; i++)
            {
                var h = GreetHistory[i];
                if (h == null) continue;
                if (h.From <= ts && h.From >= best) { best = h.From; id = h.Id; }
            }
            return id;
        }

        public GreetCfg GreetById(string id)
        {
            if (Greetings == null) return null;
            for (int i = 0; i < Greetings.Count; i++)
                if (Greetings[i] != null && Greetings[i].Id == id) return Greetings[i];
            return null;
        }

        /// <summary>切换当前版本，并把这次切换记进时间线（2 秒内重复点只覆盖最后一条）。</summary>
        public void SetGreetActive(string id)
        {
            GreetActive = id;
            if (GreetHistory == null) GreetHistory = new List<GreetSwitch>();
            var now = DateTime.Now;
            if (GreetHistory.Count > 0 && (now - GreetHistory[GreetHistory.Count - 1].From).TotalSeconds < 2)
                GreetHistory[GreetHistory.Count - 1] = new GreetSwitch { Id = id, From = GreetHistory[GreetHistory.Count - 1].From };
            else
                GreetHistory.Add(new GreetSwitch { Id = id, From = now });
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
                if (c.Greetings == null || c.Greetings.Count == 0)
                {
                    // 老配置没有招呼语：以"从今天起 A 生效"作为时间线起点，历史记录不会被误归到某一版
                    c.Greetings = DefaultGreetings();
                    c.GreetHistory = new List<GreetSwitch>();
                    c.GreetHistory.Add(new GreetSwitch { Id = "A", From = DateTime.Now });
                    c.GreetActive = "A";
                }
                if (c.GreetHistory == null || c.GreetHistory.Count == 0)
                {
                    c.GreetHistory = new List<GreetSwitch>();
                    c.GreetHistory.Add(new GreetSwitch { Id = string.IsNullOrEmpty(c.GreetActive) ? "A" : c.GreetActive, From = DateTime.Now });
                }
                if (string.IsNullOrEmpty(c.GreetActive)) c.GreetActive = c.GreetHistory[c.GreetHistory.Count - 1].Id;
                c.MigrateGreetings();      // v1.8.2 内置的 B/C 清掉，A 换成平台里真实那句
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
