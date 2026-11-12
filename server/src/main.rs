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

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("error: {e}");
            ExitCode::FAILURE
        }
    }
}

fn run() -> Result<(), String> {
    let mut bind = "127.0.0.1".to_string();
    let mut port: u16 = 7777;
    let mut levels: Option<PathBuf> = None;
    let mut start = "foothills-1".to_string();

    let mut args = std::env::args().skip(1);
    while let Some(a) = args.next() {
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

    let dir = levels.or_else(default_levels_dir).ok_or("no levels directory found; pass --levels")?;
    let store = LevelStore::new(dir);
    eprintln!("[main] levels from {}", store.dir().display());

    let trail = match store.trail() {
        Ok(t) => Some(t),
        Err(e) => {
            eprintln!("[main] {e} (the level will repeat instead of chaining)");
            None
        }
    };
    let loader = store.clone();
    let game = Game::new(&start, trail, Box::new(move |id| loader.load(id)))?;

    let listener = TcpListener::bind((bind.as_str(), port)).map_err(|e| format!("bind {bind}:{port}: {e}"))?;
    eprintln!("[main] listening on {} (trail starts at {start})", listener.local_addr().map_err(|e| e.to_string())?);
    net::serve(listener, game).map_err(|e| e.to_string())
}

/// The client's level files: next to the working directory, or relative to this crate.
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
