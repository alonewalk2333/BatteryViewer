using System;

namespace BatteryViewer
{
    // 华硕 ROG 鼠标电量：OMNI 接收器（0x1ACE）私有协议
    // 协议逆向参考 G-Helper (seerge/g-helper) 与 peripheral-battery-tray (SteveWang92)
    // 并在本机 ROG 战刃3 无线版 + OMNI 接收器上实测通过。
    //
    // 通道：USB 接口 mi_02 的 col03 collection（Usage Page 0xFF01，64 字节输出报告）
    // 命令：{0x03, 0x12, 0x07, 0x00...}（report id 0x03 + GET 0x12 + 子命令 0x07）
    // 回复：[1]==0x12 && [2]==0x07 → [5]=电量%，[10]!=0 充电中
    //       [1]==0xFF && [2]==0xAA → 接收器在线但鼠标无响应（休眠/关机）
    public static class AsusReader
    {
        const ushort ASUS_VID = 0x0B05;
        const ushort OMNI_PID = 0x1ACE;

        public static bool Matches(HidDeviceInfo iface)
        {
            return iface.Vid == ASUS_VID && iface.Pid == OMNI_PID
                && iface.UsagePage == 0xFF01
                && iface.OutputLen >= 64
                && iface.Path.ToLower().Contains("col03");
        }

        public static BatteryResult ReadBattery(HidDeviceInfo iface)
        {
            var res = new BatteryResult { Source = "华硕 ROG 专有协议（OMNI 接收器）" };
            try
            {
                using (var dev = new HidDevice(iface.Path, true))
                {
                    var body = new byte[64];
                    body[0] = 0x03;
                    body[1] = 0x12;
                    body[2] = 0x07;
                    dev.WriteOutput(body);

                    var deadline = DateTime.UtcNow.AddMilliseconds(2500);
                    while (DateTime.UtcNow < deadline)
                    {
                        var r = dev.ReadInput(300, 64);
                        if (r == null || r.Length < 11) continue;

                        if (r[1] == 0xFF && r[2] == 0xAA)
                        {
                            res.Ok = false;
                            res.Detail = "接收器在线但鼠标没响应（睡着或关机了，动一下鼠标再刷新）";
                            return res;
                        }
                        if (r[1] != 0x12 || r[2] != 0x07) continue;  // 跳过其它 input 流

                        int level = Math.Max(0, Math.Min(100, (int)r[5]));
                        res.Ok = true;
                        res.Level = level;
                        res.RawLevel = r[5];
                        res.Status = r[10] != 0 ? "charging" : "discharging";
                        res.Extra["name"] = "ROG 鼠标";
                        res.Detail = "OMNI 接收器：电量=" + level + "% 充电=" + (r[10] != 0 ? "是" : "否");
                        return res;
                    }
                    res.Ok = false;
                    res.Detail = "接口无回复（鼠标可能休眠，动一下再刷新）";
                }
            }
            catch (HidException e)
            {
                res.Ok = false;
                res.Detail = "读取失败：" + e.Message + "（若开着 Armoury Crate，可尝试关掉其后台服务）";
            }
            return res;
        }
    }
}
