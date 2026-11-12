//! Rule parity with the client's mock (Crawler.Mock.CoreMechanic), on the shipped levels
//! and on small purpose-built ones.

mod common;

use hollow::level::Level;
use hollow::protocol::Move;
use hollow::sim::{Sim, STEP_TICKS};
use hollow::world::{Kind, SimEvent};

fn mini(rows: &[&str], extra: &str) -> Sim {
    let rows_json: Vec<String> = rows.iter().map(|r| format!("\"{r}\"")).collect();
    let json = format!(r#"{{"id":"t","rows":[{}]{extra}}}"#, rows_json.join(","));
    Sim::new(Level::parse(&json).unwrap()).unwrap()
}

/// Steps the seat one cell (or a fall). Returns false if it could not move.
fn walk(sim: &mut Sim, tick: &mut u32, seat: usize, dir: Move) -> bool {
    let before = sim.world.pos[seat];
    sim.set_input(seat, dir, false);
    for _ in 0..=STEP_TICKS {
        sim.step(*tick);
        *tick += 1;
        if sim.world.pos[seat] != before {
            sim.release(seat);
            return true;
        }
    }
    sim.release(seat);
    false
}

fn interact(sim: &mut Sim, tick: &mut u32, seat: usize) {
    sim.set_input(seat, Move::None, true);
    sim.step(*tick);
    *tick += 1;
}

fn says(sim: &mut Sim) -> Vec<String> {
    sim.drain_events()
        .into_iter()
        .filter_map(|e| if let SimEvent::Say(s) = e { Some(s) } else { None })
        .collect()
}

#[test]
fn every_shipped_level_parses_and_loads() {
    let trail = common::trail();
    let ids: Vec<_> = trail.biomes.iter().flat_map(|b| b.levels.clone()).collect();
    assert!(!ids.is_empty());
    for id in ids {
        let level = common::level(&id);
        Sim::new(level).unwrap_or_else(|e| panic!("{id}: {e}"));
    }
}

#[test]
fn entity_ids_follow_the_mocks_order() {
    // Seats first, then row-scan from the top row, as Crawler.Mock.MockWorld does.
    let sim = Sim::new(common::level("foothills-1")).unwrap();
    let snap = sim.snapshot().0;
    assert_eq!(snap[0], (3, 4, Kind::Player as u8, 0, -1)); // P
    assert_eq!(snap[1], (3, 3, Kind::Player as u8, 0, -1)); // 1
    // Row 2 (y = 6) holds the first lever at column 10.
    assert_eq!(snap[2], (10, 6, Kind::Lever as u8, 0, -1));
}

#[test]
fn trail_order_crosses_biomes() {
    let t = hollow::level::Trail::parse(
        r#"{"biomes":[{"id":"a","levels":["a1","a2"]},{"id":"e","levels":[]},{"id":"b","levels":["b1"]}]}"#,
    )
    .unwrap();
    assert_eq!(t.next("a1"), Some("a2"));
    assert_eq!(t.next("a2"), Some("b1"));
    assert_eq!(t.next("b1"), None);
    assert_eq!(t.next("zz"), None);
}

/// The shortest route levelcheck.py found must win on the server too, on its last action
/// and not before. A failure here means the server and the mock disagree on a rule.
fn replay(id: &str) {
    let mut sim = Sim::new(common::level(id)).unwrap();
    let route = common::route(id);
    let mut tick = 0;
    for (i, (seat, act)) in route.iter().enumerate() {
        assert!(!sim.won(), "{id}: won early, before action {i}");
        let dir = match act.as_str() {
            "use" => {
                interact(&mut sim, &mut tick, *seat);
                continue;
            }
            "n" => Move::N,
            "s" => Move::S,
            "e" => Move::E,
            "w" => Move::W,
            other => panic!("{id}: unknown action {other}"),
        };
        let at = sim.world.pos[*seat];
        assert!(walk(&mut sim, &mut tick, *seat, dir), "{id}: action {i}: seat {seat} at {at:?} could not move {act}");
    }
    assert!(sim.won(), "{id}: route finished without a win");
    assert!(sim.drain_events().contains(&SimEvent::Won));
}

#[test]
fn route_foothills_1() {
    replay("foothills-1");
}

#[test]
fn route_foothills_2() {
    replay("foothills-2");
}

#[test]
fn route_foothills_3() {
    replay("foothills-3");
}

#[test]
fn route_hollow() {
    replay("hollow");
}

#[test]
fn linked_lever_opens_its_door_and_shuts_it_again() {
    //            col 0123456
    let mut sim = mini(
        &["#######", "#PL.D.#", "#1....#", "#######"],
        r#","links":[{"from":[2,1],"to":[4,1]}]"#,
    );
    let mut t = 0;
    let door = 3; // P, 1, then L (id 2), D (id 3)
    assert_eq!(sim.snapshot().0[door].3, 0);
    interact(&mut sim, &mut t, 0);
    assert_eq!(sim.snapshot().0[door].3, 1);
    assert_eq!(says(&mut sim), ["*clunk*. stone grinds open nearby."]);
    interact(&mut sim, &mut t, 0);
    assert_eq!(sim.snapshot().0[door].3, 0);
    assert_eq!(says(&mut sim), ["*clunk*. a door slides shut."]);
}

#[test]
fn closed_doors_walls_and_the_partner_block() {
    let mut sim = mini(&["#####", "#PD.#", "#1..#", "#####"], r#","regions":[{"rect":[0,0,5,4],"rule":"lever"}]"#);
    let mut t = 0;
    assert!(!walk(&mut sim, &mut t, 0, Move::E), "closed door");
    assert!(!walk(&mut sim, &mut t, 0, Move::W), "wall");
    assert!(!walk(&mut sim, &mut t, 0, Move::S), "partner");
    assert!(walk(&mut sim, &mut t, 1, Move::E));
    assert!(walk(&mut sim, &mut t, 0, Move::S));
}

#[test]
fn a_plate_holds_its_door_only_while_occupied() {
    // Plate region: the plate opens the region's door while a leopard stands on it.
    let mut sim = mini(
        &["######", "#P.B.#", "#1..D#", "######"],
        r#","regions":[{"rect":[0,0,6,4],"rule":"plate"}]"#,
    );
    let mut t = 0;
    let door = 3; // P, 1, B (2), D (3)
    assert!(walk(&mut sim, &mut t, 0, Move::E));
    assert_eq!(sim.snapshot().0[door].3, 0);
    assert!(walk(&mut sim, &mut t, 0, Move::E));
    assert_eq!(sim.snapshot().0[2].3, 1, "plate pressed");
    assert_eq!(sim.snapshot().0[door].3, 1);
    assert!(walk(&mut sim, &mut t, 0, Move::E));
    assert_eq!(sim.snapshot().0[door].3, 0, "door shuts when the plate is left");
}

#[test]
fn a_pit_returns_the_leopard_to_its_rooms_entrance_and_drops_its_keys() {
    let mut sim = mini(
        &["########", "#E.KH..#", "#P1....#", "########"],
        r#","regions":[{"rect":[1,1,6,2],"rule":"lever"}]"#,
    );
    let mut t = 0;
    let key = 2; // P, 1, K (2), H (3)
    assert!(walk(&mut sim, &mut t, 0, Move::N)); // onto E
    assert!(walk(&mut sim, &mut t, 0, Move::E));
    assert!(walk(&mut sim, &mut t, 0, Move::E)); // pick up the key
    assert_eq!(sim.snapshot().0[key], (3, 2, Kind::Key as u8, 0, 0));
    assert_eq!(says(&mut sim), ["key 1 of 1.", "the summit gate answers the key."]);
    assert!(walk(&mut sim, &mut t, 0, Move::E)); // into the pit
    assert_eq!(sim.world.pos[0], (1, 2), "back at the room's entrance");
    assert_eq!(sim.snapshot().0[key], (3, 2, Kind::Key as u8, 0, -1), "key back where it lay");
    assert_eq!(
        says(&mut sim),
        ["a fall. back to the room's entrance.", "the key slips loose and lands back where it lay."]
    );
    assert_eq!(sim.world.pos[1], (2, 1), "the partner is untouched");
}

#[test]
fn the_summit_gate_opens_when_every_key_is_carried() {
    let mut sim = mini(&["#######", "#PK.F.#", "#1K...#", "#######"], "");
    let mut t = 0;
    let gate = 3; // P, 1, then the top row: K (2), F (3); then K (4)
    assert!(walk(&mut sim, &mut t, 0, Move::E));
    assert_eq!(sim.snapshot().0[gate].3, 0, "one key of two");
    assert!(walk(&mut sim, &mut t, 1, Move::E));
    assert_eq!(sim.snapshot().0[gate].3, 1);
    assert!(says(&mut sim).contains(&"the summit gate answers the two keys.".to_string()));
}

#[test]
fn the_summit_needs_both_leopards() {
    let mut sim = mini(
        &["########", "#P...G.#", "#1.....#", "########"],
        r#","regions":[{"rect":[4,1,3,2],"rule":"goal"}]"#,
    );
    let mut t = 0;
    for _ in 0..4 {
        assert!(walk(&mut sim, &mut t, 0, Move::E));
    }
    assert!(!sim.won());
    assert_eq!(says(&mut sim).last().unwrap(), "the summit, but alone. your partner needs to be here too.");
    for _ in 0..3 {
        walk(&mut sim, &mut t, 1, Move::E);
    }
    assert!(sim.won(), "partner in the summit's room");
    assert_eq!(sim.snapshot().0[2].3, 1, "goal shows won");
    assert!(!walk(&mut sim, &mut t, 1, Move::E), "nothing moves after a win");
}

#[test]
fn the_sightstone_reveals_hidden_pits_and_false_keys() {
    let mut sim = mini(&["#######", "#PS.h.#", "#1..k.#", "#######"], "");
    let mut t = 0;
    let p = sim.perception(0);
    assert_eq!(p.hidden, vec![3]); // P, 1, S (2), h (3)
    assert_eq!(p.phantoms, vec![(4, 1, Kind::Key as u8)]);
    interact(&mut sim, &mut t, 0);
    assert_eq!(sim.perception(0), Default::default());
    assert!(sim.drain_events().contains(&SimEvent::Perception));
    assert_eq!(sim.snapshot().0[2].3, 1, "sightstone used");
}

#[test]
fn a_held_direction_steps_once_per_step_ticks() {
    let mut sim = mini(&["########", "#P.....#", "#1.....#", "########"], "");
    sim.set_input(0, Move::E, false);
    for t in 0..(STEP_TICKS * 3) {
        sim.step(t);
    }
    assert_eq!(sim.world.pos[0], (4, 2), "three steps in three step periods");
}

#[test]
fn unknown_mechanics_are_refused() {
    let lvl = Level::parse(r##"{"id":"t","rows":["#P1#"],"mechanics":["lava"]}"##).unwrap();
    assert!(Sim::new(lvl).is_err());
}

#[test]
fn bad_level_files_are_refused() {
    for bad in [
        r##"{"id":"t","rows":[]}"##,
        r###"{"id":"t","rows":["##","#"]}"###,
        r##"{"id":"../etc","rows":["#"]}"##,
        r##"{"id":"t","rows":["#"],"links":[{"from":[5,5],"to":[0,0]}]}"##,
        r##"{"id":"t","rows":["#"],"regions":[{"rect":[0,0]}]}"##,
    ] {
        assert!(Level::parse(bad).is_err(), "{bad} should be refused");
    }
}
