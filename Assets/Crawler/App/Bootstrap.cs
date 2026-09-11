using Crawler.Input;
using Crawler.Mock;
using Crawler.Net;
using Crawler.Protocol;
using Crawler.Render;
using Crawler.UI;
using UnityEngine;

namespace Crawler.App
{
    /// <summary>
    /// Shows the title card, picks the link (mock or TCP) once the player sets out, pumps it
    /// each frame on the main thread, and routes decoded messages to the view layers. This
    /// is the only place that knows about every assembly.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour, IInboundHandler
    {
        [Header("Link")]
        [SerializeField] bool useMockFeed = true;
        [SerializeField] string host = "127.0.0.1";
        [SerializeField] int port = 7777;
        [SerializeField] string playerName = "snep";
        [SerializeField] string mockLevelId = "hollow";

        [Header("Presentation")]
        [SerializeField] string gameTitle = "HIGH HOLLOW";
        [SerializeField] string tagline = "a co-op puzzle crawl";

        [Header("Scene")]
        [SerializeField] WorldView world;
        [SerializeField] GridFloor floor;
        [SerializeField] CameraFramer framer;
        [SerializeField] InputSender input;
        [SerializeField] ChatPanel chat;
        [SerializeField] StatusBadge status;

        IServerLink _link;
        int _localClient = -1;
        LinkStatus _lastStatus = LinkStatus.Idle;
        string _lastDetail;

        LevelDef _level;
        int _region = -1;
        PosterScreen _poster;
        RegionBanner _banner;
        float _roundStart;
        bool _won;

        void Start()
        {
            ProcCursor.Apply();

            var canvas = chat.transform.parent;
            Make<Legend>("Legend", canvas);
            _banner = Make<RegionBanner>("RegionBanner", canvas);
            _poster = Make<PosterScreen>("PosterScreen", canvas); // created last so it draws over the HUD

            input.TickSource = () => world.LastTick;
            input.Suppressed = () => chat.IsTyping || PosterBlocking();
            chat.Blocked = PosterBlocking;
            chat.OnSubmit = text => _link?.SendChat(text);
            framer.SetWorld(world);
            framer.RegionSource = CurrentRegionRect;
            status.Set(LinkStatus.Idle, "press Enter to begin");

            _poster.Show(Typeset.Spaced(gameTitle), tagline, "press  ENTER  to set out", true, StartLink, instant: true);
        }

        bool PosterBlocking() => _poster.IsShowing || _poster.LastDismissFrame == Time.frameCount;

        void StartLink()
        {
            if (_link != null) return;
            if (useMockFeed)
            {
                var lvl = LevelStore.Load(mockLevelId, out var err);
                if (lvl == null)
                {
                    Debug.LogError("[Level] " + err);
                    chat.AddSystem("could not load the mock level: " + err);
                    return;
                }
                _link = new MockFeed(lvl);
            }
            else
            {
                _link = new ServerConnection(host, port);
            }
            input.Sink = msg => _link.SendInput(msg);
            chat.AddSystem(useMockFeed ? "mock feed; no server needed" : $"dialing {host}:{port}");
            _link.Start(playerName);
        }

        void Update()
        {
            _link?.Pump(this);
            TrackRegion();
        }

        void OnDestroy()
        {
            _link?.Dispose();
            _link = null;
        }

        /// <summary>Room titles and camera framing follow the local player's cell. Perception only.</summary>
        void TrackRegion()
        {
            if (_level == null) return;
            var cell = world.LocalCell;
            if (!cell.HasValue) return;
            int r = _level.RegionAt(cell.Value.x, cell.Value.y);
            if (r < 0 || r == _region) return; // corridors keep the last room
            _region = r;
            var reg = _level.Regions[r];
            _banner.Show(reg.Name, reg.Subtitle);
        }

        RectInt? CurrentRegionRect()
        {
            if (_level == null || _region < 0) return null;
            var g = _level.Regions[_region];
            return new RectInt(g.X, g.Y, g.W, g.H);
        }

        static T Make<T>(string name, Transform parent) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        // ------------------------------------------------------------------ inbound

        public void OnWelcome(Welcome m)
        {
            _localClient = m.ClientId;
            world.SetLocalClient(m.ClientId);
            GridSpace.Size = m.Grid;
            if (_level == null) floor.Build(m.Grid);
            input.SetReplicationHz(m.ReplicationHz);
            chat.AddSystem($"welcome: client {m.ClientId}, grid {m.Grid}, {m.TickHz} Hz sim / {m.ReplicationHz} Hz replication");
        }

        public void OnLevel(LevelMsg m)
        {
            var lvl = LevelStore.Load(m.Id, out var err);
            if (lvl == null)
            {
                OnStatus(LinkStatus.ProtocolError, err);
                return;
            }
            _level = lvl;
            _region = -1;
            GridSpace.Size = lvl.Grid;
            floor.BuildLevel(lvl);
        }

        public void OnRound(RoundMsg m)
        {
            if (m.State == RoundState.Won)
            {
                _won = true;
                int secs = Mathf.Max(0, (int)(Time.time - _roundStart));
                _poster.Show(Typeset.Spaced("SUMMIT REACHED"),
                    $"the hollow is yours  ·  {secs / 60}:{secs % 60:00}",
                    "the trail resets shortly", false);
            }
            else
            {
                if (_won) _poster.Hide();
                _won = false;
                _roundStart = Time.time;
                _region = -1; // re-announce the starting room
            }
        }

        public void OnSnapshot(Snapshot m) => world.ApplySnapshot(m);

        public void OnOverlay(Overlay m) => world.ApplyOverlay(m);

        public void OnChat(ChatMsg m)
        {
            bool me = m.From == _localClient;
            if (m.From == 255) chat.AddLine("server", m.Text, Palette.Ochre); // mock convention; confirm with Rust
            else chat.AddLine(me ? "you" : $"p{m.From}", m.Text, me ? Palette.Ember : Palette.Sand);
        }

        public void OnStatus(LinkStatus s, string detail)
        {
            if (s == LinkStatus.Mock) detail = $"tick {world.LastTick}";
            status.Set(s, detail);
            if (s == LinkStatus.Mock || (s == _lastStatus && detail == _lastDetail)) return;
            _lastStatus = s; _lastDetail = detail;

            if (s == LinkStatus.Failed || s == LinkStatus.Disconnected || s == LinkStatus.ProtocolError)
            {
                Debug.LogWarning($"[Link] {s}: {detail}");
                chat.AddSystem($"{s.ToString().ToLowerInvariant()}: {detail}");
            }
            else Debug.Log($"[Link] {s}: {detail}");
        }
    }
}
