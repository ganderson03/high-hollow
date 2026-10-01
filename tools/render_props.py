"""Render the placeholder props to a sheet: a swatch of what each glyph looks like.

The boxes are copied from Looks.BuildPlaceholder and the scales from Looks.ScaleFor, so
this is what the game builds. Keep them in step by hand; nothing links the two.
"""
import os
from PIL import ImageDraw

from boxrender import (NIGHT, CREAM, SAND, OCHRE, PINE, UMBER, STONE, PIT, SS,
                       basis, canvas, draw, finish, font, lerp, out_dir, project)

W, H = 1600, 900

# name -> (ScaleFor, [(pos, scale, colour)])
PROPS = [
    ("lever",      1.35, [((0, 0.03, 0), (0.5, 0.06, 0.5), STONE),
                          ((0, 0.3, 0), (0.12, 0.6, 0.12), PINE)]),
    ("plate",      1.0,  [((0, 0.03, 0), (0.8, 0.06, 0.8), STONE)]),
    ("door",       1.0,  [((0, 0.5, 0), (0.9, 1.0, 0.3), UMBER)]),
    ("key",        1.5,  [((0, 0.4, 0), (0.25, 0.25, 0.25), OCHRE)]),
    ("pit",        1.0,  [((0, 0.02, 0), (0.9, 0.04, 0.9), PIT)]),
    ("sightstone", 1.3,  [((0, 0.45, 0), (0.35, 0.9, 0.35), STONE)]),
    ("summit",     1.15, [((0, 0.35, 0), (0.6, 0.7, 0.6), CREAM)]),
]


def scaled(boxes, k):
    return [((p[0] * k, p[1] * k, p[2] * k), (s[0] * k, s[1] * k, s[2] * k), c) for p, s, c in boxes]


def sheet(transparent):
    img, d = canvas(W, H, transparent)
    cam = basis(26, -10)
    base = int(H * 0.62) * SS
    step = W / (len(PROPS) + 0.4)

    for i, (name, k, boxes) in enumerate(PROPS):
        cx = int(step * (i + 0.7)) * SS
        if not transparent:
            d.ellipse([cx - 78 * SS, base - 20 * SS, cx + 78 * SS, base + 14 * SS],
                      fill=lerp(NIGHT, PINE, 0.22) + (255,))
        draw(d, project(scaled(boxes, k), 0, cam), cx, base, 170 * SS, 250 * SS)

    img = finish(img, W, H)
    d = ImageDraw.Draw(img)
    label = font("georgia.ttf", 30)
    for i, (name, _, _) in enumerate(PROPS):
        d.text((int(step * (i + 0.7)), int(H * 0.72)), name, font=label, fill=CREAM + (255,), anchor="mm")
    return img


def main():
    for transparent, fname in ((False, "props.png"), (True, "props-transparent.png")):
        img = sheet(transparent)
        path = os.path.join(out_dir(), fname)
        (img if transparent else img.convert("RGB")).save(path)
        print("wrote", os.path.normpath(path), img.size)


main()
