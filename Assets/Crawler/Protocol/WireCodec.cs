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
                        handler.OnWelcome(new Welcome
                        {
                            ClientId = (byte)o["client_id"],
                            Grid = (int)o["grid"],
                            TickHz = (int)o["tick_hz"],
                            ReplicationHz = (int)o["replication_hz"],
                        });
                        return true;

                    case "snapshot":
                        handler.OnSnapshot(DecodeSnapshot(o));
                        return true;

                    case "overlay":
                        handler.OnOverlay(DecodeOverlay(o));
                        return true;

                    case "chat":
                        handler.OnChat(new ChatMsg { From = (byte)o["from"], Text = (string)o["text"] ?? "" });
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
                w.WritePropertyName("grid"); w.WriteValue(m.Grid);
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

        // ---------------------------------------------------------------- helpers

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
