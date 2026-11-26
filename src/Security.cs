using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace AutoApply
{
    /// <summary>
    /// 浏览器登录态目录（browser-profile）的落脚点、权限与一次性搬移。
    ///
    /// 为什么不能放在程序目录：① 程序目录会被打包、拷贝、同步，683MB 的登录态跟着走；
    /// ② 程序目录的默认 ACL 允许同机其他账户读取，等于把四个招聘账号的登录态摆在桌面上。
    /// 所以 v1.10 起固定放到**数据目录**下的 browser-profile —— 便携版就是 exe 旁的 data\（跟程序同一个盘，
    /// 同盘仅改名搬移，不额外占 C 盘空间；数据目录在 C 盘的非便携版则落 %LOCALAPPDATA%\AutoApply），
    /// 并且只授当前用户（SYSTEM / Administrators 保留，出问题还能修）。旧版本留在程序目录的那份会自动搬过来，
    /// 搬完校验 Cookies 再删旧目录 —— 失败就继续用旧位置，绝不把用户搞成"要重新登录"。
    /// </summary>
    internal static class ProfileHome
    {
        public const string DefaultName = "browser-profile";

        private static string _root;          // %LOCALAPPDATA%\AutoApply（不可写时退回数据目录）
        private static string _active;        // 本进程实际使用的登录态目录
        private static bool _aclDone;

        /// <summary>缓存目录：登录态用不到，搬移时跳过（本机实测能省掉 460MB / 2 的搬运量）。</summary>
        private static readonly string[] SkipDirs =
        {
            "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache",
            "GraphiteDawnCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache",
            "Service Worker", "Crashpad", "BrowserMetrics", "component_crx_cache", "CacheStorage"
        };

        public static string Root()
        {
            if (!string.IsNullOrEmpty(_root)) return _root;
            // 便携版/绿色版：登录态放数据目录里 —— 跟程序同一个盘，不额外占 C 盘空间。
            // 数据目录默认是 exe 旁边（有 AutoApply.data 标记）或 %LOCALAPPDATA%\AutoApply。
            try
            {
                string dd = Paths.DataDir;
                if (!string.IsNullOrEmpty(dd) && Writable(dd)) { _root = dd; return _root; }
            }
            catch (Exception ex) { Program.Trace("ProfileHome.Root: " + ex.Message); }
            try
            {
                string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(la))
                {
                    string d = Path.Combine(la, "AutoApply");
                    if (Writable(d)) { _root = d; return _root; }
                }
            }
            catch (Exception ex) { Program.Trace("ProfileHome.Root: " + ex.Message); }
            // 极少见的受限环境（数据目录不可写）：退回程序目录，功能不受影响，只是没那么隐蔽
            _root = Paths.DataDir.Length > 0 ? Paths.DataDir : AppDomain.CurrentDomain.BaseDirectory;
            return _root;
        }

        /// <summary>登录态目录的绝对路径。Cfg.ProfileDir 是相对名（默认 browser-profile）；绝对路径视为用户自己指定，不搬。</summary>
        public static string Resolve(string baseDir, AppCfg cfg)
        {
            if (!string.IsNullOrEmpty(_active)) return _active;
            string dir = (cfg == null || string.IsNullOrEmpty(cfg.ProfileDir)) ? DefaultName : cfg.ProfileDir.Trim();
            try
            {
                if (Path.IsPathRooted(dir)) return Path.GetFullPath(dir);
                return Path.Combine(Root(), dir);
            }
            catch
            {
                return Path.Combine(baseDir ?? "", dir);
            }
        }

        /// <summary>
        /// 启动时调用一次：必要时把旧位置（程序目录）的登录态搬过来，建目录、收紧 ACL。
        /// note 非空时应当提示给用户（成功搬移 / 失败退回 / 残留目录）。
        /// </summary>
        public static string Ensure(string baseDir, AppCfg cfg, out string note)
        {
            note = "";
            string target = Resolve(baseDir, cfg);
            string legacy = LegacyPath(baseDir, cfg);

            if (!string.IsNullOrEmpty(legacy) && !SamePath(legacy, target) && Directory.Exists(legacy))
            {
                if (HasLogin(target))
                {
                    // 新位置已经有登录态，旧目录只是搬移后的残留（本程序专用浏览器配置，删掉不会丢别的）。
                    // 搬移当时若被占用没删成，这里补删；删不掉才提示用户。
                    if (TryDelete(legacy)) Program.Trace("ProfileHome: 清理旧登录态目录 " + legacy);
                    else note = "旧登录态目录 " + Short(legacy) + " 删不掉（可能被占用），可以手动删掉。";
                }
                else if (HasLogin(legacy))
                {
                    string moveNote;
                    if (MoveProfile(legacy, target, out moveNote)) note = moveNote;
                    else { note = moveNote; _active = legacy; return legacy; }   // 搬失败：本次继续用旧位置，下次启动再试
                }
            }

            try { if (!Directory.Exists(target)) Directory.CreateDirectory(target); }
            catch (Exception ex) { Program.Trace("ProfileHome.Ensure: 建目录失败 " + ex.Message); }
            HardenAcl(target, false);
            _active = target;
            return target;
        }

        /// <summary>用 Cookies 的大小判断"这份目录里有没有登录态"（和旧迁移逻辑同一个判据）。</summary>
        public static bool HasLogin(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            string[] rels = { @"Default\Network\Cookies", @"Default\Cookies", @"Network\Cookies" };
            foreach (var rel in rels)
            {
                try
                {
                    var f = Path.Combine(dir, rel);
                    if (File.Exists(f) && new FileInfo(f).Length > 2048) return true;
                }
                catch { }
            }
            return false;
        }

        public static string Short(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(la) && path.StartsWith(la, StringComparison.OrdinalIgnoreCase))
                    return "%LOCALAPPDATA%" + path.Substring(la.Length);
            }
            catch { }
            return path;
        }

        // ------------------------------------------------------------------
        // 搬移
        // ------------------------------------------------------------------
        private static bool MoveProfile(string legacy, string target, out string note)
        {
            note = "";
            try
            {
                int killed = BrowserOwner.KillBrowsersOn(legacy);
                if (killed > 0)
                {
                    Program.Trace("ProfileHome: 先关掉 " + killed + " 个占着旧登录态目录的浏览器进程");
                    Thread.Sleep(1500);
                }

                // 首选：同一个盘上直接改名 —— 不复制、不占额外空间（683MB 也只是挪一下目录项）。
                // 只有数据目录在别的盘（或改名被占用打断）才退回 robocopy 复制。
                string how = "";
                bool moved = false;
                try
                {
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    Directory.Move(legacy, target);
                    how = "已移入";
                    moved = true;
                }
                catch (Exception ex)
                {
                    Program.Trace("ProfileHome: 同盘移动失败，改用复制 —— " + ex.Message);
                }

                if (!moved)
                {
                    Directory.CreateDirectory(target);
                    HardenAcl(target, false);

                    var args = new StringBuilder();
                    args.Append("\"").Append(legacy).Append("\" \"").Append(target).Append("\"");
                    args.Append(" /E /NFL /NDL /NJH /NJS /R:1 /W:1 /MT:8 /XD");
                    foreach (var d in SkipDirs) args.Append(" \"").Append(d).Append("\"");

                    var psi = new ProcessStartInfo("robocopy", args.ToString());
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    int code = -1;
                    using (var p = Process.Start(psi))
                    {
                        p.StandardOutput.ReadToEnd();
                        p.StandardError.ReadToEnd();
                        p.WaitForExit(600000);
                        try { code = p.ExitCode; } catch { }
                    }
                    if (!HasLogin(target))
                        throw new Exception("复制完成但新位置没有登录态（robocopy " + code + "）");
                    how = "已复制到";
                }

                if (!HasLogin(target)) throw new Exception("搬移后新位置没有登录态");

                // 连同子文件一起收紧（Chrome 自己建的 Cookies 等是从程序目录继承来的 Users 读权限）。
                // 同盘移动时子文件保留旧 ACL，所以这里必须递归；一次就够，acls 会留在目录上。
                HardenAcl(target, true);

                // 校验通过才删旧的：同盘移动已经没有旧目录；复制路径删不掉也不算失败
                if (Directory.Exists(legacy) && !TryDelete(legacy))
                    Program.Trace("ProfileHome: 旧目录删不掉（可能仍被占用），留着不影响使用：" + legacy);

                note = (killed > 0 ? "已关掉占着旧登录态的浏览器窗口；" : "") +
                       "登录态" + how + " " + Short(target) + "，四个平台不用重新登录。";
                Program.Trace("ProfileHome: 迁移成功 " + legacy + " -> " + target);
                return true;
            }
            catch (Exception ex)
            {
                Program.Trace("ProfileHome: 迁移失败 " + ex);
                // 失败时把复制了一半的目标清掉，免得每启动一次就多占一份空间
                try { if (Directory.Exists(target) && !HasLogin(target)) Directory.Delete(target, true); } catch { }
                note = "登录态搬移失败（" + ex.Message + "），本次继续用旧位置 " + Short(legacy) + "；下次启动会再试。";
                return false;
            }
        }

        private static bool TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                return true;
            }
            catch (Exception ex) { Program.Trace("ProfileHome: 删除 " + dir + " 失败 " + ex.Message); return false; }
        }

        /// <summary>旧版本的位置：exe 旁边的 browser-profile。绝对路径不参与迁移。</summary>
        private static string LegacyPath(string baseDir, AppCfg cfg)
        {
            try
            {
                string dir = (cfg == null || string.IsNullOrEmpty(cfg.ProfileDir)) ? DefaultName : cfg.ProfileDir.Trim();
                if (Path.IsPathRooted(dir)) return null;
                return Path.GetFullPath(Path.Combine(baseDir, dir));
            }
            catch { return null; }
        }

        private static void HardenAcl(string dir, bool recurse)
        {
            if (_aclDone && !recurse) return;
            try
            {
                var sid = WindowsIdentity.GetCurrent().User;
                if (sid == null) return;
                // /inheritance:r 去掉从程序目录继承来的 Users 读权限；只留当前用户 + SYSTEM + 管理员
                string args = "\"" + dir + "\" /inheritance:r" +
                              " /grant:r \"*" + sid.Value + ":(OI)(CI)F\"" +
                              " \"*S-1-5-18:(OI)(CI)F\" \"*S-1-5-32-544:(OI)(CI)F\"" +
                              (recurse ? " /T" : "");
                var psi = new ProcessStartInfo("icacls", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    if (p.ExitCode != 0) Program.Trace("ProfileHome: icacls 退出 " + p.ExitCode + "：" + o.Trim());
                }
                _aclDone = true;
                Program.Trace("ProfileHome: 已把 " + dir + " 的权限收紧为仅当前账户");
            }
            catch (Exception ex) { Program.Trace("ProfileHome: icacls 失败 " + ex.Message); }
        }

        private static bool Writable(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static bool SamePath(string a, string b)
        {
            try
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
                string fa = Path.GetFullPath(a.Trim().Trim('"')).TrimEnd('\\');
                string fb = Path.GetFullPath(b.Trim().Trim('"')).TrimEnd('\\');
                return string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase);
            }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }
    }

    internal sealed class BrowserProc
    {
        public int Pid;
        public int ParentPid;
        public string Name;
        public string CmdLine;
        public string UserDataDir;
        public int DebugPort;
        public bool IsChild;   // --type=xxx 的子进程；端口归属只认主进程
    }

    /// <summary>
    /// 9333 端口属于谁：必须是用本程序那份登录态目录起的浏览器。
    /// 否则脚本会把"点投递"点进用户自己的日常浏览器、或另一个旧副本的窗口里 —— 那不是投递，是乱点。
    /// 只读 WMI 命令行，不改动任何进程。
    /// </summary>
    internal static class BrowserOwner
    {
        public static List<BrowserProc> List()
        {
            var list = new List<BrowserProc>();
            try
            {
                using (var q = new ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process WHERE Name='chrome.exe' OR Name='msedge.exe'"))
                using (var rs = q.Get())
                {
                    foreach (ManagementObject mo in rs)
                    {
                        string cmd = null, name = null;
                        int pid = 0, ppid = 0;
                        try { cmd = mo["CommandLine"] as string; } catch { }
                        try { name = mo["Name"] as string; } catch { }
                        try { pid = Convert.ToInt32(mo["ProcessId"]); } catch { }
                        try { ppid = Convert.ToInt32(mo["ParentProcessId"]); } catch { }
                        if (pid <= 0) continue;
                        // 注意：浏览器的"主进程"有时读不到 CommandLine（同一台机器上其余进程却能读），
                        // 这时不能直接丢掉它 —— 它是端口归属的当事进程，得靠它的子进程来认领。
                        var bp = new BrowserProc { Pid = pid, ParentPid = ppid, Name = name, CmdLine = cmd ?? "" };
                        bp.IsChild = cmd != null && cmd.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (cmd != null)
                        {
                            bp.UserDataDir = ArgValue(cmd, "--user-data-dir");
                            int port;
                            if (int.TryParse(ArgValue(cmd, "--remote-debugging-port") ?? "", out port)) bp.DebugPort = port;
                        }
                        list.Add(bp);
                    }
                }
            }
            catch (Exception ex) { Program.Trace("BrowserOwner.List: " + ex.Message); }
            return list;
        }

        /// <summary>
        /// 端口上那个浏览器是不是用 profile 起的。
        /// confirmed=false 表示"没能确认"（netstat/WMI 都拿不到）：调用方应放行并记日志，别把正常使用卡死。
        /// 做法：先问系统"谁在 LISTENING 这个端口"（netstat -ano），再看那个 PID 的命令行 ——
        /// 不再靠"扫所有 chrome 进程里谁带了这个端口号"，因为主进程/子进程的归属容易读错。
        /// </summary>
        public static bool Matches(string profile, int port, out bool confirmed, out string detail)
        {
            confirmed = false;
            detail = "";

            int pid = PortOwnerPid(port);
            if (pid > 0)
            {
                var procs = List();
                var self = procs.Find(p => p.Pid == pid);
                if (self != null && !string.IsNullOrEmpty(self.UserDataDir))
                {
                    confirmed = true;
                    detail = ProfileHome.Short(self.UserDataDir);
                    return ProfileHome.SamePath(self.UserDataDir, profile);
                }

                // 主进程的 CommandLine 有时读不到（Chrome 主进程常见），但它派生的渲染器/GPU 进程
                // 一定带着 --user-data-dir：用"这个端口的子进程用的是哪份目录"来认领。
                var kids = procs.FindAll(p => p.ParentPid == pid && !string.IsNullOrEmpty(p.UserDataDir));
                if (kids.Count > 0)
                {
                    confirmed = true;
                    detail = ProfileHome.Short(kids[0].UserDataDir);
                    foreach (var k in kids) if (ProfileHome.SamePath(k.UserDataDir, profile)) return true;
                    return false;
                }

                // 连子进程都读不到：不武断判"不是我们的"，交给调用方记日志放行
                Program.Trace("BrowserOwner: 端口 " + port + " 的监听进程 " + pid + " 读不到命令行，无法确认归属");
                return false;
            }

            // netstat 拿不到（极少数环境）：退回扫描，只认主进程
            foreach (var p in List())
            {
                if (p.IsChild || p.DebugPort != port) continue;
                if (string.IsNullOrEmpty(p.UserDataDir)) continue;
                confirmed = true;
                detail = ProfileHome.Short(p.UserDataDir);
                if (ProfileHome.SamePath(p.UserDataDir, profile)) return true;
            }
            return false;
        }

        /// <summary>谁在监听这个本地端口（netstat -ano）。拿不到返回 0。</summary>
        private static int PortOwnerPid(int port)
        {
            try
            {
                var psi = new ProcessStartInfo("netstat", "-ano -p TCP");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (var p = Process.Start(psi))
                {
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    string needle = ":" + port + " ";
                    foreach (var raw in text.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.IndexOf(needle, StringComparison.Ordinal) < 0) continue;
                        if (line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        // 本地地址 pid ... 最后一列是 PID
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 5) continue;
                        int got;
                        if (int.TryParse(parts[parts.Length - 1], out got) && got > 0) return got;
                    }
                }
            }
            catch (Exception ex) { Program.Trace("BrowserOwner.PortOwnerPid: " + ex.Message); }
            return 0;
        }

        public static int KillBrowsersOn(string profile)
        {
            var procs = List();
            var targets = new List<int>();
            foreach (var p in procs)
            {
                if (!ProfileHome.SamePath(p.UserDataDir, profile)) continue;
                if (!targets.Contains(p.Pid)) targets.Add(p.Pid);
                // 子进程带目录、主进程读不到命令行时，连父进程一起关，
                // 否则 /T 只掐掉子树，浏览器壳还占着登录态目录。
                if (p.ParentPid > 0 && procs.Exists(x => x.Pid == p.ParentPid) && !targets.Contains(p.ParentPid))
                    targets.Add(p.ParentPid);
            }

            int n = 0;
            foreach (var pid in targets)
            {
                try
                {
                    var psi = new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    using (var k = Process.Start(psi)) { k.WaitForExit(4000); }
                    n++;
                }
                catch { }
            }
            return n;
        }

        // 从命令行里取 --name=value（值带不带引号都认）
        private static string ArgValue(string cmd, string name)
        {
            try
            {
                int i = cmd.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return null;
                int j = i + name.Length + 1;
                if (j < cmd.Length && cmd[j] == '"')
                {
                    int k = cmd.IndexOf('"', j + 1);
                    return k > j ? cmd.Substring(j + 1, k - j - 1) : null;
                }
                int e = j;
                while (e < cmd.Length && !char.IsWhiteSpace(cmd[e])) e++;
                return e > j ? cmd.Substring(j, e - j) : null;
            }
            catch { return null; }
        }
    }
}
