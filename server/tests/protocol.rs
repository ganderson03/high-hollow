//! The wire format must match the client's WireCodec byte for byte where the client writes it,
//! and decode everything the client sends.

use hollow::protocol::{ClientMsg, Move, RoundState, ServerMsg};

#[test]
fn decodes_what_the_client_sends() {
    // Exactly as Crawler.Protocol.WireCodec writes them.
    assert_eq!(ClientMsg::decode(r#"{"t":"hello","name":"snep"}"#).unwrap(), ClientMsg::Hello { name: "snep".into() });
    assert_eq!(
        ClientMsg::decode(r#"{"t":"input","tick":412,"move":"n","interact":false}"#).unwrap(),
        ClientMsg::Input { tick: 412, dir: Move::N, interact: false }
    );
    assert_eq!(
        ClientMsg::decode(r#"{"t":"input","tick":0,"move":"none","interact":true}"#).unwrap(),
        ClientMsg::Input { tick: 0, dir: Move::None, interact: true }
    );
    assert_eq!(
        ClientMsg::decode(r#"{"t":"chat","from":0,"text":"on my way"}"#).unwrap(),
        ClientMsg::Chat { from: 0, text: "on my way".into() }
    );
    assert_eq!(ClientMsg::decode(r#"{"t":"control","seat":1}"#).unwrap(), ClientMsg::Control { seat: 1 });
}

#[test]
fn rejects_malformed_client_lines() {
    for bad in [
        "",
        "not json",
        r#"{"t":"teleport"}"#,
        r#"{"t":"input","move":"up"}"#,
        r#"{"t":"control","seat":-1}"#,
        r#"{"name":"no tag"}"#,
    ] {
        assert!(ClientMsg::decode(bad).is_err(), "{bad} should not decode");
    }
}

#[test]
fn encodes_server_messages_as_the_readme_shows() {
    let cases = [
        (
            ServerMsg::Welcome { client_id: 0, width: 24, height: 9, tick_hz: 60, replication_hz: 20 },
            r#"{"t":"welcome","client_id":0,"width":24,"height":9,"tick_hz":60,"replication_hz":20}"#,
        ),
        (ServerMsg::Seats { yours: vec![0, 1], active: 0 }, r#"{"t":"seats","yours":[0,1],"active":0}"#),
        (
            ServerMsg::Snapshot { tick: 412, entities: vec![(15, 9, 0, 0, -1)], water: vec![], hash: None },
            r#"{"t":"snapshot","tick":412,"entities":[[15,9,0,0,-1]]}"#,
        ),
        (
            ServerMsg::Overlay { tick: 412, hidden: vec![7], substituted: vec![(3, 4)], phantoms: vec![(26, 21, 4)] },
            r#"{"t":"overlay","tick":412,"hidden":[7],"substituted":[[3,4]],"phantoms":[[26,21,4]]}"#,
        ),
        (
            ServerMsg::Chat { from: 255, text: "welcome to High Hollow".into() },
            r#"{"t":"chat","from":255,"text":"welcome to High Hollow"}"#,
        ),
        (ServerMsg::Level { id: "hollow".into() }, r#"{"t":"level","id":"hollow"}"#),
        (ServerMsg::round(412, RoundState::Playing), r#"{"t":"round","tick":412,"state":"playing"}"#),
        (ServerMsg::round(9, RoundState::Won), r#"{"t":"round","tick":9,"state":"won"}"#),
    ];
    for (msg, wire) in cases {
        assert_eq!(msg.encode(), wire);
    }
}

#[test]
fn chat_text_is_escaped_on_one_line() {
    let line = ServerMsg::Chat { from: 1, text: "two \"east\"\nof you".into() }.encode();
    assert!(!line.contains('\n'));
    let back: ServerMsg = serde_json::from_str(&line).unwrap();
    assert_eq!(back, ServerMsg::Chat { from: 1, text: "two \"east\"\nof you".into() });
}

#[test]
fn decodes_the_choice_and_the_vote() {
    use hollow::protocol::Pick;
    assert_eq!(ClientMsg::decode(r#"{"t":"choice","pick":"both"}"#).unwrap(), ClientMsg::Choice { pick: Pick::Both });
    assert_eq!(ClientMsg::decode(r#"{"t":"choice","pick":"quit"}"#).unwrap(), ClientMsg::Choice { pick: Pick::Quit });
    assert_eq!(ClientMsg::decode(r#"{"t":"vote","reset":true}"#).unwrap(), ClientMsg::Vote { reset: true });
    assert!(ClientMsg::decode(r#"{"t":"choice","pick":"maybe"}"#).is_err());
}

#[test]
fn encodes_pauses_restarts_votes_water_and_hash() {
    use hollow::protocol::{RoundReason, VoteState};
    let cases = [
        (
            ServerMsg::Round { tick: 7, state: RoundState::Paused, reason: Some(RoundReason::PartnerLeft), seconds: Some(60) },
            r#"{"t":"round","tick":7,"state":"paused","reason":"partner_left","seconds":60}"#,
        ),
        (
            ServerMsg::Round { tick: 7, state: RoundState::Choose, reason: Some(RoundReason::PartnerLeft), seconds: None },
            r#"{"t":"round","tick":7,"state":"choose","reason":"partner_left"}"#,
        ),
        (
            ServerMsg::Round { tick: 9, state: RoundState::Playing, reason: Some(RoundReason::Fell), seconds: None },
            r#"{"t":"round","tick":9,"state":"playing","reason":"fell"}"#,
        ),
        (
            ServerMsg::Vote { by: 0, ticks_left: 600, state: VoteState::Open },
            r#"{"t":"vote","by":0,"ticks_left":600,"state":"open"}"#,
        ),
        (
            ServerMsg::Snapshot { tick: 3, entities: vec![(1, 2, 10, 2, -1)], water: vec![(3, 4)], hash: Some("00000000000000ff".into()) },
            r#"{"t":"snapshot","tick":3,"entities":[[1,2,10,2,-1]],"water":[[3,4]],"hash":"00000000000000ff"}"#,
        ),
    ];
    for (msg, wire) in cases {
        assert_eq!(msg.encode(), wire);
    }
}

#[test]
fn canonical_encoding_is_byte_exact() {
    use hollow::canon;
    let bytes = canon::encode(0x0102_0304, &[(1, -1, 4, 0, -1)], &[(2, 3)]);
    let want: Vec<u8> = [
        &b"HHC1"[..],
        &[4, 3, 2, 1],                                  // tick
        &[1, 0, 0, 0],                                  // one entity
        &[1, 0, 0, 0, 0xff, 0xff, 0xff, 0xff, 4, 0, 0xff], // x, y, kind, state, carried_by
        &[1, 0, 0, 0],                                  // one water cell
        &[2, 0, 0, 0, 3, 0, 0, 0],
    ]
    .concat();
    assert_eq!(bytes, want);
    // XXH3-64 of no input is a published constant; the hash function is the reference one.
    assert_eq!(xxhash_rust::xxh3::xxh3_64(b""), 0x2D06_8005_38D3_94C2);
    assert_eq!(canon::hash_hex(0, &[], &[]).len(), 16);
}
