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
                        handler.OnRound(new RoundMsg { Tick = (uint)o["tick"], State = RoundFromWire((string)o["state"]) });
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
            return new Snapshot { Tick = (uint)o["tick"], Entities = ents };
        }

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

        /// <summary>
        /// Decode one line a client sent and dispatch it: the mirror of TryDispatch, used by
        /// the server. A client's message is a request, never a fact, so everything here is
        /// treated as untrusted and a malformed line is reported rather than thrown.
        /// </summary>
        public static bool TryDispatchClient(string line, IClientHandler handler, out string error)
        {
            error = null;
            JObject o;
            try { o = JObject.Parse(line); }
            catch (JsonException e) { error = "bad json: " + e.Message; return false; }

            try
            {
                switch ((string)o["t"])
                {
                    case "hello":
                        handler.OnHello((string)o["name"] ?? "");
                        return true;

                    case "input":
                        handler.OnInput(new InputMsg(
                            (uint)o["tick"],
                            MoveFromWire((string)o["move"]),
                            (bool)o["interact"]));
                        return true;

                    case "chat":
                        handler.OnChat(new ChatMsg { From = (byte)o["from"], Text = (string)o["text"] ?? "" });
                        return true;

                    case "control":
                        handler.OnControl((int)o["seat"]);
                        return true;

                    default:
                        error = "unknown client message type: " + (string)o["t"];
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

        // ---------------------------------------------------------------- encode (client -> server)

        public static string EncodeHello(string name) =>
            Write(w => { Field(w, "t", "hello"); Field(w, "name", name); });

        public static string EncodeInput(InputMsg m) =>
            Write(w =>
            {
                Field(w, "t", "input");
                w.WritePropertyName("tick"); w.WriteValue(m.Tick);
                Field(w, "move", MoveToWire(m.Move));
                w.WritePropertyName("interact"); w.WriteValue(m.Interact);
            });

        public static string EncodeChat(byte from, string text) =>
            Write(w =>
            {
                Field(w, "t", "chat");
                w.WritePropertyName("from"); w.WriteValue(from);
                Field(w, "text", text);
            });

        // ---------------------------------------------------------------- encode (server -> client; used by MockFeed)

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

        public static string EncodeLevel(string id) =>
            Write(w => { Field(w, "t", "level"); Field(w, "id", id); });

        public static string EncodeRound(RoundMsg m) =>
            Write(w =>
            {
                Field(w, "t", "round");
                w.WritePropertyName("tick"); w.WriteValue(m.Tick);
                Field(w, "state", m.State == RoundState.Won ? "won" : "playing");
            });

        public static string EncodeSeats(SeatsMsg m) =>
            Write(w =>
            {
                Field(w, "t", "seats");
                w.WritePropertyName("yours");
                w.WriteStartArray(); foreach (var s in m.Yours) w.WriteValue(s); w.WriteEndArray();
                w.WritePropertyName("active"); w.WriteValue(m.Active);
            });

        public static string EncodeControl(int seat) =>
            Write(w => { Field(w, "t", "control"); w.WritePropertyName("seat"); w.WriteValue(seat); });

        // ---------------------------------------------------------------- helpers

        static RoundState RoundFromWire(string s)
        {
            switch (s)
            {
                case "playing": return RoundState.Playing;
                case "won": return RoundState.Won;
                default: throw new FormatException("unknown round state: " + s);
            }
        }

        public static MoveDir MoveFromWire(string s)
        {
            switch (s)
            {
                case "n": return MoveDir.N;
                case "s": return MoveDir.S;
                case "e": return MoveDir.E;
                case "w": return MoveDir.W;
                case "none":
                case null: return MoveDir.None;
                default: throw new FormatException("unknown move: " + s);
            }
        }

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

        static void Field(JsonWriter w, string name, string value)
        {
            w.WritePropertyName(name);
            w.WriteValue(value);
        }

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
