//! The session: who is connected, who steers which leopard, the clock and the trail.
//! Pure state, no sockets: the network layer feeds it messages and ticks, and sends whatever
//! it leaves in its outbox. Mirrors `Crawler.Mock.MockFeed`, plus a second player.
//!
//! Seats: two clients get one leopard each (client joined first steers seat 0). A client
//! alone holds both and switches with `control`. A third connection is refused.
//!
//! Not built yet (see docs/levels-design.md): the disconnect pause and take-both choice (a
//! dropped partner's leopard goes straight to the remaining player), major failures, the
//! vote reset, checkpoints, and canonical encoding for hash parity.

use crate::level::{Level, Trail};
use crate::protocol::{clean_text, ClientMsg, RoundState, ServerMsg, MAX_CHAT, MAX_NAME, SERVER_SENDER};
use crate::sim::Sim;
use crate::world::{SimEvent, SEATS};

pub const TICK_HZ: u32 = 60;
pub const REPLICATION_HZ: u32 = 20;
pub const TICKS_PER_SNAPSHOT: u32 = TICK_HZ / REPLICATION_HZ;
/// Six seconds on the summit card before the next level.
pub const WIN_HOLD_TICKS: u32 = 360;
pub const MAX_CLIENTS: usize = 2;

pub type ConnId = u64;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Out {
    Send(ConnId, String),
    /// Drop this connection (refused, or it broke the protocol).
    Close(ConnId),
}

#[derive(Debug, Clone)]
struct Client {
    conn: ConnId,
    id: u8,
    name: String,
    seats: Vec<usize>,
    active: usize,
}

pub type Loader = Box<dyn Fn(&str) -> Result<Level, String> + Send>;

pub struct Game {
    load: Loader,
    trail: Option<Trail>,
    start_level: String,
    sim: Sim,
    tick: u32,
    clients: Vec<Client>,
    advance_at: u32,
    trail_complete: bool,
    out: Vec<Out>,
}

impl Game {
    pub fn new(start_level: &str, trail: Option<Trail>, load: Loader) -> Result<Game, String> {
        let sim = Sim::new(load(start_level)?)?;
        Ok(Game {
            load,
            trail,
            start_level: start_level.to_string(),
            sim,
            tick: 0,
            clients: Vec::new(),
            advance_at: 0,
            trail_complete: false,
            out: Vec::new(),
        })
    }

    pub fn tick(&self) -> u32 {
        self.tick
    }

    pub fn level_id(&self) -> &str {
        &self.sim.world.level.id
    }

    pub fn sim(&self) -> &Sim {
        &self.sim
    }

    pub fn player_count(&self) -> usize {
        self.clients.len()
    }

    pub fn take_out(&mut self) -> Vec<Out> {
        std::mem::take(&mut self.out)
    }

    // ------------------------------------------------------------------ connections

    /// A message from a connection. The first must be `hello`; anything else closes it.
    pub fn handle(&mut self, conn: ConnId, msg: ClientMsg) {
        let Some(ci) = self.clients.iter().position(|c| c.conn == conn) else {
            match msg {
                ClientMsg::Hello { name } => self.join(conn, &name),
                _ => self.out.push(Out::Close(conn)),
            }
            return;
        };

        match msg {
            ClientMsg::Hello { .. } => {}
            ClientMsg::Input { dir, interact, .. } => {
                let seat = self.clients[ci].active;
                self.sim.set_input(seat, dir, interact);
            }
            ClientMsg::Chat { text, .. } => {
                let text = clean_text(&text, MAX_CHAT);
                if !text.is_empty() {
                    let from = self.clients[ci].id;
                    self.broadcast(&ServerMsg::Chat { from, text });
                }
            }
            ClientMsg::Control { seat } => {
                let seat = seat as usize;
                let c = &self.clients[ci];
                if seat == c.active || !c.seats.contains(&seat) {
                    return;
                }
                self.sim.release(c.active);
                self.clients[ci].active = seat;
                self.send_seats(ci);
                self.send_overlay(ci);
            }
        }
    }

