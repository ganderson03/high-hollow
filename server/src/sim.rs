//! One level's simulation, with no clock and no clients: the caller advances it tick by tick
//! and says which seat each input belongs to. Deterministic and testable on its own.
//! Mirrors `Crawler.Mock.MockSim`, except that both seats can act in the same tick (two
//! players), processed in seat order.

use crate::level::Level;
use crate::mechanic::{self, Mechanic};
use crate::protocol::{EntityWire, Move};
use crate::world::{Perception, SimEvent, World, SEATS};

/// One cell per 150 ms while a direction is held, at 60 Hz.
pub const STEP_TICKS: u32 = 9;

#[derive(Debug, Clone, Copy)]
struct SeatInput {
    dir: Move,
    interact: bool,
}

pub struct Sim {
    pub world: World,
    mechs: Vec<Box<dyn Mechanic>>,
    input: [SeatInput; SEATS],
    next_step: [u32; SEATS],
}

impl Sim {
    pub fn new(level: Level) -> Result<Sim, String> {
        let mut mechs = mechanic::for_level(&level.mechanics)?;
        let world = World::new(level);
        for m in &mut mechs {
            m.on_load(&world)?;
        }
        let idle = SeatInput { dir: Move::None, interact: false };
        Ok(Sim { world, mechs, input: [idle; SEATS], next_step: [0; SEATS] })
    }

    pub fn won(&self) -> bool {
        self.world.won
    }

    /// Put the level back to its start: both leopards home, no keys, every mechanic reset.
    pub fn restart(&mut self) {
        let w = &mut self.world;
        w.pos = w.spawn;
        w.carried.clear();
        w.won = false;
        for s in 0..SEATS {
            self.next_step[s] = 0;
            self.input[s] = SeatInput { dir: Move::None, interact: false };
        }
        for m in &mut self.mechs {
            m.reset(&self.world);
        }
    }

    /// A held direction replaces the last; an interact press is kept until the next step uses it.
    pub fn set_input(&mut self, seat: usize, dir: Move, interact: bool) {
        let i = &mut self.input[seat];
        i.dir = dir;
        i.interact |= interact;
    }

    /// A held key belongs to the leopard you let go of.
    pub fn release(&mut self, seat: usize) {
        self.input[seat] = SeatInput { dir: Move::None, interact: false };
    }

    pub fn step(&mut self, tick: u32) {
        if self.world.won {
            for i in &mut self.input {
                i.interact = false;
            }
            return;
        }
        for seat in 0..SEATS {
            if std::mem::take(&mut self.input[seat].interact) {
                for m in &mut self.mechs {
                    if m.on_interact(&mut self.world, seat, tick) {
                        self.world.events.push(SimEvent::Perception);
                        break;
                    }
                }
            }
            if self.input[seat].dir != Move::None && tick >= self.next_step[seat] {
                self.try_move(seat, tick);
            }
            if self.world.won {
                break;
            }
        }
    }

    fn try_move(&mut self, seat: usize, tick: u32) {
        let (dx, dy) = self.input[seat].dir.delta();
        let (x, y) = (self.world.pos[seat].0 + dx, self.world.pos[seat].1 + dy);
        if !self.walkable(x, y, seat) {
            return;
        }
        self.world.pos[seat] = (x, y);
        self.next_step[seat] = tick + STEP_TICKS;
        for m in &mut self.mechs {
            m.on_enter(&mut self.world, seat, tick);
            if self.world.pos[seat] != (x, y) {
                break; // a fall moved the leopard; nothing else happens here
            }
        }
    }

    /// Inside the map, not a wall or tree, not held by a mechanic, and free of the other leopard.
    pub fn walkable(&self, x: i32, y: i32, moving: usize) -> bool {
        let w = &self.world;
        if !w.level.in_bounds(x, y) || crate::level::Level::is_solid(w.level.at(x, y)) {
            return false;
        }
        if (0..SEATS).any(|s| s != moving && w.pos[s] == (x, y)) {
            return false;
        }
        !self.mechs.iter().any(|m| m.blocks(w, x, y))
    }

    pub fn snapshot(&self) -> Vec<EntityWire> {
        let mut out = self.world.base_snapshot();
        for m in &self.mechs {
            m.write(&self.world, &mut out);
        }
        out
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
