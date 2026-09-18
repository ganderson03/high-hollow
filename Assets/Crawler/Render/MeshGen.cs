using System.Collections.Generic;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>
    /// Procedural flat-shaded meshes: faceted prisms and cones, chiseled stone slabs, extruded
    /// flat outlines, and a ring. Every face gets its own vertices so lighting stays faceted
    /// rather than smooth, which is what keeps these shapes from reading as spheroids.
    ///
    /// Meshes are cached by key. Domain reload is off in this project, so the cache is reset
    /// explicitly and every hit is checked against Unity's own null.
    /// </summary>
    public static class MeshGen
    {
        /// <summary>
        /// Bases sit a hair below the origin. A face exactly on y=0 is coplanar with the floor
        /// quad, and the two z-fight into scanlines wherever a piece meets the ground.
        /// </summary>
        const float Sink = -0.006f;

        static readonly Dictionary<string, Mesh> _cache = new Dictionary<string, Mesh>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _cache.Clear();

        /// <summary>
        /// Drops every cached mesh. The editor scene builder calls this after it clears the
        /// generated asset folder, since those assets are the cached objects.
        /// </summary>
        public static void ClearCache() => _cache.Clear();

        static Mesh Cached(string key, System.Func<Mesh> make)
        {
            if (_cache.TryGetValue(key, out var m) && m != null) return m;
            m = make();
            m.name = key;
            _cache[key] = m;
            return m;
        }

        /// <summary>
        /// A faceted prism: sides=4 is a chunky block, 6..8 reads as a turned post, rTop=0 is a
        /// cone. Jitter pushes each ring vertex in and out so no two stones look stamped.
        /// </summary>
        public static Mesh Prism(int sides, float rBottom, float rTop, float height, int seed = 0, float jitter = 0f, float yaw = 0f) =>
            Cached($"prism_{sides}_{rBottom:F3}_{rTop:F3}_{height:F3}_{seed}_{jitter:F3}_{yaw:F1}", () =>
            {
                var bottom = new Vector3[sides];
                var top = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = (i / (float)sides) * Mathf.PI * 2f + yaw * Mathf.Deg2Rad;
                    float jb = 1f + (Hash(seed, i, 1) - 0.5f) * 2f * jitter;
                    float jt = 1f + (Hash(seed, i, 2) - 0.5f) * 2f * jitter;
                    bottom[i] = new Vector3(Mathf.Cos(a) * rBottom * jb, Sink, Mathf.Sin(a) * rBottom * jb);
                    top[i] = new Vector3(Mathf.Cos(a) * rTop * jt, height, Mathf.Sin(a) * rTop * jt);
                }

                var b = new Builder();
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    if (rTop <= 0.0001f) b.Tri(bottom[i], bottom[j], new Vector3(0, height, 0));
                    else b.Quad(bottom[i], bottom[j], top[j], top[i]);
                }
                b.Fan(bottom, new Vector3(0, Sink, 0), true);
                if (rTop > 0.0001f) b.Fan(top, new Vector3(0, height, 0), false);
                return b.ToMesh();
            });

        /// <summary>
        /// A chiseled stone: a box whose corners are pulled in and jittered, so it reads as
        /// split rock with flat faces and a usable flat top. Used for dry-stone courses.
        /// </summary>
        public static Mesh Slab(Vector3 size, int seed, float jitter = 0.12f, float taper = 0.85f) =>
            Cached($"slab_{size.x:F3}_{size.y:F3}_{size.z:F3}_{seed}_{jitter:F3}_{taper:F3}", () =>
            {
                var h = size * 0.5f;
                var corners = new Vector3[8];
                for (int i = 0; i < 8; i++)
                {
                    bool up = (i & 4) != 0;
                    float sx = (i & 1) == 0 ? -1f : 1f;
                    float sz = (i & 2) == 0 ? -1f : 1f;
                    float t = up ? taper : 1f; // narrower on top, like a split block
                    corners[i] = new Vector3(
                        sx * h.x * t * (1f + (Hash(seed, i, 3) - 0.5f) * jitter),
                        up ? size.y * (1f + (Hash(seed, i, 4) - 0.5f) * jitter * 0.6f) : Sink,
                        sz * h.z * t * (1f + (Hash(seed, i, 5) - 0.5f) * jitter));
                }

                var b = new Builder();
                b.Quad(corners[4], corners[5], corners[7], corners[6]); // top
                b.Quad(corners[2], corners[3], corners[1], corners[0]); // bottom
                b.Quad(corners[0], corners[1], corners[5], corners[4]); // -z
                b.Quad(corners[3], corners[2], corners[6], corners[7]); // +z
                b.Quad(corners[2], corners[0], corners[4], corners[6]); // -x
                b.Quad(corners[1], corners[3], corners[7], corners[5]); // +x
                return b.ToMesh();
            });

        /// <summary>A low-poly ring in the XZ plane: the scarf wrapped around a neck.</summary>
        public static Mesh Ring(float radius, float tube, int seg = 12, int sides = 6) =>
            Cached($"ring_{radius:F3}_{tube:F3}_{seg}_{sides}", () =>
            {
                var b = new Builder();
                for (int i = 0; i < seg; i++)
                {
                    float a0 = (i / (float)seg) * Mathf.PI * 2f, a1 = ((i + 1) / (float)seg) * Mathf.PI * 2f;
                    for (int j = 0; j < sides; j++)
                    {
                        float t0 = (j / (float)sides) * Mathf.PI * 2f, t1 = ((j + 1) / (float)sides) * Mathf.PI * 2f;
                        b.Quad(RingPt(a0, t0, radius, tube), RingPt(a1, t0, radius, tube),
                               RingPt(a1, t1, radius, tube), RingPt(a0, t1, radius, tube));
                    }
                }
                return b.ToMesh(p =>
                {
                    float a = Mathf.Atan2(p.z, p.x);
                    return new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                });
            });

        static Vector3 RingPt(float a, float t, float radius, float tube)
        {
            float r = radius + Mathf.Cos(t) * tube;
            return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(t) * tube, Mathf.Sin(a) * r);
        }

        /// <summary>
        /// Extrudes a flat outline (XY, counter-clockwise, fan-triangulated so it wants to be
        /// convex) along Z. This is how the graphic props are built: a bold readable silhouette
        /// with just enough thickness to catch the light.
        /// </summary>
        public static Mesh Extrude(string key, Vector2[] poly, float thickness) =>
            Cached($"ex_{key}_{thickness:F3}", () =>
            {
                var b = new Builder();
                float z = thickness * 0.5f;
                var front = new Vector3[poly.Length];
                var back = new Vector3[poly.Length];
                for (int i = 0; i < poly.Length; i++)
                {
                    front[i] = new Vector3(poly[i].x, poly[i].y, -z);
                    back[i] = new Vector3(poly[i].x, poly[i].y, z);
                }
                for (int i = 1; i < poly.Length - 1; i++)
                {
                    b.Tri(front[0], front[i], front[i + 1]);
                    b.Tri(back[0], back[i + 1], back[i]);
                }
                for (int i = 0; i < poly.Length; i++)
                {
                    int j = (i + 1) % poly.Length;
                    b.Quad(front[i], front[j], back[j], back[i]);
                }
                return b.ToMesh();
            });

        /// <summary>A regular polygon outline, for discs that should read as cut shapes.</summary>
        public static Vector2[] Polygon(int sides, float radius, float yaw = 0f)
        {
            var p = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i / (float)sides) * Mathf.PI * 2f + yaw * Mathf.Deg2Rad;
                p[i] = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
            }
            return p;
        }

        static float Hash(int a, int b, int c)
        {
            unchecked
            {
                int h = a * 73856093 ^ b * 19349663 ^ c * 83492791;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>Accumulates unshared triangles so every face keeps its own normal.</summary>
        sealed class Builder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<int> _t = new List<int>();

            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c);
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            public void Fan(Vector3[] ring, Vector3 centre, bool reverse)
            {
                for (int i = 0; i < ring.Length; i++)
                {
                    int j = (i + 1) % ring.Length;
                    if (reverse) Tri(centre, ring[j], ring[i]);
                    else Tri(centre, ring[i], ring[j]);
                }
            }

            /// <summary>
            /// Builds the mesh, first making every triangle face outwards. Winding by hand is
            /// easy to get wrong in one case out of six, and a wrong one is invisible from the
            /// angle you happen to be looking from and inside-out from every other.
            /// </summary>
            /// <param name="interiorAt">
            /// A point inside the solid, given a point on its surface. The default is the mesh
            /// centroid, which is correct for anything convex; the ring passes its tube centre.
            /// </param>
            public Mesh ToMesh(System.Func<Vector3, Vector3> interiorAt = null)
            {
                OrientOutward(interiorAt);
                var m = new Mesh();
                m.SetVertices(_v);
                m.SetTriangles(_t, 0);
                m.SetUVs(0, PlanarUVs());
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }

            void OrientOutward(System.Func<Vector3, Vector3> interiorAt)
            {
                var centroid = Vector3.zero;
                foreach (var v in _v) centroid += v;
                centroid /= Mathf.Max(1, _v.Count);

                for (int i = 0; i < _v.Count; i += 3)
                {
                    var a = _v[i];
                    var b = _v[i + 1];
                    var c = _v[i + 2];
                    var face = (a + b + c) / 3f;
                    var inside = interiorAt != null ? interiorAt(face) : centroid;
                    // Unity is left-handed: this cross product is the front-facing normal.
                    var n = Vector3.Cross(b - a, c - a);
                    if (Vector3.Dot(n, face - inside) >= 0f) continue;
                    _v[i + 1] = c;
                    _v[i + 2] = b;
                }
            }

            /// <summary>
            /// Per-face planar UVs: each triangle is projected down its own dominant axis.
            /// Without these the fur texture has nothing to sample and the coat reads flat.
            /// </summary>
            List<Vector2> PlanarUVs()
            {
                var uv = new List<Vector2>(_v.Count);
                for (int i = 0; i < _v.Count; i += 3)
                {
                    var n = Vector3.Cross(_v[i + 1] - _v[i], _v[i + 2] - _v[i]);
                    var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                    for (int k = 0; k < 3; k++)
                    {
                        var p = _v[i + k];
                        if (a.y >= a.x && a.y >= a.z) uv.Add(new Vector2(p.x, p.z));
                        else if (a.x >= a.z) uv.Add(new Vector2(p.z, p.y));
                        else uv.Add(new Vector2(p.x, p.y));
                    }
                }
                return uv;
            }
        }
    }
}
