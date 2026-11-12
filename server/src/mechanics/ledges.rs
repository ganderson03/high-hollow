//! Talus: crumbling ledges (`=`). A ledge holds while anyone stands on it; it cracks when first
//! stepped on, and collapses when the last leopard steps off. A collapsed ledge stays down
//! until a restart, and walking into one is a minor failure, like a pit.
//!
//! Wire: kind Ledge (8), state 0 intact, 1 cracked, 2 collapsed.

use std::collections::BTreeMap;

use crate::mechanic::Mechanic;
use crate::protocol::EntityWire;
use crate::world::{Kind, World};

pub const INTACT: u8 = 0;
pub const CRACKED: u8 = 1;
pub const COLLAPSED: u8 = 2;

#[derive(Default, Clone)]
pub struct Ledges {
    state: BTreeMap<usize, u8>,
}

impl Ledges {
    fn at(&self, w: &World, x: i32, y: i32) -> Option<usize> {
        w.ents_at(x, y).find(|(_, e)| e.kind == Kind::Ledge).map(|(id, _)| id)
    }
}

impl Mechanic for Ledges {
    fn name(&self) -> &'static str {
        "ledges"
    }

    fn clone_box(&self) -> Box<dyn Mechanic> {
        Box::new(self.clone())
    }

    fn on_load(&mut self, w: &mut World) -> Result<(), String> {
        for (id, e) in w.entities() {
            if e.kind == Kind::Ledge {
                self.state.insert(id, INTACT);
            }
        }
        Ok(())
    }

    /// Too narrow for a rock.
    fn blocks_rock(&self, w: &World, x: i32, y: i32) -> bool {
        self.at(w, x, y).is_some()
    }

    fn on_enter(&mut self, w: &mut World, seat: usize, _tick: u32) {
        let (x, y) = w.pos[seat];
        let Some(id) = self.at(w, x, y) else { return };
        let s = self.state.get_mut(&id).unwrap();
        if *s == COLLAPSED {
            w.say("the ledge is gone. back to the room's entrance.");
            w.minor_failure(seat, x, y);
        } else {
            *s = CRACKED;
        }
    }

    fn on_leave(&mut self, w: &mut World, _seat: usize, from: (i32, i32), _tick: u32) {
        let Some(id) = self.at(w, from.0, from.1) else { return };
        // Still held if the partner stands on it; both stepping off in one tick collapses it once.
        if self.state[&id] == CRACKED && !w.seat_at(from.0, from.1) {
            self.state.insert(id, COLLAPSED);
            w.say("the ledge crumbles away behind you.");
        }
    }

    fn write(&self, w: &World, out: &mut [EntityWire]) {
        for (&id, &s) in &self.state {
            let e = &w.ents[id];
            out[id] = (e.x, e.y, Kind::Ledge as u8, s, -1);
        }
    }
}
