//! The rules every level has: doors, levers, plates, keys and the summit gate, pits (minor
//! failures), the sightstone with its hidden pits and false keys, and the goal.
//! A port of `Crawler.Mock.CoreMechanic`; the design doc is the spec, the mock the reference.

use std::collections::{BTreeMap, BTreeSet, HashMap};

use crate::mechanic::Mechanic;
use crate::protocol::EntityWire;
use crate::world::{Kind, Perception, SimEvent, World, SEATS};

#[derive(Default)]
pub struct Core {
    levers_on: BTreeSet<usize>,
    hidden_pits: Vec<u32>,
    false_keys: Vec<(i32, i32, u8)>,
    /// Door id -> the lever or plate ids that drive it.
    door_links: BTreeMap<usize, Vec<usize>>,
    linked_sources: BTreeSet<usize>,
    total_keys: usize,
    revealed: bool,
    gate_announced: bool,
}

impl Core {
    fn door_open(&self, w: &World, id: usize) -> bool {
        let door = &w.ents[id];
        if door.glyph == 'F' {
            return self.total_keys > 0 && w.carried.len() >= self.total_keys;
        }
        // An explicit link wins over the region rule.
        if let Some(sources) = self.door_links.get(&id) {
            return sources.iter().any(|&s| self.source_active(w, s));
        }
        let plate_rule = w.level.rule_of(door.region) == "plate";
        let want = if plate_rule { Kind::Plate } else { Kind::Lever };
        w.entities().any(|(j, e)| {
            e.region == door.region && !self.linked_sources.contains(&j) && e.kind == want && self.source_active(w, j)
        })
    }

    fn source_active(&self, w: &World, id: usize) -> bool {
        let e = &w.ents[id];
        if e.kind == Kind::Lever { self.levers_on.contains(&id) } else { w.seat_at(e.x, e.y) }
    }

    /// The pair wins together: one leopard on the summit marker and the other anywhere in the
    /// summit's room (or within two cells of it, if the level gives it no room).
    fn check_summit(&self, w: &mut World, seat: usize) {
        if w.won {
            return;
        }
        let goals: Vec<_> = w.entities().filter(|(_, e)| e.kind == Kind::Goal).map(|(_, e)| *e).collect();
        for g in goals {
            for s in 0..SEATS {
                if w.pos[s] != (g.x, g.y) {
                    continue;
                }
                let other = w.pos[1 - s];
                let near = if g.region >= 0 {
                    w.level.region_at(other.0, other.1) == g.region
                } else {
                    (other.0 - g.x).abs() <= 2 && (other.1 - g.y).abs() <= 2
                };
                if !near {
                    if s == seat {
                        w.say("the summit, but alone. your partner needs to be here too.");
                    }
                    return;
                }
                w.won = true;
                w.say("the summit, together!");
                w.events.push(SimEvent::Won);
                return;
            }
        }
    }
}

fn number(n: usize) -> String {
    match n {
        2 => "two".into(),
        3 => "three".into(),
        4 => "four".into(),
        _ => n.to_string(),
    }
}

impl Mechanic for Core {
    fn on_load(&mut self, w: &World) -> Result<(), String> {
        let mut by_cell = HashMap::new();
        for (id, e) in w.entities() {
            by_cell.insert((e.x, e.y), id);
            if e.kind == Kind::Key {
                self.total_keys += 1;
            }
            if e.kind == Kind::Hazard && e.glyph == 'h' {
                self.hidden_pits.push(id as u32);
            }
        }
        for (x, y, c) in w.level.scan() {
            if c == 'k' {
                self.false_keys.push((x, y, Kind::Key as u8));
            }
        }
        for link in &w.level.links {
            let door = by_cell
                .get(&link.to)
                .copied()
                .filter(|&d| w.ents[d].kind == Kind::Door)
                .ok_or_else(|| format!("link to {:?} does not point at a door", link.to))?;
            let src = by_cell
                .get(&link.from)
                .copied()
                .filter(|&s| matches!(w.ents[s].kind, Kind::Lever | Kind::Plate))
                .ok_or_else(|| format!("link from {:?} is not a lever or plate", link.from))?;
            self.door_links.entry(door).or_default().push(src);
            self.linked_sources.insert(src);
        }
        Ok(())
    }

