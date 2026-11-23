"""Render the two leopards as a scene rather than as a lineup.

Sienna is standing on a plate, which is the only reason the door beside pine is open:
the one piece of the game that cannot be done alone, staged with the real models, the
real prop boxes, the real floor colours and the scene's own sun. Shadows are cast, not
drawn under the feet.
"""
import os
from PIL import ImageDraw

from boxrender import (NIGHT, SIENNA, CREAM, PINE, UMBER, STONE, MOSS, GROUT, SS,
                       basis, canvas, finish, fit, flat, font, hull, lerp, out_dir,
                       project, shadows)
from render_models import leopard

W, H = 1600, 900

PLATE = [((0, 0.03, 0), (0.8, 0.06, 0.8), STONE)]
DOOR = [((0, 0.5, 0), (0.9, 1.0, 0.3), UMBER)]

# Where everything stands, in cells. The camera looks from azimuth 166, so these two
# yaws sit the same distance either side of head-on: mirrored three-quarter views.
# Angles that differ in how side-on they are make one leopard look bigger than the
# other, which under an orthographic camera it is not.
SIENNA_AT, SIENNA_YAW = (-1.05, 0.0, 0.2), 198
PINE_AT, PINE_YAW = (0.85, 0.0, -0.3), 134
DOOR_AT = (2.0, 0.0, -0.9)


def floor_quads(cam, x0, x1, z0, z1):
    """The game's own floor checker, as flat tiles."""
    out = []
    for x in range(x0, x1):
        for z in range(z0, z1):
            corners = [(x, 0.0, z), (x + 1, 0.0, z), (x + 1, 0.0, z + 1), (x, 0.0, z + 1)]
            tint = MOSS if (x + z) % 2 == 0 else lerp(MOSS, GROUT, 0.6)
            out.append((flat(corners, cam), lerp(tint, NIGHT, 0.18)))
    return out


def scene():
    img, d = canvas(W, H, False)
    cam = basis(24, -14)

    tiles = floor_quads(cam, -2, 3, -2, 2)

    sienna = leopard(SIENNA)
    pine = leopard(PINE)

    casts = (shadows(PLATE, 0, cam, SIENNA_AT)
             + shadows(sienna, SIENNA_YAW, cam, SIENNA_AT)
             + shadows(pine, PINE_YAW, cam, PINE_AT)
             + shadows(DOOR, 0, cam, DOOR_AT))

    faces = (project(PLATE, 0, cam, SIENNA_AT)
             + project(DOOR, 0, cam, DOOR_AT)
             + project(sienna, SIENNA_YAW, cam, SIENNA_AT)
             + project(pine, PINE_YAW, cam, PINE_AT))
    faces.sort(key=lambda t: -t[0])

    to_screen = fit(faces, [q for q, _ in tiles] + casts,
                    int(W * 0.5) * SS, int(H * 0.78) * SS, 1340 * SS, 660 * SS)

    for quad, tint in tiles:
        d.polygon([to_screen(p) for p in quad], fill=tint + (255,))
    for poly in casts:
        if len(poly) >= 3:
            d.polygon([to_screen(p) for p in poly], fill=lerp(MOSS, NIGHT, 0.74) + (255,))
    for _, poly, col in faces:
        pts = [to_screen(p) for p in poly]
        d.polygon(pts, fill=col + (255,), outline=col + (255,))

    img = finish(img, W, H)
    d = ImageDraw.Draw(img)
    name = font("georgia.ttf", 42)
    d.text((int(W * 0.28), int(H * 0.92)), "sienna", font=name, fill=CREAM + (255,), anchor="mm")
    d.text((int(W * 0.63), int(H * 0.92)), "pine", font=name, fill=CREAM + (255,), anchor="mm")
    return img


def main():
    path = os.path.join(out_dir(), "player-models-scene.png")
    scene().convert("RGB").save(path)
    print("wrote", os.path.normpath(path))


main()
