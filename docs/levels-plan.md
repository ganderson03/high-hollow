# Level plan

Status: **first pass**. Every level on the trail, planned to the level of beats: what it teaches, how the pair solves it, what can go wrong. Maps are drawn later, one biome at a time. The rules these levels use are defined in [levels-design.md](levels-design.md).

## Conventions

- Levels are numbered **biome-level**: `1-1` is the first Foothills level, `1-F` its finale. Level ids in the manifest follow the biome (`foothills-1`, `tarns-finale`).
- **Size** is a starting estimate in cells (width × height) and will change when the map is drawn.
- **Time** is a clear at a standard pace by a pair who has not seen the level. Standard levels aim for 3 to 5 minutes, finales for 6 to 8.
- Every level ends at a **summit marker**. A level uses keys and a summit gate only where it says so; when it does, the gate opens once **the pair carries every key between them**.
- **Minor** hazards (pits, collapsed ledges, crevasses) send one leopard back to the room entrance. **Major** hazards (chasms, open water, torrents) restart the level, or the checkpoint in a finale.
- **Solo** notes appear only where holding both seats changes how a level plays.
- Each biome follows the same arc: **introduce** the mechanic alone, **twist** it, **combine** it with earlier biomes, then the **finale**.

## Overview

| Level | Name | Teaches | Size | Time |
| --- | --- | --- | --- | --- |
| 1-1 | Trailhead | moving, levers, doors | 16×12 | 3 min |
| 1-2 | Stile Meadow | plates, holding for your partner | 18×14 | 4 min |
| 1-3 | Cairn Fields | keys, the summit gate, hidden pits, the sightstone | 22×16 | 5 min |
| 1-F | High Hollow | all of it, in a hub | 32×32 | 7 min |
| 2-1 | Scree Steps | ledges; crossing together | 18×12 | 3 min |
| 2-2 | Split Scarp | one-way routes; splitting up | 22×16 | 4 min |
| 2-3 | Lever Talus | ledges with levers and plates | 24×18 | 5 min |
| 2-F | Talus Traverse | ledges over chasms | 30×24 | 7 min |
| 3-1 | First Ice | sliding and stopping | 16×14 | 3 min |
| 3-2 | Stopper's Bay | your partner as a stopper | 20×16 | 4 min |
| 3-3 | Thaw Shelf | ice with ledges and plates | 24×18 | 5 min |
| 3-F | Glass Tarn | a whole lake, open water | 32×26 | 8 min |
| 4-1 | Loose Rock | pushing, filling pits, boulders on plates | 18×14 | 3 min |
| 4-2 | Two to Shift | heavy slabs, pushing together | 20×16 | 4 min |
| 4-3 | Rock on Ice | boulders that slide | 24×18 | 5 min |
| 4-F | Rockfall Gully | boulders, slabs and chasms | 28×30 | 8 min |
| 5-1 | The Sluice | diverting water to cross | 18×14 | 3 min |
| 5-2 | Mill Run | wheels, and when not to swing a sluice | 22×18 | 4 min |
| 5-3 | Dam Builders | boulders as dams | 26×20 | 5 min |
| 5-F | Braided Creek | a whole river system | 40×28 | 8 min |
| 6-1 | First Squall | split sight, talking it through | 18×14 | 3 min |
| 6-2 | False Snow | false crevasses | 20×16 | 4 min |
| 6-3 | Blind Ice | split sight on the ice | 24×18 | 5 min |
| 6-F | Whiteout Col | everything, half hidden | 34×30 | 8 min |

Roughly 2 hours 10 minutes for the full trail at a standard pace, before failures.

---

## 1. Foothills: the basics

Warm meadow and dry-stone walls: the look the game has today. No new rules and no major hazards. This biome is the tutorial, and the testbed for the framework and the pacing target.

### 1-1 Trailhead

**Teaches.** Moving, levers, doors, and that the other leopard matters.

1. The pair arrives in a small walled meadow. A lever beside a closed door: pull it, the door opens. Chat explains Space.
2. Second room: the lever is **on the far side** of the door. One leopard takes a side path round to the lever and opens the door for the other.
3. Third room: two doors in sequence, each with its lever on the far side of the other. The pair leapfrog: A opens B's door, B opens A's.
4. Summit marker on a small rise.

**Hazards.** None.

**Solo.** Every beat is "one leopard pulls, the other walks through". Tab between them.

### 1-2 Stile Meadow

**Teaches.** Plates hold a door only while someone stands on them; holding is how you help.

