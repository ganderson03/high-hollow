"""Builds the Foothills level files (run from anywhere: python tools/build_foothills.py). Coordinates here are (col, row) with row 0 at the top,
exactly as the rows read in the JSON; regions are converted to the level's south-origin rects."""
import json
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "StreamingAssets", "levels")


class Map:
    def __init__(self, w, h, fill="T"):
        self.w, self.h = w, h
        self.g = [[fill] * w for _ in range(h)]
        self.regions = []
        self.links = []

    def fill(self, c0, r0, c1, r1, ch):
        for r in range(r0, r1 + 1):
            for c in range(c0, c1 + 1):
                self.g[r][c] = ch

    def room(self, c0, r0, c1, r1):
        """Walls on the border, floor inside."""
        self.fill(c0, r0, c1, r1, "#")
        self.fill(c0 + 1, r0 + 1, c1 - 1, r1 - 1, ".")

    def put(self, c, r, ch):
        self.g[r][c] = ch

    def region(self, name, subtitle, rule, c0, r0, c1, r1):
        y = self.h - 1 - r1
        self.regions.append({"name": name, "subtitle": subtitle,
                             "rect": [c0, y, c1 - c0 + 1, r1 - r0 + 1], "rule": rule})

    def link(self, src, dst):
        self.links.append({"from": list(src), "to": list(dst)})

    def write(self, lid, name, biome):
        d = {"id": lid, "name": name, "biome": biome, "width": self.w, "height": self.h,
             "rows": ["".join(r) for r in self.g], "regions": self.regions}
        if self.links:
            d["links"] = self.links
        path = os.path.join(OUT, lid + ".json")
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(to_json(d))
        return path


def to_json(d):
    """Readable output: one map row per line, one region or link per line."""
    lines = ["{"]
    lines.append(f'  "id": {json.dumps(d["id"])},')
    lines.append(f'  "name": {json.dumps(d["name"])},')
    lines.append(f'  "biome": {json.dumps(d["biome"])},')
    lines.append(f'  "width": {d["width"]},')
    lines.append(f'  "height": {d["height"]},')
    lines.append('  "rows": [')
    rows = d["rows"]
    for i, r in enumerate(rows):
        lines.append(f'    {json.dumps(r)}' + ("," if i < len(rows) - 1 else ""))
    lines.append("  ],")
    lines.append('  "regions": [')
    regs = d["regions"]
    for i, r in enumerate(regs):
        lines.append("    " + json.dumps(r, ensure_ascii=False) + ("," if i < len(regs) - 1 else ""))
    lines.append("  ]" + ("," if "links" in d else ""))
    if "links" in d:
        lines.append('  "links": [')
        for i, l in enumerate(d["links"]):
            lines.append("    " + json.dumps(l) + ("," if i < len(d["links"]) - 1 else ""))
        lines.append("  ]")
    lines.append("}")
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------- 1-1 Trailhead
def trailhead():
    m = Map(24, 9)
    # Room A: the trailhead pen. A lever right beside the door out.
    m.room(1, 1, 7, 7)
    m.put(3, 4, "P")
    m.put(3, 5, "1")
    m.put(6, 3, "L")
    m.put(7, 4, "D")
    m.link((6, 3), (7, 4))
    m.region("The Trailhead", "Space beside a lever pulls it", "lever", 2, 2, 6, 6)

    # Room B: two lanes. Each lane's lever opens the OTHER lane's door.
    m.room(7, 1, 17, 7)
    m.put(7, 4, "D")                      # shared wall with A
    m.fill(9, 2, 15, 6, "#")              # solid block, then carve the lanes
    m.fill(9, 3, 15, 3, ".")              # north lane
    m.fill(9, 5, 15, 5, ".")              # south lane
    m.put(10, 2, "L")                     # north lane lever, in an alcove
    m.put(10, 6, "L")                     # south lane lever, in an alcove
    m.put(13, 3, "D")                     # north lane door
    m.put(13, 5, "D")                     # south lane door
    m.link((10, 2), (13, 5))              # north lever opens the south door
    m.link((10, 6), (13, 3))              # south lever opens the north door
    m.put(17, 4, ".")                     # gap into the summit
    m.region("Split Lanes", "each lane's lever opens the other lane  ·  Tab switches leopard", "lever", 8, 2, 16, 6)

    # Room C: the summit. Both leopards need to be here.
    m.room(17, 1, 22, 7)
    m.put(17, 4, ".")
    m.put(20, 4, "G")
    m.region("Summit Rise", "both leopards to the summit", "goal", 18, 2, 21, 6)
    return m.write("foothills-1", "Trailhead", "foothills")


