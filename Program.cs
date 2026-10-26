using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MouseBattery
{
    static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
        [DllImport("kernel32.dll")] static extern bool AllocConsole();

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && (args[0] == "--probe" || args[0] == "--diagnostics"))
            {
                if (!AttachConsole(-1)) AllocConsole();
                var stdout = new StreamWriter(Console.OpenStandardOutput());
                stdout.AutoFlush = true;
                Console.SetOut(stdout);
                Console.WriteLine();
                Console.WriteLine(HidReader.Diagnostics());
                return;
            }

            if (args.Length > 1 && (args[0] == "--preview" || args[0] == "--make-icon"))
            {
                string target = string.Join(" ", args, 1, args.Length - 1);
                try
                {
                    if (args[0] == "--preview") IconFactory.WritePreviews(target);
                    else IconFactory.WriteAppIcon(target);
                }
                catch (Exception ex)
                {
                    string tag = args[0] == "--preview" ? "preview" : "icon";
                    try { File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MouseBattery-" + tag + "-error.txt"), ex.ToString()); } catch { }
                    Environment.Exit(1);
                }
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, @"Local\MouseBattery_9F1C2A6B", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
        }
    }
}
