//! End to end over real sockets: the server as the Unity client sees it.

mod common;

use std::io::{BufRead, BufReader, ErrorKind, Write};
use std::net::{SocketAddr, TcpListener, TcpStream};
use std::thread;
use std::time::Duration;

use serde_json::Value;

fn start() -> SocketAddr {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let addr = listener.local_addr().unwrap();
    let game = common::game("foothills-1");
    thread::spawn(move || hollow::net::serve(listener, game));
    addr
}

struct Client {
    w: TcpStream,
    r: BufReader<TcpStream>,
}

impl Client {
    fn connect(addr: SocketAddr, name: &str) -> Client {
        let s = TcpStream::connect(addr).unwrap();
        s.set_read_timeout(Some(Duration::from_secs(5))).unwrap();
        let mut c = Client { w: s.try_clone().unwrap(), r: BufReader::new(s) };
        c.send(&format!(r#"{{"t":"hello","name":"{name}"}}"#));
        c
    }

    fn send(&mut self, line: &str) {
        self.w.write_all(line.as_bytes()).unwrap();
        self.w.write_all(b"\n").unwrap();
    }

    /// The next line, or None once the server has closed the connection. A server that drops
    /// a client with unread input shows up as a reset rather than a clean end, on Windows.
    fn next(&mut self) -> Option<Value> {
        let mut line = String::new();
        match self.r.read_line(&mut line) {
            Ok(0) => None,
            Ok(_) => Some(serde_json::from_str(&line).unwrap()),
            Err(e) if matches!(e.kind(), ErrorKind::ConnectionReset | ErrorKind::ConnectionAborted) => None,
            Err(e) => panic!("read: {e}"),
        }
    }

    fn until(&mut self, pred: impl Fn(&Value) -> bool) -> Value {
        for _ in 0..500 {
            let v = self.next().expect("server closed the connection");
            if pred(&v) {
                return v;
            }
        }
        panic!("never saw the line we were waiting for");
    }
}

#[test]
fn a_client_is_welcomed_and_receives_snapshots() {
    let addr = start();
    let mut a = Client::connect(addr, "snep");
    let first = a.next().unwrap();
    assert_eq!(first["t"], "welcome");
    assert_eq!(first["client_id"], 0);
    assert_eq!(a.next().unwrap(), serde_json::json!({"t":"level","id":"foothills-1"}));

    let snap = a.until(|v| v["t"] == "snapshot");
    let ents = snap["entities"].as_array().unwrap();
    assert_eq!(ents[0], serde_json::json!([3, 4, 0, 0, -1]));

    // Steer east; the leopard should move within a few snapshots.
    a.send(r#"{"t":"input","tick":0,"move":"e","interact":false}"#);
    a.until(|v| v["t"] == "snapshot" && v["entities"][0][0] == 4);
}

#[test]
fn two_clients_share_the_trail_and_a_third_gets_no_welcome() {
    let addr = start();
    let mut a = Client::connect(addr, "snep");
    a.until(|v| v["t"] == "welcome");
    let mut b = Client::connect(addr, "pine");
    assert_eq!(b.until(|v| v["t"] == "welcome")["client_id"], 1);
    a.until(|v| v["t"] == "seats" && v["yours"] == serde_json::json!([0]));

    b.send(r#"{"t":"chat","from":0,"text":"on my way"}"#);
    let chat = a.until(|v| v["t"] == "chat" && v["from"] != 255);
    assert_eq!(chat, serde_json::json!({"t":"chat","from":1,"text":"on my way"}));

    let mut c = Client::connect(addr, "third");
    assert_eq!(c.next(), None, "the third connection is closed without a welcome");

    // When one leaves, the other is handed both leopards.
    drop(b);
    a.until(|v| v["t"] == "seats" && v["yours"] == serde_json::json!([0, 1]));
}

#[test]
fn junk_lines_are_ignored_and_overlong_lines_drop_the_client() {
    let addr = start();
    let mut a = Client::connect(addr, "snep");
    a.until(|v| v["t"] == "welcome");
    a.send("this is not json");
    a.send(r#"{"t":"teleport","x":1}"#);
    a.until(|v| v["t"] == "snapshot"); // still served

    let big = "x".repeat(hollow::protocol::MAX_LINE * 2);
    a.send(&big);
    // Drain until the server closes the socket.
    let mut closed = false;
    for _ in 0..500 {
        if a.next().is_none() {
            closed = true;
            break;
        }
    }
    assert!(closed);
}
