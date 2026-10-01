"""Render the two player models to a PNG for a slide.

Every box below is copied from Looks.PlaceholderLeopard and every colour from Palette.cs,
so this is the model the game builds rather than a drawing of it. Orthographic, flat per
face, backfaces culled, painter's algorithm. The box list is a copy and nothing links
it to the C#, so it has to be edited in step with Looks.PlaceholderLeopard.

The game camera looks down at 58 degrees, which turns a low animal into a totem pole, so
the portrait uses a lower three-quarter angle. The small inset shows the game's own angle.
"""
import math, os
from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "images")
FONTS = "C:\\Windows\\Fonts"
SS = 3
W, H = 1920, 1080


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
    b = [((0, 0.17, -0.02), (0.28, 0.2, 0.42), fur),
         ((0, 0.175, -0.24), (0.29, 0.22, 0.22), fur),
         ((0, 0.19, 0.16), (0.27, 0.2, 0.2), fur),
         ((0, 0.105, 0.03), (0.17, 0.07, 0.34), cream)]
    for lx, lz in ((-0.095, 0.17), (0.095, 0.17), (-0.095, -0.18), (0.095, -0.18)):
        b.append(((lx, 0.08, lz), (0.07, 0.16, 0.07), PAW))
    b += [((0, 0.27, 0.2), (0.14, 0.16, 0.14), fur),
          ((0, 0.3, 0.17), (0.3, 0.1, 0.2), accent),
          ((0, 0.4, 0.29), (0.21, 0.17, 0.2), fur),
          ((0, 0.37, 0.4), (0.12, 0.08, 0.1), cream),
          ((0, 0.4, 0.45), (0.05, 0.04, 0.02), UMBER)]
    for ex in (-0.055, 0.055):
        b.append(((ex, 0.42, 0.388), (0.035, 0.025, 0.02), NIGHT))
        b.append(((ex * 1.5, 0.5, 0.27), (0.07, 0.07, 0.03), ROSETTE))
    tx, ty, tz = 0, 0.26, -0.3
    for (x, y, z), s, c in (((0, 0, -0.08), (0.07, 0.07, 0.18), fur),
                            ((0, 0.05, -0.22), (0.06, 0.06, 0.14), fur),
                            ((0, 0.09, -0.31), (0.055, 0.055, 0.07), cream)):
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
FACES = (((0, 1, 3, 2), (0, 0, -1)), ((4, 6, 7, 5), (0, 0, 1)),
         ((0, 4, 5, 1), (-1, 0, 0)), ((2, 3, 7, 6), (1, 0, 0)),
         ((1, 5, 7, 3), (0, 1, 0)), ((0, 2, 6, 4), (0, -1, 0)))
LIGHT = (-0.38, 0.84, -0.39)


def shade(c, n):
    nl = max(0.0, dot(n, LIGHT))
    k = 0.58 + 0.42 * nl
    out = tuple(min(255, round(v * k)) for v in c)
    return out if nl > 0.05 else lerp(out, SIENNA, 0.08)   # warm bounce in the shade


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


def main():
    os.makedirs(OUT, exist_ok=True)
    img = Image.new("RGB", (W * SS, H * SS), NIGHT)
    d = ImageDraw.Draw(img)

    ground = lerp(NIGHT, PINE, 0.5)
    horizon = int(H * 0.66)
    d.rectangle([0, horizon * SS, W * SS, H * SS], fill=ground)
    d.rectangle([0, horizon * SS, W * SS, (horizon + 3) * SS], fill=SIENNA)

    portrait = basis(26, 38)       # readable three-quarter
    game = basis(58, 30)           # CameraFramer's own angle

    base = (horizon + 18) * SS
    for accent, ox in ((SIENNA, 0.29), (PINE, 0.71)):
        cx = int(W * ox) * SS
        d.ellipse([cx - 300 * SS, base - 42 * SS, cx + 300 * SS, base + 30 * SS],
                  fill=lerp(ground, NIGHT, 0.45))
        draw(d, project(leopard(accent), 210, portrait), cx, base, 620 * SS, 430 * SS)

    # Inset: the same model at the angle the game camera actually uses.
    ix = int(W * 0.5) * SS
    d.ellipse([ix - 110 * SS, base - 30 * SS, ix + 110 * SS, base + 12 * SS],
              fill=lerp(ground, NIGHT, 0.45))
    draw(d, project(leopard(SIENNA), 210, game), ix, base, 230 * SS, 230 * SS)

    img = img.resize((W, H), Image.LANCZOS)
    d = ImageDraw.Draw(img)

    d.text((W // 2, 86), "THE TWO LEOPARDS", font=font("georgiab.ttf", 62), fill=CREAM, anchor="mm")
    d.text((W // 2, 140), "one player steers both and presses Tab  ·  two players take one each",
           font=font("georgia.ttf", 28), fill=STONE, anchor="mm")

    for name, swatch, accent, ox in (("SEAT 1", "sienna  #A33B2A", SIENNA, 0.30),
                                     ("SEAT 2", "pine  #4F5B3A", PINE, 0.70)):
        x = int(W * ox)
        d.text((x, 800), name, font=font("framd.ttf", 32), fill=CREAM, anchor="mm")
        d.line([(x - 120, 828), (x + 120, 828)], fill=accent, width=4)
        d.text((x, 860), swatch, font=font("georgia.ttf", 23), fill=STONE, anchor="mm")

    d.text((W // 2, 800), "the game camera's own angle", font=font("georgia.ttf", 21),
           fill=lerp(STONE, NIGHT, 0.2), anchor="mm")
    d.text((W // 2, 1028),
           "the scarf is the only difference  ·  placeholder art, twenty boxes built in code at load",
           font=font("georgia.ttf", 23), fill=lerp(STONE, NIGHT, 0.35), anchor="mm")

    path = os.path.join(OUT, "player-models.png")
    img.save(path)
    print("wrote", path, img.size)


main()
