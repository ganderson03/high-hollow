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
use crate::hlog;
use crate::level::{Level, Trail};
use crate::protocol::{
    clean_text, ClientMsg, Pick, RoundReason, RoundState, ServerMsg, VoteState, MAX_CHAT, MAX_NAME, SERVER_SENDER,
};
use crate::sim::{Sim, PUSH_WINDOW, SOLO_PUSH_WINDOW};
use crate::world::{RestartCause, SimEvent, SEATS};

/// Simulation ticks per second. Every timing in the rules is a number of these.
pub const TICK_HZ: u32 = 60;
/// Snapshots sent per second. The client draws smoothly between them, so 20 is plenty.
pub const REPLICATION_HZ: u32 = 20;
/// A snapshot goes out every third tick.
pub const TICKS_PER_SNAPSHOT: u32 = TICK_HZ / REPLICATION_HZ;
/// Six seconds on the summit card before the next level.
pub const WIN_HOLD_TICKS: u32 = 360;
/// How long a dropped partner has to come back.
pub const PAUSE_SECONDS: u32 = 60;
/// How long a restart proposal waits for the partner (10 s).
pub const VOTE_TICKS: u32 = 600;
/// Two leopards, so at most two players; a third connection is refused.
pub const MAX_CLIENTS: usize = 2;
/// A `stat` line goes into the log this often while someone is playing (30 s).
pub const STATUS_TICKS: u32 = 30 * TICK_HZ;

/// A connection's id, given by the network layer. Not the same as a player's `client_id`
/// (0 or 1), which is what the protocol shows.
pub type ConnId = u64;

/// What the game wants the network layer to do. The game never touches a socket; it leaves
/// these in its outbox and `net.rs` carries them out.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Out {
    Send(ConnId, String),
    /// Drop this connection (refused, it left the trail, or it broke the protocol).
    Close(ConnId),
}

/// A player who has said hello.
#[derive(Debug, Clone)]
struct Client {
    conn: ConnId,
    /// 0 or 1: the id the protocol shows (in `welcome`, and as the sender of chat).
    id: u8,
    name: String,
    /// The leopards this player controls: both when alone, one each with a partner.
    seats: Vec<usize>,
    /// The leopard this player is steering right now; inputs move it.
    active: usize,
}

/// The world is frozen because a partner dropped.
#[derive(Debug, Clone)]
struct Pause {
    /// Who dropped.
    name: String,
    /// Loop ticks left before the choice; counted even though the world is frozen.
    ticks_left: u32,
    /// The minute is up and the stayer has been asked to pick `both` or `quit`.
    choosing: bool,
}

/// An open proposal to restart the level.
#[derive(Debug, Clone)]
struct Vote {
    /// Who proposed it (their agreeing again does nothing).
    by_conn: ConnId,
    by: u8,
    /// Counts down every tick; at zero the proposal lapses.
    ticks_left: u32,
}

/// Loads a level by id. Passed in, so the game itself never reads files.
pub type Loader = Box<dyn Fn(&str) -> Result<Level, String> + Send>;

/// The whole session. See the module documentation.
pub struct Game {
    load: Loader,
    /// The order of levels; `None` replays one level forever.
    trail: Option<Trail>,
    /// Where the trail starts, and where it goes back to when everyone leaves.
    start_level: String,
    /// The level being played.
    sim: Sim,
    /// The simulation clock (see `tick()`).
    tick: u32,
    /// The players who have said hello, in the order they joined.
    clients: Vec<Client>,
    /// After a win, the tick at which the next level loads (the summit card stays up till then).
    advance_at: u32,
    /// The last level has been won; nothing more to load.
    trail_complete: bool,
    /// Set while waiting for a dropped partner.
    pause: Option<Pause>,
    /// Set while a restart proposal is open.
    vote: Option<Vote>,
    /// Inputs stamped before this tick were sent for an earlier round and are dropped.
    round_start: u32,
    /// The tick the current level began, and how often it has restarted since: for the log.
    level_began: u32,
    restarts: u32,
    /// What to send or close; emptied by `take_out`.
    out: Vec<Out>,
}

impl Game {
    /// A session waiting at the start of the trail, with nobody connected yet.
    pub fn new(start_level: &str, trail: Option<Trail>, load: Loader) -> Result<Game, String> {
        let sim = Sim::new(load(start_level)?)?;
        let game = Game {
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
            level_began: 0,
            restarts: 0,
            out: Vec::new(),
        };
        game.log_level();
        Ok(game)
    }

