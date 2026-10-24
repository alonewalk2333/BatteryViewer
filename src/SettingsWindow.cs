using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace BatteryViewer
{
    // WPF 磨砂玻璃设置窗（Win10 Acrylic 真模糊）
    public class SettingsWindow : Window
    {
        // --- Acrylic 混合 API ---
        [DllImport("user32.dll")]
        static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 3,
            ACCENT_ENABLE_BLURBEHIND = 4,
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        static void EnableAcrylic(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;

                // 1) 设置透明背景（Acrylic 需要）
                uint transparent = 0x00FFFFFF;
                var colorData = new WindowCompositionAttributeData
                {
                    Attribute = 19, // WCA_ACCENT_POLICY_COLOR
                    Data = Marshal.AllocHGlobal(sizeof(uint)),
                    SizeOfData = sizeof(uint),
                };
                Marshal.WriteInt32(colorData.Data, unchecked((int)transparent));
                SetWindowCompositionAttribute(hwnd, ref colorData);
                Marshal.FreeHGlobal(colorData.Data);

                // 2) 启用 Acrylic 模糊
                int accent = (int)AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
                var accentData = new WindowCompositionAttributeData
                {
                    Attribute = 20, // WCA_ACCENT_POLICY
                    Data = Marshal.AllocHGlobal(sizeof(int)),
                    SizeOfData = sizeof(int),
                };
                Marshal.WriteInt32(accentData.Data, accent);
                SetWindowCompositionAttribute(hwnd, ref accentData);
                Marshal.FreeHGlobal(accentData.Data);
            }
            catch { }
        }

        // 主题：0=跟随主界面(磨砂白) 1=深色
        public bool ResultFloatingEnabled;
        public bool ResultAutoStart;
        public int ResultFloatingDevice = -1;
        public bool ResultFloatingDark = true;
        public int ResultBgColor = 0;
        public string ResultBgPath = "";
        public string ResultBgMode = "auto";
        public bool ResultApiEnabled;
        public int ResultApiPort = 8787;
        public bool ResultAlertEnabled = true;
        public int ResultAlertThreshold = 20;
        public int ResultBlurStrength = 40;
        public int ResultTrayDevice = -1;
        public string ResultExitMode = "ask";
        public bool Cancelled;

        public SettingsWindow(int bgColor, string bgPath, int blur, int trayDevice, string exitMode,
            bool floatEnabled, bool autoStart, int floatDevice, bool floatDark, List<string> deviceNames,
            string bgMode = "auto", bool apiEnabled = false, int apiPort = 8787,
            bool alertEnabled = true, int alertThreshold = 20)
        {
            Title = "设置";
            Width = 480; Height = 520;
            MinWidth = 460; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;   // Acrylic 需要不透明窗口
            Background = new SolidColorBrush(Color.FromRgb(246, 247, 250));
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;

            ResultBgColor = bgColor;
            ResultBgPath = bgPath;
            ResultBgMode = string.IsNullOrEmpty(bgMode) ? "auto" : bgMode;
            ResultApiEnabled = apiEnabled;
            ResultApiPort = apiPort;
            ResultAlertEnabled = alertEnabled;
            ResultAlertThreshold = alertThreshold;
            ResultBlurStrength = blur;
            ResultTrayDevice = trayDevice;
            ResultExitMode = exitMode;
            ResultFloatingEnabled = floatEnabled;
            ResultAutoStart = autoStart;
            ResultFloatingDevice = floatDevice;
            ResultFloatingDark = floatDark;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });

            // ===== 标题栏 =====
            var titleBar = new Grid { Background = Brushes.Transparent };
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 1) { try { DragMove(); } catch { } } };
            var title = new TextBlock
            {
                Text = "设置",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 39, 47)),
                Margin = new Thickness(22, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var btnClose = MakeGhost("✕", 34, 30, 12);
            btnClose.HorizontalAlignment = HorizontalAlignment.Right;
            btnClose.Margin = new Thickness(0, 0, 14, 0);
            btnClose.Click += (s, e) => { Cancelled = true; Close(); };
            titleBar.Children.Add(title);
            titleBar.Children.Add(btnClose);
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            // ===== 内容 =====
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent,
            };
            var content = new StackPanel { Margin = new Thickness(22, 4, 22, 0) };

            // ---- 背景 ----
            content.Children.Add(SectionTitle("背景"));
            var cboColor = new ComboBox
            {
                Width = 200, Height = 30,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                DisplayMemberPath = "Name",
                ItemsSource = new[]
                {
                    new { Name = "白色" }, new { Name = "深灰" }, new { Name = "藏蓝" },
                    new { Name = "紫色" }, new { Name = "墨绿" }, new { Name = "暗红" },
                },
            };
            cboColor.SelectedIndex = Math.Max(0, Math.Min(bgColor, 5));
            content.Children.Add(cboColor);

            content.Children.Add(SmallLabel("启动时背景模式"));
            var cboBgMode = new ComboBox
            {
                Width = 260, Height = 30,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            cboBgMode.Items.Add("自动（设了背景图就用图片）");
            cboBgMode.Items.Add("始终纯色背景");
            cboBgMode.Items.Add("始终自定义图片");
            cboBgMode.SelectedIndex = ResultBgMode == "color" ? 1 : (ResultBgMode == "image" ? 2 : 0);
            content.Children.Add(cboBgMode);

            content.Children.Add(SmallLabel("背景图片（优先级高于纯色，任意分辨率）"));
            var imgRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var txtPath = new TextBox { Text = bgPath, Width = 290, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
            var btnBrowse = MakeGhost("浏览…", 72, 28, 0);
            btnBrowse.Margin = new Thickness(8, 0, 0, 0);
            btnBrowse.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*",
                };
                if (dlg.ShowDialog() == true) txtPath.Text = dlg.FileName;
            };
            var btnClear = MakeGhost("清除", 62, 28, 0);
            btnClear.Margin = new Thickness(8, 0, 0, 0);
            btnClear.Click += (s, e) => { txtPath.Text = ""; };
            imgRow.Children.Add(txtPath);
            imgRow.Children.Add(btnBrowse);
            imgRow.Children.Add(btnClear);
            content.Children.Add(imgRow);

            // 毛玻璃强度：标题行（左标题 + 右百分比），滑条独占一行
            var blurHeader = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            var lblBlurTitle = new TextBlock
            {
                Text = "毛玻璃强度",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 126, 138)),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var lblBlur = new TextBlock
            {
                Text = blur + "%",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 96, 108)),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            blurHeader.Children.Add(lblBlurTitle);
            blurHeader.Children.Add(lblBlur);
            content.Children.Add(blurHeader);

            var slider = new Slider
            {
                Minimum = 0, Maximum = 100, Value = blur,
                Margin = new Thickness(0, 4, 0, 0),
                Height = 26,
                TickFrequency = 25, IsSnapToTickEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            slider.ValueChanged += (s, e) => lblBlur.Text = ((int)slider.Value) + "%";
            content.Children.Add(slider);

            // ---- 托盘 ----
            content.Children.Add(SectionTitle("托盘"));
            content.Children.Add(SmallLabel("托盘图标显示哪台设备的电量"));
            var cboTray = new ComboBox { Width = 300, Height = 30, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            cboTray.Items.Add("自动（第一台读到的设备）");
            foreach (var n in deviceNames) cboTray.Items.Add(n);
            cboTray.SelectedIndex = (trayDevice < 0 || trayDevice >= deviceNames.Count) ? 0 : trayDevice + 1;
            content.Children.Add(cboTray);

            content.Children.Add(SmallLabel("点击关闭按钮时"));
            var cboExit = new ComboBox { Width = 300, Height = 30, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            cboExit.Items.Add("每次询问（保留后台 / 完全退出）");
            cboExit.Items.Add("直接保留在后台");
            cboExit.Items.Add("直接完全退出");
            cboExit.SelectedIndex = exitMode == "tray" ? 1 : (exitMode == "exit" ? 2 : 0);
            content.Children.Add(cboExit);

            // ---- 悬浮窗 ----
            content.Children.Add(SectionTitle("桌面悬浮窗"));
            var chkFloat = new CheckBox
            {
                Content = "在桌面右下角显示电量悬浮窗",
                IsChecked = floatEnabled,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
                Margin = new Thickness(0, 4, 0, 0),
            };
            content.Children.Add(chkFloat);

            content.Children.Add(SmallLabel("悬浮窗圆形图标显示哪台设备的电量"));
            var cboFloatDev = new ComboBox { Width = 300, Height = 30, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            cboFloatDev.Items.Add("自动（第一台读到的设备）");
            foreach (var n in deviceNames) cboFloatDev.Items.Add(n);
            cboFloatDev.SelectedIndex = (floatDevice < 0 || floatDevice >= deviceNames.Count) ? 0 : floatDevice + 1;
            content.Children.Add(cboFloatDev);

            content.Children.Add(SmallLabel("悬浮窗配色"));
            var cboFloatTheme = new ComboBox { Width = 200, Height = 30, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            cboFloatTheme.Items.Add("深色");
            cboFloatTheme.Items.Add("浅色（磨砂白）");
            cboFloatTheme.SelectedIndex = floatDark ? 0 : 1;
            content.Children.Add(cboFloatTheme);

            // ---- 本地 API ----
            content.Children.Add(SectionTitle("本地 API"));
            var chkApi = new CheckBox
            {
                Content = "开放本地 HTTP 接口（供其它程序查询电量）",
                IsChecked = ResultApiEnabled,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
                Margin = new Thickness(0, 4, 0, 0),
            };
            content.Children.Add(chkApi);
            content.Children.Add(SmallLabel("仅监听本机（localhost），外网无法访问。"));
            var apiRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            apiRow.Children.Add(new TextBlock
            {
                Text = "端口",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 126, 138)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var txtApiPort = new TextBox
            {
                Text = ResultApiPort.ToString(),
                Width = 80, Height = 28,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            apiRow.Children.Add(txtApiPort);
            content.Children.Add(apiRow);
            content.Children.Add(SmallLabel("调用：GET http://localhost:端口/api/battery 返回 JSON"));

            // ---- 低电量报警 ----
            content.Children.Add(SectionTitle("低电量报警"));
            var chkAlert = new CheckBox
            {
                Content = "电量低于阈值时弹托盘通知",
                IsChecked = ResultAlertEnabled,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
                Margin = new Thickness(0, 4, 0, 0),
            };
            content.Children.Add(chkAlert);
            var alertRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            alertRow.Children.Add(new TextBlock
            {
                Text = "报警阈值（%）",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 126, 138)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var txtAlert = new TextBox
            {
                Text = ResultAlertThreshold.ToString(),
                Width = 60, Height = 28,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            alertRow.Children.Add(txtAlert);
            content.Children.Add(alertRow);
            content.Children.Add(SmallLabel("每台设备只提醒一次，充回阈值 5% 以上后重置。"));

            // ---- 通用 ----
            content.Children.Add(SectionTitle("通用"));
            var chkAuto = new CheckBox
            {
                Content = "开机时自动启动本软件",
                IsChecked = autoStart,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 44, 52)),
                Margin = new Thickness(0, 4, 0, 0),
            };
            content.Children.Add(chkAuto);
            content.Children.Add(SmallLabel("写入当前用户启动项，不需要管理员权限。"));

            scroll.Content = content;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            // ===== 底部按钮 =====
            var footer = new Grid { Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)) };
            var btnOk = MakeAccent("应用", 96);
            btnOk.HorizontalAlignment = HorizontalAlignment.Right;
            btnOk.Margin = new Thickness(0, 0, 20, 0);
            btnOk.Click += (s, e) =>
            {
                ResultBgColor = cboColor.SelectedIndex;
                ResultBgMode = cboBgMode.SelectedIndex == 1 ? "color" : (cboBgMode.SelectedIndex == 2 ? "image" : "auto");
                ResultApiEnabled = chkApi.IsChecked == true;
                int apiP;
                if (int.TryParse(txtApiPort.Text.Trim(), out apiP) && apiP >= 1024 && apiP <= 65535)
                    ResultApiPort = apiP;
                else
                    ResultApiPort = 8787;
                ResultAlertEnabled = chkAlert.IsChecked == true;
                int alertP;
                ResultAlertThreshold = (int.TryParse(txtAlert.Text.Trim(), out alertP) && alertP >= 5 && alertP <= 90)
                    ? alertP : 20;
                ResultBgPath = txtPath.Text.Trim();
                ResultBlurStrength = (int)slider.Value;
                ResultTrayDevice = cboTray.SelectedIndex <= 0 ? -1 : cboTray.SelectedIndex - 1;
                ResultExitMode = cboExit.SelectedIndex == 1 ? "tray" : (cboExit.SelectedIndex == 2 ? "exit" : "ask");
                ResultFloatingEnabled = chkFloat.IsChecked == true;
                ResultFloatingDevice = cboFloatDev.SelectedIndex <= 0 ? -1 : cboFloatDev.SelectedIndex - 1;
                ResultFloatingDark = cboFloatTheme.SelectedIndex == 0;
                ResultAutoStart = chkAuto.IsChecked == true;
                Close();
            };
            var btnCancel = MakeGhost("取消", 80, 30, 12);
            btnCancel.HorizontalAlignment = HorizontalAlignment.Right;
            btnCancel.Margin = new Thickness(0, 0, 124, 0);
            btnCancel.Click += (s, e) => { Cancelled = true; Close(); };
            footer.Children.Add(btnOk);
            footer.Children.Add(btnCancel);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
            Loaded += (s, e) => { EnableAcrylic(this); };
        }

        static TextBlock SectionTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13.5, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 39, 47)),
                Margin = new Thickness(0, 16, 0, 6),
            };
        }

        static TextBlock SmallLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 126, 138)),
                Margin = new Thickness(0, 12, 0, 0),
            };
        }

        Button MakeGhost(string text, double w, double h, double fs)
        {
            var b = new Button
            {
                Content = text, Width = w, Height = h,
                FontSize = fs == 0 ? 12 : fs,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(28, 120, 126, 138)),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 50, 60)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 120, 126, 138)),
                BorderThickness = new Thickness(1),
            };
            var tpl = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetBinding(System.Windows.Controls.Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(System.Windows.Controls.Border.BorderBrushProperty,
                new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tpl.VisualTree = border;
            border.AppendChild(cp);
            b.Template = tpl;
            return b;
        }

        Button MakeAccent(string text, double w)
        {
            var b = new Button
            {
                Content = text, Width = w, Height = 32,
                FontSize = 12.5,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(88, 140, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            var tpl = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(7));
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
    }
}
