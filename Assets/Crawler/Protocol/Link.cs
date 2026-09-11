using System;

namespace Crawler.Protocol
{
    /// <summary>Receives decoded inbound messages. Always called on the main thread, from Pump().</summary>
    public interface IInboundHandler
    {
        void OnWelcome(Welcome msg);
        void OnSnapshot(Snapshot msg);
        void OnOverlay(Overlay msg);
        void OnChat(ChatMsg msg);
        void OnLevel(LevelMsg msg);
        void OnRound(RoundMsg msg);
        void OnStatus(LinkStatus status, string detail);
    }

    /// <summary>
    /// The one seam between the client and "a server". ServerConnection and MockFeed
    /// both implement it; nothing downstream knows which one it has.
    /// </summary>
    public interface IServerLink : IDisposable
    {
        void Start(string playerName);

        /// <summary>Drain everything received since the last call into the handler. Main thread only.</summary>
        void Pump(IInboundHandler handler);

        void SendInput(InputMsg msg);
        void SendChat(string text);
    }
}
