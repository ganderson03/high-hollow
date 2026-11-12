//! One level's simulation, with no clock and no clients: the caller advances it tick by tick
//! and says which seat each input belongs to. Deterministic and testable on its own.
//! Mirrors `Crawler.Mock.MockSim`, plus both seats acting in one tick (two players), ice
//! slides, major failures and the checkpoint.
//!
//! A tick: each seat in seat order interacts, then steps or slides; then every mechanic's
//! `on_tick`; then a major failure, if any, restarts the level (or restores the lit
//! checkpoint); then the checkpoint lights if both leopards stand on or beside it.

use crate::level::Level;
use crate::mechanic::{self, each_with_others, Mechanic};
use crate::protocol::{EntityWire, Move};
use crate::world::{beside, step, Kind, Perception, RestartCause, SimEvent, World, SEATS};

/// One cell per 150 ms while a direction is held, at 60 Hz.
pub const STEP_TICKS: u32 = 9;
/// One cell per ~67 ms while sliding on ice.
pub const SLIDE_TICKS: u32 = 4;
/// A slab push stays live this long (1.5 s)...
pub const PUSH_WINDOW: u32 = 90;
/// ...or this long when one player holds both seats and has to press Tab in between.
pub const SOLO_PUSH_WINDOW: u32 = 240;

#[derive(Debug, Clone, Copy)]
struct SeatInput {
    dir: Move,
    interact: bool,
}

const IDLE: SeatInput = SeatInput { dir: Move::None, interact: false };

/// The whole level state: the world and every mechanic's own state.
#[derive(Clone)]
struct Saved {
    world: World,
    mechs: Vec<Box<dyn Mechanic>>,
}

pub struct Sim {
    pub world: World,
    mechs: Vec<Box<dyn Mechanic>>,
    input: [SeatInput; SEATS],
    next_step: [u32; SEATS],
    push_window: u32,
    start: Saved,
    checkpoint: Option<Saved>,
}

