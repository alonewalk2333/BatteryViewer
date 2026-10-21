using System;
using System.Windows;

namespace BatteryViewer
{
    static class App
    {
        [STAThread]
        static void Main()
        {
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (s, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowCrash(e.ExceptionObject as Exception);

            var win = new MainWindow();
            win.Closing += (s, e) =>
            {
                // Alt+F4 / 系统关机也走"退出询问"
                if (!win.RealExitRequested)
                {
                    e.Cancel = true;
                    win.OnUserClose();
                }
                else
                {
                    win.DisposeTray();
                }
            };
            new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }.Run(win);
        }

        static void ShowCrash(Exception ex)
        {
            string msg = ex == null ? "未知错误" : (ex.Message + "\n\n" + ex.StackTrace);
            try { System.Windows.Forms.MessageBox.Show(msg, "程序出错"); }
            catch { }
        }
    }
}
