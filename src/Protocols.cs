using System;
using System.Collections.Generic;
using System.Text;

namespace BatteryViewer
{
    public class BatteryResult
    {
        public bool Ok;
        public int? Level;        // 0-100
        public int? RawLevel;
        public string Status = "unknown";
        public int? VoltageMv;
        public string Source = "";
        public string Detail = "";
        public Dictionary<string, string> Extra = new Dictionary<string, string>();
    }

    public struct ExpectByte
    {
        public int Offset;
        public byte Value;
        public ExpectByte(int offset, byte value) { Offset = offset; Value = value; }
    }

    public class ProtocolSpec
    {
        public string Name;
        public ushort Vid;
        public ushort UsagePage;
        public byte ReportId;
        public byte[] Payload;
        public int Length;
        public int LevelOffset;
        public int WaitMs = 120;
        public int Scale = 1;
        public int? ChargingOffset;
        public bool ChargingNonzero = true;
        public int? VoltageOffset;
        public int? LevelMax;
        public string Mode = "";   // "feature" / "output"
        public ExpectByte[] Expect = new ExpectByte[0];
        public int Retries = 2;
        public string Notes = "";
        public string Kind { get { return string.IsNullOrEmpty(Mode) ? "feature" : Mode; } }
    }

    public static class Protocols
    {
        public static readonly List<ProtocolSpec> Builtin = new List<ProtocolSpec>
        {
            // ATK / VXE 无线鼠标（Nordic 54L15）协议 2
            new ProtocolSpec {
                Name = "ATK / VXE 无线鼠标（协议 2）",
                Vid = 0x373B, UsagePage = 0xFF05, ReportId = 0x08,
                Payload = new byte[] { 0x7D, 0x72, 0x02, 0x00, 0x01, 0x07, 0x01 },
                Length = 64, LevelOffset = 7, WaitMs = 100, Scale = 1, LevelMax = 100,
                Mode = "output", Expect = new ExpectByte[] { new ExpectByte(1, 0x72), new ExpectByte(5, 0x07) }, Retries = 3,
                Notes = "电量在回复第 7 字节；第 1 字节固定 0x72 为有效回复标志"
            },
            // ATK / VXE 协议 1（Nordic 52840）
            new ProtocolSpec {
                Name = "ATK / VXE 无线鼠标（协议 1）",
                Vid = 0x373B, UsagePage = 0xFF02, ReportId = 0x08,
                Payload = Build(new byte[] { 0x04 }, 15, 0x49),
                Length = 17, LevelOffset = 6, WaitMs = 100, Scale = 1, LevelMax = 100,
                Mode = "output", Retries = 3,
                Notes = "电量在回复第 6 字节，第 7 字节非 0 表示有线连接"
            },
            new ProtocolSpec {
                Name = "VXE / VGN 无线鼠标（协议 1）",
                Vid = 0x3554, UsagePage = 0xFF02, ReportId = 0x08,
                Payload = Build(new byte[] { 0x04 }, 15, 0x49),
                Length = 17, LevelOffset = 6, WaitMs = 100, Scale = 1, LevelMax = 100,
                Mode = "output", Retries = 3,
                Notes = "VXE R1 Pro Max / R1 SE+ / VGN F1 Pro"
            },
            // Endgame Gear
            new ProtocolSpec {
                Name = "Endgame Gear（XM2w / OP1W / 无线接收器）",
                Vid = 0x3367, UsagePage = 0xFF01, ReportId = 0xA1,
                Payload = new byte[] { 0xB4 }, Length = 64,
                LevelOffset = 16, WaitMs = 350, Scale = 1, VoltageOffset = 17,
                Mode = "feature", Expect = new ExpectByte[] { new ExpectByte(1, 0x01) }, Retries = 3,
                Notes = "byte1 状态：0x01 有效 / 0x03 忙 / 0x07 不支持 / 0x08 休眠；需连读两次"
            },
            // VAXEE / ZYGEN
            new ProtocolSpec {
                Name = "VAXEE / ZYGEN（含 4K 接收器）",
                Vid = 0x3057, UsagePage = 0xFF05, ReportId = 0x0E,
                Payload = new byte[] { 0xA5, 0x0B, 0x01, 0x01 }, Length = 64,
                LevelOffset = 5, WaitMs = 120, Scale = 5, LevelMax = 20, Mode = "feature",
                Notes = "以 5% 为步长上报；充电状态用命令 0x10"
            },
            // Nordic nRF
            new ProtocolSpec {
                Name = "Nordic nRF 方案 2.4G 接收器",
                Vid = 0x1915, UsagePage = 0xFFA0, ReportId = 0x05,
                Payload = new byte[] { 0x15, 0x00, 0x00, 0x01 }, Length = 32,
                LevelOffset = 9, WaitMs = 90, Scale = 1, Mode = "feature",
                Notes = "请求 0x15 读取电量"
            },
        };

        static byte[] Build(byte[] head, int zeros, byte tail)
        {
            var b = new byte[head.Length + zeros + 1];
            Array.Copy(head, b, head.Length);
            b[b.Length - 1] = tail;
            return b;
        }
    }

    // DARKRIM（杰理 Jieli 方案）2.4G 耳机 dongle —— TLV 私有协议
    public static class Darkrim
    {
        public const ushort VID = 0x3654;
        public const ushort USAGE_PAGE = 0xFF02;
        public const ushort USAGE = 0x0002;
        public const int REPORT_LEN = 65;
        const byte CH_LOCAL = 0x10;
        const byte CH_HEADSET = 0x11;
        const byte TLV_BATTERY = 0x01;
        const byte TLV_NAME = 0x02;
        const byte TLV_FW_DONGLE = 0x04;
        const byte TLV_FW_HEADSET = 0x05;

