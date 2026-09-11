using Crawler.Protocol;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>
    /// One rendered entity. The root transform sits exactly on the authoritative cell;
    /// everything that eases (glide, door sink, lever tilt) lives on the "Visual" child
    /// and is derived from state, never read back.
    /// </summary>
    public sealed class EntityView : MonoBehaviour
    {
        const float GlideRate = 18f;   // 1/s, exponential decay of the visual offset
        const float AnimRate = 10f;

        public int Id { get; private set; } = -1; // -1 = phantom (no server id)
        public int Look { get; private set; } = int.MinValue;

        Transform _visual;
        Transform _anim;
        GameObject _lookGo;
        Vector3 _lift;
        Color? _accent;
        Transform _tail;
        float _yaw = 210f; // default: face the camera
        LookAnim _animMode;
        float _animTarget, _anim01;
        Vector3 _glideOffset;
        float _phase;

        // Views are pooled inactive, so Awake hasn't run by the first Bind; init lazily.
        void EnsureInit()
        {
            if (_visual != null) return;
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);
            _phase = Random.value * 10f;
        }

        /// <summary>Bind to authoritative state. Position snaps; nothing is smoothed at the root.</summary>
        public void Bind(int id, in EntityState s, int look, bool visible)
        {
            EnsureInit();
            bool rebind = Id != id;
            Id = id;
            name = id >= 0 ? $"E{id}_{s.Kind}" : "Phantom";

            var cell = GridSpace.CellToWorld(s.X, s.Y);
            var prev = transform.localPosition;
            if (!rebind && gameObject.activeSelf && visible)
            {
                _glideOffset += prev - cell; // visual catches up; root does not
                // Face the way we just moved (a derived, visual-only heading).
                var step = cell - prev;
                if (step.sqrMagnitude > 0.01f && step.sqrMagnitude < 4f)
                    _yaw = Mathf.Atan2(step.x, step.z) * Mathf.Rad2Deg;
            }
            else
                _glideOffset = Vector3.zero;
            transform.localPosition = cell;

            SetLook(look);
            _animTarget = s.State != 0 ? 1f : 0f;
            SetCarried(s.CarriedBy >= 0);

            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        /// <summary>Per-player accent (scarf color); null for none. Rebuilds the look if it changes.</summary>
        public void SetAccent(Color? accent)
        {
            if (accent == _accent) return;
            _accent = accent;
            if (Look == int.MinValue) return;
            int look = Look;
            Look = int.MinValue;
            SetLook(look);
        }

        void SetLook(int look)
        {
            if (look == Look) return;
            Look = look;
            if (_lookGo != null) Destroy(_lookGo);
            _lookGo = Looks.Build(look, _visual, _accent);
            _anim = _lookGo.transform.Find("Anim");
            _tail = _lookGo.transform.Find("Anim/Tail");
            _animMode = Looks.AnimFor(look);
            _anim01 = _animTarget;
            _glideOffset = Vector3.zero;
        }

        GameObject _localRing;

        /// <summary>Ember ring under the local player's feet. Perception only.</summary>
        public void SetLocal(bool isLocal)
        {
            if (isLocal && _localRing == null)
            {
                EnsureInit();
                _localRing = new GameObject("LocalRing");
                _localRing.transform.SetParent(_visual, false);
                _localRing.transform.localPosition = new Vector3(0, 0.012f, 0);
                _localRing.transform.localScale = new Vector3(1.0f, 0.01f, 1.0f);
                _localRing.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(PrimitiveType.Cylinder);
                _localRing.AddComponent<MeshRenderer>().sharedMaterial = ProcArt.Glow(Palette.Ochre, 0.2f);
            }
            if (_localRing != null) _localRing.SetActive(isLocal);
        }

        /// <summary>Carried things float above the carrier's head, fanned out by id. Visual only.</summary>
        void SetCarried(bool carried) =>
            _lift = carried ? new Vector3((Id % 3 - 1) * 0.3f, 1.05f, 0f) : Vector3.zero;

        void Update()
        {
            float dt = Time.deltaTime;
            _glideOffset *= Mathf.Exp(-GlideRate * dt);
            _visual.localPosition = _glideOffset + _lift;
            var facing = _animMode == LookAnim.Critter ? Quaternion.Euler(0, _yaw, 0) : Quaternion.identity;
            _visual.localRotation = Quaternion.Slerp(_visual.localRotation, facing, 1f - Mathf.Exp(-12f * dt));

            _anim01 = Mathf.MoveTowards(_anim01, _animTarget, AnimRate * dt * 0.25f);
            if (_anim == null) return;
            float t = Time.time + _phase;
            switch (_animMode)
            {
                case LookAnim.Sink:  _anim.localPosition = new Vector3(0, -0.95f * Smooth(_anim01), 0); break;
                case LookAnim.Tilt:  _anim.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(35f, -35f, Smooth(_anim01))); break;
                case LookAnim.Press: _anim.localPosition = new Vector3(0, -0.035f * Smooth(_anim01), 0); break;
                case LookAnim.Bob:   _anim.localPosition = new Vector3(0, Mathf.Sin(t * 2.2f) * 0.05f, 0); break;
                case LookAnim.Pulse: _anim.localScale = Vector3.one * (1f + Mathf.Sin(t * 3.4f) * 0.06f); break;
                case LookAnim.Critter:
                    bool moving = _glideOffset.sqrMagnitude > 0.0004f;
                    _anim.localPosition = new Vector3(0,
                        moving ? Mathf.Abs(Mathf.Sin(t * 16f)) * 0.05f : Mathf.Sin(t * 2f) * 0.008f, 0);
                    if (_tail != null)
                        _tail.localRotation = Quaternion.Euler(0, Mathf.Sin(t * (moving ? 6f : 2.4f)) * (moving ? 24f : 14f), 0);
                    break;
            }
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);
    }
}