    fn reset(&mut self, _w: &World) {
        self.levers_on.clear();
        self.revealed = false;
        self.gate_announced = false;
    }

    fn blocks(&self, w: &World, x: i32, y: i32) -> bool {
        w.entities().any(|(id, e)| e.kind == Kind::Door && (e.x, e.y) == (x, y) && !self.door_open(w, id))
    }

    fn on_enter(&mut self, w: &mut World, seat: usize, _tick: u32) {
        let (x, y) = w.pos[seat];
        let here: Vec<usize> = w.entities().filter(|(_, e)| (e.x, e.y) == (x, y)).map(|(id, _)| id).collect();
        for id in here {
            let e = w.ents[id];
            match e.kind {
                Kind::Hazard => {
                    w.say(if e.glyph == 'h' && !self.revealed {
                        "something unseen bites. back to the room's entrance. (a sightstone might help.)"
                    } else {
                        "a fall. back to the room's entrance."
                    });
                    w.minor_failure(seat, x, y);
                    if w.carried.len() < self.total_keys {
                        self.gate_announced = false;
                    }
                    return;
                }
                Kind::Key if !w.carried.contains_key(&id) => {
                    w.carried.insert(id, seat);
                    w.say(format!("key {} of {}.", w.carried.len(), self.total_keys));
                    if w.carried.len() >= self.total_keys && !self.gate_announced {
                        self.gate_announced = true;
                        w.say(if self.total_keys == 1 {
                            "the summit gate answers the key.".to_string()
                        } else {
                            format!("the summit gate answers the {} keys.", number(self.total_keys))
                        });
                    }
                }
                _ => {}
            }
        }
        self.check_summit(w, seat);
    }

    fn on_interact(&mut self, w: &mut World, seat: usize, _tick: u32) -> bool {
        let (px, py) = w.pos[seat];
        let target = w
            .entities()
            .filter(|(_, e)| (e.x - px).abs() <= 1 && (e.y - py).abs() <= 1)
            .find(|(_, e)| e.kind == Kind::Lever || (e.kind == Kind::Sightstone && !self.revealed))
            .map(|(id, e)| (id, e.kind));
        match target {
            Some((id, Kind::Lever)) => {
                let on = self.levers_on.insert(id);
                if !on {
                    self.levers_on.remove(&id);
                }
                w.say(if on { "*clunk*. stone grinds open nearby." } else { "*clunk*. a door slides shut." });
                true
            }
            Some((_, Kind::Sightstone)) => {
                self.revealed = true;
                w.say("the sightstone hums. hidden pits show their teeth, and any false key fades.");
                true
            }
            _ => false,
        }
    }

    fn write(&self, w: &World, out: &mut [EntityWire]) {
        for (id, e) in w.entities() {
            let state = |on: bool| on as u8;
            out[id] = match e.kind {
                Kind::Door => (e.x, e.y, e.kind as u8, state(self.door_open(w, id)), -1),
                Kind::Lever => (e.x, e.y, e.kind as u8, state(self.levers_on.contains(&id)), -1),
                Kind::Plate => (e.x, e.y, e.kind as u8, state(w.seat_at(e.x, e.y)), -1),
                Kind::Key => match w.carried.get(&id) {
                    Some(&s) => (w.pos[s].0, w.pos[s].1, e.kind as u8, 0, s as i8),
                    None => (e.x, e.y, e.kind as u8, 0, -1),
                },
                Kind::Goal => (e.x, e.y, e.kind as u8, state(w.won), -1),
                Kind::Sightstone => (e.x, e.y, e.kind as u8, state(self.revealed), -1),
                Kind::Hazard | Kind::Player => (e.x, e.y, e.kind as u8, 0, -1),
            };
        }
    }

    fn perceive(&self, _w: &World, _seat: usize, p: &mut Perception) {
        if self.revealed {
            return;
        }
        p.hidden.extend(&self.hidden_pits);
        p.phantoms.extend(&self.false_keys);
    }
}
