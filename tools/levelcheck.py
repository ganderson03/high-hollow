"""Validate and solve a crawler level with the mock's core rules.

Checks the file's structure, then breadth-first searches the joint state of both leopards
(positions, levers, carried keys) for the shortest win. Also reports whether one leopard
alone can win, which tells a designer whether a co-op beat really needs the partner.
Mirrors Crawler.Mock.CoreMechanic; if the two disagree, the mock is the reference.
"""
import json
import sys
from collections import deque

SOLID = set("#T")
DIRS = [(1, 0), (-1, 0), (0, 1), (0, -1)]


def load(path):
    d = json.load(open(path, encoding="utf-8"))
    rows = d["rows"]
    h = d.get("height", d.get("grid", len(rows)))
    w = d.get("width", d.get("grid", len(rows[0])))
    errs = []
    if len(rows) != h:
        errs.append(f"{len(rows)} rows, expected {h}")
    for i, r in enumerate(rows):
        if len(r) != w:
            errs.append(f"row {i} has {len(r)} chars, expected {w}")
    tiles = {}
    for r, line in enumerate(rows):
        for x, c in enumerate(line):
            tiles[(x, h - 1 - r)] = c
    regions = []
    for reg in d.get("regions", []):
        x, y, rw, rh = reg["rect"]
        regions.append((reg["name"], reg.get("rule", ""), x, y, rw, rh))
    links = []
    for l in d.get("links", []):
        fc, fr = l["from"]
        tc, tr = l["to"]
        links.append(((fc, h - 1 - fr), (tc, h - 1 - tr)))
    return d, w, h, tiles, regions, links, errs


def region_at(regions, x, y):
    for i, (_, _, rx, ry, rw, rh) in enumerate(regions):
        if rx <= x < rx + rw and ry <= y < ry + rh:
            return i
    return -1


