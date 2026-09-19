using System.Collections.Generic;
using Crawler.Protocol;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>
    /// Holds the latest snapshot and overlay and rebuilds the rendered set from them.
    /// A snapshot replaces the previous one wholesale. The overlay is layered on at
    /// render time and never written into the entity array.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        [SerializeField] Transform entityRoot;
        [SerializeField] Transform phantomRoot;

        readonly List<EntityView> _views = new List<EntityView>();
        readonly List<EntityView> _phantoms = new List<EntityView>();
        readonly HashSet<int> _hidden = new HashSet<int>();
        readonly Dictionary<int, int> _substituted = new Dictionary<int, int>();

        EntityState[] _entities = new EntityState[0];
        Overlay _overlay = Overlay.Empty;
        int[] _owned = new int[0];
        int _active = -1;

        /// <summary>
        /// Which leopards this client controls and which one it is steering, from the server's
        /// Seats message. Seat N is the Nth Player entity by id. Drives the rings and the camera.
        /// </summary>
        public void SetSeats(int[] owned, int active)
        {
            _owned = owned ?? new int[0];
            _active = active;
            Rebuild();
        }

        public uint LastTick { get; private set; }

        /// <summary>The steered leopard's authoritative cell from the latest snapshot (perception use only).</summary>
        public Vector2Int? LocalCell { get; private set; }
        public bool HasSnapshot { get; private set; }

        /// <summary>Visible (post-overlay) entity and phantom cells, for camera framing.</summary>
        public IReadOnlyList<EntityView> Views => _views;
        public IReadOnlyList<EntityView> PhantomViews => _phantoms;

        void Awake()
        {
            if (entityRoot == null) entityRoot = MakeChild("Entities");
            if (phantomRoot == null) phantomRoot = MakeChild("Phantoms");
        }

        public void ApplySnapshot(Snapshot s)
        {
            _entities = s.Entities; // wholesale replace; the decoded array is ours and never mutated
            LastTick = s.Tick;
            HasSnapshot = true;
            Rebuild();
        }

        public void ApplyOverlay(Overlay ov)
        {
            _overlay = ov ?? Overlay.Empty;
            _hidden.Clear();
            foreach (var id in _overlay.Hidden) _hidden.Add(id);
            _substituted.Clear();
            foreach (var sub in _overlay.Substituted) _substituted[sub.Id] = sub.Look;
            Rebuild();
        }

        void Rebuild()
        {
            // Authoritative layer: one view per entity id.
            Grow(_views, _entities.Length, entityRoot);
            LocalCell = null;
            int playerOrdinal = 0;
            for (int id = 0; id < _views.Count; id++)
            {
                var v = _views[id];
                if (id >= _entities.Length) { v.gameObject.SetActive(false); continue; }

                // Perception layer, applied last.
                var s = _entities[id];
                bool visible = !_hidden.Contains(id);
                int look = _substituted.TryGetValue(id, out var sub) ? sub : Looks.ForKind(s.Kind);
                v.Bind(id, s, look, visible);
                int marker = 0;
                if (s.Kind == EntityKind.Player)
                {
                    if (playerOrdinal == _active) marker = 1;
                    else if (System.Array.IndexOf(_owned, playerOrdinal) >= 0) marker = 2;
                }
                v.SetMarker(marker);
                v.SetAccent(s.Kind == EntityKind.Player ? ScarfFor(playerOrdinal) : (Color?)null);
                if (marker == 1) LocalCell = new Vector2Int(s.X, s.Y);
                if (s.Kind == EntityKind.Player) playerOrdinal++;
            }

            // Phantoms: extra views with no server id.
            var ph = _overlay.Phantoms;
            Grow(_phantoms, ph.Length, phantomRoot);
            for (int i = 0; i < _phantoms.Count; i++)
            {
                if (i >= ph.Length) { _phantoms[i].gameObject.SetActive(false); continue; }
                var p = ph[i];
                _phantoms[i].Bind(-1, new EntityState(p.X, p.Y, EntityKind.Player, 0, -1), p.Look, true);
                _phantoms[i].SetMarker(0);
                _phantoms[i].SetAccent(null);
            }
        }

        /// <summary>Seat 0 wears sienna, seat 1 pine.</summary>
        static readonly Color[] Scarves = { Palette.Sienna, Palette.Pine };

        static Color ScarfFor(int playerOrdinal) => Scarves[playerOrdinal % Scarves.Length];

        static void Grow(List<EntityView> list, int n, Transform parent)
        {
            while (list.Count < n)
            {
                var go = new GameObject("EntityView");
                go.transform.SetParent(parent, false);
                go.SetActive(false);
                list.Add(go.AddComponent<EntityView>());
            }
        }

        Transform MakeChild(string n)
        {
            var t = new GameObject(n).transform;
            t.SetParent(transform, false);
            return t;
        }
    }
}
