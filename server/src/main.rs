//! The server executable: reads the command line, loads the levels, and hands a `Game` to the
//! network layer, which runs until the process is stopped.
//!
//! Everything interesting lives in the library (`src/lib.rs` and below), so the tests can drive
//! the same code without starting a process. This file only wires it together. Its log goes to
//! stderr, which `play.ps1` saves in `Builds/logs/server.log`.

use std::net::TcpListener;
use std::path::PathBuf;
use std::process::ExitCode;

use hollow::game::Game;
use hollow::level::LevelStore;
use hollow::net;

const USAGE: &str = "\
high-hollow-server [options]

  --bind <addr>     address to listen on      (default 127.0.0.1)
  --port <port>     port to listen on         (default 7777)
  --levels <dir>    level files directory     (default: the client's StreamingAssets/levels)
  --level <id>      level the trail starts on (default foothills-1)
";

/// Runs the server; any setup error is printed and turned into a failing exit code.
fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("error: {e}");
            ExitCode::FAILURE
        }
    }
}

/// Parses the options, loads the trail and its first level, binds the port and serves forever.
fn run() -> Result<(), String> {
    // Defaults: local only, the port the client expects, the first level of the trail.
    let mut bind = "127.0.0.1".to_string();
    let mut port: u16 = 7777;
    let mut levels: Option<PathBuf> = None;
    let mut start = "foothills-1".to_string();

    // A small hand-rolled parser: four options do not justify a dependency.
    let mut args = std::env::args().skip(1);
    while let Some(a) = args.next() {
        // The option's value is the next argument; missing it is an error that shows the usage.
        let mut value = || args.next().ok_or_else(|| format!("{a} needs a value\n\n{USAGE}"));
        match a.as_str() {
            "--bind" => bind = value()?,
            "--port" => port = value()?.parse().map_err(|e| format!("--port: {e}"))?,
            "--levels" => levels = Some(PathBuf::from(value()?)),
            "--level" => start = value()?,
            "-h" | "--help" => {
                print!("{USAGE}");
                return Ok(());
            }
            other => return Err(format!("unknown option {other}\n\n{USAGE}")),
        }
    }

    // The server reads the very same level files the client draws, so the two cannot disagree
    // about a map.
    let dir = levels.or_else(default_levels_dir).ok_or("no levels directory found; pass --levels")?;
    let store = LevelStore::new(dir);
    eprintln!("[main] levels from {}", store.dir().display());

    // Without a trail the server still runs: it just replays the starting level after a win.
    let trail = match store.trail() {
        Ok(t) => Some(t),
        Err(e) => {
            eprintln!("[main] {e} (the level will repeat instead of chaining)");
            None
        }
    };
    // The game loads later levels through this closure, so it never touches the file system
    // itself (the tests hand it levels the same way).
    let loader = store.clone();
    let game = Game::new(&start, trail, Box::new(move |id| loader.load(id)))?;

    // play.ps1 waits for the "listening on" line before it starts the players.
    let listener = TcpListener::bind((bind.as_str(), port)).map_err(|e| format!("bind {bind}:{port}: {e}"))?;
    eprintln!("[main] listening on {} (trail starts at {start})", listener.local_addr().map_err(|e| e.to_string())?);
    net::serve(listener, game).map_err(|e| e.to_string())
}

/// The client's level files, looked for in order: under the working directory (run from the
/// repo root), one level up (run from `server/`), then relative to where this crate was built.
/// The first that has a `trail.json` wins.
fn default_levels_dir() -> Option<PathBuf> {
    let rel = PathBuf::from("Assets/StreamingAssets/levels");
    [
        rel.clone(),
        PathBuf::from("..").join(&rel),
        PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("..").join(&rel),
    ]
    .into_iter()
    .find(|p| p.join("trail.json").is_file())
}
