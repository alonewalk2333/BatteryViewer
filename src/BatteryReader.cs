using System;
using System.Collections.Generic;
using System.Threading;

namespace BatteryViewer
{
    // 对每个 HID 接口依次尝试多条策略，第一条成功的即可用。
    public static class BatteryReader
    {
        // 罗技 HID++ 2.0
        const ushort LOGITECH_VID = 0x046D;
        const int FEATURE_BATTERY_STATUS = 0x1000;
        const int FEATURE_BATTERY_VOLTAGE = 0x1001;
        const int FEATURE_UNIFIED_BATTERY = 0x1004;
        static readonly int[] DEVICE_INDICES = { 0xFF, 1, 2, 3, 4, 5, 6 };

        static readonly string[] HIDPP_STATUS_KEY = { "discharging", "charging", "charging", "full", "charging", "unknown", "unknown", "unknown" };
        static readonly string[] HIDPP_STATUS_TEXT = { "放电中", "充电中", "充电中（末段）", "已充满", "充电中（慢速）", "电池类型无效", "温度异常", "充电异常" };

        static string HidppStatus(int st, out string key)
        {
            if (st >= 0 && st < HIDPP_STATUS_KEY.Length)
            {
                key = HIDPP_STATUS_KEY[st];
                return HIDPP_STATUS_TEXT[st];
            }
            key = "unknown";
            return "状态 " + st;
        }

        public static BatteryResult ReadOne(HidDeviceInfo iface)
        {
            // 0) 标准 HID 电量字段（任意设备通用兜底）
            var std = TryStandardBattery(iface);
            if (std != null && std.Ok) return std;

            // 1) 厂商协议（ATK/VXE/Endgame/VAXEE/Nordic）
            foreach (var spec in Protocols.Builtin)
            {
                if (spec.Vid != iface.Vid || spec.UsagePage != iface.UsagePage) continue;
                var r = TryVendor(iface, spec);
                if (r != null) return r;
            }

            // 1.5) 华硕 ROG 鼠标（OMNI 接收器）
            if (AsusReader.Matches(iface))
                return AsusReader.ReadBattery(iface);

            // 2) DARKRIM 杰理
            if (iface.Vid == Darkrim.VID && iface.UsagePage == Darkrim.USAGE_PAGE && iface.OutputLen == Darkrim.REPORT_LEN)
                return Darkrim.ReadInfo(iface);

            // 3) 罗技 HID++
            if (iface.Vid == LOGITECH_VID && iface.OutputLen > 0)
            {
                var r = TryHidpp(iface);
                if (r != null) return r;
            }

            return null;
        }

        // 标准 HID 电量字段：Usage Page 0x0006/0x000D/0x0085 里的 Battery Strength 等
        struct BatteryUsage
        {
            public ushort Page;
            public ushort Usage;
            public string Name;
            public BatteryUsage(ushort page, ushort usage, string name) { Page = page; Usage = usage; Name = name; }
        }

        static readonly BatteryUsage[] BATTERY_USAGES = new BatteryUsage[]
        {
            new BatteryUsage(0x0006, 0x0020, "Battery Strength（通用设备控制页）"),
            new BatteryUsage(0x000D, 0x003B, "Battery Strength（数字化仪页）"),
            new BatteryUsage(0x0085, 0x0001, "Absolute State of Charge（电池系统页）"),
            new BatteryUsage(0x0085, 0x0066, "Relative State of Charge（电池系统页）"),
            new BatteryUsage(0x0085, 0x0065, "Battery Level（电池系统页）"),
            new BatteryUsage(0x0006, 0x0029, "RF Signal Strength（无线信号强度）"),
        };

