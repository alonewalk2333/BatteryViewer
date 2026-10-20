@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ============================================
echo   2.4G 外设电量查看器 - 编译脚本
echo ============================================
echo.

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo [错误] 找不到系统自带的 C# 编译器。
    echo 请安装 .NET Framework 4.8：
    echo   https://dotnet.microsoft.com/download/dotnet-framework/net48
    echo.
    pause
    exit /b 1
)

echo 使用编译器: %CSC%
echo.

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /unsafe /out:BatteryViewer.exe /win32manifest:app.manifest /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\HidApi.cs src\HidDevice.cs src\Protocols.cs src\BatteryReader.cs src\Program.cs

if errorlevel 1 (
    echo.
    echo [编译失败] 请把上面的错误信息发给我。
    pause
    exit /b 1
)

echo.
echo ============================================
echo   编译成功！生成 BatteryViewer.exe
echo   双击即可运行（无需安装任何环境）
echo ============================================
echo.
pause
