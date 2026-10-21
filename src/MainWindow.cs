using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Shell;

namespace BatteryViewer
{
    public class MainWindow : Window
    {
        // ---- 设置 ----
        string bgPath = "";
        int blurStrength = 40;   // 毛玻璃强度 0-100（0=关闭）
        int bgColorIndex = 0;
        string exitMode = "ask";
        int trayDeviceIndex = -1;
        string configPath;
        ImageSource bgImage;
        bool imageIsDark = true;   // 背景图平均亮度（浅图=false）
        bool realExit = false;
        bool lastDarkState = true;   // 主题兜底检测

        // 数据
        class DeviceRow
        {
            public HidDeviceInfo Info;
            public BatteryResult Result;
            public bool Readable;
            public string Name;
        }
        List<DeviceRow> lastRows = new List<DeviceRow>();
        bool refreshing = false;
        System.Windows.Forms.Timer timer;
        System.Windows.Forms.NotifyIcon trayIcon;

        // 6 种默认纯色背景
        static readonly Color[] BG_COLORS = new Color[]
        {
            Color.FromRgb(245, 246, 248),
            Color.FromRgb(24, 26, 32),
            Color.FromRgb(30, 40, 55),
            Color.FromRgb(40, 30, 50),
            Color.FromRgb(30, 45, 40),
            Color.FromRgb(55, 30, 30),
        };
        static readonly string[] BG_COLOR_NAMES = new string[]
        { "白色", "深灰", "藏蓝", "紫色", "墨绿", "暗红" };

        // UI 引用
        Border rootBorder;
        Grid rootGrid;
        StackPanel cardList;
        TextBlock lblStatus;
        Button btnRefresh;
        StackPanel mainPanel;
        TextBlock titleLabel;
        Button btnSettings;
        Button btnMin;
        Button btnClose;
        Image bgImageCtrl;      // 背景图层（实时模糊）
        System.Windows.Shapes.Rectangle shadeRect; // 遮罩

        Color BgColor { get { return BG_COLORS[Math.Max(0, Math.Min(bgColorIndex, BG_COLORS.Length - 1))]; } }

        bool IsDarkBackground()
        {
            if (bgImage != null) return imageIsDark;
            Color c = BgColor;
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return lum < 128;
        }

        // 计算图片平均亮度（缩到 1x1 采样）
        static bool ComputeImageIsDark(BitmapSource src)
        {
            try
            {
                var small = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var g = dv.RenderOpen())
                    g.DrawImage(src, new Rect(0, 0, 1, 1));
                small.Render(dv);
                small.Freeze();
                var px = new byte[4];
                small.CopyPixels(px, 4, 0);
                // Pbgra32 字节序: B, G, R, A
                double lum = 0.299 * px[2] + 0.587 * px[1] + 0.114 * px[0];
                return lum < 140;
            }
            catch { return true; }
        }

        public MainWindow()
        {
            // 无边框圆角窗口
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Width = 880; Height = 640;
            MinWidth = 660; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI");

            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "batteryviewer.ini");
            LoadConfig();
            LoadBackground();

            BuildUi();