        static BatteryResult TryStandardBattery(HidDeviceInfo iface)
        {
            if (iface.FeatureLen == 0) return null;
            try
            {
                using (var dev = new HidDevice(iface.Path, false))
                {
                    foreach (var cap in dev.ValueCaps(HidPReportType.Feature))
                    {
                        string name = null;
                        foreach (var b in BATTERY_USAGES)
                            if (cap.UsagePage == b.Page && cap.Usage == b.Usage) { name = b.Name; break; }
                        if (name == null) continue;

                        byte[] report;
                        try { report = dev.GetFeatureReport(cap.ReportID, iface.FeatureLen); }
                        catch (HidException e)
                        {
                            return new BatteryResult { Ok = false, Source = "标准 HID 电量字段", Detail = "读取 Feature Report 失败：" + e.Message };
                        }
                        int? value = dev.UsageValue(HidPReportType.Feature, report, cap);
                        if (value == null) continue;
                        int level = Scale(value.Value, cap.LogicalMax);
                        return new BatteryResult
                        {
                            Ok = true, Level = level, RawLevel = value.Value,
                            Status = "unknown", Source = "标准 HID 电量字段（" + name + "）",
                            Detail = "UsagePage=0x" + cap.UsagePage.ToString("X4") + " Usage=0x" + cap.Usage.ToString("X4")
                                + " ReportID=0x" + cap.ReportID.ToString("X2") + " 原始值=" + value.Value + " 上限=" + cap.LogicalMax,
                        };
                    }
                }
            }
            catch (HidException) { return null; }
            return null;
        }

        static int Scale(int value, int logicalMax)
        {
            if (logicalMax > 0 && logicalMax != 100)
            {
                if (logicalMax == 0xFF && value > 100) return Math.Max(0, Math.Min(100, (int)Math.Round(value * 100.0 / 255)));
                return Math.Max(0, Math.Min(100, (int)Math.Round(value * 100.0 / logicalMax)));
            }
            if (value > 100) return Math.Max(0, Math.Min(100, (int)Math.Round(value * 100.0 / 255)));
            return Math.Max(0, value);
        }

        static BatteryResult TryVendor(HidDeviceInfo iface, ProtocolSpec spec)
        {
            string kind = spec.Kind == "feature" ? "feature" : (iface.FeatureLen > 0 && spec.Kind == "feature" ? "feature" : "output");
            if (spec.Kind == "feature" && iface.FeatureLen == 0) kind = "output";

            try
            {
                using (var dev = new HidDevice(iface.Path, true))
                {
                    byte[] body = BuildBody(spec);
                    byte[] last = null;
                    for (int attempt = 0; attempt < Math.Max(1, spec.Retries); attempt++)
                    {
                        dev.Drain();
                        if (kind == "feature")
                        {
                            dev.SetFeatureReport(body);
                            Thread.Sleep(spec.WaitMs);
                            last = dev.GetFeatureReport(spec.ReportId, spec.Length);
                            if (spec.Expect.Length == 0 || Matches(last, spec.Expect)) return Parse(spec, last, iface, kind);
                            continue;
                        }
                        dev.WriteOutput(body);
                        Thread.Sleep(spec.WaitMs);
                        int end = Environment.TickCount + 600;
                        while (Environment.TickCount < end)
                        {
                            var r = dev.ReadInput(150, iface.InputLen);
                            if (r == null) continue;
                            last = r;
                            if (spec.Expect.Length == 0 || Matches(r, spec.Expect)) return Parse(spec, r, iface, kind);
                        }
                    }
                    if (last == null)
                        return new BatteryResult { Ok = false, Source = spec.Name, Detail = "设备没有回复（多半是外设正在休眠，动一下鼠标/开机后重试）" };
                    return Parse(spec, last, iface, kind);
                }
            }
            catch (HidException e)
            {
                return new BatteryResult { Ok = false, Source = spec.Name, Detail = "收发失败：" + e.Message + "（可能是权限被占用，请关闭厂商驱动软件后重试）" };
            }
        }

        static byte[] BuildBody(ProtocolSpec spec)
        {
            var body = new byte[spec.Length];
            body[0] = spec.ReportId;
            for (int i = 0; i < spec.Payload.Length && i + 1 < spec.Length; i++)
                body[i + 1] = spec.Payload[i];
            return body;
        }

        static bool Matches(byte[] report, ExpectByte[] expect)
        {
            if (report == null) return false;
            foreach (var e in expect)
                if (report.Length <= e.Offset || report[e.Offset] != e.Value) return false;
            return true;
        }

