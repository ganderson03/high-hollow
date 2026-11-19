//! The rules every level has: doors, levers, plates, keys and the summit gate, pits and
//! chasms, the sightstone with its hidden pits, false keys and disguised ledges, the
//! checkpoint's look, and the goal. A port of `Crawler.Mock.CoreMechanic`, extended with
//! the design doc's major failures and the sources other biomes add (rocks on plates, wheels).
//!
//! Most state here is *derived*, not stored: whether a door is open is worked out from its
//! levers, plates or wheels every time it is asked, so it can never fall out of step with them.
//! Only the things a player changes directly are stored: which levers are on, and which
//! leopards have used the sightstone.

use std::collections::{BTreeMap, BTreeSet, HashMap};

use crate::mechanic::Mechanic;
use crate::protocol::EntityWire;
use crate::world::{beside, Kind, Perception, SimEvent, World, SEATS};

/// The core rules' own state. Cloned whole for a save, like every mechanic.
/// (`BTree` collections rather than hash maps: their order is fixed, so iterating them is
/// deterministic.)
#[derive(Default, Clone)]
pub struct Core {
    /// Lever ids currently pulled on.
    levers_on: BTreeSet<usize>,
    /// Hidden pits (`h`): real, but missing from a leopard's overlay until it uses the sightstone.
    hidden_pits: Vec<usize>,
    /// Pits dressed as ledges (`-`): drawn as a ledge until the sightstone is used.
    disguised: Vec<usize>,
    /// False keys (`k`): drawn as keys until the sightstone is used; not entities at all.
    false_keys: Vec<(i32, i32, u8)>,
    /// Door id -> the lever, plate or wheel ids that drive it.
    door_links: BTreeMap<usize, Vec<usize>>,
    /// Every lever, plate or wheel that drives a linked door, so it does not also open its
    /// region's other doors.
    linked_sources: BTreeSet<usize>,
    /// Keys in the level; the summit gate needs them all carried.
    total_keys: usize,
    /// Per seat: that leopard has used the sightstone, so hidden things show and false things
    /// fade for it alone. Its partner still sees the lie until it uses the stone too.
    revealed: [bool; SEATS],
    /// "The summit gate answers the keys" has been said, so it is not repeated every step.
    gate_announced: bool,
}

impl Core {
    /// Whether a door is open right now. Three rules, in order:
    /// 1. the summit gate `F` opens when every key is carried, and its links too if it has any;
    /// 2. a door with links opens while any of its sources is active;
    /// 3. otherwise its region's rule decides: in a "plate" region any plate there opens it,
    ///    elsewhere any lever there does.
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

    /// Whether a door's source is on: a lever that has been pulled, a plate with a leopard or a
    /// rock standing on it, or a wheel that water is reaching.
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
            // Either leopard may be the one on the marker; the other is "the partner".
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
                    // Only say it to the leopard that just arrived, not every step its partner takes.
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

/// Small numbers as words, for the chat lines ("the summit gate answers the two keys").
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

    /// Sorts the level's pieces into what the core tracks, and resolves every link from the
    /// map coordinates in the file to entity ids. A link to something that is not a door, or
    /// from something that is not a lever, plate or wheel, is a broken level and is refused.
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

    /// The core blocks only one thing: a closed door.
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

    /// A leopard stepped into a cell: it may fall, pick up a key, or reach the summit.
    fn on_enter(&mut self, w: &mut World, seat: usize, _tick: u32) {
        let (x, y) = w.pos[seat];
        let here: Vec<usize> = w.ents_at(x, y).map(|(id, _)| id).collect();
        for id in here {
            let e = w.ents[id];
            match e.kind {
                // A pit a boulder has filled is just floor now.
                Kind::Hazard if !w.filled.contains(&id) => {
                    // A chasm costs the level. The core only asks; the simulation restarts it.
                    if e.glyph == 'X' {
                        w.major_failure("into the chasm.");
                        return;
                    }
                    w.say(match e.glyph {
                        'h' if !self.revealed[seat] => {
                            "something unseen bites. back to the room's entrance. (a sightstone might help.)"
                        }
                        '-' if !self.revealed[seat] => "that ledge was only loose scree. back to the room's entrance.",
                        'a' | 'b' => "a crevasse. back to the room's entrance.",
                        _ => "a fall. back to the room's entrance.",
                    });
                    // Every other hazard is minor: this leopard alone goes back and drops its keys.
                    w.minor_failure(seat, x, y);
                    // Keys went back, so the gate is shut again; announce it again when it reopens.
                    if w.carried.len() < self.total_keys {
                        self.gate_announced = false;
                    }
                    return;
                }
                // A key nobody is carrying: this leopard picks it up just by stepping on it.
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

    /// Interact pulls the first lever beside the leopard (diagonals count), or uses a sightstone
    /// this leopard has not used yet. Entity order decides when two are in reach, the same as
    /// the client's mock.
    fn on_interact(&mut self, w: &mut World, seat: usize, _tick: u32) -> bool {
        let me = w.pos[seat];
        let target = w
            .entities()
            .filter(|(_, e)| beside((e.x, e.y), me))
            .find(|(_, e)| e.kind == Kind::Lever || (e.kind == Kind::Sightstone && !self.revealed[seat]))
            .map(|(id, e)| (id, e.kind));
        match target {
            Some((id, Kind::Lever)) => {
                // A lever toggles: insert succeeds if it was off; otherwise it was on, so remove it.
                let on = self.levers_on.insert(id);
                if !on {
                    self.levers_on.remove(&id);
                }
                w.say(if on { "*clunk*. stone grinds open nearby." } else { "*clunk*. a door slides shut." });
                true
            }
            Some((_, Kind::Sightstone)) => {
                // Only this leopard's sight changes; the partner must use the stone themself.
                self.revealed[seat] = true;
                w.say("the sightstone hums. hidden pits show their teeth to the one who touched it, and any false key fades.");
                true
            }
            _ => false,
        }
    }

    /// Each core piece's state for the snapshot. A carried key is drawn at its carrier's cell,
    /// with `carried_by` set to that seat. Pieces belonging to other mechanics are left alone.
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
                // Lit once either leopard has used it (the stone itself is no secret).
                Kind::Sightstone => (e.x, e.y, k, on(self.revealed.iter().any(|&r| r)), -1),
                Kind::Hazard => (e.x, e.y, k, on(w.filled.contains(&id)), -1),
                Kind::Checkpoint => (e.x, e.y, k, on(w.checkpoint_lit), -1),
                _ => continue,
            };
        }
    }

    /// Until a leopard uses the sightstone, it sees the lie: hidden pits are missing, disguised
    /// pits look like ledges, and false keys appear. A filled pit is shown as it is. Per seat,
    /// so using the stone reveals the truth to that leopard's player only.
    fn perceive(&self, w: &World, seat: usize, p: &mut Perception) {
        if self.revealed[seat] {
            return;
        }
        p.hidden.extend(self.hidden_pits.iter().filter(|id| !w.filled.contains(id)).map(|&id| id as u32));
        p.substituted
            .extend(self.disguised.iter().filter(|id| !w.filled.contains(id)).map(|&id| (id as u32, Kind::Ledge as u8)));
        p.phantoms.extend(&self.false_keys);
    }
}
