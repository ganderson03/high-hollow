//! The mechanics beyond Foothills, the failure grades and the checkpoint, each on a small
//! purpose-built map. Coordinates are world cells: x from the west, y from the south (the
//! last row listed is y = 0).

use hollow::level::Level;
use hollow::mechanics::boulders::{GONE, STRAIN};
use hollow::protocol::Move;
use hollow::sim::{Sim, STEP_TICKS};
use hollow::world::{Kind, RestartCause, SimEvent};

struct T {
    sim: Sim,
    tick: u32,
}

impl T {
    fn new(rows: &[&str], extra: &str) -> T {
        let rows_json: Vec<String> = rows.iter().map(|r| format!("\"{r}\"")).collect();
        let json = format!(r#"{{"id":"t","rows":[{}]{extra}}}"#, rows_json.join(","));
        T { sim: Sim::new(Level::parse(&json).unwrap()).unwrap_or_else(|e| panic!("{e}")), tick: 0 }
    }

    fn run(&mut self, ticks: u32) {
        for _ in 0..ticks {
            self.sim.step(self.tick);
            self.tick += 1;
        }
    }

    /// Holds a direction for one step period, then lets go.
    fn go(&mut self, seat: usize, dir: Move) {
        self.sim.set_input(seat, dir, false);
        self.run(STEP_TICKS);
        self.sim.release(seat);
    }

    /// One tick of pushing in a direction (a bump), then let go.
    fn nudge(&mut self, seat: usize, dir: Move) {
        self.sim.set_input(seat, dir, false);
        self.run(1);
        self.sim.release(seat);
    }

    fn use_(&mut self, seat: usize) {
        self.sim.set_input(seat, Move::None, true);
        self.run(1);
    }

    fn pos(&self, seat: usize) -> (i32, i32) {
        self.sim.world.pos[seat]
    }

    fn state(&self, id: usize) -> u8 {
        self.sim.snapshot().0[id].3
    }

    fn row(&self, id: usize) -> (i32, i32, u8, u8, i8) {
        self.sim.snapshot().0[id]
    }

    fn events(&mut self) -> Vec<SimEvent> {
        self.sim.drain_events()
    }

    fn said(&mut self, text: &str) -> bool {
        self.events().contains(&SimEvent::Say(text.into()))
    }
}

fn fell(events: &[SimEvent], checkpoint: bool) -> bool {
    events.contains(&SimEvent::Restart { cause: RestartCause::Fell, checkpoint })
}

// ------------------------------------------------------------------ framework

#[test]
fn a_glyph_needs_its_mechanic_switched_on() {
    for (row, needs) in [("#P1=#", "ledges"), ("#P1~#", "ice"), ("#P1O#", "boulders"), ("#P1w#", "water"), ("#P1a#", "sight")] {
        let json = format!(r#"{{"id":"t","rows":["{row}"]}}"#);
        let err = Sim::new(Level::parse(&json).unwrap()).err().unwrap();
        assert!(err.contains(needs), "{row}: {err}");
    }
}

#[test]
fn a_chasm_restarts_the_whole_level() {
    // y=1: P(1,1) K(2,1) . X(4,1);  y=0: 1(1,0)
    let mut t = T::new(&["#PK.X#", "#1...#"], "");
    t.go(0, Move::E); // key
    assert_eq!(t.sim.world.carried.len(), 1);
    t.go(0, Move::E);
    t.go(0, Move::E); // chasm
    let ev = t.events();
    assert!(fell(&ev, false));
    assert!(ev.contains(&SimEvent::Say("into the chasm.".into())));
    assert_eq!(t.pos(0), (1, 1), "both leopards home");
    assert!(t.sim.world.carried.is_empty(), "every key back where it began");
}

#[test]
fn the_checkpoint_lights_with_both_leopards_and_a_fall_returns_there() {
    // y=2: P(1,2) . C(3,2);  y=1: 1(1,1) .... X(6,1)
    let mut t = T::new(&["########", "#P.C...#", "#1....X#", "########"], "");
    let cp = 2;
    t.go(0, Move::E); // (2,2), beside C
    assert_eq!(t.state(cp), 0, "one leopard is not enough");
    t.go(1, Move::E); // (2,1), beside C too
    assert_eq!(t.state(cp), 1);
    assert!(t.sim.checkpoint_lit());
    assert!(t.said("the trail marker flares. a fall from here brings you back to it."));

    for _ in 0..4 {
        t.go(1, Move::E); // to the chasm at (6,1)
    }
    let ev = t.events();
    assert!(fell(&ev, true));
    assert!(ev.contains(&SimEvent::Say("back to the checkpoint.".into())));
    assert_eq!((t.pos(0), t.pos(1)), ((2, 2), (2, 1)), "where they stood when it lit");
    assert_eq!(t.state(cp), 1, "it stays lit");

    t.sim.restart_latest(RestartCause::Vote);
    assert_eq!(t.pos(1), (2, 1), "a vote also goes back to the checkpoint");
}

#[test]
fn a_disguised_pit_looks_like_a_ledge_until_the_sightstone() {
    // y=1: P(1,1) S(2,1) - (3,1);  y=0: 1(1,0)
    let mut t = T::new(&["#PS-.#", "#1...#"], "");
    let pit = 3; // P, 1, S (2), - (3)
    assert_eq!(t.sim.perception(0).substituted, vec![(pit as u32, Kind::Ledge as u8)]);
    t.use_(0);
    assert!(t.sim.perception(0).substituted.is_empty());
    t.go(0, Move::E);
    t.go(0, Move::E);
    assert_eq!(t.pos(0), (1, 1), "it is still a pit");
}

// ------------------------------------------------------------------ ledges

#[test]
fn a_ledge_cracks_underfoot_and_collapses_behind() {
    // y=1: P(1,1) =(2,1) . 1(4,1)
    let mut t = T::new(&["#######", "#P=.1.#", "#######"], r#","mechanics":["ledges"]"#);
    let ledge = 2;
    assert_eq!(t.state(ledge), 0);
    t.go(0, Move::E);
    assert_eq!(t.state(ledge), 1, "cracked while stood on");
    t.go(0, Move::E);
    assert_eq!(t.state(ledge), 2, "collapsed when the last leopard stepped off");
    assert!(t.said("the ledge crumbles away behind you."));
    t.go(0, Move::W);
    assert_eq!(t.pos(0), (1, 1), "walking into a collapsed ledge is a minor failure");
    assert!(t.said("the ledge is gone. back to the room's entrance."));

    t.sim.restart_latest(RestartCause::Vote);
    assert_eq!(t.state(ledge), 0, "a restart brings it back");
}

// ------------------------------------------------------------------ ice

#[test]
fn ice_slides_to_the_first_non_ice_cell_and_ignores_input() {
    // y=2: P(1,2) ~~~ (2..4) floor (5,2) wall;  y=1: 1(1,1) ~~~~ (2..5) wall (6,1)
    let mut t = T::new(&["#########", "#P~~~.#.#", "#1~~~~#.#", "#########"], r#","mechanics":["ice"]"#);
    t.sim.set_input(0, Move::E, false);
    t.run(1);
    assert_eq!(t.pos(0), (2, 2));
    t.sim.set_input(0, Move::W, false); // ignored while sliding
    while t.sim.world.slide[0].is_some() {
        t.run(1);
    }
    assert_eq!(t.pos(0), (5, 2), "stops on the first floor cell");
    t.sim.release(0);

    t.sim.set_input(1, Move::E, false);
    t.run(1);
    t.sim.release(1);
    t.run(40);
    assert_eq!(t.pos(1), (5, 1), "stops on the ice against the wall");
}

#[test]
fn a_partner_on_the_ice_is_a_stopper() {
    // y=1: P(1,1) ~ ~ 1(4,1, on ice) . ;  the "ice" field marks the spawn cell as ice
    let mut t = T::new(&["#######", "#P~~1.#", "#######"], r#","mechanics":["ice"],"ice":[[4,1]]"#);
    t.go(0, Move::E);
    t.run(20);
    assert_eq!(t.pos(0), (3, 1), "stopped one short of the partner");
}

#[test]
fn sliding_into_open_water_restarts_the_level() {
    let mut t = T::new(&["#######", "#P~~%.#", "#1....#", "#######"], r#","mechanics":["ice"]"#);
    t.go(0, Move::E);
    t.run(20);
    let ev = t.events();
    assert!(fell(&ev, false));
    assert!(ev.contains(&SimEvent::Say("through the ice into open water.".into())));
    assert_eq!(t.pos(0), (1, 2));
}

// ------------------------------------------------------------------ boulders

#[test]
fn a_boulder_fills_a_pit() {
    // y=2: P(1,2) . O(3,2) H(4,2)
    let mut t = T::new(&["#########", "#P.OH...#", "#1......#", "#########"], r#","mechanics":["boulders"]"#);
    let (rock, pit) = (2, 3);
    t.go(0, Move::E);
    t.go(0, Move::E); // pushes the boulder into the pit
    assert_eq!(t.pos(0), (3, 2));
    assert_eq!(t.state(rock) & GONE, GONE);
    assert_eq!(t.state(pit), 1, "filled");
    assert!(t.said("the boulder drops into the pit and fills it."));
    t.go(0, Move::E);
    assert_eq!(t.pos(0), (4, 2), "a filled pit is floor");
}

#[test]
fn a_boulder_holds_a_plate_and_walls_stop_it() {
    // y=2: P(1,2) . O(3,2) B(4,2) # D(6,2) linked from the plate
    let mut t = T::new(
        &["#########", "#P.OB#D.#", "#1......#", "#########"],
        r#","mechanics":["boulders"],"links":[{"from":[4,1],"to":[6,1]}]"#,
    );
    let (rock, plate, door) = (2, 3, 4);
    t.go(0, Move::E);
    t.go(0, Move::E);
    assert_eq!(t.row(rock), (4, 2, Kind::Boulder as u8, 0, -1));
    assert_eq!((t.state(plate), t.state(door)), (1, 1), "the boulder holds the plate down");
    t.go(0, Move::E);
    assert_eq!(t.pos(0), (3, 2), "a wall behind the boulder stops the push");
}

#[test]
fn a_boulder_is_lost_to_a_chasm_and_slides_on_ice() {
    let mut t = T::new(&["########", "#P.OX..#", "#1.O~~.#", "########"], r#","mechanics":["boulders","ice"]"#);
    t.go(0, Move::E);
    t.go(0, Move::E);
    assert_eq!(t.state(2) & GONE, GONE);
    assert_eq!(t.state(3), 0, "a chasm is not filled");
    assert!(t.said("the boulder tumbles into the chasm."));

    t.go(1, Move::E);
    t.go(1, Move::E); // boulder onto the ice at (4,1)
    t.run(20);
    assert_eq!(t.row(4).0, 6, "it slid to the floor beyond the ice");
}

#[test]
fn a_slab_needs_both_leopards_inside_the_window() {
    // A slab along x at (2,2)-(3,2); the leopards stand under its halves.
    let rows = ["########", "#......#", "#.[]...#", "#.P1...#", "########"];
    let mut t = T::new(&rows, r#","mechanics":["boulders"]"#);
    let slab = 2;
    assert_eq!(t.state(slab), 1, "a slab along x");

    t.nudge(0, Move::N);
    assert_eq!(t.state(slab), 1 + STRAIN, "straining");
    assert!(t.said("the slab strains. it needs a second shove, beside you."));
    t.run(91);
    assert_eq!(t.state(slab), 1, "the push lapsed");
    t.nudge(1, Move::N);
    assert_eq!(t.row(slab).1, 2, "one push alone moves nothing");

    t.nudge(0, Move::N); // inside seat 1's window
    assert_eq!(t.row(slab).1, 3, "moved one cell north");
    assert_eq!((t.pos(0), t.pos(1)), ((2, 2), (3, 2)), "both stepped forward with it");
    assert!(t.said("the slab grinds forward."));

    t.nudge(0, Move::E);
    t.run(STEP_TICKS);
    assert_eq!(t.row(slab).0, 2, "pushing along its length does nothing");
}

// ------------------------------------------------------------------ water

/// Source (1,3) -> channel (2,3) -> sluice (3,3), exits east to the wheel (6,3) or south down
/// the channel (3,2), (3,1). The wheel opens the door (6,1).
fn creek() -> T {
    T::new(
        &["########", "#WwVwwM#", "#..w...#", "#P.w.1D#", "########"],
        r#","mechanics":["water"],"links":[{"from":[6,1],"to":[6,3]}]"#,
    )
}

#[test]
fn water_turns_a_wheel_that_opens_its_door() {
    let t = creek();
    let (sluice, wheel, door) = (2, 3, 4);
    assert_eq!(t.state(sluice), 1, "the first open exit is east");
    assert_eq!((t.state(wheel), t.state(door)), (1, 1));
    assert_eq!(t.sim.snapshot().1, vec![(1, 3), (2, 3), (3, 3), (4, 3), (5, 3)]);
}

#[test]
fn swinging_a_sluice_dries_one_channel_and_floods_another() {
    let mut t = creek();
    t.go(0, Move::E);
    t.go(0, Move::E);
    assert_eq!(t.pos(0), (3, 1), "a dry bed is walkable");
    t.go(0, Move::W);
    t.go(1, Move::N);
    t.go(1, Move::W); // (4,2), beside the sluice
    t.use_(1);
    assert_eq!(t.state(2), 2, "now open to the south");
    assert!(t.said("the sluice swings and the water turns. anyone standing in its new channel will be swept away."));
    assert_eq!((t.state(3), t.state(4)), (0, 0), "the wheel stops and its door shuts");
    t.go(0, Move::E);
    assert_eq!(t.pos(0), (2, 1), "flowing water cannot be entered");
}

#[test]
fn a_leopard_in_a_channel_when_water_arrives_is_swept_away() {
    let mut t = creek();
    t.go(0, Move::E);
    t.go(0, Move::E); // standing in the dry bed at (3,1)
    t.go(1, Move::N);
    t.go(1, Move::W);
    t.use_(1);
    let ev = t.events();
    assert!(fell(&ev, false));
    assert!(ev.contains(&SimEvent::Say("swept away by the water.".into())));
    assert_eq!(t.pos(0), (1, 1));
    assert_eq!(t.state(2), 1, "the sluice swings back on a restart");
}

#[test]
fn a_boulder_dams_a_channel() {
    // The boulder at (3,2) is pushed west into the channel (2,2), damming the water above it.
    let mut t = T::new(
        &["#######", "#W....#", "#w.O.1#", "#w....#", "#wP...#", "#######"],
        r#","mechanics":["water","boulders"]"#,
    );
    assert!(t.sim.snapshot().1.contains(&(1, 1)), "water runs down the channel");
    // Seat 1 at (5,3) walks to (4,3) and pushes the boulder west into... (2,3) is floor; push twice.
    t.go(1, Move::W);
    t.go(1, Move::W); // boulder to (2,3)
    t.go(1, Move::W); // boulder to (1,3), into the channel
    let water = t.sim.snapshot().1;
    assert!(water.contains(&(1, 4)), "above the dam still flows");
    assert!(!water.contains(&(1, 2)) && !water.contains(&(1, 1)), "below the dam runs dry");
}

// ------------------------------------------------------------------ split sight

#[test]
fn each_leopard_sees_its_own_crevasses_and_false_ones() {
    // y=1: P(1,1) a(2,1) y(3,1);  y=0: 1(1,0) b(2,0) z(3,0)
    let mut t = T::new(&["#Pay#", "#1bz#"], r#","mechanics":["sight"]"#);
    let (a, b) = (2, 3);
    let p0 = t.sim.perception(0);
    assert_eq!(p0.hidden, vec![b as u32], "leopard 0 cannot see b");
    assert_eq!(p0.phantoms, vec![(3, 1, Kind::Hazard as u8)]);
    let p1 = t.sim.perception(1);
    assert_eq!(p1.hidden, vec![a as u32]);
    assert_eq!(p1.phantoms, vec![(3, 0, Kind::Hazard as u8)]);

    t.go(1, Move::E); // into b, which leopard 1 can see: still a fall
    assert_eq!(t.pos(1), (1, 0));
    assert!(t.said("a crevasse. back to the room's entrance."));
}
