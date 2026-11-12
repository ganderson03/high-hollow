//! Seats, joining and leaving, chat, control and trail progression, without sockets.

mod common;

use common::{game, lines_to, types};
use hollow::game::{Game, Out, TICKS_PER_SNAPSHOT, WIN_HOLD_TICKS};
use hollow::protocol::{ClientMsg, Move};
use serde_json::json;

fn hello(g: &mut Game, conn: u64, name: &str) -> Vec<Out> {
    g.handle(conn, ClientMsg::Hello { name: name.into() });
    g.take_out()
}

#[test]
fn a_lone_player_gets_the_whole_intro_and_both_seats() {
    let mut g = game("foothills-1");
    let out = hello(&mut g, 1, "snep");
    let lines = lines_to(&out, 1);
    assert_eq!(types(&lines), ["welcome", "level", "seats", "overlay", "round", "chat"]);
    assert_eq!(lines[0], json!({"t":"welcome","client_id":0,"width":24,"height":9,"tick_hz":60,"replication_hz":20}));
    assert_eq!(lines[1], json!({"t":"level","id":"foothills-1"}));
    assert_eq!(lines[2], json!({"t":"seats","yours":[0,1],"active":0}));
    assert_eq!(lines[4]["state"], "playing");
    assert!(lines[5]["text"].as_str().unwrap().contains("you hold both leopards"));
}

#[test]
fn a_second_player_takes_seat_one_and_a_third_is_refused() {
    let mut g = game("foothills-1");
    hello(&mut g, 1, "snep");
    let out = hello(&mut g, 2, "pine");

    let first = lines_to(&out, 1);
    assert_eq!(first[0], json!({"t":"seats","yours":[0],"active":0}));
    assert!(first.iter().any(|l| l["text"] == "pine joins. you each steer one leopard now."));

    let second = lines_to(&out, 2);
    assert_eq!(second[0]["client_id"], 1);
    assert!(second.contains(&json!({"t":"seats","yours":[1],"active":1})));

    let out = hello(&mut g, 3, "third");
    assert_eq!(out, [Out::Close(3)], "no welcome for a third connection");
    assert_eq!(g.player_count(), 2);
}

#[test]
fn anything_before_hello_closes_the_connection() {
    let mut g = game("foothills-1");
    g.handle(9, ClientMsg::Control { seat: 1 });
    assert_eq!(g.take_out(), [Out::Close(9)]);
}

#[test]
fn chat_is_stamped_with_the_real_sender_and_cleaned() {
    let mut g = game("foothills-1");
    hello(&mut g, 1, "snep");
    hello(&mut g, 2, "pine");
    g.handle(2, ClientMsg::Chat { from: 255, text: "  two east\u{7}of you  ".into() });
    let out = g.take_out();
    let want = json!({"t":"chat","from":1,"text":"two eastof you"});
    assert_eq!(lines_to(&out, 1), std::slice::from_ref(&want));
    assert_eq!(lines_to(&out, 2), [want]);

    g.handle(2, ClientMsg::Chat { from: 1, text: "   ".into() });
    assert!(g.take_out().is_empty(), "empty chat is dropped");
}

#[test]
fn control_switches_only_between_seats_you_hold() {
    let mut g = game("foothills-1");
    hello(&mut g, 1, "snep");
    g.handle(1, ClientMsg::Control { seat: 1 });
    let lines = lines_to(&g.take_out(), 1);
    assert_eq!(types(&lines), ["seats", "overlay"]);
    assert_eq!(lines[0], json!({"t":"seats","yours":[0,1],"active":1}));

    hello(&mut g, 2, "pine");
    g.handle(1, ClientMsg::Control { seat: 1 });
    assert!(g.take_out().is_empty(), "seat 1 belongs to the partner now");
}

#[test]
fn input_moves_the_seat_being_steered() {
    let mut g = game("foothills-1");
    hello(&mut g, 1, "snep");
    let start = g.sim().world.pos;
    g.handle(1, ClientMsg::Control { seat: 1 });
    g.handle(1, ClientMsg::Input { tick: 0, dir: Move::E, interact: false });
    g.step();
    assert_eq!(g.sim().world.pos[0], start[0], "the idle leopard stands still");
    assert_eq!(g.sim().world.pos[1], (start[1].0 + 1, start[1].1));
}

