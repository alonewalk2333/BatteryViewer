@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ============================================
echo   2.4G 外设电量查看器 - 编译脚本（WPF 版）
echo ============================================
echo.

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
if not exist "%WPF%\PresentationFramework.dll" set WPF=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\WPF

set SRW=
for /d %%i in ("%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.Runtime.WindowsRuntime\v4.0_4.0.0.0_*") do set SRW=%%i\System.Runtime.WindowsRuntime.dll
set SR=
for /d %%i in ("%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0_*") do set SR=%%i\System.Runtime.dll
set SX=
for /d %%i in ("%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0_*") do set SX=%%i\System.Xaml.dll

echo 使用编译器: %CSC%
echo.

rem ---------- 1. 主程序 ----------
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /unsafe /out:BatteryViewer.exe /win32manifest:app.manifest /win32icon:app.ico /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%SX%" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll" /r:"%SRW%" /r:"%SR%" /r:"%WINDIR%\System32\WinMetadata\Windows.Foundation.winmd" /r:"%WINDIR%\System32\WinMetadata\Windows.Devices.winmd" /r:"%WINDIR%\System32\WinMetadata\Windows.Storage.winmd" src\HidApi.cs src\HidDevice.cs src\Protocols.cs src\BatteryReader.cs src\BluetoothReader.cs src\FloatingWidget.cs src\AutoStart.cs src\MainWindow.cs src\App.cs

if errorlevel 1 (
    echo.
    echo [主程序编译失败]
    pause
    exit /b 1
)
echo [1/2] 主程序 BatteryViewer.exe 编译成功

rem ---------- 2. 安装器（内嵌主程序） ----------
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:Setup.exe /win32manifest:app.manifest /win32icon:app.ico /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%SX%" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\Microsoft.CSharp.dll" /resource:BatteryViewer.exe src\Installer.cs

if errorlevel 1 (
    echo.
    echo [安装器编译失败]
    pause
    exit /b 1
)
echo [2/2] 安装器 Setup.exe 编译成功

echo.
echo 全部完成！BatteryViewer.exe（主程序）+ Setup.exe（安装包）
echo.
pause
