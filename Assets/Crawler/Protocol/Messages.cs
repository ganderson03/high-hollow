// Plain message types. No engine types, no JSON types: the wire codec is the only
// place that knows about field names, so a later canonical/hashed encoding can sit
// beside it without touching anything downstream.
namespace Crawler.Protocol
{
    public enum EntityKind : byte
    {
        Player = 0,
        Door = 1,
        Lever = 2,
        Plate = 3,
        Key = 4,
        Hazard = 5,
        Goal = 6,
        Sightstone = 7,
        Ledge = 8,       // state 0 intact, 1 cracked, 2 collapsed
        Boulder = 9,     // state: shape (0 boulder, 1 slab along x, 2 along y) + 4 straining + 8 gone
        Sluice = 10,     // state: open exit, 0 n, 1 e, 2 s, 3 w
        Wheel = 11,      // state 1 = turning
        Checkpoint = 12, // state 1 = lit
    }

    public enum MoveDir : byte { None, N, S, E, W }

    public readonly struct EntityState
    {
        public readonly int X;
        public readonly int Y;
        public readonly EntityKind Kind;
        public readonly byte State;
        public readonly sbyte CarriedBy; // -1 = not carried, else player id

        public EntityState(int x, int y, EntityKind kind, byte state, sbyte carriedBy)
        {
            X = x; Y = y; Kind = kind; State = state; CarriedBy = carriedBy;
        }
    }

    public sealed class Welcome
    {
        public byte ClientId;
        public int Width;   // level size in cells; levels are rectangular
        public int Height;
        public int TickHz;
        public int ReplicationHz;
    }

    /// <summary>
    /// Which leopards this client controls, and which one it is steering. Server -> client,
    /// whenever control changes. A client holding both seats switches between them with a
    /// Control message; the idle leopard stands still.
    /// </summary>
    public sealed class SeatsMsg
    {
        public int[] Yours;
        public int Active;
    }

    public readonly struct Cell
    {
        public readonly int X;
        public readonly int Y;
        public Cell(int x, int y) { X = x; Y = y; }
    }

    /// <summary>Authoritative world state. Index in Entities is the entity id.</summary>
    public sealed class Snapshot
    {
        public uint Tick;
        public EntityState[] Entities;
        /// <summary>Cells with flowing water, top row first. Empty when the level is dry.</summary>
        public Cell[] Water = new Cell[0];
        /// <summary>XXH3-64 of the canonical encoding as 16 hex digits; null from servers that do not send it.</summary>
        public string Hash;
    }

    public readonly struct Substitution
    {
        public readonly int Id;
        public readonly int Look; // wire name: "sprite"
        public Substitution(int id, int look) { Id = id; Look = look; }
    }

    public readonly struct Phantom
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Look; // wire name: "sprite"
        public Phantom(int x, int y, int look) { X = x; Y = y; Look = look; }
    }

    /// <summary>Per-client perception layer. Never feeds back into inputs or hashed state.</summary>
    public sealed class Overlay
    {
        public uint Tick;
        public int[] Hidden;
        public Substitution[] Substituted;
        public Phantom[] Phantoms;

        public static readonly Overlay Empty = new Overlay
        {
            Hidden = new int[0],
            Substituted = new Substitution[0],
            Phantoms = new Phantom[0],
        };
    }

    public sealed class ChatMsg
    {
        public byte From;
        public string Text;
    }

    public readonly struct InputMsg
    {
        public readonly uint Tick;
        public readonly MoveDir Move;
        public readonly bool Interact;
        public InputMsg(uint tick, MoveDir move, bool interact) { Tick = tick; Move = move; Interact = interact; }
    }

    /// <summary>Which level file to load. Server -> client, after welcome.</summary>
    public sealed class LevelMsg
    {
        public string Id;
    }

    /// <summary>
    /// Playing and Won as before. Paused: the partner dropped and the world is frozen while they
    /// have Seconds left to come back. Choose: they did not; this client picks Both or Quit.
    /// </summary>
    public enum RoundState : byte { Playing, Won, Paused, Choose }

    /// <summary>Server-decided round state; the client only displays it.</summary>
    public sealed class RoundMsg
    {
        public uint Tick;
        public RoundState State;
        /// <summary>Why a round started that was not a fresh level: "fell", "vote" or "partner_left"; else null.</summary>
        public string Reason;
        /// <summary>Seconds left to come back, while paused.</summary>
        public int Seconds;
    }

    public enum VoteState : byte { Open, Passed, Lapsed }

    /// <summary>A restart proposal: who proposed it, how long it has left, and how it ended.</summary>
    public sealed class VoteMsg
    {
        public byte By;
        public int TicksLeft;
        public VoteState State;
    }

    /// <summary>The answer to RoundState.Choose.</summary>
    public enum ChoicePick : byte { Both, Quit }

    public enum LinkStatus { Idle, Connecting, Connected, Mock, Disconnected, Failed, ProtocolError }
}
