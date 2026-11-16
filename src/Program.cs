using System;
using System.Drawing;
using System.IO;
using System.Text;
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
            var cfgPath = Path.Combine(f.BaseDir, "config.json");
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
                Application.DoEvents();
                System.Threading.Thread.Sleep(600);
                Application.DoEvents();
                foreach (var key in new[] { "dash", "plan", "sites", "log", "about" })
                {
                    f.GotoPage(key);
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(350);
                    Application.DoEvents();
                    using (var bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
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

        [STAThread]
        static void Main(string[] args)
        {
            Trace("--- start " + Application.ProductVersion);
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
                var f = new MainForm();
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
