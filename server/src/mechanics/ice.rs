//! Frozen Tarns: ice (`~`, or cells listed under the level's `"ice"`) and open water (`%`).
//!
//! Stepping onto ice sets a leopard sliding the way it moved, one cell every SLIDE_TICKS,
//! until the next cell is not ice, is blocked, or holds the partner. It stops on the first
//! non-ice cell. Input is ignored while sliding (the simulation runs the slide). Sliding into
//! open water is a major failure. No protocol: both sides read ice from the level file.

use crate::mechanic::Mechanic;
use crate::world::World;

#[derive(Clone, Copy)]
pub struct Ice;

impl Mechanic for Ice {
    fn name(&self) -> &'static str {
        "ice"
    }

    fn clone_box(&self) -> Box<dyn Mechanic> {
        Box::new(*self)
    }

    fn blocks_rock(&self, w: &World, x: i32, y: i32) -> bool {
        w.level.at(x, y) == '%'
    }

    fn on_enter(&mut self, w: &mut World, seat: usize, _tick: u32) {
        let (x, y) = w.pos[seat];
        if w.level.at(x, y) == '%' {
            w.major_failure("through the ice into open water.");
            return;
        }
        w.slide[seat] = if w.level.is_ice(x, y) { Some(w.last_dir[seat]) } else { None };
    }
}