    /// The current simulation tick. It stops advancing while nobody is connected or the world
    /// is paused.
    pub fn tick(&self) -> u32 {
        self.tick
    }

    /// The id of the level being played, as in its file name.
    pub fn level_id(&self) -> &str {
        &self.sim.world.level.id
    }

    /// The simulation, read-only (for the tests).
    pub fn sim(&self) -> &Sim {
        &self.sim
    }

    /// How many players have said hello.
    pub fn player_count(&self) -> usize {
        self.clients.len()
    }

    /// True while waiting for a dropped partner, or for the stayer's choice.
    pub fn paused(&self) -> bool {
        self.pause.is_some()
    }

    /// Everything the game wants sent or closed since the last call. The network layer calls
    /// this after every tick and every message.
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
                    hlog!("chat", "[{}] {}: {text}", self.level_id(), self.clients[ci].name);
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
                self.log(format_args!("{} switches to leopard {seat}", c.name));
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
                        self.log(format_args!("{} takes both leopards and plays on alone", self.clients[ci].name));
                        self.pause = None;
                        self.rebalance(None);
                        self.resume(None);
                        self.say("you hold both leopards now: Tab switches between them.");
                    }
                    Pick::Quit => {
                        self.log(format_args!("{} leaves the trail rather than play on alone", self.clients[ci].name));
                        self.out.push(Out::Close(conn));
                        self.leave(conn);
                    }
                }
            }
            ClientMsg::Vote { reset } => self.vote(ci, reset),
        }
    }

    /// The index in `clients` of the player on this connection, if they have said hello.
    fn client(&self, conn: ConnId) -> Option<usize> {
        self.clients.iter().position(|c| c.conn == conn)
    }

    /// A `hello` from a new connection: refuse a third player, otherwise seat them and send
    /// everything a client needs to draw the level. If the world was paused for a dropped
    /// partner, the newcomer takes that leopard and play resumes.
    fn join(&mut self, conn: ConnId, name: &str) {
        if self.clients.len() >= MAX_CLIENTS {
            self.log(format_args!("refused conn {conn}: both seats are taken"));
            self.out.push(Out::Close(conn));
            return;
        }
        let mut name = clean_text(name, MAX_NAME);
        if name.is_empty() {
            name = "leopard".into();
        }
        let id = (0..MAX_CLIENTS as u8).find(|i| self.clients.iter().all(|c| c.id != *i)).unwrap();
        self.clients.push(Client { conn, id, name: name.clone(), seats: Vec::new(), active: 0 });
        self.cancel_vote();
        self.rebalance(Some(conn));
        // play.ps1 waits for "joined as client" before it starts the next player.
        let holds = match self.clients.last().unwrap().seats.as_slice() {
            [s] => format!("steers leopard {s}"),
            _ => "holds both leopards".to_string(),
        };
        self.log(format_args!("{name} joined as client {id} (conn {conn}); {holds}"));

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
            self.log(format_args!("the pause ends: {name} took {}'s place", p.name));
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

    /// A connection closed. Nobody left: back to the start of the trail. A partner left: the
    /// world pauses for a minute to give them time to come back.
    pub fn leave(&mut self, conn: ConnId) {
        let Some(ci) = self.client(conn) else { return };
        let gone = self.clients.remove(ci);
        self.log(format_args!("{} left (client {}); {} still playing", gone.name, gone.id, self.clients.len()));
        for &s in &gone.seats {
            self.sim.release(s);
        }
        self.cancel_vote();

        if self.clients.is_empty() {
            self.log(format_args!("nobody is left; the trail goes back to {}", self.start_level));
            self.reset_to_start();
            return;
        }
        if self.trail_complete {
            self.rebalance(None);
            return;
        }
        // The one who stayed keeps their own leopard while the world waits.
        self.sim.release_all();
        self.log(format_args!("world paused: {} has {PAUSE_SECONDS} s to come back", gone.name));
        self.pause = Some(Pause { name: gone.name.clone(), ticks_left: PAUSE_SECONDS * TICK_HZ, choosing: false });
        self.broadcast(&self.paused_msg(PAUSE_SECONDS));
        self.say(format!("{} dropped. the trail waits a minute for them to come back.", gone.name));
    }

    /// Nobody is left: the next arrival starts the trail fresh.
    fn reset_to_start(&mut self) {
        match (self.load)(&self.start_level).and_then(Sim::new) {
            Ok(sim) => self.sim = sim,
            Err(e) => {
                self.log(format_args!("could not reload {}: {e}; restarting the current level", self.start_level));
                self.sim.restart();
            }
        }
        self.sim.drain_events();
        self.trail_complete = false;
        self.advance_at = 0;
        self.pause = None;
        self.vote = None;
        self.round_start = self.tick;
        self.level_began = self.tick;
        self.restarts = 0;
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

    /// The `round` message that says the world is paused, and for how much longer.
    fn paused_msg(&self, seconds: u32) -> ServerMsg {
        ServerMsg::Round { tick: self.tick, state: RoundState::Paused, reason: Some(RoundReason::PartnerLeft), seconds: Some(seconds) }
    }

    // ------------------------------------------------------------------ vote

    /// A `vote` from a player. `reset: true` proposes, or agrees to the partner's proposal;
    /// `false` withdraws or declines. Nobody can vote while paused or after a win.
    fn vote(&mut self, ci: usize, reset: bool) {
        if self.pause.is_some() || self.sim.won() {
            return;
        }
        let (conn, id) = (self.clients[ci].conn, self.clients[ci].id);
        match (&self.vote, reset) {
            (Some(v), true) if v.by_conn != conn => {
                self.log(format_args!("{} agrees to restart", self.clients[ci].name));
                self.pass_vote(v.by)
            }
            (Some(_), true) => {} // already proposed
            (Some(_), false) => {
                self.log(format_args!("{} withdraws or declines the restart vote", self.clients[ci].name));
                self.lapse_vote()
            }
            (None, true) if self.clients.len() == 1 => {
                self.log(format_args!("{} restarts (playing alone, no vote needed)", self.clients[ci].name));
                self.pass_vote(id)
            }
            (None, true) => {
                self.log(format_args!("{} proposes a restart; {} s for the partner to agree", self.clients[ci].name, VOTE_TICKS / TICK_HZ));
                self.vote = Some(Vote { by_conn: conn, by: id, ticks_left: VOTE_TICKS });
                self.broadcast(&ServerMsg::Vote { by: id, ticks_left: VOTE_TICKS, state: VoteState::Open });
                let name = self.clients[ci].name.clone();
                self.say(format!("{name} wants to restart the level. press R to agree."));
            }
            (None, false) => {}
        }
    }

    /// Both agreed (or one player holds both seats): restart through the same path a major
    /// failure uses, so the checkpoint is honoured the same way.
    fn pass_vote(&mut self, by: u8) {
        self.vote = None;
        self.broadcast(&ServerMsg::Vote { by, ticks_left: 0, state: VoteState::Passed });
        let to_checkpoint = self.sim.checkpoint_lit();
        self.say(if to_checkpoint { "agreed: back to the checkpoint." } else { "agreed: the level starts over." });
        self.sim.restart_latest(RestartCause::Vote);
        let t = self.tick;
        self.flush_events(t);
    }

    /// Ends an open proposal without a restart, and tells everyone.
    fn lapse_vote(&mut self) {
        if let Some(v) = self.vote.take() {
            self.broadcast(&ServerMsg::Vote { by: v.by, ticks_left: 0, state: VoteState::Lapsed });
        }
    }

    /// A proposal does not survive a change in who is playing, a restart or a win.
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
                self.log(format_args!("the restart vote lapsed unanswered"));
                self.lapse_vote();
            }
        }
        let was_won = self.sim.won();
        self.sim.step(t);
        self.flush_events(t);
        if !was_won && self.sim.won() {
            self.advance_at = t + WIN_HOLD_TICKS;
            self.cancel_vote();
            let restarts = match self.restarts {
                0 => "no restarts".to_string(),
                1 => "1 restart".to_string(),
                n => format!("{n} restarts"),
            };
            self.log(format_args!("summit reached in {} with {restarts}", duration(t - self.level_began)));
        }
        if t > self.level_began && (t - self.level_began).is_multiple_of(STATUS_TICKS) {
            self.log_status();
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

    /// One loop tick of the pause: count down, tell the stayer once a second, and at zero ask
    /// them to choose. The world itself does not move.
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
        self.log(format_args!("{name} did not come back; waiting for the stayer to choose both or quit"));
        self.broadcast(&ServerMsg::Round { tick: self.tick, state: RoundState::Choose, reason: Some(RoundReason::PartnerLeft), seconds: None });
        self.say(format!("{name} has not come back. take both leopards, or leave the trail?"));
    }

    /// Turns what the simulation reported into messages: chat lines to everyone, the win, new
    /// overlays when perception changed, and the restart notice with its reason.
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
                SimEvent::Restart { cause, checkpoint } => {
                    self.restarts += 1;
                    let why = match cause {
                        RestartCause::Fell => "a major failure",
                        RestartCause::Vote => "the vote",
                    };
                    let to = if checkpoint { "back to the checkpoint" } else { "from the start" };
                    self.log(format_args!("restart #{} after {why}: {to}", self.restarts));
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
                self.log(format_args!("the trail is complete"));
                self.say("the trail is complete. well walked.");
                return;
            }
            Some(Some(id)) => match (self.load)(&id).and_then(Sim::new) {
                Ok(sim) => self.sim = sim,
                Err(e) => {
                    self.log(format_args!("the next level, {id}, would not load: {e}"));
                    self.say(format!("the trail goes on to '{id}', but that level would not load."));
                    self.trail_complete = true;
                    return;
                }
            },
            None => {}
        }
        self.begin_level();
    }

    /// Starts the newly loaded level for everyone: the same messages a client gets on joining,
    /// in the order the client expects them (it rebuilds the terrain on `level`).
    fn begin_level(&mut self) {
        for ci in 0..self.clients.len() {
            self.send(ci, &self.welcome(ci));
            self.send(ci, &ServerMsg::Level { id: self.level_id().to_string() });
        }
        self.sim.restart();
        self.sim.drain_events();
        self.update_push_window();
        self.round_start = self.tick;
        self.level_began = self.tick;
        self.restarts = 0;
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
        self.log_level();
        self.say(format!("onward: {name}."));
    }

    // ------------------------------------------------------------------ log

    /// One `game` line, tagged with the level and tick so it can be lined up with the clients.
    fn log(&self, text: std::fmt::Arguments) {
        hlog!("game", "[{} t={}] {text}", self.level_id(), self.tick);
    }

    /// What a level is, when it begins: its name, size, rooms and mechanics.
    fn log_level(&self) {
        let l = &self.sim.world.level;
        let mechanics = if l.mechanics.is_empty() { "core only".to_string() } else { l.mechanics.join(", ") };
        self.log(format_args!(
            "level begins: {} \"{}\" ({}x{}, {} rooms; mechanics: {mechanics})",
            l.id, l.name, l.width, l.height, l.regions.len()
        ));
    }

    /// The periodic `stat` line: how long this level has run, who is where, keys carried,
    /// restarts so far.
    fn log_status(&self) {
        let w = &self.sim.world;
        let who: Vec<String> = self
            .clients
            .iter()
            .map(|c| {
                let (x, y) = w.pos[c.active];
                let room = w.level.region_at(x, y);
                let room = if room >= 0 { w.level.regions[room as usize].name.as_str() } else { "outside any room" };
                format!("{} on leopard {} at ({x},{y}) in {room}", c.name, c.active)
            })
            .collect();
        let keys = w.entities().filter(|(_, e)| e.kind == crate::world::Kind::Key).count();
        let keys = if keys > 0 { format!("; keys {}/{keys}", w.carried.len()) } else { String::new() };
        hlog!(
            "stat",
            "[{} t={}] {} in; {}{keys}; {} restarts",
            self.level_id(),
            self.tick,
            duration(self.tick - self.level_began),
            who.join("; "),
            self.restarts
        );
    }

    // ------------------------------------------------------------------ output

    /// The first message a client gets: its id, the level's size and the clock rates.
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

    /// To one player.
    fn send(&mut self, ci: usize, msg: &ServerMsg) {
        self.out.push(Out::Send(self.clients[ci].conn, msg.encode()));
    }

    /// To every player. Encoded once, however many players there are.
    fn broadcast(&mut self, msg: &ServerMsg) {
        let line = msg.encode();
        for c in &self.clients {
            self.out.push(Out::Send(c.conn, line.clone()));
        }
    }

    /// Server chat to everyone (sender 255, shown as "server").
    fn say(&mut self, text: impl Into<String>) {
        let text = text.into();
        hlog!("say", "[{} t={}] {text}", self.level_id(), self.tick);
        self.broadcast(&ServerMsg::Chat { from: SERVER_SENDER, text });
    }

    /// Server chat to one player only.
    fn whisper(&mut self, ci: usize, text: String) {
        self.send(ci, &ServerMsg::Chat { from: SERVER_SENDER, text });
    }

    /// Tells a player which leopards are theirs and which one they are steering.
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

/// A tick count as minutes and seconds ("2m 05s"), for the log.
fn duration(ticks: u32) -> String {
    let s = ticks / TICK_HZ;
    if s >= 60 { format!("{}m {:02}s", s / 60, s % 60) } else { format!("{s}s") }
}
