# Battery Viewer for 2.4G Wireless Peripherals

[中文](README.md) | **English**

A native Windows app that shows the battery level of your 2.4G wireless peripherals (mice / keyboards / headsets). **Zero dependencies** — download, double-click, done. No Python, no runtimes, no internet connection required.

![icon](app.ico)

## Download

**Regular users**: grab the latest `Setup.exe` from [Releases](../../releases) and install (creates desktop / start menu shortcuts, uninstallable from Windows Settings → Apps).

**Portable fans**: download `BatteryViewer.exe` and run it directly.

## Features

- Pure C# + WPF, talks to `hid.dll` / `setupapi.dll` via P/Invoke, reads Bluetooth BLE through WinRT APIs
- Borderless rounded window + frosted-glass cards + drop shadows, light/dark theme follows system automatically
- **Desktop floating widget**: sits in the bottom-right corner, expands on hover to show every device, freely draggable
- **6 solid background colors + custom background image** (adjustable live blur without losing original resolution)
- **System tray**: dynamic battery icon (green/yellow/red + number), hover for all devices, choose which device the icon tracks
- **Optional autostart** (current-user Run key, no admin required)
- Color-coded battery levels (green ≥60 / yellow 30-59 / red <30), big percentage + progress bar
- Click a card for details (device name, VID/PID, read source, voltage)
- Auto-refresh every 60 seconds + manual refresh
- No network access, no logs, no driver installation

## Supported Devices

| Device | Method |
| --- | --- |
| Logitech G703 (Lightspeed) | HID++ 2.0 battery voltage (0x1001, Li-ion linear mapping) ✅ verified on real hardware |
| Other Logitech HID++ devices | HID++ 2.0 (0x1000 / 0x1004 / 0x1001) |
| **Bluetooth BLE devices** (e.g. AULA F2098 keyboard) | Standard Battery Service 0x180F ✅ verified on real hardware |
| Standard HID battery fields | Universal fallback for any device |
| ATK / VXE wireless mice | Nordic chipset (protocol 1 / protocol 2) |
| VXE / VGN wireless mice | Nordic chipset (protocol 1) |
| Endgame Gear | Vendor-specific Feature Report |
| VAXEE / ZYGEN | Vendor-specific Feature Report |
| Nordic nRF receivers | Feature Report 0x05 |
| DARKRIM SHADOW headsets | Jieli TLV proprietary protocol (CRC16) |
| **ASUS ROG wireless mice** (Gladius III etc. via OMNI receiver) | ROG proprietary protocol (`{03 12 07}` query over the OMNI col03 channel) ✅ verified on real hardware |

> ⚠️ Chinese gamepads (FlyDiGi etc.) masquerade as Xbox controllers over XInput on Windows — their protocol carries no battery data, so **no tool can read it**. Physically impossible, not a limitation of this app.
> ⚠️ Wireless devices sleep. If a reading fails, move the device first, then refresh.

## Build It Yourself

