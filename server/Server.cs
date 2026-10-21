using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Crawler.Mock;
using Crawler.Protocol;

namespace Hollowline
{
    /// <summary>
    /// The authoritative loop. One thread owns the simulation and is the only thing that
    /// touches it; sockets only move bytes. Clients send requests into a queue, the tick
    /// thread drains it, steps the world and writes snapshots out.
    ///
    /// Transport is TCP with JSON Lines, the same format ServerConnection already speaks.
    ///
    /// NOT YET: every client is handed both seats, exactly as the in-client mock does, so a
    /// second client steers the same pair rather than getting one each. Splitting seats per
    /// client, and filtering each client's view of hidden pits and false keys, is the next
    /// piece of work and the reason this one stops here.
    /// </summary>
    sealed class Server : IClientHandler
    {
        const int TickHz = 60;
        const int ReplicationHz = 20;
        const int TicksPerSnapshot = TickHz / ReplicationHz;
        const uint WinHoldTicks = 360;              // 6 s on the summit card before the next level

        readonly TrailDef _trail;
        readonly Func<string, LevelDef> _load;
        readonly bool _quiet;
        readonly Stopwatch _clock = new Stopwatch();
        readonly ConcurrentQueue<Inbound> _inbox = new ConcurrentQueue<Inbound>();
        readonly List<Session> _sessions = new List<Session>();
        readonly object _sessionLock = new object();

        MockSim _sim;
        uint _tick, _nextSnapshotTick, _advanceAt;
        bool _trailComplete;
        int _nextClientId;

        public Server(LevelDef first, TrailDef trail, Func<string, LevelDef> load, bool quiet)
        {
            _sim = new MockSim(first);
            _trail = trail;
            _load = load;
            _quiet = quiet;
        }

        struct Inbound
        {
            public Session From;
            public string Line;
        }

        // ------------------------------------------------------------------ lifetime

        public void Run(int port)
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Log($"listening on {port}, level '{_sim.World.Level.Id}', {TickHz} Hz tick, {ReplicationHz} Hz snapshots");

            var accept = new Thread(() => AcceptLoop(listener)) { IsBackground = true, Name = "accept" };
            accept.Start();

            _clock.Start();
            _sim.Restart(0);
            DrainSim();

            var spin = new SpinWait();
            while (true)
            {
                uint target = (uint)(_clock.ElapsedMilliseconds * TickHz / 1000);
                if (_tick > target)
                {
                    Thread.Sleep(1);
                    continue;
                }
                while (_tick <= target)
                {
                    StepOnce(_tick);
                    _tick++;
                }
                spin.SpinOnce();
            }
        }

        void StepOnce(uint tick)
        {
            Apply();                                 // requests that arrived since the last tick

            bool wasWon = _sim.Won;
            _sim.Step(tick);
            DrainSim();
            if (!wasWon && _sim.Won)
            {
                _advanceAt = tick + WinHoldTicks;
                Log($"won at tick {tick}");
            }
            if (_sim.Won && !_trailComplete && tick >= _advanceAt) Advance(tick);

            if (tick >= _nextSnapshotTick)
            {
                Broadcast(WireCodec.EncodeSnapshot(_sim.Snapshot(tick)));
                _nextSnapshotTick = tick - tick % TicksPerSnapshot + TicksPerSnapshot;
            }
        }

        /// <summary>After a win: the next level on the trail, or this one again if there is none.</summary>
        void Advance(uint tick)
        {
            string next = _trail != null ? _trail.Next(_sim.World.Level.Id) : null;
            if (_trail != null && next == null)
            {
                _trailComplete = true;
                Say("the trail is complete. well walked.");
                return;
            }

            LevelDef lvl = next != null && _load != null ? _load(next) : null;
            if (next != null && lvl == null)
            {
                Say($"the trail goes on to '{next}', but that level would not load.");
                _trailComplete = true;
                return;
            }

            if (lvl != null) _sim = new MockSim(lvl);
            var now = _sim.World.Level;
            Log($"level '{now.Id}'");
            lock (_sessionLock)
                foreach (var s in _sessions) SendOpening(s, now);
            _sim.Restart(tick);
            DrainSim();
            Say($"onward: {now.Name}.");
        }

        // ------------------------------------------------------------------ sockets

