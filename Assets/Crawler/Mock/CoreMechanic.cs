using System;
using System.Collections.Generic;
using Crawler.Protocol;

namespace Crawler.Mock
{
    /// <summary>
    /// The rules every level has: doors, levers, plates, keys and the summit gate, pits
    /// (minor failures), the sightstone with its hidden pits and false keys, and the goal.
    /// </summary>
    sealed class CoreMechanic : IMechanic
    {
        readonly HashSet<int> _leversOn = new HashSet<int>();
        readonly List<int> _hiddenPits = new List<int>();
        readonly List<Phantom> _falseKeys = new List<Phantom>();
        readonly Dictionary<int, List<int>> _doorLinks = new Dictionary<int, List<int>>(); // door id -> source ids
        int _totalKeys;
        bool _revealed, _gateAnnounced;

        public void OnLoad(MockWorld w)
        {
            var byCell = new Dictionary<(int, int), int>();
            for (int id = w.FirstEntity; id < w.Ents.Count; id++)
            {
                var e = w.Ents[id];
                byCell[(e.X, e.Y)] = id;
                if (e.Kind == EntityKind.Key) _totalKeys++;
                if (e.Kind == EntityKind.Hazard && e.Glyph == 'h') _hiddenPits.Add(id);
            }

            for (int y = w.Level.Height - 1; y >= 0; y--)
            for (int x = 0; x < w.Level.Width; x++)
                if (w.Level.At(x, y) == 'k') _falseKeys.Add(new Phantom(x, y, (int)EntityKind.Key));

            foreach (var link in w.Level.Links)
            {
                if (!byCell.TryGetValue((link.ToX, link.ToY), out int door) || w.Ents[door].Kind != EntityKind.Door)
                    throw new FormatException($"link to ({link.ToX},{link.ToY}) does not point at a door");
                if (!byCell.TryGetValue((link.FromX, link.FromY), out int src)
                    || (w.Ents[src].Kind != EntityKind.Lever && w.Ents[src].Kind != EntityKind.Plate))
                    throw new FormatException($"link from ({link.FromX},{link.FromY}) is not a lever or plate");
                if (!_doorLinks.TryGetValue(door, out var list)) _doorLinks[door] = list = new List<int>();
                list.Add(src);
            }
        }

        public void Reset(MockWorld w)
        {
            _leversOn.Clear();
            _revealed = _gateAnnounced = false;
        }

        public bool Blocks(MockWorld w, int x, int y)
        {
            for (int id = w.FirstEntity; id < w.Ents.Count; id++)
            {
                var e = w.Ents[id];
                if (e.Kind == EntityKind.Door && e.X == x && e.Y == y && !DoorOpen(w, id)) return true;
            }
            return false;
        }

        public void OnEnter(MockWorld w, int seat, uint tick)
        {
            var (x, y) = w.Pos[seat];
            for (int id = w.FirstEntity; id < w.Ents.Count; id++)
            {
                var e = w.Ents[id];
                if (e.X != x || e.Y != y) continue;

                switch (e.Kind)
                {
                    case EntityKind.Hazard:
                        w.Say(e.Glyph == 'h' && !_revealed
                            ? "something unseen bites. back to the room's entrance. (a sightstone might help.)"
                            : "a fall. back to the room's entrance.");
                        w.MinorFailure(seat, x, y);
                        if (CarriedCount(w) < _totalKeys) _gateAnnounced = false;
                        return;

                    case EntityKind.Key:
                        if (w.Carried.ContainsKey(id)) break;
                        w.Carried[id] = seat;
                        w.Say($"key {CarriedCount(w)} of {_totalKeys}.");
                        if (CarriedCount(w) >= _totalKeys && !_gateAnnounced)
                        {
                            _gateAnnounced = true;
                            w.Say(_totalKeys == 1 ? "the summit gate answers the key." : $"the summit gate answers the {Number(_totalKeys)} keys.");
                        }
                        break;

                }
            }

            CheckSummit(w, seat, tick);
        }

        /// <summary>
        /// The pair wins together: one leopard on the summit marker and the other anywhere in
        /// the summit's room (or within two cells of it, if the level gives it no room).
        /// Checked after every step, so it fires whichever leopard arrives second.
        /// </summary>
        void CheckSummit(MockWorld w, int seat, uint tick)
        {
            if (w.Won) return;
            for (int id = w.FirstEntity; id < w.Ents.Count; id++)
            {
                var g = w.Ents[id];
                if (g.Kind != EntityKind.Goal) continue;
                for (int s = 0; s < MockWorld.Seats; s++)
                {
                    if (w.Pos[s].x != g.X || w.Pos[s].y != g.Y) continue;
                    var other = w.Pos[1 - s];
                    bool near = g.Region >= 0
                        ? w.Level.RegionAt(other.x, other.y) == g.Region
                        : Math.Abs(other.x - g.X) <= 2 && Math.Abs(other.y - g.Y) <= 2;
                    if (!near)
                    {
                        if (s == seat) w.Say("the summit, but alone. your partner needs to be here too.");
                        return;
                    }
                    w.Won = true;
                    w.Say("the summit, together!");
                    w.Out.Enqueue(WireCodec.EncodeRound(new RoundMsg { Tick = tick, State = RoundState.Won }));
                    return;
                }
            }
        }

