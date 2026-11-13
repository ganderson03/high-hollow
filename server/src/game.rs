//! The session: who is connected, who steers which leopard, the clock, the trail, the
//! disconnect pause and the restart vote. Pure state, no sockets: the network layer feeds it
//! messages and ticks, and sends whatever it leaves in its outbox. Mirrors
//! `Crawler.Mock.MockFeed`, plus a second player.
//!
//! Seats: one client alone holds both leopards and switches with `control`. A second client
//! takes the leopard the first is not steering. A third connection is refused.
//!
//! Disconnects (docs/levels-design.md, "Seats"): when one of two players drops, the world
//! freezes for 60 s while they may come back. After that the one who stayed picks `both`
//! (take both leopards) or `quit` (leave the trail). Whoever connects next takes the empty
//! seat, during the pause or after; there is no identity beyond the name, so "coming back"
//! means any second player joining.
//!
//! Vote reset: either player proposes with `vote` `reset: true`; it passes when the partner
//! agrees within 600 ticks, at once for a player holding both seats. A pass restarts the
//! level, or restores its lit checkpoint, exactly like a major failure.

use crate::canon;
use crate::level::{Level, Trail};
use crate::protocol::{
    clean_text, ClientMsg, Pick, RoundReason, RoundState, ServerMsg, VoteState, MAX_CHAT, MAX_NAME, SERVER_SENDER,
};
use crate::sim::{Sim, PUSH_WINDOW, SOLO_PUSH_WINDOW};
use crate::world::{RestartCause, SimEvent, SEATS};

pub const TICK_HZ: u32 = 60;
pub const REPLICATION_HZ: u32 = 20;
pub const TICKS_PER_SNAPSHOT: u32 = TICK_HZ / REPLICATION_HZ;
/// Six seconds on the summit card before the next level.
pub const WIN_HOLD_TICKS: u32 = 360;
/// How long a dropped partner has to come back.
pub const PAUSE_SECONDS: u32 = 60;
/// How long a restart proposal waits for the partner (10 s).
pub const VOTE_TICKS: u32 = 600;
pub const MAX_CLIENTS: usize = 2;

