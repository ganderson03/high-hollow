# High Hollow server

The authoritative server for High Hollow, in Rust. It speaks the wire protocol in the [root README](../README.md#wire-protocol), JSON Lines over TCP. It runs the rules in [docs/levels-design.md](../docs/levels-design.md), reading the same level and trail files as the client from `Assets/StreamingAssets/levels`.

Status: **v0.2.0.** Every system in the design doc is built: two seats with the disconnect pause, both failure grades, the vote reset, checkpoints, all six biomes' mechanics, and canonical hashing. The client's art for the new pieces is not built yet; see [Client side](#client-side).

## Running it

Requires Rust 1.88 or later (edition 2024, let-chains).

```bash
cd server
cargo run --release
```

It listens on `127.0.0.1:7777` and starts the trail at `foothills-1`. Options:

| Option | Default | |
| --- | --- | --- |
| `--bind <addr>` | `127.0.0.1` | `0.0.0.0` to accept players from other machines |
| `--port <port>` | `7777` | |
| `--levels <dir>` | the client's `Assets/StreamingAssets/levels` | found from the working directory or the crate |
| `--level <id>` | `foothills-1` | where the trail starts, and where it returns when the server empties |

To play against it, untick `useMockFeed` on the scene's `Bootstrap` object and press Play. A second Unity player (a build) pointed at the same host and port takes the other leopard.

## Sessions

- **Seats.** The first client to say `hello` holds both leopards and switches with `control`. A second client takes the leopard the first is not steering. A third connection is closed with no `welcome`.
- **Clock.** 60 Hz simulation, a snapshot every third tick (20 Hz). The world holds still while nobody is connected, and an empty server goes back to the start of the trail.
- **Disconnects.** When one of two players drops, the world freezes and the other gets `round` `paused` (`reason` `partner_left`, `seconds` 60), repeated each second. Whoever connects next takes the empty seat and play resumes. After the minute the stayer gets `round` `choose` and answers `{"t":"choice","pick":"both"}` (take both leopards) or `"quit"` (closes their connection). There is no identity beyond the name, so "coming back" means any second player joining, during the pause or later.
- **Vote reset.** `{"t":"vote","reset":true}` proposes a restart, or agrees to the partner's proposal. The server answers `{"t":"vote","by":0,"ticks_left":600,"state":"open"}`, then `passed` or `lapsed` (after 600 ticks, on `reset: false`, or when a player joins or leaves). A player holding both seats passes at once. A pass restarts the level, or restores the lit checkpoint, and sends `round` `playing` with `reason` `vote`.
- **Inputs** apply to the seat the sender is steering. A held direction steps one cell every 9 ticks, and an interact press is kept until the next tick uses it. An input's `tick` is checked: one stamped before the current round began (a key still held from before a restart, a pause or a new level) or more than a second ahead of the server is dropped.
- **Chat** is relayed to both players, stamped with the sender's real `client_id`, trimmed, stripped of control characters and capped at 200 characters. Server chat comes from `255`.
- **Overlays** are per client, for the seat it is steering, and resent when perception can change.
- **Progression.** On a win: `round` `won` and a 6-second hold, then `welcome`, `level`, `overlay`, `round` `playing` and `seats` for the next level on the trail. Each level starts with every client steering the first seat it holds.
- **Bad input.** A line that is not valid JSON or not a known message is logged and ignored. A line over 4 KiB, or anything other than `hello` as the first message, closes the connection. A client that falls more than 256 lines behind is dropped.

## Rules

A tick runs each seat in seat order (interact, then step or slide), then each mechanic's `on_tick` in the order the level lists them. Then it handles a major failure if there is one, then lights the checkpoint if both leopards are on or beside it. Timing is counted in ticks only.

| Mechanic | Switched on by | Rules |
| --- | --- | --- |
| core | always | doors, levers, plates, keys and the summit gate, pits (minor), chasms `X` (major), the sightstone with hidden pits `h`, false keys `k` and pits dressed as ledges `-`, the checkpoint `C`, the summit |
| `ledges` | Talus | `=` cracks when stepped on and collapses when the last leopard steps off; walking into a collapsed ledge is minor |
| `ice` | Frozen Tarns | `~` slides a leopard one cell every 4 ticks to the first non-ice cell, stopping at walls and the partner; input is ignored meanwhile; open water `%` is major |
| `boulders` | Rockfall | `O` pushes one cell, slides on ice, fills pits, is lost in chasms, holds plates and dams channels. Slabs `[` `]` (along a row) and `v` `^` (`^` north of `v`) move across their width only, when both leopards push their two halves the same way within 90 ticks (240 when one player holds both). Both pushers step forward with it |
| `water` | Creeklands | flow from sources `W` through channels `w`, recomputed every tick. Sluices `V` send water out one exit, swung by interact; flowing water cannot be entered; a leopard in a channel when water arrives is swept away (major). Wheels `M` turn when water reaches them and drive their linked doors or the summit gate |
| `sight` | Whiteout | crevasses `a` / `b` are seen only by leopard 0 / 1; `y` / `z` are false crevasses shown only to leopard 0 / 1 |

- **Failures.** A minor failure sends one leopard to its room's entrance `E` and drops its keys. A major failure restarts the level, or restores the lit checkpoint, and sends `round` `playing` with `reason` `fell`.
- **Checkpoints.** A save is a copy of the whole world plus every mechanic's state. The level start is the save made at load, and a restart restores a save.
- **Glyphs need their mechanic.** A level using a glyph without its mechanic, or listing an unknown mechanic, fails to load.
- **Ice under pieces.** A cell holding an entity glyph is floor. A level can mark such cells as ice with `"ice": [[col, row], ...]` (a key out on the lake).
- **Sluice exits.** A sluice's inlet is the side water first reaches it from. It starts open on the first other side (north, east, south, west) that leads into the creek, and interact cycles through those sides.
- **Wheels** link like levers: `{"from": [wheel], "to": [door]}`. The summit gate `F` opens when every key is carried *and*, if it has links, one of them is active.

## Wire additions

| Change | Shape |
| --- | --- |
| Kinds | Ledge 8 (`state` 0 intact, 1 cracked, 2 collapsed), Boulder 9 (`state` = shape 0 boulder / 1 slab along x / 2 slab along y, +4 straining, +8 gone), Sluice 10 (`state` = open exit, 0 n, 1 e, 2 s, 3 w), Wheel 11 (1 turning), Checkpoint 12 (1 lit). Hazard `state` 1 = filled |
| `snapshot` | `water`: flowing cells as `[x, y]`, top row first, omitted when dry; `hash`: see below |
| `round` | `state` `paused` (with `seconds`) and `choose`; `reason` `fell`, `vote` or `partner_left` |
| `vote` | client `{"t":"vote","reset":true}`; server `{"t":"vote","by":0,"ticks_left":600,"state":"open"}`, then `passed` or `lapsed` |
| `choice` | client `{"t":"choice","pick":"both"}` or `"quit"` |

**Canonical hash.** Each snapshot carries `hash`: XXH3-64 (seed 0) of a fixed binary encoding, as 16 lowercase hex digits. The encoding, all little-endian, is the bytes `HHC1`, then the tick (u32), the entity count (u32), each entity as x i32, y i32, kind u8, state u8, carried_by i8, then the water count (u32) and each water cell as x i32, y i32. Overlays are never hashed. See `src/canon.rs`.

## Layout

| Module | Responsibility | Mirrors in the client |
| --- | --- | --- |
| `protocol` | wire messages, encode and decode | `Crawler.Protocol.WireCodec`, `Messages` |
| `canon` | canonical encoding and hash | not yet |
| `level` | level and trail files | `LevelCodec`, `TrailCodec` |
| `world` | entities, seats, keys, rocks, water, failures | `Crawler.Mock.MockWorld` |
| `mechanic` | the per-mechanic trait, and which a level switches on | `IMechanic` |
| `mechanics/*` | `core`, `ledges`, `ice`, `boulders`, `water`, `sight` | `CoreMechanic` (core only) |
| `sim` | one level, ticked by the caller; saves, restarts, slides | `MockSim` |
| `game` | clients, seats, clock, trail, pause, vote; no sockets | `MockFeed` |
| `net` | accept, reader and writer threads, 60 Hz loop | `Crawler.Net.ServerConnection` (other end) |

Everything up to `game` is plain state with no I/O, so it is tested directly. Entity ids follow the mock exactly (the two seats, then every entity glyph in row-scan order from the top row), because overlays name entities by id.

## Tests

```bash
cargo test
```

- `protocol`: decodes what the client sends, encodes every server message byte for byte, and checks the canonical encoding.
- `rules`: each core rule on a small map, and a **parity replay** of every trail level. `tests/routes/<id>.json` holds the shortest winning route found by `tools/levelcheck.py` (the Python mirror of the mock's rules). The server must win on exactly its last action. Regenerate the fixtures after changing a level with `python tools/export_routes.py`.
- `mechanics`: ledges, ice, open water, boulders, slabs and the push window, water, sluices, wheels, dams, split sight, chasms and checkpoints.
- `session`: seats, chat, control, snapshot rate, the pause, the choice, coming back, the vote, stale inputs, hashes, and walking the whole trail.
- `tcp`: the real server over sockets.

## Client side

The client's protocol layer (`Crawler.Protocol`, `Crawler.Net`, `Crawler.Mock`) decodes everything above. The Unity layer has two additions: R proposes or agrees to a restart, and while the server asks for a choice, B takes both leopards and Q leaves the trail. Still to build in Unity: looks for kinds 8 to 12 and for ice, open water and channels (they fall back to a plain marker today), drawing `water`, the pause and vote cards, and checking `hash`. The mock still runs the core rules only.
