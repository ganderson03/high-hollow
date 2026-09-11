using System;
using System.Collections.Generic;
using System.Diagnostics;
using Crawler.Protocol;

namespace Crawler.Mock
{
    /// <summary>
    /// Stand-in server. Emits snapshots at 20 Hz as JSON lines through the same WireCodec
    /// the real connection uses, so decoding is exercised with zero server.
    ///
    /// DEMO RULES LIVE HERE AND ONLY HERE. The movement/lever/hazard/goal logic below is a
    /// throwaway stand-in so the input -> snapshot -> render loop is playable before the
    /// Rust server exists. It is not a spec for the server's rules; delete it when the
    /// server lands. Nothing outside Crawler.Mock references it.
    /// </summary>
    public sealed class MockFeed : IServerLink
    {
        const int TickHz = 60;
        const int ReplicationHz = 20;
        const int TicksPerSnapshot = TickHz / ReplicationHz;
        const int Grid = 32;
        const byte LocalClientId = 0;
        const byte ServerSender = 255;       // mock convention for server-authored chat
        const uint StepTicks = 9;            // one cell per 150 ms while a direction is held
        const uint ResetDelayTicks = 180;    // 3 s after a win

        // Layout. The goal sits in an alcove walled by visible hazards; the door is the way in.
        const int SpawnX = 11, SpawnY = 16;
        const int LeverX = 19, LeverY = 15;
        const int DoorX = 15, DoorY = 23;
        const int GoalX = 15, GoalY = 24;
        const int PlateX = 13, PlateY = 13;
        const int TrapX = 16, TrapY = 16;    // on the straight path to the lever; hidden from client 0
        static readonly (int x, int y)[] Ring =
            { (14, 23), (16, 23), (14, 24), (16, 24), (14, 25), (15, 25), (16, 25) };

        // Entity ids (array indices).
        const int IdPlate = 5;
        const int IdTrap = 7;

        readonly Stopwatch _clock = new Stopwatch();
        readonly Queue<string> _lines = new Queue<string>();
        uint _nextSnapshotTick;
        bool _overlaySent;
        bool _started;

        // Demo world state (mock-only).
        int _px = SpawnX, _py = SpawnY;
        bool _leverOn;
        MoveDir _move;
        bool _interact;
        uint _nextStep;
        bool _won;
        uint _resetAt;

        public void Start(string playerName)
        {
            _started = true;
            _clock.Start();
            _lines.Enqueue(WireCodec.EncodeWelcome(new Welcome
            {
                ClientId = LocalClientId, Grid = Grid, TickHz = TickHz, ReplicationHz = ReplicationHz,
            }));
            Say($"welcome, {playerName}. pull the lever by the east wall (space) to open the door; the goal's behind it. mind your step.");
        }

        public void Pump(IInboundHandler handler)
        {
            if (!_started) return;

            uint serverTick = (uint)(_clock.ElapsedMilliseconds * TickHz / 1000);
            int budget = 4;
            while (_nextSnapshotTick <= serverTick && budget-- > 0)
            {
                Simulate(_nextSnapshotTick);
                _lines.Enqueue(WireCodec.EncodeSnapshot(BuildSnapshot(_nextSnapshotTick)));
                if (!_overlaySent)
                {
                    _lines.Enqueue(WireCodec.EncodeOverlay(OverlayFor(LocalClientId, _nextSnapshotTick)));
                    _overlaySent = true;
                }
                _nextSnapshotTick += TicksPerSnapshot;
            }
            if (_nextSnapshotTick <= serverTick)
                _nextSnapshotTick = serverTick - serverTick % TicksPerSnapshot + TicksPerSnapshot;

            handler.OnStatus(LinkStatus.Mock, "tick " + serverTick);
            while (_lines.Count > 0)
            {
                if (!WireCodec.TryDispatch(_lines.Dequeue(), handler, out var err))
                    handler.OnStatus(LinkStatus.ProtocolError, err);
            }
        }

        public void SendInput(InputMsg msg)
        {
            _move = msg.Move;                 // held direction; "none" on release
            _interact |= msg.Interact;        // consumed on the next simulated snapshot
        }

        public void SendChat(string text) => _lines.Enqueue(WireCodec.EncodeChat(LocalClientId, text));

        public void Dispose() => _clock.Stop();

        // ------------------------------------------------------------------ demo rules (mock-only)

