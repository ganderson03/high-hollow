//! One self-contained rule set, switched on by a level's `"mechanics"` list. The core is
//! always on and runs first; the rest run in the order the level lists them. Same boundaries
//! as docs/levels-design.md ("Mechanic modules"), so rule parity can be reviewed one
//! mechanic at a time.
//!
//! Ordering within a tick: seats act in seat order (interact, then move or slide); then each
//! mechanic's `on_tick`, in list order. Timing is counted in ticks, never wall-clock time.
//!
//! There is no `reset`: a mechanic's state is saved by cloning it (`clone_box`), at level
//! load and when a checkpoint lights, and a restart restores the clone.

use crate::mechanics;
use crate::protocol::{EntityWire, Move};
use crate::world::{Perception, World};

#[allow(unused_variables)]
pub trait Mechanic: Send {
    fn name(&self) -> &'static str;

    fn clone_box(&self) -> Box<dyn Mechanic>;

    /// Called once the world has created its entities from the level's glyphs.
    fn on_load(&mut self, w: &mut World) -> Result<(), String> {
        Ok(())
    }

    /// True if this mechanic makes the cell impassable to a leopard right now.
    fn blocks(&self, w: &World, x: i32, y: i32) -> bool {
        false
    }

    /// True if a rock cannot be pushed or slide into the cell.
    fn blocks_rock(&self, w: &World, x: i32, y: i32) -> bool {
        self.blocks(w, x, y)
    }

    /// A leopard tried to step in `dir`, before the step is checked (a push).
    fn on_bump(&mut self, w: &mut World, others: &Others, seat: usize, dir: Move, tick: u32) {}

    /// A leopard has just left a cell (it already stands in the new one).
    fn on_leave(&mut self, w: &mut World, seat: usize, from: (i32, i32), tick: u32) {}

    /// A leopard has just stepped into a new cell.
    fn on_enter(&mut self, w: &mut World, seat: usize, tick: u32) {}

    /// A leopard pressed interact. Return true if this mechanic handled it.
    fn on_interact(&mut self, w: &mut World, seat: usize, tick: u32) -> bool {
        false
    }

    /// After every seat has acted this tick.
    fn on_tick(&mut self, w: &mut World, others: &Others, tick: u32) {}

    /// Write this mechanic's entities' current states into the snapshot.
    fn write(&self, w: &World, out: &mut [EntityWire]) {}

    /// Add what the given seat should not see, or should see falsely.
    fn perceive(&self, w: &World, seat: usize, p: &mut Perception) {}
}

impl Clone for Box<dyn Mechanic> {
    fn clone(&self) -> Self {
        self.clone_box()
    }
}

/// Every mechanic except the one being called, for questions about the whole rule set.
pub struct Others<'a> {
    pub before: &'a [Box<dyn Mechanic>],
    pub after: &'a [Box<dyn Mechanic>],
}

impl Others<'_> {
    fn all(&self) -> impl Iterator<Item = &Box<dyn Mechanic>> {
        self.before.iter().chain(self.after)
    }

    pub fn blocks(&self, w: &World, x: i32, y: i32) -> bool {
        self.all().any(|m| m.blocks(w, x, y))
    }

    pub fn blocks_rock(&self, w: &World, x: i32, y: i32) -> bool {
        self.all().any(|m| m.blocks_rock(w, x, y))
    }
}

/// Runs `f` on each mechanic in order, giving it the others.
pub fn each_with_others(mechs: &mut [Box<dyn Mechanic>], mut f: impl FnMut(&mut Box<dyn Mechanic>, &Others)) {
    for i in 0..mechs.len() {
        let (before, rest) = mechs.split_at_mut(i);
        let (m, after) = rest.split_first_mut().unwrap();
        f(m, &Others { before, after });
    }
}

/// The mechanic a glyph belongs to, for glyphs outside the core.
pub fn glyph_needs(c: char) -> Option<&'static str> {
    Some(match c {
        '=' => "ledges",
        '~' | '%' => "ice",
        'O' | '[' | ']' | '^' | 'v' => "boulders",
        'w' | 'W' | 'V' | 'M' => "water",
        'a' | 'b' | 'y' | 'z' => "sight",
        _ => return None,
    })
}

/// Builds the mechanics a level asks for, core first. Unknown names are an error, so a level
/// written for a newer server fails loudly instead of playing with missing rules.
pub fn for_level(names: &[String]) -> Result<Vec<Box<dyn Mechanic>>, String> {
    let mut list: Vec<Box<dyn Mechanic>> = vec![Box::new(mechanics::core::Core::default())];
    for n in names {
        let m: Box<dyn Mechanic> = match n.as_str() {
            "core" | "hollow" => continue,
            "ledges" => Box::new(mechanics::ledges::Ledges::default()),
            "ice" => Box::new(mechanics::ice::Ice),
            "boulders" => Box::new(mechanics::boulders::Boulders::default()),
            "water" => Box::new(mechanics::water::Water::default()),
            "sight" => Box::new(mechanics::sight::Sight::default()),
            other => return Err(format!("unknown mechanic '{other}'")),
        };
        if list.iter().any(|x| x.name() == m.name()) {
            return Err(format!("mechanic '{n}' is listed twice"));
        }
        list.push(m);
    }
    Ok(list)
}
