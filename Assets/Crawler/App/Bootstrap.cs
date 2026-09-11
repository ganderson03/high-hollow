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
    /// Picks the link (mock or TCP), pumps it each frame on the main thread, and routes
    /// decoded messages to the view layers. This is the only place that knows about
    /// every assembly.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour, IInboundHandler
    {
        [Header("Link")]
        [SerializeField] bool useMockFeed = true;
        [SerializeField] string host = "127.0.0.1";
        [SerializeField] int port = 7777;
        [SerializeField] string playerName = "snep";

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

        void Start()
        {
            ProcCursor.Apply();

            var legend = new GameObject("Legend", typeof(RectTransform));
            legend.transform.SetParent(chat.transform.parent, false);
            legend.AddComponent<Legend>();

            input.TickSource = () => world.LastTick;
            input.Suppressed = () => chat.IsTyping;
            chat.OnSubmit = text => _link.SendChat(text);
            framer.SetWorld(world);

            _link = useMockFeed ? new MockFeed() : (IServerLink)new ServerConnection(host, port);
            input.Sink = msg => _link.SendInput(msg);
            chat.AddSystem(useMockFeed ? "mock feed; no server needed" : $"dialing {host}:{port}");
            _link.Start(playerName);
        }

        void Update() => _link?.Pump(this);

        void OnDestroy()
        {
            _link?.Dispose();
            _link = null;
        }

        // ------------------------------------------------------------------ inbound

        public void OnWelcome(Welcome m)
        {
            _localClient = m.ClientId;
            world.SetLocalClient(m.ClientId);
            GridSpace.Size = m.Grid;
            floor.Build(m.Grid);
            input.SetReplicationHz(m.ReplicationHz);
            chat.AddSystem($"welcome: client {m.ClientId}, grid {m.Grid}, {m.TickHz} Hz sim / {m.ReplicationHz} Hz replication");
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
