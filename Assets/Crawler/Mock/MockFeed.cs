using System;
using System.Collections.Generic;
using System.Diagnostics;
using Crawler.Protocol;

namespace Crawler.Mock
{
    /// <summary>
    /// Stand-in server. Runs a MockSim at 60 Hz against a real-time clock, emits snapshots at
    /// 20 Hz as JSON lines through the same WireCodec the real connection uses, and walks the
    /// trail from level to level.
    ///
    /// DEMO RULES LIVE HERE AND ONLY HERE (in the mechanics under Crawler.Mock). They are a
    /// stand-in so the game is playable before the Rust server exists, and the design doc is
    /// the spec, not this code. The level and trail files are the shared part.
    /// </summary>
    public sealed class MockFeed : IServerLink
    {
        const int TickHz = 60;
        const int ReplicationHz = 20;
        const int TicksPerSnapshot = TickHz / ReplicationHz;
        const byte LocalClientId = 0;
        const uint WinHoldTicks = 360;          // 6 s on the summit card before the next level

        readonly Func<string, LevelDef> _load;
        readonly TrailDef _trail;
        readonly Stopwatch _clock = new Stopwatch();
        readonly Queue<string> _lines = new Queue<string>();

        MockSim _sim;
        uint _nextSnapshotTick;
        uint _advanceAt;
        bool _started, _trailComplete;
        string _playerName;

        /// <param name="first">The level to start on.</param>
        /// <param name="trail">The trail order; null plays the one level on repeat.</param>
        /// <param name="load">Loads a level by id, for progression.</param>
        public MockFeed(LevelDef first, TrailDef trail = null, Func<string, LevelDef> load = null)
        {
            _trail = trail;
            _load = load;
            _sim = new MockSim(first);
        }

        public void Start(string playerName)
        {
            _started = true;
            _playerName = playerName;
            _clock.Start();
            Begin(0, first: true);
        }

        void Begin(uint tick, bool first)
        {
            var lvl = _sim.World.Level;
            _lines.Enqueue(WireCodec.EncodeWelcome(new Welcome
            {
                ClientId = LocalClientId, Width = lvl.Width, Height = lvl.Height, TickHz = TickHz, ReplicationHz = ReplicationHz,
            }));
            _lines.Enqueue(WireCodec.EncodeLevel(lvl.Id));
            _sim.Restart(tick);
            Drain();
            Say(first
                ? $"welcome to {lvl.Name}, {_playerName}. you hold both leopards: Tab switches between them."
                : $"onward: {lvl.Name}.");
        }

        public void Pump(IInboundHandler handler)
        {
            if (!_started) return;

            uint serverTick = (uint)(_clock.ElapsedMilliseconds * TickHz / 1000);
            int budget = 4;
            while (_nextSnapshotTick <= serverTick && budget-- > 0)
            {
                Simulate(_nextSnapshotTick);
                _lines.Enqueue(WireCodec.EncodeSnapshot(_sim.Snapshot(_nextSnapshotTick)));
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

        void Simulate(uint tick)
        {
            bool wasWon = _sim.Won;
            _sim.Step(tick);
            Drain();
            if (!wasWon && _sim.Won) _advanceAt = tick + WinHoldTicks;

            if (_sim.Won && !_trailComplete && tick >= _advanceAt) Advance(tick);
        }

        /// <summary>After a win: the next level on the trail, or the same level again if there is no trail.</summary>
        void Advance(uint tick)
        {
            string next = _trail?.Next(_sim.World.Level.Id);
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
            Begin(tick, first: false);
        }

        public void SendInput(InputMsg msg) => _sim.SetInput(msg.Move, msg.Interact);

        public void SendChat(string text) => _lines.Enqueue(WireCodec.EncodeChat(LocalClientId, text));

        /// <summary>The mock's one client holds both seats, so a restart proposal passes at once.</summary>
        public void SendVote(bool reset)
        {
            if (!reset || _sim.Won) return;
            uint tick = (uint)(_clock.ElapsedMilliseconds * TickHz / 1000);
            _lines.Enqueue(WireCodec.EncodeVoteMsg(new VoteMsg { By = LocalClientId, TicksLeft = 0, State = VoteState.Passed }));
            Say("agreed: the level starts over.");
            _sim.Restart(tick, "vote");
            Drain();
        }

        /// <summary>The mock has no partner to lose, so it never asks for a choice.</summary>
        public void SendChoice(ChoicePick pick) { }

        public void SendControl(int seat)
        {
            _sim.SetActive(seat, (uint)(_clock.ElapsedMilliseconds * TickHz / 1000));
            Drain();
        }

        public void Dispose() => _clock.Stop();

        void Drain()
        {
            while (_sim.Out.Count > 0) _lines.Enqueue(_sim.Out.Dequeue());
        }

        void Say(string text) => _lines.Enqueue(WireCodec.EncodeChat(MockSim.ServerSender, text));
    }
}
