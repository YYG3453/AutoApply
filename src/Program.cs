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
