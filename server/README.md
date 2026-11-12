# High Hollow server

The authoritative server for High Hollow, in Rust. It speaks the wire protocol in the [root README](../README.md#wire-protocol), JSON Lines over TCP, and runs the same core rules as the client's mock (`Assets/Crawler/Mock`), reading the same level and trail files from `Assets/StreamingAssets/levels`.

Status: **v0.1.0, first cut.** Two seats, the core mechanic, the trail. See [What is not built yet](#what-is-not-built-yet).

## Running it

Requires Rust 1.85 or later (edition 2024).

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

## How it behaves

- **Seats.** The first client to say `hello` holds both leopards and switches with `control`. A second client takes seat 1, and the first keeps seat 0. A third connection is closed with no `welcome`. When one of two leaves, the other is handed both leopards straight away.
- **Clock.** 60 Hz simulation, a snapshot every third tick (20 Hz). The world holds still while nobody is connected, and an empty server goes back to the start of the trail.
- **Inputs** apply to the seat the sender is steering. A held direction steps one cell every 9 ticks, and an interact press is kept until the next tick uses it. Within a tick, seats act in seat order.
- **Chat** is relayed to both players, stamped with the sender's real `client_id` (whatever `from` the client wrote), trimmed, stripped of control characters and capped at 200 characters. Server chat comes from `255`.
- **Overlays** are per client, for the seat it is steering, and resent when perception can change (a sightstone, a switch, a new level).
- **Progression.** On a win: `round` `won` and a 6-second hold, then `welcome`, `level`, `overlay`, `round` `playing` and `seats` for the next level on the trail. Each level starts with every client steering the first seat it holds. After the last level: "the trail is complete".
- **Bad input.** A line that is not valid JSON or not a known message is logged and ignored. A line over 4 KiB, or anything other than `hello` as the first message, closes the connection. A client that falls more than 256 lines behind is dropped.

## Layout

| Module | Responsibility | Mirrors in the client |
| --- | --- | --- |
| `protocol` | wire messages, encode and decode | `Crawler.Protocol.WireCodec`, `Messages` |
| `level` | level and trail files | `LevelCodec`, `TrailCodec` |
| `world` | entities, seats, carried keys, minor failure | `Crawler.Mock.MockWorld` |
| `mechanic` | the per-mechanic trait, and which a level switches on | `IMechanic` |
| `core` | doors, levers, plates, keys, pits, sightstone, summit | `CoreMechanic` |
| `sim` | one level, ticked by the caller | `MockSim` |
| `game` | clients, seats, clock, trail; no sockets | `MockFeed` |
| `net` | accept, reader and writer threads, 60 Hz loop | `Crawler.Net.ServerConnection` (other end) |

Everything up to `game` is plain state with no I/O, so it is tested directly. Entity ids follow the mock exactly (the two seats, then every entity glyph in row-scan order from the top row), because overlays name entities by id.

## Tests

```bash
cargo test
```

- `protocol`: decodes what the client sends and encodes the README's examples byte for byte.
- `rules`: each core rule on a small purpose-built map, and a **parity replay** of every trail level. `tests/routes/<id>.json` holds the shortest winning route found by `tools/levelcheck.py` (the Python mirror of the mock's rules). The server must win on exactly its last action. Regenerate the fixtures after changing a level with `python tools/export_routes.py`.
- `session`: seats, join and leave, chat, control, snapshot rate, and walking the whole trail through the session layer.
- `tcp`: the real server over sockets: welcome, snapshots, movement, two players, a refused third, junk and overlong lines.

## What is not built yet

From [docs/levels-design.md](../docs/levels-design.md), not yet on the server (or in the mock):

- The disconnect pause: a 60-second countdown, then the take-both-leopards or leave choice, and seat hand-back on reconnect. Today a dropped partner's leopard goes to the remaining player at once, and nobody can rejoin a seat as themselves.
- Major failures, the vote reset and checkpoints.
- Every non-core mechanic (Talus onward). A level listing an unknown mechanic is refused at load.
- Canonical encoding and the xxh3 hash for parity checks.
- Input `tick` is accepted but not used. Inputs apply on arrival.
