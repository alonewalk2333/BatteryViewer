using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Input;
using Microsoft.Win32;

namespace BatteryViewer.Installer
{
    public class InstallerApp
    {
        const string AppName = "BatteryViewer";
        const string AppTitle = "2.4G 外设电量查看器";
        static string installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "BatteryViewer");

        static Window window;
        static Grid pageHost;
        static TextBlock lblPath;
        static System.Windows.Controls.ProgressBar progress;
        static TextBlock lblProgress;
        static Button btnNext;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "/uninstall")
            {
                RunUninstall();
                return;
            }

            window = new Window
            {
                Title = AppTitle + " 安装向导",
                Width = 560, Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
            };

            var outer = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(28, 32, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 3, Opacity = 0.5, Color = Colors.Black },
            };
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });

            // 标题栏
            var titleBar = new Grid { Background = Brushes.Transparent };
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 1) { try { window.DragMove(); } catch { } } };
            var title = new TextBlock
            {
                Text = AppTitle + " 安装向导",
                FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(20, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var btnClose = new Button
            {
                Content = "✕", Width = 36, Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
                Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            btnClose.Click += (s, e) => System.Windows.Application.Current.Shutdown();
            titleBar.Children.Add(title);
            titleBar.Children.Add(btnClose);
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            // 内容页容器
            pageHost = new Grid();
            Grid.SetRow(pageHost, 1);
            root.Children.Add(pageHost);

            // 底部按钮栏
            var btnBar = new Grid { Background = new SolidColorBrush(Color.FromArgb(24, 0, 0, 0)) };
            btnNext = MakeButton("下一步", new SolidColorBrush(Color.FromRgb(88, 140, 255)), Brushes.White);
            btnNext.HorizontalAlignment = HorizontalAlignment.Right;
            btnNext.VerticalAlignment = VerticalAlignment.Center;
            btnNext.Margin = new Thickness(0, 0, 20, 0);
            btnNext.Width = 110;
            var btnCancel = MakeButton("取消", new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), Brushes.White);
            btnCancel.HorizontalAlignment = HorizontalAlignment.Right;
            btnCancel.VerticalAlignment = VerticalAlignment.Center;
            btnCancel.Margin = new Thickness(0, 0, 146, 0);
            btnCancel.Width = 80;
            btnCancel.Click += (s, e) => System.Windows.Application.Current.Shutdown();
            btnBar.Children.Add(btnNext);
            btnBar.Children.Add(btnCancel);
            Grid.SetRow(btnBar, 2);
            root.Children.Add(btnBar);

            outer.Child = root;
            window.Content = outer;

            ShowWelcomePage();
            new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }.Run(window);
        }

        static Button MakeButton(string text, Brush bg, Brush fg)
        {
            var b = new Button
            {
                Content = text, Height = 34, Cursor = Cursors.Hand,
                Background = bg, Foreground = fg,
                FontSize = 13, BorderThickness = new Thickness(0),
            };
            var tpl = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetBinding(System.Windows.Controls.Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tpl.VisualTree = border;
            border.AppendChild(cp);
            b.Template = tpl;
            return b;
        }

        // ---- 第 1 页：欢迎 ----
        static void ShowWelcomePage()
        {
            pageHost.Children.Clear();
            var panel = new StackPanel { Margin = new Thickness(40, 20, 40, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = "欢迎使用 " + AppTitle,
                FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(235, 238, 245)),
            });
            panel.Children.Add(new TextBlock
            {
                Text = "\n本向导将把软件安装到你的电脑。\n\n· 支持罗技 HID++、蓝牙 BLE 电池服务、ATK/VXE 等协议\n· 零依赖，安装后无需联网即可使用\n· 可随时从系统「设置 → 应用」中卸载\n\n点击「下一步」选择安装位置。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 22,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 166, 178)),
            });
            pageHost.Children.Add(panel);
            btnNext.Click += NextFromWelcome;
        }

        static void NextFromWelcome(object s, RoutedEventArgs e)
        {
            btnNext.Click -= NextFromWelcome;
            ShowPathPage();
        }

        // ---- 第 2 页：选目录 ----
        static void ShowPathPage()
        {
            pageHost.Children.Clear();
            var panel = new StackPanel { Margin = new Thickness(40, 20, 40, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = "选择安装位置",
                FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(235, 238, 245)),
            });
            panel.Children.Add(new TextBlock
            {
                Text = "\n默认安装到用户目录（无需管理员权限）：",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 166, 178)),
                Margin = new Thickness(0, 8, 0, 10),
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var txt = new TextBox
            {
                Text = installDir, Width = 340, Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(20, 23, 30)),
                Foreground = new SolidColorBrush(Color.FromRgb(235, 238, 245)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 76, 90)),
            };
            var btnBrowse = MakeButton("浏览…", new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), Brushes.White);
            btnBrowse.Width = 70;
            btnBrowse.Margin = new Thickness(8, 0, 0, 0);
            btnBrowse.Click += (s, e) =>
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog();
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) txt.Text = dlg.SelectedPath;
            };
            row.Children.Add(txt);
            row.Children.Add(btnBrowse);
            panel.Children.Add(row);
            pageHost.Children.Add(panel);

            RoutedEventHandler h = null;
            h = (s, e) =>
            {
                btnNext.Click -= h;
                installDir = txt.Text.Trim();
                ShowInstallingPage();
            };
            btnNext.Click += h;
        }

        // ---- 第 3 页：安装中/完成 ----
        static void ShowInstallingPage()
        {
            pageHost.Children.Clear();
            var panel = new StackPanel { Margin = new Thickness(40, 30, 40, 0) };
            var lblTitle = new TextBlock
            {
                Text = "正在安装…",
                FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(235, 238, 245)),
            };
            progress = new System.Windows.Controls.ProgressBar
            {
                Height = 8,
                Minimum = 0, Maximum = 100, Value = 0,
                Margin = new Thickness(0, 20, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(88, 140, 255)),
                Background = new SolidColorBrush(Color.FromRgb(40, 44, 55)),
                BorderThickness = new Thickness(0),
            };
            lblProgress = new TextBlock
            {
                Text = "释放文件…",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 166, 178)),
                Margin = new Thickness(0, 10, 0, 0),
            };
            panel.Children.Add(lblTitle);
            panel.Children.Add(progress);
            panel.Children.Add(lblProgress);
            pageHost.Children.Add(panel);
            btnNext.IsEnabled = false;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    DoInstall();
                    DispatcherInvoke(() => ShowDonePage());
                }
                catch (Exception ex)
                {
                    DispatcherInvoke(() =>
                    {
                        lblTitle.Text = "安装失败";
                        lblProgress.Text = ex.Message;
                        btnNext.IsEnabled = true;
                        btnNext.Content = "关闭";
                        btnNext.Click += (s2, e2) => System.Windows.Application.Current.Shutdown();
                    });
                }
            });
        }

        static void DoInstall()
        {
            Directory.CreateDirectory(installDir);
            SetProgress(20, "释放程序文件…");

            // 从嵌入资源释放 exe
            var asm = Assembly.GetExecutingAssembly();
            string resName = null;
            foreach (var n in asm.GetManifestResourceNames())
                if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { resName = n; break; }
            if (resName == null) throw new Exception("安装包损坏：缺少程序文件");

            string exePath = Path.Combine(installDir, AppName + ".exe");
            using (var src = asm.GetManifestResourceStream(resName))
            using (var dst = new FileStream(exePath, FileMode.Create, FileAccess.Write))
                src.CopyTo(dst);
            SetProgress(70, "创建快捷方式…");

            // 快捷方式：桌面 + 开始菜单
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string startMenu = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs");
                CreateShortcut(Path.Combine(desktop, AppTitle + ".lnk"), exePath, installDir);
                var smFolder = Path.Combine(startMenu, AppTitle);
                Directory.CreateDirectory(smFolder);
                CreateShortcut(Path.Combine(smFolder, AppTitle + ".lnk"), exePath, installDir);
                CreateShortcut(Path.Combine(smFolder, "卸载 " + AppTitle + ".lnk"), exePath, installDir, "/uninstall");
            }
            catch { }
            SetProgress(85, "注册卸载信息…");

            // 卸载注册表（出现在系统「设置 → 应用」）
            string self = Assembly.GetExecutingAssembly().Location;
            using (var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName))
            {
                key.SetValue("DisplayName", AppTitle);
                key.SetValue("DisplayVersion", "1.2.0.0");
                key.SetValue("InstallLocation", installDir);
                key.SetValue("DisplayIcon", exePath);
                key.SetValue("UninstallString", "\"" + Path.Combine(installDir, "Uninstall.exe") + "\" /uninstall");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
                key.SetValue("Publisher", "BatteryViewer");
            }

            // 安装器自复制为 Uninstall.exe
            try
            {
                string self2 = ProcessPath();
                if (!string.IsNullOrEmpty(self2))
                    File.Copy(self2, Path.Combine(installDir, "Uninstall.exe"), true);
            }
            catch { }
            SetProgress(100, "完成");
        }

        static string ProcessPath()
        {
            return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
        }

        static void CreateShortcut(string lnkPath, string target, string workDir, string args = null)
        {
            // 先删旧的同名文件（WScript.Shell 不覆盖已存在的只读项）
            try { if (File.Exists(lnkPath)) File.Delete(lnkPath); } catch { }
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic lnk = shell.CreateShortcut(lnkPath);
            lnk.TargetPath = target;
            lnk.WorkingDirectory = workDir;
            if (!string.IsNullOrEmpty(args)) lnk.Arguments = args;
            lnk.IconLocation = target + ",0";
            lnk.Save();
        }

        static void ShowDonePage()
        {
            pageHost.Children.Clear();
            var panel = new StackPanel { Margin = new Thickness(40, 30, 40, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = "✔ 安装完成",
                FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(92, 230, 130)),
            });
            panel.Children.Add(new TextBlock
            {
                Text = "\n已在桌面和开始菜单创建快捷方式。\n\n· 运行软件后点击右上角「设置」可自定义背景和托盘\n· 卸载请到系统「设置 → 应用 → 安装的应用」",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13, LineHeight = 22,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 166, 178)),
                Margin = new Thickness(0, 10, 0, 0),
            });
            pageHost.Children.Add(panel);

            progress.Value = 100;
            lblProgress.Text = "全部完成";
            btnNext.IsEnabled = true;
            btnNext.Content = "立即运行";

            RoutedEventHandler h = null;
            h = (s, e) =>
            {
                btnNext.Click -= h;
                try { System.Diagnostics.Process.Start(Path.Combine(installDir, AppName + ".exe")); } catch { }
                System.Windows.Application.Current.Shutdown();
            };
            btnNext.Click += h;
        }

        static void SetProgress(int pct, string text)
        {
            DispatcherInvoke(() =>
            {
                if (progress != null) progress.Value = pct;
                if (lblProgress != null) lblProgress.Text = text;
            });
        }

        static void DispatcherInvoke(Action a)
        {
            window.Dispatcher.BeginInvoke(a);
        }

        // ---- 卸载 ----
        static void RunUninstall()
        {
            var r = System.Windows.Forms.MessageBox.Show(
                "确定要完全卸载 " + AppTitle + " 吗？\n\n（如果软件正在运行，请先从托盘完全退出）",
                "卸载 " + AppTitle,
                System.Windows.Forms.MessageBoxButtons.YesNo,
                System.Windows.Forms.MessageBoxIcon.Question);
            if (r != System.Windows.Forms.DialogResult.Yes) return;

            try
            {
                string dir = installDir;
                if (!Directory.Exists(dir))
                {
                    // 从注册表找安装位置
                    using (var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName))
                    {
                        if (key != null) dir = (string)key.GetValue("InstallLocation") ?? dir;
                    }
                }

                // 删除快捷方式
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                try { File.Delete(Path.Combine(desktop, AppTitle + ".lnk")); } catch { }
                try { Directory.Delete(Path.Combine(startMenu, AppTitle), true); } catch { }

                // 删除注册表
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName); } catch { }

                // 延迟删除文件（自身 Uninstall.exe 在运行中）
                string self = ProcessPath();
                var toDelete = new List<string>();
                foreach (var f in Directory.GetFiles(dir))
                    if (!string.Equals(f, self, StringComparison.OrdinalIgnoreCase)) toDelete.Add(f);
                foreach (var f in toDelete) { try { File.Delete(f); } catch { } }
                try { Directory.Delete(dir, true); } catch { }
                // 自删：cmd 延迟删除
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c ping 127.0.0.1 -n 2 > nul & rd /s /q \"" + dir + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                    });
                }
                catch { }

                System.Windows.Forms.MessageBox.Show("卸载完成。", AppTitle,
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("卸载出错：" + ex.Message, AppTitle);
            }
        }
    }
}
