using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 锁屏 / 远程会话下也能出界面图：--render 走的是屏幕抓取，桌面锁了就只会拍到锁屏；
    /// 这里改用 DrawToBitmap（WM_PRINT），窗口不必真的显示在最前面。
    /// 用法：AutoApply.render.exe &lt;输出目录&gt;
    /// </summary>
    internal static class Render
    {
        [STAThread]
        static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : "shots";
            try
            {
                Directory.CreateDirectory(outDir);
                var f = new MainForm();
                f.SuppressWizard = true;
                f.Show();
                Application.DoEvents();
                Thread.Sleep(700);
                Application.DoEvents();
                foreach (var key in f.Pages)
                {
                    f.GotoPage(key);
                    Application.DoEvents();
                    Thread.Sleep(key == "dash" ? 900 : 350);
                    Application.DoEvents();
                    using (var bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(Path.Combine(outDir, key + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Console.WriteLine("rendered " + key);
                }
                f.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("CRASH " + ex);
                return 1;
            }
            Environment.Exit(0);
            return 0;
        }
    }
}
