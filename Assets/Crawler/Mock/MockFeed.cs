using System;
using System.Collections.Generic;
using System.Diagnostics;
using Crawler.Protocol;

namespace Crawler.Mock
{
    /// <summary>
    /// Stand-in server. Simulates a level at 60 Hz and emits snapshots at 20 Hz as JSON
    /// lines through the same WireCodec the real connection uses.
    ///
    /// DEMO RULES LIVE HERE AND ONLY HERE. Movement, levers, plates, keys, the sightstone
    /// reveal, the summit gate, and the scripted teammates are a throwaway stand-in so the
    /// demo is playable before the Rust server exists. They are not a spec for the server's
    /// rules; the level file is the shared part. Nothing outside Crawler.Mock references this.
    /// </summary>
    public sealed class MockFeed : IServerLink
    {
        const int TickHz = 60;
        const int ReplicationHz = 20;
        const int TicksPerSnapshot = TickHz / ReplicationHz;
        const byte LocalClientId = 0;
        const byte ServerSender = 255;          // mock convention for server-authored chat
        const uint StepTicks = 9;               // local player: one cell per 150 ms while held
        const uint TeammateStepTicks = 12;
        const uint PatrolStepTicks = 30;
        const uint ResetDelayTicks = 360;       // 6 s on the win screen

        static readonly int[] Dx = { 1, -1, 0, 0 };
        static readonly int[] Dy = { 0, 0, 1, -1 };

        struct Ent
        {
            public EntityKind Kind;
            public char Glyph;
            public int X, Y, Region;
        }

        readonly LevelDef _lvl;
        readonly Stopwatch _clock = new Stopwatch();
        readonly Queue<string> _lines = new Queue<string>();
        readonly List<Ent> _ents = new List<Ent>();      // index = entity id
        readonly List<int> _hidden = new List<int>();    // hidden hazards (overlay) until revealed
        readonly List<Phantom> _phantoms = new List<Phantom>();
        readonly int _playerCount;
        readonly (int x, int y)[] _spawn;
        readonly (int x, int y)[] _pos;
        readonly int _totalKeys;
        readonly int _plateForMate = -1;

        readonly HashSet<int> _leversOn = new HashSet<int>();
        readonly HashSet<int> _carried = new HashSet<int>();

        uint _nextSnapshotTick;
        bool _started;
        MoveDir _move;
        bool _interact;
        uint _nextStep, _mateNextStep, _patrolNextStep, _resetAt;
        bool _revealed, _gateAnnounced, _won, _mateHelping, _patrolOut;

        public MockFeed(LevelDef level)
        {
            _lvl = level;

            // Players first, P then 1 then 2, so the Nth Player entity is client N.
            foreach (var glyph in new[] { 'P', '1', '2' })
                if (Find(glyph, out int px, out int py)) Add(EntityKind.Player, glyph, px, py);
            _playerCount = _ents.Count;
            _spawn = new (int, int)[_playerCount];
            _pos = new (int, int)[_playerCount];
            for (int i = 0; i < _playerCount; i++) _spawn[i] = (_ents[i].X, _ents[i].Y);

            for (int y = _lvl.Grid - 1; y >= 0; y--)
            for (int x = 0; x < _lvl.Grid; x++)
            {
                char c = _lvl.At(x, y);
                switch (c)
                {
                    case 'D':
                    case 'F': Add(EntityKind.Door, c, x, y); break;
                    case 'L': Add(EntityKind.Lever, c, x, y); break;
                    case 'B':
                        if (_plateForMate < 0 && RuleOf(_lvl.RegionAt(x, y)) == "plate") _plateForMate = _ents.Count;
                        Add(EntityKind.Plate, c, x, y);
                        break;
                    case 'K': Add(EntityKind.Key, c, x, y); _totalKeys++; break;
                    case 'H': Add(EntityKind.Hazard, c, x, y); break;
                    case 'h': _hidden.Add(_ents.Count); Add(EntityKind.Hazard, c, x, y); break;
                    case 'G': Add(EntityKind.Goal, c, x, y); break;
                    case 'S': Add(EntityKind.Sightstone, c, x, y); break;
                    case 'k': _phantoms.Add(new Phantom(x, y, (int)EntityKind.Key)); break;
                }
            }
        }

