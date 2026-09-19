using System.Collections.Generic;
using Crawler.Protocol;

namespace Crawler.Mock
{
    /// <summary>
    /// One level's simulation, with no clock of its own: the caller advances it tick by tick.
    /// That keeps it deterministic and testable from the editor. MockFeed wraps it with a
    /// real-time clock and level progression.
    ///
    /// The mock has one client, and it holds both seats: whichever leopard it is steering
    /// moves, and the other stands still. That is also how solo play works on the real server.
    /// </summary>
    public sealed class MockSim
    {
        public const byte ServerSender = 255;   // mock convention for server-authored chat
        public const uint StepTicks = 9;        // one cell per 150 ms while held

        internal readonly MockWorld World;
        public readonly Queue<string> Out = new Queue<string>();

        int _active;
        MoveDir _move;
        bool _interact;
        readonly uint[] _nextStep = new uint[MockWorld.Seats];

        public int Active => _active;
        public bool Won => World.Won;
        public (int x, int y) SeatPos(int seat) => World.Pos[seat];
        public int KeysCarried => World.Carried.Count;

        public MockSim(LevelDef level)
        {
            World = new MockWorld(level, Out);
            World.Mechanics.Add(new CoreMechanic()); // always on; later mechanics come from the level
            foreach (var m in World.Mechanics) m.OnLoad(World);
        }

        /// <summary>Put the level back to its start: both leopards home, no keys, every mechanic reset.</summary>
        public void Restart(uint tick)
        {
            for (int s = 0; s < MockWorld.Seats; s++)
            {
                World.Pos[s] = World.Spawn[s];
                _nextStep[s] = 0;
            }
            World.Carried.Clear();
            World.Won = false;
            foreach (var m in World.Mechanics) m.Reset(World);
            _move = MoveDir.None;
            _interact = false;
            Out.Enqueue(WireCodec.EncodeOverlay(Perception(tick)));
            Out.Enqueue(WireCodec.EncodeRound(new RoundMsg { Tick = tick, State = RoundState.Playing }));
            Out.Enqueue(WireCodec.EncodeSeats(Seats()));
        }

        // ------------------------------------------------------------------ input

        public void SetInput(MoveDir move, bool interact)
        {
            _move = move;
            _interact |= interact; // consumed on the next simulated step
        }

        public void SetActive(int seat, uint tick)
        {
            if (seat < 0 || seat >= MockWorld.Seats || seat == _active) return;
            _active = seat;
            _move = MoveDir.None; // a held key belongs to the leopard you let go of
            Out.Enqueue(WireCodec.EncodeSeats(Seats()));
            Out.Enqueue(WireCodec.EncodeOverlay(Perception(tick))); // perception follows the active seat
        }

        public SeatsMsg Seats() => new SeatsMsg { Yours = new[] { 0, 1 }, Active = _active };

        // ------------------------------------------------------------------ simulation

        /// <summary>Advance to this tick. Only the steered leopard acts; the idle one stands still.</summary>
        public void Step(uint tick)
        {
            if (World.Won) { _interact = false; return; }

            if (_interact)
            {
                _interact = false;
                foreach (var m in World.Mechanics)
                    if (m.OnInteract(World, _active, tick)) { Out.Enqueue(WireCodec.EncodeOverlay(Perception(tick))); break; }
            }

            if (_move != MoveDir.None && tick >= _nextStep[_active]) TryMove(_active, tick);
        }

        void TryMove(int seat, uint tick)
        {
            var (x, y) = World.Pos[seat];
            switch (_move)
            {
                case MoveDir.N: y++; break;
                case MoveDir.S: y--; break;
                case MoveDir.E: x++; break;
                case MoveDir.W: x--; break;
            }
            if (!World.Walkable(x, y, seat)) return;
            World.Pos[seat] = (x, y);
            _nextStep[seat] = tick + StepTicks;
            foreach (var m in World.Mechanics)
            {
                m.OnEnter(World, seat, tick);
                if (World.Pos[seat] != (x, y)) break; // a fall moved the leopard; nothing else happens here
            }
        }

        public Snapshot Snapshot(uint tick)
        {
            var arr = new EntityState[World.Ents.Count];
            for (int s = 0; s < MockWorld.Seats; s++)
                arr[s] = new EntityState(World.Pos[s].x, World.Pos[s].y, EntityKind.Player, 0, -1);
            foreach (var m in World.Mechanics) m.Write(World, arr);
            return new Snapshot { Tick = tick, Entities = arr };
        }

        /// <summary>What the steered leopard sees.</summary>
        public Overlay Perception(uint tick)
        {
            var b = new OverlayBuilder();
            foreach (var m in World.Mechanics) m.Perceive(World, _active, b);
            return b.Build(tick);
        }
    }
}
