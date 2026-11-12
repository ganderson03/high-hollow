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
            ServerMsg::Snapshot { tick: 412, entities: vec![(15, 9, 0, 0, -1)] },
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
        (ServerMsg::Round { tick: 412, state: RoundState::Playing }, r#"{"t":"round","tick":412,"state":"playing"}"#),
        (ServerMsg::Round { tick: 9, state: RoundState::Won }, r#"{"t":"round","tick":9,"state":"won"}"#),
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
