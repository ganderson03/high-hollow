//! Rockfall: boulders (`O`) and heavy slabs (`[` `]` along a row, `v` `^` up a column).
//!
//! A boulder moves one cell when a leopard walks into it and the cell beyond takes a rock
//! (floor, ice, a plate, a channel; not a wall, door, other piece, rock or leopard). On ice it
//! slides like a leopard. Pushed into a pit it fills it (the pit becomes floor) and is gone;
//! into a chasm it is simply gone. A rock on a plate holds it down; in a channel it dams it.
//!
//! A slab moves only across its width, and only when both leopards push it from the same
//! side, one per half, each push live for the push window (90 ticks; longer when one player
//! holds both seats). Both pushers step forward with it. A slab never fills a pit.
//!
//! Wire: kind Boulder (9). `state` = shape (0 boulder, 1 slab along x, 2 slab along y)
//! + 4 while a push is live (straining) + 8 once gone.

use crate::mechanic::{Mechanic, Others};
use crate::protocol::{EntityWire, Move};
use crate::sim::SLIDE_TICKS;
use crate::world::{step, Kind, Rock, World};

pub const STRAIN: u8 = 4;
pub const GONE: u8 = 8;

#[derive(Debug, Clone)]
struct Push {
    rock: usize,
    seat: usize,
    half: usize,
    dir: Move,
    until: u32,
}

#[derive(Default, Clone)]
pub struct Boulders {
    pushes: Vec<Push>,
}

impl Boulders {
    fn can_enter(w: &World, others: &Others, rock: usize, x: i32, y: i32) -> bool {
        if !w.level.in_bounds(x, y) || crate::level::Level::is_solid(w.level.at(x, y)) || w.seat_at(x, y) {
            return false;
        }
        if w.rocks.iter().enumerate().any(|(i, r)| i != rock && r.covers(x, y)) {
            return false;
        }
        // Slabs are too broad to drop into a pit.
        let slab = w.rocks[rock].shape != 0;
        if slab && w.ents_at(x, y).any(|(id, e)| e.kind == Kind::Hazard && !w.filled.contains(&id)) {
            return false;
        }
        !others.blocks_rock(w, x, y)
    }

    /// Moves a rock one cell, then settles it: into a pit, a chasm, or onto ice.
    fn shift(w: &mut World, rock: usize, dir: Move, tick: u32) {
        let (x, y) = step((w.rocks[rock].x, w.rocks[rock].y), dir);
        w.rocks[rock].x = x;
        w.rocks[rock].y = y;
        w.rocks[rock].slide = None;
        if w.rocks[rock].shape == 0 {
            let pit = w
                .ents_at(x, y)
                .find(|(id, e)| e.kind == Kind::Hazard && !w.filled.contains(id))
                .map(|(id, e)| (id, e.glyph));
            match pit {
                Some((_, 'X')) => {
                    w.rocks[rock].gone = true;
                    w.say("the boulder tumbles into the chasm.");
                    return;
                }
                Some((id, _)) => {
                    w.rocks[rock].gone = true;
                    w.filled.insert(id);
                    w.say("the boulder drops into the pit and fills it.");
                    return;
                }
                None => {}
            }
            if w.level.is_ice(x, y) {
                w.rocks[rock].slide = Some(dir);
                w.rocks[rock].next = tick + SLIDE_TICKS;
            }
        }
    }

    fn straining(&self, rock: usize) -> bool {
        self.pushes.iter().any(|p| p.rock == rock)
    }
}

/// Which way a slab can move: across its width.
fn across(shape: u8, dir: Move) -> bool {
    match shape {
        1 => matches!(dir, Move::N | Move::S),
        2 => matches!(dir, Move::E | Move::W),
        _ => false,
    }
}

