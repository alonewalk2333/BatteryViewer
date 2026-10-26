# -*- coding: utf-8 -*-
"""BatteryViewer C# 一键编译脚本。

用法：
    python compile.py
    python compile.py --test       # 编译后运行 10 秒测试
    python compile.py --installer  # 同时编译安装包
"""
import os
import sys
import re
import subprocess
import argparse
import time

BASE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(BASE, "src")
CSC = r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"


def log(msg):
    print(f"[compile] {msg}")


def fix_from_argb():
    """把 Color.FromArgb(r,g,b) 三参数自动补成四参数 Color.FromArgb(255,r,g,b)。"""
    fixed = 0
    for name in os.listdir(SRC):
        if not name.endswith(".cs"):
            continue
        path = os.path.join(SRC, name)
        with open(path, "r", encoding="utf-8") as f:
            text = f.read()

        def repl(m):
            nonlocal fixed
            parts = [x.strip() for x in m.group(1).split(",")]
            if len(parts) == 3:
                fixed += 1
                return f"Color.FromArgb(255, {', '.join(parts)})"
            return m.group(0)

        new_text = re.sub(r"Color\.FromArgb\(([^()]*)\)", repl, text)
        if new_text != text:
            with open(path, "w", encoding="utf-8") as f:
                f.write(new_text)
            log(f"已修复 {name}")
    if fixed == 0:
        log("未发现需要修复的 FromArgb")
    return fixed


def run_csc(rsp, output_name):
    log(f"编译 {output_name} …")
    rsp_path = os.path.join(BASE, rsp)
    if not os.path.exists(rsp_path):
        raise FileNotFoundError(f"找不到响应文件：{rsp_path}")
    exe = os.path.join(BASE, output_name)
    mtime_before = os.path.getmtime(exe) if os.path.exists(exe) else 0
    result = subprocess.run([CSC, f"@{rsp_path}"], cwd=BASE, capture_output=True, text=True,
                            encoding="gbk", errors="replace")
    if result.stdout:
        print(result.stdout)
    if result.stderr:
        print(result.stderr, file=sys.stderr)
    if not os.path.exists(exe):
        raise RuntimeError(f"编译失败，未生成 {output_name}")
    if os.path.getmtime(exe) <= mtime_before:
        raise RuntimeError(f"编译失败，{output_name} 未被更新（文件被占用？先 taskkill）")
    if "error CS" in (result.stdout or ""):
        raise RuntimeError("编译输出含 error CS，详见上方日志")
    size = os.path.getsize(exe)
    log(f"生成 {output_name} ({size} 字节)")


def test_run():
    exe = os.path.join(BASE, "BatteryViewer.exe")
    log("启动测试 10 秒…")
    proc = subprocess.Popen([exe], cwd=BASE)
    time.sleep(10)
    proc.poll()
    if proc.returncode is None:
        log("运行稳定，正在结束进程…")
        subprocess.run(["taskkill", "/F", "/IM", "BatteryViewer.exe"], capture_output=True)
        proc.wait(timeout=5)
        log("测试通过")
    else:
        log(f"进程已退出，返回码：{proc.returncode}")


def main():
    parser = argparse.ArgumentParser(description="BatteryViewer 一键编译")
    parser.add_argument("--installer", action="store_true", help="同时编译安装包")
    parser.add_argument("--test", action="store_true", help="编译后运行 10 秒测试")
    args = parser.parse_args()

    if not os.path.exists(CSC):
        log(f"找不到 C# 编译器：{CSC}")
        sys.exit(1)

    fix_from_argb()
    run_csc("build.rsp", "BatteryViewer.exe")
    if args.installer:
        run_csc("build_installer.rsp", "Setup.exe")
    if args.test:
        test_run()
    log("完成")


if __name__ == "__main__":
    main()
