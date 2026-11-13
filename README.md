# High Hollow

A networked two-player co-op puzzle crawler, drawn like a 1970s national park poster. A pair of snow leopards works its way up a trail of levels, holding doors open for each other, fetching keys, and reaching each summit together.

This repository holds the **Unity client** and, under [`server/`](server/README.md), the authoritative **Rust server**. The server runs every rule in the design doc for one or two players: the Foothills trail, the five biome mechanics still waiting for levels, both failure grades, the vote reset, checkpoints and the disconnect pause. A mock feed built into the client runs the same rules, so the game is also fully playable offline.

Current version: **v0.3.0** (see [Versioning](#versioning)).

## Requirements

- Unity **6000.6.0f1** (Unity 6) with the Universal Render Pipeline
- Input System package (the project uses the new Input System only)
- Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`, installed through `Packages/manifest.json`)

## Getting started

1. Open the project folder in Unity Hub with 6000.6.0f1.
2. Open `Assets/Scenes/Crawler.unity`.
3. Press Play, then **Enter** on the title screen to set out.

By default the client runs against the mock feed and needs no server. To play against the real server, run `cargo run --release` in `server/` (see [server/README.md](server/README.md)), untick `useMockFeed` on `Bootstrap`, and press Play.

### Two players and the server, in one command

```powershell
.\play.ps1
```

Or double-click `play.cmd`. The script builds the Rust server, and also builds the Unity player when there is none or the client has changed since the last build. It then starts the server in its own window and opens two game windows side by side, one per leopard, that join on their own. Closing both game windows stops the server. The Unity editor must be closed while the script builds the player.

| Option | |
| --- | --- |
| `-Players 1` | one window holding both leopards |
| `-Level hollow` | start the trail on another level |
| `-Build` / `-NoBuild` | force a Unity rebuild, or never build |
| `-Port 7778` | use another port |

Logs go to `Builds/logs/`. A built player also takes these on its command line: `-server host[:port]`, `-mock`, `-name NAME`, `-level ID` (mock only), `-autostart` (skip the title card) and `-window-x X -window-y Y`.

### Bootstrap settings

The `Bootstrap` object in the scene holds the switches you are likely to touch:

| Field | Default | What it does |
| --- | --- | --- |
| `useMockFeed` | on | Play against the built-in mock instead of a real server |
| `host`, `port` | `127.0.0.1`, `7777` | Server address when the mock is off |
| `playerName` | `snep` | Name sent in `hello` |
| `mockLevelId` | `foothills-1` | Level the mock starts on; the trail continues from there |
| `artStyle` | Placeholder | `Poster` for the finished look, `Placeholder` for fast testing |

The committed scene uses **Placeholder** art. Switch `artStyle` to **Poster** before a demo or a build.

## Controls

| Key | Action |
| --- | --- |
| WASD / arrow keys | Move (the last direction pressed wins) |
| Space | Interact with an adjacent lever or sightstone |
| Tab | Switch to your other leopard, when you hold both |
| Enter | Open chat, send a message |
| R | Propose restarting the level, or agree to your partner's proposal |
| B / Q | When your partner has not come back: take both leopards / leave the trail |

## How the trail plays

The game is for **two leopards**. With a partner, each player steers one. Solo, or against the mock, one player holds both and presses **Tab** to switch; the leopard left behind stands exactly where it was, which is often the point: an idle leopard holds a plate open for the other.

The trail is a run of levels grouped into **biomes**, listed in `levels/trail.json`. Reaching a summit loads the next level. The first biome, **Foothills**, is built:

| Level | Teaches |
| --- | --- |
| Trailhead | levers and doors; each lane's lever opens the other lane |
| Stile Meadow | plates hold a door only while someone stands on them, so the pair takes turns |
| Cairn Fields | keys and the summit gate, pits, hidden pits, the sightstone and a false key |
| High Hollow | the finale: a hub with four rooms using all of the above |

Rules that apply everywhere:

- **Both leopards must reach the summit**: one on the marker, the other in the summit's room.
- **Falling into a pit** sends that leopard back to the entrance of the room it fell in, and every key it carries goes back where it was found. The partner and the room are untouched.
- **The summit gate** opens once the pair carries every key between them.

The rules live only in the mock today (`Assets/Crawler/Mock`); the full design, including the five biomes still to come, is in [docs/levels-design.md](docs/levels-design.md) and [docs/levels-plan.md](docs/levels-plan.md).

## Project layout

The client code lives in `Assets/Crawler`, split into assemblies so the layers cannot leak into each other:

| Assembly | Folder | Responsibility | Depends on |
| --- | --- | --- | --- |
| `Crawler.Protocol` | `Protocol/` | Message types, JSON codec, level format | Newtonsoft only (no UnityEngine) |
| `Crawler.Net` | `Net/` | TCP link: background reader thread, main-thread drain | Protocol (no UnityEngine) |
| `Crawler.Mock` | `Mock/` | Stand-in server with the demo rules | Protocol (no UnityEngine) |
| `Crawler.Render` | `Render/` | World view, entity views, terrain, procedural art, camera | Protocol |
| `Crawler.Input` | `Input/` | Samples input and sends it at the replication rate | Protocol, Input System |
| `Crawler.UI` | `UI/` | Chat, status badge, field guide, title and win posters, room banner | Render, uGUI, Input System |
| `Crawler.App` | `App/` | Bootstrap: picks the link and routes messages to the views | everything |
| `Crawler.EditorTools` | `EditorTools/` | Editor-only scene builder | Protocol, Render |

Other folders:

- `Assets/StreamingAssets/levels/`: level files, loaded at runtime.
- `Assets/Crawler/Generated/`: materials, textures and meshes written by the scene builder. Rebuilt on every run; do not edit by hand.
- `Assets/Crawler/Render/Resources/`: material templates that keep URP Lit in player builds.

### Principles

- **The client never simulates.** It renders state and captures input. There is no prediction, and no game rule lives in the client (the mock is the one deliberate exception, and it is isolated in its own assembly).
- **Snapshots replace, they do not patch.** Each snapshot replaces the whole replica. Each view's root snaps to its cell; any easing (gliding, facing, door sinks) happens on a child transform and is never read back.
- **The overlay is perception only.** Per-client hidden entities, substituted looks and phantoms are applied at render time and never written into the entity array, never into an input, and never into anything that will be hashed.

## Wire protocol

JSON Lines over TCP: one JSON object per line, discriminated by `t`.

**Client to server**

```json
{"t":"hello","name":"snep"}
{"t":"input","tick":412,"move":"n","interact":false}
{"t":"chat","from":0,"text":"on my way"}
```

`move` is one of `n`, `s`, `e`, `w`, `none`. Inputs are sent at the replication rate (20 Hz) and logged to the console. They move whichever leopard the client is steering.

```json
{"t":"control","seat":1}
{"t":"vote","reset":true}
{"t":"choice","pick":"both"}
```

`vote` proposes restarting the level or agrees to the partner's proposal (`false` withdraws or declines). `choice` answers `round` `choose`, after a partner has been gone a minute, with `both` or `quit`.

A client holding both seats asks to steer the other leopard; the server answers with `seats`.

**Server to client**

```json
{"t":"welcome","client_id":0,"width":24,"height":9,"tick_hz":60,"replication_hz":20}
{"t":"seats","yours":[0,1],"active":0}
{"t":"snapshot","tick":412,"entities":[[15,9,0,0,-1]]}
{"t":"overlay","tick":412,"hidden":[7],"substituted":[[3,4]],"phantoms":[[26,21,4]]}
{"t":"chat","from":255,"text":"welcome to High Hollow"}
{"t":"level","id":"hollow"}
{"t":"round","tick":412,"state":"playing"}
```

An entity is `[x, y, kind, state, carried_by]`, where `carried_by` is `-1` when nobody carries it, else the seat carrying it. Its index in the array is its id; ids 0 and 1 are the two seats' leopards.

`seats` says which leopards this client controls and which it is steering; it is sent at the start and whenever control changes. Levels are rectangular, so `welcome` carries `width` and `height` (older servers sending a square `grid` are still accepted).

| Kind | Value | `state` |
| --- | --- | --- |
| Player | 0 | unused |
| Door | 1 | 1 = open |
| Lever | 2 | 1 = on |
| Plate | 3 | 1 = pressed |
| Key | 4 | unused |
| Hazard | 5 | unused |
| Goal | 6 | 1 = won |
| Sightstone | 7 | 1 = used |
| Ledge | 8 | 0 intact, 1 cracked, 2 collapsed |
| Boulder | 9 | shape (0 boulder, 1 slab along x, 2 slab along y), +4 straining, +8 gone |
| Sluice | 10 | open exit: 0 n, 1 e, 2 s, 3 w |
| Wheel | 11 | 1 = turning |
| Checkpoint | 12 | 1 = lit |

A Hazard's `state` is 1 once a boulder has filled it.

The server also sends `round` with `state` `paused` (and `seconds`) or `choose` while a partner is gone, and a `reason` of `fell`, `vote` or `partner_left` on a round that is not a fresh level; `{"t":"vote","by":0,"ticks_left":600,"state":"open"}`, then `passed` or `lapsed`; and on `snapshot`, `water` (flowing cells as `[x, y]`, omitted when dry) and `hash`. See [server/README.md](server/README.md#wire-additions).

Overlay entries are `substituted: [id, look]` and `phantoms: [x, y, look]`, where a look is an appearance key (the kind values above).

**Settled by the Rust server.** These began as client-side conventions; the server now implements them:

- Seat N is the Nth Player entity (entity id N). One client alone holds both seats; two hold one each.
- `level` and `round` are part of the protocol, sent as in the mock.
- Chat from sender `255` is the server. Player chat is stamped with the sender's real `client_id`.
- The overlay's look key is the kind value (it was called `sprite` in early notes, from a 2D plan).

Each snapshot's `hash` is XXH3-64 of a fixed binary encoding of its tick, entities and water ([server/README.md](server/README.md#wire-additions)). The client decodes it but does not check it yet.

## Levels

Levels are JSON in `Assets/StreamingAssets/levels/{id}.json`: a name, a biome, a rectangular ASCII map (rows listed north to south), named regions with a rule, and optional links. Level ids are whitelisted to `[a-z0-9_-]{1,32}` because a server may send them. The order of play is `levels/trail.json`. Both files are part of the contract with the server.

| Glyph | Meaning | Glyph | Meaning |
| --- | --- | --- | --- |
| `.` | floor | `L` | lever |
| `#` | wall | `B` | plate |
| `T` | tree | `K` | key |
| `P` `1` | the two leopards' spawns | `k` | phantom key (perception only) |
| `D` | door | `H` | pit |
| `F` | summit gate | `h` | hidden pit |
| `G` | summit | `S` | sightstone |
| `E` | room entrance, where a fallen leopard returns | `X` | chasm (major: the level restarts) |
| `C` | checkpoint (finales) | `-` | pit that looks like a ledge until the sightstone |

Biome glyphs, each needing its mechanic in the level's `"mechanics"` list: `=` ledge (`ledges`); `~` ice and `%` open water (`ice`); `O` boulder, `[` `]` and `v` `^` slabs (`boulders`); `w` channel, `W` source, `V` sluice, `M` mill wheel (`water`); `a` / `b` crevasses seen only by leopard 0 / 1, and `y` / `z` false crevasses shown only to leopard 0 / 1 (`sight`). A level may list `"ice": [[col, row], ...]` for ice under a piece. The rules are in [server/README.md](server/README.md#rules).

A door opens by its **links** if it has any: `"links": [{ "from": [col, row], "to": [col, row] }]`, written as column and row exactly as they read in `rows`, from a lever or plate to a door. A door with no links falls back to its region's rule: its region's plate in a `plate` region, its region's lever otherwise. The summit gate `F` opens when every key is carried.

### Tools

- `python tools/levelcheck.py <level.json>...` validates a level and **solves** it with the same rules as the mock: it reports the shortest winning route and how often control passes between the two leopards on it. Every shipped level must solve. The search stops at 10M states (High Hollow needs about 5M) and says so rather than calling the level unsolvable.
- `python tools/export_routes.py [ids...]` writes those shortest routes to `server/tests/routes/`, where the server's tests replay them. Rerun it after changing a level.
- `python tools/build_foothills.py` regenerates the Foothills level files from code, where rooms, pieces and links are placed by coordinate rather than by hand-editing the map.

## Editor tools

The **Crawler** menu in the editor:

- **Build Environment in Scene** (Ctrl+Shift+E): builds the level's terrain and a prop for every entity into the open scene, so it can be inspected without entering Play. Generated assets are written to `Assets/Crawler/Generated` so a saved scene keeps them. At runtime the static copy is cleared and rebuilt from the live feed, so it never duplicates.
- **Clear Environment from Scene**: removes what the builder added.
- **Art Style > Poster / Placeholder**: switches what the builder produces and rebuilds.
- **Next Level in Scene** (Ctrl+Shift+N): steps the builder through the trail's levels.

## Building

Build the standalone Windows player through **File > Build Profiles**, with `Assets/Scenes/Crawler.unity` as the only scene. Output under `Builds/` is gitignored. It also builds headless:

```bash
Unity.exe -batchmode -quit -projectPath "<project>" -buildTarget Win64 -buildWindows64Player "Builds/HighHollow/HighHollow.exe"
```

## Things that will bite you

- **Domain reload is off** (Enter Play Mode Options). Static fields survive between play sessions while the Unity objects they point at are destroyed, and a destroyed material renders magenta. Any static cache of Unity objects must reset in a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` method, and cache hits must use Unity's own null check, never `??` or `?.`. Test changes with two back-to-back play sessions.
- **Play mode only advances while the editor has focus**, so scripted checks run from outside the editor can stall at frame 2.
- **The floor is one unbroken quad.** Nothing below y=0 is visible, and a face exactly on y=0 z-fights with it. Procedural bases sit slightly below zero for that reason.

## Versioning

The project follows [Semantic Versioning](https://semver.org). While it is below 1.0, new features bump the minor version, and fixes and small additions (documentation, tuning, content tweaks) bump the patch. **Every change that is pushed gets a version**, so the tag list doubles as a changelog. Releases are annotated git tags (`v0.1.0`, `v0.3.0`), and the player's version in Player Settings matches the latest tag. Commit messages follow [Conventional Commits](https://www.conventionalcommits.org) (`feat:`, `fix:` and so on), with Added / Changed / Fixed sections in the body.

| Version | Summary |
| --- | --- |
| v0.3.0 | Two leopards with Tab switching, the Foothills biome (four levels), trail progression, rectangular levels with a scenery apron, room entrances, door links, the level solver |
| v0.2.1 | README; level design doc and the full 24-level plan |
| v0.2.0 | Procedural flat-shaded art, placeholder art style, editor scene builder, pits drop carried keys |
| v0.1.0 | First playable demo: hub and rooms, title and win posters, mock feed |