1. A plate and a door. Stand on it, the door opens; step off, it shuts. Chat says so.
2. The reciprocal stile: A holds a plate so B can pass, then B finds a plate on the far side that lets A through.
3. Two plates, two doors, alternating down a narrow lane, so the pair advances one at a time like a ratchet.
4. Summit marker.

**Hazards.** None.

**Solo.** The first level where the idle leopard earns its keep: park it on the plate, switch, walk through.

### 1-3 Cairn Fields

**Teaches.** Keys, the summit gate, pits (minor), hidden pits and the sightstone.

1. A summit gate with three empty key slots, in plain view from the start.
2. Key one: behind a lever door in the west nook.
3. Key two: across a field with a few visible pits. Walking into one shows the minor failure: back to the room entrance, the key you carried back where it lay.
4. Key three: in the east nook, with hidden pits and a false key. A sightstone stands at the nook's mouth; using it reveals the pits and unmasks the false key.
5. Carry all three to the gate, then the summit.

**Hazards.** Pits and hidden pits (minor).

### 1-F High Hollow

The existing level, reworked for two leopards and the new failure model.

**Teaches.** Everything above at once, in a hub with four rooms.

1. Camp at the centre, with the summit gate to the south.
2. **Lever Hollow** (west): a lever and its door; a key.
3. **Twin Stones** (north): a plate that holds the door for the partner; a key.
4. **Veiled Scree** (east): hidden pits, a false key, the sightstone; a key.
5. The gate answers three keys. The summit is beyond it.

**Checkpoint.** At the mouth of Veiled Scree, the hardest room, lit once both leopards reach it.

**Hazards.** Pits and hidden pits (minor).

**Changes from today.** The third spawn and the scripted teammates go. Rooms get `E` entrances for minor failures. The pit rule moves from "back to camp" to "back to the room entrance".

---

## 2. Talus: crumbling ledges

Grey scree slopes and cut drops. Ledges hold while someone stands on them and collapse when the last leopard steps off. A collapsed ledge is a minor hazard; the authored chasm (`X`) is major and appears only in the finale.

### 2-1 Scree Steps

**Teaches.** How a ledge behaves, and crossing together.

1. A single ledge over a short drop, with a chat hint. The first leopard across finds the ledge still standing behind them.
2. When the second leopard steps off, it collapses. They have both crossed; nothing is lost. The crack shown after the first step is the warning.
3. A chasm crossed by two parallel ledges. Each leopard takes one.
4. A ledge long enough for both leopards to stand on at once. It only collapses when **both** are off, so crossing in a line is safe.
5. Summit marker.

**Hazards.** Collapsed ledges (minor): falling back to the entrance is cheap here.

### 2-2 Split Scarp

**Teaches.** Every ledge is one-way; decide who goes where before stepping.

1. A fork: two ledge routes, each leading to a lever that opens a door on the **other** route. The pair must split.
2. Both routes are burned once crossed. The levers open a shared door where the pair meets again.
3. A key at the end of a single ledge spur: only one leopard can fetch it, and the other has to go round by a lever route to let them back out.
4. Summit gate (one key), then the summit.

**Hazards.** Collapsed ledges (minor).

**Solo.** Plan both routes before moving either leopard; the idle one waits at the fork.

### 2-3 Lever Talus

**Teaches.** Ledges alongside the Foothills rules.

1. A plate on a rock island reached by a single ledge. Whoever stands on it opens the way for the partner, but the ledge behind them is gone.
2. The island has a lever that opens a door off it; pulling it is how the plate-holder gets out afterwards. Order matters: pull the lever before the partner needs you on the plate, and you are stuck.
3. Three ledges across a drop, but two are pits wearing a ledge's look (an overlay substitution). The sightstone shows which one is real.
4. Two keys, each behind a different combination, then the gate.

**Hazards.** Collapsed ledges, hidden pits (minor).

### 2-F Talus Traverse

**Teaches.** Ledges with real consequences.

1. A long descent across a talus field cut by **chasms** (`X`, major). Ledges bridge them.
2. Three keys, each at the end of a route that burns ledges behind it, so the pair must plan an order that leaves a way back.
3. A two-ledge bridge where both leopards must be on the bridge at once to reach a plate at its middle.
4. The summit gate at the bottom of the traverse.

**Checkpoint.** On the saddle halfway down, after the first key.

**Hazards.** Collapsed ledges (minor); chasms (major).

---

## 3. Frozen Tarns: ice slides