        public bool OnInteract(MockWorld w, int seat, uint tick)
        {
            var (px, py) = w.Pos[seat];
            for (int id = w.FirstEntity; id < w.Ents.Count; id++)
            {
                var e = w.Ents[id];
                if (Math.Abs(e.X - px) > 1 || Math.Abs(e.Y - py) > 1) continue;
                if (e.Kind == EntityKind.Lever)
                {
                    bool on = _leversOn.Add(id);
                    if (!on) _leversOn.Remove(id);
                    w.Say(on ? "*clunk*. stone grinds open nearby." : "*clunk*. a door slides shut.");
                    return true;
                }
                if (e.Kind == EntityKind.Sightstone && !_revealed)
                {
                    _revealed = true;
                    w.Say("the sightstone hums. hidden pits show their teeth, and any false key fades.");
                    return true;
                }
            }
            return false;
        }

        public void Write(MockWorld w, EntityState[] arr)
        {
            for (int id = w.FirstEntity; id < w.Ents.Count; id++)
            {
                var e = w.Ents[id];
                switch (e.Kind)
                {
                    case EntityKind.Door:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, (byte)(DoorOpen(w, id) ? 1 : 0), -1); break;
                    case EntityKind.Lever:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, (byte)(_leversOn.Contains(id) ? 1 : 0), -1); break;
                    case EntityKind.Plate:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, (byte)(w.SeatAt(e.X, e.Y) ? 1 : 0), -1); break;
                    case EntityKind.Key:
                        if (w.Carried.TryGetValue(id, out int seat))
                            arr[id] = new EntityState(w.Pos[seat].x, w.Pos[seat].y, e.Kind, 0, (sbyte)seat);
                        else
                            arr[id] = new EntityState(e.X, e.Y, e.Kind, 0, -1);
                        break;
                    case EntityKind.Goal:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, (byte)(w.Won ? 1 : 0), -1); break;
                    case EntityKind.Sightstone:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, (byte)(_revealed ? 1 : 0), -1); break;
                    case EntityKind.Hazard:
                        arr[id] = new EntityState(e.X, e.Y, e.Kind, 0, -1); break;
                }
            }
        }

        public void Perceive(MockWorld w, int seat, OverlayBuilder overlay)
        {
            if (_revealed) return;
            overlay.Hidden.AddRange(_hiddenPits);
            overlay.Phantoms.AddRange(_falseKeys);
        }

        // ------------------------------------------------------------------ rules

        bool DoorOpen(MockWorld w, int id)
        {
            var door = w.Ents[id];
            if (door.Glyph == 'F') return _totalKeys > 0 && CarriedCount(w) >= _totalKeys;

            // An explicit link wins over the region rule.
            if (_doorLinks.TryGetValue(id, out var sources))
            {
                foreach (var src in sources)
                    if (SourceActive(w, src)) return true;
                return false;
            }

            bool plateRule = w.RuleOf(door.Region) == "plate";
            for (int j = w.FirstEntity; j < w.Ents.Count; j++)
            {
                var e = w.Ents[j];
                if (e.Region != door.Region || IsLinkedSource(j)) continue;
                if (plateRule && e.Kind == EntityKind.Plate && SourceActive(w, j)) return true;
                if (!plateRule && e.Kind == EntityKind.Lever && SourceActive(w, j)) return true;
            }
            return false;
        }

        /// <summary>A lever or plate that drives a linked door does not also open its region's doors.</summary>
        bool IsLinkedSource(int id)
        {
            foreach (var list in _doorLinks.Values)
                if (list.Contains(id)) return true;
            return false;
        }

        bool SourceActive(MockWorld w, int id)
        {
            var e = w.Ents[id];
            return e.Kind == EntityKind.Lever ? _leversOn.Contains(id) : w.SeatAt(e.X, e.Y);
        }

        static int CarriedCount(MockWorld w) => w.Carried.Count;

        static string Number(int n) => n == 2 ? "two" : n == 3 ? "three" : n == 4 ? "four" : n.ToString();
    }
}
