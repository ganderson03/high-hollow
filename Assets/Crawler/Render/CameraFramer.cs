using UnityEngine;

namespace Crawler.Render
{
    /// <summary>
    /// Orthographic three-quarter camera that frames the region occupied by what this
    /// client can see. Framing uses post-overlay visibility on purpose: framing on
    /// hidden entities would leak their positions.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFramer : MonoBehaviour
    {
        [SerializeField] WorldView world;
        [SerializeField] Vector3 eulerAngles = new Vector3(58f, 30f, 0f);
        [SerializeField] float paddingCells = 5f;
        [SerializeField] float minHalfExtent = 9f;
        [SerializeField] float easeRate = 4f;
        [SerializeField] float regionPaddingCells = 1.5f;
        [SerializeField] float regionMinHalfExtent = 5f;

        /// <summary>Room to frame, in grid cells; null falls back to framing visible entities.</summary>
        public System.Func<RectInt?> RegionSource;

        Camera _cam;
        Vector3 _center;
        float _size;
        bool _primed;

        public void SetWorld(WorldView w) => world = w;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Palette.Night;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 200f;
            transform.rotation = Quaternion.Euler(eulerAngles);
            _center = new Vector3(16, 0, 16);
            _size = 12f;
        }

        void LateUpdate()
        {
            if (world == null) return;
            var rot = Quaternion.Euler(eulerAngles);
            var inv = Quaternion.Inverse(rot);

            bool any = false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            void Add(Vector3 p)
            {
                any = true;
                // Include a little height so tall pieces aren't clipped at the top edge.
                for (int k = 0; k < 2; k++)
                {
                    var c = inv * (p + new Vector3(0, k * 1.2f, 0));
                    min = Vector3.Min(min, c);
                    max = Vector3.Max(max, c);
                }
            }
            float pad = paddingCells, minExtent = minHalfExtent;
            var region = RegionSource?.Invoke();
            if (region.HasValue)
            {
                // Locked to the room the local player is in: steady framing, no zoom breathing.
                var r = region.Value;
                Add(new Vector3(r.xMin, 0, r.yMin)); Add(new Vector3(r.xMax, 0, r.yMin));
                Add(new Vector3(r.xMin, 0, r.yMax)); Add(new Vector3(r.xMax, 0, r.yMax));
                pad = regionPaddingCells;
                minExtent = regionMinHalfExtent;
            }
            else
            {
                foreach (var v in world.Views) if (v.gameObject.activeInHierarchy) Add(v.transform.position);
                foreach (var v in world.PhantomViews) if (v.gameObject.activeInHierarchy) Add(v.transform.position);
                if (!any) { Add(new Vector3(0, 0, 0)); Add(new Vector3(GridSpace.Size, 0, GridSpace.Size)); }
            }

            Vector3 midCam = (min + max) * 0.5f;
            Vector3 targetCenter = rot * midCam;
            float halfH = (max.y - min.y) * 0.5f + pad;
            float halfW = (max.x - min.x) * 0.5f + pad;
            float targetSize = Mathf.Max(minExtent, halfH, halfW / Mathf.Max(0.1f, _cam.aspect));

            // Visual easing of the camera only; derived, never fed back.
            float k2 = _primed ? 1f - Mathf.Exp(-easeRate * Time.deltaTime) : 1f;
            _primed = true;
            _center = Vector3.Lerp(_center, targetCenter, k2);
            _size = Mathf.Lerp(_size, targetSize, k2);

            transform.rotation = rot;
            transform.position = _center - rot * Vector3.forward * 60f;
            _cam.orthographicSize = _size;
        }
    }
}
