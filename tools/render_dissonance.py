"""Render the dissonance pair: one level, two views of it.

    python tools/render_dissonance.py [level-id]

Left is the state the server holds. Right is what it tells a client, which is not the
same picture and is not meant to be: the hidden pits are withheld, and a key that does
not exist is shown. Both are drawn from the one level file, so the difference between
the two images is exactly the difference the server manufactures.

Writes the pair and each half separately, every one of them captioned, so a slide can
reveal them one at a time or show them together.
"""
import os
import sys
from PIL import Image, ImageDraw

from boxrender import CREAM, NIGHT, SIENNA, STONE, font, lerp, out_dir
from render_level import render

PAD, CAPTION, GAP = 54, 86, 70


def panel(img, caption):
    """One map on the poster background, with a rule and its caption beneath."""
    out = Image.new("RGB", (img.width + PAD * 2, img.height + PAD + CAPTION), NIGHT)
    out.paste(img, (PAD, PAD))
    d = ImageDraw.Draw(out)
    y = PAD + img.height
    d.line([(PAD + 40, y + 26), (PAD + img.width - 40, y + 26)],
           fill=lerp(STONE, NIGHT, 0.55), width=2)
    d.text((out.width // 2, y + 56), caption, font=font("georgia.ttf", 30),
           fill=CREAM, anchor="mm")
    return out


def main():
    level_id = (sys.argv[1:] or ["foothills-3"])[0]
    cell = 34

    panels = [panel(render(level_id, False, False, "truth", cell), "what the server knows"),
              panel(render(level_id, False, False, "client", cell), "what a client is told")]

    pair = Image.new("RGB", (panels[0].width * 2 + GAP, panels[0].height), NIGHT)
    for i, pan in enumerate(panels):
        pair.paste(pan, (i * (pan.width + GAP), 0))
    # A hairline between them, so they read as one comparison rather than two pictures.
    d = ImageDraw.Draw(pair)
    cx = panels[0].width + GAP // 2
    d.line([(cx, PAD + 20), (cx, panels[0].height - CAPTION - 20)],
           fill=lerp(SIENNA, NIGHT, 0.45), width=2)

    for name, im in (("dissonance-pair", pair),
                     ("dissonance-server", panels[0]),
                     ("dissonance-client", panels[1])):
        path = os.path.join(out_dir(), name + ".png")
        im.save(path)
        print("wrote", os.path.normpath(path), im.size)


if __name__ == "__main__":
    main()
