//! One self-contained rule set, switched on by a level. The core is always on; later
//! mechanics come from the level's `mechanics` list. Same boundaries as the client's
//! `Crawler.Mock.IMechanic`, so rule parity can be reviewed one mechanic at a time.
//!
//! Ordering: within a tick, seats are processed in seat order, and mechanics are called in
//! the order the level lists them (core first).

use crate::protocol::EntityWire;
use crate::world::{Perception, World};

pub trait Mechanic: Send {
    /// Called once the world has created its entities from the level's glyphs.
    fn on_load(&mut self, w: &World) -> Result<(), String>;

    /// Put this mechanic back to the level's starting state.
    fn reset(&mut self, w: &World);

    /// True if this mechanic makes the cell impassable right now (a closed door).
    fn blocks(&self, w: &World, x: i32, y: i32) -> bool;

    /// A seat has just stepped into a new cell.
    fn on_enter(&mut self, w: &mut World, seat: usize, tick: u32);

    /// A seat pressed interact. Return true if this mechanic handled it.
    fn on_interact(&mut self, w: &mut World, seat: usize, tick: u32) -> bool;

    /// Write this mechanic's entities' current states into the snapshot.
    fn write(&self, w: &World, out: &mut [EntityWire]);

    /// Add what the given seat should not see, or should see falsely.
    fn perceive(&self, w: &World, seat: usize, p: &mut Perception);
}

/// Builds the mechanics a level asks for, core first. Unknown names are an error, so a level
/// written for a newer server fails loudly instead of playing with missing rules.
pub fn for_level(names: &[String]) -> Result<Vec<Box<dyn Mechanic>>, String> {
    let list: Vec<Box<dyn Mechanic>> = vec![Box::new(crate::core::Core::default())];
    for n in names {
        match n.as_str() {
            "core" | "hollow" => {}
            other => return Err(format!("unknown mechanic '{other}'")),
        }
    }
    Ok(list)
}
