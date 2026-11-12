//! High Hollow's authoritative server. Layers, innermost first:
//!
//! - `protocol`: wire messages (JSON Lines), matching the client's `WireCodec`.
//! - `level`: level and trail files, read as the client reads them.
//! - `world`, `mechanic`, `core`: the rules, split per mechanic like the client's mock.
//! - `sim`: one level, ticked by the caller.
//! - `game`: seats, clients, clock and trail; no sockets.
//! - `net`: TCP threads around a `Game`.

pub mod core;
pub mod game;
pub mod level;
pub mod mechanic;
pub mod net;
pub mod protocol;
pub mod sim;
pub mod world;