impl Mechanic for Boulders {
    fn name(&self) -> &'static str {
        "boulders"
    }

    fn clone_box(&self) -> Box<dyn Mechanic> {
        Box::new(self.clone())
    }

    fn on_load(&mut self, w: &mut World) -> Result<(), String> {
        let mut rocks = Vec::new();
        let lvl = &w.level;
        for (id, e) in w.entities() {
            if e.kind != Kind::Boulder {
                continue;
            }
            let shape = match e.glyph {
                '[' if lvl.at(e.x + 1, e.y) == ']' => 1,
                'v' if lvl.at(e.x, e.y + 1) == '^' => 2,
                'O' => 0,
                g => return Err(format!("slab half '{g}' at ({}, {}) has no partner", e.x, e.y)),
            };
            rocks.push(Rock { id, x: e.x, y: e.y, shape, gone: false, slide: None, next: 0 });
        }
        w.rocks = rocks;
        let lvl = &w.level;
        for (x, y, c) in lvl.scan() {
            let ok = match c {
                ']' => lvl.at(x - 1, y) == '[',
                '^' => lvl.at(x, y - 1) == 'v',
                _ => true,
            };
            if !ok {
                return Err(format!("slab half '{c}' at ({x}, {y}) has no partner"));
            }
        }
        Ok(())
    }

    fn blocks(&self, w: &World, x: i32, y: i32) -> bool {
        w.rock_at(x, y).is_some()
    }

    fn blocks_rock(&self, _w: &World, _x: i32, _y: i32) -> bool {
        false // rocks check each other in can_enter
    }

    fn on_bump(&mut self, w: &mut World, others: &Others, seat: usize, dir: Move, tick: u32) {
        let target = step(w.pos[seat], dir);
        let Some(ri) = w.rock_at(target.0, target.1) else { return };
        if w.rocks[ri].slide.is_some() {
            return;
        }
        let shape = w.rocks[ri].shape;

        if shape == 0 {
            let (bx, by) = step(target, dir);
            if Self::can_enter(w, others, ri, bx, by) {
                Self::shift(w, ri, dir, tick);
            }
            return;
        }

        if !across(shape, dir) {
            return; // too long to shove end-on
        }
        let half = w.rocks[ri].cells().iter().position(|&c| c == target).unwrap();
        let fresh = !self.pushes.iter().any(|p| p.rock == ri && p.seat == seat && p.dir == dir);
        self.pushes.retain(|p| !(p.rock == ri && p.seat == seat));
        let until = tick + w.push_window;
        self.pushes.push(Push { rock: ri, seat, half, dir, until });

        let partner = self
            .pushes
            .iter()
            .find(|p| p.rock == ri && p.seat != seat && p.half != half && p.dir == dir && p.until >= tick)
            .map(|p| p.seat);
        let Some(partner) = partner else {
            if fresh {
                w.say("the slab strains. it needs a second shove, beside you.");
            }
            return;
        };

        let dests: Vec<_> = w.rocks[ri].cells().iter().map(|&c| step(c, dir)).collect();
        if dests.iter().all(|&(x, y)| Self::can_enter(w, others, ri, x, y)) {
            Self::shift(w, ri, dir, tick);
            self.pushes.retain(|p| p.rock != ri);
            // The pusher calling us steps forward by its own move; carry the partner too.
            w.forced.push((partner, dir));
            w.say("the slab grinds forward.");
        }
    }

    fn on_tick(&mut self, w: &mut World, others: &Others, tick: u32) {
        self.pushes.retain(|p| p.until >= tick);
        for ri in 0..w.rocks.len() {
            let r = &w.rocks[ri];
            let Some(dir) = r.slide else { continue };
            if r.gone || tick < r.next {
                continue;
            }
            let (x, y) = step((r.x, r.y), dir);
            if Self::can_enter(w, others, ri, x, y) {
                Self::shift(w, ri, dir, tick);
            } else {
                w.rocks[ri].slide = None;
            }
        }
    }

    fn write(&self, _w: &World, out: &mut [EntityWire]) {
        for (ri, r) in _w.rocks.iter().enumerate() {
            let mut state = r.shape;
            if self.straining(ri) {
                state += STRAIN;
            }
            if r.gone {
                state += GONE;
            }
            out[r.id] = (r.x, r.y, Kind::Boulder as u8, state, -1);
        }
    }
}
