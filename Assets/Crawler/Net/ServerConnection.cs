using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Crawler.Protocol;

namespace Crawler.Net
{
    /// <summary>
    /// Raw TCP, JSON Lines. A background thread connects and reads lines into a queue;
    /// the main thread decodes and dispatches them in Pump(). Writes happen on the
    /// calling (main) thread under a lock. Nothing here touches the engine.
    /// </summary>
    public sealed class ServerConnection : IServerLink
    {
        const int ConnectTimeoutMs = 3000;

        readonly string _host;
        readonly int _port;
        readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<(LinkStatus, string)> _status = new ConcurrentQueue<(LinkStatus, string)>();
        readonly object _writeLock = new object();

        TcpClient _client;
        StreamWriter _writer;
        Thread _reader;
        volatile bool _running;
        volatile bool _connected;
        volatile bool _everConnected;
        byte _clientId;
        bool _warnedNotConnected;

        public ServerConnection(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void Start(string playerName)
        {
            if (_reader != null) return;
            _running = true;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "ServerConnection.Reader" };
            _reader.Start(playerName);
        }

        void ReadLoop(object state)
        {
            var name = (string)state;
            Post(LinkStatus.Connecting, $"{_host}:{_port}");
            try
            {
                _client = new TcpClient { NoDelay = true };
                var connect = _client.ConnectAsync(_host, _port);
                if (!connect.Wait(ConnectTimeoutMs))
                    throw new TimeoutException($"no answer from {_host}:{_port} after {ConnectTimeoutMs} ms");

                var stream = _client.GetStream();
                lock (_writeLock)
                {
                    _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                    _writer.WriteLine(WireCodec.EncodeHello(name));
                }
                _connected = true;
                _everConnected = true;
                Post(LinkStatus.Connected, $"{_host}:{_port}");

                using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                {
                    string line;
                    while (_running && (line = reader.ReadLine()) != null)
                        if (line.Length > 0) _inbox.Enqueue(line);
                }
                if (_running) Post(LinkStatus.Disconnected, "server closed the connection");
            }
            catch (Exception e)
            {
                if (_running) Post(LinkStatus.Failed, Clean(Unwrap(e).Message));
            }
            finally
            {
                _connected = false;
            }
        }

        public void Pump(IInboundHandler handler)
        {
            while (_status.TryDequeue(out var s)) handler.OnStatus(s.Item1, s.Item2);
            if (_inbox.IsEmpty) return;
            var sniffer = new IdSniffer(this, handler);
            while (_inbox.TryDequeue(out var line))
            {
                if (!WireCodec.TryDispatch(line, sniffer, out var err))
                    handler.OnStatus(LinkStatus.ProtocolError, err);
            }
        }

        public void SendInput(InputMsg msg) => SendLine(WireCodec.EncodeInput(msg));

        public void SendChat(string text) => SendLine(WireCodec.EncodeChat(_clientId, text));

        void SendLine(string line)
        {
            if (!_connected)
            {
                // Still dialing or never reached the server: drop silently; the reader
                // thread reports the real outcome. Only a lost live link is worth a warning.
                if (_everConnected && !_warnedNotConnected) Post(LinkStatus.Disconnected, "link lost; dropping outbound messages");
                _warnedNotConnected = true;
                return;
            }
            try
            {
                lock (_writeLock) _writer.WriteLine(line);
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is SocketException)
            {
                _connected = false;
                Post(LinkStatus.Disconnected, e.Message);
            }
        }

        public void Dispose()
        {
            _running = false;
            _connected = false;
            try { _client?.Close(); } catch { /* shutting down */ }
            _reader?.Join(500);
            _reader = null;
        }

        void Post(LinkStatus s, string detail) => _status.Enqueue((s, detail));

        /// <summary>Windows socket messages arrive with a trailing CRLF and NUL padding.</summary>
        static string Clean(string s)
        {
            int nul = s.IndexOf('\0');
            if (nul >= 0) s = s.Substring(0, nul);
            return s.Trim();
        }

        static Exception Unwrap(Exception e)
        {
            while (e is AggregateException && e.InnerException != null) e = e.InnerException;
            return e;
        }

        /// <summary>Records our client id from welcome so outbound chat carries it; forwards everything.</summary>
        sealed class IdSniffer : IInboundHandler
        {
            readonly ServerConnection _owner;
            readonly IInboundHandler _inner;
            public IdSniffer(ServerConnection owner, IInboundHandler inner) { _owner = owner; _inner = inner; }
            public void OnWelcome(Welcome m) { _owner._clientId = m.ClientId; _inner.OnWelcome(m); }
            public void OnSnapshot(Snapshot m) => _inner.OnSnapshot(m);
            public void OnOverlay(Overlay m) => _inner.OnOverlay(m);
            public void OnChat(ChatMsg m) => _inner.OnChat(m);
            public void OnLevel(LevelMsg m) => _inner.OnLevel(m);
            public void OnRound(RoundMsg m) => _inner.OnRound(m);
            public void OnStatus(LinkStatus s, string d) => _inner.OnStatus(s, d);
        }
    }
}
