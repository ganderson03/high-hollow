//! The network layer: TCP sockets carrying JSON Lines (one JSON object per line).
//!
//! ```text
//!   accept thread ──Open──▶ ┐
//!   read-N threads ─Line──▶ ├─ one channel ─▶  simulation thread (owns the Game, 60 Hz)
//!                  ─Closed▶ ┘                        │
//!                                                    ▼  per-connection bounded queues
//!                                              write-N threads ──▶ sockets
//! ```
//!
//! Only the simulation thread ever touches the `Game`. Every other thread just moves bytes and
//! talks to it through channels, so there are no locks around game state and no way for two
//! threads to see the world half-updated. A slow or stuck client can never stall the game
//! either: its writer has a bounded queue, and when that fills, the client is dropped.

use std::collections::HashMap;
use std::io::{self, BufRead, BufReader, BufWriter, Read, Write};
use std::net::{Shutdown, TcpListener, TcpStream};
use std::sync::mpsc::{self, Receiver, RecvTimeoutError, Sender, SyncSender, TrySendError};
use std::thread;
use std::time::{Duration, Instant};

use crate::game::{ConnId, Game, Out, TICK_HZ};
use crate::protocol::{ClientMsg, MAX_LINE};

/// Lines a client may fall behind by before it is dropped (about 10 s of snapshots).
const OUTBOX: usize = 256;
/// Ticks the clock may catch up in one go after a stall, before it skips ahead instead.
const MAX_CATCH_UP: u32 = 4;

/// What the socket threads tell the simulation thread.
enum NetEvent {
    /// A new connection, with the socket its writer thread will own.
    Open(ConnId, TcpStream),
    /// One complete line from a client, not yet decoded.
    Line(ConnId, String),
    /// The client went away (or broke the line-length rule).
    Closed(ConnId),
}

/// The simulation thread's handle on one connection: the queue its writer thread drains.
struct Conn {
    tx: SyncSender<String>,
}

/// Runs the server on an already-bound listener. Returns only on a fatal error.
pub fn serve(listener: TcpListener, game: Game) -> io::Result<()> {
    let (tx, rx) = mpsc::channel();
    thread::Builder::new().name("accept".into()).spawn(move || accept_loop(listener, tx))?;
    run(game, rx); // this thread becomes the simulation thread
    Ok(())
}

/// Accepts connections forever. Each gets an id, and a reader thread of its own.
fn accept_loop(listener: TcpListener, tx: Sender<NetEvent>) {
    let mut next_id: ConnId = 1;
    for stream in listener.incoming() {
        let stream = match stream {
            Ok(s) => s,
            Err(e) => {
                eprintln!("[net] accept failed: {e}");
                continue;
            }
        };
        let id = next_id;
        next_id += 1;
        // Small messages, sent often: turn off Nagle's algorithm so inputs are not held back
        // waiting to be batched.
        let _ = stream.set_nodelay(true);
        let peer = stream.peer_addr().map(|a| a.to_string()).unwrap_or_else(|_| "?".into());
        eprintln!("[net] conn {id} from {peer}");

        // One handle reads (here), the other writes (handed to the simulation thread).
        let reader = match stream.try_clone() {
            Ok(r) => r,
            Err(e) => {
                eprintln!("[net] conn {id}: {e}");
                continue;
            }
        };
        if tx.send(NetEvent::Open(id, stream)).is_err() {
            return; // the simulation thread has gone; nothing left to serve
        }
        let tx = tx.clone();
        let _ = thread::Builder::new().name(format!("read-{id}")).spawn(move || read_loop(id, reader, tx));
    }
}

/// Reads one connection line by line and forwards each line, until the client hangs up.
fn read_loop(id: ConnId, stream: TcpStream, tx: Sender<NetEvent>) {
    let mut r = BufReader::new(stream);
    let mut buf = Vec::new();
    loop {
        buf.clear();
        // Read at most MAX_LINE + 1 bytes, so a client sending one endless line cannot make
        // the server buffer it all.
        match (&mut r).take(MAX_LINE as u64 + 1).read_until(b'\n', &mut buf) {
            Ok(0) => break, // end of stream: the client closed
            Ok(_) => {
                if buf.len() > MAX_LINE && buf.last() != Some(&b'\n') {
                    eprintln!("[net] conn {id}: line over {MAX_LINE} bytes; closing");
                    break;
                }
                let line = String::from_utf8_lossy(&buf).trim().to_string();
                if !line.is_empty() && tx.send(NetEvent::Line(id, line)).is_err() {
                    return;
                }
            }
            Err(_) => break, // reset, or the writer shut the socket
        }
    }
    let _ = tx.send(NetEvent::Closed(id));
}

