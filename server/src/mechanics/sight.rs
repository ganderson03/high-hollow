//! Whiteout: split sight. Each crevasse is visible to exactly one leopard, and some leopards
//! see false crevasses where the ground is safe. Crevasses are real for both (a minor
//! failure, handled by the core as a pit); only the seeing differs, so this mechanic is
//! perception alone and never touches the rules.
//!
//! Glyphs: `a` / `b` a crevasse only leopard 0 / leopard 1 can see; `y` / `z` a false crevasse
//! shown only to leopard 0 / leopard 1 (floor for the rules).
//! Wire: each seat's overlay hides the crevasses it cannot see and adds its false ones as
//! phantoms with the Hazard look (5).

use crate::mechanic::Mechanic;
use crate::world::{Kind, Perception, World, SEATS};

#[derive(Default, Clone)]
pub struct Sight {
    /// Crevasse ids seen only by each seat.
    seen_by: [Vec<usize>; SEATS],
    /// False crevasses shown to each seat.
    false_for: [Vec<(i32, i32, u8)>; SEATS],
}

impl Mechanic for Sight {
    fn name(&self) -> &'static str {
        "sight"
    }

    fn clone_box(&self) -> Box<dyn Mechanic> {
        Box::new(self.clone())
    }

    fn on_load(&mut self, w: &mut World) -> Result<(), String> {
        for (id, e) in w.entities() {
            match (e.kind, e.glyph) {
                (Kind::Hazard, 'a') => self.seen_by[0].push(id),
                (Kind::Hazard, 'b') => self.seen_by[1].push(id),
                _ => {}
            }
        }
        for (x, y, c) in w.level.scan() {
            match c {
                'y' => self.false_for[0].push((x, y, Kind::Hazard as u8)),
                'z' => self.false_for[1].push((x, y, Kind::Hazard as u8)),
                _ => {}
            }
        }
        Ok(())
    }

    fn perceive(&self, w: &World, seat: usize, p: &mut Perception) {
        let other = 1 - seat;
        p.hidden.extend(self.seen_by[other].iter().filter(|id| !w.filled.contains(id)).map(|&id| id as u32));
        p.phantoms.extend(&self.false_for[seat]);
    }
}
