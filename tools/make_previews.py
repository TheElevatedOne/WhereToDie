#!/usr/bin/env python3
"""Turns the showcase screenshots (make screenshots) into small README previews with Pillow.

  NN_*.png        first-person shots without HUD -> docs/screenshots/NN_*.jpg, 480 px wide
  hud_icon_*.png  the N'tormat page icons, cropped from a 1920x1080 HUD shot and enlarged (*_row: several pages)
  hud_chat_*.png  the chat lines, cropped from a 1920x1080 HUD shot

Usage: python3 tools/make_previews.py <screenshot dir> <output dir>
"""
import os
import sys

from PIL import Image

PREVIEW_WIDTH = 480
# Regions of the 1920x1080 HUD (scaled for other resolutions).
ICON_BOX = (1360, 835, 1490, 925)
# With several pages the icon row is wider.
ICON_ROW_BOX = (1290, 835, 1580, 925)
CHAT_BOX = (110, 690, 640, 835)


def scaled(box, size):
    sx, sy = size[0] / 1920, size[1] / 1080
    return tuple(int(v * (sx if i % 2 == 0 else sy)) for i, v in enumerate(box))


def main():
    src, dst = sys.argv[1], sys.argv[2]
    os.makedirs(dst, exist_ok=True)
    for name in sorted(os.listdir(src)):
        if not name.endswith(".png"):
            continue
        image = Image.open(os.path.join(src, name)).convert("RGB")
        stem = name[:-4]
        if stem.startswith("hud_icon_"):
            box = ICON_ROW_BOX if stem.endswith("_row") else ICON_BOX
            crop = image.crop(scaled(box, image.size))
            crop = crop.resize((crop.width * 2, crop.height * 2), Image.LANCZOS)
            out = os.path.join(dst, stem + ".png")
            crop.save(out)
        elif stem.startswith("hud_chat_"):
            out = os.path.join(dst, stem + ".png")
            image.crop(scaled(CHAT_BOX, image.size)).save(out)
        elif stem[:2].isdigit():
            height = round(image.height * PREVIEW_WIDTH / image.width)
            out = os.path.join(dst, stem + ".jpg")
            image.resize((PREVIEW_WIDTH, height), Image.LANCZOS).save(out, quality=82, optimize=True)
        else:
            continue
        print("wrote", out)


if __name__ == "__main__":
    main()
