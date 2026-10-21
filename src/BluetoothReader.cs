using System;
using System.Collections.Generic;
using System.Threading;

namespace BatteryViewer
{
    // BLE（低功耗蓝牙）设备电量读取：走 GATT 电池服务 0x180F。
    // 适用：蓝牙键盘/鼠标/耳机等暴露标准 Battery Service 的设备（如狼蛛 F2098）。
    public class BleDeviceRow
    {
        public string Name;
        public string Address;
        public int Level;
    }

    public static class BluetoothReader
    {
        static T Await<T>(Windows.Foundation.IAsyncOperation<T> op, int timeoutMs)
        {
            int deadline = Environment.TickCount + timeoutMs;
            while (op.Status == Windows.Foundation.AsyncStatus.Started)
            {
                if (Environment.TickCount > deadline) throw new Exception("BLE 操作超时");
                Thread.Sleep(20);
            }
            if (op.Status == Windows.Foundation.AsyncStatus.Error)
                throw new Exception(op.ErrorCode.Message);
            return op.GetResults();
        }

        static readonly Guid BatteryServiceUuid = new Guid("0000180f-0000-1000-8000-00805f9b34fb");
        static readonly Guid BatteryLevelUuid = new Guid("00002a19-0000-1000-8000-00805f9b34fb");

        // 枚举系统已知 BLE 设备，逐个尝试读电池服务
        public static List<BleDeviceRow> ReadAll()
        {
            var result = new List<BleDeviceRow>();
            try
            {
                var selector = Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelector();
                var found = Await(Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(selector), 5000);
                foreach (var d in found)
                {
                    if (string.IsNullOrEmpty(d.Name)) continue;
                    try
                    {
                        int? level = ReadDeviceBattery(d.Id);
                        if (level != null)
                        {
                            result.Add(new BleDeviceRow
                            {
                                Name = d.Name,
                                Address = ExtractAddress(d.Id),
                                Level = level.Value,
                            });
                        }
                    }
                    catch { /* 单台设备失败不影响其它 */ }
                }
            }
            catch { }
            return result;
        }

        static int? ReadDeviceBattery(string deviceId)
        {
            var ble = Await(Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(deviceId), 5000);
            if (ble == null) return null;

            try
            {
                var svcResult = Await(ble.GetGattServicesForUuidAsync(BatteryServiceUuid,
                    Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached), 5000);
                if (svcResult.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success
                    || svcResult.Services.Count == 0) return null;
                var bat = svcResult.Services[0];

                var chars = Await(bat.GetCharacteristicsForUuidAsync(BatteryLevelUuid), 5000);
                if (chars.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success
                    || chars.Characteristics.Count == 0) return null;

                var val = Await(chars.Characteristics[0].ReadValueAsync(
                    Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached), 5000);
                if (val.Value == null || val.Value.Length == 0) return null;

                var reader = Windows.Storage.Streams.DataReader.FromBuffer(val.Value);
                byte[] data = new byte[val.Value.Length];
                reader.ReadBytes(data);
                if (data[0] > 100) return null; // 非法电量
                return data[0];
            }
            finally
            {
                try { ble.Dispose(); } catch { }
            }
        }

        // 从 DeviceInformation.Id（如 BluetoothLE#BluetoothLE20:xx...-fc:ae:7c:b7:24:63）提取地址
        static string ExtractAddress(string id)
        {
            try
            {
                int dash = id.LastIndexOf('-');
                if (dash > 0) return id.Substring(dash + 1).ToUpper();
            }
            catch { }
            return "BLE";
        }
    }
}