        public static ushort Crc16Ccitt(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in data)
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc = (ushort)(crc << 1);
                }
            }
            return crc;
        }

        public static byte[] BuildFrame(byte seq, byte[] payload, byte ch = CH_LOCAL, byte sub = 0x02, byte frag = 0, byte more = 0)
        {
            var f = new byte[REPORT_LEN];
            f[0] = 0x00;
            f[1] = 0xFE;
            f[2] = seq;
            f[3] = ch;
            f[4] = 0x00;
            f[5] = sub;
            f[6] = frag;
            f[7] = more;
            f[8] = (byte)(payload.Length & 0xFF);
            f[9] = (byte)((payload.Length >> 8) & 0xFF);
            Array.Copy(payload, 0, f, 10, payload.Length);
            // CRC 覆盖 帧[1 : 10+len]
            int n = 10 + payload.Length;
            var crcBytes = new byte[n - 1];
            Array.Copy(f, 1, crcBytes, 0, n - 1);
            ushort crc = Crc16Ccitt(crcBytes);
            f[n] = (byte)((crc >> 8) & 0xFF);
            f[n + 1] = (byte)(crc & 0xFF);
            return f;
        }

        class Frame
        {
            public byte Seq;
            public byte[] Payload;
        }

        static Frame ParseFrame(byte[] report)
        {
            if (report.Length < 12 || report[1] != 0xFE) return null;
            int ln = report[8] | (report[9] << 8);
            if (10 + ln + 2 > report.Length) return null;
            var payload = new byte[ln];
            Array.Copy(report, 10, payload, 0, ln);
            return new Frame { Seq = report[2], Payload = payload };
        }

        class Tlv
        {
            public byte Type;
            public byte[] Value;
        }

        static List<Tlv> Tlvs(byte[] payload)
        {
            var outList = new List<Tlv>();
            int i = 0;
            while (i + 2 <= payload.Length)
            {
                byte t = payload[i], ln = payload[i + 1];
                var v = new byte[ln];
                if (i + 2 + ln <= payload.Length)
                    Array.Copy(payload, i + 2, v, 0, ln);
                outList.Add(new Tlv { Type = t, Value = v });
                i += 2 + ln;
            }
            return outList;
        }

        static string Ascii(byte[] v)
        {
            var sb = new StringBuilder();
            foreach (byte b in v) if (b >= 32 && b < 127) sb.Append((char)b);
            return sb.ToString().Trim();
        }

        static List<Frame> Query(HidDevice dev, byte seq, byte tlvType, byte ch, int timeoutMs)
        {
            dev.Drain();
            dev.WriteOutput(BuildFrame(seq, new byte[] { tlvType, 0x00 }, ch));
            var frames = new List<Frame>();
            var end = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < end)
            {
                var r = dev.ReadInput(120, REPORT_LEN);
                if (r == null) continue;
                var f = ParseFrame(r);
                if (f != null && f.Seq == seq) frames.Add(f);
            }
            return frames;
        }

        public static BatteryResult ReadInfo(HidDeviceInfo iface)
        {
            var res = new BatteryResult { Source = "DARKRIM 私有协议（TLV）" };
            try
            {
                using (var dev = new HidDevice(iface.Path, true))
                {
                    byte seq = 0;
                    int? battery = null, batteryRaw = null;
                    string name = "", fwD = "", fwH = "";

                    foreach (var f in Query(dev, seq++, TLV_BATTERY, CH_LOCAL, 350))
                        foreach (var tv in Tlvs(f.Payload))
                            if (tv.Type == TLV_BATTERY && tv.Value.Length >= 1)
                            {
                                batteryRaw = tv.Value[0];
                                battery = tv.Value[0] <= 100 ? (int?)tv.Value[0] : null;
                            }

                    foreach (var tlv in new byte[] { TLV_NAME, TLV_FW_DONGLE, TLV_FW_HEADSET })
                        foreach (var f in Query(dev, seq++, tlv, CH_LOCAL, 350))
                            foreach (var tv in Tlvs(f.Payload))
                            {
                                byte t = tv.Type;
                                byte[] v = tv.Value;
                                if (t == TLV_NAME) name = Ascii(v);
                                else if (t == TLV_FW_DONGLE) fwD = Ascii(v);
                                else if (t == TLV_FW_HEADSET) fwH = Ascii(v);
                            }

                    if (battery == null)
                    {
                        res.Ok = false;
                        res.Detail = "耳机没有上报电量（确认耳机已开机并连上 dongle）";
                        return res;
                    }
                    res.Ok = true;
                    res.Level = battery;
                    res.RawLevel = batteryRaw;
                    res.Detail = "设备=" + name + " 固件(dongle/耳机)=" + fwD + "/" + fwH;
                    if (!string.IsNullOrEmpty(name)) res.Extra["name"] = name;
                    if (!string.IsNullOrEmpty(fwD)) res.Extra["dongle_fw"] = fwD;
                    if (!string.IsNullOrEmpty(fwH)) res.Extra["headset_fw"] = fwH;
                }
            }
            catch (HidException e)
            {
                res.Ok = false;
                res.Detail = "读取失败：" + e.Message;
            }
            return res;
        }
    }
}
