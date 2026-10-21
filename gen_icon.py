# -*- coding: utf-8 -*-
# 生成 BatteryViewer 图标：深蓝圆角底 + 白色电池轮廓 + 绿色电量格
from PIL import Image, ImageDraw

def make_icon(size):
    S = 256  # 设计基准
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # 圆角底：深蓝到蓝渐变（手动画渐变）
    grad = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad)
    top = (56, 108, 255)     # 亮蓝
    bot = (28, 52, 140)      # 深蓝
    for y in range(S):
        t = y / S
        c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
        gd.line([(0, y), (S, y)], fill=c + (255,))
    # 圆角蒙版
    mask = Image.new('L', (S, S), 0)
    md = ImageDraw.Draw(mask)
    radius = 52
    md.rounded_rectangle([0, 0, S - 1, S - 1], radius=radius, fill=255)
    img.paste(grad, (0, 0), mask)

    # 电池主体（横置，白色描边圆角矩形）
    bx0, by0, bx1, by1 = 44, 92, 196, 164
    d.rounded_rectangle([bx0, by0, bx1, by1], radius=18, outline=(255, 255, 255, 255), width=10)
    # 电池正极头
    d.rounded_rectangle([bx1 - 2, 112, bx1 + 22, 144], radius=8, fill=(255, 255, 255, 255))

    # 电量格（绿色 3 格）
    gcol = (92, 230, 130, 255)
    pad = 16
    inner_y0, inner_y1 = by0 + pad, by1 - pad
    cell_w = 34
    gap = 10
    for i in range(3):
        x0 = bx0 + pad + i * (cell_w + gap)
        d.rounded_rectangle([x0, inner_y0, x0 + cell_w, inner_y1], radius=6, fill=gcol)

    # 高光（左上淡白斜条，装饰）
    hl = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hl)
    hd.ellipse([-60, -60, 160, 160], fill=(255, 255, 255, 26))
    img = Image.alpha_composite(img, hl)

    # 缩放到目标尺寸
    if size != S:
        img = img.resize((size, size), Image.LANCZOS)
    return img

sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [make_icon(s) for s in sizes]
frames[-1].save(
    r'D:\workbuddy 记忆\2026-10-20-11-39-20\BatteryViewer\app.ico',
    format='ICO',
    sizes=[(s, s) for s in sizes],
)
print('ico saved')
