//! # The simulation: one level, advanced one tick at a time
//!
//! `Sim` is the authoritative rule engine for a single level. It owns the world (where the
//! leopards are, what every piece is doing) and the rule modules ("mechanics") that the level
//! switches on, and it turns inputs into a new world state, one tick at a time.
//!
//! ## Where it sits
//!
//! ```text
//!   net.rs   sockets: bytes in, bytes out
//!     |
//!   game.rs  sessions: who is connected, which seat they steer, the clock, the trail
//!     |        calls  sim.set_input(seat, ..)  then  sim.step(tick)  60 times a second
//!   sim.rs   THIS FILE: the rules of one level
//!     |
//!   world.rs + mechanics/*  the state, and one module of rules per biome
//! ```
//!
//! The simulation has no clock, no sockets and no idea how many players there are. The caller
//! says "this is seat 0's input" and "advance to tick 412". That makes it deterministic (the
//! same inputs always give the same world) and testable on its own: the tests drive it
//! directly, with no server running.
//!
//! ## From the single-player mock to the server
//!
//! It mirrors the client's `Crawler.Mock.MockSim`, which ran the rules inside the game for one
//! player. The mock moves only the leopard being steered. Here **both seats act every tick**,
//! because two people can press keys at the same moment, so the order in which they act has to
//! be fixed: seat 0 first, then seat 1, which sees seat 0's result.
//!
//! Timing is counted in ticks, never wall-clock time, so a replay of the same inputs is exact.

use crate::level::Level;
use crate::mechanic::{self, each_with_others, Mechanic};
use crate::protocol::{EntityWire, Move};
use crate::world::{beside, step, Kind, Perception, RestartCause, SimEvent, World, SEATS};

// ---------------------------------------------------------------------- timing, in ticks (60 per second)

/// Walking speed: one cell every 9 ticks (150 ms) while a direction is held.
pub const STEP_TICKS: u32 = 9;
/// Sliding speed on ice: one cell every 4 ticks (about 67 ms), faster than walking.
pub const SLIDE_TICKS: u32 = 4;
/// How long one leopard's push on a slab stays live, waiting for the partner's (1.5 s).
pub const PUSH_WINDOW: u32 = 90;
/// The same window when one player steers both leopards and has to press Tab between the two
/// pushes (4 s).
pub const SOLO_PUSH_WINDOW: u32 = 240;

/// What one seat's player is doing right now, as last reported by the session.
#[derive(Debug, Clone, Copy)]
struct SeatInput {
    /// The direction being held, or `Move::None`. Held directions repeat every `STEP_TICKS`.
    dir: Move,
    /// An interact press waiting to be used. Latched (see `set_input`) so a quick tap between
    /// two ticks is not lost.
    interact: bool,
}

/// No key held, nothing pressed.
const IDLE: SeatInput = SeatInput { dir: Move::None, interact: false };

/// A complete copy of the level's state: the world plus every mechanic's private state.
///
/// Saving is just cloning, and restoring is swapping the copy back in. That one idea gives us
/// three features: the level start (saved when the level loads), the checkpoint (saved when it
/// lights) and every kind of restart (a major failure, an agreed vote).
#[derive(Clone)]
struct Saved {
    world: World,
    mechs: Vec<Box<dyn Mechanic>>,
}

/// One level's rules and state. See the module documentation for the shape of a tick.
pub struct Sim {
    /// Everything the leopards and pieces are doing. Public so the session and the tests can
    /// read positions; only the simulation changes it.
    pub world: World,
    /// The rule modules this level uses, core first, then the level's `"mechanics"` list.
    mechs: Vec<Box<dyn Mechanic>>,
    /// The latest input for each seat.
    input: [SeatInput; SEATS],
    /// The earliest tick each seat may take its next step (walking or sliding).
    next_step: [u32; SEATS],
    /// The slab push window currently in force (see `set_push_window`).
    push_window: u32,
    /// The state when the level loaded. A restart with no checkpoint comes back here.
    start: Saved,
    /// The state when the checkpoint lit, once it has; restarts come back here instead.
    checkpoint: Option<Saved>,
}

