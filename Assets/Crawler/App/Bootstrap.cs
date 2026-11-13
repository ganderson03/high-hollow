using Crawler.Input;
using Crawler.Mock;
using Crawler.Net;
using Crawler.Protocol;
using Crawler.Render;
using Crawler.UI;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [SerializeField] ArtStyle artStyle = ArtStyle.Poster;
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
        TrailDef _trail;
        int[] _seats = new int[0];
        int _activeSeat = -1;
        int _region = -1;
        float _roomBannerAfter; // the level's own banner gets a moment before room names take over
        PosterScreen _poster;
        RegionBanner _banner;
        float _roundStart;
        bool _won;
        bool _choosing; // the partner did not come back: B takes both leopards, Q leaves
        bool _autostart;

        void Start()
        {
            ApplyCommandLine();
            Art.Style = artStyle; // read by the view layers as they build
            ProcCursor.Apply();

            var canvas = chat.transform.parent;
            Make<Legend>("Legend", canvas);
            _banner = Make<RegionBanner>("RegionBanner", canvas);
            _poster = Make<PosterScreen>("PosterScreen", canvas); // created last so it draws over the HUD

            input.TickSource = () => world.LastTick;
            input.OnSwitch = SwitchLeopard;
            input.OnVote = () => _link?.SendVote(true);
            input.Suppressed = () => chat.IsTyping || PosterBlocking();
            chat.Blocked = PosterBlocking;
            chat.OnSubmit = text => _link?.SendChat(text);
            framer.SetWorld(world);
            framer.RegionSource = CurrentRegionRect;
            status.Set(LinkStatus.Idle, "press Enter to begin");

            _poster.Show(Typeset.Spaced(gameTitle), tagline, "press  ENTER  to set out", true, StartLink, instant: true);
            if (_autostart)
            {
                _poster.Hide();
                StartLink();
            }
        }

        /// <summary>
        /// Overrides for a built player, so a script can start several clients against one server
        /// without editing the scene (see play.ps1):
        ///   -server host[:port]   play against a server instead of the mock
        ///   -mock                 play against the built-in mock
        ///   -name NAME            the name sent in hello
        ///   -level ID             the level the mock starts on
        ///   -autostart            skip the title card
        ///   -window-x X -window-y Y   move the window (players only)
        /// </summary>
        void ApplyCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            int? wx = null, wy = null;
            for (int i = 1; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "-server" when next != null:
                        i++;
                        useMockFeed = false;
                        int colon = next.LastIndexOf(':');
                        if (colon > 0 && int.TryParse(next.Substring(colon + 1), out int p)) { host = next.Substring(0, colon); port = p; }
                        else host = next;
                        break;
                    case "-mock": useMockFeed = true; break;
                    case "-name" when next != null: playerName = next; i++; break;
                    case "-level" when next != null: mockLevelId = next; i++; break;
                    case "-autostart": _autostart = true; break;
                    case "-window-x" when int.TryParse(next, out int x): wx = x; i++; break;
                    case "-window-y" when int.TryParse(next, out int y): wy = y; i++; break;
                }
            }

            // Two windows on one machine: the one without focus must keep pumping the link.
            if (!useMockFeed) Application.runInBackground = true;

            if (!Application.isEditor && (wx.HasValue || wy.HasValue))
            {
                var at = Screen.mainWindowPosition;
                Screen.MoveMainWindowTo(Screen.mainWindowDisplayInfo, new Vector2Int(wx ?? at.x, wy ?? at.y));
            }
        }

        bool PosterBlocking() => _poster.IsShowing || _poster.LastDismissFrame == Time.frameCount;

        void StartLink()
        {
            if (_link != null) return;
            _trail = LevelStore.LoadTrail(out var trailErr);
            if (_trail == null) Debug.LogWarning("[Level] " + trailErr + " (levels will not chain)");

            if (useMockFeed)
            {
                var lvl = LevelStore.Load(mockLevelId, out var err);
                if (lvl == null)
                {
                    Debug.LogError("[Level] " + err);
                    chat.AddSystem("could not load the mock level: " + err);
                    return;
                }
                _link = new MockFeed(lvl, _trail, id => LevelStore.Load(id, out _));
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
            PollChoice();
        }

        /// <summary>While the server asks whether to go on alone: B takes both leopards, Q leaves the trail.</summary>
        void PollChoice()
        {
            var kb = Keyboard.current;
            if (!_choosing || _link == null || kb == null || chat.IsTyping) return;
            if (kb.bKey.wasPressedThisFrame) { _choosing = false; _link.SendChoice(ChoicePick.Both); }
            else if (kb.qKey.wasPressedThisFrame) { _choosing = false; _link.SendChoice(ChoicePick.Quit); }
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
            if (Time.time < _roomBannerAfter) return;
            var reg = _level.Regions[r];
            _banner.Show(reg.Name, reg.Subtitle);
        }

        /// <summary>Tab: steer the other leopard, if this client holds both. The server confirms with Seats.</summary>
        void SwitchLeopard()
        {
            if (_link == null || _seats.Length < 2) return;
            int i = System.Array.IndexOf(_seats, _activeSeat);
            _link.SendControl(_seats[(i + 1) % _seats.Length]);
        }

        /// <summary>"Foothills, 2 of 4", or the level name alone if the trail does not list it.</summary>
        string TrailPlace(string levelId)
        {
            if (_trail == null || !_trail.Locate(levelId, out var biome, out int index)) return "";
            return biome.IsFinale(index)
                ? $"{biome.Name}  ·  finale"
                : $"{biome.Name}  ·  {index + 1} of {biome.Levels.Count}";
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
            GridSpace.Width = m.Width;
            GridSpace.Height = m.Height;
            if (_level == null) floor.Build(Mathf.Max(m.Width, m.Height));
            input.SetReplicationHz(m.ReplicationHz);
            Debug.Log($"[Link] welcome: client {m.ClientId}, {m.Width}x{m.Height}, {m.TickHz} Hz sim / {m.ReplicationHz} Hz replication");
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
            GridSpace.Width = lvl.Width;
            GridSpace.Height = lvl.Height;
            floor.BuildLevel(lvl);
            _banner.Show(lvl.Name, TrailPlace(lvl.Id));
            _roomBannerAfter = Time.time + 3.5f;
        }

        public void OnRound(RoundMsg m)
        {
            // The server explains pauses and restarts in chat; the client adds only the keys to press.
            if (m.State == RoundState.Paused) return;
            if (m.State == RoundState.Choose)
            {
                _choosing = true;
                chat.AddSystem("your partner has not come back. B: take both leopards   Q: leave the trail");
                return;
            }
            _choosing = false;

            if (m.State == RoundState.Won)
            {
                _won = true;
                int secs = Mathf.Max(0, (int)(Time.time - _roundStart));
                string time = $"{secs / 60}:{secs % 60:00}";
                string id = _level != null ? _level.Id : "";
                string next = _trail?.Next(id);
                string nextName = next != null ? LevelStore.Load(next, out _)?.Name ?? next : null;
                _poster.Show(Typeset.Spaced("SUMMIT REACHED"),
                    $"{(_level != null ? _level.Name : "")}  ·  {time}",
                    nextName != null ? $"onward to {nextName}" : _trail != null ? "the trail is complete" : "the trail resets shortly",
                    false);
            }
            else
            {
                if (_won) _poster.Hide();
                _won = false;
                _roundStart = Time.time;
                _region = -1; // re-announce the starting room
            }
        }

        public void OnSeats(SeatsMsg m)
        {
            _seats = m.Yours ?? new int[0];
            _activeSeat = m.Active;
            world.SetSeats(_seats, _activeSeat);
            _region = -1; // re-announce the room the newly steered leopard is in
        }

        public void OnVote(VoteMsg m)
        {
            if (m.State == VoteState.Open && m.By != _localClient)
                chat.AddSystem("your partner wants to restart the level. press R to agree.");
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