    fn join(&mut self, conn: ConnId, name: &str) {
        if self.clients.len() >= MAX_CLIENTS {
            eprintln!("[game] refused conn {conn}: both seats are taken");
            self.out.push(Out::Close(conn));
            return;
        }
        let mut name = clean_text(name, MAX_NAME);
        if name.is_empty() {
            name = "leopard".into();
        }
        let id = (0..MAX_CLIENTS as u8).find(|i| self.clients.iter().all(|c| c.id != *i)).unwrap();
        eprintln!("[game] {name} joined as client {id} (conn {conn})");
        self.clients.push(Client { conn, id, name: name.clone(), seats: Vec::new(), active: 0 });
        self.rebalance(Some(conn));

        let ci = self.clients.len() - 1;
        let lname = self.sim.world.level.name.clone();
        self.send(ci, &self.welcome(ci));
        self.send(ci, &ServerMsg::Level { id: self.level_id().to_string() });
        self.send_seats(ci);
        self.send_overlay(ci);
        let state = if self.sim.won() { RoundState::Won } else { RoundState::Playing };
        self.send(ci, &ServerMsg::Round { tick: self.tick, state });

        let greeting = if self.clients.len() == 1 {
            format!("welcome to {lname}, {name}. you hold both leopards: Tab switches between them.")
        } else {
            format!("welcome to {lname}, {name}. you each steer one leopard.")
        };
        self.whisper(ci, greeting);
        for other in 0..self.clients.len() {
            if other != ci {
                self.whisper(other, format!("{name} joins. you each steer one leopard now."));
            }
        }
    }

    pub fn leave(&mut self, conn: ConnId) {
        let Some(ci) = self.clients.iter().position(|c| c.conn == conn) else { return };
        let gone = self.clients.remove(ci);
        eprintln!("[game] {} left (client {})", gone.name, gone.id);
        for &s in &gone.seats {
            self.sim.release(s);
        }

        if self.clients.is_empty() {
            self.reset_to_start();
            return;
        }
        self.rebalance(None);
        self.say(format!("{} left the trail. you hold both leopards now: Tab switches between them.", gone.name));
    }

    /// Nobody is left: the next arrival starts the trail fresh.
    fn reset_to_start(&mut self) {
        match (self.load)(&self.start_level).and_then(Sim::new) {
            Ok(sim) => self.sim = sim,
            Err(e) => {
                eprintln!("[game] could not reload {}: {e}; restarting the current level", self.start_level);
                self.sim.restart();
            }
        }
        self.sim.drain_events();
        self.trail_complete = false;
        self.advance_at = 0;
    }

    /// One client holds both seats; two hold one each, in join order. Seats that change hands
    /// drop their held input, and every client whose seats changed is told.
    fn rebalance(&mut self, skip: Option<ConnId>) {
        let n = self.clients.len();
        let mut changed = Vec::new();
        for (i, c) in self.clients.iter_mut().enumerate() {
            let (old_seats, old_active) = (std::mem::take(&mut c.seats), c.active);
            c.seats = if n == 1 { (0..SEATS).collect() } else { vec![i] };
            if !c.seats.contains(&c.active) {
                c.active = c.seats[0];
            }
            if c.active != old_active {
                self.sim.release(old_active);
            }
            if (old_seats != c.seats || old_active != c.active) && Some(c.conn) != skip {
                changed.push(i);
            }
        }
        for ci in changed {
            self.send_seats(ci);
            self.send_overlay(ci);
        }
    }

    // ------------------------------------------------------------------ clock

    /// Advance one simulation tick. The world holds still while nobody is connected.
    pub fn step(&mut self) {
        if self.clients.is_empty() {
            return;
        }
        let t = self.tick;
        let was_won = self.sim.won();
        self.sim.step(t);
        self.flush_events(t);
        if !was_won && self.sim.won() {
            self.advance_at = t + WIN_HOLD_TICKS;
        }
        if self.sim.won() && !self.trail_complete && t >= self.advance_at {
            self.advance();
        }
        if t.is_multiple_of(TICKS_PER_SNAPSHOT) {
            self.broadcast(&ServerMsg::Snapshot { tick: t, entities: self.sim.snapshot() });
        }
        self.tick += 1;
    }

