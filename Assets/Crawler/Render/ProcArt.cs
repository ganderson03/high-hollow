using System.Collections.Generic;
using Crawler.Protocol;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>
    /// Placeholder art generated at runtime: materials, meshes, and textures (floor,
    /// snow leopard rosettes, paw print, poster). No imported art.
    /// </summary>
    public static class ProcArt
    {
        static Shader _lit;
        static Material _tplLit, _tplGlow;
        static readonly Dictionary<PrimitiveType, Mesh> _meshes = new Dictionary<PrimitiveType, Mesh>();
        static readonly Dictionary<Color, Material> _solid = new Dictionary<Color, Material>();
        static Texture2D _rosettes, _paw;
        static Material _fur;

        /// <summary>Shared snow leopard fur material (rosette texture).</summary>
        public static Material Fur
        {
            get
            {
                if (_fur != null) return _fur;
                _fur = Textured(Rosettes, Color.white);
                // The faceted body parts carry planar UVs in local metres, so a body panel is
                // well under one texture unit across. Tile hard or the rosettes come out huge.
                var tiling = new Vector2(7f, 7f);
                _fur.SetTextureScale("_BaseMap", tiling);
                _fur.mainTextureScale = tiling;
                return _fur;
            }
        }

        // The project enters play mode with domain reload disabled, so statics survive
        // between sessions while the objects they point at are destroyed on exit. A
        // destroyed material renders magenta. Reset at every play start, and treat any
        // Unity-null cache hit as a miss.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _lit = null;
            _tplLit = null;
            _tplGlow = null;
            _meshes.Clear();
            _solid.Clear();
            _rosettes = null;
            _paw = null;
            _fur = null;
        }

        /// <summary>
        /// Drops every cached material and texture. The editor scene builder calls this after
        /// it clears the generated asset folder, since those assets are the cached objects.
        /// </summary>
        public static void ClearCache() => ResetStatics();

        static Shader Lit
        {
            get
            {
                if (_lit != null) return _lit;
                // Fallback only. (No ?? here: it bypasses Unity's null check on destroyed objects.)
                _lit = Shader.Find("Universal Render Pipeline/Lit");
                if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (_lit == null) _lit = Shader.Find("Standard");
                return _lit;
            }
        }

        /// <summary>
        /// New material cloned from a template asset under Resources. The templates are what
        /// keep URP Lit (and its _EMISSION variant) in player builds; Shader.Find alone would
        /// return a stripped shader and everything would render magenta.
        /// </summary>
        static Material NewLit(bool emissive)
        {
            if (emissive)
            {
                if (_tplGlow == null) _tplGlow = Resources.Load<Material>("CrawlerLitEmissive");
                if (_tplGlow != null) return new Material(_tplGlow);
            }
            else
            {
                if (_tplLit == null) _tplLit = Resources.Load<Material>("CrawlerLit");
                if (_tplLit != null) return new Material(_tplLit);
            }
            return new Material(Lit);
        }

        public static Mesh Mesh(PrimitiveType type)
        {
            if (_meshes.TryGetValue(type, out var m) && m != null) return m;
            var go = GameObject.CreatePrimitive(type);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            // The editor scene builder runs outside play mode, where Destroy is refused.
            if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
            _meshes[type] = m;
            return m;
        }

        public static Material Solid(Color c, float smooth = 0.15f)
        {
            if (_solid.TryGetValue(c, out var mat) && mat != null) return mat;
            mat = NewLit(false);
            mat.name = "Proc_" + ColorUtility.ToHtmlStringRGB(c);
            mat.SetColor("_BaseColor", c);
            mat.color = c;
            mat.SetFloat("_Smoothness", smooth);
            _solid[c] = mat;
            return mat;
        }

        public static Material Glow(Color c, float intensity)
        {
            var mat = NewLit(true);
            mat.name = "ProcGlow_" + ColorUtility.ToHtmlStringRGB(c);
            mat.SetColor("_BaseColor", c);
            mat.color = c;
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", c * intensity);
            return mat;
        }

        public static Material Textured(Texture2D tex, Color tint)
        {
            var mat = NewLit(false);
            mat.name = "ProcTex_" + tex.name;
            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Smoothness", 0.1f);
            return mat;
        }

        /// <summary>Deterministic 0..1 hash for scattering and variation.</summary>
        public static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>Cream fur with dark open rosettes. Tiles around a capsule.</summary>
        public static Texture2D Rosettes
        {
            get
            {
                if (_rosettes != null) return _rosettes;
                const int n = 128;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Rosettes", wrapMode = TextureWrapMode.Repeat };
                var px = new Color[n * n];
                var rng = new System.Random(7);
                var spots = new List<Vector3>();
                for (int i = 0; i < 14; i++) spots.Add(new Vector3(rng.Next(n), rng.Next(n), 7 + rng.Next(6)));
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Color c = Color.Lerp(Palette.Cream, Palette.Sand, (y / (float)n) * 0.35f);
                    foreach (var s in spots)
                    {
                        // Wrapped distance so the pattern tiles.
                        float dx = Mathf.Abs(x - s.x); dx = Mathf.Min(dx, n - dx);
                        float dy = Mathf.Abs(y - s.y); dy = Mathf.Min(dy, n - dy);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float ring = Mathf.Abs(d - s.z);
                        if (ring < 2.2f && (Mathf.Atan2(dy, dx) * 3f + s.x) % 2f > 0.45f) c = Palette.Rosette;
                        else if (d < s.z - 2.2f) c = Color.Lerp(c, Palette.SandDeep, 0.55f);
                    }
                    px[y * n + x] = c;
                }
                tex.SetPixels(px);
                tex.Apply(true, false);
                return _rosettes = tex;
            }
        }

        /// <summary>Snow leopard paw print on transparent; used for cursor and loading glyph.</summary>
        public static Texture2D Paw
        {
            get
            {
                if (_paw != null) return _paw;
                const int n = 32;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Paw", filterMode = FilterMode.Bilinear };
                var px = new Color[n * n];
                var blobs = new[]
                {
                    new Vector3(16, 11, 7.5f),                    // main pad
                    new Vector3(7, 20, 3.6f), new Vector3(12.5f, 25, 3.8f),
                    new Vector3(19.5f, 25, 3.8f), new Vector3(25, 20, 3.6f),
                };
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = 0, edge = 0;
                    foreach (var b in blobs)
                    {
                        float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(b.x, b.y));
                        a = Mathf.Max(a, Mathf.Clamp01(b.z + 0.5f - d));
                        edge = Mathf.Max(edge, Mathf.Clamp01(b.z + 1.8f - d));
                    }
                    // Sienna pad with a cream outline so it reads on both floor and night bg.
                    var c = Color.Lerp(Palette.Cream, Palette.Sienna, a);
                    c.a = edge;
                    px[y * n + x] = c;
                }
                tex.SetPixels(px);
                tex.Apply();
                return _paw = tex;
            }
        }

        /// <summary>Plain grid floor (no level loaded yet).</summary>
        public static Texture2D FloorTiles(int grid, int texelsPerCell)
        {
            int n = grid * texelsPerCell;
            var tex = NewFloorTexture(n, "FloorTiles");
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int cx = x / texelsPerCell, cy = y / texelsPerCell;
                Color c = MossTexel(x, y, cx, cy, texelsPerCell);
                if (cx == 0 || cy == 0 || cx == grid - 1 || cy == grid - 1)
                    c = Color.Lerp(c, Palette.Sienna, 0.75f);
                px[y * n + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>Floor painted from a level: moss tiles in rooms, forest floor outside, bedding under walls.</summary>
        public static Texture2D LevelFloor(LevelDef level, int texelsPerCell)
        {
            int n = level.Grid * texelsPerCell;
            var tex = NewFloorTexture(n, "LevelFloor_" + level.Id);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int cx = x / texelsPerCell, cy = y / texelsPerCell;
                char t = level.At(cx, cy);
                Color c;
                if (t == 'T')
                    c = Color.Lerp(Palette.Forest, Palette.Forest2,
                        Hash01(cx, cy, 12) * 0.7f + Hash01(x / 3, y / 3, 13) * 0.3f);
                else if (t == '#')
                    c = Palette.WallBed;
                else
                    c = MossTexel(x, y, cx, cy, texelsPerCell);
                px[y * n + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply(true, false);
            return tex;
        }

        static Texture2D NewFloorTexture(int n, string name) =>
            new Texture2D(n, n, TextureFormat.RGBA32, true)
                { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4 };

        /// <summary>Quiet moss tile with a thin rounded grout line. Low contrast so pieces read.</summary>
        static Color MossTexel(int x, int y, int cx, int cy, int texelsPerCell)
        {
            float half = texelsPerCell * 0.5f;
            float r = texelsPerCell * 0.18f;
            float lx = x % texelsPerCell + 0.5f - half, ly = y % texelsPerCell + 0.5f - half;
            float inset = half - 0.6f;
            float qx = Mathf.Abs(lx) - (inset - r), qy = Mathf.Abs(ly) - (inset - r);
            float sd = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            Color tile = ((cx + cy) & 1) == 0 ? Palette.Moss : Palette.MossDeep;
            return Color.Lerp(tile, Palette.Grout, Mathf.Clamp01(sd + 0.8f));
        }

        // ------------------------------------------------------------------ poster

        /// <summary>
        /// 1970s national-park poster: banded sunset, a solid sun, three mountain ranges
        /// with snowcaps, and a pine-silhouette foreground. Used by the title and win screens.
        /// </summary>
        public static Texture2D Poster(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { name = "Poster", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            float aspect = w / (float)h;
            const float sunX = 0.5f, sunY = 0.47f, sunR = 0.2f;

            for (int y = 0; y < h; y++)
            {
                float v = y / (float)h;
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w;
                    Color c = SkyBand(v);

                    float dx = (u - sunX) * aspect, dy = v - sunY;
                    // Solid disc. It used to be banded across its lower half, but the ranges
                    // cover that half, so all that survived was one stray sienna sliver sitting
                    // across the sun like a scratch.
                    if (dx * dx + dy * dy < sunR * sunR) c = Palette.Cream;

                    float far = Ridge(u, 0.50f, 0.05f, 1.6f, 0.3f) + 0.08f * Peak(u, 0.28f, 0.10f) + 0.10f * Peak(u, 0.72f, 0.09f);
                    float mid = Ridge(u, 0.37f, 0.04f, 1.2f, 2.0f) + 0.12f * Peak(u, 0.50f, 0.14f);
                    float near = Ridge(u, 0.24f, 0.025f, 0.9f, 0.7f);
                    float fore = 0.11f + 0.012f * Mathf.Sin(u * 11f);

                    if (v < far) c = v > far - 0.022f && far > 0.57f ? Palette.Cream : Palette.Ridge;
                    if (v < mid) c = v > mid - 0.02f && mid > 0.46f ? Palette.Sand : Palette.Umber;
                    if (v < near) c = Palette.Pine;
                    if (v < fore || InPine(u, v)) c = Palette.Night;

                    float grain = 1f + (Hash01(x, y, 9) - 0.5f) * 0.05f; // a little print grain
                    px[y * w + x] = new Color(c.r * grain, c.g * grain, c.b * grain, 1f);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        static Color SkyBand(float v)
        {
            if (v > 0.86f) return Palette.Cream;
            if (v > 0.76f) return Palette.Sand;
            if (v > 0.67f) return Palette.Ochre;
            if (v > 0.59f) return Palette.Burnt;
            return Palette.Sienna;
        }

        static float Ridge(float u, float baseline, float amp, float freq, float phase) =>
            baseline
            + amp * 0.6f * Mathf.Sin(u * freq * 6.2832f + phase)
            + amp * 0.4f * Mathf.Sin(u * freq * 2.3f * 6.2832f + phase * 1.7f);

        static float Peak(float u, float center, float width)
        {
            float p = Mathf.Max(0f, 1f - Mathf.Abs(u - center) / width);
            return p * p * (3f - 2f * p);
        }

        static bool InPine(float u, float v)
        {
            const int count = 16;
            const float baseV = 0.10f;
            for (int i = 0; i < count; i++)
            {
                float cx = (i + 0.5f) / count + (Hash01(i, 0, 11) - 0.5f) * 0.035f;
                float height = 0.09f + Hash01(i, 1, 11) * 0.11f;
                float t = (v - baseV) / height;
                if (t < 0f || t > 1f) continue;
                float tier = 1f - Frac(t * 3f) * 0.35f; // three soft tiers of boughs
                float hw = 0.024f * (1f - t) * tier + 0.002f;
                if (Mathf.Abs(u - cx) < hw) return true;
            }
            return false;
        }

        static float Frac(float x) => x - Mathf.Floor(x);
    }
}
