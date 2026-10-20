# 2.4G 外设电量查看器

Windows 上查看 2.4G 无线外设（鼠标/耳机）电量的实体软件。**零依赖**，双击 `BatteryViewer.exe` 即用——不用装 Python、不用装任何运行库、不用联网。

## 特点

- 纯 C# + .NET Framework，P/Invoke 直接调 `hid.dll` / `setupapi.dll` / `kernel32.dll`
- 单文件原生 exe（约 44 KB），双击即用
- 枚举本机所有 HID 设备，自动识别协议读取电量
- 毛玻璃卡片式界面：大号百分比 + 进度条，颜色分级（绿 ≥60 / 黄 30-59 / 红 <30）
- **自定义背景图 + 高斯模糊**（毛玻璃效果），设置自动记忆
- 点击卡片看详情（设备名、VID/PID、读取来源、电压）
- 每 60 秒自动刷新 + 手动刷新
- 不联网、不写日志、不装驱动

## 下载

到 [Releases](../../releases) 下载 `BatteryViewer.exe`，或直接下载仓库根目录的 `BatteryViewer.exe`。

## 支持的设备

| 设备 | 读取方式 |
|------|----------|
| 罗技 G703（Lightspeed） | HID++ 2.0 电池电压（0x1001，锂电池线性折算百分比）✅ 真机验证 |
| 罗技其他 HID++ 设备 | HID++ 2.0（0x1000 电池状态 / 0x1004 统一电池 / 0x1001 电压） |
| 标准 HID 电池字段 | 任意设备通用兜底（Battery Strength 0x0006/0x0020、电池系统页 0x0085） |
| ATK / VXE 无线鼠标 | Nordic 方案（协议 1 / 协议 2） |
| VXE / VGN 无线鼠标 | Nordic 方案（协议 1） |
| Endgame Gear | 私有 Feature Report |
| VAXEE / ZYGEN | 私有 Feature Report |
| Nordic nRF 接收器 | Feature Report 0x05 |
| DARKRIM SHADOW 耳机 | 杰理 TLV 私有协议（CRC16） |

## 使用方法

1. 下载并双击 `BatteryViewer.exe`
2. 界面自动枚举设备并显示电量
3. 点「刷新电量」手动刷新；默认每 60 秒自动刷新
4. 点「设置背景」选背景图，可开高斯模糊
5. 点击设备卡片查看详情

> ⚠️ 无线设备会休眠。如果显示"没有读到电量"，先**动一下鼠标/按一下键盘**再点刷新。

## 自己编译

不需要 Visual Studio，用系统自带的 C# 编译器即可：

```bat
双击 编译.bat
```

或手动：

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe @build.rsp
```

## 源码结构

```
src/
  HidApi.cs        HID 枚举 + 结构体定义
  HidDevice.cs     设备打开/读写（独立异步读句柄，防并发崩溃）
  Protocols.cs     厂商协议表 + DARKRIM 杰理 TLV/CRC16
  BatteryReader.cs 多策略电量读取引擎（标准字段/厂商协议/罗技 HID++/DARKRIM）
  Program.cs       主界面（毛玻璃卡片、背景图、高斯模糊）
```

## 技术要点

- 罗技 HID++ 2.0：查询前需先 ping 唤醒（设备休眠）；`Root.GetFeature` 回复的 feature index 在**第 4 字节**（不是第 6 字节，那是版本号）。
- HID++ 电压设备（如 G703）只报毫伏，按锂电池 3.0V–4.2V 线性折算百分比。
- 同一 HID 句柄上「阻塞读 + 并发写」会崩进程，用独立 `FILE_FLAG_OVERLAPPED` 读句柄 + 事件超时规避。

## 协议来源

厂商私有协议逆向自 [yszyszysz233/2.4G-battery-viewer](https://github.com/yszyszysz233/2.4G-battery-viewer)（罗技 HID++ 规范参考 Solaar / Logitech cpg-docs）。

## 你的设备不在列表里？

把设备型号或 VID/PID 发到 Issues，我可以按这套方法逆向出协议补进去。

## License

仅供学习与个人使用。