Pale blue ice, rock islands, snow-dusted pines. Ice sends a leopard sliding until something stops it; a partner is a stopper. Open water (`%`, major) appears only in the finale.

### 3-1 First Ice

**Teaches.** Sliding, and where you stop.

1. A small frozen pond with rocks round the edge. Step on and slide across; you stop on the far rock.
2. A key on a rock in the middle of the pond. Slide to it from the side where it lines up.
3. A key on the ice with nothing behind it. You slide straight past. Chat hints: "something to stop against".
4. The partner stands on the ice beyond the key; the other slides in and stops on the key.
5. Summit marker.

**Hazards.** None (a pit at the pond edge only in the last room, to show a slide can end badly).

### 3-2 Stopper's Bay

**Teaches.** Taking turns being the stopper.

1. Two levers on two islands. Each island is reachable only by sliding into a leopard parked at the right spot.
2. A zigzag where the pair alternates: A parks, B slides to A, B moves on and parks, A slides to B.
3. A plate on a rock island: the leopard who slides there holds it, which opens the channel the other needs.
4. Summit marker.

**Hazards.** Pits on the ice (minor).

**Solo.** The biome's core loop: park, switch, slide. A leopard left mid-slide finishes its slide on its own.

### 3-3 Thaw Shelf

**Teaches.** Ice alongside ledges and plates.

1. A slide that overshoots its rock lands on a ledge, which then collapses behind you: sliding the wrong way costs a route.
2. Plates sit on rock, never on ice, so you cannot stop on them by sliding unless a partner stops you there.
3. A sightstone reveals which ice cells are cracked pits.
4. Two keys and the gate.

**Hazards.** Pits, hidden pits, collapsed ledges (minor).

### 3-F Glass Tarn

**Teaches.** Ice at scale, with open water.

1. A wide frozen lake with **holes of open water** (`%`, major). A slide into one restarts the level (or the checkpoint).
2. Three keys on floes in different parts of the lake, each reachable only by a chain of stops.
3. A ledge at the lake's edge means one missed stop costs the only way back to shore.
4. The summit on a far headland.

**Checkpoint.** On the central island, after the first two keys.

**Hazards.** Pits, collapsed ledges (minor); open water (major).

---

## 4. Rockfall: boulders and slabs

A gully of fallen rock. Boulders push one cell, fill pits and hold plates. Heavy slabs need both leopards pushing inside the window. Chasms return in the finale, and a boulder pushed into one is gone.

### 4-1 Loose Rock

**Teaches.** Pushing, filling a pit, a boulder on a plate.

1. A boulder in a corridor. Walk into it to push it.
2. A pit in the way. Push a boulder into it; it fills and becomes floor.
3. A plate and a door. Push a boulder onto the plate so neither leopard has to hold it.
4. A boulder pushed into a corner, deliberately unrecoverable in a side room, with chat introducing **R** for the vote reset.
5. Summit marker.

**Hazards.** Pits (minor).

### 4-2 Two to Shift

**Teaches.** Heavy slabs and the push window.

1. A slab blocking a wide corridor. One leopard pushes: it strains, then settles. Chat explains.
2. Both push, one per half, from the same side inside the window. It moves.
3. A slab that must be pushed several cells, down a corridor exactly two wide, so the pair walks it along together.
4. A slab that ends on two plates at once, holding a double door.
5. Summit marker.

**Hazards.** None beyond pits (minor).

**Solo.** Push, Tab, push inside the window; the window is longer when one player holds both seats.

### 4-3 Rock on Ice

**Teaches.** Boulders with the ice rules.

1. A boulder pushed onto ice slides until it stops, like a leopard.
2. Use a sliding boulder to fill a pit on the far side of a pond that no leopard can reach.
3. Use a boulder as a **stopper** on the ice, freeing the partner from stopper duty.
4. A ledge that a boulder slides across: the boulder does not count as standing on it, so it survives.
5. Two keys and the gate.

**Hazards.** Pits, collapsed ledges (minor).

### 4-F Rockfall Gully

**Teaches.** Boulders at scale, with chasms.

1. A long gully choked with boulders and slabs. Some must be moved; some are traps that block the way if pushed.
2. **Chasms** (`X`, major) along the gully. A boulder pushed into a chasm is lost, which may make the level unwinnable: the vote reset is the escape.
3. A slab gate midway that takes both leopards and several pushes, with a plate on either side.
4. Three keys, the gate, the summit at the head of the gully.

**Checkpoint.** Just past the slab gate.

**Hazards.** Pits (minor); chasms (major).