    fn flush_events(&mut self, t: u32) {
        for ev in self.sim.drain_events() {
            match ev {
                SimEvent::Say(text) => self.say(text),
                SimEvent::Won => self.broadcast(&ServerMsg::Round { tick: t, state: RoundState::Won }),
                SimEvent::Perception => {
                    for ci in 0..self.clients.len() {
                        self.send_overlay(ci);
                    }
                }
            }
        }
    }

    /// After a win: the next level on the trail, or the same level again if there is no trail.
    fn advance(&mut self) {
        let current = self.level_id().to_string();
        let next = self.trail.as_ref().map(|t| t.next(&current).map(str::to_string));
        match next {
            Some(None) => {
                self.trail_complete = true;
                self.say("the trail is complete. well walked.");
                return;
            }
            Some(Some(id)) => match (self.load)(&id).and_then(Sim::new) {
                Ok(sim) => self.sim = sim,
                Err(e) => {
                    eprintln!("[game] {e}");
                    self.say(format!("the trail goes on to '{id}', but that level would not load."));
                    self.trail_complete = true;
                    return;
                }
            },
            None => {}
        }
        self.begin_level();
    }

    fn begin_level(&mut self) {
        for ci in 0..self.clients.len() {
            self.send(ci, &self.welcome(ci));
            self.send(ci, &ServerMsg::Level { id: self.level_id().to_string() });
        }
        self.sim.restart();
        self.sim.drain_events();
        // Each level starts steering the first leopard you hold, as the mock does.
        for c in &mut self.clients {
            c.active = c.seats[0];
        }
        for ci in 0..self.clients.len() {
            self.send_overlay(ci);
        }
        self.broadcast(&ServerMsg::Round { tick: self.tick, state: RoundState::Playing });
        for ci in 0..self.clients.len() {
            self.send_seats(ci);
        }
        let name = self.sim.world.level.name.clone();
        eprintln!("[game] level {}", self.level_id());
        self.say(format!("onward: {name}."));
    }

    // ------------------------------------------------------------------ output

    fn welcome(&self, ci: usize) -> ServerMsg {
        let level = &self.sim.world.level;
        ServerMsg::Welcome {
            client_id: self.clients[ci].id,
            width: level.width,
            height: level.height,
            tick_hz: TICK_HZ,
            replication_hz: REPLICATION_HZ,
        }
    }

    fn send(&mut self, ci: usize, msg: &ServerMsg) {
        self.out.push(Out::Send(self.clients[ci].conn, msg.encode()));
    }

    fn broadcast(&mut self, msg: &ServerMsg) {
        let line = msg.encode();
        for c in &self.clients {
            self.out.push(Out::Send(c.conn, line.clone()));
        }
    }

    fn say(&mut self, text: impl Into<String>) {
        self.broadcast(&ServerMsg::Chat { from: SERVER_SENDER, text: text.into() });
    }

    fn whisper(&mut self, ci: usize, text: String) {
        self.send(ci, &ServerMsg::Chat { from: SERVER_SENDER, text });
    }

    fn send_seats(&mut self, ci: usize) {
        let c = &self.clients[ci];
        let msg = ServerMsg::Seats { yours: c.seats.iter().map(|&s| s as u8).collect(), active: c.active as u8 };
        self.send(ci, &msg);
    }

    /// Perception follows the seat the client is steering.
    fn send_overlay(&mut self, ci: usize) {
        let p = self.sim.perception(self.clients[ci].active);
        let msg = ServerMsg::Overlay { tick: self.tick, hidden: p.hidden, substituted: p.substituted, phantoms: p.phantoms };
        self.send(ci, &msg);
    }
}
