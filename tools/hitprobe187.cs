using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 点「重开」到底会不会真的开跑：把 health 页画出来、从它的命中表里找到
    /// health:reopen:&lt;site&gt; 的矩形，往那个坐标发一次真实的按下+抬起，
    /// 然后看引擎是不是起来了。锁屏也能跑（不依赖屏幕坐标）。
    /// 用法：AutoApply.hit.exe
    /// </summary>
    internal static class HitProbe
    {
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        [STAThread]
        static int Main(string[] args)
        {
            var sb = new StringBuilder();
            var f = new MainForm();
            f.SuppressWizard = true;
            f.Show();
            Application.DoEvents();
            Thread.Sleep(600);
            Application.DoEvents();

            var logs = new List<string>();
            f.Engine.Log += delegate (string lv, string tx) { logs.Add(lv + " | " + tx); };

            f.GotoPage("health");
            Application.DoEvents();
            Thread.Sleep(500);

            Page hp = null;
            foreach (Control c in f.Controls) if (c is HealthPage) hp = (Page)c;
            if (hp == null) { Console.WriteLine("no health page"); return 1; }
            hp.Refresh();
            Application.DoEvents();

            var field = typeof(Page).GetField("Hits", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = field.GetValue(hp) as IList;
            sb.AppendLine("hits on health page: " + (list == null ? -1 : list.Count));
            string want = null;
            RectangleF wantRect = RectangleF.Empty;
            foreach (var it in list)
            {
                var t = it.GetType();
                string id = (string)t.GetField("Id").GetValue(it);
                RectangleF r = (RectangleF)t.GetField("Rect").GetValue(it);
                sb.AppendLine("   hit " + id + "  " + r.X.ToString("F0") + "," + r.Y.ToString("F0") + " " +
                              r.Width.ToString("F0") + "x" + r.Height.ToString("F0"));
                if (id != null && id.StartsWith("health:reopen:")) { want = id; wantRect = r; }
            }
            if (want == null) { sb.AppendLine("NO reopen hit registered"); Done(sb); return 2; }

            int x = (int)((wantRect.X + wantRect.Width / 2) * T.S);
            int y = (int)((wantRect.Y + wantRect.Height / 2) * T.S);
            long lp = ((long)(y << 16) | (uint)(x & 0xFFFF));
            sb.AppendLine("clicking " + want + " at client " + x + "," + y);
            SendMessage(hp.Handle, 0x0201, (IntPtr)1, (IntPtr)lp);
            Thread.Sleep(60);
            SendMessage(hp.Handle, 0x0202, IntPtr.Zero, (IntPtr)lp);

            for (int i = 0; i < 40; i++)
            {
                Application.DoEvents();
                Thread.Sleep(100);
                if (f.Engine.Running) break;
            }
            sb.AppendLine("engine running = " + f.Engine.Running);
            for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(100); }
            sb.AppendLine("results = " + f.Engine.Results.Count);
            foreach (var l in logs) sb.AppendLine("   log " + l);
            Done(sb);
            return f.Engine.Results.Count > 0 ? 0 : 3;
        }

        static void Done(StringBuilder sb)
        {
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hitprobe.txt");
            File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine(sb.ToString());
            Environment.Exit(0);
        }
    }
}