        void AcceptLoop(TcpListener listener)
        {
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { return; }

                client.NoDelay = true;
                var session = new Session((byte)Interlocked.Increment(ref _nextClientId), client);
                lock (_sessionLock) _sessions.Add(session);
                Log($"client {session.Id} connected from {session.Remote}");

                SendOpening(session, _sim.World.Level);
                session.Send(WireCodec.EncodeSnapshot(_sim.Snapshot(_tick)));

                var t = new Thread(() => ReadLoop(session)) { IsBackground = true, Name = "client" + session.Id };
                t.Start();
            }
        }

        void SendOpening(Session s, LevelDef level)
        {
            s.Send(WireCodec.EncodeWelcome(new Welcome
            {
                ClientId = s.Id,
                Width = level.Width,
                Height = level.Height,
                TickHz = TickHz,
                ReplicationHz = ReplicationHz,
            }));
            s.Send(WireCodec.EncodeLevel(level.Id));
            s.Send(WireCodec.EncodeSeats(_sim.Seats()));
        }

        void ReadLoop(Session s)
        {
            try
            {
                string line;
                while ((line = s.Reader.ReadLine()) != null)
                    if (line.Length > 0) _inbox.Enqueue(new Inbound { From = s, Line = line });
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is SocketException)
            {
                // the drop is reported below like any other
            }
            Drop(s);
        }

        void Drop(Session s)
        {
            bool removed;
            lock (_sessionLock) removed = _sessions.Remove(s);
            s.Close();
            if (removed) Log($"client {s.Id} ({s.Name}) disconnected");
        }

        // ------------------------------------------------------------------ requests

        Session _current;   // whose line is being handled, for the duration of one dispatch

        void Apply()
        {
            while (_inbox.TryDequeue(out var item))
            {
                _current = item.From;
                if (!WireCodec.TryDispatchClient(item.Line, this, out var err))
                    Log($"client {item.From.Id}: {err}");
                _current = null;
            }
        }

        public void OnHello(string name)
        {
            _current.Name = string.IsNullOrWhiteSpace(name) ? "someone" : name.Trim();
            Log($"client {_current.Id} is '{_current.Name}'");
            Say($"welcome to {_sim.World.Level.Name}, {_current.Name}. you hold both leopards: Tab switches between them.");
        }

        public void OnInput(InputMsg msg) => _sim.SetInput(msg.Move, msg.Interact);

        public void OnChat(ChatMsg msg) => Broadcast(WireCodec.EncodeChat(_current.Id, msg.Text));

        public void OnControl(int seat)
        {
            _sim.SetActive(seat, _tick);
            DrainSim();
            Broadcast(WireCodec.EncodeSeats(_sim.Seats()));
        }

        // ------------------------------------------------------------------ output

        void DrainSim()
        {
            while (_sim.Out.Count > 0) Broadcast(_sim.Out.Dequeue());
        }

        void Say(string text) => Broadcast(WireCodec.EncodeChat(MockSim.ServerSender, text));

        void Broadcast(string line)
        {
            List<Session> dead = null;
            lock (_sessionLock)
            {
                foreach (var s in _sessions)
                    if (!s.Send(line)) (dead ?? (dead = new List<Session>())).Add(s);
            }
            if (dead != null) foreach (var s in dead) Drop(s);
        }

        void Log(string text)
        {
            if (!_quiet) Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}");
        }
    }

    /// <summary>One connected client: a socket, a writer under a lock, and a name.</summary>
    sealed class Session
    {
        readonly TcpClient _client;
        readonly StreamWriter _writer;
        readonly object _writeLock = new object();
        bool _closed;

        public readonly byte Id;
        public readonly string Remote;
        public readonly StreamReader Reader;
        public string Name = "?";

        public Session(byte id, TcpClient client)
        {
            Id = id;
            _client = client;
            Remote = client.Client.RemoteEndPoint != null ? client.Client.RemoteEndPoint.ToString() : "?";
            var stream = client.GetStream();
            var utf8 = new UTF8Encoding(false);
            _writer = new StreamWriter(stream, utf8) { AutoFlush = true, NewLine = "\n" };
            Reader = new StreamReader(stream, utf8);
        }

        /// <summary>False once the socket has gone, so the caller can drop this session.</summary>
        public bool Send(string line)
        {
            if (_closed) return false;
            try
            {
                lock (_writeLock) _writer.WriteLine(line);
                return true;
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is SocketException)
            {
                _closed = true;
                return false;
            }
        }

        public void Close()
        {
            _closed = true;
            try { _client.Close(); } catch { /* already gone */ }
        }
    }
}