#[test]
fn snapshots_go_out_at_the_replication_rate_and_only_with_players() {
    let mut g = game("foothills-1");
    for _ in 0..10 {
        g.step();
    }
    assert_eq!(g.tick(), 0, "the world holds still with nobody connected");
    hello(&mut g, 1, "snep");
    let mut snaps = 0;
    for _ in 0..(TICKS_PER_SNAPSHOT * 4) {
        g.step();
        snaps += types(&lines_to(&g.take_out(), 1)).iter().filter(|t| *t == "snapshot").count();
    }
    assert_eq!(snaps, 4);
}

#[test]
fn when_a_partner_leaves_the_other_holds_both() {
    let mut g = game("foothills-1");
    hello(&mut g, 1, "snep");
    hello(&mut g, 2, "pine");
    g.leave(1);
    let lines = lines_to(&g.take_out(), 2);
    assert_eq!(lines[0], json!({"t":"seats","yours":[0,1],"active":1}));
    assert!(lines.iter().any(|l| l["text"].as_str().is_some_and(|t| t.starts_with("snep left the trail"))));
}

#[test]
fn an_empty_server_goes_back_to_the_start_of_the_trail() {
    let mut g = game("foothills-1");
    hello(&mut g, 1, "snep");
    win_by_route(&mut g, 1, "foothills-1");
    for _ in 0..=WIN_HOLD_TICKS {
        g.step();
    }
    assert_eq!(g.level_id(), "foothills-2");
    g.leave(1);
    assert_eq!(g.level_id(), "foothills-1");
}

/// Plays a level's fixture route through the session, as one client holding both seats.
fn win_by_route(g: &mut Game, conn: u64, id: &str) {
    let mut active = 0;
    for (seat, act) in common::route(id) {
        if seat != active {
            g.handle(conn, ClientMsg::Control { seat: seat as u8 });
            active = seat;
        }
        let dir = match act.as_str() {
            "n" => Move::N,
            "s" => Move::S,
            "e" => Move::E,
            "w" => Move::W,
            _ => {
                g.handle(conn, ClientMsg::Input { tick: 0, dir: Move::None, interact: true });
                g.step();
                continue;
            }
        };
        let before = g.sim().world.pos[seat];
        g.handle(conn, ClientMsg::Input { tick: 0, dir, interact: false });
        for _ in 0..20 {
            g.step();
            if g.sim().world.pos[seat] != before {
                break;
            }
        }
        g.handle(conn, ClientMsg::Input { tick: 0, dir: Move::None, interact: false });
        assert_ne!(g.sim().world.pos[seat], before, "{id}: seat {seat} could not move {act}");
    }
    assert!(g.sim().won(), "{id}: route did not win");
}

#[test]
fn a_win_holds_then_loads_the_next_level_for_everyone() {
    let mut g = game("foothills-1");
    hello(&mut g, 1, "snep");
    g.take_out();
    win_by_route(&mut g, 1, "foothills-1");
    let won = lines_to(&g.take_out(), 1);
    assert!(won.iter().any(|l| l["t"] == "round" && l["state"] == "won"));
    assert!(won.iter().any(|l| l["text"] == "the summit, together!"));

    for _ in 0..WIN_HOLD_TICKS - 1 {
        g.step();
    }
    assert_eq!(g.level_id(), "foothills-1", "still holding on the summit card");
    for _ in 0..2 {
        g.step();
    }
    assert_eq!(g.level_id(), "foothills-2");
    let lines = lines_to(&g.take_out(), 1);
    let t = types(&lines);
    let at = t.iter().position(|x| x == "welcome").unwrap();
    assert_eq!(t[at..at + 5], ["welcome", "level", "overlay", "round", "seats"]);
    assert_eq!(lines[at + 1], json!({"t":"level","id":"foothills-2"}));
    assert_eq!(lines[at + 3]["state"], "playing");
    assert!(lines.iter().any(|l| l["text"] == "onward: Stile Meadow."));
}

#[test]
fn the_whole_trail_can_be_walked_to_its_end() {
    let mut g = game("foothills-1");
    hello(&mut g, 1, "snep");
    for id in ["foothills-1", "foothills-2", "foothills-3", "hollow"] {
        assert_eq!(g.level_id(), id);
        win_by_route(&mut g, 1, id);
        for _ in 0..=WIN_HOLD_TICKS {
            g.step();
        }
    }
    let lines = lines_to(&g.take_out(), 1);
    assert!(lines.iter().any(|l| l["text"] == "the trail is complete. well walked."));
    assert_eq!(g.level_id(), "hollow", "the last summit stays up");
}
