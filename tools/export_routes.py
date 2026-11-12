"""Write each level's shortest winning route as a fixture for the Rust server's parity test.

    python tools/export_routes.py [level ids...]

With no ids, exports every level on the trail. Each fixture is server/tests/routes/<id>.json:
a list of [seat, action] pairs, action one of n s e w use, as found by levelcheck's solver.
The server test replays it and must reach the summit on exactly the last action.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import levelcheck  # noqa: E402

ROOT = os.path.join(os.path.dirname(__file__), "..")
LEVELS = os.path.join(ROOT, "Assets", "StreamingAssets", "levels")
OUT = os.path.join(ROOT, "server", "tests", "routes")


def main(ids):
    if not ids:
        trail = json.load(open(os.path.join(LEVELS, "trail.json"), encoding="utf-8"))
        ids = [l for b in trail["biomes"] for l in b["levels"]]
    os.makedirs(OUT, exist_ok=True)
    for lid in ids:
        d, w, h, errs, duo, states = levelcheck.analyse(os.path.join(LEVELS, lid + ".json"), max_states=levelcheck.MAX_STATES)
        if errs or duo is None:
            print(f"{lid}: skipped ({'; '.join(errs) or 'unsolved'})")
            continue
        _, _, actions = duo
        with open(os.path.join(OUT, lid + ".json"), "w", encoding="utf-8", newline="\n") as f:
            f.write("[\n" + ",\n".join(f'  [{s}, "{a}"]' for s, a in actions) + "\n]\n")
        print(f"{lid}: {len(actions)} actions ({states} states)")


if __name__ == "__main__":
    main(sys.argv[1:])
