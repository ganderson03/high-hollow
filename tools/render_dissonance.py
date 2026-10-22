"""Render the dissonance pair: one level, two views of it.

    python tools/render_dissonance.py [level-id]

Left is the state the server holds. Right is what it tells a client, which is not the
same picture and is not meant to be: the hidden pits are withheld, and a key that does
not exist is shown. Both are drawn from the one level file, so the difference between
the two images is exactly the difference the server manufactures.

Writes the pair and each half separately, for a slide that wants to reveal them one at
a time.
"""
import os
import sys
from PIL import Image, ImageDraw

from boxrender import CREAM, NIGHT, SIENNA, STONE, font, lerp, out_dir
from render_level import render

GAP, PAD, CAPTION = 70, 54, 86


def main():
    level_id = (sys.argv[1:] or ["foothills-3"])[0]
    cell = 34

    halves = [("what the server knows", render(level_id, False, False, "truth", cell)),
              ("what a client is told", render(level_id, False, False, "client", cell))]

    w = PAD * 2 + halves[0][1].width * 2 + GAP
    h = PAD * 2 + halves[0][1].height + CAPTION
    img = Image.new("RGB", (w, h), NIGHT)
    d = ImageDraw.Draw(img)

    for i, (caption, half) in enumerate(halves):
        x = PAD + i * (half.width + GAP)
        img.paste(half, (x, PAD))
        mid = x + half.width // 2
        d.line([(x + 40, PAD + half.height + 26), (x + half.width - 40, PAD + half.height + 26)],
               fill=lerp(STONE, NIGHT, 0.55), width=2)
        d.text((mid, PAD + half.height + 56), caption, font=font("georgia.ttf", 30),
               fill=CREAM, anchor="mm")

    # A hairline between the two, so they read as a comparison rather than two pictures.
    cx = PAD + halves[0][1].width + GAP // 2
    d.line([(cx, PAD + 20), (cx, PAD + halves[0][1].height - 20)], fill=lerp(SIENNA, NIGHT, 0.45), width=2)

    for name, im in (("dissonance-pair", img),
                     ("dissonance-server", halves[0][1]),
                     ("dissonance-client", halves[1][1])):
        path = os.path.join(out_dir(), name + ".png")
        im.convert("RGB").save(path)
        print("wrote", os.path.normpath(path), im.size)


if __name__ == "__main__":
    main()
