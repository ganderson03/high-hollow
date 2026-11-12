//! Shared state every mechanic works against: the level, both seats, the entity list
//! (index = entity id on the wire) and carried keys. Mirrors `Crawler.Mock.MockWorld`.

use std::collections::BTreeMap;

use crate::level::Level;
use crate::protocol::EntityWire;

pub const SEATS: usize = 2;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum Kind {
    Player = 0,
    Door = 1,
    Lever = 2,
    Plate = 3,
    Key = 4,
    Hazard = 5,
    Goal = 6,
    Sightstone = 7,
}

impl Kind {
    pub fn of_glyph(c: char) -> Option<Kind> {
        Some(match c {
            'D' | 'F' => Kind::Door,
            'L' => Kind::Lever,
            'B' => Kind::Plate,
            'K' => Kind::Key,
            'H' | 'h' => Kind::Hazard,
            'G' => Kind::Goal,
            'S' => Kind::Sightstone,
            _ => return None,
        })
    }
}

/// A level entity: what it is and where it started.
#[derive(Debug, Clone, Copy)]
pub struct Ent {
    pub kind: Kind,
    pub glyph: char,
    pub x: i32,
    pub y: i32,
    pub region: i32,
}

/// Something the simulation wants said or sent. The session decides who receives it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SimEvent {
    /// Server chat to everyone.
    Say(String),
    /// The pair reached the summit.
    Won,
    /// Overlays may have changed; resend each client's.
    Perception,
}

pub struct World {
    pub level: Level,
    pub ents: Vec<Ent>,
    pub spawn: [(i32, i32); SEATS],
    pub pos: [(i32, i32); SEATS],
    /// Key entity id -> the seat carrying it.
    pub carried: BTreeMap<usize, usize>,
    pub won: bool,
    pub events: Vec<SimEvent>,
}

impl World {
    /// Entity ids 0 and 1 are the seats.
    pub const FIRST_ENTITY: usize = SEATS;

    pub fn new(level: Level) -> World {
        // P is seat 0, 1 is seat 1. A level missing '1' puts the second leopard beside the first.
        let p = level.find('P').unwrap_or((1, 1));
        let q = level.find('1').unwrap_or((p.0 + 1, p.1));
        let spawn = [p, q];

        let mut ents: Vec<Ent> = spawn
            .iter()
            .enumerate()
            .map(|(s, &(x, y))| Ent {
                kind: Kind::Player,
                glyph: if s == 0 { 'P' } else { '1' },
                x,
                y,
                region: level.region_at(x, y),
            })
            .collect();
        // Everything else in row-scan order, so ids match the client's mock.
        for (x, y, c) in level.scan() {
            if let Some(kind) = Kind::of_glyph(c) {
                ents.push(Ent { kind, glyph: c, x, y, region: level.region_at(x, y) });
            }
        }

        World { level, ents, spawn, pos: spawn, carried: BTreeMap::new(), won: false, events: Vec::new() }
    }

    pub fn entities(&self) -> impl Iterator<Item = (usize, &Ent)> {
        self.ents.iter().enumerate().skip(Self::FIRST_ENTITY)
    }

    pub fn seat_at(&self, x: i32, y: i32) -> bool {
        self.pos.contains(&(x, y))
    }

    pub fn say(&mut self, text: impl Into<String>) {
        self.events.push(SimEvent::Say(text.into()));
    }

    /// Minor failure: only this leopard pays. It returns to the entrance of the room it fell
    /// in (or its spawn if the room has none, or the partner stands there) and every key it
    /// carries goes back where it lay.
    pub fn minor_failure(&mut self, seat: usize, x: i32, y: i32) {
        let region = self.level.region_at(x, y);
        let other = self.pos[1 - seat];
        self.pos[seat] = match self.level.entrance_of(region) {
            Some(e) if e != other => e,
            _ => self.spawn[seat],
        };

        let before = self.carried.len();
        self.carried.retain(|_, s| *s != seat);
        match before - self.carried.len() {
            0 => {}
            1 => self.say("the key slips loose and lands back where it lay."),
            n => self.say(format!("all {n} keys slip loose and land back where they lay.")),
        }
    }

    /// A blank snapshot row for each entity at its start; mechanics overwrite their own.
    pub fn base_snapshot(&self) -> Vec<EntityWire> {
        let mut out: Vec<EntityWire> = self.ents.iter().map(|e| (e.x, e.y, e.kind as u8, 0, -1)).collect();
        for (row, &(x, y)) in out.iter_mut().zip(&self.pos) {
            *row = (x, y, Kind::Player as u8, 0, -1);
        }
        out
    }
}

/// One seat's perception, collected before it is sent as an overlay.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Perception {
    pub hidden: Vec<u32>,
    pub substituted: Vec<(u32, u8)>,
    pub phantoms: Vec<(i32, i32, u8)>,
}
