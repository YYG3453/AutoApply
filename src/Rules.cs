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
        public int SchemaVersion = 1;
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

        public static string FilePath(string dir) { return Path.Combine(dir, "rules.json"); }

        public static RulesCfg Default()
        {
            var r = new RulesCfg();
            r.TitleInclude = new List<string> { "实施", "交付", "技术支持", "售后", "运维" };
            r.TitleExclude = new List<string> { "客服", "销售", "电话销售", "文员", "实习", "兼职", "普工", "司机", "仓管", "保洁", "保安", "收银" };
            r.CompanyBlacklist = new List<string> { "派遣", "外包", "劳务", "人力资源", "人力服务", "众包", "猎头" };
            r.SkillWords = new List<string> { "实施", "交付", "部署", "运维", "售后", "技术支持", "MES", "ERP", "SaaS", "WMS", "CRM", "数据库", "Linux", "驻场" };
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
            if (score > 100) score = 100;
            pass = score >= Threshold;
            var head = pass ? "通过 " + score + " 分" : "不通过 " + score + " 分（阈值 " + Threshold + "）";
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
