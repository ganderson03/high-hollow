#![allow(dead_code)]

use std::path::PathBuf;

use hollow::game::{Game, Out};
use hollow::level::{Level, LevelStore, Trail};

pub fn levels_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../Assets/StreamingAssets/levels")
}

pub fn store() -> LevelStore {
    LevelStore::new(levels_dir())
}

pub fn level(id: &str) -> Level {
    store().load(id).unwrap_or_else(|e| panic!("{e}"))
}

pub fn trail() -> Trail {
    store().trail().unwrap()
}

pub fn game(start: &str) -> Game {
    let s = store();
    Game::new(start, Some(trail()), Box::new(move |id| s.load(id))).unwrap()
}

/// A route fixture from tools/export_routes.py: [seat, action] pairs.
pub fn route(id: &str) -> Vec<(usize, String)> {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join(format!("tests/routes/{id}.json"));
    let text = std::fs::read_to_string(&path).unwrap_or_else(|e| panic!("{}: {e}", path.display()));
    serde_json::from_str(&text).unwrap()
}

/// Lines sent to one connection, decoded.
pub fn lines_to(out: &[Out], conn: u64) -> Vec<serde_json::Value> {
    out.iter()
        .filter_map(|o| match o {
            Out::Send(c, l) if *c == conn => Some(serde_json::from_str(l).unwrap()),
            _ => None,
        })
        .collect()
}

pub fn types(lines: &[serde_json::Value]) -> Vec<String> {
    lines.iter().map(|v| v["t"].as_str().unwrap().to_string()).collect()
}
