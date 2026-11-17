using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AutoApply
{
    /// <summary>一条投递记录：从 state-*.jsonl 解析出来的只读事实。</summary>
    internal class JobRec
    {
        public string SiteKey = "";
        public string SiteName = "";
        public string Company = "";
        public string Title = "";
        public string Salary = "";
        public string City = "";
        public string Loc = "";
        public string Kw = "";
        public string Key = "";
        public DateTime Ts;
        public bool HasTs = true;
        public string Raw = "";

        public string TimeText { get { return HasTs ? Ts.ToString("MM-dd HH:mm") : "—"; } }
        public string DayText { get { return HasTs ? Ts.ToString("yyyy-MM-dd") : ""; } }
    }

    /// <summary>一条被规则拦下来的记录（来自 state-skip.jsonl，由 scripts\score.js 的调用方写入）。</summary>
    internal class SkipRec
    {
        public string SiteKey = "";
        public string SiteName = "";
        public string Title = "";
        public string Company = "";
        public string Loc = "";
        public string Kw = "";
        public int Score;
        public string Reason = "";
        public DateTime Ts;
        public bool HasTs = true;

        public string TimeText { get { return HasTs ? Ts.ToString("MM-dd HH:mm") : "—"; } }
        public string DayText { get { return HasTs ? Ts.ToString("yyyy-MM-dd") : ""; } }
    }

    /// <summary>
    /// 四个 state-*.jsonl 的读取层：一次解析 → 内存索引 → 页面只查索引。
    /// 为什么不用数据库：现在 1k 行量级，一行式解析 + 内存索引已经够快；
    /// 真到 5 万行以上、需要分页 join 或事务时再换 ESENT（见 README 的升级触发条件）。
    /// </summary>
    internal class RecordStore
    {
        public readonly List<JobRec> All = new List<JobRec>();

        public Dictionary<string, int> TodayBySite = new Dictionary<string, int>();
        public Dictionary<string, int> TotalBySite = new Dictionary<string, int>();
        public Dictionary<string, int> TodayByCity = new Dictionary<string, int>();
        /// <summary>按天统计（yyyy-MM-dd → 条数），四站合计，用于趋势图。</summary>
        public Dictionary<string, int> ByDay = new Dictionary<string, int>();

        public int Today, Total;
        public string LastDay = "";
        /// <summary>解析失败的行数（坏数据不该让整页崩）。</summary>
        public int BadLines;

        /// <summary>被规则过滤掉的岗位（state-skip.jsonl），新的在前。</summary>
        public readonly List<SkipRec> Skips = new List<SkipRec>();
        public int SkippedToday, SkippedTotal;
        public Dictionary<string, int> SkipByReason = new Dictionary<string, int>();

        public int CountToday(string key) { int v; return TodayBySite.TryGetValue(key, out v) ? v : 0; }
        public int CountTotal(string key) { int v; return TotalBySite.TryGetValue(key, out v) ? v : 0; }

        /// <summary>四个 state 文件；第三个元素是该站的中文名。</summary>
        private static readonly string[][] Files = new string[][]
        {
            new[] { "state.jsonl",     "boss", "BOSS直聘" },
            new[] { "state-zp.jsonl",  "zp",   "智联招聘" },
            new[] { "state-yjs.jsonl", "yjs",  "应届生"   },
            new[] { "state-q51.jsonl", "q51",  "前程无忧" },
        };

        public static RecordStore Load(string scriptDir)
        {
            var st = new RecordStore();
            var day = DateTime.Now.ToString("yyyy-MM-dd");
            st.LastDay = day;

            for (int i = 0; i < Files.Length; i++)
            {
                var key = Files[i][1];
                var name = Files[i][2];
                var path = Path.Combine(scriptDir, Files[i][0]);
                int today = 0, total = 0;

                if (File.Exists(path))
                {
                    try
                    {
                        // FileShare.ReadWrite：投递进程正在 append 时也能读
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs, Encoding.UTF8))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                line = line.Trim();
                                if (line.Length < 2 || line[0] != '{') { if (line.Length > 0) st.BadLines++; continue; }
                                JobRec r;
                                try { r = Parse(line, key, name); }
                                catch { st.BadLines++; continue; }
                                st.All.Add(r);
                                total++;
                                if (!r.HasTs) continue;
                                var d = r.Ts.ToString("yyyy-MM-dd");
                                int dv; st.ByDay.TryGetValue(d, out dv); st.ByDay[d] = dv + 1;
                                if (d == day)
                                {
                                    today++;
                                    if (r.City.Length > 0)
                                    {
                                        int cv; st.TodayByCity.TryGetValue(r.City, out cv); st.TodayByCity[r.City] = cv + 1;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                st.TodayBySite[key] = today;
                st.TotalBySite[key] = total;
                st.Today += today;
                st.Total += total;
            }

            // 被规则过滤的记录（可能不存在，那时 Skips 为空）
            st.LoadSkips(scriptDir, day);

            // 新的在前；没有时间戳的沉到最后
            st.All.Sort(delegate (JobRec a, JobRec b) { return b.Ts.CompareTo(a.Ts); });
            st.Skips.Sort(delegate (SkipRec a, SkipRec b) { return b.Ts.CompareTo(a.Ts); });
            return st;
        }

        private static string SiteNameOf(string key)
        {
            for (int i = 0; i < Files.Length; i++) if (Files[i][1] == key) return Files[i][2];
            return key;
        }

        private void LoadSkips(string scriptDir, string day)
        {
            var path = Path.Combine(scriptDir, "state-skip.jsonl");
            if (!File.Exists(path)) return;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length < 2 || line[0] != '{') { if (line.Length > 0) BadLines++; continue; }
                        try
                        {
                            var r = new SkipRec();
                            r.SiteKey = Str(line, "site");
                            r.SiteName = SiteNameOf(r.SiteKey.Length > 0 ? r.SiteKey : "");
                            r.Title = Str(line, "title");
                            r.Company = Str(line, "company");
                            r.Loc = Str(line, "loc");
                            if (r.Loc.Length == 0) r.Loc = Str(line, "area");
                            r.Kw = Str(line, "kw");
                            r.Reason = Str(line, "reason");
                            int.TryParse(Str(line, "score"), out r.Score);
                            var ts = Str(line, "ts");
                            if (ts.Length >= 10)
                            {
                                DateTimeOffset dto;
                                if (DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto)) r.Ts = dto.LocalDateTime;
                                else
                                {
                                    DateTime dt;
                                    if (DateTime.TryParse(ts.Substring(0, Math.Min(19, ts.Length)), out dt)) r.Ts = dt;
                                    else r.HasTs = false;
                                }
                            }
                            else r.HasTs = false;

                            Skips.Add(r);
                            SkippedTotal++;
                            // 原因归并：去掉冒号后面的具体词，便于做 Top 统计
                            var rk = r.Reason;
                            int c = rk.IndexOf('：');
                            if (c > 0) rk = rk.Substring(0, c);
                            int v; SkipByReason.TryGetValue(rk, out v); SkipByReason[rk] = v + 1;
                            if (r.HasTs && r.Ts.ToString("yyyy-MM-dd") == day) SkippedToday++;
                        }
                        catch { BadLines++; }
                    }
                }
            }
            catch { }
        }

        private static JobRec Parse(string line, string siteKey, string siteName)
        {
            var r = new JobRec();
            r.Raw = line;
            r.SiteKey = siteKey;
            r.SiteName = siteName;
            r.Key = Str(line, "id");
            if (r.Key.Length == 0) r.Key = Str(line, "key");
            r.Title = Str(line, "title");
            r.Company = Str(line, "company");
            r.Salary = Str(line, "salary");
            r.Kw = Str(line, "kw");
            r.City = Str(line, "city");
            r.Loc = Str(line, "loc");
            if (r.Loc.Length == 0) r.Loc = Str(line, "area");
            if (r.City.Length == 0) r.City = CityOf(r.Loc);

            var ts = Str(line, "ts");
            if (ts.Length >= 10)
            {
                DateTimeOffset dto;
                if (DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
                    r.Ts = dto.LocalDateTime;
                else
                {
                    DateTime dt;
                    if (DateTime.TryParse(ts.Substring(0, Math.Min(19, ts.Length)), out dt)) r.Ts = dt;
                    else r.HasTs = false;
                }
            }
            else r.HasTs = false;
            return r;
        }

        /// <summary>从 "杭州·滨江区·长河" / "重庆-渝中区..." / "北京 海淀" 里取城市。</summary>
        public static string CityOf(string loc)
        {
            if (string.IsNullOrEmpty(loc)) return "";
            loc = loc.Trim();
            var cut = loc.IndexOfAny(new[] { '·', '-', ' ', '\t', '/' });
            var s = cut > 0 ? loc.Substring(0, cut) : loc;
            s = s.Trim();
            return s.Length > 8 ? s.Substring(0, 8) : s;
        }

        /// <summary>容错的 JSON 取字段（只取顶层标量，够用且不依赖序列化器）。</summary>
        public static string Str(string line, string field)
        {
            var k = "\"" + field + "\":";
            int i = line.IndexOf(k, StringComparison.Ordinal);
            if (i < 0) return "";
            i += k.Length;
            while (i < line.Length && line[i] == ' ') i++;
            if (i >= line.Length) return "";
            if (line[i] == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < line.Length)
                {
                    char c = line[i];
                    if (c == '\\' && i + 1 < line.Length)
                    {
                        char n = line[i + 1];
                        if (n == 'n') sb.Append(' ');
                        else if (n == 'r' || n == 't') sb.Append(' ');
                        else if (n == 'u' && i + 5 < line.Length)
                        {
                            int code;
                            if (int.TryParse(line.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            { sb.Append((char)code); i += 4; }
                        }
                        else sb.Append(n);
                        i += 2;
                        continue;
                    }
                    if (c == '"') break;
                    sb.Append(c);
                    i++;
                }
                return sb.ToString().Trim();
            }
            int start = i;
            while (i < line.Length && line[i] != ',' && line[i] != '}') i++;
            var v = line.Substring(start, i - start).Trim();
            return v == "null" ? "" : v;
        }
    }
}