# ---------------------------------------------------------------- 1-2 Stile Meadow
def stile_meadow():
    m = Map(30, 11)
    # Room A: the stile. A plate on each side of one door.
    m.room(1, 1, 9, 9)
    m.put(3, 5, "P")
    m.put(3, 6, "1")
    m.put(6, 3, "B")                      # near plate
    m.put(9, 5, "D")                      # the stile door
    m.link((6, 3), (9, 5))

    # Room B: the far side of the stile, with its own plate to let the first holder through.
    m.room(9, 1, 14, 9)
    m.put(9, 5, "D")
    m.put(12, 3, "B")
    m.link((12, 3), (9, 5))
    m.region("The Stile", "a plate holds its door only while someone stands on it", "plate", 2, 2, 13, 8)

    # The ratchet: a narrow lane in its own room, forest above and below. Two doors, a plate
    # in an alcove on each side of each door, so the pair leapfrogs through.
    m.room(14, 3, 23, 7)
    m.put(14, 5, ".")                      # in from the stile's far side
    m.fill(15, 4, 22, 4, "#")
    m.fill(15, 6, 22, 6, "#")
    m.put(17, 5, "D")
    m.put(16, 4, "B"); m.link((16, 4), (17, 5))   # before door 1
    m.put(18, 6, "B"); m.link((18, 6), (17, 5))   # after door 1
    m.put(20, 5, "D")
    m.put(19, 4, "B"); m.link((19, 4), (20, 5))   # before door 2
    m.put(21, 6, "B"); m.link((21, 6), (20, 5))   # after door 2
    m.put(23, 5, ".")                      # out to the summit
    m.region("The Ratchet", "one holds, one goes, then swap", "plate", 15, 4, 22, 6)

    # Summit.
    m.room(23, 2, 28, 8)
    m.put(23, 5, ".")
    m.put(26, 5, "G")
    m.region("Meadow Top", "both leopards to the summit", "goal", 24, 3, 27, 7)
    return m.write("foothills-2", "Stile Meadow", "foothills")


# ---------------------------------------------------------------- 1-3 Cairn Fields
def cairn_fields():
    m = Map(26, 18)
    # The field: a wide room with a pit cluster around its key.
    m.room(6, 5, 19, 16)
    m.put(12, 15, "E")                    # field entrance, also where fallers return
    m.put(12, 14, "P")
    m.put(13, 14, "1")
    for c, r in [(10, 9), (11, 8), (12, 8), (13, 8), (14, 9), (10, 10), (14, 10), (11, 11), (13, 11)]:
        m.put(c, r, "H")                  # visible pits around the field key
    m.put(12, 10, "K")                    # key two, reached through the gap at (12, 11)
    m.region("Cairn Field", "carry every key to the summit gate", "hub", 7, 6, 18, 15)

    # West nook: a lever door, key one.
    m.room(1, 7, 6, 13)
    m.put(6, 10, "D")
    m.put(7, 12, "L")                     # lever in the field, beside the nook door
    m.link((7, 12), (6, 10))
    m.put(3, 10, "K")
    m.region("West Nook", "a lever, a door", "lever", 2, 8, 5, 12)

    # East nook: hidden pits, a false key in the open, the real key tucked behind.
    m.room(19, 6, 24, 15)
    m.put(19, 13, ".")                    # open mouth into the field
    m.put(20, 13, "E")
    m.put(18, 12, "S")                    # sightstone in the field, at the nook's mouth
    # One safe path winds from the mouth to the real key; every other cell below row 13 is a
    # hidden pit. The false key sits where walking straight down from the entrance would go,
    # so chasing it crosses three hidden pits.
    safe = {(22, 12), (22, 11), (23, 11), (23, 10), (23, 9), (23, 8), (23, 7)}
    false_key = (20, 9)
    for r in range(7, 13):
        for c in range(20, 24):
            if (c, r) not in safe and (c, r) != false_key:
                m.put(c, r, "h")
    m.put(*false_key, "k")                # the false key, in plain sight
    m.put(23, 7, "K")                     # the real key, deep in the nook
    m.region("East Nook", "not all is shown  ·  Space beside the sightstone", "sight", 20, 7, 23, 14)

    # Summit gate in the field's north wall.
    m.put(12, 5, "F")
    m.room(9, 0, 16, 5)
    m.put(12, 5, "F")
    m.put(12, 2, "G")
    m.region("Cairn Summit", "the gate answers three keys", "goal", 10, 1, 15, 4)
    return m.write("foothills-3", "Cairn Fields", "foothills")


# ---------------------------------------------------------------- 1-F High Hollow, reworked
def high_hollow():
    path = os.path.join(OUT, "hollow.json")
    d = json.load(open(path, encoding="utf-8"))
    rows = [list(r) for r in d["rows"]]
    for r, line in enumerate(rows):
        for c, ch in enumerate(line):
            if ch == "2":
                line[c] = "."                 # two leopards now
    rows[20][8] = "E"                         # Lever Hollow entrance, where the corridor comes in
    rows[13][15] = "E"                        # Twin Stones entrance, just in from camp
    rows[20][23] = "E"                        # Veiled Scree entrance
    d["rows"] = ["".join(r) for r in rows]
    h = len(rows)
    out = {"id": "hollow", "name": "High Hollow", "biome": "foothills", "width": len(rows[0]), "height": h,
           "rows": d["rows"], "regions": d["regions"]}
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(to_json(out))
    return path


def trail():
    t = {"biomes": [
        {"id": "foothills", "name": "Foothills", "levels": ["foothills-1", "foothills-2", "foothills-3", "hollow"]},
    ]}
    path = os.path.join(OUT, "trail.json")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(t, indent=2) + "\n")
    return path


if __name__ == "__main__":
    for fn in (trailhead, stile_meadow, cairn_fields, high_hollow, trail):
        print(fn())
