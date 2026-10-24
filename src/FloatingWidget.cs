using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BatteryViewer
{
    // 桌面悬浮窗：常驻右下角，显示各设备电量
    // 用「不透明窗口 + Win32 圆角裁剪」而非 AllowsTransparency
    // 原因：AllowsTransparency 透明窗口的鼠标命中测试不可靠，会导致拖不动
    public class FloatingWidget : Window
    {
        public event Action CloseRequested;

        // --- Win32 圆角裁剪 ---
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int w, int h);
        [DllImport("user32.dll")]
        static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        // --- Win32 窗口过程替换：系统级拖动（不依赖 WPF 鼠标事件） ---
        delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp);
        const int GWLP_WNDPROC = -4;

        static WndProcDelegate wndProcKeeper;   // 必须 static 持有，防止被 GC
        static IntPtr originalWndProc;

        const int WM_NCHITTEST = 0x0084;
        const int WM_NCLBUTTONDOWN = 0x00A1;
        const int WM_NCLBUTTONUP = 0x00A2;
        const int WM_NCLBUTTONDBLCLK = 0x00A3;
        const int WM_NCMOUSEMOVE = 0x00A0;
        const int HTCAPTION = 2;
        const int HTCLIENT = 1;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        Point dragAnchor;      // 按下时的屏幕坐标
        bool ncPressed = false;
        bool ncDragged = false;

        class Item
        {
            public string Name;
            public int Level;
        }

        List<Item> items = new List<Item>();
        StackPanel listPanel;
        TextBlock headerText;
        TextBlock tipText;
        Grid headerGrid;
        Grid tipGrid;
        Border root;
        bool collapsed = true;
        bool dark = true;

        const double CollapsedSize = 68;
        const double ExpandedWidth = 256;

        public FloatingWidget()
        {
            Title = "电量悬浮窗";
            Width = CollapsedSize;
            Height = CollapsedSize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;          // 关键：不用透明窗口
            Background = new SolidColorBrush(Color.FromArgb(255, 22, 25, 33));
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            FontFamily = new FontFamily("Segoe UI");

            root = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 22, 25, 33)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- 标题栏 ----
            headerGrid = new Grid { Margin = new Thickness(14, 9, 10, 2) };
            headerText = new TextBlock
            {
                Text = "设备电量",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 156, 168)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var btnClose = new Button
            {
                Content = "✕", Width = 22, Height = 22,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 156, 168)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                ToolTip = "关闭悬浮窗",
            };
            btnClose.Click += (s, e) => { if (CloseRequested != null) CloseRequested(); };
            headerGrid.Children.Add(headerText);
            headerGrid.Children.Add(btnClose);
            Grid.SetRow(headerGrid, 0);
            grid.Children.Add(headerGrid);

            // ---- 电量列表 ----
            listPanel = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
            var scroll = new ScrollViewer
            {
                Content = listPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent,
                MaxHeight = 260,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            // ---- 底部提示 ----
            tipGrid = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            tipText = new TextBlock
            {
                Text = "按住拖动 · 单击收起",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 110, 116, 128)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 8),
            };
            tipGrid.Children.Add(tipText);
            Grid.SetRow(tipGrid, 2);
            grid.Children.Add(tipGrid);

            root.Child = grid;
            Content = root;

            root.SizeChanged += (s, e) => ApplyRoundCorner();

            // 替换窗口过程：系统级拖动
            // 必须等 SourceInitialized（WPF 的 HwndSource 建好、装完自己的 WndProc 之后再替换）
            SourceInitialized += (s, e) =>
            {
                try
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    if (wndProcKeeper == null) wndProcKeeper = new WndProcDelegate(WndProc);
                    originalWndProc = GetWindowLongPtr(hwnd, GWLP_WNDPROC);
                    SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(wndProcKeeper));
                }
                catch { }
            };

            // 鼠标移出延时收起（仅展开态）
            var collapseTimer = new System.Windows.Forms.Timer { Interval = 250 };
            bool pointerInside = false;
            collapseTimer.Tick += (s, e) =>
            {
                if (!pointerInside && !collapsed)
                {
                    collapseTimer.Stop();
                    SetCollapsed(true);
                }
            };
            MouseEnter += (s, e) => { pointerInside = true; collapseTimer.Stop(); };
            MouseLeave += (s, e) => { pointerInside = false; if (!collapsed) collapseTimer.Start(); };

            ApplyCollapsedLayout();
        }

        // 窗口过程：命中测试 + 系统级拖动
        IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp)
        {
            try
            {
                if (msg == WM_NCHITTEST)
                {
                    // 屏幕坐标
                    int sx = (short)(lp.ToInt64() & 0xFFFF);
                    int sy = (short)((lp.ToInt64() >> 16) & 0xFFFF);
                    if (InDragZone(sx, sy)) return (IntPtr)HTCAPTION;
                    return (IntPtr)HTCLIENT;
                }
                if (msg == WM_NCLBUTTONDOWN)
                {
                    ncPressed = true;
                    ncDragged = false;
                    dragAnchor = new Point(
                        (short)(lp.ToInt64() & 0xFFFF),
                        (short)((lp.ToInt64() >> 16) & 0xFFFF));
                }
                else if (msg == WM_NCMOUSEMOVE && ncPressed)
                {
                    Point cur = new Point(
                        (short)(lp.ToInt64() & 0xFFFF),
                        (short)((lp.ToInt64() >> 16) & 0xFFFF));
                    if (Math.Abs(cur.X - dragAnchor.X) > 4 || Math.Abs(cur.Y - dragAnchor.Y) > 4)
                        ncDragged = true;
                }
                else if (msg == WM_NCLBUTTONUP)
                {
                    if (ncPressed && !ncDragged)
                    {
                        // 没拖动 = 点击 → 展开/收起
                        SetCollapsed(!collapsed);
                    }
                    ncPressed = false;
                    ncDragged = false;
                }
                else if (msg == WM_NCLBUTTONDBLCLK)
                {
                    return IntPtr.Zero;  // 屏蔽双击最大化
                }
            }
            catch { }
            if (originalWndProc != IntPtr.Zero)
                return CallWindowProc(originalWndProc, hwnd, msg, wp, lp);
            return IntPtr.Zero;
        }

        // 可拖动区域：折叠态整个圆球；展开态仅标题栏和底部（排除关闭按钮）
        bool InDragZone(int screenX, int screenY)
        {
            try
            {
                var local = PointFromScreen(new Point(screenX, screenY));
                if (collapsed)
                    return local.X >= 0 && local.Y >= 0 && local.X <= ActualWidth && local.Y <= ActualHeight;
                // 右上角关闭按钮区域不当作拖动区
                if (local.Y <= 42 && local.X > ActualWidth - 44) return false;
                return local.Y <= 42 || local.Y >= ActualHeight - 32;
            }
            catch { return false; }
        }

        // Win32 圆角裁剪
        void ApplyRoundCorner()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                RECT r;
                if (!GetWindowRect(hwnd, out r)) return;
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                int radius = collapsed ? (int)CollapsedSize : 16;
                var rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, radius * 2, radius * 2);
                SetWindowRgn(hwnd, rgn, true);
            }
            catch { }
        }

        void SetCollapsed(bool value)
        {
            if (collapsed == value) { ApplyCollapsedLayout(); return; }
            collapsed = value;
            ApplyCollapsedLayout();
        }

        void ApplyCollapsedLayout()
        {
            if (collapsed)
            {
                Width = CollapsedSize;
                Height = CollapsedSize;
                SizeToContent = SizeToContent.Manual;
                headerGrid.Visibility = Visibility.Collapsed;
                tipGrid.Visibility = Visibility.Collapsed;
                listPanel.Margin = new Thickness(0);
                listPanel.Children.Clear();
                int? lv = items.Count > 0 ? (int?)items[0].Level : null;
                listPanel.Children.Add(MakeBadge(lv));
            }
            else
            {
                Width = ExpandedWidth;
                Height = double.NaN;
                SizeToContent = SizeToContent.Height;
                headerGrid.Visibility = Visibility.Visible;
                tipGrid.Visibility = Visibility.Visible;
                listPanel.Margin = new Thickness(10, 0, 10, 0);
                RenderItems();
            }
            ApplyRoundCorner();
            if (IsVisible) PlaceBottomRight();
        }

        // 圆形徽标
        Grid MakeBadge(int? level)
        {
            var outer = new Grid
            {
                Width = CollapsedSize,
                Height = CollapsedSize,
                Background = Brushes.Transparent,
            };
            var circle = new Ellipse
            {
                Width = 52, Height = 52,
                Fill = new SolidColorBrush(level.HasValue ? LevelBrush(level.Value)
                    : (dark ? Color.FromArgb(255, 90, 100, 115) : Color.FromArgb(255, 200, 205, 212))),
            };
            circle.HorizontalAlignment = HorizontalAlignment.Center;
            circle.VerticalAlignment = VerticalAlignment.Center;
            outer.Children.Add(circle);
            var tb = new TextBlock
            {
                Text = level.HasValue ? (level.Value >= 100 ? "F" : level.Value.ToString()) : "—",
                FontSize = level.HasValue && level.Value >= 100 ? 16 : 19,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            outer.Children.Add(tb);
            return outer;
        }

        public void SetData(List<DeviceRowLite> rows)
        {
            items.Clear();
            foreach (var r in rows) items.Add(new Item { Name = r.Name, Level = r.Level });
            if (collapsed)
            {
                listPanel.Children.Clear();
                int? lv = items.Count > 0 ? (int?)items[0].Level : null;
                listPanel.Children.Add(MakeBadge(lv));
            }
            else RenderItems();
        }

        public void ApplyTheme(bool isDark)
        {
            dark = isDark;
            Color bg = isDark ? Color.FromArgb(255, 22, 25, 33) : Color.FromArgb(255, 250, 250, 252);
            Background = new SolidColorBrush(bg);
            root.Background = new SolidColorBrush(bg);
            root.BorderBrush = new SolidColorBrush(isDark
                ? Color.FromArgb(70, 255, 255, 255)
                : Color.FromArgb(90, 160, 165, 175));
            headerText.Foreground = new SolidColorBrush(isDark ? Color.FromArgb(255, 150, 156, 168) : Color.FromArgb(255, 110, 116, 128));
            tipText.Foreground = new SolidColorBrush(isDark ? Color.FromArgb(255, 110, 116, 128) : Color.FromArgb(255, 140, 146, 158));
            if (collapsed)
            {
                listPanel.Children.Clear();
                int? lv = items.Count > 0 ? (int?)items[0].Level : null;
                listPanel.Children.Add(MakeBadge(lv));
            }
            else RenderItems();
        }

        void RenderItems()
        {
            listPanel.Children.Clear();
            if (items.Count == 0)
            {
                listPanel.Children.Add(new TextBlock
                {
                    Text = "没有读到电量",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(dark ? Color.FromArgb(255, 120, 126, 138) : Color.FromArgb(255, 150, 156, 168)),
                    Margin = new Thickness(6, 10, 6, 10),
                });
                return;
            }
            foreach (var it in items)
            {
                var row = new Grid { Margin = new Thickness(4, 3, 4, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = it.Name,
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(dark ? Color.FromRgb(232, 235, 242) : Color.FromRgb(40, 44, 52)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = it.Name,
                };
                Grid.SetColumn(name, 0);
                row.Children.Add(name);

                var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var track = new Grid { Width = 44, Height = 5, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new Rectangle
                {
                    Fill = new SolidColorBrush(dark ? Color.FromArgb(255, 70, 74, 84) : Color.FromArgb(255, 215, 220, 226)),
                    RadiusX = 2.5, RadiusY = 2.5,
                });
                var fill = new Rectangle
                {
                    Fill = new SolidColorBrush(LevelBrush(it.Level)),
                    RadiusX = 2.5, RadiusY = 2.5,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                track.Children.Add(fill);
                track.SizeChanged += (s, e) => { fill.Width = Math.Max(2, 44 * it.Level / 100.0); };
                right.Children.Add(track);
                right.Children.Add(new TextBlock
                {
                    Text = it.Level + "%",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(LevelBrush(it.Level)),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 34,
                    TextAlignment = TextAlignment.Right,
                });
                Grid.SetColumn(right, 1);
                row.Children.Add(right);
                listPanel.Children.Add(row);
            }
        }

        static Color LevelBrush(int level)
        {
            if (level >= 60) return Color.FromRgb(46, 204, 113);
            if (level >= 30) return Color.FromRgb(241, 196, 15);
            return Color.FromRgb(231, 76, 60);
        }

        public void PlaceBottomRight()
        {
            try
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 18;
                Top = wa.Bottom - ActualHeight - 18;
            }
            catch { }
        }
    }

    // 轻量设备数据（供悬浮窗使用）
    public class DeviceRowLite
    {
        public string Name;
        public int Level;
    }
}