impl Sim {
    /// Loads a level: checks it only uses mechanics it switched on, builds those mechanics,
    /// lets each one set itself up against the world, and saves the result as the level start.
    pub fn new(level: Level) -> Result<Sim, String> {
        // A glyph that belongs to a biome (ice '~', a boulder 'O', ...) only works if that
        // biome's mechanic is switched on. Refuse the level otherwise, instead of quietly
        // playing it with rules missing.
        for (x, y, c) in level.scan() {
            if let Some(need) = mechanic::glyph_needs(c)
                && !level.mechanics.iter().any(|m| m == need)
            {
                return Err(format!("glyph '{c}' at ({x}, {y}) needs the \"{need}\" mechanic"));
            }
        }
        if !level.ice_under.is_empty() && !level.mechanics.iter().any(|m| m == "ice") {
            return Err("\"ice\" cells need the \"ice\" mechanic".into());
        }

        let mut mechs = mechanic::for_level(&level.mechanics)?;
        let mut world = World::new(level);
        for m in &mut mechs {
            m.on_load(&mut world)?; // e.g. water computes its first flow, boulders find their rocks
        }
        world.events.clear(); // nothing said during loading reaches the players
        let start = Saved { world: world.clone(), mechs: mechs.clone() };
        Ok(Sim { world, mechs, input: [IDLE; SEATS], next_step: [0; SEATS], push_window: PUSH_WINDOW, start, checkpoint: None })
    }

    /// True once the pair has reached the summit. Nothing moves after that.
    pub fn won(&self) -> bool {
        self.world.won
    }

    /// True once the checkpoint has lit (it never unlights within an attempt).
    pub fn checkpoint_lit(&self) -> bool {
        self.checkpoint.is_some()
    }

    /// Sets the slab push window. The session calls this whenever seats change hands: one
    /// player holding both leopards gets the longer `SOLO_PUSH_WINDOW`.
    pub fn set_push_window(&mut self, ticks: u32) {
        self.push_window = ticks;
        self.world.push_window = ticks; // the boulder mechanic reads it from the world
    }

    // ------------------------------------------------------------------ restarts

    /// Back to the very start of the level, forgetting any checkpoint. Used when a new level
    /// begins or the server empties.
    pub fn restart(&mut self) {
        self.checkpoint = None;
        let start = self.start.clone();
        self.restore(start);
    }

    /// Back to the lit checkpoint if there is one, else the level start. This is the one path
    /// for a major failure and for an agreed vote, so the two always behave the same. Tells the
    /// session why (so the players hear it) and that what each one sees may have changed.
    pub fn restart_latest(&mut self, cause: RestartCause) {
        let to = self.checkpoint.clone().unwrap_or_else(|| self.start.clone());
        let checkpoint = self.checkpoint.is_some();
        self.restore(to);
        self.world.events.push(SimEvent::Restart { cause, checkpoint });
        self.world.events.push(SimEvent::Perception);
    }

    /// Swaps a saved state back in.
    fn restore(&mut self, to: Saved) {
        // Keep the messages raised this tick (such as "into the chasm."), which the saved copy
        // does not have, so the players still hear why the level restarted.
        let events = std::mem::take(&mut self.world.events);
        self.world = to.world;
        self.mechs = to.mechs;
        self.world.events = events;
        // The push window belongs to the session's current seating, not to when the save was made.
        self.world.push_window = self.push_window;
        // Keys held before the restart must not carry the leopards off on the restored map.
        self.input = [IDLE; SEATS];
        self.next_step = [0; SEATS];
    }

    /// Takes a complete copy of the level as it is now (for the checkpoint).
    fn save(&self) -> Saved {
        let mut world = self.world.clone();
        world.events.clear();
        // Restore into a calm world: nobody mid-slide, no half-finished slab carry.
        world.slide = [None; SEATS];
        world.forced.clear();
        Saved { world, mechs: self.mechs.clone() }
    }

    // ------------------------------------------------------------------ input

    /// Records a seat's input. The session calls this for whichever seat the sending client is
    /// steering. A held direction replaces the previous one; an interact press is OR-ed in and
    /// kept until the next tick uses it, so a tap is never dropped.
    pub fn set_input(&mut self, seat: usize, dir: Move, interact: bool) {
        let i = &mut self.input[seat];
        i.dir = dir;
        i.interact |= interact;
    }

