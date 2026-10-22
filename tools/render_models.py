"""Render the two player models to a sheet.

Every box is copied from Looks.PlaceholderLeopard and every colour from Palette.cs, so
this is the model the game builds rather than a drawing of it. The box list is a copy and
nothing links it to the C#, so it has to be edited in step with Looks.PlaceholderLeopard.

The game camera looks down at 58 degrees, which turns a low animal into a totem pole, so
the sheet uses a lower three-quarter angle, from the sun's side. Two files are written:
one on the poster background and one transparent, for a slide of any colour.
"""
import os
from PIL import ImageDraw

from boxrender import (NIGHT, SIENNA, CREAM, SAND, PINE, UMBER, ROSETTE, SS,
                       basis, canvas, draw, finish, font, lerp, out_dir, project)

W, H = 1600, 900
PAW = lerp(SAND, UMBER, 0.25)


def leopard(accent):
    """Looks.PlaceholderLeopard, box for box, times ScaleFor(Player)."""
    fur, cream = SAND, CREAM
    b = [
         ((0, 0.28, -0.17), (0.29, 0.23, 0.17), fur),
         ((0, 0.27, -0.01), (0.26, 0.21, 0.27), fur),
         ((0, 0.28, 0.145), (0.27, 0.22, 0.15), fur),
         ((0, 0.19, -0.01), (0.18, 0.06, 0.24), cream),
    ]
    for lx, lz in ((-0.09, 0.13), (0.09, 0.13), (-0.095, -0.145), (0.095, -0.145)):
        b.append(((lx, 0.08, lz), (0.085, 0.16, 0.085), PAW))
    b += [
         ((0, 0.375, 0.2), (0.155, 0.145, 0.145), fur),
         ((0, 0.387, 0.2), (0.24, 0.085, 0.2), accent),
         ((0, 0.5, 0.247), (0.213, 0.17, 0.196), fur),
         ((0, 0.468, 0.353), (0.119, 0.077, 0.068), cream),
         ((0, 0.497, 0.391), (0.051, 0.038, 0.017), UMBER),
    ]
    for ex in (-0.055, 0.055):
        b.append(((ex, 0.523, 0.343), (0.031, 0.022, 0.017), NIGHT))
        b.append(((ex * 1.3909, 0.6035, 0.2295), (0.068, 0.064, 0.03), ROSETTE))
    tx, ty, tz = 0, 0.357, -0.238
    for (x, y, z), s, c in (((0, 0, -0.068), (0.085, 0.085, 0.17), fur),
                            ((0, 0.06, -0.196), (0.077, 0.077, 0.145), fur),
                            ((0, 0.11, -0.2975), (0.068, 0.068, 0.085), cream)):
        b.append(((tx + x, ty + y, tz + z), s, c))
    k = 1.15
    return [((p[0] * k, p[1] * k, p[2] * k), (s[0] * k, s[1] * k, s[2] * k), c) for p, s, c in b]


def sheet(transparent):
    img, d = canvas(W, H, transparent)

    # Viewed from the sun's side, so the face and chest are lit rather than in shadow.
    # The game camera is fixed at yaw 30 and pitch 58; this is a portrait, not a screenshot.
    portrait = basis(26, -10)
    base = int(H * 0.74) * SS

    for accent, ox in ((SIENNA, 0.27), (PINE, 0.73)):
        cx = int(W * ox) * SS
        if not transparent:            # a shadow needs a floor to fall on
            d.ellipse([cx - 285 * SS, base - 40 * SS, cx + 285 * SS, base + 28 * SS],
                      fill=lerp(NIGHT, PINE, 0.22) + (255,))
        draw(d, project(leopard(accent), 210, portrait), cx, base, 660 * SS, 470 * SS)

    img = finish(img, W, H)
    d = ImageDraw.Draw(img)
    name = font("georgia.ttf", 46)
    for text, ox in (("sienna", 0.27), ("pine", 0.73)):
        d.text((int(W * ox), int(H * 0.87)), text, font=name, fill=CREAM + (255,), anchor="mm")
    return img


def main():
    for transparent, fname in ((False, "player-models.png"),
                               (True, "player-models-transparent.png")):
        img = sheet(transparent)
        path = os.path.join(out_dir(), fname)
        (img if transparent else img.convert("RGB")).save(path)
        print("wrote", os.path.normpath(path), img.size)


if __name__ == "__main__":
    main()