def analyse(path, max_states=3_000_000):
    d, w, h, tiles, regions, links, errs = load(path)
    at = lambda x, y: tiles.get((x, y), "#")

    def find(g):
        for y in range(h - 1, -1, -1):
            for x in range(w):
                if at(x, y) == g:
                    return (x, y)
        return None

    p0 = find("P")
    p1 = find("1") or (p0[0] + 1, p0[1])
    if find("2"):
        errs.append("glyph '2' found: the game is two-player now")

    ents = []  # (kind, glyph, x, y, region)
    for y in range(h - 1, -1, -1):
        for x in range(w):
            c = at(x, y)
            kind = {"D": "door", "F": "door", "L": "lever", "B": "plate", "K": "key",
                    "H": "pit", "h": "pit", "G": "goal", "S": "sight"}.get(c)
            if kind:
                ents.append((kind, c, x, y, region_at(regions, x, y)))
    by_cell = {(e[2], e[3]): i for i, e in enumerate(ents)}
    levers = [i for i, e in enumerate(ents) if e[0] == "lever"]
    keys = [i for i, e in enumerate(ents) if e[0] == "key"]
    lever_bit = {i: b for b, i in enumerate(levers)}
    key_idx = {i: b for b, i in enumerate(keys)}

    door_links = {}
    for (fc, tc) in links:
        if tc not in by_cell or ents[by_cell[tc]][0] != "door":
            errs.append(f"link to {tc} is not a door")
            continue
        if fc not in by_cell or ents[by_cell[fc]][0] not in ("lever", "plate"):
            errs.append(f"link from {fc} is not a lever or plate")
            continue
        door_links.setdefault(by_cell[tc], []).append(by_cell[fc])
    linked_sources = {s for v in door_links.values() for s in v}

    # every room with a pit should have an entrance
    for i, e in enumerate(ents):
        if e[0] == "pit":
            reg = e[4]
            if reg < 0:
                errs.append(f"pit at {e[2:4]} is outside every region")
            else:
                _, _, rx, ry, rw, rh = regions[reg]
                if not any(at(x, y) == "E" for x in range(rx, rx + rw) for y in range(ry, ry + rh)):
                    errs.append(f"region '{regions[reg][0]}' has a pit but no E entrance")

    entrance = {}
    for i, (_, _, rx, ry, rw, rh) in enumerate(regions):
        for y in range(ry, ry + rh):
            for x in range(rx, rx + rw):
                if at(x, y) == "E":
                    entrance[i] = (x, y)

    def source_active(src, pos, lv):
        e = ents[src]
        if e[0] == "lever":
            return bool(lv >> lever_bit[src] & 1)
        return (e[2], e[3]) in pos

    def door_open(di, pos, lv, carried):
        e = ents[di]
        if e[1] == "F":
            return len(keys) > 0 and all(c >= 0 for c in carried)
        if di in door_links:
            return any(source_active(s, pos, lv) for s in door_links[di])
        plate_rule = e[4] >= 0 and regions[e[4]][1] == "plate"
        for j, o in enumerate(ents):
            if o[4] != e[4] or j in linked_sources:
                continue
            if plate_rule and o[0] == "plate" and source_active(j, pos, lv):
                return True
            if not plate_rule and o[0] == "lever" and source_active(j, pos, lv):
                return True
        return False

    def walkable(x, y, pos, other, lv, carried):
        if not (0 <= x < w and 0 <= y < h) or at(x, y) in SOLID:
            return False
        if (x, y) == other:
            return False
        i = by_cell.get((x, y))
        if i is not None and ents[i][0] == "door" and not door_open(i, pos, lv, carried):
            return False
        return True

    goals = [e for e in ents if e[0] == "goal"]

    def won(npos):
        # both leopards: one on the marker, the other in the summit's room (or within 2 cells)
        for g in goals:
            for s in (0, 1):
                if npos[s] != (g[2], g[3]):
                    continue
                o = npos[1 - s]
                if g[4] >= 0 and region_at(regions, *o) == g[4]:
                    return True
                if g[4] < 0 and abs(o[0] - g[2]) <= 2 and abs(o[1] - g[3]) <= 2:
                    return True
        return False

    def solve(seats):
        start = (p0, p1, 0, tuple([-1] * len(keys)))
        prev = {start: None}
        q = deque([start])
        while q:
            st = q.popleft()
            pa, pb, lv, carried = st
            pos = (pa, pb)
            for s in seats:
                me, other = pos[s], pos[1 - s]
                # interact: first adjacent lever in entity order toggles
                for li in levers:
                    e = ents[li]
                    if abs(e[2] - me[0]) <= 1 and abs(e[3] - me[1]) <= 1:
                        nst = (pa, pb, lv ^ (1 << lever_bit[li]), carried)
                        if nst not in prev:
                            prev[nst] = (st, s, "use")
                            q.append(nst)
                        break
                for dx, dy in DIRS:
                    nx, ny = me[0] + dx, me[1] + dy
                    if not walkable(nx, ny, pos, other, lv, carried):
                        continue
                    npos = [pa, pb]
                    npos[s] = (nx, ny)
                    nc = list(carried)
                    i = by_cell.get((nx, ny))
                    if i is not None:
                        k = ents[i][0]
                        if k == "pit":
                            npos[s] = entrance.get(ents[i][4], (p0, p1)[s])
                            nc = [-1 if c == s else c for c in nc]
                        if k == "key" and nc[key_idx[i]] < 0:
                            nc[key_idx[i]] = s
                    nst = (npos[0], npos[1], lv, tuple(nc))
                    step = {(1, 0): "e", (-1, 0): "w", (0, 1): "n", (0, -1): "s"}[(dx, dy)]
                    if won(npos):
                        actions = [(s, step)]
                        cur = st
                        while prev[cur] is not None:
                            cur, who, act = prev[cur]
                            actions.append((who, act))
                        actions.reverse()
                        switches = sum(1 for a, b in zip(actions, actions[1:]) if a[0] != b[0])
                        return (len(actions), switches, actions), len(prev)
                    if nst not in prev:
                        prev[nst] = (st, s, step)
                        q.append(nst)
            if len(prev) > max_states:
                return None, len(prev)
        return None, len(prev)

    duo, duo_states = solve((0, 1))
    return d, w, h, errs, duo, duo_states


if __name__ == "__main__":
    for path in sys.argv[1:]:
        d, w, h, errs, duo, states = analyse(path)
        print(f"== {d['id']} '{d['name']}' {w}x{h}")
        for e in dict.fromkeys(errs):
            print("  ERROR", e)
        if duo is None:
            print(f"  UNSOLVABLE (searched {states} states)")
        else:
            actions, switches, _ = duo
            print(f"  solvable: shortest {actions} actions (~{actions * 0.15:.0f}s of pure input), {states} states searched")
            print(f"  control passes between leopards {switches} times on that route")
