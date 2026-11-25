"""Generates TestData/canvas_test_d.tex: a 512x512 B8G8R8A8 test pattern for the Soundstage picture canvas test.
Run: python make_canvas_test.py   (needs numpy + Pillow; needs a texio.py .tex writer on the folders named in SOUNDSTAGE_PYLIBS)
Orientation cues: "TOP" and an up arrow at the top, "BOTTOM" at the bottom, "L" on the left edge and "R" on the right
(so a mirror or a rotation is obvious), and a colour grid whose top-left cell is red, top-right is blue."""
import os, struct, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

for _p in os.environ.get("SOUNDSTAGE_PYLIBS", "").split(os.pathsep):
    if _p:
        sys.path.insert(0, _p)
import texio

S = 512
img = Image.new("RGBA", (S, S), (255, 255, 255, 255))
d = ImageDraw.Draw(img)
cols = [(230, 40, 40), (240, 150, 30), (240, 230, 40), (50, 190, 70),
        (40, 200, 200), (50, 90, 230), (150, 60, 220), (230, 70, 180)]
cell = S // 8
for r in range(8):
    for c in range(8):
        base = cols[(c + r) % 8] if r % 2 == 0 else cols[(c - r) % 8]
        shade = 1.0 if (r + c) % 2 == 0 else 0.7
        d.rectangle([c * cell, r * cell, (c + 1) * cell - 1, (r + 1) * cell - 1],
                    fill=tuple(int(v * shade) for v in base) + (255,))
# Top-left red / top-right blue corner swatches, so a mirror is obvious.
d.rectangle([0, 0, cell * 2 - 1, cell * 2 - 1], fill=(255, 0, 0, 255))
d.rectangle([S - cell * 2, 0, S - 1, cell * 2 - 1], fill=(0, 0, 255, 255))

def font(size):
    for f in (r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()

def label(txt, xy, size, fill=(255, 255, 255, 255)):
    d.text(xy, txt, font=font(size), fill=fill, anchor="mm", stroke_width=max(2, size // 12), stroke_fill=(0, 0, 0, 255))

label("TOP", (S // 2, 40), 64)
label("BOTTOM", (S // 2, S - 40), 56)
label("L", (28, S // 2), 56)
label("R", (S - 28, S // 2), 56)
# Big up arrow in the middle.
d.polygon([(S // 2, 120), (S // 2 + 90, 250), (S // 2 + 35, 250), (S // 2 + 35, 400),
           (S // 2 - 35, 400), (S // 2 - 35, 250), (S // 2 - 90, 250)], fill=(255, 255, 255, 255), outline=(0, 0, 0, 255), width=6)

src_header = struct.pack("<I", 0x00800000)   # attribute flags of a stock 2D texture
out = texio.write(np.array(img), src_header)
dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "canvas_test_d.tex")
open(dst, "wb").write(out)
img.save(os.path.join(os.path.dirname(dst), "canvas_test_d_preview.png"))
print(dst, len(out), "bytes")