---

## 5. Creeklands: diverting water

Braided channels, stony banks, mill wheels. Sluices swing water between channels; flowing water cannot be entered, and a leopard standing in a channel when water arrives is swept away (major). Unlike other biomes, the major hazard is part of the mechanic, so it is introduced carefully from the first level.

### 5-1 The Sluice

**Teaches.** Swinging a sluice to dry a channel and cross.

1. One channel, one sluice. Water blocks the way; swing the sluice, the channel dries, cross the bed.
2. A fork: the sluice can dry one of two channels at a time. Dry one, cross, swing back to dry the other.
3. A mill wheel: send water to it and the exit opens.
4. Summit marker.

**Hazards.** Torrents (major), but every channel here is in full view of its sluice, so the swinger can always see whether the bed is clear. Chat warns the first time a sluice is used.

### 5-2 Mill Run

**Teaches.** Two wheels, one sluice, and the order of operations.

1. Two wheels, each opening a door. One sluice can feed only one at a time.
2. One leopard crosses a dry bed to the far bank, then water goes back to the wheel on their side.
3. The partner has to be clear of the channel before the sluice swings: the first real torrent risk, with the channel out of the swinger's direct view.
4. Summit marker.

**Hazards.** Torrents (major).

**Solo.** Watch for sweeping away your own idle leopard.

### 5-3 Dam Builders

**Teaches.** Boulders as dams.

1. A boulder pushed into a channel blocks the flow past it.
2. Split one stream between two wheels by damming one branch partway, so both turn at once.
3. A slab across a wide channel, pushed together, dams it completely.
4. Two keys and the gate.

**Hazards.** Pits (minor); torrents (major).

### 5-F Braided Creek

**Teaches.** A whole river system.

1. A source at the north edge braiding into four channels across the level, with sluices at every fork.
2. Three wheels. Two open doors, one opens the summit gate, and they must all turn at once for the end.
3. Keys on gravel bars reachable only when the right channels are dry.
4. Boulders available as dams, and ice on a still pool in one corner.

**Checkpoint.** At the old mill in the middle of the creek, after two wheels are turning.

**Hazards.** Pits (minor); torrents (major).

---

## 6. Whiteout: split sight

A snowfield in a squall. Each crevasse is visible to exactly one leopard; some leopards see false crevasses where the ground is safe. Crevasses are **minor** hazards; chasms (major) appear in the finale. This biome is built for two real players and should be playtested that way.

### 6-1 First Squall

**Teaches.** Your partner sees what you cannot.

1. A wide, safe snowfield with a few crevasses, each shown to only one leopard. Chat explains.
2. A lane where the crevasses one leopard sees are the ones the other must avoid. They guide each other through.
3. Summit marker.

**Hazards.** Crevasses (minor), spaced generously.

**Solo.** You see what the leopard you are steering sees. Switch to check the other's view before moving.

### 6-2 False Snow

**Teaches.** Some crevasses are not there.

1. One leopard sees a crevasse blocking the only path. The other sees clear snow. It is false.
2. The reverse: a crevasse only one leopard sees is real, and the other walks toward it.
3. A mix where the pair must compare views cell by cell.
4. Summit marker.

**Hazards.** Crevasses (minor).

### 6-3 Blind Ice

**Teaches.** Split sight on the ice, where you cannot stop to check.

1. An icy slope with crevasses on it, split between the two views. A slide commits you, so the partner has to call the line before you step on.
2. Stopper positions that only one leopard can see are safe.
3. Two keys and the gate.

**Hazards.** Crevasses (minor).

### 6-F Whiteout Col

**Teaches.** Everything, partly hidden. The end of the trail.

1. A climb to a mountain col in a squall. Ledges, ice, boulders and a frozen channel all appear, each with parts hidden from one leopard or the other.
2. **Chasms** (major) near the top.
3. Three keys and the last summit gate.
4. The summit, and the "trail complete" card.

**Checkpoint.** At the col hut, after the icy section.

**Hazards.** Crevasses, collapsed ledges (minor); chasms (major).

---

## Before drawing maps

- **Build Foothills first.** It needs no new rules, so it tests the framework and the pacing target. Time real clears of 1-1 to 1-3 against their 3 to 5 minute targets before planning the other biomes' maps in detail.
- **One biome at a time, to its full arc.** Draw, build and playtest each biome's four levels before starting the next.
- **Keep a clear-time log.** Record every playtest clear per level. It is the only way to hold the pacing target as the levels get harder.
