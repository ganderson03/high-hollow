# Presenting `sim.rs`

A walkthrough for a 5 to 7 minute talk on [server/src/sim.rs](../server/src/sim.rs), the server's rule engine. Line numbers refer to the file as committed with this note.

## The one-sentence version

`Sim` takes the players' inputs and turns them into the next state of the level, 60 times a second. It has no clock, no sockets and no idea who is connected. That is why it can be exact, deterministic and tested on its own.

## 1. Where it sits (sim.rs lines 7–22)

Show the diagram in the module header: `net.rs` moves bytes, `game.rs` knows who steers which leopard and owns the clock, and `sim.rs` only knows the rules.

> "The simulation never asks what time it is or who pressed a key. The session tells it: this is seat 0's input, now advance to tick 412. Give it the same inputs and you get the same world, every time."

## 2. Single-player to server (sim.rs lines 24–29, next to MockSim.cs line 79)

Put the two loops side by side:

```csharp
// The client's single-player mock: only the leopard you are steering acts.
if (_move != MoveDir.None && tick >= _nextStep[_active]) TryMove(_active, tick);
```

```rust
// The server: both seats act every tick, in a fixed order.
for seat in 0..SEATS { /* interact, then step or slide */ }
```

> "In single-player only one leopard can move at a time, so order never mattered. With two people, both can press a key in the same sixtieth of a second, so we fix the order: seat 0, then seat 1, and seat 1 sees what seat 0 just did. That one rule is what makes the server's answer the same every time."

## 3. The tick (sim.rs lines 221–277): the centrepiece

Walk the five numbered comments in `step`:

1. **Each seat, in order:** slide if on ice, otherwise interact, then step (at most once every 9 ticks).
2. **Settle:** if anything fell into a chasm, restart now, before seat 1 acts on a world that no longer exists.
3. **Mechanics' `on_tick`:** water re-flows, boulders slide, slab pushes time out.
4. **Settle again:** water can arrive under a leopard.
5. **The checkpoint** lights when both leopards stand at it.

> "Every rule in the game hangs off one of these five steps, and that is the whole game loop."

## 4. Mechanics ask, the simulation acts (sim.rs line 285, `settle`)

Mechanics never restart the level themselves. They set `world.major` with a reason, and `settle` is the one place that acts on it.

> "So there is exactly one restart path, used by a fall, by water, and by the players' vote, and a restart can never happen halfway through a rule."

## 5. Save and restore by cloning (sim.rs line 81 `Saved`, line 172 `restore`)

A `Saved` is the world plus every mechanic's state, and saving is just `clone()`.

> "That one idea gives us three features: the level start, the checkpoint, and every restart. Nobody writes a reset function per mechanic. A restart swaps the copy back in."

Mention the detail in `restore`: it keeps this tick's messages (so players still hear "into the chasm.") and drops held keys (so nobody keeps walking on the restored map).

## 6. How we know it is right (rules.rs, `replay`)

> "A separate Python solver, written from the original game's rules, finds the shortest winning route through each level. The server has to win on exactly the last move of that route. It does, for every level, including the ones redesigned since."

## Likely questions

- **Why Rust?** One owner of the world (the simulation thread), no shared mutable state, and the compiler enforces it. Fixed-size integers and no floating point in the rules keep it exact.
- **Why ticks instead of time?** A replay of the same inputs gives the same result, and timing windows (the 1.5 s slab push) mean the same thing on every machine.
- **What does the client do?** It only draws what the server sends and sends keys. It never decides a rule.