    /// Clears a seat's input. Used when a player switches leopard (a key held for the leopard
    /// you let go of should not keep walking it) and when seats change hands.
    pub fn release(&mut self, seat: usize) {
        self.input[seat] = IDLE;
    }

    /// Clears every seat's input, for example when the world pauses for a dropped partner.
    pub fn release_all(&mut self) {
        self.input = [IDLE; SEATS];
    }

    // ------------------------------------------------------------------ the tick

    /// Advances the level by one tick. The numbered steps match the module documentation.
    pub fn step(&mut self, tick: u32) {
        // After a win nothing moves; drop any interact press so it does not fire on the next
        // level.
        if self.world.won {
            for i in &mut self.input {
                i.interact = false;
            }
            return;
        }

        // 1. Each seat in seat order. Seat 1 sees whatever seat 0 just did, which is what makes
        //    two players pressing keys in the same tick come out the same way every time.
        for seat in 0..SEATS {
            if self.world.slide[seat].is_some() {
                // Sliding on ice: the slide carries on by itself and input is ignored.
                self.input[seat].interact = false;
                if tick >= self.next_step[seat] {
                    self.slide_step(seat, tick);
                }
            } else {
                // Interact first: the first mechanic that knows what to do with it handles it
                // (the core pulls a lever, the water mechanic swings a sluice).
                if std::mem::take(&mut self.input[seat].interact) {
                    for m in &mut self.mechs {
                        if m.on_interact(&mut self.world, seat, tick) {
                            // A lever or sightstone can change what each player sees.
                            self.world.events.push(SimEvent::Perception);
                            break;
                        }
                    }
                }
                // Then the step, at most once every STEP_TICKS while a direction is held.
                let dir = self.input[seat].dir;
                if dir != Move::None && tick >= self.next_step[seat] {
                    self.try_move(seat, dir, tick);
                }
            }
            // 2. Settle: a fall into a chasm, or a win, ends the tick right here, so seat 1
            //    never acts on a world that has just been reset.
            if self.settle() {
                return;
            }
        }

        // 3. Every mechanic's per-tick work, after all moves, in the level's order. Each gets
        //    the others too, so it can ask whether they block a cell.
        let world = &mut self.world;
        each_with_others(&mut self.mechs, |m, others| m.on_tick(world, others, tick));

        // 4. Settle again: water swinging into a channel can sweep a leopard away.
        if self.settle() {
            return;
        }

        // 5. The checkpoint.
        self.light_checkpoint();
    }

    /// Acts on a major failure if a mechanic asked for one, and reports whether the tick must
    /// stop (after a restart, or once the pair has won).
    ///
    /// Mechanics never restart the level themselves; they set `world.major` with a reason, and
    /// this is the one place that acts on it. So a restart always happens between seats or
    /// after `on_tick`, never halfway through a mechanic.
    fn settle(&mut self) -> bool {
        if let Some(why) = self.world.major.take() {
            let to_checkpoint = self.checkpoint.is_some();
            self.world.say(why);
            self.world.say(if to_checkpoint { "back to the checkpoint." } else { "the level starts over." });
            self.restart_latest(RestartCause::Fell);
            return true;
        }
        self.world.won
    }

    // ------------------------------------------------------------------ movement

    /// One attempted step by a walking leopard.
    fn try_move(&mut self, seat: usize, dir: Move, tick: u32) {
        // Remember the direction: stepping onto ice slides you this way.
        self.world.last_dir[seat] = dir;

        // Bump first: whatever is in the way may move before we check the cell. A boulder
        // rolls on; a slab registers the push and, if the partner is pushing too, moves and
        // asks for the partner to be carried forward ("forced" moves, run next).
        let world = &mut self.world;
        each_with_others(&mut self.mechs, |m, others| m.on_bump(world, others, seat, dir, tick));
        self.run_forced(tick);
        if self.world.major.is_some() {
            return; // the partner was carried somewhere fatal; settle() deals with it
        }

        // Now the step itself, if the cell is free.
        let to = step(self.world.pos[seat], dir);
        if !self.walkable(to.0, to.1, seat) {
            return; // blocked; no cooldown, so holding the key keeps pushing every tick
        }
        self.next_step[seat] = tick + STEP_TICKS;
        self.move_seat(seat, to, tick);
    }