        public void Start(string playerName)
        {
            _started = true;
            _clock.Start();
            _lines.Enqueue(WireCodec.EncodeWelcome(new Welcome
            {
                ClientId = LocalClientId, Grid = _lvl.Grid, TickHz = TickHz, ReplicationHz = ReplicationHz,
            }));
            _lines.Enqueue(WireCodec.EncodeLevel(_lvl.Id));
            Reset(0);
            Say($"welcome to {_lvl.Name}, {playerName}. three keys wait in the west, north and east rooms. " +
                "bring them to the summit gate south of camp.");
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
            _move = msg.Move;              // held direction; "none" on release
            _interact |= msg.Interact;     // consumed on the next simulated snapshot
        }

        public void SendChat(string text) => _lines.Enqueue(WireCodec.EncodeChat(LocalClientId, text));

        public void Dispose() => _clock.Stop();

        // ------------------------------------------------------------------ demo rules (mock-only)

        void Reset(uint tick)
        {
            for (int i = 0; i < _playerCount; i++) _pos[i] = _spawn[i];
            _leversOn.Clear();
            _carried.Clear();
            _revealed = _gateAnnounced = _won = _mateHelping = _patrolOut = false;
            _move = MoveDir.None;
            _interact = false;
            _lines.Enqueue(WireCodec.EncodeOverlay(CurrentOverlay(tick)));
            _lines.Enqueue(WireCodec.EncodeRound(new RoundMsg { Tick = tick, State = RoundState.Playing }));
        }

        void Simulate(uint tick)
        {
            if (_won)
            {
                _interact = false;
                if (tick < _resetAt) return;
                Reset(tick);
                Say("the trail resets. again?");
                return;
            }

            StepTeammates(tick);
            if (_interact) { _interact = false; Interact(tick); }
            if (_move != MoveDir.None && tick >= _nextStep) TryMove(tick);
        }

        void Interact(uint tick)
        {
            var (px, py) = _pos[0];
            for (int id = _playerCount; id < _ents.Count; id++)
            {
                var e = _ents[id];
                if (Math.Abs(e.X - px) > 1 || Math.Abs(e.Y - py) > 1) continue;
                if (e.Kind == EntityKind.Lever)
                {
                    bool on = _leversOn.Add(id);
                    if (!on) _leversOn.Remove(id);
                    Say(on ? "*clunk*. stone grinds open nearby." : "*clunk*. the door slides shut.");
                    return;
                }
                if (e.Kind == EntityKind.Sightstone && !_revealed)
                {
                    _revealed = true;
                    _lines.Enqueue(WireCodec.EncodeOverlay(CurrentOverlay(tick)));
                    Say("the sightstone hums. the scree shows its teeth, and the false key fades.");
                    return;
                }
            }
        }

        void TryMove(uint tick)
        {
            var (x, y) = _pos[0];
            switch (_move)
            {
                case MoveDir.N: y++; break;
                case MoveDir.S: y--; break;
                case MoveDir.E: x++; break;
                case MoveDir.W: x--; break;
            }
            if (!Walkable(x, y)) return;
            _pos[0] = (x, y);
            _nextStep = tick + StepTicks;
            OnEnter(tick);
        }