        void Simulate(uint tick)
        {
            if (_won)
            {
                _interact = false;
                if (tick < _resetAt) return;
                _won = false;
                _leverOn = false;
                _px = SpawnX; _py = SpawnY;
                Say("round reset. again?");
                return;
            }

            if (_interact)
            {
                _interact = false;
                if (Math.Abs(_px - LeverX) <= 1 && Math.Abs(_py - LeverY) <= 1)
                {
                    _leverOn = !_leverOn;
                    Say(_leverOn ? "*clunk* somewhere south, stone grinds open." : "*clunk* the door slides shut.");
                }
            }

            if (_move == MoveDir.None || tick < _nextStep) return;

            int nx = _px, ny = _py;
            switch (_move)
            {
                case MoveDir.N: ny++; break;
                case MoveDir.S: ny--; break;
                case MoveDir.E: nx++; break;
                case MoveDir.W: nx--; break;
            }
            bool inBounds = nx >= 0 && ny >= 0 && nx < Grid && ny < Grid;
            bool doorBlocks = nx == DoorX && ny == DoorY && !_leverOn;
            if (!inBounds || doorBlocks) return;

            _px = nx; _py = ny;
            _nextStep = tick + StepTicks;

            if (IsHazard(_px, _py))
            {
                Say(_px == TrapX && _py == TrapY
                    ? "something unseen bites. back to camp. (a sightstone might have shown it...)"
                    : "ouch. back to camp.");
                _px = SpawnX; _py = SpawnY;
            }
            else if (_px == GoalX && _py == GoalY)
            {
                _won = true;
                _resetAt = tick + ResetDelayTicks;
                Say("p0 reached the goal. room cleared!");
            }
        }

        static bool IsHazard(int x, int y)
        {
            if (x == TrapX && y == TrapY) return true;
            foreach (var r in Ring) if (r.x == x && r.y == y) return true;
            return false;
        }

        void Say(string text) => _lines.Enqueue(WireCodec.EncodeChat(ServerSender, text));

        // ------------------------------------------------------------------ snapshot

        Snapshot BuildSnapshot(uint tick)
        {
            int step = (int)(tick / 30);
            var (p1x, p1y) = Loop(step, 10, 12, 5, 3);   // teammate 1 walks a loop over the plate
            int pace = step % 10;
            int p2x = 12 + (pace < 5 ? pace : 10 - pace); // teammate 2 paces carrying the key

            bool platePressed = (_px == PlateX && _py == PlateY) || (p1x == PlateX && p1y == PlateY);

            var e = new List<EntityState>
            {
                /* 0 */ new EntityState(_px, _py, EntityKind.Player, 0, -1),
                /* 1 */ new EntityState(p1x, p1y, EntityKind.Player, 0, -1),
                /* 2 */ new EntityState(p2x, 19, EntityKind.Player, 0, -1),
                /* 3 */ new EntityState(DoorX, DoorY, EntityKind.Door, (byte)(_leverOn ? 1 : 0), -1),
                /* 4 */ new EntityState(LeverX, LeverY, EntityKind.Lever, (byte)(_leverOn ? 1 : 0), -1),
                /* 5 */ new EntityState(PlateX, PlateY, EntityKind.Plate, (byte)(platePressed ? 1 : 0), -1),
                /* 6 */ new EntityState(p2x, 19, EntityKind.Key, 0, 2),
                /* 7 */ new EntityState(TrapX, TrapY, EntityKind.Hazard, 0, -1),
                /* 8 */ new EntityState(GoalX, GoalY, EntityKind.Goal, (byte)(_won ? 1 : 0), -1),
                /* 9 */ new EntityState(9, 20, EntityKind.Sightstone, 0, -1),
            };
            foreach (var r in Ring) e.Add(new EntityState(r.x, r.y, EntityKind.Hazard, 0, -1));
            return new Snapshot { Tick = tick, Entities = e.ToArray() };
        }

        /// <summary>Walk the perimeter of a w x h rectangle anchored at (x0, y0).</summary>
        static (int, int) Loop(int step, int x0, int y0, int w, int h)
        {
            int perim = 2 * (w + h);
            int s = step % perim;
            if (s < w) return (x0 + s, y0);
            s -= w;
            if (s < h) return (x0 + w, y0 + s);
            s -= h;
            if (s < w) return (x0 + w - s, y0 + h);
            s -= w;
            return (x0, y0 + h - s);
        }

        static Overlay OverlayFor(byte clientId, uint tick)
        {
            if (clientId != 0) return Overlay.Empty;
            return new Overlay
            {
                Tick = tick,
                Hidden = new[] { IdTrap },                                                // client 0 can't see the trap
                Substituted = new[] { new Substitution(IdPlate, (int)EntityKind.Goal) },  // plate masquerades as a goal
                Phantoms = new[] { new Phantom(21, 20, (int)EntityKind.Key) },            // a key that isn't there
            };
        }
    }
}
