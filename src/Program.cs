using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AutoApply
{
    internal static class Program
    {
        private static readonly object Gate = new object();

        public static void Trace(string msg)
        {
            try
            {
                lock (Gate)
                {
                    var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autoapply.log");
                    File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>同一时间只允许一个实例；第二次双击时把已经在跑的那个叫到前面来。</summary>
        private static Mutex _singleInstance;


        /// <summary>
        /// 低完整性/不受信任时的提示。为什么不清静地退出：低权限进程只要浏览器已经在跑，
        /// 照样能通过 CDP 操作它投递 —— 不是致命错误，但必须让用户知道"重开浏览器"这条路走不通。
        /// 只提示一次（进程内），别每次启动都拦一道。
        /// </summary>
        private static void WarnIfLowIntegrity(string[] args)
        {
            string joined = string.Join(" ", args);
            bool tooling = joined.IndexOf("--render", StringComparison.OrdinalIgnoreCase) >= 0
                        || joined.IndexOf("--perf", StringComparison.OrdinalIgnoreCase) >= 0
                        || joined.IndexOf("--test-hit", StringComparison.OrdinalIgnoreCase) >= 0;
            bool silent = joined.IndexOf("--tray", StringComparison.OrdinalIgnoreCase) >= 0;
            if (tooling) return;

            string il = Engine.IntegrityLevel();
            if (il == "medium" || il == "high") return;
            Trace("low integrity (" + il + ")：本进程拉不起 Chrome，见 tools\\fix-integrity-label.cmd");
            if (silent) return;   // 托盘/开机自启时不弹框，平台健康页里已有同样的说明

            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                string text =
                    "这次 AutoApply 是以「受限权限」运行的，程序自己启动浏览器会被 Windows 拒绝。\r\n\r\n" +
                    "原因通常是：AutoApply.exe 所在的文件夹被系统或安全软件打上了「低完整性」标签，\r\n" +
                    "从该文件夹启动的程序都会继承这个标签，双击多少次结果都一样。\r\n\r\n" +
                    "修法：右键 AutoApply\\tools\\fix-integrity-label.cmd →「以管理员身份运行」，\r\n" +
                    "等它跑完（几秒到一分钟），再打开 AutoApply。\r\n\r\n" +
                    "临时办法：如果浏览器窗口已经在运行（9333 端口有响应），本程序仍能连上它投递，\r\n" +
                    "可以先用「开始投递」。\r\n\r\n" +
                    "当前程序目录：" + dir;
                MessageBox.Show(text, "AutoApply · 权限受限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { Trace("low integrity notice failed: " + ex.Message); }
        }
        /// <summary>同一时间只允许一个实例；第二次双击时把已经在跑的那个叫到前面来。</summary>
        private static bool AnotherInstanceRunning()
        {
            bool created;
            _singleInstance = new Mutex(true, "AutoApply.SingleInstance.1", out created);
            if (created) return false;
            try
            {
                var h = FindWindow(null, "AutoApply · 四平台自动投递");
                if (h != IntPtr.Zero)
                {
                    ShowWindow(h, 9);          // SW_RESTORE
                    SetForegroundWindow(h);
                }
            }
            catch { }
            return true;
        }

        /// <summary>--test-hit：把窗体与子控件的 WM_NCHITTEST 结果写到 hit-test.txt（验收拖动区）。</summary>
        private static void TestHit(MainForm f)
        {
            var sb = new StringBuilder();
            var h = f.Handle;
            sb.AppendLine("form size " + f.Width + "x" + f.Height + " scale=" + T.S);

            Func<IntPtr, Point, int> hit = (hwnd, clientPt) =>
            {
                var sp = f.PointToScreen(clientPt);
                long lp = ((long)(sp.Y << 16) | (uint)(sp.X & 0xFFFF));
                return SendMessage(hwnd, 0x0084, IntPtr.Zero, (IntPtr)lp).ToInt32();
            };

            var probes = new[]
            {
                new Point(600, 20),                 // 顶部条中部 -> 2 (HTCAPTION)
                new Point(60, 20),                  // 左侧导航顶部 -> 2
                new Point(600, f.Height / 2),       // 中间内容区 -> 1 (HTCLIENT)
                new Point(3, f.Height / 2),         // 左边缘 -> 2
                new Point(f.Width - 3, f.Height / 2), // 右边缘 -> 2
                new Point(600, 2),                  // 顶部边缘 -> 2
                new Point(600, f.Height - 3),       // 底部边缘 -> 2
                new Point(f.Width - 30, 20),        // 右上按钮区 -> 1
            };
            foreach (var p in probes) sb.AppendLine("form " + p.X + "," + p.Y + " -> " + hit(h, p));

            // 子控件（页面 / 导航栏）在拖动条内应返回 -1 (HTTRANSPARENT) 让事件冒到窗体
            foreach (Control c in f.Controls)
            {
                if (c is Page || c is NavRail)
                {
                    var cp = f.PointToScreen(new Point(c.Left + c.Width / 2, c.Top + 8));
                    long lp = ((long)(cp.Y << 16) | (uint)(cp.X & 0xFFFF));
                    var r = SendMessage(c.Handle, 0x0084, IntPtr.Zero, (IntPtr)lp).ToInt32();
                    var cp2 = f.PointToScreen(new Point(c.Left + c.Width / 2, c.Top + c.Height / 2));
                    long lp2 = ((long)(cp2.Y << 16) | (uint)(cp2.X & 0xFFFF));
                    var r2 = SendMessage(c.Handle, 0x0084, IntPtr.Zero, (IntPtr)lp2).ToInt32();
                    sb.AppendLine(c.GetType().Name + " top -> " + r + " , body -> " + r2 + " (期望 -1 / 1)");
                }
            }

            // 日志页：右上按钮在拖动条内应仍可点击（1），空白处应让位给标题栏（-1）
            f.ShowPage("log");
            f.Refresh();
            Application.DoEvents();
            foreach (Control c in f.Controls)
            {
                if (!(c is LogPage)) continue;
                ((LogPage)c).Refresh();
                Application.DoEvents();
                var probes2 = new[]
                {
                    new Point((int)(c.Left + (c.Width / T.S - 47) * T.S), (int)(c.Top + 40 * T.S)),
                    new Point((int)(c.Left + 300 * T.S), (int)(c.Top + 8 * T.S)),
                };
                foreach (var p in probes2)
                {
                    var sp = f.PointToScreen(p);
                    long lp = ((long)(sp.Y << 16) | (uint)(sp.X & 0xFFFF));
                    sb.AppendLine("LogPage " + (p.X - c.Left) + "," + (p.Y - c.Top) + " -> " +
                                  SendMessage(c.Handle, 0x0084, IntPtr.Zero, (IntPtr)lp).ToInt32() + " (按钮1 / 空白-1)");
                }
                sb.AppendLine("LogPage debug: " + ((Page)c).HitDebug(new PointF(c.Width / T.S - 47, 40)));
                sb.AppendLine("LogPage visible=" + c.Visible + " size=" + c.Width + "x" + c.Height);
            }

            // 主题切换 + 落盘验证（跑完恢复夜晚模式）
            var cfgPath = Paths.CfgPath.Length > 0 ? Paths.CfgPath : Path.Combine(f.BaseDir, "config.json");
            var before = AppCfg.Load(cfgPath).Theme;
            f.ApplyTheme("day", true);
            var dayTheme = AppCfg.Load(cfgPath).Theme;
            f.ApplyTheme("night", true);
            var nightTheme = AppCfg.Load(cfgPath).Theme;
            sb.AppendLine("theme persist: before=" + before + " -> day=" + dayTheme + " -> night=" + nightTheme);

            var outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hit-test.txt");            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Trace("hit test written: " + outPath);
        }

        private static void RenderAll(MainForm f, string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                f.Show();
                f.TopMost = true;
                f.Activate();
                Application.DoEvents();
                System.Threading.Thread.Sleep(600);
                Application.DoEvents();
                foreach (var key in f.Pages)
                {
                    f.GotoPage(key);
                    Application.DoEvents();
                    // 切页会启动 220ms 的淡入（Timer 驱动）。Sleep 期间不泵消息，计时器就不走，
                    // 截出来永远是"半透明"的第一帧；所以这里边泵边等，让 Appear 走到 1。
                    var settleUntil = Environment.TickCount + 1400;
                    while (Environment.TickCount < settleUntil)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(20);
                    }
                    Application.DoEvents();
                    // 只截客户区：窗口现在保留 WS_THICKFRAME（为了 DWM 圆角），
                    // DrawToBitmap 会把系统标题栏一起画进来，所以走屏幕抓取。
                    var o = f.PointToScreen(Point.Empty);
                    var cs = f.ClientSize;
                    using (var bmp = new Bitmap(cs.Width, cs.Height))
                    using (var gg = Graphics.FromImage(bmp))
                    {
                        gg.CopyFromScreen(o.X, o.Y, 0, 0, new Size(cs.Width, cs.Height));
                        bmp.Save(Path.Combine(dir, key + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Trace("rendered " + key);
                }
            }
            catch (Exception ex) { Trace("render crash: " + ex); }
            try { f.Close(); } catch { }
            Application.Exit();
            Environment.Exit(0);
        }

        /// <summary>
        /// 只给文档用：停在仪表盘，打开皮肤面板，日夜各截一张。
        /// 面板是独立顶层窗口，DrawToBitmap 抓不到，所以还是 CopyFromScreen。
        /// </summary>
        private static void RenderSkins(MainForm f, string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                f.Show();
                f.TopMost = true;
                f.Activate();
                Pump(700);
                Cursor.Position = new Point(0, 0);   // 鼠标在右上角会触发按钮 ToolTip，截图里会多出一块白框
                f.GotoPage("dash");
                Pump(1200);
                foreach (bool night in new[] { true, false })
                {
                    f.ApplyTheme(night ? "night" : "day", false);
                    Pump(900);
                    f.ShowSkinMenu();
                    Pump(600);
                    Capture(f, Path.Combine(dir, night ? "skin-night.png" : "skin-day.png"));
                    f.ShowSkinMenu();    // 同一个入口再点一次 = 收起面板
                    Pump(250);
                }
            }
            catch (Exception ex) { Trace("skinshot crash: " + ex); }
            try { f.Close(); } catch { }
            Application.Exit();
            Environment.Exit(0);
        }

        private static void Pump(int ms)
        {
            var until = Environment.TickCount + ms;
            while (Environment.TickCount < until)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(20);
            }
            Application.DoEvents();
        }

        private static void Capture(MainForm f, string path)
        {
            var o = f.PointToScreen(Point.Empty);
            var cs = f.ClientSize;
            using (var bmp = new Bitmap(cs.Width, cs.Height))
            using (var gg = Graphics.FromImage(bmp))
            {
                gg.CopyFromScreen(o.X, o.Y, 0, 0, new Size(cs.Width, cs.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            Trace("captured " + path);
        }

        /// <summary>量每个页面"画一次"要多久：切页卡不卡的根子就在这里。</summary>
        private static void PerfPages(MainForm f)
        {
            var sb = new StringBuilder();
            try
            {
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(900);
                Application.DoEvents();
                foreach (var key in f.Pages)
                {
                    if (key == "log") continue;
                    f.GotoPage(key);
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(420);
                    Application.DoEvents();
                    var t = new List<double>();
                    for (int i = 0; i < 5; i++)
                    {
                        var sw = Stopwatch.StartNew();
                        using (var bmp = new Bitmap(f.Width, f.Height))
                            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        sw.Stop();
                        t.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    t.Sort();
                    sb.AppendLine(key.PadRight(10) + " min=" + t[0].ToString("F1") + "ms  med=" +
                        t[t.Count / 2].ToString("F1") + "ms  max=" + t[t.Count - 1].ToString("F1") + "ms");
                }
            }
            catch (Exception ex) { sb.AppendLine("crash: " + ex); }
            var outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "perf.txt");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Trace("perf written: " + outPath);
            try { f.Close(); } catch { }
            Application.Exit();
            Environment.Exit(0);
        }

        [STAThread]
        static void Main(string[] args)
        {
            Trace("--- start " + Application.ProductVersion);
            // 低完整性进程拉不起 Chrome（crashpad 写不进配置目录，0x80000003 退出）。
            // 最常见的成因是 exe 所在文件夹被打了「低完整性」标签（沙箱/安全软件会这么干），
            // 从该文件夹启动的程序会继承标签一起被降权 —— 双击多少次都一样。
            WarnIfLowIntegrity(args);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => { Trace("UI exception: " + e.Exception); try { MessageBox.Show(e.Exception.Message, "AutoApply"); } catch { } };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Trace("fatal: " + e.ExceptionObject);

            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    T.S = g.DpiX / 96f;
                }
            }
            catch { }
            if (T.S < 1f) T.S = 1f;
            Trace("dpi scale = " + T.S);

            try
            {
                Trace("creating form");
                var joinedBefore = string.Join(" ", args);
                bool tooling = joinedBefore.IndexOf("--render", StringComparison.OrdinalIgnoreCase) >= 0
                            || joinedBefore.IndexOf("--perf", StringComparison.OrdinalIgnoreCase) >= 0
                            || joinedBefore.IndexOf("--skinshot", StringComparison.OrdinalIgnoreCase) >= 0
                            || joinedBefore.IndexOf("--test-hit", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!tooling && AnotherInstanceRunning())
                {
                    Trace("another instance is already running, activating it and exiting");
                    return;
                }
                var f = new MainForm();
                if (tooling || joinedBefore.IndexOf("--run-now", StringComparison.OrdinalIgnoreCase) >= 0) f.SuppressWizard = true;
                Trace("form created, running");
                var joined = string.Join(" ", args);
                if (joined.IndexOf("--test-hit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // 必须等窗体真正显示并绘制过一次，子控件的命中矩形才有内容
                    f.Shown += (s, e) => f.BeginInvoke(new Action(() =>
                    {
                        try { TestHit(f); }
                        catch (Exception ex) { Trace("hit test crash: " + ex); }
                        try { f.Close(); } catch { }
                        Application.Exit();
                        Environment.Exit(0);
                    }));
                    Application.Run(f);
                    return;
                }
                if (joined.IndexOf("--render", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(joined, "--render\\s+\"?([^\"\\s]+)\"?");
                    var outDir = m.Success ? m.Groups[1].Value : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shots");
                    RenderAll(f, outDir);
                    return;
                }
                if (joined.IndexOf("--skinshot", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var ms = System.Text.RegularExpressions.Regex.Match(joined, "--skinshot\\s+\"?([^\"\\s]+)\"?");
                    var outDir = ms.Success ? ms.Groups[1].Value : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shots");
                    RenderSkins(f, outDir);
                    return;
                }
                if (joined.IndexOf("--perf", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PerfPages(f);
                    return;
                }
                Application.Run(f);
                Trace("exited normally");
            }
            catch (Exception ex)
            {
                Trace("crash: " + ex);
                try { MessageBox.Show(ex.Message + "\r\n\r\n" + ex.StackTrace, "AutoApply 启动失败"); } catch { }
            }
        }
    }
}