        void OnEnter(uint tick)
        {
            var (x, y) = _pos[0];
            for (int id = _playerCount; id < _ents.Count; id++)
            {
                var e = _ents[id];
                if (e.X != x || e.Y != y) continue;

                if (e.Kind == EntityKind.Hazard)
                {
                    Say(e.Glyph == 'h' && !_revealed
                        ? "something unseen bites. back to camp. (the sightstone by the door might help.)"
                        : "ouch. back to camp.");
                    _pos[0] = _spawn[0];
                    // A fall costs the run: carried keys drop back where they were found,
                    // otherwise the pits are free to walk into.
                    if (_carried.Count > 0)
                    {
                        Say(_carried.Count == 1 ? "the key slips loose and lands back where it lay."
                                                : $"all {_carried.Count} keys slip loose and land back where they lay.");
                        _carried.Clear();
                        _gateAnnounced = false;
                    }
                    return;
                }
                if (e.Kind == EntityKind.Key && _carried.Add(id))
                {
                    Say($"key {_carried.Count} of {_totalKeys}.");
                    if (_carried.Count >= _totalKeys && !_gateAnnounced)
                    {
                        _gateAnnounced = true;
                        Say("far below camp, the summit gate answers the three keys.");
                    }
                }
                if (e.Kind == EntityKind.Goal)
                {
                    _won = true;
                    _resetAt = tick + ResetDelayTicks;
                    Say("p0 reached the summit!");
                    _lines.Enqueue(WireCodec.EncodeRound(new RoundMsg { Tick = tick, State = RoundState.Won }));
                    return;
                }
            }
        }

        void StepTeammates(uint tick)
        {
            // Teammate 1 heads to the stone plate the first time you enter a plate room.
            if (_playerCount > 1 && _plateForMate >= 0)
            {
                if (!_mateHelping && RuleOf(_lvl.RegionAt(_pos[0].x, _pos[0].y)) == "plate")
                {
                    _mateHelping = true;
                    _lines.Enqueue(WireCodec.EncodeChat(1, "i'll hold the stone plate. go on through!"));
                }
                if (_mateHelping && tick >= _mateNextStep)
                {
                    var plate = _ents[_plateForMate];
                    if (BfsNext(_pos[1], (plate.X, plate.Y), out var next)) _pos[1] = next;
                    _mateNextStep = tick + TeammateStepTicks;
                }
            }

            // Teammate 2 paces the camp.
            if (_playerCount > 2 && tick >= _patrolNextStep)
            {
                var target = _patrolOut ? (_spawn[2].x, _spawn[2].y + 5) : _spawn[2];
                if (BfsNext(_pos[2], target, out var next)) _pos[2] = next;
                else _patrolOut = !_patrolOut;
                _patrolNextStep = tick + PatrolStepTicks;
            }
        }

