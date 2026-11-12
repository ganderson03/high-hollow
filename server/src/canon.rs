//! Canonical encoding of the authoritative world state, and its hash.
//!
//! The JSON on the wire is transport only; two encoders may order or space it differently.
//! This encoding is fixed, so a client (or a replay) can rebuild the same bytes from a decoded
//! snapshot and check it got exactly what the server simulated. Overlays are perception and
//! are never part of it.
//!
//! Layout, all integers little-endian:
//!
//! | Field | Type |
//! | --- | --- |
//! | magic | the 4 bytes `HHC1` |
//! | tick | u32 |
//! | entity count | u32 |
//! | each entity, in id order | x i32, y i32, kind u8, state u8, carried_by i8 (11 bytes) |
//! | water cell count | u32 |
//! | each water cell, top row first then west to east | x i32, y i32 |
//!
//! The hash is XXH3-64 with seed 0, written as 16 lowercase hex digits.

use crate::protocol::EntityWire;

pub const MAGIC: &[u8; 4] = b"HHC1";

pub fn encode(tick: u32, entities: &[EntityWire], water: &[(i32, i32)]) -> Vec<u8> {
    let mut b = Vec::with_capacity(16 + entities.len() * 11 + water.len() * 8);
    b.extend_from_slice(MAGIC);
    b.extend_from_slice(&tick.to_le_bytes());
    b.extend_from_slice(&(entities.len() as u32).to_le_bytes());
    for &(x, y, kind, state, carried) in entities {
        b.extend_from_slice(&x.to_le_bytes());
        b.extend_from_slice(&y.to_le_bytes());
        b.push(kind);
        b.push(state);
        b.push(carried as u8);
    }
    b.extend_from_slice(&(water.len() as u32).to_le_bytes());
    for &(x, y) in water {
        b.extend_from_slice(&x.to_le_bytes());
        b.extend_from_slice(&y.to_le_bytes());
    }
    b
}

pub fn hash(tick: u32, entities: &[EntityWire], water: &[(i32, i32)]) -> u64 {
    xxhash_rust::xxh3::xxh3_64(&encode(tick, entities, water))
}

pub fn hash_hex(tick: u32, entities: &[EntityWire], water: &[(i32, i32)]) -> String {
    format!("{:016x}", hash(tick, entities, water))
}
