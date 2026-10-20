using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BatteryViewer
{
    public class MainForm : Form
    {
        // ---- 配色 ----
        public static readonly Color BG = Color.FromArgb(24, 26, 32);
        public static readonly Color BORDER = Color.FromArgb(52, 56, 68);
        public static readonly Color TEXT = Color.FromArgb(235, 238, 245);
        public static readonly Color TEXT_DIM = Color.FromArgb(150, 156, 168);
        public static readonly Color ACCENT = Color.FromArgb(88, 140, 255);
        public static readonly Color GREEN = Color.FromArgb(46, 204, 113);
        public static readonly Color YELLOW = Color.FromArgb(241, 196, 15);
        public static readonly Color RED = Color.FromArgb(231, 76, 60);
        public static readonly Color BATTERY_TRACK = Color.FromArgb(70, 74, 84);

        // ---- 设置 ----
        string bgPath = "";
        bool blurBg = true;
        string configPath;

        Image bgImage;
        Image bgOriginal;

        Button btnRefresh;
        Label lblStatus;
        System.Windows.Forms.Timer timer;
        bool refreshing = false;

        // 卡片数据（全部画在窗体上，零子控件）
        class Card
        {
            public Rectangle Bounds;
            public BatteryRow Row;
            public bool Hover;
        }
        class BatteryRow
        {
            public HidDeviceInfo Info;
            public BatteryResult Result;
            public bool Readable;
            public string Name;
        }
        readonly List<Card> cards = new List<Card>();

        public MainForm()
        {
            Text = "2.4G 外设电量查看器";
            ClientSize = new Size(880, 640);
            MinimumSize = new Size(660, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BG;
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;

            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "batteryviewer.ini");
            LoadConfig();
            LoadBackground();

            BuildFooter();

            var btnCfg = MakeGhostButton("设置背景", 20, 64);
            btnCfg.Click += (s, e) => OpenSettings();
            Controls.Add(btnCfg);

            timer = new System.Windows.Forms.Timer { Interval = 60000 };
            timer.Tick += (s, e) => { if (!refreshing) RefreshAll(); };
            timer.Start();

            Shown += (s, e) => RefreshAll();
        }

        Button MakeGhostButton(string text, int x, int y)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(96, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(140, 44, 48, 58),
                ForeColor = TEXT,
                Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderColor = BORDER;
            return b;
        }

        // ---- 背景 ----
        void LoadBackground()
        {
            var old = bgImage;
            bgImage = null;
            if (bgOriginal != null) { bgOriginal.Dispose(); bgOriginal = null; }

            if (!string.IsNullOrEmpty(bgPath) && File.Exists(bgPath))
            {
                try
                {
                    using (var fs = new FileStream(bgPath, FileMode.Open, FileAccess.Read))
                        bgOriginal = Image.FromStream(fs);
                    bgImage = blurBg ? GaussianApprox(bgOriginal) : bgOriginal;
                }
                catch { }
            }
            if (old != null && old != bgImage) old.Dispose();
            Invalidate();
        }

        // 高斯模糊近似：两级下采样 + 最终绘制时高质量上采样
        static Image GaussianApprox(Image src)
        {
            try
            {
                int w1 = Math.Max(1, src.Width / 12), h1 = Math.Max(1, src.Height / 12);
                var small = new Bitmap(w1, h1);
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(src, new Rectangle(0, 0, w1, h1));
                }
                int w2 = Math.Max(1, src.Width / 4), h2 = Math.Max(1, src.Height / 4);
                var mid = new Bitmap(w2, h2);
                using (var g = Graphics.FromImage(mid))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(small, new Rectangle(0, 0, w2, h2));
                }
                small.Dispose();
                return mid;
            }
            catch { return null; }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // 底色
            using (var b = new SolidBrush(BG))
                g.FillRectangle(b, ClientRectangle);

            // 背景图
            if (bgImage != null)
            {
                var src = CropFillRect(new Size(bgImage.Width, bgImage.Height), ClientSize);
                var srcRect = new Rectangle((bgImage.Width - src.Width) / 2, (bgImage.Height - src.Height) / 2, src.Width, src.Height);
                g.DrawImage(bgImage, ClientRectangle, srcRect, GraphicsUnit.Pixel);
            }

            // 遮罩
            using (var b = new SolidBrush(Color.FromArgb(155, 15, 17, 21)))
                g.FillRectangle(b, ClientRectangle);

            // 标题
            using (var f = new Font("Segoe UI", 17f, FontStyle.Bold))
            using (var b = new SolidBrush(TEXT))
                g.DrawString("2.4G 外设电量查看器", f, b, 22, 14);
            using (var f = new Font("Segoe UI", 9f))
            using (var b = new SolidBrush(TEXT_DIM))
                g.DrawString("枚举本机 HID 设备，自动识别协议读取电量", f, b, 24, 46);

            // 卡片
            int pad = 24;
            int cardW = ClientSize.Width - pad * 2;
            int y = 106;
            foreach (var card in cards)
            {
                card.Bounds = new Rectangle(pad, y, cardW, 132);
                DrawCard(g, card);
                y += 132 + 14;
            }

            if (cards.Count == 0 && refreshing == false)
            {
                using (var f = new Font("Segoe UI", 10.5f))
                using (var b = new SolidBrush(TEXT_DIM))
                    g.DrawString("没有读到任何设备的电量\n\n可能原因：\n· 2.4G 接收器上没有配对/在线的设备（动一下鼠标）\n· 设备协议暂未支持",
                        f, b, new RectangleF(pad, y, cardW, 140), new StringFormat { Alignment = StringAlignment.Center });
            }

            base.OnPaint(e);
        }

        void DrawCard(Graphics g, Card card)
        {
            var row = card.Row;
            var rect = card.Bounds;

            // 毛玻璃卡片底
            var r = new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            using (var path = RoundPath(r, 14))
            {
                using (var b = new SolidBrush(card.Hover ? Color.FromArgb(180, 42, 46, 58) : Color.FromArgb(160, 28, 31, 40)))
                    g.FillPath(b, path);
                using (var p = new Pen(card.Hover ? Color.FromArgb(140, 120, 150, 255) : Color.FromArgb(90, 70, 76, 92)))
                    g.DrawPath(p, path);
            }

            string name = DeviceLabel(row.Info);
            if (!string.IsNullOrEmpty(row.Name)) name = row.Name;
            string meta = row.Info.VidPid + "  ·  " + row.Info.UsagePage.ToString("X4") + ":" + row.Info.Usage.ToString("X4");
            string sub = row.Result != null ? row.Result.Source : "";
            if (row.Result != null && row.Result.VoltageMv != null)
                sub += "  ·  " + (row.Result.VoltageMv.Value / 1000.0).ToString("0.00") + " V";

            // 设备名
            using (var f = new Font("Segoe UI", 15.5f, FontStyle.Bold))
            using (var b = new SolidBrush(TEXT))
                g.DrawString(name, f, b, rect.X + 20, rect.Y + 14);

            // VID:PID + Usage
            using (var f = new Font("Consolas", 9.5f))
            using (var b = new SolidBrush(TEXT_DIM))
                g.DrawString(meta, f, b, rect.X + 21, rect.Y + 48);

            // 右侧大百分比
            var rightRect = new RectangleF(rect.Right - 270, rect.Y + 4, 246, rect.Height - 8);
            if (row.Readable && row.Result.Level != null)
            {
                int level = row.Result.Level.Value;
                Color c = LevelColor(level);
                using (var f = new Font("Segoe UI", 42f, FontStyle.Bold))
                using (var b = new SolidBrush(c))
                using (var sfR = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                    g.DrawString(level + "%", f, b, rightRect, sfR);

                // 来源/电压
                using (var f = new Font("Segoe UI", 8.5f))
                using (var b = new SolidBrush(TEXT_DIM))
                using (var sfR = new StringFormat { Alignment = StringAlignment.Far })
                    g.DrawString(sub, f, b, new RectangleF(rect.Right - 360, rect.Bottom - 32, 336, 20), sfR);

                // 进度条
                var track = new Rectangle(rect.X + 20, rect.Bottom - 26, rect.Width - 400, 6);
                using (var b = new SolidBrush(BATTERY_TRACK)) g.FillRectangle(b, track);
                int w = (int)(track.Width * level / 100.0);
                if (w > 2)
                    using (var path = RoundPath(new Rectangle(track.X, track.Y, w, track.Height), 3))
                    using (var b = new SolidBrush(c))
                        g.FillPath(b, path);
            }
            else if (row.Readable && row.Result.VoltageMv != null)
            {
                using (var f = new Font("Segoe UI", 30f, FontStyle.Bold))
                using (var b = new SolidBrush(ACCENT))
                using (var sfR = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                    g.DrawString((row.Result.VoltageMv.Value / 1000.0).ToString("0.00") + " V", f, b, rightRect, sfR);
            }
            else
            {
                using (var f = new Font("Segoe UI", 26f))
                using (var b = new SolidBrush(TEXT_DIM))
                using (var sfR = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                    g.DrawString("—", f, b, rightRect, sfR);
            }
        }

        static Size CropFillRect(Size src, Size dst)
        {
            double scale = Math.Max((double)dst.Width / src.Width, (double)dst.Height / src.Height);
            return new Size((int)(dst.Width / scale), (int)(dst.Height / scale));
        }

        // 点击命中测试 + 悬停
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool changed = false;
            foreach (var c in cards)
            {
                bool h = c.Bounds.Contains(e.Location);
                if (h != c.Hover) { c.Hover = h; changed = true; }
            }
            if (changed) Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                foreach (var c in cards)
                    if (c.Bounds.Contains(e.Location)) { ShowDetail(c.Row); break; }
            base.OnMouseClick(e);
        }

        // ---- 设置 ----
        void OpenSettings()
        {
            var form = new Form
            {
                Text = "背景设置",
                Size = new Size(380, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(30, 32, 40),
            };
            var lbl = new Label { Text = "背景图片：", Location = new Point(20, 22), AutoSize = true, ForeColor = TEXT };
            var txt = new TextBox { Text = bgPath, Location = new Point(20, 46), Width = 240 };
            var btnBrowse = new Button { Text = "浏览…", Location = new Point(268, 44), Size = new Size(70, 24) };
            var chkBlur = new CheckBox { Text = "高斯模糊（毛玻璃效果）", Location = new Point(20, 80), AutoSize = true, ForeColor = TEXT, Checked = blurBg };
            var btnClear = new Button { Text = "清除背景图", Location = new Point(20, 112), Size = new Size(100, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(44, 48, 58), ForeColor = TEXT };
            btnClear.FlatAppearance.BorderColor = BORDER;
            var btnOk = new Button { Text = "应用", Location = new Point(258, 112), Size = new Size(80, 28), FlatStyle = FlatStyle.Flat, BackColor = ACCENT, ForeColor = Color.White };
            btnOk.FlatAppearance.BorderSize = 0;

            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "选择背景图片";
                    dlg.Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*";
                    if (dlg.ShowDialog(form) == DialogResult.OK) txt.Text = dlg.FileName;
                }
            };
            btnOk.Click += (s, e) =>
            {
                bgPath = txt.Text.Trim();
                blurBg = chkBlur.Checked;
                SaveConfig();
                LoadBackground();
                form.Close();
            };
            btnClear.Click += (s, e) => { txt.Text = ""; };

            form.Controls.AddRange(new Control[] { lbl, txt, btnBrowse, chkBlur, btnClear, btnOk });
            form.ShowDialog(this);
        }

        void SaveConfig()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("bgpath=" + bgPath);
                sb.AppendLine("blur=" + (blurBg ? "1" : "0"));
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
                    else if (k == "blur") blurBg = v == "1";
                }
            }
            catch { }
        }

        // ---- 底栏 ----
        void BuildFooter()
        {
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(120, 18, 20, 25) };
            btnRefresh = new Button
            {
                Text = "刷新电量",
                Location = new Point(20, 12),
                Size = new Size(120, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = ACCENT,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f),
                Cursor = Cursors.Hand,
            };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.Click += (s, e) => { if (!refreshing) RefreshAll(); };

            lblStatus = new Label
            {
                Location = new Point(156, 19),
                AutoSize = true,
                ForeColor = TEXT_DIM,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f),
                Text = "就绪",
            };

            var hint = new Label
            {
                Text = "点击卡片查看详情 · 每 60 秒自动刷新",
                AutoSize = true,
                ForeColor = TEXT_DIM,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5f),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            };
            footer.Resize += (s, e) => hint.Location = new Point(footer.Width - hint.PreferredWidth - 20, 21);

            footer.Controls.Add(btnRefresh);
            footer.Controls.Add(lblStatus);
            footer.Controls.Add(hint);
            Controls.Add(footer);
            footer.BringToFront();
        }

        // ---- 数据 ----
        void RefreshAll()
        {
            refreshing = true;
            btnRefresh.Enabled = false;
            btnRefresh.Text = "读取中…";
            lblStatus.Text = "正在枚举设备并读取电量…";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    List<HidDeviceInfo> list;
                    try { list = HidApi.Enumerate(); }
                    catch (Exception) { list = new List<HidDeviceInfo>(); }

                    var rows = new List<BatteryRow>();
                    foreach (var d in list)
                    {
                        BatteryResult r = null;
                        try { r = BatteryReader.ReadOne(d); }
                        catch (Exception) { r = null; }
                        var br = new BatteryRow { Info = d, Result = r, Readable = r != null && r.Ok };
                        if (r != null && r.Extra != null && r.Extra.ContainsKey("name")) br.Name = r.Extra["name"];
                        rows.Add(br);
                    }

                    BeginInvoke((MethodInvoker)(() =>
                    {
                        cards.Clear();
                        foreach (var row in rows)
                            if (row.Readable) cards.Add(new Card { Row = row });

                        int readable = rows.Count;
                        int ok = cards.Count;
                        lblStatus.Text = ok > 0
                            ? "读到 " + ok + " 台设备的电量"
                            : "没有读到电量（设备可能休眠，动一下鼠标再刷新）";
                        btnRefresh.Enabled = true;
                        btnRefresh.Text = "刷新电量";
                        Invalidate();
                    }));
                }
                catch (Exception)
                {
                    BeginInvoke((MethodInvoker)(() =>
                    {
                        lblStatus.Text = "读取出错";
                        btnRefresh.Enabled = true;
                        btnRefresh.Text = "刷新电量";
                        refreshing = false;
                        Invalidate();
                    }));
                }
                refreshing = false;
            });
        }

        void ShowDetail(BatteryRow row)
        {
            var sb = new StringBuilder();
            string name = DeviceLabel(row.Info);
            if (!string.IsNullOrEmpty(row.Name)) name = row.Name;
            sb.AppendLine("设备: " + name);
            sb.AppendLine("VID:PID: " + row.Info.VidPid);
            sb.AppendLine("Usage: " + row.Info.UsagePage.ToString("X4") + ":" + row.Info.Usage.ToString("X4"));
            sb.AppendLine("路径: " + row.Info.Path);
            sb.AppendLine();
            if (row.Result != null)
            {
                sb.AppendLine("读取来源: " + row.Result.Source);
                sb.AppendLine("说明: " + row.Result.Detail);
            }
            else sb.AppendLine("没有可用的电量读取方式");
            MessageBox.Show(sb.ToString(), "设备详情", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        public static Color LevelColor(int level)
        {
            if (level >= 60) return GREEN;
            if (level >= 30) return YELLOW;
            return RED;
        }

        public static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) return path;
            radius = Math.Max(1, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowCrash(e.ExceptionObject as Exception);
            Application.Run(new MainForm());
        }

        static void ShowCrash(Exception ex)
        {
            string msg = ex == null ? "未知错误" : (ex.Message + "\n\n" + ex.StackTrace);
            try { MessageBox.Show(msg, "程序出错", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            catch { }
        }
    }
}
