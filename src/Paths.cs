using System;
using System.IO;

namespace AutoApply
{
    /// <summary>
    /// 目录约定：
    ///   BaseDir = exe 所在目录
    ///   BinDir  = exe\scripts（.js / cities.json 这些"代码"，随版本走）
    ///   DataDir = %LOCALAPPDATA%\AutoApply，或便携标记（AutoApply.data）旁的 data\（state-*.jsonl / runs.jsonl / rules.json / config.json / report.md）
    /// 为什么分开：程序会被复制、会被打包解压到 dist\ 下跑，数据要是还跟着 exe 走，
    /// 每换一个目录就"数据没了"一次（v1.8.3 在 dist 副本里跑就是这样）。
    /// 数据放在固定的用户目录，所有副本读同一份；第一次运行会把旧位置的数据搬过来。
    /// </summary>
    internal static class Paths
    {
        public static string BaseDir = "";
        public static string BinDir = "";
        public static string DataDir = "";
        /// <summary>config.json 的落地位置：跟着数据走，副本之间共享一份设置。</summary>
        public static string CfgPath = "";

        /// <summary>运行时数据：投递流水、跳过、回复、每轮事实、报告。</summary>
        private static readonly string[] DataFiles = new string[]
        {
            "state.jsonl", "state-zp.jsonl", "state-yjs.jsonl", "state-q51.jsonl",
            "state-skip.jsonl", "state-replies.jsonl", "runs.jsonl", "report.md"
        };

        /// <summary>静态资产：跟着版本走，每次都从 exe 旁边刷新，免得升级后城市表过期。</summary>
        private static readonly string[] StaticFiles = new string[] { "cities.json", "cities-zp.json" };

        public static void Init(string baseDir)
        {
            BaseDir = baseDir;
            BinDir = Path.Combine(baseDir, "scripts");
            DataDir = BinDir;
            CfgPath = Path.Combine(DataDir, "config.json");

            // 1) 便携模式：exe 往上三层里有 AutoApply.data 这个标记文件，数据就放标记旁的 data\
            //    用途：同一台机器上放多个副本（原目录 + dist\ 解压出来的），它们还能共用一份数据，
            //    而且不依赖 %LOCALAPPDATA%（受限账号、受限目录也能跑）。
            var portable = FindPortable(baseDir);
            if (portable != null && TryWrite(portable))
            {
                DataDir = portable;
                CfgPath = Path.Combine(DataDir, "config.json");
                Program.Trace("data dir = " + DataDir + " (portable marker)");
            }
            else
            {
                // 2) 常规位置：%LOCALAPPDATA%\AutoApply，所有副本共用一份
                try
                {
                    var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (!string.IsNullOrEmpty(root))
                    {
                        var d = Path.Combine(root, "AutoApply");
                        Directory.CreateDirectory(d);
                        // 真能写才算数，否则退回 exe 旁边（U 盘、只读目录之类）
                        var probe = Path.Combine(d, ".w");
                        File.WriteAllText(probe, "1");
                        File.Delete(probe);
                        DataDir = d;
                        CfgPath = Path.Combine(d, "config.json");
                    }
                }
                catch (Exception ex)
                {
                    Program.Trace("data dir fallback to scripts: " + ex.Message);
                }
            }

            if (DataDir != BinDir)
            {
                int n = 0;
                foreach (var f in DataFiles) if (Copy(Path.Combine(BinDir, f), Path.Combine(DataDir, f))) n++;
                if (Copy(Path.Combine(BinDir, "rules.json"), Path.Combine(DataDir, "rules.json"))) n++;
                foreach (var f in StaticFiles) Copy(Path.Combine(BinDir, f), Path.Combine(DataDir, f), true);
                Program.Trace("data dir = " + DataDir + " (migrated " + n + " files from scripts)");
            }

            // 配置也从 exe 旁边搬一份过来；搬过之后所有副本都用 DataDir 里的这一份
            try
            {
                var fromBase = Path.Combine(BaseDir, "config.json");
                if (!File.Exists(CfgPath) && File.Exists(fromBase))
                {
                    File.Copy(fromBase, CfgPath, false);
                    Program.Trace("config seeded from " + fromBase);
                }
            }
            catch (Exception ex) { Program.Trace("config seed failed: " + ex.Message); }
        }

        /// <summary>从 exe 目录往上找 AutoApply.data 标记；找到就返回它旁边的 data 目录。</summary>
        private static string FindPortable(string baseDir)
        {
            try
            {
                var d = new DirectoryInfo(baseDir);
                for (int i = 0; i < 3 && d != null; i++, d = d.Parent)
                {
                    if (File.Exists(Path.Combine(d.FullName, "AutoApply.data")))
                        return Path.Combine(d.FullName, "data");
                }
            }
            catch { }
            return null;
        }

        /// <summary>真写一个探针文件才算可写：只读目录、没有写权限的账号都会在这里被筛掉。</summary>
        private static bool TryWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".w");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                Program.Trace("data dir not writable: " + dir + " :: " + ex.Message);
                return false;
            }
        }

        /// <summary>把旧位置的文件搬一个过来；already=true 时按时间戳刷新。返回是否复制了。</summary>
        private static bool Copy(string src, string dst, bool always = false)
        {
            try
            {
                if (!File.Exists(src)) return false;
                if (File.Exists(dst))
                {
                    if (!always) return false;
                    var a = new FileInfo(src); var b = new FileInfo(dst);
                    if (a.LastWriteTimeUtc <= b.LastWriteTimeUtc && a.Length == b.Length) return false;
                }
                File.Copy(src, dst, true);
                return true;
            }
            catch (Exception ex) { Program.Trace("migrate " + Path.GetFileName(src) + " failed: " + ex.Message); return false; }
        }

        /// <summary>给日志/界面用：数据目录的短写法。</summary>
        public static string DataDirText
        {
            get
            {
                try
                {
                    var la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (!string.IsNullOrEmpty(la) && DataDir.StartsWith(la, StringComparison.OrdinalIgnoreCase))
                        return "%LOCALAPPDATA%" + DataDir.Substring(la.Length);
                }
                catch { }
                return DataDir;
            }
        }
    }
}