    /// One cell of an ice slide, or the end of it.
    fn slide_step(&mut self, seat: usize, tick: u32) {
        let dir = self.world.slide[seat].unwrap();
        let to = step(self.world.pos[seat], dir);
        if !self.walkable(to.0, to.1, seat) {
            self.world.slide[seat] = None; // stopped by a wall, a rock or the partner
            return;
        }
        self.next_step[seat] = tick + SLIDE_TICKS;
        // Entering the next cell lets the ice mechanic decide whether the slide continues
        // (more ice) or ends (anything else).
        self.move_seat(seat, to, tick);
    }

    /// Moves that a mechanic asked for on a leopard's behalf (a slab carrying its second pusher
    /// forward). They go through the same checks and hooks as a normal step.
    fn run_forced(&mut self, tick: u32) {
        for (seat, dir) in std::mem::take(&mut self.world.forced) {
            let to = step(self.world.pos[seat], dir);
            if self.walkable(to.0, to.1, seat) {
                self.move_seat(seat, to, tick);
            }
        }
    }

    /// Puts a leopard in a new cell and lets every mechanic react: first to the cell it left
    /// (a ledge collapses behind it), then to the cell it entered (a key, a pit, ice, the summit).
    fn move_seat(&mut self, seat: usize, to: (i32, i32), tick: u32) {
        let from = self.world.pos[seat];
        self.world.pos[seat] = to;
        for m in &mut self.mechs {
            m.on_leave(&mut self.world, seat, from, tick);
        }
        for m in &mut self.mechs {
            m.on_enter(&mut self.world, seat, tick);
            // If a mechanic moved the leopard (a fall back to the room's entrance) or called a
            // major failure, the cell it entered no longer matters to the others.
            if self.world.pos[seat] != to || self.world.major.is_some() {
                break;
            }
        }
    }

    /// Whether a leopard may stand in this cell right now: inside the map, not a wall or tree,
    /// not the other leopard's cell, and not blocked by any mechanic (a closed door, a rock,
    /// flowing water).
    pub fn walkable(&self, x: i32, y: i32, moving: usize) -> bool {
        let w = &self.world;
        if !w.level.in_bounds(x, y) || Level::is_solid(w.level.at(x, y)) {
            return false;
        }
        if (0..SEATS).any(|s| s != moving && w.pos[s] == (x, y)) {
            return false;
        }
        !self.mechs.iter().any(|m| m.blocks(w, x, y))
    }

    /// Lights the checkpoint once both leopards stand on or beside it, and saves the whole level
    /// there. Requiring both means a restore never strands one leopard on the wrong side of a
    /// closed door or a collapsed ledge.
    fn light_checkpoint(&mut self) {
        if self.checkpoint.is_some() {
            return; // lit once per attempt; the save is fixed from then on
        }
        let Some(c) = self.world.entities().find(|(_, e)| e.kind == Kind::Checkpoint).map(|(_, e)| (e.x, e.y)) else {
            return; // this level has no checkpoint (only finales do)
        };
        if self.world.pos.iter().all(|&p| beside(p, c)) {
            self.world.checkpoint_lit = true; // set before saving, so the save shows it lit
            self.world.say("the trail marker flares. a fall from here brings you back to it.");
            self.checkpoint = Some(self.save());
        }
    }

    // ------------------------------------------------------------------ output, for the session

    /// The state to send to every client: one row per entity (index = entity id) and the cells
    /// with flowing water. The world fills in positions; each mechanic writes its own pieces'
    /// states (a door open, a ledge cracked).
    pub fn snapshot(&self) -> (Vec<EntityWire>, Vec<(i32, i32)>) {
        let mut out = self.world.base_snapshot();
        for m in &self.mechs {
            m.write(&self.world, &mut out);
        }
        (out, self.world.water_cells())
    }

    /// What one seat is allowed to see: pits it cannot see, false keys or crevasses it does.
    /// Perception only; the rules always use the true world.
    pub fn perception(&self, seat: usize) -> Perception {
        let mut p = Perception::default();
        for m in &self.mechs {
            m.perceive(&self.world, seat, &mut p);
        }
        p
    }

    /// Hands the session everything that happened since it last asked: lines to say, a win, a
    /// restart, a change in what players see.
    pub fn drain_events(&mut self) -> Vec<SimEvent> {
        std::mem::take(&mut self.world.events)
    }
}