        static BatteryResult Parse(ProtocolSpec spec, byte[] report, HidDeviceInfo iface, string kind)
        {
            if (report.Length <= spec.LevelOffset)
                return new BatteryResult { Ok = false, Source = spec.Name, Detail = "回复长度不足（" + report.Length + " 字节）" };
            int rawLevel = report[spec.LevelOffset];
            if (spec.LevelMax != null && rawLevel > spec.LevelMax)
                return new BatteryResult { Ok = false, Source = spec.Name, Detail = "电量字节 0x" + rawLevel.ToString("X2") + " 超出预期范围" };
            int level = Math.Max(0, Math.Min(100, rawLevel * spec.Scale));

            string status = "unknown";
            if (spec.ChargingOffset != null && report.Length > spec.ChargingOffset)
            {
                byte v = report[spec.ChargingOffset.Value];
                status = spec.ChargingNonzero ? (v != 0 ? "charging" : "discharging") : (v == 0 ? "charging" : "discharging");
            }

            int? voltage = null;
            if (spec.VoltageOffset != null && report.Length > spec.VoltageOffset.Value + 1)
            {
                int mv = report[spec.VoltageOffset.Value] | (report[spec.VoltageOffset.Value + 1] << 8);
                if (mv >= 2500 && mv <= 5000) voltage = mv;
            }

            return new BatteryResult
            {
                Ok = true,
                Level = level,
                RawLevel = rawLevel,
                Status = status,
                VoltageMv = voltage,
                Source = spec.Name,
                Detail = "ReportID=0x" + spec.ReportId.ToString("X2") + " 电量字节@" + spec.LevelOffset + "=0x" + rawLevel.ToString("X2") + " ×" + spec.Scale,
            };
        }

        // ---- 罗技 HID++ 2.0 ----
        static BatteryResult TryHidpp(HidDeviceInfo iface)
        {
            try
            {
                using (var dev = new HidDevice(iface.Path, true))
                {
                    if (dev.OutputReportByteLength >= 20)
                    {
                        // ping 唤醒（无线鼠标/键盘会休眠，不 ping 直接查全是超时）
                        // ping = root(0x00) 的 function 1，参数尾部 swid 原样返回
                        var pingBody = new byte[20];
                        pingBody[0] = 0x11; pingBody[1] = 0x01; pingBody[2] = 0x00;
                        pingBody[3] = 0x11; pingBody[6] = 0x42;
                        try { dev.WriteOutput(pingBody); } catch { }
                        Thread.Sleep(300);
                        dev.Drain();

                        // 快速路径：先试最常用的 index 1（Unifying/Lightspeed 接收器上的第一台设备）
                        var fast = ProbeIndex(dev, iface, 0x11, 1);
                        if (fast != null && fast.Ok)
                        {
                            fast.Extra["name"] = ReadHidppDeviceName(dev, 1);
                            return fast;
                        }

                        // 通用路径：两轮全索引探测
                        for (int round = 0; round < 2; round++)
                        {
                            foreach (int index in DEVICE_INDICES)
                            {
                                var r = ProbeIndex(dev, iface, 0x11, index);
                                if (r != null && r.Ok)
                                {
                                    r.Extra["name"] = ReadHidppDeviceName(dev, index);
                                    return r;
                                }
                            }
                        }
                    }
                }
            }
            catch (HidException e)
            {
                return new BatteryResult { Ok = false, Source = "罗技 HID++", Detail = "无法打开厂商接口：" + e.Message };
            }
            return null;
        }

        // 读 HID++ 设备名（feature 0x0005：GetCount + GetDeviceName 分块）
        static string ReadHidppDeviceName(HidDevice dev, int index)
        {
            try
            {
                var fs = HidppCall(dev, 0x11, index, 0x00, 0x00, new byte[] { 0x00, 0x05 }, 400);
                if (fs == null || HidppError(fs) != null || fs.Length <= 4 || fs[4] == 0) return "";
                int fidx = fs[4];
                var cnt = HidppCall(dev, 0x11, index, fidx, 0x00, new byte[0], 400);
                if (cnt == null || HidppError(cnt) != null || cnt.Length <= 4) return "";
                int len = cnt[4];
                if (len <= 0 || len > 64) return "";
                var sb = new System.Text.StringBuilder();
                for (int off = 0; off < len; off += 16)
                {
                    var chunk = HidppCall(dev, 0x11, index, fidx, 0x01, new byte[] { (byte)off }, 400);
                    if (chunk == null || HidppError(chunk) != null) break;
                    for (int i = 4; i < chunk.Length && sb.Length < len; i++)
                        if (chunk[i] >= 32 && chunk[i] < 127) sb.Append((char)chunk[i]);
                }
                return sb.ToString().Trim();
            }
            catch { return ""; }
        }

        static int? HidppError(byte[] report)
        {
            if (report.Length < 4) return null;
            if ((report[3] & 0x80) != 0) return report.Length > 4 ? report[4] : 0xFF;
            return null;
        }

