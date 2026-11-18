using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AutoApply
{
    /// <summary>
    /// v1.8.7 的验证入口（不进界面）：证明「检查 / 重开」背后那条链路真的能
    /// 起浏览器、开标签页、发现未登录并等待。用法：
    ///   AutoApply.verify.exe &lt;安装目录&gt; [等待秒数]
    /// </summary>
    internal static class Verify
    {
        [STAThread]
        static int Main(string[] args)
        {
            string baseDir = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
            int waitSec = args.Length > 1 ? int.Parse(args[1]) : 12;
            var sb = new StringBuilder();
            Action<string> W = delegate (string s) { sb.AppendLine(s); Console.WriteLine(s); };
            try
            {
                Paths.Init(baseDir);
                W("BaseDir   = " + baseDir);
                W("DataDir   = " + Paths.DataDir);
                W("CfgPath   = " + Paths.CfgPath);
                var cfg = AppCfg.Load(Paths.CfgPath);
                W("sites     = " + cfg.Sites.Count + " (enabled " + cfg.Sites.FindAll(delegate (SiteCfg s) { return s.Enabled; }).Count + ")");
                W("port      = " + cfg.Port + "  profile=" + cfg.ProfileDir);
                var e = new Engine(baseDir);
                e.Cfg = cfg;
                Health.Port = cfg.Port;
                Health.ScriptDir = Paths.DataDir;

                var sw = Stopwatch.StartNew();
                W("ChromeUp before = " + e.ChromeUp());
                string msg;
                bool ok = e.EnsureChrome(out msg);
                W("EnsureChrome    = " + ok + "  (" + sw.ElapsedMilliseconds + "ms)  " + msg);
                sw.Restart();
                string msg2;
                bool ok2 = e.EnsureChrome(out msg2);
                W("EnsureChrome#2  = " + ok2 + "  (" + sw.ElapsedMilliseconds + "ms)  " + msg2);
                if (!ok) W("(浏览器起不来，跳过标签页/就绪检查，继续验证整轮收尾)");

                foreach (var s in cfg.Sites)
                {
                    if (!s.Enabled) continue;
                    sw.Restart();
                    bool t = e.EnsureTab(s);
                    W("EnsureTab " + s.Key.PadRight(5) + " = " + t + "  (" + sw.ElapsedMilliseconds + "ms)");
                }
                var tabs = ok ? Health.Tabs() : new List<string>();
                W("tabs       = " + tabs.Count);
                foreach (var u in tabs) W("   " + u);

                foreach (var s in cfg.Sites)
                {
                    if (!s.Enabled) continue;
                    string reason;
                    sw.Restart();
                    bool ready = e.WaitSiteReady(s, 0, out reason);
                    W("ready " + s.Key.PadRight(5) + " = " + ready + "  reason=" + reason + "  (" + sw.ElapsedMilliseconds + "ms)");
                }

                var first = cfg.Sites.Find(delegate (SiteCfg s) { return s.Enabled; });
                if (first != null)
                {
                    string reason;
                    sw.Restart();
                    bool ready = e.WaitSiteReady(first, waitSec, out reason);
                    W("wait " + first.Key + " " + waitSec + "s = " + ready + "  reason=" + reason + "  (" + sw.ElapsedMilliseconds + "ms)");
                    W(ready ? "WAIT-RESULT: became ready" : "WAIT-RESULT: still not ready (expected when not logged in)");
                }
                Engine.FocusBrowser();
                W("FocusBrowser ok");

                // 整轮跑一次：浏览器起不来时，应该立刻收尾，而不是每站再空转半分钟。
                var logs = new List<string>();
                e.Log += delegate (string lv, string tx) { logs.Add(lv + " | " + tx); };
                var run = new List<SiteCfg>();
                foreach (var s in cfg.Sites) if (s.Enabled) run.Add(s);
                sw.Restart();
                e.StartRun(run, "verify");
                while (e.Running && sw.ElapsedMilliseconds < 180000) Thread.Sleep(200);
                W("StartRun done in " + sw.ElapsedMilliseconds + "ms  results=" + e.Results.Count);
                foreach (var r in e.Results) W("   " + r.Key + " stop=" + r.Stop + " needs=" + r.Needs);
                foreach (var l in logs) W("   log " + l);
            }
            catch (Exception ex)
            {
                W("CRASH " + ex);
                return 3;
            }
            string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verify187.txt");
            try { File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
            Console.WriteLine("written " + outPath);
            return 0;
        }
    }
}
