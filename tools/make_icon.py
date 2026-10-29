"""Draws icon.png (256x256) for the Thunderstore package: a window with a highlighted term and the pinned window it
opened, in Valheim's dark and gold colours. Needs Pillow. Run: python tools/make_icon.py"""
import os

from PIL import Image, ImageDraw

SIZE = 256
SCALE = 4  # draw large, shrink for smooth edges
S = SIZE * SCALE

BG = (24, 19, 15, 255)
EDGE = (92, 72, 48, 255)
WINDOW = (44, 36, 29, 255)
WINDOW2 = (52, 43, 34, 255)
GOLD = (246, 196, 83, 255)
GOLD_DARK = (176, 132, 52, 255)
TEXT = (196, 188, 176, 255)
TEXT_DIM = (130, 122, 112, 255)


def r(v):
    return int(v * SCALE)


def box(d, x0, y0, x1, y1, fill, outline, width, radius=10):
    d.rounded_rectangle([r(x0), r(y0), r(x1), r(y1)], radius=r(radius), fill=fill, outline=outline, width=r(width))


def line(d, x0, y, x1, color, width=6):
    d.rounded_rectangle([r(x0), r(y - width / 2), r(x1), r(y + width / 2)], radius=r(width / 2), fill=color)


img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
box(d, 4, 4, 252, 252, BG, EDGE, 4, radius=36)

# first window: title and text with one highlighted term
box(d, 26, 38, 168, 150, WINDOW, GOLD_DARK, 3)
line(d, 40, 58, 118, GOLD, 8)
line(d, 40, 82, 152, TEXT_DIM)
line(d, 40, 100, 74, TEXT_DIM)
line(d, 80, 100, 134, GOLD)  # the term
line(d, 80, 108, 134, GOLD, 2)  # underline
line(d, 40, 118, 140, TEXT_DIM)
line(d, 40, 134, 104, TEXT_DIM)

# the window it opened, pinned (gold frame and pin)
box(d, 96, 118, 234, 228, WINDOW2, GOLD, 5)
line(d, 110, 140, 176, GOLD, 8)
line(d, 110, 164, 218, TEXT)
line(d, 110, 182, 196, TEXT)
line(d, 110, 200, 210, TEXT)
# pin: round head and needle
d.ellipse([r(204), r(128), r(222), r(146)], fill=GOLD)
d.rounded_rectangle([r(211.5), r(144), r(214.5), r(158)], radius=r(1.5), fill=GOLD)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "icon.png")
img.resize((SIZE, SIZE), Image.LANCZOS).save(out)
print("icon written:", os.path.normpath(out))