impl Sim {
    pub fn new(level: Level) -> Result<Sim, String> {
        // Every glyph outside the core needs its mechanic switched on.
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
            m.on_load(&mut world)?;
        }
        world.events.clear();
        let start = Saved { world: world.clone(), mechs: mechs.clone() };
        Ok(Sim { world, mechs, input: [IDLE; SEATS], next_step: [0; SEATS], push_window: PUSH_WINDOW, start, checkpoint: None })
    }

    pub fn won(&self) -> bool {
        self.world.won
    }

    pub fn checkpoint_lit(&self) -> bool {
        self.checkpoint.is_some()
    }

    /// Slab push window: longer while one player steers both leopards.
    pub fn set_push_window(&mut self, ticks: u32) {
        self.push_window = ticks;
        self.world.push_window = ticks;
    }

    /// Back to the very start of the level, forgetting any checkpoint.
    pub fn restart(&mut self) {
        self.checkpoint = None;
        let start = self.start.clone();
        self.restore(start);
    }

    /// Back to the lit checkpoint, or the level start. Used by a major failure and the vote.
    pub fn restart_latest(&mut self, cause: RestartCause) {
        let to = self.checkpoint.clone().unwrap_or_else(|| self.start.clone());
        let checkpoint = self.checkpoint.is_some();
        self.restore(to);
        self.world.events.push(SimEvent::Restart { cause, checkpoint });
        self.world.events.push(SimEvent::Perception);
    }

    fn restore(&mut self, to: Saved) {
        let events = std::mem::take(&mut self.world.events);
        self.world = to.world;
        self.mechs = to.mechs;
        self.world.events = events;
        self.world.push_window = self.push_window;
        self.input = [IDLE; SEATS];
        self.next_step = [0; SEATS];
    }

    fn save(&self) -> Saved {
        let mut world = self.world.clone();
        world.events.clear();
        world.slide = [None; SEATS];
        world.forced.clear();
        Saved { world, mechs: self.mechs.clone() }
    }

    // ------------------------------------------------------------------ input

    /// A held direction replaces the last; an interact press is kept until the next step uses it.
    pub fn set_input(&mut self, seat: usize, dir: Move, interact: bool) {
        let i = &mut self.input[seat];
        i.dir = dir;
        i.interact |= interact;
    }

    /// A held key belongs to the leopard you let go of.
    pub fn release(&mut self, seat: usize) {
        self.input[seat] = IDLE;
    }

    pub fn release_all(&mut self) {
        self.input = [IDLE; SEATS];
    }

    // ------------------------------------------------------------------ simulation

    pub fn step(&mut self, tick: u32) {
        if self.world.won {
            for i in &mut self.input {
                i.interact = false;
            }
            return;
        }
        for seat in 0..SEATS {
            if self.world.slide[seat].is_some() {
                // Input is ignored while sliding; the slide runs on its own.
                self.input[seat].interact = false;
                if tick >= self.next_step[seat] {
                    self.slide_step(seat, tick);
                }
            } else {
                if std::mem::take(&mut self.input[seat].interact) {
                    for m in &mut self.mechs {
                        if m.on_interact(&mut self.world, seat, tick) {
                            self.world.events.push(SimEvent::Perception);
                            break;
                        }
                    }
                }
                let dir = self.input[seat].dir;
                if dir != Move::None && tick >= self.next_step[seat] {
                    self.try_move(seat, dir, tick);
                }
            }
            if self.settle() {
                return;
            }
        }

        let world = &mut self.world;
        each_with_others(&mut self.mechs, |m, others| m.on_tick(world, others, tick));
        if self.settle() {
            return;
        }
        self.light_checkpoint();
    }

    /// Handles a major failure or a win. True if the tick should stop here.
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

    fn try_move(&mut self, seat: usize, dir: Move, tick: u32) {
        self.world.last_dir[seat] = dir;
        // Something in the way may move first (a boulder, a slab with its other pusher).
        let world = &mut self.world;
        each_with_others(&mut self.mechs, |m, others| m.on_bump(world, others, seat, dir, tick));
        self.run_forced(tick);
        if self.world.major.is_some() {
            return;
        }

        let to = step(self.world.pos[seat], dir);
        if !self.walkable(to.0, to.1, seat) {
            return;
        }
        self.next_step[seat] = tick + STEP_TICKS;
        self.move_seat(seat, to, tick);
    }

    fn slide_step(&mut self, seat: usize, tick: u32) {
        let dir = self.world.slide[seat].unwrap();
        let to = step(self.world.pos[seat], dir);
        if !self.walkable(to.0, to.1, seat) {
            self.world.slide[seat] = None; // stopped by a wall, a rock or the partner
            return;
        }
        self.next_step[seat] = tick + SLIDE_TICKS;
        self.move_seat(seat, to, tick);
    }

    fn run_forced(&mut self, tick: u32) {
        for (seat, dir) in std::mem::take(&mut self.world.forced) {
            let to = step(self.world.pos[seat], dir);
            if self.walkable(to.0, to.1, seat) {
                self.move_seat(seat, to, tick);
            }
        }
    }

    fn move_seat(&mut self, seat: usize, to: (i32, i32), tick: u32) {
        let from = self.world.pos[seat];
        self.world.pos[seat] = to;
        for m in &mut self.mechs {
            m.on_leave(&mut self.world, seat, from, tick);
        }
        for m in &mut self.mechs {
            m.on_enter(&mut self.world, seat, tick);
            if self.world.pos[seat] != to || self.world.major.is_some() {
                break; // a fall moved the leopard; nothing else happens here
            }
        }
    }

    /// Inside the map, not a wall or tree, not held by a mechanic, and free of the other leopard.
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

    /// The checkpoint lights once, when both leopards stand on or beside it, and records the
    /// whole level state then.
    fn light_checkpoint(&mut self) {
        if self.checkpoint.is_some() {
            return;
        }
        let Some(c) = self.world.entities().find(|(_, e)| e.kind == Kind::Checkpoint).map(|(_, e)| (e.x, e.y)) else {
            return;
        };
        if self.world.pos.iter().all(|&p| beside(p, c)) {
            self.world.checkpoint_lit = true;
            self.world.say("the trail marker flares. a fall from here brings you back to it.");
            self.checkpoint = Some(self.save());
        }
    }

    // ------------------------------------------------------------------ output

    /// Entity rows and flowing cells.
    pub fn snapshot(&self) -> (Vec<EntityWire>, Vec<(i32, i32)>) {
        let mut out = self.world.base_snapshot();
        for m in &self.mechs {
            m.write(&self.world, &mut out);
        }
        (out, self.world.water_cells())
    }

    /// What the given seat sees.
    pub fn perception(&self, seat: usize) -> Perception {
        let mut p = Perception::default();
        for m in &self.mechs {
            m.perceive(&self.world, seat, &mut p);
        }
        p
    }

    pub fn drain_events(&mut self) -> Vec<SimEvent> {
        std::mem::take(&mut self.world.events)
    }
}
