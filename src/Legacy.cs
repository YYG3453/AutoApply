using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AutoApply
{
    internal class LegacyTask
    {
        public string Name = "";
        public string Status = "";
        public string Command = "";
        public bool Exists { get { return Status.Length > 0 && !string.Equals(Status, "notfound", StringComparison.OrdinalIgnoreCase); } }
        public bool Active { get { return string.Equals(Status, "Ready", StringComparison.OrdinalIgnoreCase) || string.Equals(Status, "Running", StringComparison.OrdinalIgnoreCase); } }
    }

    /// <summary>
    /// 收编旧版 boss-apply：禁用老的 Windows 计划任务、把老的投递记录并进来、必要时把老的登录态目录复制过来。
    /// 只做一次（用 logs\legacy-*.txt 做标记），失败只写日志，不挡启动。
    /// </summary>
    internal static class Legacy
    {
        public static readonly string[] TaskNames = { "BOSSTouDi-Morning", "BOSSTouDi-Afternoon", "BOSSChat-Reply" };
        private static readonly string[] StateFiles = { "state.jsonl", "state-zp.jsonl", "state-yjs.jsonl", "state-q51.jsonl" };

        public static string ResolveDir(string baseDir, string legacyDir)
        {
            try
            {
                if (string.IsNullOrEmpty(legacyDir)) return null;
                var p = Path.IsPathRooted(legacyDir) ? legacyDir : Path.GetFullPath(Path.Combine(baseDir, legacyDir));
                return Directory.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        private static int _lastExit;

        private static List<string> Run(string exe, string args, int timeoutMs)
        {
            var lines = new List<string>();
            try
            {
                var psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                psi.StandardOutputEncoding = Encoding.Default;
                psi.StandardErrorEncoding = Encoding.Default;
                using (var p = Process.Start(psi))
                {
                    var so = p.StandardOutput.ReadToEnd();
                    var se = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                    try { _lastExit = p.ExitCode; } catch { _lastExit = -1; }
                    foreach (var l in so.Split('\n')) lines.Add(l.TrimEnd('\r'));
                    foreach (var l in se.Split('\n')) lines.Add(l.TrimEnd('\r'));
                    var joined = string.Join(" | ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(System.Linq.Enumerable.Where(lines, x => x.Trim().Length > 0), 3)));
                    Program.Trace("legacy cmd: " + exe + " " + args + " exit=" + _lastExit + " lines=" + lines.Count + " :: " + joined);
                }
            }
            catch { }
            return lines;
        }

        /// <summary>查三个旧任务的状态；不存在时 Status = notfound。</summary>
        public static List<LegacyTask> Query()
        {
            var list = new List<LegacyTask>();
            foreach (var name in TaskNames)
            {
                var t = new LegacyTask { Name = name };
                var raw = string.Join("\n", Run("schtasks", "/query /tn \"\\" + name + "\" /fo LIST", 8000).ToArray());
                var low = raw.ToLowerInvariant();
                if (raw.Length > 2000) { t.Status = "unknown"; list.Add(t); continue; }
                foreach (var line in raw.Split('\n'))
                {
                    var l = line.Trim();
                    if (l.StartsWith("Task To Run:", StringComparison.OrdinalIgnoreCase)) t.Command = l.Substring(12).Trim();
                }
                if (_lastExit != 0)
                    t.Status = "unknown";   // 读不到（权限/服务不可用），不能当成"任务不存在"
                else if (low.Contains("disabled") || low.Contains("已禁用") || low.Contains("禁用"))
                    t.Status = "Disabled";
                else if (low.Contains("running") || low.Contains("正在运行"))
                    t.Status = "Running";
                else if (low.Contains("ready") || low.Contains("就绪"))
                    t.Status = "Ready";
                else t.Status = "unknown";
                list.Add(t);
            }
            return list;
        }

        public static bool Disable(string name)
        {
            var outp = Run("schtasks", "/change /tn \"\\" + name + "\" /disable", 8000);
            var s = string.Join(" ", outp.ToArray());
            return s.IndexOf("SUCCESS", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("成功", StringComparison.Ordinal) >= 0;
        }

        // ---------------- 投递记录合并 ----------------
        public static int ImportState(string legacyRoot, string scriptDir, out string detail)
        {
            var sb = new StringBuilder();
            int totalNew = 0;
            foreach (var f in StateFiles)
            {
                var src = Path.Combine(legacyRoot, f);
                var dst = Path.Combine(scriptDir, f);
                if (!File.Exists(src)) continue;

                var seen = new HashSet<string>(StringComparer.Ordinal);
                if (File.Exists(dst))
                {
                    try { foreach (var l in File.ReadAllLines(dst, Encoding.UTF8)) { var t = l.Trim(); if (t.Length > 0) seen.Add(t); } }
                    catch { }
                }

                var add = new StringBuilder();
                int added = 0;
                try
                {
                    foreach (var l in File.ReadAllLines(src, Encoding.UTF8))
                    {
                        var t = l.Trim();
                        if (t.Length == 0) continue;
                        if (!seen.Add(t)) continue;
                        add.Append(t).Append('\n');
                        added++;
                    }
                }
                catch { }

                if (added > 0)
                {
                    try
                    {
                        var needNl = false;
                        if (File.Exists(dst))
                        {
                            var fi = new FileInfo(dst);
                            if (fi.Length > 0)
                            {
                                using (var fs = new FileStream(dst, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                {
                                    fs.Seek(-1, SeekOrigin.End);
                                    needNl = fs.ReadByte() != '\n';
                                }
                            }
                        }
                        using (var fs = new FileStream(dst, FileMode.Append, FileAccess.Write, FileShare.Read))
                        using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                        {
                            if (needNl) sw.Write("\n");
                            sw.Write(add.ToString());
                        }
                    }
                    catch { }
                }
                sb.Append(f).Append(" +").Append(added).Append("  ");
                totalNew += added;
            }
            detail = sb.ToString().Trim();
            return totalNew;
        }

        // ---------------- 登录态目录迁移 ----------------
        /// <summary>本程序的 browser-profile 里还没有 cookie 时，把旧流程的 cp2 目录整体复制过来（省掉重新登录四个平台）。</summary>
        public static string MigrateProfile(string baseDir, string legacyRoot, string profileDir)
        {
            try
            {
                if (string.IsNullOrEmpty(profileDir)) return null;
                var src = Path.Combine(legacyRoot, "cp2");
                if (!Directory.Exists(src)) return null;
                var dst = Path.Combine(baseDir, profileDir);
                var dstCookies = Path.Combine(dst, "Default", "Network", "Cookies");
                if (File.Exists(dstCookies) && new FileInfo(dstCookies).Length > 2048) return null;   // 已经登录过了，不动
                if (!File.Exists(Path.Combine(src, "Default", "Network", "Cookies"))) return null;

                var psi = new ProcessStartInfo("robocopy",
                    "\"" + src + "\" \"" + dst + "\" /MIR /NFL /NDL /NJH /NJS /R:1 /W:1");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (var p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(120000);
                }
                if (File.Exists(dstCookies) && new FileInfo(dstCookies).Length > 2048) return "已把旧登录态复制到 " + profileDir;
                return null;
            }
            catch { return null; }
        }

        // ---------------- 总入口 ----------------
        public static void Takeover(MainForm f, Action<string, string> log, Action<string, string> notify)
        {
            var cfg = f.Cfg;
            var legacyRoot = ResolveDir(f.BaseDir, cfg.LegacyDir);

            // 1) 旧计划任务
            if (cfg.LegacyTakeover)
            {
                var tasks = Query();
                var closed = new List<string>();
                var unknown = new List<string>();
                foreach (var t in tasks)
                {
                    if (t.Status == "unknown") { unknown.Add(t.Name); continue; }
                    if (!t.Exists) continue;
                    if (t.Active)
                    {
                        if (Disable(t.Name)) { closed.Add(t.Name); log("sys", "已禁用旧计划任务：" + t.Name); }
                        else log("warn", "禁用旧计划任务失败：" + t.Name + "（" + t.Status + "）");
                    }
                    else log("dim", "旧计划任务已是 " + t.Status + "：" + t.Name);
                }
                if (unknown.Count > 0)
                {
                    log("warn", "读不到任务计划程序（权限不足），没能自动关掉旧任务：" + string.Join("、", unknown.ToArray()));
                    notify("没能自动关闭旧定时任务",
                        "读不到任务计划程序（权限不足）。如果旧任务还在跑会和本程序撞车：请在「任务计划程序」里禁用 BOSSTouDi-Morning / BOSSTouDi-Afternoon，或右键以管理员身份运行本程序一次。");
                }
                if (closed.Count > 0)
                    notify("已收编旧定时任务", "禁用了 " + closed.Count + " 个旧任务（" + string.Join("、", closed.ToArray()) + "），以后只由本程序投递");
            }

            if (legacyRoot == null)
            {
                log("dim", "没找到旧版目录（" + cfg.LegacyDir + "），跳过记录导入");
                return;
            }

            var logsDir = Path.Combine(f.BaseDir, "logs");
            try { if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir); } catch { }

            // 2) 历史记录导入（只做一次）
            var importedFlag = Path.Combine(logsDir, "legacy-import.txt");
            if (!File.Exists(importedFlag))
            {
                string detail;
                int n = ImportState(legacyRoot, f.Engine.ScriptDir, out detail);
                try
                {
                    File.WriteAllText(importedFlag,
                        "time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\nnew=" + n + "\r\n" + detail + "\r\n",
                        new UTF8Encoding(false));
                }
                catch { }
                log("sys", "已导入旧投递记录 " + n + " 条（" + detail + "）");
                if (n > 0) notify("历史记录已合并", "从旧版导入 " + n + " 条投递记录，仪表盘的累计数会对上");
            }

            // 3) 登录态目录（只在本程序还没登录过时做）
            var migrated = MigrateProfile(f.BaseDir, legacyRoot, cfg.ProfileDir);
            if (!string.IsNullOrEmpty(migrated)) notify("登录态已迁移", migrated + "，四个平台不用重新登录");
            else log("dim", "登录态目录无需迁移");
        }
    }
}
