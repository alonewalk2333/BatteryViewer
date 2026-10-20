using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BatteryViewer
{
    // HID 底层：枚举接口 + 打开/读写 Feature/Output/Input Report。
    // 行为逐字节等价于原版 Python hid_win.py。

    public static class HidApi
    {
        static Guid HidGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
        const uint DIGCF_PRESENT = 0x02;
        const uint DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 0x01;
        const uint FILE_SHARE_WRITE = 0x02;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        const long INVALID_HANDLE_VALUE = -1;
        const int ERROR_IO_PENDING = 997;
        const uint WAIT_OBJECT_0 = 0;
        const uint INFINITE = 0xFFFFFFFF;

        // ---- setupapi ----
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevsW(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        // ---- hid.dll ----
        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_GetPreparsedData(IntPtr HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_GetAttributes(IntPtr HidDeviceObject, out HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_GetFeature(IntPtr HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_SetFeature(IntPtr HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_SetOutputReport(IntPtr HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);

        // ---- kernel32 ----
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetOverlappedResult(IntPtr hFile, IntPtr lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

        [DllImport("kernel32.dll")]
        static extern int GetLastError();

        // ---- 结构体 ----
        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        // HIDP_CAPS 完整结构体（含 Reserved[17] + 计数），保证与实际大小一致。
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            public fixed ushort Reserved[17];
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        // ---- 枚举 ----
        public static List<HidDeviceInfo> Enumerate()
        {
            var result = new List<HidDeviceInfo>();
            Guid hidGuid = HidGuid;
            IntPtr devInfoSet = SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (devInfoSet == IntPtr.Zero || devInfoSet.ToInt64() == INVALID_HANDLE_VALUE)
                return result;
            try
            {
                uint idx = 0;
                while (true)
                {
                    var did = new SP_DEVICE_INTERFACE_DATA();
                    did.cbSize = Marshal.SizeOf(did);
                    if (!SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref hidGuid, idx, ref did))
                        break;
                    idx++;

                    uint required = 0;
                    SetupDiGetDeviceInterfaceDetailW(devInfoSet, ref did, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required == 0) continue;

                    IntPtr detailPtr = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W 的 cbSize 字段：x64=8, x86=6
                        Marshal.WriteInt32(detailPtr, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetailW(devInfoSet, ref did, detailPtr, required, out required, IntPtr.Zero))
                            continue;
                        string path = Marshal.PtrToStringUni(detailPtr + 4);
                        if (path == null) continue;

                        var info = Probe(path);
                        if (info != null) result.Add(info);
                    }
                    finally { Marshal.FreeHGlobal(detailPtr); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(devInfoSet); }
            return result;
        }

        static HidDeviceInfo Probe(string path)
        {
            IntPtr h = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.ToInt64() == INVALID_HANDLE_VALUE || h == IntPtr.Zero) return null;
            try
            {
                var info = new HidDeviceInfo { Path = path };

                var attrs = new HIDD_ATTRIBUTES();
                attrs.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                if (HidD_GetAttributes(h, out attrs))
                {
                    info.Vid = attrs.VendorID;
                    info.Pid = attrs.ProductID;
                }

                IntPtr pp = IntPtr.Zero;
                if (HidD_GetPreparsedData(h, out pp) && pp != IntPtr.Zero)
                {
                    try
                    {
                        HIDP_CAPS caps;
                        int st = HidP_GetCaps(pp, out caps);
                        if (st == 0 || st == 0x00110000) // STATUS_SUCCESS / HIDP_STATUS_SUCCESS
                        {
                            info.UsagePage = caps.UsagePage;
                            info.Usage = caps.Usage;
                            info.InputLen = caps.InputReportByteLength;
                            info.OutputLen = caps.OutputReportByteLength;
                            info.FeatureLen = caps.FeatureReportByteLength;
                        }
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
                return info;
            }
            finally { CloseHandle(h); }
        }
    }

    public class HidDeviceInfo
    {
        public string Path;
        public ushort Vid;
        public ushort Pid;
        public ushort UsagePage;
        public ushort Usage;
        public ushort InputLen;
        public ushort OutputLen;
        public ushort FeatureLen;
        public string DebugNote = "";
        public string VidPid { get { return Vid.ToString("X4") + ":" + Pid.ToString("X4"); } }
    }

    // HidP_GetValueCaps / HidP_GetUsageValue 用的报告类型枚举
    public enum HidPReportType : ushort
    {
        Input = 0,
        Output = 1,
        Feature = 2,
    }

    public class HidException : Exception
    {
        public HidException(string msg) : base(msg) { }
    }
}
