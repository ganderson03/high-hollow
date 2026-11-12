//! Shared state every mechanic works against: the level, both seats, the entity list
//! (index = entity id on the wire), carried keys, and the few things mechanics need from
//! each other (rocks, filled pits, water, slides). Mirrors `Crawler.Mock.MockWorld`.
//!
//! The whole world is `Clone`: a checkpoint, or the level start, is a saved copy of it plus
//! the mechanics' own state, and a restart restores that copy.

use std::collections::{BTreeMap, BTreeSet};

use crate::level::Level;
use crate::protocol::{EntityWire, Move};

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
    Ledge = 8,
    Boulder = 9,
    Sluice = 10,
    Wheel = 11,
    Checkpoint = 12,
}

impl Kind {
    /// The entity a glyph creates. Terrain glyphs (walls, ice, channels, phantoms) create none.
    pub fn of_glyph(c: char) -> Option<Kind> {
        Some(match c {
            'D' | 'F' => Kind::Door,
            'L' => Kind::Lever,
            'B' => Kind::Plate,
            'K' => Kind::Key,
            // pit, hidden pit, chasm, crevasses seen by one leopard, pit dressed as a ledge
            'H' | 'h' | 'X' | 'a' | 'b' | '-' => Kind::Hazard,
            'G' => Kind::Goal,
            'S' => Kind::Sightstone,
            '=' => Kind::Ledge,
            // boulder; a slab is one entity at its first cell: '[' (then ']'), or 'v' (then '^' north of it)
            'O' | '[' | 'v' => Kind::Boulder,
            'V' => Kind::Sluice,
            'M' => Kind::Wheel,
            'C' => Kind::Checkpoint,
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

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RestartCause {
    /// A major failure.
    Fell,
    /// Both players agreed, or one holding both seats asked.
    Vote,
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
    /// The level went back to its start, or to the lit checkpoint.
    Restart { cause: RestartCause, checkpoint: bool },
}

/// A boulder or slab. Shape 0 is a boulder; 1 a slab along x (x and x+1); 2 a slab along y
/// (y and y+1). A rock that filled a pit or fell into a chasm is `gone` and occupies nothing.
#[derive(Debug, Clone)]
pub struct Rock {
    pub id: usize,
    pub x: i32,
    pub y: i32,
    pub shape: u8,
    pub gone: bool,
    pub slide: Option<Move>,
    pub next: u32,
}

impl Rock {
    pub fn cells(&self) -> Vec<(i32, i32)> {
        match self.shape {
            1 => vec![(self.x, self.y), (self.x + 1, self.y)],
            2 => vec![(self.x, self.y), (self.x, self.y + 1)],
            _ => vec![(self.x, self.y)],
        }
    }

    pub fn covers(&self, x: i32, y: i32) -> bool {
        !self.gone && self.cells().contains(&(x, y))
    }
}

#[derive(Clone)]
pub struct World {
    pub level: Level,
    pub ents: Vec<Ent>,
    pub spawn: [(i32, i32); SEATS],
    pub pos: [(i32, i32); SEATS],
    /// Key entity id -> the seat carrying it.
    pub carried: BTreeMap<usize, usize>,
    pub won: bool,
    pub events: Vec<SimEvent>,

    /// A leopard sliding on ice, and which way. Input is ignored while sliding.
    pub slide: [Option<Move>; SEATS],
    /// The direction each leopard last moved, which is the way it slides.
    pub last_dir: [Move; SEATS],
    pub rocks: Vec<Rock>,
    /// Hazard ids a boulder has filled; they are floor now.
    pub filled: BTreeSet<usize>,
    /// Cells with flowing water this tick, recomputed from the source every tick.
    pub water: BTreeSet<(i32, i32)>,
    /// Wheel ids that water reaches this tick.
    pub turning: BTreeSet<usize>,
    pub checkpoint_lit: bool,
    /// Set by a mechanic when a major failure happens; the simulation restarts the level.
    pub major: Option<String>,
    /// Moves a mechanic makes for a leopard (a slab carrying its pushers forward).
    pub forced: Vec<(usize, Move)>,
    /// How long a slab push stays live, in ticks.
    pub push_window: u32,
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

        World {
            level,
            ents,
            spawn,
            pos: spawn,
            carried: BTreeMap::new(),
            won: false,
            events: Vec::new(),
            slide: [None; SEATS],
            last_dir: [Move::None; SEATS],
            rocks: Vec::new(),
            filled: BTreeSet::new(),
            water: BTreeSet::new(),
            turning: BTreeSet::new(),
            checkpoint_lit: false,
            major: None,
            forced: Vec::new(),
            push_window: 90,
        }
    }

    pub fn entities(&self) -> impl Iterator<Item = (usize, &Ent)> {
        self.ents.iter().enumerate().skip(Self::FIRST_ENTITY)
    }

    /// Ids of the entities whose home cell is this one.
    pub fn ents_at(&self, x: i32, y: i32) -> impl Iterator<Item = (usize, &Ent)> {
        self.entities().filter(move |(_, e)| e.x == x && e.y == y)
    }

    pub fn seat_at(&self, x: i32, y: i32) -> bool {
        self.pos.contains(&(x, y))
    }

    /// Index into `rocks` of the rock covering this cell, if any.
    pub fn rock_at(&self, x: i32, y: i32) -> Option<usize> {
        self.rocks.iter().position(|r| r.covers(x, y))
    }

    pub fn say(&mut self, text: impl Into<String>) {
        self.events.push(SimEvent::Say(text.into()));
    }

    /// Ask for a level restart. The first request in a tick wins.
    pub fn major_failure(&mut self, text: impl Into<String>) {
        if self.major.is_none() {
            self.major = Some(text.into());
        }
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
        self.slide[seat] = None;

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

    /// Flowing cells in row-scan order: top row first, west to east.
    pub fn water_cells(&self) -> Vec<(i32, i32)> {
        let mut cells: Vec<_> = self.water.iter().copied().collect();
        cells.sort_by_key(|&(x, y)| (-y, x));
        cells
    }
}

/// One seat's perception, collected before it is sent as an overlay.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Perception {
    pub hidden: Vec<u32>,
    pub substituted: Vec<(u32, u8)>,
    pub phantoms: Vec<(i32, i32, u8)>,
}

/// One step in a direction.
pub fn step((x, y): (i32, i32), dir: Move) -> (i32, i32) {
    let (dx, dy) = dir.delta();
    (x + dx, y + dy)
}

/// True if the two cells touch, diagonals included, or are the same cell.
pub fn beside(a: (i32, i32), b: (i32, i32)) -> bool {
    (a.0 - b.0).abs() <= 1 && (a.1 - b.1).abs() <= 1
}