No Visual Studio needed — just run `编译.bat` (uses the C# compiler + WPF libraries shipped with Windows).

```
src/
  HidApi.cs           HID enumeration + struct definitions
  HidDevice.cs        Device open/read/write (separate async read handle, crash-safe)
  Protocols.cs        Vendor protocol table + DARKRIM (Jieli) TLV/CRC16
  BatteryReader.cs    Multi-strategy battery engine (standard fields / vendor protocols / Logitech HID++)
  BluetoothReader.cs  Bluetooth BLE Battery Service reader (WinRT GATT)
  MainWindow.cs       Main UI (WPF: theme / background / blur / tray / settings)
  App.cs              Entry point
  Installer.cs        Setup wizard (builds Setup.exe)
```

## Local API

Enable it in Settings → "本地 API" (default port 8787, configurable). The app runs a tiny HTTP server bound to localhost only — other programs and scripts can query battery data; the outside world cannot reach it.

```
GET http://localhost:8787/api/ping                # health check
GET http://localhost:8787/api/battery             # all devices (including failed reads)
GET http://localhost:8787/api/battery?readable=1  # only devices with a successful reading
GET http://localhost:8787/api/alerts              # devices currently below the alert threshold
```

Example response:

```json
{
  "ok": true,
  "devices": [
    {
      "name": "G703 LIGHTSPEED Wireless Gaming Mouse w/ HERO",
      "vidpid": "046D:C539",
      "ok": true,
      "level": 71,
      "status": "unknown",
      "voltage_mv": 3863,
      "source": "Logitech HID++ 2.0 (0x1001 battery voltage)",
      "detail": "device index 1: 3863 mV (linear mapping over Li-ion 3.0-4.2V)"
    }
  ],
  "time": "2026-10-24 12:55:23"
}
```

Multiple HID interfaces of the same device are de-duplicated automatically (the one with a successful reading wins). CORS is enabled, so local web pages can `fetch` it directly.

## Low Battery Alerts

Settings → "低电量报警" (Low Battery Alerts): on by default, threshold 20% (5-90 configurable). Every refresh cycle (60s) checks all devices and pops a Windows tray notification when a device drops below the threshold. **Each device only alerts once**; the state resets once it charges back above threshold + 5%, so no notification spam around the threshold.

`/api/alerts` returns the current alert list for external scripts:

```json
{"ok":true,"threshold":20,"alerts":[{"name":"G703 ...","level":15}]}
```

## Protocol Credits

Vendor proprietary protocols are reverse-engineered from [yszyszysz233/2.4G-battery-viewer](https://github.com/yszyszysz233/2.4G-battery-viewer) (Logitech HID++ spec references Solaar).

## Changelog

### v1.6 (2026-10-24) — Startup background fix + Local API + Low battery alerts

- Fix: custom background image not showing on autostart (image was loaded but never applied after the UI was built)
- Add: "startup background mode" setting — Auto / Always solid color / Always custom image
- Add: local HTTP API (`/api/battery`, `/api/ping`, `/api/alerts`; localhost only, configurable port)
- Add: low battery alerts (tray notification, configurable threshold, one-shot per device with hysteresis reset)

### v1.5 (2026-10-21) — Floating widget & settings window overhaul

- Collapsed floating widget is now a **true circle** (68×68, no more rectangular ghost)
- Dark / light (frosted white) themes for the widget
- Choose which device the circular badge tracks
- Settings window rebuilt in WPF with real Win10 Acrylic blur, resizable, no overlapping controls

### v1.4 (2026-10-21) — Desktop floating widget + autostart

- Desktop floating widget (on by default): circular badge in the corner, expands to show every device, draggable
- Autostart toggle (Settings → General), current-user Run key, no admin required

### v1.3 (2026-10-21) — Icon + installer

- New app icon (battery + charge bars), consistent across exe / taskbar / shortcuts
- Self-made `Setup.exe` wizard: 3-step install, shortcuts, uninstallable from Windows Settings
- Fix: dark/light theme judged by actual image brightness
- Fix: background image clipped to the window's rounded corners

### v1.2 (2026-10-20) — WPF rewrite

- UI migrated from WinForms to WPF: borderless rounded window, card shadows, hover highlights
- Live GPU blur (no resolution loss), 0-100% strength slider
- Tray enhancements: click to open, context menu, dynamic icon, hover device list
- Exit prompt with "remember choice"; two-tab settings; scroll-wheel-safe dropdowns

### v1.1 (2026-10-20) — Bluetooth BLE support

- BLE Battery Service (GATT 0x180F) reader for Bluetooth keyboards etc.
- 6 solid background colors, text color adapts to background brightness
- HID++ device name reading (e.g. G703 shows its full model name)

### v1.0 (2026-10-20) — Initial release

- Multi-protocol battery reading: Logitech HID++ 2.0 (incl. G703 voltage mapping), standard HID fields, ATK/VXE, Endgame, VAXEE, Nordic, DARKRIM
- Dark card UI, 60s auto refresh, device details

## License

MIT