        static byte[] HidppCall(HidDevice dev, byte rid, int index, int featureIndex, int function, byte[] prms, int timeoutMs)
        {
            var body = new List<byte> { rid, (byte)index, (byte)featureIndex, (byte)(((function & 0x0F) << 4) | 0x01) };
            body.AddRange(prms);
            int length = rid == 0x11 ? 20 : 7;
            while (body.Count < length) body.Add(0);
            try { dev.WriteOutput(body.ToArray()); }
            catch (HidException) { return null; }

            int deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                int remain = deadline - Environment.TickCount;
                var r = dev.ReadInput(Math.Min(150, Math.Max(1, remain)), rid == 0x11 ? 20 : 7);
                if (r == null || r.Length < 4) continue;
                if (r[0] != rid || r[1] != index || r[2] != featureIndex) continue;
                if (((r[3] >> 4) & 0x0F) != function) continue;
                return r;
            }
            return null;
        }

        static int? HidppFeatureIndex(HidDevice dev, byte rid, int index, int featureId)
        {
            var reply = HidppCall(dev, rid, index, 0x00, 0x00, new byte[] { (byte)(featureId >> 8), (byte)(featureId & 0xFF) }, 500);
            if (reply == null || HidppError(reply) != null) return null;
            // Root.GetFeature 回复：byte4 = feature index（0 = 不支持），byte5 = 类型，byte6 = 版本
            if (reply.Length > 4 && reply[4] != 0) return reply[4];
            return null;
        }

        static BatteryResult ProbeIndex(HidDevice dev, HidDeviceInfo iface, byte rid, int index)
        {
            var fidx = HidppFeatureIndex(dev, rid, index, FEATURE_BATTERY_STATUS);
            if (fidx != null)
            {
                var reply = HidppCall(dev, rid, index, fidx.Value, 0x00, new byte[0], 260);
                if (reply != null && HidppError(reply) == null && reply.Length >= 7)
                {
                    int rawLevel = reply[4], st = reply[6];
                    string status, text;
                    text = HidppStatus(st, out status);
                    int level = rawLevel;
                    if (level == 0 && status != "discharging")
                        level = status == "full" ? 100 : (status == "charging" ? 80 : 0);
                    return new BatteryResult
                    {
                        Ok = true, Level = Math.Max(0, Math.Min(100, level)), RawLevel = rawLevel,
                        Status = status, Source = "罗技 HID++ 2.0（0x1000 电池状态）",
                        Detail = "设备索引 " + index + "：电量=" + rawLevel + "% 状态=" + text
                    };
                }
            }

            fidx = HidppFeatureIndex(dev, rid, index, FEATURE_UNIFIED_BATTERY);
            if (fidx != null)
            {
                var reply = HidppCall(dev, rid, index, fidx.Value, 0x00, new byte[0], 260);
                if (reply != null && HidppError(reply) == null && reply.Length >= 6)
                {
                    int rawLevel = reply[4], st = reply[5];
                    string status, text;
                    text = HidppStatus(st, out status);
                    return new BatteryResult
                    {
                        Ok = true, Level = Math.Max(0, Math.Min(100, rawLevel)), RawLevel = rawLevel,
                        Status = status, Source = "罗技 HID++ 2.0（0x1004 统一电池）",
                        Detail = "设备索引 " + index + "：电量=" + rawLevel + "% 状态=" + text
                    };
                }
            }

            fidx = HidppFeatureIndex(dev, rid, index, FEATURE_BATTERY_VOLTAGE);
            if (fidx != null)
            {
                var reply = HidppCall(dev, rid, index, fidx.Value, 0x00, new byte[0], 400);
                if (reply != null && HidppError(reply) == null && reply.Length >= 6)
                {
                    // 0x1001 电压为大端 16 位
                    int mv = (reply[4] << 8) | reply[5];
                    if (mv >= 2000 && mv <= 5000)
                    {
                        // 锂电池线性估算：3000mV=0%，4200mV=100%
                        int level = (mv - 3000) * 100 / (4200 - 3000);
                        if (level > 100) level = 100;
                        if (level < 0) level = 0;
                        return new BatteryResult
                        {
                            Ok = true, Level = level, VoltageMv = mv, Status = "unknown",
                            Source = "罗技 HID++ 2.0（0x1001 电池电压）",
                            Detail = "设备索引 " + index + "：电压 " + mv + " mV（按锂电池 3.0-4.2V 线性折算百分比）"
                        };
                    }
                }
            }
            return null;
        }
    }
}