            // 托盘
            trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = MakeTrayIcon(null),
                Text = "2.4G 外设电量查看器",
                Visible = true,
            };
            trayIcon.DoubleClick += (s, e) => ShowMain();
            trayIcon.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowMain(); };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("打开主界面", null, (s, e) => ShowMain());
            menu.Items.Add("立即刷新", null, (s, e) => { if (!refreshing) RefreshAll(); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("完全退出", null, (s, e) => { realExit = true; trayIcon.Visible = false; System.Windows.Application.Current.Shutdown(); });
            trayIcon.ContextMenuStrip = menu;

            timer = new System.Windows.Forms.Timer { Interval = 60000 };
            timer.Tick += (s, e) => { if (!refreshing) RefreshAll(); };
            timer.Start();

            Loaded += (s, e) => RefreshAll();
        }

        // ---- UI 构建 ----
        void BuildUi()
        {
            bool dark = IsDarkBackground();

            rootBorder = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = MakeBackgroundBrush(),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
                BorderThickness = new Thickness(1),
            };

            var outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });   // 标题栏
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 卡片
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });   // 底栏

            // 分层背景：背景图层（实时模糊，不损失原图）+ 遮罩 + 内容
            rootGrid = new Grid();
            bgImageCtrl = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
            RenderOptions.SetBitmapScalingMode(bgImageCtrl, BitmapScalingMode.HighQuality);
            bgImageCtrl.Effect = blurStrength > 0 ? new BlurEffect { Radius = blurStrength * 0.6 } : null;
            shadeRect = new System.Windows.Shapes.Rectangle { Fill = Brushes.Transparent };
            Grid.SetRowSpan(bgImageCtrl, 3);
            Grid.SetRowSpan(shadeRect, 3);
            rootGrid.Children.Add(bgImageCtrl);
            rootGrid.Children.Add(shadeRect);
            rootGrid.Children.Add(outer);
            rootBorder.Child = rootGrid;

            // 标题栏（可拖动）
            var titleBar = new Grid { Background = Brushes.Transparent };
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 1) DragMove(); };
            var title = new TextBlock
            {
                Text = "2.4G 外设电量查看器",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(24, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            titleLabel = title;
            btnSettings = MakeGhostButton("设置", (s, e) => OpenSettings());
            btnMin = MakeGhostButton("—", (s, e) => WindowState = WindowState.Minimized);
            btnClose = MakeGhostButton("✕", (s, e) => OnUserClose());
            btnClose.Margin = new Thickness(0, 0, 16, 0);
            btnMin.Margin = new Thickness(0, 0, 8, 0);
            btnSettings.Margin = new Thickness(0, 0, 8, 0);
            var btnStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            btnStack.Children.Add(btnSettings);
            btnStack.Children.Add(btnMin);
            btnStack.Children.Add(btnClose);
            titleBar.Children.Add(title);
            titleBar.Children.Add(btnStack);
            Grid.SetRow(titleBar, 0);
            outer.Children.Add(titleBar);

            // 卡片区
            mainPanel = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = mainPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 0),
            };
            var scrollHost = new Grid { Background = Brushes.Transparent };
            scrollHost.Children.Add(scroll);
            Grid.SetRow(scrollHost, 1);
            outer.Children.Add(scrollHost);

            // 底栏
            var footer = new Grid { Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)) };
            btnRefresh = MakeAccentButton("刷新电量", (s, e) => { if (!refreshing) RefreshAll(); });
            btnRefresh.Margin = new Thickness(24, 0, 0, 0);
            btnRefresh.VerticalAlignment = VerticalAlignment.Center;
            btnRefresh.HorizontalAlignment = HorizontalAlignment.Left;
            lblStatus = new TextBlock
            {
                Text = "就绪",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 156, 168)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(160, 0, 0, 0),
            };
            footer.Children.Add(btnRefresh);
            footer.Children.Add(lblStatus);
            Grid.SetRow(footer, 2);
            outer.Children.Add(footer);

            Content = rootBorder;
            // Border 圆角不裁剪子元素，手动裁剪让背景图也带圆角
            rootBorder.SizeChanged += (s2, e2) =>
            {
                rootBorder.Clip = new RectangleGeometry(
                    new Rect(0, 0, e2.NewSize.Width, e2.NewSize.Height), 14, 14);
            };
            lastDarkState = IsDarkBackground();
            ApplyTheme();
        }

        // 标题栏文字/按钮颜色随主题（深色背景浅字，浅色背景深字）
        void ApplyTheme()
        {
            bool dark = IsDarkBackground();
            Color main = dark ? Color.FromRgb(235, 238, 245) : Color.FromRgb(40, 44, 52);
            if (titleLabel != null) titleLabel.Foreground = new SolidColorBrush(main);
            if (btnSettings != null)
            {
                btnSettings.Foreground = new SolidColorBrush(main);
                btnSettings.Background = new SolidColorBrush(dark ? Color.FromArgb(60, 128, 128, 128) : Color.FromArgb(40, 0, 0, 0));
                btnSettings.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(80, 128, 128, 128) : Color.FromArgb(60, 0, 0, 0));
            }
            if (btnMin != null)
            {
                btnMin.Foreground = new SolidColorBrush(main);
                btnMin.Background = new SolidColorBrush(dark ? Color.FromArgb(60, 128, 128, 128) : Color.FromArgb(40, 0, 0, 0));
                btnMin.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(80, 128, 128, 128) : Color.FromArgb(60, 0, 0, 0));
            }
            if (btnClose != null)
            {
                btnClose.Foreground = new SolidColorBrush(main);
                btnClose.Background = new SolidColorBrush(dark ? Color.FromArgb(60, 128, 128, 128) : Color.FromArgb(40, 0, 0, 0));
                btnClose.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(80, 128, 128, 128) : Color.FromArgb(60, 0, 0, 0));
            }
            lastDarkState = dark;
        }

        Button MakeGhostButton(string text, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                Width = 64,
                Height = 30,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                Foreground = new SolidColorBrush(Color.FromRgb(230, 233, 240)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)),
            };
            b.Click += onClick;
            var tpl = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tpl.VisualTree = border;
            border.AppendChild(cp);
            tpl.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(100, 128, 128, 160)), "bd") } });
            b.Template = tpl;
            return b;
        }

        Button MakeAccentButton(string text, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                Width = 120,
                Height = 34,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(88, 140, 255)),
                Foreground = Brushes.White,
                FontSize = 13,
                BorderThickness = new Thickness(0),
            };
            b.Click += onClick;
            var tpl = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tpl.VisualTree = border;
            border.AppendChild(cp);
            b.Template = tpl;
            return b;
        }

        Brush MakeBackgroundBrush()
        {
            return new SolidColorBrush(BgColor);
        }

        // ---- 背景处理（图片走 Image 控件实时模糊，不降采样不损失分辨率） ----
        void LoadBackground()
        {
            bgImage = null;
            if (!string.IsNullOrEmpty(bgPath) && File.Exists(bgPath))
            {
                try
                {
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(bgPath);
                    bmp.EndInit();
                    bmp.Freeze();
                    bgImage = bmp;
                    imageIsDark = ComputeImageIsDark(bmp);
                }
                catch { }
            }
            if (rootBorder != null)
            {
                rootBorder.Background = MakeBackgroundBrush();
                if (bgImageCtrl != null)
                {
                    if (bgImage != null)
                    {
                        bgImageCtrl.Source = bgImage;
                        bgImageCtrl.Visibility = Visibility.Visible;
                        bgImageCtrl.Effect = blurStrength > 0 ? new BlurEffect { Radius = blurStrength * 0.6 } : null;
                    }
                    else
                    {
                        bgImageCtrl.Source = null;
                        bgImageCtrl.Visibility = Visibility.Collapsed;
                    }
                }
                UpdateShade();
                ApplyTheme();
                RenderCards();
            }
        }

        // 遮罩：仅图片背景时压暗保证文字可读
        void UpdateShade()
        {
            if (shadeRect == null) return;
            shadeRect.Fill = (bgImage != null && IsDarkBackground())
                ? new SolidColorBrush(Color.FromArgb(120, 15, 17, 21))
                : Brushes.Transparent;
        }

        // ---- 刷新 ----
        public void RefreshAll()
        {
            refreshing = true;
            if (btnRefresh != null) btnRefresh.IsEnabled = false;
            if (lblStatus != null) lblStatus.Text = "正在枚举设备并读取电量…";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var rows = new List<DeviceRow>();
                try
                {
                    List<HidDeviceInfo> list;
                    try { list = HidApi.Enumerate(); }
                    catch { list = new List<HidDeviceInfo>(); }
                    foreach (var d in list)
                    {
                        BatteryResult r = null;
                        try { r = BatteryReader.ReadOne(d); }
                        catch { r = null; }
                        var row = new DeviceRow { Info = d, Result = r, Readable = r != null && r.Ok };
                        if (r != null && r.Extra != null && r.Extra.ContainsKey("name")) row.Name = r.Extra["name"];
                        rows.Add(row);
                    }
                    try
                    {
                        var ble = BluetoothReader.ReadAll();
                        foreach (var b in ble)
                        {
                            rows.Add(new DeviceRow
                            {
                                Info = new HidDeviceInfo { Vid = 0, Pid = 0, Path = "BLE " + b.Address },
                                Result = new BatteryResult
                                {
                                    Ok = true, Level = b.Level, Status = "unknown",
                                    Source = "蓝牙 BLE 电池服务（0x180F）",
                                    Detail = "蓝牙地址 " + b.Address,
                                },
                                Readable = true,
                                Name = b.Name,
                            });
                        }
                    }
                    catch { }
                }
                catch { }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    lastRows = rows;
                    RenderCards();
                    int ok = 0;
                    foreach (var r in rows) if (r.Readable) ok++;
                    lblStatus.Text = ok > 0 ? "读到 " + ok + " 台设备的电量" : "没有读到电量（设备可能休眠，动一下鼠标再刷新）";
                    btnRefresh.IsEnabled = true;
                    UpdateTray();
                }));
                refreshing = false;
            });
        }

        void RenderCards()
        {
            mainPanel.Children.Clear();
            bool dark = IsDarkBackground();
            int shown = 0;

            foreach (var row in lastRows)
            {
                if (!row.Readable || row.Result == null) continue;
                mainPanel.Children.Add(MakeCard(row, dark));
                shown++;
            }

            if (shown == 0)
            {
                var tip = new TextBlock
                {
                    Text = "没有读到任何设备的电量\n\n可能原因：\n· 2.4G 接收器上没有配对/在线的设备（动一下鼠标）\n· 设备协议暂未支持",
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 156, 168)),
                    FontSize = 14,
                    Margin = new Thickness(24, 20, 24, 20),
                };
                mainPanel.Children.Add(tip);
            }
        }

        UIElement MakeCard(DeviceRow row, bool dark)
        {
            string name = DeviceLabel(row.Info);
            if (!string.IsNullOrEmpty(row.Name)) name = row.Name;
            string meta = row.Info.Path.StartsWith("BLE ")
                ? "蓝牙 LE  ·  " + row.Info.Path.Substring(4)
                : row.Info.VidPid + "  ·  " + row.Info.UsagePage.ToString("X4") + ":" + row.Info.Usage.ToString("X4");
            string sub = row.Result.Source;
            if (row.Result.VoltageMv != null) sub += "  ·  " + (row.Result.VoltageMv.Value / 1000.0).ToString("0.00") + " V";

            var card = new Border
            {
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(24, 8, 24, 8),
                Padding = new Thickness(20, 14, 20, 14),
                Cursor = Cursors.Hand,
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = dark ? 0.5 : 0.15, Color = Colors.Black },
            };
            if (dark)
                card.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.Self),
                    Path = new PropertyPath("(0)", UIElement.IsMouseOverProperty),
                    Converter = MouseOverConverter(dark),
                });
            card.Background = dark
                ? new SolidColorBrush(Color.FromArgb(190, 32, 36, 46))
                : new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左列
            var left = new StackPanel();
            var tbName = new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeights.Bold };
            var tbMeta = new TextBlock { Text = meta, FontSize = 11, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 4, 0, 0) };
            Color textColor = dark ? Color.FromRgb(235, 238, 245) : Color.FromRgb(40, 44, 52);
            Color dimColor = dark ? Color.FromRgb(150, 156, 168) : Color.FromRgb(110, 116, 128);
            tbName.Foreground = new SolidColorBrush(textColor);
            tbMeta.Foreground = new SolidColorBrush(dimColor);
            left.Children.Add(tbName);
            left.Children.Add(tbMeta);

            // 进度条（左列底部）
            if (row.Result.Level != null)
            {
                int level = row.Result.Level.Value;
                var trackGrid = new Grid { Margin = new Thickness(0, 16, 0, 0), Height = 6 };
                var track = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(dark ? Color.FromRgb(70, 74, 84) : Color.FromRgb(220, 224, 230)),
                    RadiusX = 3, RadiusY = 3,
                };
                var fill = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(LevelColor(level)),
                    RadiusX = 3, RadiusY = 3,
                    Width = Math.Max(2, trackGrid.ActualWidth * level / 100.0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                trackGrid.Children.Add(track);
                trackGrid.Children.Add(fill);
                track.SizeChanged += (s, e) => { fill.Width = Math.Max(2, track.ActualWidth * level / 100.0); };
                left.Children.Add(trackGrid);
            }
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            // 右列：大百分比
            var right = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (row.Result.Level != null)
            {
                int level = row.Result.Level.Value;
                var tbPct = new TextBlock
                {
                    Text = level + "%",
                    FontSize = 40,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(LevelColor(level)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                var tbSub = new TextBlock
                {
                    Text = sub,
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(dimColor),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 300,
                };
                right.Children.Add(tbPct);
                right.Children.Add(tbSub);
            }
            else if (row.Result.VoltageMv != null)
            {
                var tbV = new TextBlock
                {
                    Text = (row.Result.VoltageMv.Value / 1000.0).ToString("0.00") + " V",
                    FontSize = 30,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(88, 140, 255)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                right.Children.Add(tbV);
            }
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            card.Child = grid;
            card.MouseLeftButtonUp += (s, e) => ShowDetail(row);
            return card;
        }

        static MouseOverColorConverter MouseOverConverter(bool dark)
        {
            return new MouseOverColorConverter { Dark = dark };
        }

        class MouseOverColorConverter : System.Windows.Data.IValueConverter
        {
            public bool Dark;
            public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
            {
                bool over = value is bool && (bool)value;
                if (Dark) return new SolidColorBrush(over ? Color.FromArgb(220, 44, 48, 60) : Color.FromArgb(190, 32, 36, 46));
                return new SolidColorBrush(over ? Color.FromArgb(250, 255, 255, 255) : Color.FromArgb(230, 255, 255, 255));
            }
            public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) { throw new NotImplementedException(); }
        }

        Color LevelColor(int level)
        {
            if (level >= 60) return Color.FromRgb(46, 204, 113);
            if (level >= 30) return Color.FromRgb(241, 196, 15);
            return Color.FromRgb(231, 76, 60);
        }

        public static string DeviceLabel(HidDeviceInfo d)
        {
            if (d.Vid == 0x046D) return "罗技设备";
            if (d.Vid == 0x373B) return "ATK / VXE 鼠标";
            if (d.Vid == 0x3554) return "VXE / VGN 鼠标";
            if (d.Vid == 0x3367) return "Endgame Gear";
            if (d.Vid == 0x3057) return "VAXEE / ZYGEN";
            if (d.Vid == 0x1915) return "Nordic nRF 接收器";
            if (d.Vid == Darkrim.VID) return "DARKRIM 耳机";
            if (d.Vid == 0x05AC) return "Apple 设备";
            return "HID 设备";
        }

        // ---- 详情 ----
        void ShowDetail(DeviceRow row)
        {
            var sb = new StringBuilder();
            string name = DeviceLabel(row.Info);
            if (!string.IsNullOrEmpty(row.Name)) name = row.Name;
            sb.AppendLine("设备: " + name);
            sb.AppendLine("VID:PID: " + row.Info.VidPid);
            sb.AppendLine("路径: " + row.Info.Path);
            sb.AppendLine();
            if (row.Result != null)
            {
                sb.AppendLine("读取来源: " + row.Result.Source);
                sb.AppendLine("说明: " + row.Result.Detail);
            }
            else sb.AppendLine("没有可用的电量读取方式");
            System.Windows.Forms.MessageBox.Show(sb.ToString(), "设备详情");
        }

        // ---- 退出 ----
        public bool RealExitRequested { get { return realExit; } }

        public void DisposeTray()
        {
            if (trayIcon != null) { try { trayIcon.Visible = false; trayIcon.Dispose(); } catch { } }
        }

        // 主题兜底：渲染循环持续检测明暗变化，漏网的主题更新在这里强制生效
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            bool dark = IsDarkBackground();
            if (dark != lastDarkState)
            {
                lastDarkState = dark;
                ApplyTheme();
                UpdateShade();
                RenderCards();
                InvalidateVisual();
            }
        }

        public void OnUserClose()
        {
            if (exitMode == "tray") { HideToTray(); return; }
            if (exitMode == "exit") { realExit = true; trayIcon.Visible = false; System.Windows.Application.Current.Shutdown(); return; }
            string result = ShowExitDialog();
            if (result == "tray") HideToTray();
            else if (result == "exit") { realExit = true; trayIcon.Visible = false; System.Windows.Application.Current.Shutdown(); }
        }

        void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        public void ShowMain()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            Activate();
        }

        string ShowExitDialog()
        {
            // WPF 原生对话框：颜色直接从当前背景派生，跟随主题
            bool dark = IsDarkBackground();
            Color baseBg = BgColor;

            // 对话框底色 = 背景色提亮/压暗一档，形成同色系层次
            Color dlgBg = dark
                ? Color.FromRgb(
                    (byte)Math.Min(255, baseBg.R + 34),
                    (byte)Math.Min(255, baseBg.G + 34),
                    (byte)Math.Min(255, baseBg.B + 44))
                : Color.FromRgb(
                    (byte)Math.Max(0, baseBg.R - 30),
                    (byte)Math.Max(0, baseBg.G - 30),
                    (byte)Math.Max(0, baseBg.B - 30));
            Color txc = dark ? Color.FromRgb(235, 238, 245) : Color.FromRgb(40, 44, 52);
            Color dimc = dark ? Color.FromRgb(150, 156, 168) : Color.FromRgb(110, 116, 128);

            string result = null;
            var dlg = new Window
            {
                Title = "退出程序",
                Width = 400, Height = 200,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
            };

            var outer = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(dlgBg),
                BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(140, 255, 255, 255) : Color.FromArgb(120, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 3, Opacity = 0.45, Color = Colors.Black },
            };
            var root = new Grid { Margin = new Thickness(24, 18, 24, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock { Text = "退出程序", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(txc) };
            var lbl = new TextBlock
            {
                Text = "要保留在后台还是完全退出？保留在后台可以从托盘随时查看电量。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(dimc),
                FontSize = 12.5,
                Margin = new Thickness(0, 10, 0, 0),
            };
            var chk = new CheckBox
            {
                Content = "保持我的默认选项，下次无需提示",
                Foreground = new SolidColorBrush(txc),
                Margin = new Thickness(0, 14, 0, 0),
            };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var btnTray = new Button
            {
                Content = "保留在后台",
                Width = 140, Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(88, 140, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 12, 0),
            };
            var btnExit = new Button
            {
                Content = "完全退出",
                Width = 140, Height = 34,
                Background = new SolidColorBrush(dark ? Color.FromArgb(60, 128, 128, 128) : Color.FromArgb(40, 0, 0, 0)),
                Foreground = new SolidColorBrush(txc),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            btns.Children.Add(btnTray);
            btns.Children.Add(btnExit);

            Grid.SetRow(title, 0);
            Grid.SetRow(lbl, 1);
            Grid.SetRow(chk, 2);
            Grid.SetRow(btns, 3);
            root.Children.Add(title);
            root.Children.Add(lbl);
            root.Children.Add(chk);
            root.Children.Add(btns);
            outer.Child = root;
            dlg.Content = outer;

            // 标题栏拖动
            root.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 1) { try { dlg.DragMove(); } catch { } } };

            btnTray.Click += (s, e) => { result = "tray"; dlg.Close(); };
            btnExit.Click += (s, e) => { result = "exit"; dlg.Close(); };
            dlg.Closed += (s, e) =>
            {
                if (result != null && chk.IsChecked == true) { exitMode = result; SaveConfig(); }
            };

            // 模态：禁用主窗交互
            IsHitTestVisible = false;
            try { dlg.ShowDialog(); } catch { }
            IsHitTestVisible = true;
            return result;
        }

        // ---- 托盘 ----
        System.Drawing.Icon MakeTrayIcon(int? level)
        {
            System.Drawing.Color bgc = level == null ? System.Drawing.Color.FromArgb(90, 100, 115)
                : System.Drawing.Color.FromArgb(LevelColor(level.Value).R, LevelColor(level.Value).G, LevelColor(level.Value).B);
            using (var bmp = new System.Drawing.Bitmap(32, 32))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new System.Drawing.SolidBrush(bgc))
                    g.FillEllipse(b, 0, 0, 31, 31);
                string txt = level == null ? "…" : level.Value.ToString();
                if (level != null && level.Value == 100) txt = "F";
                using (var f = new System.Drawing.Font("Segoe UI", txt.Length >= 2 ? 12f : 15f, System.Drawing.FontStyle.Bold))
                using (var b = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                {
                    var sz = g.MeasureString(txt, f);
                    g.DrawString(txt, f, b, (32 - sz.Width) / 2, (32 - sz.Height) / 2);
                }
                return System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }

        void UpdateTray()
        {
            if (trayIcon == null) return;
            try
            {
                DeviceRow target = null;
                int idx = 0;
                DeviceRow t = null;
                foreach (var r in lastRows)
                {
                    if (!r.Readable || r.Result == null || r.Result.Level == null) continue;
                    if (trayDeviceIndex < 0) { t = r; break; }
                    if (idx == trayDeviceIndex) { t = r; break; }
                    idx++;
                }
                if (t == null)
                    foreach (var r in lastRows)
                        if (r.Readable && r.Result != null && r.Result.Level != null) { t = r; break; }

                int? iconLevel = t != null ? t.Result.Level : null;

                var sb = new StringBuilder();
                foreach (var r in lastRows)
                {
                    if (!r.Readable || r.Result == null) continue;
                    string name = !string.IsNullOrEmpty(r.Name) ? r.Name : DeviceLabel(r.Info);
                    name = name.Split(' ')[0];
                    if (name.Length > 10) name = name.Substring(0, 10);
                    if (r.Result.Level != null)
                    {
                        sb.Append(name + " " + r.Result.Level + "%");
                        if (r.Result.VoltageMv != null) sb.Append("(" + (r.Result.VoltageMv.Value / 1000.0).ToString("0.00") + "V)");
                        sb.Append("  ");
                    }
                }
                string tip = sb.ToString().TrimEnd();
                if (tip.Length == 0) tip = "没有读到电量（设备可能休眠）";
                if (tip.Length > 60) tip = tip.Substring(0, 57) + "...";
                trayIcon.Text = tip;
                var old = trayIcon.Icon;
                trayIcon.Icon = MakeTrayIcon(iconLevel);
                if (old != null) { try { old.Dispose(); } catch { } }
            }
            catch { }
        }

        // ---- 设置 ----
        public void OpenSettings()
        {
            bool dark = IsDarkBackground();
            var form = new System.Windows.Forms.Form
            {
                Text = "设置",
                Size = new System.Drawing.Size(420, 330),
                MinimumSize = new System.Drawing.Size(380, 280),
                StartPosition = System.Windows.Forms.FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = System.Drawing.Color.FromArgb(
                    (int)(dark ? 30 : 248), (int)(dark ? 32 : 249), (int)(dark ? 40 : 251)),
            };

            var tabs = new System.Windows.Forms.TabControl { Dock = System.Windows.Forms.DockStyle.Fill };
            var tabBg = new System.Windows.Forms.TabPage("背景");
            var tabTray = new System.Windows.Forms.TabPage("托盘");

            // 背景页
            var lblColor = new System.Windows.Forms.Label { Text = "纯色背景：", Location = new System.Drawing.Point(16, 16), AutoSize = true };
            var cboColor = new WheelDisabledComboBox { Location = new System.Drawing.Point(16, 40), Width = 200, DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList };
            for (int i = 0; i < BG_COLORS.Length; i++) cboColor.Items.Add(BG_COLOR_NAMES[i]);
            cboColor.SelectedIndex = Math.Max(0, Math.Min(bgColorIndex, BG_COLORS.Length - 1));
            var preview = new System.Windows.Forms.Panel { Location = new System.Drawing.Point(228, 40), Size = new System.Drawing.Size(70, 24), BackColor = System.Drawing.Color.FromArgb(BgColor.R, BgColor.G, BgColor.B), BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle };
            cboColor.SelectedIndexChanged += (s, e) => preview.BackColor = System.Drawing.Color.FromArgb(BG_COLORS[cboColor.SelectedIndex].R, BG_COLORS[cboColor.SelectedIndex].G, BG_COLORS[cboColor.SelectedIndex].B);

            var lblImg = new System.Windows.Forms.Label { Text = "背景图片（优先级高于纯色，任意分辨率均可）：", Location = new System.Drawing.Point(16, 80), AutoSize = true };
            var txt = new System.Windows.Forms.TextBox { Text = bgPath, Location = new System.Drawing.Point(16, 104), Width = 230 };
            var btnBrowse = new System.Windows.Forms.Button { Text = "浏览…", Location = new System.Drawing.Point(254, 102), Size = new System.Drawing.Size(66, 26) };
            var lblBlur = new System.Windows.Forms.Label { Text = "毛玻璃强度：", Location = new System.Drawing.Point(16, 144), AutoSize = true };
            var sliderBlur = new System.Windows.Forms.TrackBar { Location = new System.Drawing.Point(110, 136), Width = 170, Height = 36, Minimum = 0, Maximum = 100, TickFrequency = 25, Value = blurStrength };
            var lblBlurVal = new System.Windows.Forms.Label { Text = blurStrength + "%", Location = new System.Drawing.Point(288, 144), AutoSize = true };
            sliderBlur.ValueChanged += (s2, e2) =>
            {
                blurStrength = sliderBlur.Value;
                lblBlurVal.Text = blurStrength + "%";
                if (bgImageCtrl != null)
                    bgImageCtrl.Effect = blurStrength > 0 ? new BlurEffect { Radius = blurStrength * 0.6 } : null;
            };
            var btnClear = new System.Windows.Forms.Button { Text = "清除背景图", Location = new System.Drawing.Point(16, 178), Size = new System.Drawing.Size(110, 28) };
            tabBg.Controls.AddRange(new System.Windows.Forms.Control[] { lblColor, cboColor, preview, lblImg, txt, btnBrowse, lblBlur, sliderBlur, lblBlurVal, btnClear });

            // 托盘页
            var lblTray = new System.Windows.Forms.Label { Text = "托盘图标显示哪台设备的电量：", Location = new System.Drawing.Point(16, 20), AutoSize = true };
            var cboTray = new WheelDisabledComboBox { Location = new System.Drawing.Point(16, 48), Width = 260, DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList };
            cboTray.Items.Add("自动（第一台读到的设备）");
            foreach (var r in lastRows)
            {
                if (!r.Readable || r.Result == null) continue;
                cboTray.Items.Add(!string.IsNullOrEmpty(r.Name) ? r.Name : DeviceLabel(r.Info));
            }
            if (trayDeviceIndex < 0) cboTray.SelectedIndex = 0;
            else if (trayDeviceIndex < cboTray.Items.Count) cboTray.SelectedIndex = trayDeviceIndex + 1;
            else cboTray.SelectedIndex = 0;
            var lblHint = new System.Windows.Forms.Label
            {
                Text = "选中的设备电量会显示在托盘圆形图标上。\n悬停仍显示全部设备。",
                Location = new System.Drawing.Point(16, 90),
                AutoSize = true,
            };
            // 关闭窗口行为
            var lblExit = new System.Windows.Forms.Label { Text = "点击关闭按钮时：", Location = new System.Drawing.Point(16, 140), AutoSize = true };
            var cboExit = new WheelDisabledComboBox { Location = new System.Drawing.Point(16, 166), Width = 260, DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList };
            cboExit.Items.Add("每次询问（保留后台 / 完全退出）");
            cboExit.Items.Add("直接保留在后台");
            cboExit.Items.Add("直接完全退出");
            if (exitMode == "tray") cboExit.SelectedIndex = 1;
            else if (exitMode == "exit") cboExit.SelectedIndex = 2;
            else cboExit.SelectedIndex = 0;
            tabTray.Controls.AddRange(new System.Windows.Forms.Control[] { lblTray, cboTray, lblHint, lblExit, cboExit });

            tabs.TabPages.Add(tabBg);
            tabs.TabPages.Add(tabTray);

            var btnPanel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Bottom, Height = 48 };
            var btnOk = new System.Windows.Forms.Button { Text = "应用", Size = new System.Drawing.Size(90, 30), Anchor = System.Windows.Forms.AnchorStyles.Right | System.Windows.Forms.AnchorStyles.Bottom, BackColor = System.Drawing.Color.FromArgb(88, 140, 255), ForeColor = System.Drawing.Color.White, FlatStyle = System.Windows.Forms.FlatStyle.Flat };
            btnOk.FlatAppearance.BorderSize = 0;
            var btnCancel = new System.Windows.Forms.Button { Text = "取消", Size = new System.Drawing.Size(90, 30), Anchor = System.Windows.Forms.AnchorStyles.Right | System.Windows.Forms.AnchorStyles.Bottom, FlatStyle = System.Windows.Forms.FlatStyle.Flat };
            btnPanel.Controls.Add(btnOk);
            btnPanel.Controls.Add(btnCancel);
            btnPanel.Resize += (s, e) =>
            {
                btnOk.Location = new System.Drawing.Point(btnPanel.Width - 102, 9);
                btnCancel.Location = new System.Drawing.Point(btnPanel.Width - 198, 9);
            };

            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new System.Windows.Forms.OpenFileDialog())
                {
                    dlg.Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*";
                    if (dlg.ShowDialog(form) == System.Windows.Forms.DialogResult.OK) txt.Text = dlg.FileName;
                }
            };
            btnClear.Click += (s, e) => { txt.Text = ""; };
            btnOk.Click += (s, e) =>
            {
                bgColorIndex = cboColor.SelectedIndex < 0 ? 0 : cboColor.SelectedIndex;
                bgPath = txt.Text.Trim();
                // blurStrength 已由滑条 ValueChanged 实时更新
                trayDeviceIndex = cboTray.SelectedIndex <= 0 ? -1 : cboTray.SelectedIndex - 1;
                exitMode = cboExit.SelectedIndex == 1 ? "tray" : (cboExit.SelectedIndex == 2 ? "exit" : "ask");
                SaveConfig();
                LoadBackground();
                UpdateTray();
                // 重建 UI 背景
                rootBorder.Background = MakeBackgroundBrush();
                RenderCards();
                form.Close();
            };
            btnCancel.Click += (s, e) => form.Close();

            form.Controls.Add(btnPanel);
            form.Controls.Add(tabs);
            form.ShowDialog();
        }

        // ---- 配置 ----
        void SaveConfig()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("bgpath=" + bgPath);
                sb.AppendLine("blur=" + blurStrength);
                sb.AppendLine("color=" + bgColorIndex);
                sb.AppendLine("exitmode=" + exitMode);
                sb.AppendLine("traydevice=" + trayDeviceIndex);
                File.WriteAllText(configPath, sb.ToString());
            }
            catch { }
        }

        void LoadConfig()
        {
            try
            {
                if (!File.Exists(configPath)) return;
                foreach (var line in File.ReadAllLines(configPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k == "bgpath") bgPath = v;
                    else if (k == "blur") int.TryParse(v, out blurStrength);
                    else if (k == "color") int.TryParse(v, out bgColorIndex);
                    else if (k == "exitmode") exitMode = v;
                    else if (k == "traydevice") int.TryParse(v, out trayDeviceIndex);
                }
            }
            catch { }
        }
    }
}

// 禁用鼠标滚轮换选项的 ComboBox（防误滑）
public class WheelDisabledComboBox : System.Windows.Forms.ComboBox
{
    protected override void OnMouseWheel(System.Windows.Forms.MouseEventArgs e)
    {
        // 吞掉滚轮事件，不改变选中项
        ((System.Windows.Forms.HandledMouseEventArgs)e).Handled = true;
        base.OnMouseWheel(e);
    }
}
