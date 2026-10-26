using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace MouseBattery
{
    public static class Settings
    {
        const string AppKey = @"Software\MouseBattery";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "MouseBattery";

        public static bool Digits
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(AppKey))
                {
                    object v = k == null ? null : k.GetValue("Style");
                    return v as string == "digits";
                }
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(AppKey))
                    k.SetValue("Style", value ? "digits" : "battery", RegistryValueKind.String);
            }
        }

        public static bool IsStartupEnabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
        }

        public static void SetStartup(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunName, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"", RegistryValueKind.String);
                else k.DeleteValue(RunName, false);
            }
        }
    }

    public static class Theme
    {
        public static bool IsLightTaskbar()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }
    }

    public static class Log
    {
        static readonly object Gate = new object();
        public static readonly string Dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseBattery");
        public static readonly string Path = System.IO.Path.Combine(Dir, "app.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    var fi = new FileInfo(Path);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        string old = Path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(Path, old);
                    }
                    File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void Open()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(Path)) File.WriteAllText(Path, "");
                Process.Start("notepad.exe", "\"" + Path + "\"");
            }
            catch { }
        }
    }
}
