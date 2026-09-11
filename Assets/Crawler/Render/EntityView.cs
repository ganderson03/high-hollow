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
        GameObject _carriedPip;
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
            if (!rebind && gameObject.activeSelf && visible)
                _glideOffset += transform.localPosition - cell; // visual catches up; root does not
            else
                _glideOffset = Vector3.zero;
            transform.localPosition = cell;

            SetLook(look);
            _animTarget = s.State != 0 ? 1f : 0f;
            SetCarried(s.CarriedBy >= 0);

            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        void SetLook(int look)
        {
            if (look == Look) return;
            Look = look;
            if (_lookGo != null) Destroy(_lookGo);
            _lookGo = Looks.Build(look, _visual);
            _anim = _lookGo.transform.Find("Anim");
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

        void SetCarried(bool carried)
        {
            if (carried && _carriedPip == null)
            {
                _carriedPip = new GameObject("CarriedPip");
                _carriedPip.transform.SetParent(_visual, false);
                _carriedPip.transform.localPosition = new Vector3(0, 1.05f, 0);
                _carriedPip.transform.localScale = new Vector3(0.16f, 0.04f, 0.16f);
                _carriedPip.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(PrimitiveType.Cylinder);
                _carriedPip.AddComponent<MeshRenderer>().sharedMaterial = ProcArt.Solid(Palette.Cream);
            }
            if (_carriedPip != null) _carriedPip.SetActive(carried);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _glideOffset *= Mathf.Exp(-GlideRate * dt);
            _visual.localPosition = _glideOffset;

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
            }
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);
    }
}
