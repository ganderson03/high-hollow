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

    /// <summary>Authoritative world state. Index in Entities is the entity id.</summary>
    public sealed class Snapshot
    {
        public uint Tick;
        public EntityState[] Entities;
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

    public enum RoundState : byte { Playing, Won }

    /// <summary>Server-decided round state; the client only displays it.</summary>
    public sealed class RoundMsg
    {
        public uint Tick;
        public RoundState State;
    }

    public enum LinkStatus { Idle, Connecting, Connected, Mock, Disconnected, Failed, ProtocolError }
}
