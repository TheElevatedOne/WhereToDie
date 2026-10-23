#!/usr/bin/env python3
"""Generates the mod's 256x256 Thunderstore icon (icon.png) with Pillow.

A Page of Reincarnation in the green-to-red highlight frame the mod draws in the HUD,
standing in a ritual circle under the hallowed aurora.

Usage: python3 tools/make_icon.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SIZE = 256
GREEN = (90, 242, 77)
YELLOW = (255, 224, 51)
RED = (255, 38, 26)
PAGE = (226, 214, 180, 255)
INK = (52, 30, 40, 255)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def background():
    img = Image.new("RGBA", (SIZE, SIZE))
    px = img.load()
    for y in range(SIZE):
        for x in range(SIZE):
            t = y / (SIZE - 1)
            px[x, y] = (int(10 + 14 * t), int(12 + 10 * t), int(28 + 10 * t), 255)
    return img


def aurora():
    """Soft vertical curtains of yellow light, brightest at the top."""
    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for i, (x, w, h) in enumerate([(52, 10, 150), (86, 16, 190), (128, 22, 210), (170, 16, 185), (204, 10, 140)]):
        for y in range(h):
            a = int(200 * (1 - y / h) * (0.7 + 0.3 * math.sin(i + y / 18)))
            d.line([(x - w // 2, y), (x + w // 2, y)], fill=YELLOW + (max(a, 0),))
    return layer.filter(ImageFilter.GaussianBlur(7))


def ritual_circle():
    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.ellipse([28, 196, 228, 246], outline=RED + (230,), width=5)
    d.ellipse([58, 206, 198, 236], outline=RED + (150,), width=3)
    glow = layer.filter(ImageFilter.GaussianBlur(5))
    glow.alpha_composite(layer)
    return glow


def page_with_frame():
    """The page (square corners) inside a frame that fades green (bottom) to red (top)."""
    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    x0, y0, x1, y1 = 72, 46, 184, 206
    frame = 10
    px = layer.load()
    for y in range(y0 - frame, y1 + frame):
        t = 1 - (y - (y0 - frame)) / (y1 - y0 + 2 * frame)
        color = lerp(GREEN, RED, t)
        for x in range(x0 - frame, x1 + frame):
            px[x, y] = color + (255,)
    d = ImageDraw.Draw(layer)
    d.rectangle([x0, y0, x1, y1], fill=PAGE)

    # A skull-like mark and lines of script: "sacrifice yourself".
    cx = (x0 + x1) // 2
    d.ellipse([cx - 26, 70, cx + 26, 118], fill=INK)
    d.rectangle([cx - 16, 112, cx + 16, 130], fill=INK)
    d.ellipse([cx - 17, 84, cx - 5, 98], fill=PAGE)
    d.ellipse([cx + 5, 84, cx + 17, 98], fill=PAGE)
    for i in range(3):
        d.rectangle([cx - 9 + i * 7, 122, cx - 6 + i * 7, 130], fill=PAGE)
    for i, w in enumerate([76, 60, 70, 48]):
        y = 146 + i * 14
        d.rectangle([cx - w // 2, y, cx + w // 2, y + 5], fill=INK)

    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rectangle([x0 - frame + 6, y0 - frame + 8, x1 + frame + 6, y1 + frame + 8], fill=(0, 0, 0, 160))
    shadow = shadow.filter(ImageFilter.GaussianBlur(6))
    shadow.alpha_composite(layer)
    return shadow


def main():
    img = background()
    img.alpha_composite(aurora())
    img.alpha_composite(ritual_circle())
    img.alpha_composite(page_with_frame())
    img.convert("RGB").save(os.path.join(ROOT, "icon.png"))
    print("Wrote icon.png")


if __name__ == "__main__":
    main()
