//! TCP, JSON Lines. One thread accepts; each connection gets a reader thread (lines in) and a
//! writer thread (lines out, through a bounded queue). One simulation thread owns the Game
//! and runs it at TICK_HZ, so game state is never shared.

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

enum NetEvent {
    Open(ConnId, TcpStream),
    Line(ConnId, String),
    Closed(ConnId),
}

struct Conn {
    tx: SyncSender<String>,
}

/// Runs the server on an already-bound listener. Returns only on a fatal error.
pub fn serve(listener: TcpListener, game: Game) -> io::Result<()> {
    let (tx, rx) = mpsc::channel();
    thread::Builder::new().name("accept".into()).spawn(move || accept_loop(listener, tx))?;
    run(game, rx);
    Ok(())
}

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
        let _ = stream.set_nodelay(true);
        let peer = stream.peer_addr().map(|a| a.to_string()).unwrap_or_else(|_| "?".into());
        eprintln!("[net] conn {id} from {peer}");

        let reader = match stream.try_clone() {
            Ok(r) => r,
            Err(e) => {
                eprintln!("[net] conn {id}: {e}");
                continue;
            }
        };
        if tx.send(NetEvent::Open(id, stream)).is_err() {
            return;
        }
        let tx = tx.clone();
        let _ = thread::Builder::new().name(format!("read-{id}")).spawn(move || read_loop(id, reader, tx));
    }
}

fn read_loop(id: ConnId, stream: TcpStream, tx: Sender<NetEvent>) {
    let mut r = BufReader::new(stream);
    let mut buf = Vec::new();
    loop {
        buf.clear();
        match (&mut r).take(MAX_LINE as u64 + 1).read_until(b'\n', &mut buf) {
            Ok(0) => break,
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
            Err(_) => break,
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

fn run(mut game: Game, rx: Receiver<NetEvent>) {
    let period = Duration::from_secs(1) / TICK_HZ;
    let mut conns: HashMap<ConnId, Conn> = HashMap::new();
    let mut next = Instant::now() + period;

    loop {
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

        match rx.recv_timeout(next - now) {
            Ok(NetEvent::Open(id, stream)) => {
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