/// Writes queued lines until the queue is dropped, then closes the socket (which also ends
/// the reader). Lines already queued when the queue is dropped are still sent.
fn write_loop(stream: TcpStream, rx: Receiver<String>) {
    let shut = stream.try_clone();
    let mut w = BufWriter::new(stream);
    'outer: while let Ok(line) = rx.recv() {
        // Write everything waiting, then flush once: one system call per burst, not per line.
        let mut next = Some(line);
        while let Some(l) = next {
            if w.write_all(l.as_bytes()).and_then(|_| w.write_all(b"\n")).is_err() {
                break 'outer;
            }
            next = rx.try_recv().ok();
        }
        if w.flush().is_err() {
            break;
        }
    }
    let _ = w.flush();
    if let Ok(s) = shut {
        let _ = s.shutdown(Shutdown::Both);
    }
}

/// The simulation thread: runs the game at TICK_HZ and, between ticks, handles whatever the
/// socket threads report.
fn run(mut game: Game, rx: Receiver<NetEvent>) {
    let period = Duration::from_secs(1) / TICK_HZ;
    let mut conns: HashMap<ConnId, Conn> = HashMap::new();
    let mut next = Instant::now() + period; // when the next tick is due

    loop {
        // A tick is due: run it (and catch up a few if we fell behind), then send its output.
        let now = Instant::now();
        if now >= next {
            let mut n = 0;
            while Instant::now() >= next && n < MAX_CATCH_UP {
                game.step();
                next += period;
                n += 1;
            }
            if Instant::now() >= next {
                next = Instant::now() + period; // fell too far behind: skip ahead, don't spiral
            }
            flush(&mut game, &mut conns);
            continue;
        }

        // Otherwise wait for socket news, but never past the next tick.
        match rx.recv_timeout(next - now) {
            Ok(NetEvent::Open(id, stream)) => {
                // The game does not know about this connection until it says hello; it just
                // gets a writer, so the game can answer (or refuse) it.
                let (tx, out_rx) = mpsc::sync_channel(OUTBOX);
                match thread::Builder::new().name(format!("write-{id}")).spawn(move || write_loop(stream, out_rx)) {
                    Ok(_) => {
                        conns.insert(id, Conn { tx });
                    }
                    Err(e) => eprintln!("[net] conn {id}: {e}"),
                }
            }
            Ok(NetEvent::Line(id, line)) => match ClientMsg::decode(&line) {
                Ok(msg) => game.handle(id, msg),
                // A malformed line is logged and ignored, not fatal: clients may be newer.
                Err(e) => eprintln!("[net] conn {id}: ignored bad line ({e})"),
            },
            Ok(NetEvent::Closed(id)) => {
                eprintln!("[net] conn {id} closed");
                conns.remove(&id);
                game.leave(id);
            }
            Err(RecvTimeoutError::Timeout) => {}
            Err(RecvTimeoutError::Disconnected) => return,
        }
        flush(&mut game, &mut conns);
    }
}

/// Delivers everything the game wants sent. Dropping a client can make the game send more
/// (the partner hears about it), so this loops until the game has nothing left to say.
fn flush(game: &mut Game, conns: &mut HashMap<ConnId, Conn>) {
    loop {
        let out = game.take_out();
        if out.is_empty() {
            return;
        }
        for o in out {
            match o {
                Out::Send(id, line) => {
                    let Some(c) = conns.get(&id) else { continue };
                    // try_send never blocks: the game must not wait on a slow client.
                    match c.tx.try_send(line) {
                        Ok(()) => {}
                        Err(TrySendError::Full(_)) => {
                            eprintln!("[net] conn {id} is not keeping up; dropping it");
                            conns.remove(&id);
                            game.leave(id);
                        }
                        Err(TrySendError::Disconnected(_)) => {
                            conns.remove(&id);
                            game.leave(id);
                        }
                    }
                }
                // Dropping the queue lets the writer send what is queued, then close.
                Out::Close(id) => {
                    conns.remove(&id);
                }
            }
        }
    }
}
