"""Render the two player models to a PNG for a slide.

Every box below is copied from Looks.PlaceholderLeopard and every colour from Palette.cs,
so this is the model the game builds rather than a drawing of it. Orthographic, flat per
face, backfaces culled, painter's algorithm. The box list is a copy and nothing links
it to the C#, so it has to be edited in step with Looks.PlaceholderLeopard.

The game camera looks down at 58 degrees, which turns a low animal into a totem pole, so
the sheet uses a lower three-quarter angle. Two files are written: one on the poster
background and one with a transparent background, for dropping onto a slide of any colour.
"""
import math, os
from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "images")
FONTS = "C:\\Windows\\Fonts"
SS = 3
W, H = 1600, 900


def hex_rgb(v):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


NIGHT   = hex_rgb(0x232A25)
SIENNA  = hex_rgb(0xA33B2A)
CREAM   = hex_rgb(0xEFE2C4)
SAND    = hex_rgb(0xD8C29D)
PINE    = hex_rgb(0x4F5B3A)
UMBER   = hex_rgb(0x7A4A2E)
ROSETTE = hex_rgb(0x3B342C)
STONE   = hex_rgb(0x9C8B72)


def lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


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
# CORNERS is indexed 4*bx + 2*by + bz, with each b 0 for -1 and 1 for +1, so a face is
# the four corners sharing one bit. Pairing a face with another face's normal is an easy
# mistake to make and a hard one to see: the silhouette stays right while every face
# takes someone else's light.
FACES = (((0, 2, 6, 4), (0, 0, -1)), ((1, 5, 7, 3), (0, 0, 1)),
         ((0, 1, 3, 2), (-1, 0, 0)), ((4, 6, 7, 5), (1, 0, 0)),
         ((2, 3, 7, 6), (0, 1, 0)), ((0, 4, 5, 1), (0, -1, 0)))
# The scene's own Sun: its transform points the light along (-0.384, -0.743, 0.548),
# so this is the vector pointing back at it. Getting the sign of x wrong put the key
# light almost on the camera axis, which lit the chest as brightly as the back and read
# as underlit.
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
            depth = max(dot(pts3[i], f) for i in idx)
            out.append((depth, poly, shade(color, wn)))
    out.sort(key=lambda t: -t[0])
    return out


def draw(d, faces, cx, baseline, fit_w, fit_h):
    xs = [p[0] for _, poly, _ in faces for p in poly]
    ys = [p[1] for _, poly, _ in faces for p in poly]
    k = min(fit_w / (max(xs) - min(xs)), fit_h / (max(ys) - min(ys)))
    mx, lo = (max(xs) + min(xs)) / 2, min(ys)
    for _, poly, col in faces:
        d.polygon([(cx + (x - mx) * k, baseline - (y - lo) * k) for x, y in poly], fill=col, outline=col)
    return k * (max(xs) - min(xs))


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def sheet(transparent):
    bg = (0, 0, 0, 0) if transparent else NIGHT + (255,)
    img = Image.new("RGBA", (W * SS, H * SS), bg)
    d = ImageDraw.Draw(img)

    # Viewed from the sun's side, so the face and chest are lit rather than in shadow.
    # The game camera is fixed at yaw 30 and pitch 58; this is a portrait, not a screenshot.
    portrait = basis(26, -10)
    base = int(H * 0.74) * SS

    for accent, ox in ((SIENNA, 0.27), (PINE, 0.73)):
        cx = int(W * ox) * SS
        if not transparent:        # a shadow needs a floor to fall on
            d.ellipse([cx - 285 * SS, base - 40 * SS, cx + 285 * SS, base + 28 * SS],
                      fill=lerp(NIGHT, PINE, 0.22) + (255,))
        draw(d, project(leopard(accent), 210, portrait), cx, base, 660 * SS, 470 * SS)

    img = img.resize((W, H), Image.LANCZOS)
    d = ImageDraw.Draw(img)
    name = font("georgia.ttf", 46)
    for text, ox in (("sienna", 0.27), ("pine", 0.73)):
        d.text((int(W * ox), int(H * 0.87)), text, font=name, fill=CREAM + (255,), anchor="mm")

    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    for transparent, fname in ((False, "player-models.png"),
                               (True, "player-models-transparent.png")):
        img = sheet(transparent)
        path = os.path.join(OUT, fname)
        (img.convert("RGB") if not transparent else img).save(path)
        print("wrote", os.path.normpath(path), img.size)


main()
