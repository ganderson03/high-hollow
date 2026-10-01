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
                       HEATHER, MOSS, GROUT, PIT, BOULDER, NEEDLE, SS,
                       canvas, finish, font, lerp, out_dir)

LEVELS = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                      "..", "Assets", "StreamingAssets", "levels")

# glyph -> (fill, inset fraction, outline). Every piece is given a hue of its own and an
# outline, because the floor is a green checker and anything green or unlined vanishes
# into it. The in-game colours are not all usable here: a lever is pine green in the
# world, where it stands up off the floor and reads fine, and invisible from above.
SPAWNS = {"P": SIENNA, "1": lerp(PINE, CREAM, 0.45), "2": OCHRE}
CELL = {
    "#": (lerp(BOULDER, NIGHT, 0.66), 0.0, None),
    "T": (lerp(NEEDLE, NIGHT, 0.25), 0.12, None),
    "D": (UMBER, 0.04, NIGHT),
    "F": (EMBER, 0.04, NIGHT),
    "L": (HEATHER, 0.24, NIGHT),
    "B": (SAND, 0.14, NIGHT),
    "K": (OCHRE, 0.26, NIGHT),
    "k": (lerp(OCHRE, STONE, 0.55), 0.26, NIGHT),
    "H": (PIT, 0.08, SAND),
    "h": (lerp(PIT, SAND, 0.22), 0.2, SAND),
    "S": (STONE, 0.24, CREAM),
    "G": (CREAM, 0.18, EMBER),
    "E": (lerp(MOSS, CREAM, 0.3), 0.36, None),
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

    def rect(x, y, inset, fill, outline=None):
        a = inset * cell
        d.rectangle([((margin + x * cell + a)) * SS, ((margin + y * cell + a)) * SS,
                     ((margin + (x + 1) * cell - a)) * SS, ((margin + (y + 1) * cell - a)) * SS],
                    fill=fill + (255,),
                    outline=None if outline is None else outline + (255,),
                    width=0 if outline is None else max(2, cell // 16) * SS)

    def disc(x, y, r, fill, outline):
        cx = (margin + x * cell + cell / 2) * SS
        cy = (margin + y * cell + cell / 2) * SS
        d.ellipse([cx - r * SS, cy - r * SS, cx + r * SS, cy + r * SS],
                  fill=fill + (255,), outline=outline + (255,), width=3 * SS)

    # Floor first, as a quiet checker so the grid is legible without drawing lines.
    for y in range(h):
        for x in range(w):
            g = at(x, y)
            if g == "T":
                rect(x, y, 0, lerp(NIGHT, NEEDLE, 0.4))
            elif g == "#":
                rect(x, y, 0, lerp(NIGHT, NEEDLE, 0.4))
            else:
                # A quiet checker, so the grid reads without drawing lines over it.
                rect(x, y, 0, MOSS if (x + y) % 2 == 0 else lerp(MOSS, GROUT, 0.6))

    def centre(c):
        # levelcheck stores tiles at (x, h - 1 - row): its y counts north from the south
        # edge, while rows and this picture run north to south. Flip, or the route comes
        # out mirrored and walks through walls.
        row = h - 1 - c[1]
        return ((margin + c[0] * cell + cell / 2) * SS, (margin + row * cell + cell / 2) * SS)

    # The route runs UNDER the pieces: drawn last it covers the keys and levers it is
    # there to explain.
    ends = []
    if with_route and duo:
        acts, states = duo[2], duo[3]
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
            ends.append((thin[-1], colour))

    for y in range(h):
        for x in range(w):
            g = at(x, y)
            if g in CELL:
                fill, inset, outline = CELL[g]
                rect(x, y, inset, fill, outline)

    # Where each leopard starts, in its own scarf colour, and where it ends up.
    for y in range(h):
        for x in range(w):
            if at(x, y) in SPAWNS:
                disc(x, y, cell * 0.26, SPAWNS[at(x, y)], NIGHT)
    for (px, py), colour in ends:
        r = 15 * SS
        d.ellipse([px - r, py - r, px + r, py + r], fill=colour + (255,),
                  outline=NIGHT + (255,), width=3 * SS)

    # Where control passes from one leopard to the other.
    if with_route and duo:
        acts, states = duo[2], duo[3]
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
