using System.Collections.Generic;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>
    /// Placeholder art generated at runtime: materials, meshes, and a few textures
    /// (floor tiles, snow leopard rosettes, paw print). No imported assets.
    /// </summary>
    public static class ProcArt
    {
        static Shader _lit;
        static readonly Dictionary<PrimitiveType, Mesh> _meshes = new Dictionary<PrimitiveType, Mesh>();
        static readonly Dictionary<Color, Material> _solid = new Dictionary<Color, Material>();
        static Texture2D _rosettes, _paw;

        // The project enters play mode with domain reload disabled, so statics survive
        // between sessions while the objects they point at are destroyed on exit. A
        // destroyed material renders magenta. Reset at every play start, and treat any
        // Unity-null cache hit as a miss.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _lit = null;
            _meshes.Clear();
            _solid.Clear();
            _rosettes = null;
            _paw = null;
        }

        static Shader Lit
        {
            get
            {
                if (_lit != null) return _lit;
                // Editor-safe for this pass. For player builds, reference a material asset
                // instead so the URP Lit variant isn't stripped.
                // (No ?? here: it bypasses Unity's null check on destroyed objects.)
                _lit = Shader.Find("Universal Render Pipeline/Lit");
                if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (_lit == null) _lit = Shader.Find("Standard");
                return _lit;
            }
        }

        public static Mesh Mesh(PrimitiveType type)
        {
            if (_meshes.TryGetValue(type, out var m) && m != null) return m;
            var go = GameObject.CreatePrimitive(type);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            _meshes[type] = m;
            return m;
        }

        public static Material Solid(Color c, float smooth = 0.15f)
        {
            if (_solid.TryGetValue(c, out var mat) && mat != null) return mat;
            mat = new Material(Lit) { name = "Proc_" + ColorUtility.ToHtmlStringRGB(c) };
            mat.SetColor("_BaseColor", c);
            mat.color = c;
            mat.SetFloat("_Smoothness", smooth);
            _solid[c] = mat;
            return mat;
        }

        public static Material Glow(Color c, float intensity)
        {
            var mat = new Material(Lit) { name = "ProcGlow_" + ColorUtility.ToHtmlStringRGB(c) };
            mat.SetColor("_BaseColor", c);
            mat.color = c;
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", c * intensity);
            return mat;
        }

        public static Material Textured(Texture2D tex, Color tint)
        {
            var mat = new Material(Lit) { name = "ProcTex_" + tex.name };
            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Smoothness", 0.1f);
            return mat;
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

        /// <summary>Rounded-inset floor tiles, one texel block per cell, with a sienna rim.</summary>
        public static Texture2D FloorTiles(int grid, int texelsPerCell)
        {
            int n = grid * texelsPerCell;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
                { name = "FloorTiles", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            var px = new Color[n * n];
            float r = texelsPerCell * 0.18f;
            float half = texelsPerCell * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int cx = x / texelsPerCell, cy = y / texelsPerCell;
                float lx = x % texelsPerCell + 0.5f - half, ly = y % texelsPerCell + 0.5f - half;
                // Rounded-rect SDF: quiet moss tiles with a thin grout line. The floor should
                // recede so the pieces read; keep contrast low.
                float inset = half - 0.6f;
                float qx = Mathf.Abs(lx) - (inset - r), qy = Mathf.Abs(ly) - (inset - r);
                float sd = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;

                Color tile = ((cx + cy) & 1) == 0 ? Palette.Moss : Palette.MossDeep;
                Color c = Color.Lerp(tile, Palette.Grout, Mathf.Clamp01(sd + 0.8f));
                if (cx == 0 || cy == 0 || cx == grid - 1 || cy == grid - 1)
                    c = Color.Lerp(c, Palette.Sienna, 0.75f);
                px[y * n + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply(true, false);
            return tex;
        }
    }
}
