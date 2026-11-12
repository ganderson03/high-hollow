//! Wire messages: JSON Lines over TCP, one object per line, discriminated by `t`.
//!
//! Field names and order match `Crawler.Protocol.WireCodec` in the client. This is transport
//! encoding only; the canonical encoding that will be hashed for parity is not built yet.

use serde::{Deserialize, Serialize};

/// Sender id the client shows as "server".
pub const SERVER_SENDER: u8 = 255;

/// Longest line the server will read from a client, in bytes.
pub const MAX_LINE: usize = 4096;

/// Longest chat message relayed, in characters.
pub const MAX_CHAT: usize = 200;

/// Longest player name kept, in characters.
pub const MAX_NAME: usize = 24;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Move {
    N,
    S,
    E,
    W,
    None,
}

impl Move {
    pub fn delta(self) -> (i32, i32) {
        match self {
            Move::N => (0, 1),
            Move::S => (0, -1),
            Move::E => (1, 0),
            Move::W => (-1, 0),
            Move::None => (0, 0),
        }
    }
}

/// Client to server.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "t", rename_all = "snake_case")]
pub enum ClientMsg {
    Hello {
        name: String,
    },
    Input {
        #[serde(default)]
        tick: u32,
        #[serde(rename = "move")]
        dir: Move,
        #[serde(default)]
        interact: bool,
    },
    /// `from` is whatever the client claims; the server ignores it and stamps the real id.
    Chat {
        #[serde(default)]
        from: u8,
        text: String,
    },
    Control {
        seat: u8,
    },
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum RoundState {
    Playing,
    Won,
}

/// `[x, y, kind, state, carried_by]`; `carried_by` is -1 or the carrying seat.
pub type EntityWire = (i32, i32, u8, u8, i8);

/// Server to client.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "t", rename_all = "snake_case")]
pub enum ServerMsg {
    Welcome {
        client_id: u8,
        width: i32,
        height: i32,
        tick_hz: u32,
        replication_hz: u32,
    },
    Seats {
        yours: Vec<u8>,
        active: u8,
    },
    Snapshot {
        tick: u32,
        entities: Vec<EntityWire>,
    },
    Overlay {
        tick: u32,
        hidden: Vec<u32>,
        substituted: Vec<(u32, u8)>,
        phantoms: Vec<(i32, i32, u8)>,
    },
    Chat {
        from: u8,
        text: String,
    },
    Level {
        id: String,
    },
    Round {
        tick: u32,
        state: RoundState,
    },
}

impl ServerMsg {
    pub fn encode(&self) -> String {
        serde_json::to_string(self).expect("server messages always serialize")
    }
}

impl ClientMsg {
    pub fn decode(line: &str) -> Result<ClientMsg, String> {
        serde_json::from_str(line).map_err(|e| e.to_string())
    }

    pub fn encode(&self) -> String {
        serde_json::to_string(self).expect("client messages always serialize")
    }
}

/// Trims, drops control characters and caps the length.
pub fn clean_text(s: &str, max: usize) -> String {
    s.trim().chars().filter(|c| !c.is_control()).take(max).collect()
}
