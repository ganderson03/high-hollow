"""Shared bits for the documentation renders: the palette, an orthographic box renderer
and the fonts.

The palette mirrors Assets/Crawler/Render/Palette.cs and the light mirrors the scene's
Sun, so a render looks like the game rather than like an illustration of it. Nothing
links these numbers to the C#, so they have to be edited together.
"""
import math, os
from PIL import Image, ImageDraw, ImageFont

FONTS = "C:\\Windows\\Fonts"
SS = 3                                   # supersample factor, for clean edges


def hex_rgb(v):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


NIGHT   = hex_rgb(0x232A25)
SIENNA  = hex_rgb(0xA33B2A)
EMBER   = hex_rgb(0xE06B4F)
CREAM   = hex_rgb(0xEFE2C4)
SAND    = hex_rgb(0xD8C29D)
OCHRE   = hex_rgb(0xD9A441)
PINE    = hex_rgb(0x4F5B3A)
UMBER   = hex_rgb(0x7A4A2E)
STONE   = hex_rgb(0x9C8B72)
HEATHER = hex_rgb(0xB9A3C4)
PLUM    = hex_rgb(0x6B3F4A)
ROSETTE = hex_rgb(0x3B342C)
MOSS    = hex_rgb(0x5B6544)
GROUT   = hex_rgb(0x444C35)
PIT     = hex_rgb(0x2A2620)
BOULDER = hex_rgb(0x6F6C66)
NEEDLE  = hex_rgb(0x3F4A2F)


def lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def out_dir():
    d = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "images")
    os.makedirs(d, exist_ok=True)
    return d


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def basis(pitch_deg, yaw_deg):
    a, y = math.radians(pitch_deg), math.radians(yaw_deg)
    f = (math.sin(y) * math.cos(a), -math.sin(a), math.cos(y) * math.cos(a))   # view direction
    r = (math.cos(y), 0.0, -math.sin(y))                                       # screen right
    u = (math.sin(y) * math.sin(a), math.cos(a), math.cos(y) * math.sin(a))    # screen up
    return f, r, u


def rot_y(p, deg):
    a = math.radians(deg)
    x, y, z = p
    return (x * math.cos(a) + z * math.sin(a), y, -x * math.sin(a) + z * math.cos(a))


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


CORNERS = [(sx, sy, sz) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]

# CORNERS is indexed 4*bx + 2*by + bz, with each b 0 for -1 and 1 for +1, so a face is the
# four corners sharing one bit. Pairing a face with another face's normal is an easy
# mistake to make and a hard one to see: the silhouette stays right while every face takes
# someone else's light.
FACES = (((0, 2, 6, 4), (0, 0, -1)), ((1, 5, 7, 3), (0, 0, 1)),
         ((0, 1, 3, 2), (-1, 0, 0)), ((4, 6, 7, 5), (1, 0, 0)),
         ((2, 3, 7, 6), (0, 1, 0)), ((0, 4, 5, 1), (0, -1, 0)))

# The scene's Sun points the light along (-0.384, -0.743, 0.548); this is the vector back
# towards it.
LIGHT = (0.384, 0.743, -0.548)


def shade(c, n):
    nl = max(0.0, dot(n, LIGHT))
    out = tuple(min(255, round(v * (0.40 + 0.60 * nl))) for v in c)
    # GridFloor sets a trilight ambient: warm sky above, sienna bounce off the ground.
    if n[1] > 0.3:
        return lerp(out, CREAM, 0.10 * n[1])
    if n[1] < -0.3 or nl < 0.05:
        return lerp(out, SIENNA, 0.10)
    return out


def project(boxes, yaw_model, cam):
    """Every visible face as (depth, screen polygon, colour), far to near."""
    f, r, u = cam
    out = []
    for pos, scale, color in boxes:
        hs = (scale[0] / 2, scale[1] / 2, scale[2] / 2)
        pts3 = [rot_y((pos[0] + sx * hs[0], pos[1] + sy * hs[1], pos[2] + sz * hs[2]), yaw_model)
                for sx, sy, sz in CORNERS]
        for idx, n in FACES:
            wn = rot_y(n, yaw_model)
            if dot(wn, f) >= 0:                            # facing away: never seen
                continue
            poly = [(dot(pts3[i], r), dot(pts3[i], u)) for i in idx]
            out.append((max(dot(pts3[i], f) for i in idx), poly, shade(color, wn)))
    out.sort(key=lambda t: -t[0])
    return out


def draw(d, faces, cx, baseline, fit_w, fit_h):
    """Draw a projected model standing on `baseline`, scaled to fit, centred on cx."""
    xs = [p[0] for _, poly, _ in faces for p in poly]
    ys = [p[1] for _, poly, _ in faces for p in poly]
    k = min(fit_w / (max(xs) - min(xs)), fit_h / (max(ys) - min(ys)))
    mx, lo = (max(xs) + min(xs)) / 2, min(ys)
    for _, poly, col in faces:
        d.polygon([(cx + (x - mx) * k, baseline - (y - lo) * k) for x, y in poly],
                  fill=col, outline=col)
    return k


def canvas(w, h, transparent):
    img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0) if transparent else NIGHT + (255,))
    return img, ImageDraw.Draw(img)


def finish(img, w, h):
    return img.resize((w, h), Image.LANCZOS)
