using System.Collections.Generic;
using Crawler.Protocol;

namespace Crawler.Mock
{
    /// <summary>A level entity as the mock simulates it: what it is, and where it started.</summary>
    struct MockEnt
    {
        public EntityKind Kind;
        public char Glyph;
        public int X, Y, Region;
    }

    /// <summary>
    /// Shared state every mechanic works against: the level, both seats, the entity list
    /// (index = entity id on the wire) and carried keys. Mechanics own their own extra state.
    /// </summary>
    sealed class MockWorld
    {
        public const int Seats = 2;

        public readonly LevelDef Level;
        public readonly List<MockEnt> Ents = new List<MockEnt>();
        public readonly (int x, int y)[] Spawn = new (int, int)[Seats];
        public readonly (int x, int y)[] Pos = new (int, int)[Seats];

        /// <summary>Key entity id -> the seat carrying it.</summary>
        public readonly Dictionary<int, int> Carried = new Dictionary<int, int>();

        public readonly List<IMechanic> Mechanics = new List<IMechanic>();
        public readonly Queue<string> Out;

        public bool Won;
        public int FirstEntity => Seats; // entity ids 0 and 1 are the seats

        public MockWorld(LevelDef level, Queue<string> output)
        {
            Level = level;
            Out = output;

            // Seats first, so entity id == seat. P is seat 0, 1 is seat 1. A level missing '1'
            // puts the second leopard beside the first, so old single-spawn levels still load.
            if (!Find('P', out int px, out int py)) { px = 1; py = 1; }
            if (!Find('1', out int qx, out int qy)) { qx = px + 1; qy = py; }
            Spawn[0] = (px, py);
            Spawn[1] = (qx, qy);
            for (int s = 0; s < Seats; s++)
                Ents.Add(new MockEnt { Kind = EntityKind.Player, Glyph = s == 0 ? 'P' : '1', X = Spawn[s].x, Y = Spawn[s].y, Region = level.RegionAt(Spawn[s].x, Spawn[s].y) });

            // Everything else in row-scan order, top row first, as an author reads the map.
            for (int y = level.Height - 1; y >= 0; y--)
            for (int x = 0; x < level.Width; x++)
            {
                char c = level.At(x, y);
                switch (c)
                {
                    case 'D':
                    case 'F': Add(EntityKind.Door, c, x, y); break;
                    case 'L': Add(EntityKind.Lever, c, x, y); break;
                    case 'B': Add(EntityKind.Plate, c, x, y); break;
                    case 'K': Add(EntityKind.Key, c, x, y); break;
                    case 'H':
                    case 'h': Add(EntityKind.Hazard, c, x, y); break;
                    case 'G': Add(EntityKind.Goal, c, x, y); break;
                    case 'S': Add(EntityKind.Sightstone, c, x, y); break;
                }
            }
        }

        void Add(EntityKind kind, char glyph, int x, int y) =>
            Ents.Add(new MockEnt { Kind = kind, Glyph = glyph, X = x, Y = y, Region = Level.RegionAt(x, y) });

        public bool Find(char glyph, out int fx, out int fy)
        {
            for (int y = Level.Height - 1; y >= 0; y--)
            for (int x = 0; x < Level.Width; x++)
                if (Level.At(x, y) == glyph) { fx = x; fy = y; return true; }
            fx = fy = 0;
            return false;
        }

        /// <summary>The cell is inside the map, not a wall or tree, not held by a mechanic, and free of the other leopard.</summary>
        public bool Walkable(int x, int y, int movingSeat)
        {
            if (!Level.InBounds(x, y) || LevelDef.IsSolidTile(Level.At(x, y))) return false;
            for (int s = 0; s < Seats; s++)
                if (s != movingSeat && Pos[s].x == x && Pos[s].y == y) return false;
            foreach (var m in Mechanics)
                if (m.Blocks(this, x, y)) return false;
            return true;
        }

        public bool SeatAt(int x, int y)
        {
            for (int s = 0; s < Seats; s++)
                if (Pos[s].x == x && Pos[s].y == y) return true;
            return false;
        }

        public string RuleOf(int region) =>
            region >= 0 && region < Level.Regions.Count ? Level.Regions[region].Rule : "";

        public void Say(string text) => Out.Enqueue(WireCodec.EncodeChat(MockSim.ServerSender, text));

        /// <summary>
        /// Minor failure: only this leopard pays. It returns to the entrance of the room it fell
        /// in (or its spawn if the room has none) and every key it carries goes back where it lay.
        /// </summary>
        public void MinorFailure(int seat, int x, int y)
        {
            int region = Level.RegionAt(x, y);
            Pos[seat] = Level.EntranceOf(region, out int ex, out int ey) && !OtherSeatAt(seat, ex, ey) ? (ex, ey) : Spawn[seat];

            int dropped = 0;
            var mine = new List<int>();
            foreach (var kv in Carried) if (kv.Value == seat) mine.Add(kv.Key);
            foreach (var id in mine) { Carried.Remove(id); dropped++; }
            if (dropped == 1) Say("the key slips loose and lands back where it lay.");
            else if (dropped > 1) Say($"all {dropped} keys slip loose and land back where they lay.");
        }

        bool OtherSeatAt(int seat, int x, int y)
        {
            int other = 1 - seat;
            return Pos[other].x == x && Pos[other].y == y;
        }
    }

    /// <summary>Collects one seat's perception before it is sent as an overlay.</summary>
    sealed class OverlayBuilder
    {
        public readonly List<int> Hidden = new List<int>();
        public readonly List<Substitution> Substituted = new List<Substitution>();
        public readonly List<Phantom> Phantoms = new List<Phantom>();

        public Overlay Build(uint tick) => new Overlay
        {
            Tick = tick,
            Hidden = Hidden.ToArray(),
            Substituted = Substituted.ToArray(),
            Phantoms = Phantoms.ToArray(),
        };
    }
}
