//! The rules every level has: doors, levers, plates, keys and the summit gate, pits and
//! chasms, the sightstone with its hidden pits, false keys and disguised ledges, the
//! checkpoint's look, and the goal. A port of `Crawler.Mock.CoreMechanic`, extended with
//! the design doc's major failures and the sources other biomes add (rocks on plates, wheels).

use std::collections::{BTreeMap, BTreeSet, HashMap};

use crate::mechanic::Mechanic;
use crate::protocol::EntityWire;
use crate::world::{beside, Kind, Perception, SimEvent, World, SEATS};

#[derive(Default, Clone)]
pub struct Core {
    levers_on: BTreeSet<usize>,
    hidden_pits: Vec<usize>,
    disguised: Vec<usize>,
    false_keys: Vec<(i32, i32, u8)>,
    /// Door id -> the lever, plate or wheel ids that drive it.
    door_links: BTreeMap<usize, Vec<usize>>,
    linked_sources: BTreeSet<usize>,
    total_keys: usize,
    revealed: bool,
    gate_announced: bool,
}

impl Core {
    fn door_open(&self, w: &World, id: usize) -> bool {
        let door = &w.ents[id];
        let linked = self.door_links.get(&id);
        let links_on = || linked.is_some_and(|s| s.iter().any(|&s| self.source_active(w, s)));

        if door.glyph == 'F' {
            // The summit gate: every key carried, and its links (wheels, levers) if it has any.
            let mut rules = Vec::new();
            if self.total_keys > 0 {
                rules.push(w.carried.len() >= self.total_keys);
            }
            if linked.is_some() {
                rules.push(links_on());
            }
            return !rules.is_empty() && rules.iter().all(|&r| r);
        }
        // An explicit link wins over the region rule.
        if linked.is_some() {
            return links_on();
        }
        let plate_rule = w.level.rule_of(door.region) == "plate";
        let want = if plate_rule { Kind::Plate } else { Kind::Lever };
        w.entities().any(|(j, e)| {
            e.region == door.region && !self.linked_sources.contains(&j) && e.kind == want && self.source_active(w, j)
        })
    }

    fn source_active(&self, w: &World, id: usize) -> bool {
        let e = &w.ents[id];
        match e.kind {
            Kind::Lever => self.levers_on.contains(&id),
            Kind::Plate => w.seat_at(e.x, e.y) || w.rock_at(e.x, e.y).is_some(),
            Kind::Wheel => w.turning.contains(&id),
            _ => false,
        }
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
    fn name(&self) -> &'static str {
        "core"
    }

    fn clone_box(&self) -> Box<dyn Mechanic> {
        Box::new(self.clone())
    }

    fn on_load(&mut self, w: &mut World) -> Result<(), String> {
        let mut by_cell = HashMap::new();
        for (id, e) in w.entities() {
            by_cell.insert((e.x, e.y), id);
            match (e.kind, e.glyph) {
                (Kind::Key, _) => self.total_keys += 1,
                (Kind::Hazard, 'h') => self.hidden_pits.push(id),
                (Kind::Hazard, '-') => self.disguised.push(id),
                _ => {}
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
                .filter(|&s| matches!(w.ents[s].kind, Kind::Lever | Kind::Plate | Kind::Wheel))
                .ok_or_else(|| format!("link from {:?} is not a lever, plate or wheel", link.from))?;
            self.door_links.entry(door).or_default().push(src);
            self.linked_sources.insert(src);
        }
        let checkpoints = w.entities().filter(|(_, e)| e.kind == Kind::Checkpoint).count();
        if checkpoints > 1 {
            return Err(format!("a level has at most one checkpoint, found {checkpoints}"));
        }
        Ok(())
    }

    fn blocks(&self, w: &World, x: i32, y: i32) -> bool {
        w.ents_at(x, y).any(|(id, e)| e.kind == Kind::Door && !self.door_open(w, id))
    }

    /// Rocks go onto floor, plates, pits and chasms; not onto doors or other pieces.
    fn blocks_rock(&self, w: &World, x: i32, y: i32) -> bool {
        w.ents_at(x, y).any(|(id, e)| match e.kind {
            Kind::Door | Kind::Lever | Kind::Goal | Kind::Sightstone | Kind::Checkpoint => true,
            Kind::Key => !w.carried.contains_key(&id),
            _ => false,
        })
    }

    fn on_enter(&mut self, w: &mut World, seat: usize, _tick: u32) {
        let (x, y) = w.pos[seat];
        let here: Vec<usize> = w.ents_at(x, y).map(|(id, _)| id).collect();
        for id in here {
            let e = w.ents[id];
            match e.kind {
                Kind::Hazard if !w.filled.contains(&id) => {
                    if e.glyph == 'X' {
                        w.major_failure("into the chasm.");
                        return;
                    }
                    w.say(match e.glyph {
                        'h' if !self.revealed => {
                            "something unseen bites. back to the room's entrance. (a sightstone might help.)"
                        }
                        '-' if !self.revealed => "that ledge was only loose scree. back to the room's entrance.",
                        'a' | 'b' => "a crevasse. back to the room's entrance.",
                        _ => "a fall. back to the room's entrance.",
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
        let me = w.pos[seat];
        let target = w
            .entities()
            .filter(|(_, e)| beside((e.x, e.y), me))
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
            let k = e.kind as u8;
            let on = |b: bool| b as u8;
            out[id] = match e.kind {
                Kind::Door => (e.x, e.y, k, on(self.door_open(w, id)), -1),
                Kind::Lever => (e.x, e.y, k, on(self.levers_on.contains(&id)), -1),
                Kind::Plate => (e.x, e.y, k, on(self.source_active(w, id)), -1),
                Kind::Key => match w.carried.get(&id) {
                    Some(&s) => (w.pos[s].0, w.pos[s].1, k, 0, s as i8),
                    None => (e.x, e.y, k, 0, -1),
                },
                Kind::Goal => (e.x, e.y, k, on(w.won), -1),
                Kind::Sightstone => (e.x, e.y, k, on(self.revealed), -1),
                Kind::Hazard => (e.x, e.y, k, on(w.filled.contains(&id)), -1),
                Kind::Checkpoint => (e.x, e.y, k, on(w.checkpoint_lit), -1),
                _ => continue,
            };
        }
    }

    fn perceive(&self, w: &World, _seat: usize, p: &mut Perception) {
        if self.revealed {
            return;
        }
        p.hidden.extend(self.hidden_pits.iter().filter(|id| !w.filled.contains(id)).map(|&id| id as u32));
        p.substituted
            .extend(self.disguised.iter().filter(|id| !w.filled.contains(id)).map(|&id| (id as u32, Kind::Ledge as u8)));
        p.phantoms.extend(&self.false_keys);
    }
}
