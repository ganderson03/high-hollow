//! Creeklands: water from sources (`W`) through channels (`w`), swung at sluices (`V`), turning
//! mill wheels (`M`).
//!
//! Flow is recomputed from the sources every tick, never patched. Water spreads to every
//! neighbouring channel cell, except out of a sluice, where it leaves only by the sluice's open
//! exit. A rock in a channel dams it. Water reaching a wheel turns it, and a turning wheel
//! drives its linked door or the summit gate (links from a wheel, like a lever's).
//!
//! Flowing water cannot be entered; a dry bed can. A leopard standing in a channel when water
//! arrives is swept away: a major failure. Sources, sluices and wheels are never walkable.
//!
//! A sluice's inlet is the side water first reaches it from (found at load with every sluice
//! open); its exit cycles through the other sides that lead somewhere, starting with the
//! first of north, east, south, west. Wire: Sluice (10) `state` = open exit, 0 north, 1 east,
//! 2 south, 3 west; Wheel (11) `state` 1 = turning; flowing cells in the snapshot's `water`.

use std::collections::{BTreeMap, BTreeSet, VecDeque};

use crate::mechanic::{Mechanic, Others};
use crate::protocol::{EntityWire, Move};
use crate::world::{beside, step, Kind, World, SEATS};

const DIRS: [Move; 4] = [Move::N, Move::E, Move::S, Move::W];

#[derive(Debug, Clone)]
struct Sluice {
    at: (i32, i32),
    inlet: Option<usize>,
    exit: usize,
}

/// Wet cells, turning wheels, and the direction index each wet cell was reached by.
type Flow = (BTreeSet<(i32, i32)>, BTreeSet<usize>, BTreeMap<(i32, i32), usize>);

#[derive(Default, Clone)]
pub struct Water {
    sources: Vec<(i32, i32)>,
    sluices: BTreeMap<usize, Sluice>,
    wheels: BTreeMap<(i32, i32), usize>,
    warned: bool,
}

fn conducts(c: char) -> bool {
    matches!(c, 'w' | 'V' | 'M' | 'W')
}

impl Water {
    fn sluice_at(&self, cell: (i32, i32)) -> Option<&Sluice> {
        self.sluices.values().find(|s| s.at == cell)
    }

    /// Breadth-first from every source. With `all_open`, sluices pass water every way (used at
    /// load to find each sluice's inlet). Returns the wet cells, the turning wheels, and for
    /// each cell the direction index it was reached by.
    fn flow(&self, w: &World, all_open: bool) -> Flow {
        let mut wet = BTreeSet::new();
        let mut turning = BTreeSet::new();
        let mut came = BTreeMap::new();
        let mut q = VecDeque::new();
        for &s in &self.sources {
            if wet.insert(s) {
                q.push_back(s);
            }
        }
        while let Some(c) = q.pop_front() {
            let exits: Vec<usize> = match self.sluice_at(c) {
                Some(s) if !all_open => vec![s.exit],
                _ => (0..4).collect(),
            };
            for d in exits {
                let n = step(c, DIRS[d]);
                if !conducts(w.level.at(n.0, n.1)) || wet.contains(&n) || w.rock_at(n.0, n.1).is_some() {
                    continue;
                }
                if let Some(&wheel) = self.wheels.get(&n) {
                    turning.insert(wheel);
                    continue;
                }
                wet.insert(n);
                came.insert(n, d);
                q.push_back(n);
            }
        }
        (wet, turning, came)
    }

    /// The exits a sluice can swing to: sides other than its inlet that lead into the creek.
    fn exits(&self, w: &World, s: &Sluice) -> Vec<usize> {
        (0..4)
            .filter(|&d| Some(d) != s.inlet)
            .filter(|&d| {
                let n = step(s.at, DIRS[d]);
                conducts(w.level.at(n.0, n.1)) && w.level.at(n.0, n.1) != 'W'
            })
            .collect()
    }

    fn recompute(&self, w: &mut World) {
        let (wet, turning, _) = self.flow(w, false);
        w.water = wet;
        w.turning = turning;
    }
}

impl Mechanic for Water {
    fn name(&self) -> &'static str {
        "water"
    }

    fn clone_box(&self) -> Box<dyn Mechanic> {
        Box::new(self.clone())
    }

    fn on_load(&mut self, w: &mut World) -> Result<(), String> {
        for (x, y, c) in w.level.scan() {
            if c == 'W' {
                self.sources.push((x, y));
            }
        }
        for (id, e) in w.entities() {
            match e.kind {
                Kind::Sluice => {
                    self.sluices.insert(id, Sluice { at: (e.x, e.y), inlet: None, exit: 0 });
                }
                Kind::Wheel => {
                    self.wheels.insert((e.x, e.y), id);
                }
                _ => {}
            }
        }
        if self.sources.is_empty() && !(self.sluices.is_empty() && self.wheels.is_empty()) {
            return Err("a level with sluices or wheels needs a source 'W'".into());
        }

        // Each sluice's inlet: the opposite of the direction water first reached it by.
        let (_, _, came) = self.flow(w, true);
        let ids: Vec<usize> = self.sluices.keys().copied().collect();
        for id in ids {
            let at = self.sluices[&id].at;
            let inlet = came.get(&at).map(|&d| (d + 2) % 4);
            self.sluices.get_mut(&id).unwrap().inlet = inlet;
            let first = self.exits(w, &self.sluices[&id]).first().copied().unwrap_or(0);
            self.sluices.get_mut(&id).unwrap().exit = first;
        }
        self.recompute(w);
        Ok(())
    }

    fn blocks(&self, w: &World, x: i32, y: i32) -> bool {
        matches!(w.level.at(x, y), 'W' | 'V' | 'M') || w.water.contains(&(x, y))
    }

    /// A rock may go into a channel, wet or dry (a dam), but not onto the machinery.
    fn blocks_rock(&self, w: &World, x: i32, y: i32) -> bool {
        matches!(w.level.at(x, y), 'W' | 'V' | 'M')
    }

    fn on_interact(&mut self, w: &mut World, seat: usize, _tick: u32) -> bool {
        let me = w.pos[seat];
        let Some((&id, s)) = self.sluices.iter().find(|(_, s)| beside(s.at, me)) else { return false };
        let exits = self.exits(w, s);
        if exits.len() < 2 {
            w.say("the sluice will not swing; there is nowhere else for the water to go.");
            return true;
        }
        let i = exits.iter().position(|&d| d == s.exit).map_or(0, |i| (i + 1) % exits.len());
        self.sluices.get_mut(&id).unwrap().exit = exits[i];
        if self.warned {
            w.say("the sluice swings.");
        } else {
            self.warned = true;
            w.say("the sluice swings and the water turns. anyone standing in its new channel will be swept away.");
        }
        true
    }

    fn on_tick(&mut self, w: &mut World, _others: &Others, _tick: u32) {
        self.recompute(w);
        for s in 0..SEATS {
            if w.water.contains(&w.pos[s]) {
                w.major_failure("swept away by the water.");
                return;
            }
        }
    }

    fn write(&self, w: &World, out: &mut [EntityWire]) {
        for (&id, s) in &self.sluices {
            out[id] = (s.at.0, s.at.1, Kind::Sluice as u8, s.exit as u8, -1);
        }
        for (&at, &id) in &self.wheels {
            out[id] = (at.0, at.1, Kind::Wheel as u8, w.turning.contains(&id) as u8, -1);
        }
    }
}
