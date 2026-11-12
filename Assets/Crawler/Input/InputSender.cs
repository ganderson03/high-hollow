using System;
using Crawler.Protocol;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Crawler.Input
{
    /// <summary>
    /// Samples WASD/arrows + space and sends exactly one Input per replication interval.
    /// Move is whatever direction is held at send time (last pressed wins); Interact is
    /// latched so a tap between sends is not lost. The tick stamp is the last snapshot
    /// tick received; the client never advances ticks itself.
    /// </summary>
    public sealed class InputSender : MonoBehaviour
    {
        [SerializeField] int replicationHz = 20;
        [SerializeField] bool logInputs = true;

        public Action<InputMsg> Sink;
        public Func<uint> TickSource;
        public Func<bool> Suppressed; // e.g. chat field focused

        /// <summary>Tab: switch to the other leopard, when this client holds both.</summary>
        public Action OnSwitch;

        /// <summary>R: propose restarting the level, or agree to the partner's proposal.</summary>
        public Action OnVote;

        float _accum;
        bool _interactLatched;
        MoveDir _lastPressed = MoveDir.None;

        public void SetReplicationHz(int hz) => replicationHz = Mathf.Max(1, hz);

        void Update()
        {
            var kb = Keyboard.current;
            bool blocked = Suppressed != null && Suppressed();

            if (kb != null && !blocked)
            {
                if (kb.spaceKey.wasPressedThisFrame) _interactLatched = true;
                if (kb.tabKey.wasPressedThisFrame) { _lastPressed = MoveDir.None; OnSwitch?.Invoke(); }
                if (kb.rKey.wasPressedThisFrame) OnVote?.Invoke();
                if (Pressed(kb.wKey, kb.upArrowKey)) _lastPressed = MoveDir.N;
                if (Pressed(kb.sKey, kb.downArrowKey)) _lastPressed = MoveDir.S;
                if (Pressed(kb.dKey, kb.rightArrowKey)) _lastPressed = MoveDir.E;
                if (Pressed(kb.aKey, kb.leftArrowKey)) _lastPressed = MoveDir.W;
            }

            float interval = 1f / replicationHz;
            _accum += Time.unscaledDeltaTime;
            if (_accum < interval) return;
            _accum %= interval; // one send per interval, even after a hitch

            var move = blocked || kb == null ? MoveDir.None : CurrentMove(kb);
            var msg = new InputMsg(TickSource?.Invoke() ?? 0, move, _interactLatched && !blocked);
            _interactLatched = false;

            Sink?.Invoke(msg);
            if (logInputs) Debug.Log("[Input] " + WireCodec.EncodeInput(msg));
        }

        MoveDir CurrentMove(Keyboard kb)
        {
            if (Held(_lastPressed, kb)) return _lastPressed;
            foreach (var d in new[] { MoveDir.N, MoveDir.S, MoveDir.E, MoveDir.W })
                if (Held(d, kb)) return _lastPressed = d;
            return MoveDir.None;
        }

        static bool Held(MoveDir d, Keyboard kb)
        {
            switch (d)
            {
                case MoveDir.N: return kb.wKey.isPressed || kb.upArrowKey.isPressed;
                case MoveDir.S: return kb.sKey.isPressed || kb.downArrowKey.isPressed;
                case MoveDir.E: return kb.dKey.isPressed || kb.rightArrowKey.isPressed;
                case MoveDir.W: return kb.aKey.isPressed || kb.leftArrowKey.isPressed;
                default: return false;
            }
        }

        static bool Pressed(UnityEngine.InputSystem.Controls.KeyControl a, UnityEngine.InputSystem.Controls.KeyControl b) =>
            a.wasPressedThisFrame || b.wasPressedThisFrame;
    }
}
