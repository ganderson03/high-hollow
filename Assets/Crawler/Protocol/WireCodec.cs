using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Crawler.Protocol
{
    /// <summary>
    /// JSON Lines wire format. This is transport encoding only; it is deliberately
    /// NOT the canonical encoding that will later be hashed for parity with Rust.
    /// </summary>
    public static class WireCodec
    {
        // ---------------------------------------------------------------- decode

        /// <summary>Decode one line and dispatch it. Returns false with an error on malformed input.</summary>
        public static bool TryDispatch(string line, IInboundHandler handler, out string error)
        {
            error = null;
            JObject o;
            try { o = JObject.Parse(line); }
            catch (JsonException e) { error = "bad json: " + e.Message; return false; }

            try
            {
                switch ((string)o["t"])
                {
                    case "welcome":
                        // "grid" is the old square form; width/height replace it.
                        int w = o["width"] != null ? (int)o["width"] : (int)o["grid"];
                        int h = o["height"] != null ? (int)o["height"] : (int)o["grid"];
                        handler.OnWelcome(new Welcome
                        {
                            ClientId = (byte)o["client_id"],
                            Width = w,
                            Height = h,
                            TickHz = (int)o["tick_hz"],
                            ReplicationHz = (int)o["replication_hz"],
                        });
                        return true;

                    case "seats":
                    {
                        var yours = (JArray)o["yours"];
                        var seats = new SeatsMsg { Yours = new int[yours.Count], Active = (int)o["active"] };
                        for (int i = 0; i < yours.Count; i++) seats.Yours[i] = (int)yours[i];
                        handler.OnSeats(seats);
                        return true;
                    }

                    case "snapshot":
                        handler.OnSnapshot(DecodeSnapshot(o));
                        return true;

                    case "overlay":
                        handler.OnOverlay(DecodeOverlay(o));
                        return true;

                    case "chat":
                        handler.OnChat(new ChatMsg { From = (byte)o["from"], Text = (string)o["text"] ?? "" });
                        return true;

                    case "level":
                        handler.OnLevel(new LevelMsg { Id = (string)o["id"] });
                        return true;

                    case "round":
                        handler.OnRound(new RoundMsg
                        {
                            Tick = (uint)o["tick"],
                            State = RoundFromWire((string)o["state"]),
                            Reason = (string)o["reason"],
                            Seconds = o["seconds"] != null ? (int)o["seconds"] : 0,
                        });
                        return true;

                    case "vote":
                        handler.OnVote(new VoteMsg
                        {
                            By = (byte)o["by"],
                            TicksLeft = (int)o["ticks_left"],
                            State = VoteFromWire((string)o["state"]),
                        });
                        return true;

                    default:
                        error = "unknown message type: " + (string)o["t"];
                        return false;
                }
            }
            catch (Exception e) when (e is InvalidCastException || e is ArgumentException
                                      || e is NullReferenceException || e is FormatException
                                      || e is OverflowException || e is JsonException)
            {
                error = "malformed " + (string)o["t"] + ": " + e.Message;
                return false;
            }
        }

        /// <summary>Entities arrive as compact arrays <c>[x, y, kind, state, carried_by]</c>; water and hash are optional
        /// (older servers and the mock send neither).</summary>
        static Snapshot DecodeSnapshot(JObject o)
        {
            var arr = (JArray)o["entities"];
            var ents = new EntityState[arr.Count];
            for (int i = 0; i < ents.Length; i++)
            {
                var e = (JArray)arr[i];
                if (e.Count != 5) throw new FormatException($"entity {i} has {e.Count} fields, expected 5");
                ents[i] = new EntityState((int)e[0], (int)e[1], (EntityKind)(byte)e[2], (byte)e[3], (sbyte)e[4]);
            }
            var water = (JArray)o["water"] ?? new JArray();
            var cells = new Cell[water.Count];
            for (int i = 0; i < cells.Length; i++) cells[i] = new Cell((int)water[i][0], (int)water[i][1]);
            return new Snapshot { Tick = (uint)o["tick"], Entities = ents, Water = cells, Hash = (string)o["hash"] };
        }

        /// <summary>Any of the three lists may be missing; a missing list means nothing of that sort.</summary>
        static Overlay DecodeOverlay(JObject o)
        {
            var hidden = (JArray)o["hidden"] ?? new JArray();
            var subs = (JArray)o["substituted"] ?? new JArray();
            var phan = (JArray)o["phantoms"] ?? new JArray();

            var ov = new Overlay
            {
                Tick = (uint)o["tick"],
                Hidden = new int[hidden.Count],
                Substituted = new Substitution[subs.Count],
                Phantoms = new Phantom[phan.Count],
            };
            for (int i = 0; i < hidden.Count; i++) ov.Hidden[i] = (int)hidden[i];
            for (int i = 0; i < subs.Count; i++) ov.Substituted[i] = new Substitution((int)subs[i][0], (int)subs[i][1]);
            for (int i = 0; i < phan.Count; i++) ov.Phantoms[i] = new Phantom((int)phan[i][0], (int)phan[i][1], (int)phan[i][2]);
            return ov;
        }

        // ---------------------------------------------------------------- encode (client -> server)

        /// <summary>The first line a client sends.</summary>
        public static string EncodeHello(string name) =>
            Write(w => { Field(w, "t", "hello"); Field(w, "name", name); });

        /// <summary>The held direction and any interact press, stamped with the last tick seen.</summary>
        public static string EncodeInput(InputMsg m) =>
            Write(w =>
            {
                Field(w, "t", "input");
                w.WritePropertyName("tick"); w.WriteValue(m.Tick);
                Field(w, "move", MoveToWire(m.Move));
                w.WritePropertyName("interact"); w.WriteValue(m.Interact);
            });

        /// <summary>Propose or agree to a restart (true), or withdraw or decline (false).</summary>
        public static string EncodeVote(bool reset) =>
            Write(w => { Field(w, "t", "vote"); w.WritePropertyName("reset"); w.WriteValue(reset); });

        /// <summary>The answer once a dropped partner's minute is up.</summary>
        public static string EncodeChoice(ChoicePick pick) =>
            Write(w => { Field(w, "t", "choice"); Field(w, "pick", pick == ChoicePick.Quit ? "quit" : "both"); });

        /// <summary>A chat line. The server ignores <c>from</c> and stamps the real sender.</summary>
        public static string EncodeChat(byte from, string text) =>
            Write(w =>
            {
                Field(w, "t", "chat");
                w.WritePropertyName("from"); w.WriteValue(from);
                Field(w, "text", text);
            });

        // ---------------------------------------------------------------- encode (server -> client; used by MockFeed)

        /// <summary>Server side, used by the mock so it speaks exactly what a server would.</summary>
        public static string EncodeWelcome(Welcome m) =>
            Write(w =>
            {
                Field(w, "t", "welcome");
                w.WritePropertyName("client_id"); w.WriteValue(m.ClientId);
                w.WritePropertyName("width"); w.WriteValue(m.Width);
                w.WritePropertyName("height"); w.WriteValue(m.Height);
                w.WritePropertyName("tick_hz"); w.WriteValue(m.TickHz);
                w.WritePropertyName("replication_hz"); w.WriteValue(m.ReplicationHz);
            });

        /// <summary>Server side (mock). Water and hash are left out; the mock has neither.</summary>
        public static string EncodeSnapshot(Snapshot s) =>
            Write(w =>
            {
                Field(w, "t", "snapshot");
                w.WritePropertyName("tick"); w.WriteValue(s.Tick);
                w.WritePropertyName("entities");
                w.WriteStartArray();
                foreach (var e in s.Entities)
                {
                    w.WriteStartArray();
                    w.WriteValue(e.X); w.WriteValue(e.Y); w.WriteValue((byte)e.Kind);
                    w.WriteValue(e.State); w.WriteValue(e.CarriedBy);
                    w.WriteEndArray();
                }
                w.WriteEndArray();
            });

        /// <summary>Server side (mock): one seat's perception.</summary>
        public static string EncodeOverlay(Overlay ov) =>
            Write(w =>
            {
                Field(w, "t", "overlay");
                w.WritePropertyName("tick"); w.WriteValue(ov.Tick);
                w.WritePropertyName("hidden");
                w.WriteStartArray(); foreach (var id in ov.Hidden) w.WriteValue(id); w.WriteEndArray();
                w.WritePropertyName("substituted");
                w.WriteStartArray();
                foreach (var s in ov.Substituted) { w.WriteStartArray(); w.WriteValue(s.Id); w.WriteValue(s.Look); w.WriteEndArray(); }
                w.WriteEndArray();
                w.WritePropertyName("phantoms");
                w.WriteStartArray();
                foreach (var p in ov.Phantoms) { w.WriteStartArray(); w.WriteValue(p.X); w.WriteValue(p.Y); w.WriteValue(p.Look); w.WriteEndArray(); }
                w.WriteEndArray();
            });

        /// <summary>Server side (mock): which level file to load.</summary>
        public static string EncodeLevel(string id) =>
            Write(w => { Field(w, "t", "level"); Field(w, "id", id); });

        /// <summary>Server side (mock): the round state, with a reason and countdown only when they apply.</summary>
        public static string EncodeRound(RoundMsg m) =>
            Write(w =>
            {
                Field(w, "t", "round");
                w.WritePropertyName("tick"); w.WriteValue(m.Tick);
                Field(w, "state", RoundToWire(m.State));
                if (m.Reason != null) Field(w, "reason", m.Reason);
                if (m.State == RoundState.Paused) { w.WritePropertyName("seconds"); w.WriteValue(m.Seconds); }
            });

        /// <summary>Server side (mock): a vote's progress.</summary>
        public static string EncodeVoteMsg(VoteMsg m) =>
            Write(w =>
            {
                Field(w, "t", "vote");
                w.WritePropertyName("by"); w.WriteValue(m.By);
                w.WritePropertyName("ticks_left"); w.WriteValue(m.TicksLeft);
                Field(w, "state", m.State == VoteState.Open ? "open" : m.State == VoteState.Passed ? "passed" : "lapsed");
            });

        /// <summary>Server side (mock): which leopards this client holds.</summary>
        public static string EncodeSeats(SeatsMsg m) =>
            Write(w =>
            {
                Field(w, "t", "seats");
                w.WritePropertyName("yours");
                w.WriteStartArray(); foreach (var s in m.Yours) w.WriteValue(s); w.WriteEndArray();
                w.WritePropertyName("active"); w.WriteValue(m.Active);
            });

        /// <summary>Ask to steer another of this client's leopards.</summary>
        public static string EncodeControl(int seat) =>
            Write(w => { Field(w, "t", "control"); w.WritePropertyName("seat"); w.WriteValue(seat); });

        // ---------------------------------------------------------------- helpers

        /// <summary>Wire name to round state; an unknown name is a protocol error.</summary>
        static RoundState RoundFromWire(string s)
        {
            switch (s)
            {
                case "playing": return RoundState.Playing;
                case "won": return RoundState.Won;
                case "paused": return RoundState.Paused;
                case "choose": return RoundState.Choose;
                default: throw new FormatException("unknown round state: " + s);
            }
        }

        /// <summary>Round state to wire name.</summary>
        static string RoundToWire(RoundState s)
        {
            switch (s)
            {
                case RoundState.Won: return "won";
                case RoundState.Paused: return "paused";
                case RoundState.Choose: return "choose";
                default: return "playing";
            }
        }

        /// <summary>Wire name to vote state; an unknown name is a protocol error.</summary>
        static VoteState VoteFromWire(string s)
        {
            switch (s)
            {
                case "open": return VoteState.Open;
                case "passed": return VoteState.Passed;
                case "lapsed": return VoteState.Lapsed;
                default: throw new FormatException("unknown vote state: " + s);
            }
        }

        /// <summary>Direction to its one-letter wire name.</summary>
        public static string MoveToWire(MoveDir m)
        {
            switch (m)
            {
                case MoveDir.N: return "n";
                case MoveDir.S: return "s";
                case MoveDir.E: return "e";
                case MoveDir.W: return "w";
                default: return "none";
            }
        }

        /// <summary>Writes one string property.</summary>
        static void Field(JsonWriter w, string name, string value)
        {
            w.WritePropertyName(name);
            w.WriteValue(value);
        }

        /// <summary>Builds one JSON object on one line: the unit of JSON Lines.</summary>
        static string Write(Action<JsonWriter> body)
        {
            var sb = new StringWriter(CultureInfo.InvariantCulture);
            using (var w = new JsonTextWriter(sb) { Formatting = Formatting.None })
            {
                w.WriteStartObject();
                body(w);
                w.WriteEndObject();
            }
            return sb.ToString();
        }
    }
}