pub type ConnId = u64;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Out {
    Send(ConnId, String),
    /// Drop this connection (refused, it left the trail, or it broke the protocol).
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

#[derive(Debug, Clone)]
struct Pause {
    /// Who dropped.
    name: String,
    /// Loop ticks left before the choice; counted even though the world is frozen.
    ticks_left: u32,
    choosing: bool,
}

#[derive(Debug, Clone)]
struct Vote {
    by_conn: ConnId,
    by: u8,
    ticks_left: u32,
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
    pause: Option<Pause>,
    vote: Option<Vote>,
    /// Inputs stamped before this tick were sent for an earlier round and are dropped.
    round_start: u32,
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
            pause: None,
            vote: None,
            round_start: 0,
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

    pub fn paused(&self) -> bool {
        self.pause.is_some()
    }

    pub fn take_out(&mut self) -> Vec<Out> {
        std::mem::take(&mut self.out)
    }

    // ------------------------------------------------------------------ connections

    /// A message from a connection. The first must be `hello`; anything else closes it.
    pub fn handle(&mut self, conn: ConnId, msg: ClientMsg) {
        let Some(ci) = self.client(conn) else {
            match msg {
                ClientMsg::Hello { name } => self.join(conn, &name),
                _ => self.out.push(Out::Close(conn)),
            }
            return;
        };

        match msg {
            ClientMsg::Hello { .. } => {}
            ClientMsg::Input { tick, dir, interact } => {
                if self.pause.is_some() || tick < self.round_start || tick > self.tick + TICK_HZ {
                    return; // frozen, or sent for an earlier round, or from a clock that is not ours
                }
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
                if self.pause.is_some() || seat == c.active || !c.seats.contains(&seat) {
                    return;
                }
                self.sim.release(c.active);
                self.clients[ci].active = seat;
                self.send_seats(ci);
                self.send_overlay(ci);
            }
            ClientMsg::Choice { pick } => {
                if !self.pause.as_ref().is_some_and(|p| p.choosing) {
                    return;
                }
                match pick {
                    Pick::Both => {
                        self.pause = None;
                        self.rebalance(None);
                        self.resume(None);
                        self.say("you hold both leopards now: Tab switches between them.");
                    }
                    Pick::Quit => {
                        self.out.push(Out::Close(conn));
                        self.leave(conn);
                    }
                }
            }
            ClientMsg::Vote { reset } => self.vote(ci, reset),
        }
    }

    fn client(&self, conn: ConnId) -> Option<usize> {
        self.clients.iter().position(|c| c.conn == conn)
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
        self.cancel_vote();
        self.rebalance(Some(conn));

        let ci = self.clients.len() - 1;
        let lname = self.sim.world.level.name.clone();
        self.send(ci, &self.welcome(ci));
        self.send(ci, &ServerMsg::Level { id: self.level_id().to_string() });
        self.send_seats(ci);
        self.send_overlay(ci);
        let state = if self.sim.won() { RoundState::Won } else { RoundState::Playing };
        self.send(ci, &ServerMsg::round(self.tick, state));

        let greeting = if self.clients.len() == 1 {
            format!("welcome to {lname}, {name}. you hold both leopards: Tab switches between them.")
        } else {
            format!("welcome to {lname}, {name}. you each steer one leopard.")
        };
        self.whisper(ci, greeting);

        if let Some(p) = self.pause.take() {
            let back = if p.name == name { format!("{name} is back.") } else { format!("{name} takes {}'s leopard.", p.name) };
            self.resume(Some(ci));
            for other in 0..self.clients.len() {
                if other != ci {
                    self.whisper(other, format!("{back} the trail goes on."));
                }
            }
        } else {
            for other in 0..self.clients.len() {
                if other != ci {
                    self.whisper(other, format!("{name} joins. you each steer one leopard now."));
                }
            }
        }
    }

    pub fn leave(&mut self, conn: ConnId) {
        let Some(ci) = self.client(conn) else { return };
        let gone = self.clients.remove(ci);
        eprintln!("[game] {} left (client {})", gone.name, gone.id);
        for &s in &gone.seats {
            self.sim.release(s);
        }
        self.cancel_vote();

        if self.clients.is_empty() {
            self.reset_to_start();
            return;
        }
        if self.trail_complete {
            self.rebalance(None);
            return;
        }
        // The one who stayed keeps their own leopard while the world waits.
        self.sim.release_all();
        self.pause = Some(Pause { name: gone.name.clone(), ticks_left: PAUSE_SECONDS * TICK_HZ, choosing: false });
        self.broadcast(&self.paused_msg(PAUSE_SECONDS));
        self.say(format!("{} dropped. the trail waits a minute for them to come back.", gone.name));
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
        self.pause = None;
        self.vote = None;
        self.round_start = self.tick;
    }

    /// One client holds both seats. With two, a client that already steers one leopard keeps
    /// it, and the other client takes the other. Seats that change hands drop their held
    /// input, and every client whose seats changed (except `skip`) is told.
    fn rebalance(&mut self, skip: Option<ConnId>) {
        let n = self.clients.len();
        let keep = self.clients.first().map(|c| c.active).unwrap_or(0);
        let mut changed = Vec::new();
        for (i, c) in self.clients.iter_mut().enumerate() {
            let (old_seats, old_active) = (std::mem::take(&mut c.seats), c.active);
            c.seats = match (n, i) {
                (1, _) => (0..SEATS).collect(),
                (_, 0) => vec![keep],
                _ => vec![1 - keep],
            };
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
        self.update_push_window();
    }

    /// A slab push stays live longer for a player who has to press Tab between pushes.
    fn update_push_window(&mut self) {
        let solo = self.clients.len() == 1 && self.clients[0].seats.len() == SEATS;
        self.sim.set_push_window(if solo { SOLO_PUSH_WINDOW } else { PUSH_WINDOW });
    }

    /// The pause is over: everyone hears the round is back on (except `skip`, already told).
    fn resume(&mut self, skip: Option<usize>) {
        self.round_start = self.tick;
        let state = if self.sim.won() { RoundState::Won } else { RoundState::Playing };
        for ci in 0..self.clients.len() {
            if Some(ci) != skip {
                self.send(ci, &ServerMsg::round(self.tick, state));
            }
        }
    }

    fn paused_msg(&self, seconds: u32) -> ServerMsg {
        ServerMsg::Round { tick: self.tick, state: RoundState::Paused, reason: Some(RoundReason::PartnerLeft), seconds: Some(seconds) }
    }

    // ------------------------------------------------------------------ vote

    fn vote(&mut self, ci: usize, reset: bool) {
        if self.pause.is_some() || self.sim.won() {
            return;
        }
        let (conn, id) = (self.clients[ci].conn, self.clients[ci].id);
        match (&self.vote, reset) {
            (Some(v), true) if v.by_conn != conn => self.pass_vote(v.by),
            (Some(_), true) => {} // already proposed
            (Some(_), false) => self.lapse_vote(),
            (None, true) if self.clients.len() == 1 => self.pass_vote(id),
            (None, true) => {
                self.vote = Some(Vote { by_conn: conn, by: id, ticks_left: VOTE_TICKS });
                self.broadcast(&ServerMsg::Vote { by: id, ticks_left: VOTE_TICKS, state: VoteState::Open });
                let name = self.clients[ci].name.clone();
                self.say(format!("{name} wants to restart the level. press R to agree."));
            }
            (None, false) => {}
        }
    }

    fn pass_vote(&mut self, by: u8) {
        self.vote = None;
        self.broadcast(&ServerMsg::Vote { by, ticks_left: 0, state: VoteState::Passed });
        let to_checkpoint = self.sim.checkpoint_lit();
        self.say(if to_checkpoint { "agreed: back to the checkpoint." } else { "agreed: the level starts over." });
        self.sim.restart_latest(RestartCause::Vote);
        let t = self.tick;
        self.flush_events(t);
    }

    fn lapse_vote(&mut self) {
        if let Some(v) = self.vote.take() {
            self.broadcast(&ServerMsg::Vote { by: v.by, ticks_left: 0, state: VoteState::Lapsed });
        }
    }

    fn cancel_vote(&mut self) {
        self.lapse_vote();
    }

    // ------------------------------------------------------------------ clock

    /// Advance one tick. The world holds still while nobody is connected, and while a dropped
    /// partner has time to come back (only the pause countdown runs then).
    pub fn step(&mut self) {
        if self.clients.is_empty() {
            return;
        }
        if self.pause.is_some() {
            self.count_pause();
            return;
        }

        let t = self.tick;
        if let Some(v) = &mut self.vote {
            v.ticks_left = v.ticks_left.saturating_sub(1);
            if v.ticks_left == 0 {
                self.lapse_vote();
            }
        }
        let was_won = self.sim.won();
        self.sim.step(t);
        self.flush_events(t);
        if !was_won && self.sim.won() {
            self.advance_at = t + WIN_HOLD_TICKS;
            self.cancel_vote();
        }
        if self.sim.won() && !self.trail_complete && t >= self.advance_at {
            self.advance();
        }
        if t.is_multiple_of(TICKS_PER_SNAPSHOT) {
            let (entities, water) = self.sim.snapshot();
            let hash = Some(canon::hash_hex(t, &entities, &water));
            self.broadcast(&ServerMsg::Snapshot { tick: t, entities, water, hash });
        }
        self.tick += 1;
    }

    fn count_pause(&mut self) {
        let p = self.pause.as_mut().unwrap();
        if p.choosing {
            return;
        }
        p.ticks_left = p.ticks_left.saturating_sub(1);
        let left = p.ticks_left;
        if left > 0 {
            if left.is_multiple_of(TICK_HZ) {
                let msg = self.paused_msg(left / TICK_HZ);
                self.broadcast(&msg);
            }
            return;
        }
        p.choosing = true;
        let name = p.name.clone();
        self.broadcast(&ServerMsg::Round { tick: self.tick, state: RoundState::Choose, reason: Some(RoundReason::PartnerLeft), seconds: None });
        self.say(format!("{name} has not come back. take both leopards, or leave the trail?"));
    }

    fn flush_events(&mut self, t: u32) {
        for ev in self.sim.drain_events() {
            match ev {
                SimEvent::Say(text) => self.say(text),
                SimEvent::Won => self.broadcast(&ServerMsg::round(t, RoundState::Won)),
                SimEvent::Perception => {
                    for ci in 0..self.clients.len() {
                        self.send_overlay(ci);
                    }
                }
                SimEvent::Restart { cause, .. } => {
                    self.round_start = t;
                    self.cancel_vote();
                    let reason = match cause {
                        RestartCause::Fell => RoundReason::Fell,
                        RestartCause::Vote => RoundReason::Vote,
                    };
                    self.broadcast(&ServerMsg::Round { tick: t, state: RoundState::Playing, reason: Some(reason), seconds: None });
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
        self.update_push_window();
        self.round_start = self.tick;
        // Each level starts steering the first leopard you hold, as the mock does.
        for c in &mut self.clients {
            c.active = c.seats[0];
        }
        for ci in 0..self.clients.len() {
            self.send_overlay(ci);
        }
        self.broadcast(&ServerMsg::round(self.tick, RoundState::Playing));
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
