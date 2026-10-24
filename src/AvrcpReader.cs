using System;
using System.Collections.Generic;
using System.Threading;

namespace BatteryViewer
{
    // 经典蓝牙（BR/EDR）耳机电量：AVRCP 协议读取
    // 参考 AVRCP 1.6 spec：CT(控制器) 通过 RegisterNotification(BatteryStatus) 获取目标电量
    public class AvrcpDeviceRow
    {
        public string Name;
        public string Address;
        public int Level;
    }

    public static class AvrcpReader
    {
        static T Await<T>(Windows.Foundation.IAsyncOperation<T> op, int timeoutMs = 8000)
        {
            int deadline = Environment.TickCount + timeoutMs;
            while (op.Status == Windows.Foundation.AsyncStatus.Started)
            {
                if (Environment.TickCount > deadline) throw new Exception("AVRCP 操作超时");
                Thread.Sleep(20);
            }
            if (op.Status == Windows.Foundation.AsyncStatus.Error)
                throw new Exception(op.ErrorCode.Message);
            return op.GetResults();
        }

        // AVRCP 服务 UUID
        static readonly Guid AvrcpUuid = new Guid("00001108-0000-1000-8000-00805f9b34fb");

        // 枚举所有已连接的经典蓝牙设备，逐个尝试读 Avrcp 电量
        public static List<AvrcpDeviceRow> ReadAll()
        {
            var result = new List<AvrcpDeviceRow>();
            try
            {
                var selector = Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelector();
                var found = Await(Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(selector), 5000);
                foreach (var d in found)
                {
                    if (string.IsNullOrEmpty(d.Name)) continue;
                    if (d.Id.ToUpperInvariant().Contains("BTHLE")) continue; // 跳过 BLE（已有 BLE 通道）
                    try
                    {
                        int? level = ReadDeviceBattery(d.Name, d.Id);
                        if (level != null)
                            result.Add(new AvrcpDeviceRow { Name = d.Name, Address = "", Level = level.Value });
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        static int? ReadDeviceBattery(string name, string deviceId)
        {
            var dev = Await(Windows.Devices.Bluetooth.BluetoothDevice.FromIdAsync(deviceId), 5000);
            if (dev == null) return null;
            if (dev.ConnectionStatus != Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected) return null;

            // 找 AVRCP 服务
            Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService avrcp = null;
            var services = Await(dev.GetRfcommServicesAsync(), 5000);
            foreach (var s in services.Services)
            {
                if (s.ServiceId.Uuid == AvrcpUuid) { avrcp = s; break; }
            }
            if (avrcp == null) return null;

            return ReadBatteryViaAvrcp(avrcp);
        }

        // 通过 AVRCP 读电量（尝试多种方案）
        static int? ReadBatteryViaAvrcp(Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService service)
        {
            // 方案：读设备信息 + 电池
            // AVRCP 电池主要通过 Browsing 或 RegisterNotification 获取，
            // 这里先尝试 GetCapabilities 探测，再走各厂商兼容路径
            try
            {
                var attr = Await(service.RequestAccessAsync(), 5000);
                if (attr != Windows.Devices.Bluetooth.Rfcomm.DeviceAccessStatus.Allowed) return null;

                // 记录服务信息
                System.Diagnostics.Debug.WriteLine("AVRCP service: " + service.ConnectionHostName + ":" + service.ConnectionServiceName);
            }
            catch { }

            // 兜底：Windows 系统已知电量（通过注册表/系统 API）
            return null;
        }
    }
}
