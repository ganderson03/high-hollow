"""Render a level from above, optionally with the solver's shortest cooperative route.

    python tools/render_level.py foothills-1 [--no-route] [--name]

The map comes from the level file and the route from levelcheck, so the picture cannot
disagree with the game: if the route is drawn, a solver proved it wins.
"""
import os
import sys
from PIL import ImageDraw

import levelcheck
from boxrender import (NIGHT, SIENNA, EMBER, CREAM, SAND, OCHRE, PINE, UMBER, STONE,
                       MOSS, GROUT, PIT, BOULDER, NEEDLE, SS,
                       canvas, finish, font, lerp, out_dir)

LEVELS = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                      "..", "Assets", "StreamingAssets", "levels")

# glyph -> (fill, inset fraction). None means draw the floor only.
CELL = {
    "#": (lerp(BOULDER, NIGHT, 0.66), 0.0),
    "T": (NEEDLE, 0.12),
    "D": (UMBER, 0.06),
    "F": (lerp(UMBER, OCHRE, 0.45), 0.06),
    "L": (PINE, 0.26),
    "B": (STONE, 0.16),
    "K": (OCHRE, 0.3),
    "k": (lerp(OCHRE, MOSS, 0.55), 0.3),
    "H": (PIT, 0.1),
    "h": (lerp(PIT, MOSS, 0.55), 0.1),
    "S": (STONE, 0.28),
    "G": (CREAM, 0.22),
    "E": (lerp(MOSS, CREAM, 0.12), 0.34),
}


def render(level_id, with_route=True, with_name=False):
    path = os.path.join(LEVELS, level_id + ".json")
    if with_route:
        d_, w, h, errs, duo, _states, _solo = levelcheck.analyse(path, max_states=14_000_000)
    else:
        d_, w, h, _tiles, _regions, _links, errs = levelcheck.load(path)
        duo = None
    rows = d_["rows"]

    def at(x, y):
        r = rows[y]
        return r[x] if x < len(r) else "#"

    margin, cell = 60, 46
    W, H = w * cell + margin * 2, h * cell + margin * 2 + (46 if with_name else 0)
    img, d = canvas(W, H, False)

    def rect(x, y, inset, fill):
        a = inset * cell
        d.rectangle([((margin + x * cell + a)) * SS, ((margin + y * cell + a)) * SS,
                     ((margin + (x + 1) * cell - a)) * SS, ((margin + (y + 1) * cell - a)) * SS],
                    fill=fill + (255,))

    # Floor first, as a quiet checker so the grid is legible without drawing lines.
    for y in range(h):
        for x in range(w):
            g = at(x, y)
            if g == "T":
                rect(x, y, 0, lerp(NIGHT, NEEDLE, 0.45))
            elif g == "#":
                rect(x, y, 0, lerp(NIGHT, NEEDLE, 0.45))
            else:
                # A quiet checker, so the grid reads without drawing lines over it.
                rect(x, y, 0, MOSS if (x + y) % 2 == 0 else lerp(MOSS, GROUT, 0.6))

    for y in range(h):
        for x in range(w):
            g = at(x, y)
            if g in CELL:
                fill, inset = CELL[g]
                rect(x, y, inset, fill)

    # The route, one line per leopard, drawn over the map.
    if with_route and duo:
        actions, _switches, acts, states = duo[0], duo[1], duo[2], duo[3]

        def centre(c):
            # levelcheck stores tiles at (x, h - 1 - row): its y counts north from the south
            # edge, while rows and this picture run north to south. Flip, or the route comes
            # out mirrored and walks through walls.
            row = h - 1 - c[1]
            return ((margin + c[0] * cell + cell / 2) * SS, (margin + row * cell + cell / 2) * SS)

        for seat, colour in ((0, SIENNA), (1, lerp(PINE, CREAM, 0.45))):
            pts = [centre(st[seat]) for st in states]
            thin = [pts[0]]
            for p in pts[1:]:
                if p != thin[-1]:
                    thin.append(p)
            if len(thin) > 1:
                # A dark halo under each line, so pine does not sink into the grass.
                d.line(thin, fill=NIGHT + (255,), width=13 * SS, joint="curve")
                d.line(thin, fill=colour + (255,), width=7 * SS, joint="curve")
            for p, r in ((thin[0], 12), (thin[-1], 15)):
                d.ellipse([p[0] - r * SS, p[1] - r * SS, p[0] + r * SS, p[1] + r * SS],
                          fill=colour + (255,), outline=NIGHT + (255,), width=3 * SS)

        # Where control passes from one leopard to the other.
        for i in range(len(acts) - 1):
            if acts[i][0] != acts[i + 1][0]:
                cx, cy = centre(states[min(i + 1, len(states) - 1)][acts[i][0]])
                d.ellipse([cx - 13 * SS, cy - 13 * SS, cx + 13 * SS, cy + 13 * SS],
                          outline=EMBER + (255,), width=5 * SS)

    img = finish(img, W, H)
    if with_name:
        dd = ImageDraw.Draw(img)
        dd.text((W // 2, H - 30), d_["name"].lower(), font=font("georgia.ttf", 26),
                fill=CREAM + (255,), anchor="mm")
    return img


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    flags = {a for a in sys.argv[1:] if a.startswith("--")}
    for level_id in (args or ["foothills-1"]):
        img = render(level_id, "--no-route" not in flags, "--name" in flags)
        suffix = "-map" if "--no-route" in flags else "-route"
        out = os.path.join(out_dir(), level_id + suffix + ".png")
        img.convert("RGB").save(out)
        print("wrote", os.path.normpath(out), img.size)


main()
