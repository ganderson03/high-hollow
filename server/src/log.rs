//! The server's log: one line per event on stderr, so `play.ps1` can keep it in
//! `Builds\logs\server.log` and show it live.
//!
//! Every line starts with the time since the server started and the part of the server that
//! wrote it, padded so the messages line up:
//!
//! ```text
//! 00:00:00.004 main  listening on 127.0.0.1:7777 (trail starts at foothills-1)
//! 00:00:03.120 net   conn 1 from 127.0.0.1:56863
//! 00:00:03.122 game  [foothills-1 t=0] snep joined as client 0 (conn 1); holds both leopards
//! 00:01:12.480 say   [foothills-1 t=4121] key 1 of 2.
//! ```
//!
//! Areas: `main` (startup), `net` (connections), `game` (players, seats, votes, pauses,
//! restarts, levels), `chat` (what players type), `say` (what the server tells them, which is
//! also the story of the level: keys, levers, falls), `stat` (a periodic summary).

use std::fmt;
use std::sync::OnceLock;
use std::time::Instant;

static START: OnceLock<Instant> = OnceLock::new();

/// Starts the clock the log's timestamps count from. Called first thing in `main`; if it is
/// not, the first line logged starts it.
pub fn start() {
    START.get_or_init(Instant::now);
}

/// Writes one log line. Use the `hlog!` macro rather than calling this directly.
pub fn line(area: &str, text: fmt::Arguments) {
    let t = START.get_or_init(Instant::now).elapsed();
    let s = t.as_secs();
    eprintln!("{:02}:{:02}:{:02}.{:03} {area:<5} {text}", s / 3600, s / 60 % 60, s % 60, t.subsec_millis());
}

/// `hlog!("game", "{name} joined")`: one timestamped line in the server log.
#[macro_export]
macro_rules! hlog {
    ($area:literal, $($arg:tt)*) => {
        $crate::log::line($area, format_args!($($arg)*))
    };
}
