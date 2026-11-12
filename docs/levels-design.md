# Levels and mechanics: design

Status: **decisions folded in; partly built**. As of v0.3.0 the client and mock have two seats with switching, minor failures with room entrances, door links, rectangular levels with an apron, trail progression, and the core module split. As of server v0.2.0 the Rust server also has major failures, the vote reset, checkpoints, disconnect handling and every non-core mechanic (see [server/README.md](../server/README.md)); the mock still has the core only, and the client still needs looks for the new pieces. It covers the level trail, one bespoke mechanic per level, the framework they need, and every protocol change they imply, so the Rust server can be planned against the same document.

## Decisions

| Question | Decision |
| --- | --- |
| Structure | **Biomes.** Six biomes of **three levels plus a finale** each (possibly up to five later). A level takes 3 to 5 minutes at a standard pace; each finale runs longer. The level-by-level plan is in [levels-plan.md](levels-plan.md). |
| Finale checkpoints | **Yes, one per finale.** A major failure after it restores the checkpoint, not the level start. See [Checkpoints](#checkpoints). |
| Player count | **Exactly two leopards.** Every level is designed for a pair. |
| Winning | **Both leopards reach the summit**: one on the marker, the other in the summit's room. Otherwise a plate-holder could stay behind while the partner wins alone. |
| Failure | **Two grades.** A minor failure (a pit, a collapsed ledge) resets only the leopard who fell; a major failure (chasm, open water, torrent) restarts the whole level. See [Failure](#failure). |
| Soft-locks | **A vote reset.** Either player proposes restarting the level; it happens only if the other agrees. See [Vote reset](#vote-reset). |
| Two-player pushes | **A timing window, not a same-tick read.** Each push counts for 1.5 s; length to be tuned in playtest. See [Heavy slabs](#heavy-slabs). |
| Disconnects and solo play | **No NPC.** One player can control both leopards and switch between them. A disconnect pauses for a minute, then offers that or quitting. See [Seats](#seats). |
| Water | **The server sends the flowing cells.** No water logic in the client. |
| Level size | **Each level is as big as it needs to be**, rectangular, with a scenery apron so the camera never shows the edge of the world. |

## Summary

- The game becomes a **linear trail** through six **biomes** for **two leopards**. Each biome is a short run of levels built around **one bespoke mechanic**, ending on a longer finale. Reaching a summit loads the next level.
- Within a biome the mechanic is taught alone, then twisted, then combined with earlier biomes' mechanics, so the trail gets harder by combination rather than by new rules alone.
- Before any level is built, the mock's rules are split into **per-mechanic modules** that a level switches on from its JSON. The Rust server should mirror the same split.
- Protocol cost: **five new entity kinds**, one new hazard state, a `water` field on snapshots, rectangular level dimensions, seat control and vote messages, a trail manifest, and new level-file fields. Ice and split sight need no new kinds at all.

## Biomes

| # | Biome | Mechanic | Levels | Reuses |
| --- | --- | --- | --- | --- |
| 1 | Foothills | levers, plates, keys, sightstone | 3 + finale (High Hollow) | |
| 2 | Talus | crumbling ledges | 3 + finale | pits |
| 3 | Frozen Tarns | ice slides | 3 + finale | ledges |
| 4 | Rockfall | pushable boulders and heavy slabs | 3 + finale | ice, plates |
| 5 | Creeklands | diverting water | 3 + finale | boulders as dams |
| 6 | Whiteout | split sight | 3 + finale | everything |

The biomes run from rules you can see to rules you have to reason about, and end on the one that forces talking.

### Pacing

- A **standard level** clears in **3 to 5 minutes** at a normal pace: two or three rooms, one idea.
- A **finale** runs longer, roughly **6 to 8 minutes**, and pulls the biome's mechanic together with what earlier biomes taught.
- This is what makes a full-level restart on a major failure fair: losing a level costs minutes, not a session.

### The arc inside a biome

1. **Introduce.** The mechanic alone, in a small, safe space. No major hazards yet.
2. **Twist.** The same mechanic with a complication: a timing constraint, a one-way consequence, the partner as part of the solution.
3. **Combine.** The mechanic alongside earlier biomes' rules.
4. **Finale.** Longer, with major hazards placed where the risk is obvious, and a summit worth reaching.

### What a biome owns

- A **mechanic** (its rules module, switched on from level files).
- A **look**: apron scenery, floor palette, wall and tree style, ambient light. Levels inherit it through a `"biome"` field, so a level file does not repeat it.
- A **title card** shown when the trail enters the biome, and a "crossed" card when its finale is cleared.

**Scope.** Six biomes of four levels is 24 hand-built levels. That is a lot. The plan below builds one biome at a time to its full arc, so the game is shippable after any biome, and a biome can start at two levels plus a finale and grow later.

## Framework (build first)

### Seats

There are exactly **two seats**, one per leopard. A player controls one seat, or both.

**Switching.** A player who holds both seats presses **Tab** to switch which leopard they are steering. The other leopard stands still exactly where it was left. Standing still is useful: an idle leopard holds a plate, stops a slide on the ice, or waits at a ledge.

**Who holds what.**

| Situation | Seats |
| --- | --- |
| Two players connected | one each; Tab does nothing |
| Solo, chosen from the title screen | one player holds both |
| Partner disconnects, remaining player chooses to continue | the remaining player holds both |
| Dropped player reconnects | their seat is handed back |

**Disconnects.**

1. The partner drops. The server **pauses the simulation** (the tick stops advancing and inputs are ignored) and tells the remaining player why, with a 60-second countdown.
2. If the dropped player reconnects within the minute, they take their seat back and play resumes where it stopped.
3. When the minute runs out, the remaining player picks on a poster card: **take both leopards** or **leave the trail**.
4. If the dropped player comes back later, their seat is handed back and Tab stops working for the other player.

A third connection is refused (no `welcome` is sent). Spectating can come later.

**What this replaces.** No NPC partner is needed, on the server or in the mock. Solo testing works by holding both seats, so the mock's scripted teammates go away, High Hollow's included. Each level below has a **Solo** note instead, covering anything that changes when one person steers both.

**Perception follows the active seat.** Each seat has its own overlay (split sight, hidden pits). A player holding both sees through whichever leopard they are steering, and the view changes on Tab. Rules always use the true state, so this never touches inputs or anything hashed.

**Client changes.** Scarves drop to two colors: sienna for seat 0, pine for seat 1. The gold ring marks the leopard you are steering; a dim ring marks your other leopard when you hold both. The field guide says "you" and "your partner".

### Level progression

The order of the trail lives in one **manifest**, `levels/trail.json`, instead of a `next` field in every level:

```json
{
  "biomes": [
    { "id": "foothills", "name": "Foothills",    "levels": ["foothills-1", "foothills-2", "foothills-3", "hollow"] },
    { "id": "tarns",     "name": "Frozen Tarns", "levels": ["tarns-1", "tarns-2", "tarns-3", "tarns-finale"] }
  ]
}
```

- The **server** reads it to decide what comes next. On a win it sends `round` `won`, waits as it does today, then sends `level` with the next id and fresh snapshots. The client already rebuilds terrain on `level`, and snapshot replacement clears old entities, so no new message is needed.
- The **client** reads it for the level select and for poster text: "Frozen Tarns, 2 of 4" between levels, the biome's title card on entering it, a "crossed" card after its finale, and "trail complete" after the last one.
- Each level file gains `"biome": "<biome id>"`, which selects its look and apron scenery.
- There is no saved progress. A **title-screen level select**, grouped by biome, is added for testing and demos. The title screen also offers **solo** (hold both seats) or **with a partner**.

### Failure

Failures come in two grades. A minor failure costs one leopard its position and keys. **A major failure restarts the level.**

#### Minor: the leopard resets

Only the leopard who fell pays:

- They return to the **entrance of the room they fell in**, marked in the level file with `E` (one per room).
- **Every key they carry** drops back where it was found, whichever room it came from.
- Their partner, and the room, are untouched. Ledges stay collapsed, boulders stay put, the partner keeps their keys and their place.

This is roughly today's pit rule (shipped in v0.2.0), except that you return to the room's entrance rather than all the way to camp.

#### Major: the level restarts

A major failure puts the whole level back to its start:

- **Both leopards** return to their spawn points, wherever they were.
- **Every key** returns to where it began, whoever was carrying it.
- Every mechanic returns to its starting state: collapsed ledges come back, boulders and slabs go home, sluices swing back, and any pit a boulder filled opens again.

It is the same thing that already happens after a win, minus the poster: the round restarts. In a finale whose checkpoint is lit, it restores the checkpoint instead (see [Checkpoints](#checkpoints)). Because a major failure costs the whole level, levels should stay short enough that a restart stings rather than crushes, and major hazards should be placed where the risk is obvious.

**What counts as major is authored, not detected.** Automatically spotting an unwinnable level is hard to get right, so the level author marks it with hazards that are major by design:

| Hazard | Grade | Where |
| --- | --- | --- |
| Pit, `H` / hidden `h` | minor | everywhere |
| Collapsed ledge | minor | Talus: it behaves as a pit |
| Chasm, `X` | major | wherever a drop should cost the level |
| Open water, `%` | major | Frozen Tarns: a hole in the ice |
| Torrent | major | Creeklands: water arriving in a channel while a leopard stands in it |

### Vote reset

Puzzles can end up unwinnable without anyone falling: a boulder shoved into a corner, the only ledge to a key burned. For those cases either player can **propose restarting the level**:

- Press **R** to propose. If it passes, the level restarts exactly as it does after a major failure.
- The partner sees the proposal and presses **R** to agree. If they do not agree within **10 s (600 ticks)**, the proposal lapses.
- Only one proposal can be open at a time.
- A player **holding both seats** has no one to ask, so their proposal passes at once.

Because the vote needs the partner's consent, neither player can throw away the pair's progress alone. The restart itself is the same code path as a major failure.

### Checkpoints

Every **finale** has exactly one checkpoint, a trail marker placed around its midpoint. Standard levels have none; they are short enough to restart.

- The checkpoint **lights when both leopards are standing on or beside it** at the same time. Requiring both means the saved state never strands one leopard on the wrong side of a burned ledge or a closed door.
- Lighting it **records the whole level state**: both leopards' positions, every key and who carries it, and every mechanic's state.
- After that, a **major failure restores the recorded state** instead of restarting the level. Minor failures are unaffected.
- A **vote reset** also restores the checkpoint once it is lit. A full restart from the beginning is still available from the level select.
- The checkpoint cannot be lit twice; the recorded state is fixed for the rest of the attempt.

Because the checkpoint restores a snapshot rather than re-running a level from its start, each mechanic's `Reset()` becomes a pair: `Save()` and `Restore(saved)`. The level start is simply the state saved at load.

### Level size and the apron

- Levels are **rectangular and sized to fit their puzzle**: a tight gully might be 20×14, the creek 40×28.
- `welcome` carries `width` and `height` instead of a single `grid`. Level files hold a rectangular map; the loader validates every row against the width.
- The client draws a **scenery apron** around the playable map: a band of decorative terrain (forest, scree, snowfield by level) wide enough that the camera never shows the edge of the plinth. The apron is purely visual. The server never sees it and nothing can enter it.
- Apron width is derived from the camera's widest framing, so it grows automatically if framing changes.

### Mechanic modules

Today every rule lives in `MockFeed.cs`. Six levels of bespoke rules in one file will not survive. Instead:

```
level JSON:  "mechanics": ["ledges", "ice"]

IMechanic
  OnLoad(level, world)                  claim glyphs, create entities
  TryMove(seat, from, dir) -> result    allow, block, redirect (ice) or push (boulders)
  OnEnter(seat, cell)                   ledge cracks, keys, pits
  OnLeave(seat, cell)                   ledge collapses
  OnInteract(seat)                      sluices
  OnTick(tick)                          water flow, slides in progress, push windows
  Save() -> state                       record this mechanic's state (level load, checkpoint)
  Restore(state)                        put it back (major failure, vote reset)
  Snapshot(entities)                    write this mechanic's entity states
  Overlay(seat, overlay)                split sight
```

The core (movement, keys, doors, goal, round flow, both failure grades, vote reset, seats) becomes the "hollow" module, so the existing level is the first client of the new interface. The Rust server should use the same boundaries, which makes rule parity reviewable one mechanic at a time.

### Ordering rules (for hash parity later)

Several mechanics let leopards affect each other (pushing, sliding into each other). To keep the server and any future replay or hash check in agreement:

- Within a tick, seats are processed **in seat order**. Seat 0 moves before seat 1, and seat 1 sees seat 0's result.
- A mechanic's `OnTick` runs **after** all moves in the tick, in the order the level lists mechanics.
- Derived state (water flow) is **recomputed from scratch** each tick, never patched.
- Timing windows are measured in **server ticks**, never wall-clock time, so they replay identically.

## Biome mechanics

### 1. Foothills: the basics

Everything High Hollow teaches today, spread over short levels ahead of the full hub.

**Levels.** (1) a lever and its door; (2) a plate and a partner who holds it; (3) keys and the summit gate, with a sightstone and hidden pits. **Finale:** High Hollow, reworked for two leopards and the new failure model.

**Rules.** No new mechanic: this biome is the core module. That makes it the right place to test the framework and the 3 to 5 minute pacing target before any new rule is written.

### 2. Talus: crumbling ledges

A scree slope cut by drops, crossed on narrow stone ledges.

**Rule.** A ledge holds while anyone stands on it. When the last leopard steps off, it collapses and stays collapsed until the level restarts. Ledges show a crack as soon as they have been stepped on, so the collapse is never a surprise. Walking into a collapsed ledge is a **minor** failure, like a pit. A route burned by accident comes back only through a level restart, by vote if nobody fell.

**Co-op hook.** The pair cannot follow the same path. They split up, or one crosses first and opens a lever route for the other. A key at the end of a single-use ledge forces a choice of who fetches it.

**Glyph.** `=` ledge.

**Protocol.** New kind **Ledge (8)**. `state`: 0 intact, 1 cracked, 2 collapsed.

**Solo.** Plays the same: cross with one leopard, switch, route the other.

**Edge case.** Both leopards step off the same ledge on the same tick: it collapses once.

### 3. Frozen Tarns: ice slides

A frozen lake with rock islands.

**Rule.** Stepping onto ice sets you sliding in that direction, one cell every 4 ticks (faster than walking at 9), until the next cell is not ice, is blocked, or holds your partner. You stop on the first non-ice cell. Input is ignored while sliding. Sliding over a key picks it up. Sliding into a pit is a minor failure; sliding into **open water** (a hole in the ice) is a major one.

**Co-op hook.** A leopard standing on the ice is a **stopper** for the other. Many routes are impossible alone and trivial with your partner parked in the right spot.

**Glyphs.** `~` ice, `%` open water. Both are terrain read from the level file, like walls.

**Protocol.** None. Both sides read ice from the level file (see [the level file is part of the contract](#protocol-changes)).

**Reuses.** Ledges at the lake edge, so a missed stop can cost the ledge you came in on.

**Solo.** Works well: park one leopard as the stopper, switch, slide the other into it. A leopard left mid-slide finishes its slide on its own.

**Edge case.** Both sliding into each other: in seat order, the first to arrive takes the cell and the second stops one short.

### 4. Rockfall: pushable boulders

A gully choked with fallen rock.

**Boulders.** Walking into a boulder pushes it one cell if the cell beyond is empty floor or ice (not a wall, door, boulder, slab or leopard). A boulder on ice slides like a leopard. A boulder pushed into a pit **fills** it: the pit becomes walkable floor and the boulder is gone. A boulder on a plate holds the plate down, freeing your partner.

#### Heavy slabs

A **slab** is a long rock two cells wide. It moves only across its width, and only when **both leopards push it from the same side, one per half**.

- A push registers when a leopard walks into a slab half from the side it can move toward. That push stays **live for 90 ticks (1.5 s)**.
- If the other leopard pushes the other half, from the same side, while the first push is live, the slab moves one cell. Both leopards step forward with it.
- If the window lapses, the push expires and nothing moves. The leopard can push again.
- While a push is live the slab **strains**: it shows visibly, so the partner knows to push now.
- Pushing along the slab's length does nothing; it is too long to shove end-on.

The window is counted in server ticks, so it replays exactly. It also forgives network jitter between two players on different connections, which a same-tick rule would not.

**Soft-locks.** Boulders and slabs can be pushed where they can never come back. That is what the [vote reset](#vote-reset) is for.

**Glyphs.** `O` boulder; `=` is taken, so a slab is `[` and `]` for its two halves along a row, or `^` and `v` down a column.

**Protocol.**
- New kind **Boulder (9)** for boulders and slabs alike. A slab is one entity at its first cell. `state` = shape + strain:

  | `state` | meaning |
  | --- | --- |
  | 0 | boulder |
  | 1 | slab lying along x (occupies x and x+1) |
  | 2 | slab lying along y (occupies y and y+1) |
  | +4 | a push is live (slab is straining) |

- Hazard gains `state` 1 = **filled** (walkable).

**Solo.** A slab needs both leopards, so a solo player pushes with one, presses Tab and pushes with the other inside the window. 1.5 s is probably tight for that; the server knows when one client holds both seats and can use a longer window then. To tune in playtest.

### 5. Creeklands: diverting water

A creek enters from the north edge and braids into channels across the level.

**Rule.** Water flows from a source through channel cells. **Sluice gates** at the forks send it one way or another; interact with a sluice to swing it. Flowing water cannot be walked into; a dry channel bed is walkable. A leopard standing in a channel when water arrives is **swept away**, a major failure, so swinging a sluice while your partner is crossing restarts the level. Water reaching a **mill wheel** turns it, and a turning wheel opens its linked door or the summit gate. Flow is recomputed every tick from the source, so swinging one sluice can dry one channel and flood another.

**Co-op hook.** One leopard dries a channel so the other can cross, then the one who crossed sends water back to a wheel on their side. Some wheels need water while both are across, so the order of operations matters.

**Reuses.** Boulders as **dams**: a boulder pushed into a channel blocks flow past it.

**Glyphs.** `w` channel, `W` source, `V` sluice, `M` mill wheel.

**Protocol.**
- New kinds **Sluice (10)** (`state` = which exit is open, 0 to 3) and **Wheel (11)** (`state` 1 turning).
- New field on `snapshot`: `"water": [[x, y], ...]`, every cell currently flowing. The server computes flow; the client only draws it.

**Solo.** Plays the same, one leopard at a time. Watch for sweeping away your own idle leopard.

### 6. Whiteout: split sight

A snowfield in a squall, riddled with hidden crevasses.

**Rule.** Each crevasse is visible to exactly one leopard. Neither sees the whole field. Crevasses are real for both (walking into one you cannot see is still a fall); only the seeing differs. A few **false** crevasses are shown to one leopard where the ground is actually safe.

**Co-op hook.** The pair guide each other through chat: "two east of you, don't". This is the only biome that cannot be solved by one leopard looking harder.

**Protocol.** None beyond what exists. Each seat's overlay `hidden` list holds the crevasses that leopard may not see, and `phantoms` carries its false ones.

**Reuses.** Ice, ledges and boulders all appear, each partly hidden.

**Solo.** Because perception follows the active seat, a solo player sees each leopard's half of the truth in turn and has to remember the other half. Playable, but a weaker puzzle; this biome earns its place with two real players and should be playtested that way before it is called done.

## Protocol changes

| Change | Needed by | Kind |
| --- | --- | --- |
| `welcome` carries `width`, `height` (replaces `grid`) | level size | message change |
| Third connection refused | seats | server behavior |
| Server sends `{"t":"seats","yours":[0,1],"active":0}` whenever control changes | seats | new message |
| Client sends `{"t":"control","seat":1}` to switch the leopard it steers | seats | new message |
| `round` states `paused` (with `reason`, `seconds`) and `choose` | disconnects | message change |
| Client sends `{"t":"choice","pick":"both"}` or `"quit"` | disconnects | new message |
| Client sends `{"t":"vote","reset":true}` to propose or agree | vote reset | new message |
| Server sends `{"t":"vote","by":0,"ticks_left":600}` and the outcome | vote reset | new message |
| `round` gains a `reason` when a restart is not a win: `"fell"` or `"vote"` | failure, vote reset | message change |
| Overlays are per seat, sent for the seat a client is steering | seats, split sight | behavior change |
| `levels/trail.json` manifest: biomes and their levels, in order | progression | new file |
| `biome` in level files | biomes | level format |
| Checkpoint (12), `state` 1 = lit | finales | new kind |
| `mechanics` list in level files | framework | level format |
| `links` in level files: lever or plate to door, as `[col, row]` in map coordinates | all levels | level format (built in v0.3.0) |
| Rectangular maps; `E` room entrance glyph | level size, failure | level format |
| Ledge (8), `state` 0/1/2 | Talus | new kind |
| Boulder (9), `state` shape + strain | Rockfall | new kind |
| Hazard `state` 1 = filled | Rockfall | new state |
| Sluice (10), `state` 0 to 3 | Creeklands | new kind |
| Wheel (11), `state` 0/1 | Creeklands | new kind |
| `water` field on `snapshot` | Creeklands | new field |
| New glyphs `E X C = ~ % O [ ] ^ v w W V M` (`C` checkpoint) | several | level format |

Inputs need no seat field: the server applies a client's input to the seat it last switched to with `control`.

**The level file is part of the contract.** Ice, channels and room entrances are terrain the server's rules depend on, and the client draws them from the same file. Both sides must read the same JSON; a mismatch would show ice where the server has floor. Worth a shared schema and a test that both parsers agree on every shipped level.

## Client work

- Each new kind needs a look in both art styles (placeholder first) and a field-guide entry.
- Terrain types (ice, channels, open water) need floor-texture support in `ProcArt.LevelFloor` and placeholder tiles. Water gets a flowing and a dry look, driven by the `water` field.
- The slab's straining state needs a visible cue, since it is the only prompt the partner gets.
- The apron and rectangular levels touch `GridFloor`, `CameraFramer` and the level loader.
- Seats: Tab to switch, the camera following the active leopard, a gold ring on the active leopard and a dim ring on the idle one, two scarf colors.
- A vote prompt ("your partner wants to restart the level: R to agree") with a countdown, and R to propose.
- A short card when the level restarts after a major failure or a vote, saying which.
- A pause card with the 60-second countdown, then the take-both-leopards or leave choice. Both reuse the poster screen.
- Title screen: solo or with a partner, plus a level select grouped by biome.
- Per-biome looks: apron scenery, floor palette, walls and trees, ambient light, keyed by the level's `biome`.
- Biome title and "crossed" cards, and "biome, n of m" on the between-levels poster.
- Failure grades need distinct looks: a pit, a collapsed ledge, a chasm, open water. The field guide gains a line on minor versus major, and on R.

## Suggested build order

1. Mechanic module split in the mock, with the core as its first module. No behavior change; High Hollow must play identically.
2. Seats and switching, with the mock giving a solo tester both seats. High Hollow drops its third spawn and its scripted teammates here.
3. Both failure grades, the vote reset, rectangular levels and the apron. High Hollow moves to the new failure model.
4. Disconnect pause and the take-both choice. Mostly the seats work from step 2 plus a pause.
5. The trail manifest, progression, biome cards, the level select and the solo option.
6. **Foothills**: three short intro levels ahead of High Hollow. No new rules, so this proves the framework and the pacing target.
7. **Talus**: the smallest new rule, and it proves the module interface end to end.
8. **Frozen Tarns**, **Rockfall** and **Creeklands**, in trail order, each built to its full arc before the next starts.
9. **Whiteout** last, playtested with two real clients.

## Open questions

1. **Window lengths** (1.5 s for a two-player slab push, likely longer when one player holds both seats) are to be tuned in playtest.
2. **Levels per biome** is three plus a finale for now, with room to grow to five if a biome needs more teaching.
