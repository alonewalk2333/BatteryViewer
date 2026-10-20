using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BatteryViewer
{
    // 一个已打开的 HID 接口句柄。等价 Python 的 HidDevice。
    // 关键坑（原作者踩过）：不能在同一句柄上「阻塞读 + 写」并发，否则驱动层可能崩进程。
    // 因此 Input Report 用一个独立的、带 FILE_FLAG_OVERLAPPED 的读句柄 + 事件超时 + CancelIoEx。
    public class HidDevice : IDisposable
    {
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 0x01;
        const uint FILE_SHARE_WRITE = 0x02;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        const long INVALID_HANDLE_VALUE = -1;
        const int ERROR_IO_PENDING = 997;
        const uint WAIT_OBJECT_0 = 0;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CancelIoEx(IntPtr hFile, ref OVERLAPPED lpOverlapped);

        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_GetFeature(IntPtr HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_SetFeature(IntPtr HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_SetOutputReport(IntPtr HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);

        [StructLayout(LayoutKind.Sequential)]
        struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        readonly string path;
        IntPtr handle = IntPtr.Zero;
        IntPtr readHandle = IntPtr.Zero;
        IntPtr readEvent = IntPtr.Zero;
        IntPtr preparsed = IntPtr.Zero;

        public bool WriteAccess { get; private set; }
        public ushort InputReportByteLength { get; private set; }
        public ushort OutputReportByteLength { get; private set; }
        public ushort FeatureReportByteLength { get; private set; }

        public HidDevice(string path, bool write)
        {
            this.path = path;
            WriteAccess = write;
            uint access = write ? (GENERIC_READ | GENERIC_WRITE) : 0;
            handle = CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if ((handle.ToInt64() == INVALID_HANDLE_VALUE || handle == IntPtr.Zero) && write)
            {
                handle = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                WriteAccess = false;
            }
            if (handle.ToInt64() == INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                throw new HidException("无法打开设备 (CreateFileW 失败)");

            // 读能力表拿三长度（preparsed 数据缓存起来，供 value_caps 用）
            if (HidD_GetPreparsedData(handle, out preparsed) && preparsed != IntPtr.Zero)
            {
                HidApi.HIDP_CAPS caps;
                if (HidP_GetCaps(preparsed, out caps) == 0 || HidP_GetCaps(preparsed, out caps) == 0x00110000)
                {
                    InputReportByteLength = caps.InputReportByteLength;
                    OutputReportByteLength = caps.OutputReportByteLength;
                    FeatureReportByteLength = caps.FeatureReportByteLength;
                }
            }
        }

        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_GetPreparsedData(IntPtr HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        static extern int HidP_GetCaps(IntPtr PreparsedData, out HidApi.HIDP_CAPS Capabilities);

        public byte[] GetFeatureReport(byte reportId, int length)
        {
            if (length <= 0) length = FeatureReportByteLength > 0 ? FeatureReportByteLength : 64;
            var buf = new byte[length];
            buf[0] = reportId;
            if (!HidD_GetFeature(handle, buf, (uint)length))
                throw new HidException("读 Feature Report 0x" + reportId.ToString("X2") + " 失败");
            return buf;
        }

        public void SetFeatureReport(byte[] data)
        {
            if (!WriteAccess) throw new HidException("当前句柄无写权限，无法发送 Feature Report");
            if (!HidD_SetFeature(handle, data, (uint)data.Length))
                throw new HidException("写 Feature Report 失败");
        }

        public void WriteOutput(byte[] data)
        {
            if (!WriteAccess) throw new HidException("当前句柄无写权限，无法发送 Output Report");
            if (HidD_SetOutputReport(handle, data, (uint)data.Length)) return;
            uint written = 0;
            if (!WriteFile(handle, data, (uint)data.Length, out written, IntPtr.Zero))
                throw new HidException("WriteFile 失败");
        }

        // 读一条 Input Report，超时返回 null。独立异步读句柄 + 事件超时。
        public byte[] ReadInput(int timeoutMs, int length)
        {
            if (InputReportByteLength == 0) return null;
            OpenReader();
            if (readHandle == IntPtr.Zero) return null;
            if (length <= 0) length = InputReportByteLength;

            var buf = new byte[length];
            var ov = new OVERLAPPED();
            ov.hEvent = readEvent;
            ResetEvent(readEvent);
            uint read = 0;

            bool ok = ReadFile(readHandle, buf, (uint)length, out read, ref ov);
            if (ok) return Slice(buf, (int)read);
            if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return null;

            int waitMs = Math.Max(timeoutMs, 1);
            uint r = WaitForSingleObject(readEvent, (uint)waitMs);
            if (r == WAIT_OBJECT_0)
            {
                uint done = 0;
                if (GetOverlappedResult(readHandle, ref ov, out done, false))
                    return Slice(buf, (int)done);
            }
            else
            {
                CancelIoEx(readHandle, ref ov);
                WaitForSingleObject(readEvent, 200);
                uint done = 0;
                GetOverlappedResult(readHandle, ref ov, out done, false);
            }
            return null;
        }

        static byte[] Slice(byte[] src, int len)
        {
            var r = new byte[len];
            Array.Copy(src, r, len);
            return r;
        }

        void OpenReader()
        {
            if (readHandle != IntPtr.Zero) return;
            IntPtr h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h.ToInt64() == INVALID_HANDLE_VALUE || h == IntPtr.Zero) return;
            IntPtr ev = CreateEventW(IntPtr.Zero, true, false, null);
            if (ev == IntPtr.Zero) { CloseHandle(h); return; }
            readHandle = h;
            readEvent = ev;
        }

        public void Drain()
        {
            if (InputReportByteLength == 0) return;
            for (int i = 0; i < 64; i++)
                if (ReadInput(8, InputReportByteLength) == null) return;
        }

        public void Dispose()
        {
            if (readHandle != IntPtr.Zero) { var ov = new OVERLAPPED(); CancelIoEx(readHandle, ref ov); CloseHandle(readHandle); readHandle = IntPtr.Zero; }
            if (readEvent != IntPtr.Zero) { CloseHandle(readEvent); readEvent = IntPtr.Zero; }
            if (preparsed != IntPtr.Zero) { HidD_FreePreparsedData(preparsed); preparsed = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }

        // ---- 能力表：值能力（用于标准 HID 电量字段策略）----
        // 不 marshal 整个 HIDP_VALUE_CAPS（布局易错），按固定偏移手动读关键字段。
        // 每个 cap 分配 128 字节安全余量（实际结构约 60-64 字节），彻底避免堆溢出。
        public List<ValueCap> ValueCaps(HidPReportType reportType)
        {
            var result = new List<ValueCap>();
            if (preparsed == IntPtr.Zero) return result;
            ushort count = 0;
            int probeSt = HidP_GetValueCaps(reportType, IntPtr.Zero, ref count, preparsed);
            if (count == 0 || probeSt != 0) return result;

            const int CAP_SIZE = 128;
            IntPtr buf = Marshal.AllocHGlobal(CAP_SIZE * count);
            try
            {
                if (HidP_GetValueCaps(reportType, buf, ref count, preparsed) != 0)
                    return result;
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = IntPtr.Add(buf, i * CAP_SIZE);
                    result.Add(new ValueCap
                    {
                        UsagePage = (ushort)Marshal.ReadInt16(p, 0),
                        ReportID = Marshal.ReadByte(p, 2),
                        LogicalMax = Marshal.ReadInt32(p, 44),
                        Usage = (ushort)Marshal.ReadInt16(p, 56),
                        LinkCollection = (ushort)Marshal.ReadInt16(p, 6),
                    });
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return result;
        }

        public int? UsageValue(HidPReportType reportType, byte[] report, ValueCap cap)
        {
            if (preparsed == IntPtr.Zero) return null;
            uint value = 0;
            int st = HidP_GetUsageValue(reportType, cap.UsagePage, cap.LinkCollection, cap.Usage,
                out value, preparsed, report, (uint)report.Length);
            if (st != 0 && st != 0x00110000) return null;
            return (int)value;
        }

        [DllImport("hid.dll", SetLastError = true)]
        static extern int HidP_GetValueCaps(HidPReportType ReportType, IntPtr ValueCaps, ref ushort ValueCapsLength, IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        static extern int HidP_GetUsageValue(HidPReportType ReportType, ushort UsagePage, ushort LinkCollection, ushort Usage, out uint UsageValue, IntPtr PreparsedData, byte[] Report, uint ReportLength);
    }

    public class ValueCap
    {
        public ushort UsagePage;
        public byte ReportID;
        public int LogicalMax;
        public ushort Usage;
        public ushort LinkCollection;
    }
}