        bool Walkable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _lvl.Grid || y >= _lvl.Grid) return false;
            if (LevelDef.IsSolidTile(_lvl.At(x, y))) return false;
            for (int id = _playerCount; id < _ents.Count; id++)
            {
                var e = _ents[id];
                if (e.Kind == EntityKind.Door && e.X == x && e.Y == y && !DoorOpen(id)) return false;
            }
            return true;
        }

        bool HazardAt(int x, int y)
        {
            for (int id = _playerCount; id < _ents.Count; id++)
                if (_ents[id].Kind == EntityKind.Hazard && _ents[id].X == x && _ents[id].Y == y) return true;
            return false;
        }

        bool DoorOpen(int id)
        {
            var door = _ents[id];
            if (door.Glyph == 'F') return _totalKeys > 0 && _carried.Count >= _totalKeys;

            bool plateRule = RuleOf(door.Region) == "plate";
            for (int j = _playerCount; j < _ents.Count; j++)
            {
                var e = _ents[j];
                if (e.Region != door.Region) continue;
                if (plateRule && e.Kind == EntityKind.Plate && PlatePressed(j)) return true;
                if (!plateRule && e.Kind == EntityKind.Lever && _leversOn.Contains(j)) return true;
            }
            return false;
        }

        bool PlatePressed(int id)
        {
            var e = _ents[id];
            for (int i = 0; i < _playerCount; i++)
                if (_pos[i].x == e.X && _pos[i].y == e.Y) return true;
            return false;
        }

        /// <summary>Next step on a shortest path for a scripted teammate (avoids hazards and closed doors).</summary>
        bool BfsNext((int x, int y) from, (int x, int y) to, out (int x, int y) next)
        {
            next = from;
            if (from == to) return false;
            int g = _lvl.Grid;
            var prev = new int[g * g];
            for (int i = 0; i < prev.Length; i++) prev[i] = -1;
            int s = from.y * g + from.x, t = to.y * g + to.x;
            var q = new Queue<int>();
            q.Enqueue(s);
            prev[s] = s;
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                if (c == t) break;
                int cx = c % g, cy = c / g;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], ny = cy + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= g || ny >= g) continue;
                    int n = ny * g + nx;
                    if (prev[n] != -1) continue;
                    if (n != t && (!Walkable(nx, ny) || HazardAt(nx, ny))) continue;
                    prev[n] = c;
                    q.Enqueue(n);
                }
            }
            if (prev[t] == -1) return false;
            int cur = t;
            while (prev[cur] != s) cur = prev[cur];
            next = (cur % g, cur / g);
            return true;
        }

        // ------------------------------------------------------------------ snapshot / overlay

        Snapshot BuildSnapshot(uint tick)
        {
            var arr = new EntityState[_ents.Count];
            for (int i = 0; i < _playerCount; i++)
                arr[i] = new EntityState(_pos[i].x, _pos[i].y, EntityKind.Player, 0, -1);

            for (int id = _playerCount; id < _ents.Count; id++)
            {
                var e = _ents[id];
                switch (e.Kind)
                {
                    case EntityKind.Door:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, (byte)(DoorOpen(id) ? 1 : 0), -1); break;
                    case EntityKind.Lever:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, (byte)(_leversOn.Contains(id) ? 1 : 0), -1); break;
                    case EntityKind.Plate:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, (byte)(PlatePressed(id) ? 1 : 0), -1); break;
                    case EntityKind.Key:
                        arr[id] = _carried.Contains(id)
                            ? new EntityState(_pos[0].x, _pos[0].y, e.Kind, 0, 0)
                            : new EntityState(e.X, e.Y, e.Kind, 0, -1);
                        break;
                    case EntityKind.Goal:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, (byte)(_won ? 1 : 0), -1); break;
                    case EntityKind.Sightstone:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, (byte)(_revealed ? 1 : 0), -1); break;
                    default:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, 0, -1); break;
                }
            }
            return new Snapshot { Tick = tick, Entities = arr };
        }

        /// <summary>Client 0's perception: hidden hazards and a phantom key until the sightstone is used.</summary>
        Overlay CurrentOverlay(uint tick) => new Overlay
        {
            Tick = tick,
            Hidden = _revealed ? new int[0] : _hidden.ToArray(),
            Substituted = new Substitution[0],
            Phantoms = _revealed ? new Phantom[0] : _phantoms.ToArray(),
        };

        // ------------------------------------------------------------------ helpers

        void Add(EntityKind kind, char glyph, int x, int y) =>
            _ents.Add(new Ent { Kind = kind, Glyph = glyph, X = x, Y = y, Region = _lvl.RegionAt(x, y) });

        bool Find(char glyph, out int fx, out int fy)
        {
            for (int y = _lvl.Grid - 1; y >= 0; y--)
            for (int x = 0; x < _lvl.Grid; x++)
                if (_lvl.At(x, y) == glyph) { fx = x; fy = y; return true; }
            fx = fy = 0;
            return false;
        }

        string RuleOf(int region) =>
            region >= 0 && region < _lvl.Regions.Count ? _lvl.Regions[region].Rule : "";

        void Say(string text) => _lines.Enqueue(WireCodec.EncodeChat(ServerSender, text));
    }
}
