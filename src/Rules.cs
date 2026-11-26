using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AutoApply
{
    /// <summary>
    /// 匹配规则（scoring rules）。真正的运行期打分由 scripts\score.js 执行，
    /// 这里负责「读写 rules.json」和界面上的试算预览；两边逻辑保持一致（都很短）。
    /// </summary>
    internal class RulesCfg
    {
        public int SchemaVersion = 2;
        public int Threshold = 55;
        public bool TitleMust = true;

        public List<string> TitleInclude = new List<string>();
        public List<string> TitleExclude = new List<string>();
        public List<string> CompanyBlacklist = new List<string>();
        public List<string> SkillWords = new List<string>();

        public int BaseScore = 40;
        public int IncludeBonus = 25;
        public int SkillBonus = 4;
        public int SkillBonusCap = 16;
        public int KwBonus = 8;

        /// <summary>正文（JD）规则：BOSS / 智联 会先打开详情读正文再决定投不投。</summary>
        public List<string> JdExclude = new List<string>();
        public List<string> JdInclude = new List<string>();
        public int JdBonus = 3;
        public int JdBonusCap = 12;

        // ---- v1.10.0：硬性条件（在卡片信息上命中就直接跳过，和标题/公司黑名单同级）----
        /// <summary>学历要求里出现这些词就跳过（如「本科」「硕士」）。</summary>
        public List<string> EduExclude = new List<string>();
        /// <summary>经验要求里出现这些词就跳过（如「5-10年」「5年以上」）。</summary>
        public List<string> ExpExclude = new List<string>();
        /// <summary>福利/标签里出现这些词就跳过（如「单休」「996」）。</summary>
        public List<string> WelfareExclude = new List<string>();
        /// <summary>福利里出现这些词就加分。</summary>
        public List<string> WelfareInclude = new List<string>();
        public int WelfareBonus = 3;
        public int WelfareBonusCap = 9;
        /// <summary>只投这些城市；空 = 不限。</summary>
        public List<string> CityAllow = new List<string>();
        /// <summary>城市卡里自己加进来的城市（默认城市表之外的候选池）：只决定卡片显示哪些词，不参与筛选；点「不限」不会丢。</summary>
        public List<string> CityExtra = new List<string>();
        /// <summary>默认 19 城里被「×」删掉的（不参与筛选，只是想让它从卡片上消失）；「恢复默认」会清空。</summary>
        public List<string> CityHidden = new List<string>();
        /// <summary>命中这些城市直接跳过。</summary>
        public List<string> CityDeny = new List<string>();
        /// <summary>职位薪资上限低于它 → 跳过（K/月；0 = 不限）。</summary>
        public int SalaryMinK = 0;
        /// <summary>职位薪资下限高于它 → 跳过（K/月；0 = 不限）。</summary>
        public int SalaryMaxK = 0;

        public static string FilePath(string dir) { return Path.Combine(dir, "rules.json"); }

        public static RulesCfg Default()
        {
            var r = new RulesCfg();
            r.TitleInclude = new List<string> { "实施", "交付", "技术支持", "售后", "运维" };
            r.TitleExclude = new List<string> { "客服", "销售", "电话销售", "文员", "实习", "兼职", "普工", "司机", "仓管", "保洁", "保安", "收银" };
            r.CompanyBlacklist = new List<string> { "派遣", "外包", "劳务", "人力资源", "人力服务", "众包", "猎头" };
            r.SkillWords = new List<string> { "实施", "交付", "部署", "运维", "售后", "技术支持", "MES", "ERP", "SaaS", "WMS", "CRM", "数据库", "Linux", "驻场" };
            r.JdExclude = new List<string> { "电话销售", "外呼", "电销", "电话邀约", "招生", "课程顾问", "保险代理人", "无底薪", "纯提成" };
            r.JdInclude = new List<string> { "MySQL", "SQL", "Linux", "数据库", "部署", "驻场", "出差", "售后", "培训", "验收", "实施", "排查", "日志", "服务器" };
            r.EduExclude = new List<string> { "本科", "硕士", "博士", "研究生" };
            r.ExpExclude = new List<string> { "5-10年", "5年以上", "10年以上" };
            r.WelfareExclude = new List<string> { "单休", "大小周", "996", "夜班", "倒班" };
            r.WelfareInclude = new List<string> { "五险一金", "双休", "包吃", "包住", "年终奖", "13薪" };
            return r;
        }

        public static RulesCfg Load(string dir)
        {
            var path = FilePath(dir);
            try
            {
                if (File.Exists(path))
                {
                    var txt = File.ReadAllText(path, Encoding.UTF8);
                    if (txt.Trim().Length > 2)
                    {
                        var r = new JavaScriptSerializer().Deserialize<RulesCfg>(txt);
                        if (r != null)
                        {
                            if (r.TitleInclude == null) r.TitleInclude = new List<string>();
                            if (r.TitleExclude == null) r.TitleExclude = new List<string>();
                            if (r.CompanyBlacklist == null) r.CompanyBlacklist = new List<string>();
                            if (r.SkillWords == null) r.SkillWords = new List<string>();
                            if (r.JdExclude == null) r.JdExclude = new List<string>();
                            if (r.JdInclude == null) r.JdInclude = new List<string>();
                            if (r.JdBonus <= 0) r.JdBonus = 3;
                            if (r.JdBonusCap <= 0) r.JdBonusCap = 12;
                            if (r.EduExclude == null) r.EduExclude = new List<string>();
                            if (r.ExpExclude == null) r.ExpExclude = new List<string>();
                            if (r.WelfareExclude == null) r.WelfareExclude = new List<string>();
                            if (r.WelfareInclude == null) r.WelfareInclude = new List<string>();
                            if (r.CityAllow == null) r.CityAllow = new List<string>();
                            if (r.CityExtra == null) r.CityExtra = new List<string>();
                            if (r.CityHidden == null) r.CityHidden = new List<string>();
                            if (r.CityDeny == null) r.CityDeny = new List<string>();
                            // v1 规则文件里没有这四张表：升级时补默认词，否则新维度形同虚设
                            if (r.SchemaVersion < 2)
                            {
                                var d = Default();
                                if (r.EduExclude.Count == 0) r.EduExclude = d.EduExclude;
                                if (r.ExpExclude.Count == 0) r.ExpExclude = d.ExpExclude;
                                if (r.WelfareExclude.Count == 0) r.WelfareExclude = d.WelfareExclude;
                                if (r.WelfareInclude.Count == 0) r.WelfareInclude = d.WelfareInclude;
                            }
                            if (r.WelfareBonus <= 0) r.WelfareBonus = 3;
                            if (r.WelfareBonusCap <= 0) r.WelfareBonusCap = 9;
                            if (r.SalaryMinK < 0) r.SalaryMinK = 0;
                            if (r.SalaryMaxK < 0) r.SalaryMaxK = 0;
                            if (r.SchemaVersion < 2) r.SchemaVersion = 2;
                            return r;
                        }
                    }
                }
            }
            catch { }
            return Default();
        }

        /// <summary>原子写：先写 .tmp 落盘，再整体替换，旧文件留 .bak。</summary>
        public void Save(string dir)
        {
            var path = FilePath(dir);
            var tmp = path + ".tmp";
            var bak = path + ".bak";
            var json = new JavaScriptSerializer().Serialize(this);
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    sw.Write(json);
                    sw.Flush();
                    fs.Flush(true);
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

        public RulesCfg Clone()
        {
            var r = new RulesCfg();
            r.SchemaVersion = SchemaVersion; r.Threshold = Threshold; r.TitleMust = TitleMust;
            r.BaseScore = BaseScore; r.IncludeBonus = IncludeBonus;
            r.SkillBonus = SkillBonus; r.SkillBonusCap = SkillBonusCap; r.KwBonus = KwBonus;
            r.TitleInclude = new List<string>(TitleInclude);
            r.TitleExclude = new List<string>(TitleExclude);
            r.CompanyBlacklist = new List<string>(CompanyBlacklist);
            r.SkillWords = new List<string>(SkillWords);
            r.JdExclude = new List<string>(JdExclude);
            r.JdInclude = new List<string>(JdInclude);
            r.JdBonus = JdBonus; r.JdBonusCap = JdBonusCap;
            r.EduExclude = new List<string>(EduExclude);
            r.ExpExclude = new List<string>(ExpExclude);
            r.WelfareExclude = new List<string>(WelfareExclude);
            r.WelfareInclude = new List<string>(WelfareInclude);
            r.WelfareBonus = WelfareBonus; r.WelfareBonusCap = WelfareBonusCap;
            r.CityAllow = new List<string>(CityAllow);
            r.CityExtra = new List<string>(CityExtra);
            r.CityHidden = new List<string>(CityHidden);
            r.CityDeny = new List<string>(CityDeny);
            r.SalaryMinK = SalaryMinK; r.SalaryMaxK = SalaryMaxK;
            return r;
        }

        /// <summary>与 scripts\score.js 等价的试算（只用于界面预览）。</summary>
        public string Preview(string title, string company, string kw, out int score, out bool pass)
        {
            score = 0; pass = false;
            title = (title ?? "").Trim();
            company = (company ?? "").Trim();

            var bad = Hit(company, CompanyBlacklist);
            if (bad != null) { score = 0; return "公司黑名单：" + bad; }

            // 硬性条件：学历 / 经验 / 福利排除（试算只有标题+公司，没有城市和薪资，那两项放行）
            var info = (title + " " + company).Trim();
            var badEdu = Hit(info, EduExclude);
            if (badEdu != null) { score = 0; return "学历要求「" + badEdu + "」，已排除"; }
            var badExp = Hit(info, ExpExclude);
            if (badExp != null) { score = 0; return "经验要求「" + badExp + "」，已排除"; }
            var badWelfare = Hit(info, WelfareExclude);
            if (badWelfare != null) { score = 0; return "福利含「" + badWelfare + "」，已排除"; }

            if (title.Length == 0) { score = BaseScore; pass = true; return "无标题信息，放行（未评分）"; }

            var bx = Hit(title, TitleExclude);
            if (bx != null) { score = 0; return "标题含排除词：" + bx; }

            var inc = Hit(title, TitleInclude);
            score = BaseScore;
            var notes = new List<string>();
            if (inc != null) { score += IncludeBonus; notes.Add("标题命中 " + inc); }
            else if (TitleMust) { pass = false; return "标题不含指定词（" + score + " 分）"; }

            int sk = CountHits(title, SkillWords);
            if (sk > 0)
            {
                int add = Math.Min(SkillBonusCap, sk * SkillBonus);
                score += add;
                notes.Add("技能词 " + sk + " 个 +" + add);
            }
            if (kw.Length > 0 && title.IndexOf(kw, StringComparison.Ordinal) >= 0)
            {
                score += KwBonus;
                notes.Add("与关键词一致 +" + KwBonus);
            }
            int wf = CountHits(info, WelfareInclude);
            if (wf > 0)
            {
                int add = Math.Min(WelfareBonusCap, wf * WelfareBonus);
                score += add;
                notes.Add("福利 " + wf + " 项 +" + add);
            }
            if (score > 100) score = 100;
            pass = score >= Threshold;
            var head = pass ? "通过 " + score + " 分" : "不通过 " + score + " 分（阈值 " + Threshold + "）";
            if (CityAllow.Count > 0 || CityDeny.Count > 0 || SalaryMinK > 0 || SalaryMaxK > 0)
                head += "（试算不含城市/薪资，实际按卡片信息再筛）";
            return notes.Count > 0 ? head + " · " + string.Join(" / ", notes.ToArray()) : head;
        }

        private static string Hit(string text, List<string> words)
        {
            if (string.IsNullOrEmpty(text) || words == null) return null;
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                if (!string.IsNullOrEmpty(w) && text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return w;
            }
            return null;
        }

        private static int CountHits(string text, List<string> words)
        {
            int n = 0;
            if (string.IsNullOrEmpty(text) || words == null) return 0;
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                if (!string.IsNullOrEmpty(w) && text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) n++;
            }
            return n;
        }
    }
}
