using System;
using System.IO;
using Microsoft.Win32;

namespace BatteryViewer
{
    // 开机自启动管理（当前用户 Run 键，不需要管理员权限）
    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "BatteryViewer";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        if (key == null) return false;
                        return key.GetValue(ValueName) != null;
                    }
                }
                catch { return false; }
            }
        }

        public static void SetEnabled(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enable)
                    {
                        string exe = ExePath();
                        if (!string.IsNullOrEmpty(exe))
                            key.SetValue(ValueName, "\"" + exe + "\"");
                    }
                    else
                    {
                        if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch { }
        }

        static string ExePath()
        {
            try
            {
                string p = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                // 开发期跑的是项目目录下的 exe，仍然可用
                return p;
            }
            catch { return null; }
        }
    }
}
