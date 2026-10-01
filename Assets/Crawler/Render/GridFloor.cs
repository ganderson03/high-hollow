using Crawler.Protocol;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>
    /// Static terrain: the floor quad, a sienna plinth, and (once a level is known) dry-stone
    /// walls, a pine forest and ground scatter built from the level's tiles. Every piece is a
    /// flat-shaded procedural mesh. Purely visual; collision belongs to whatever simulates the
    /// world.
    /// </summary>
    public sealed class GridFloor : MonoBehaviour
    {
        [SerializeField] int grid = 32;
        [SerializeField] int texelsPerCell = 16;
        [SerializeField, Range(0f, 1f)] float treeDensity = 0.6f;

        /// <summary>
        /// Width of the scenery band drawn around the playable map, in cells. It has to cover
        /// the camera's widest framing past the map's edge (room framing plus padding at a wide
        /// aspect), so the plinth's edge never shows. Purely visual: nothing can enter it.
        /// </summary>
        [SerializeField] int apronCells = 14;

        int _w = 32, _h = 32;

        public void Build(int size)
        {
            _w = _h = grid = size;
            WorldMap.Set(null);
            Clear();
            BuildFloor(ProcArt.FloorTiles(grid, texelsPerCell), apron: 0);
        }

        public void BuildLevel(LevelDef level)
        {
            _w = level.Width;
            _h = level.Height;
            WorldMap.Set(level); // doors ask it which way their wall runs
            Clear();
            BuildFloor(ProcArt.LevelFloor(level, texelsPerCell), apronCells);

            var walls = Child("Walls");
            var trees = Child("Trees");
            var scatter = Child("Scatter");

            // Warm sky, sienna-tinted ground bounce: keeps shadows from going cold.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(Palette.Cream, Palette.Ochre, 0.3f) * 0.75f;
            RenderSettings.ambientEquatorColor = Color.Lerp(Palette.Sand, Palette.Moss, 0.3f) * 0.55f;
            RenderSettings.ambientGroundColor = Palette.Sienna * 0.35f;

            var grey = Palette.Boulder;
            var stone = new[]
            {
                ProcArt.Solid(grey),
                ProcArt.Solid(Color.Lerp(grey, Palette.MossDeep, 0.3f)),
                ProcArt.Solid(Color.Lerp(grey, Palette.Cream, 0.15f)),
            };
            var lichen = ProcArt.Solid(Color.Lerp(Palette.Moss, Palette.Needle, 0.45f));
            var tuft = new[] { ProcArt.Solid(Palette.Moss), ProcArt.Solid(Color.Lerp(Palette.Moss, Palette.Ochre, 0.35f)) };
            var needles = new[] { ProcArt.Solid(Palette.Pine), ProcArt.Solid(Palette.Needle) };
            var bark = ProcArt.Solid(Palette.Umber);
            var snow = ProcArt.Solid(Palette.Cream);

            BuildApron(Child("Apron"), level, needles, bark, snow);

            if (Art.Simple)
            {
                BuildPlaceholderTerrain(level, walls, trees, stone[0], needles[0]);
                return;
            }

            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                char t = level.At(x, y);
                if (t == '#') DryStone(walls, x, y, stone, lichen, WorldMap.AxisAt(level, x, y));
                else if (t == 'T') { if (ProcArt.Hash01(x, y, 3) < treeDensity) Pine(trees, x, y, needles, bark, snow); }
                else if (t == '.') Scatter(scatter, x, y, stone, tuft, level.RegionAt(x, y) >= 0);
                // Cells holding an entity (pits, doors, keys) get no scatter: a pebble sitting
                // in the mouth of a shaft reads as a bug.
            }
        }

        /// <summary>
        /// One box per wall cell and one per tree, no scatter. Roughly a tenth of the objects
        /// of the real terrain and it builds instantly, which is the point.
        /// </summary>
        static void BuildPlaceholderTerrain(LevelDef level, Transform walls, Transform trees, Material stone, Material needle)
        {
            var cube = ProcArt.Mesh(PrimitiveType.Cube);
            for (int y = 0; y < level.Height; y++)
            for (int x = 0; x < level.Width; x++)
            {
                char t = level.At(x, y);
                if (t == '#')
                    Box(walls, cube, stone, new Vector3(x + 0.5f, 0.3f, y + 0.5f), new Vector3(1f, 0.6f, 1f));
                else if (t == 'T')
                    Box(trees, cube, needle, new Vector3(x + 0.5f, 0.35f, y + 0.5f), new Vector3(0.5f, 0.7f, 0.5f));
            }
        }

        static void Box(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>
        /// One cell of dry-stone wall: courses of split stones laid along the run in a running
        /// bond, each course shorter than the one below, finished with flat capstones. The
        /// stones are laid lengthways so the joints follow the wall rather than cutting across.
        /// </summary>
        static void DryStone(Transform parent, int x, int y, Material[] stone, Material lichen, float axis)
        {
            var cellRoot = new GameObject("WallCell").transform;
            cellRoot.SetParent(parent, false);
            cellRoot.localPosition = new Vector3(x + 0.5f, 0f, y + 0.5f);
            cellRoot.localRotation = Quaternion.Euler(0, axis, 0);

            int seed = x * 397 + y;
            float[] courses = { 0.27f, 0.23f, 0.19f };
            float baseY = 0f;

            for (int c = 0; c < courses.Length; c++)
            {
                float ch = courses[c] * (0.85f + ProcArt.Hash01(x, y, 30 + c) * 0.3f);
                int n = c % 2 == 1 ? 3 : 2; // running bond: joints never line up between courses
                for (int i = 0; i < n; i++)
                {
                    float w = 1f / n;
                    float px = -0.5f + w * (i + 0.5f);
                    float depth = 0.48f + ProcArt.Hash01(x, y, 40 + c * 4 + i) * 0.12f;
                    var mesh = MeshGen.Slab(new Vector3(w * 0.92f, ch, depth), seed + c * 17 + i, 0.18f, 0.86f);
                    var m = stone[(int)(ProcArt.Hash01(x, y, 50 + c * 3 + i) * 3f) % 3];
                    float yaw = (ProcArt.Hash01(x, y, 60 + c * 3 + i) - 0.5f) * 12f;
                    float sideways = (ProcArt.Hash01(x, y, 65 + c * 3 + i) - 0.5f) * 0.12f;
                    Piece(cellRoot, mesh, m, new Vector3(px, baseY, sideways), Quaternion.Euler(0, yaw, 0));
                }
                baseY += ch * 0.9f; // courses bed into each other slightly
            }

            // Capstones: flat slabs across the run, the top surface of the wall.
            for (int i = 0; i < 2; i++)
            {
                var mesh = MeshGen.Slab(new Vector3(0.47f, 0.12f, 0.56f), seed + 200 + i, 0.12f, 0.92f);
                var m = stone[(int)(ProcArt.Hash01(x, y, 70 + i) * 3f) % 3];
                Piece(cellRoot, mesh, m, new Vector3(-0.25f + i * 0.5f, baseY, 0f),
                      Quaternion.Euler(0, (ProcArt.Hash01(x, y, 80 + i) - 0.5f) * 10f, 0));
            }

            if (ProcArt.Hash01(x, y, 19) < 0.22f)
                Piece(cellRoot, MeshGen.Slab(new Vector3(0.18f, 0.02f, 0.14f), seed + 300, 0.35f, 1f), lichen,
                      new Vector3(0.1f, baseY + 0.11f, -0.1f), Quaternion.Euler(0, 24f, 0));
        }

        /// <summary>Tapered trunk with three faceted cone tiers, tilted slightly per tier.</summary>
        void Pine(Transform parent, int x, int y, Material[] needles, Material bark, Material snow)
        {
            float s = 0.78f + ProcArt.Hash01(x, y, 4) * 0.5f;
            var jitter = new Vector3((ProcArt.Hash01(x, y, 5) - .5f) * .35f, 0f, (ProcArt.Hash01(x, y, 6) - .5f) * .35f);
            var root = new GameObject("Pine").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(x + .5f, 0f, y + .5f) + jitter;
            root.localScale = Vector3.one * s;
            root.localRotation = Quaternion.Euler(0, ProcArt.Hash01(x, y, 9) * 360f, 0);

            int seed = x * 131 + y;
            var m = needles[ProcArt.Hash01(x, y, 7) < .5f ? 0 : 1];
            Piece(root, MeshGen.Prism(5, 0.075f, 0.05f, 0.3f, seed, 0.1f), bark, Vector3.zero, Quaternion.identity);

            float[] tierY = { 0.2f, 0.5f, 0.8f };
            float[] tierR = { 0.42f, 0.33f, 0.22f };
            float[] tierH = { 0.46f, 0.42f, 0.38f };
            for (int i = 0; i < 3; i++)
            {
                float tilt = (ProcArt.Hash01(x, y, 90 + i) - 0.5f) * 7f;
                Piece(root, MeshGen.Prism(6, tierR[i], 0.02f, tierH[i], seed + i, 0.12f, i * 22f), m,
                      new Vector3(0, tierY[i], 0), Quaternion.Euler(tilt, 0, tilt * 0.6f));
            }
            if (ProcArt.Hash01(x, y, 8) < 0.35f)
                Piece(root, MeshGen.Prism(6, 0.1f, 0.01f, 0.16f, seed + 7, 0.14f), snow, new Vector3(0, 1.06f, 0), Quaternion.identity);
        }

        /// <summary>Sparse pebbles and grass blades on walkable ground. Decoration only.</summary>
        static void Scatter(Transform parent, int x, int y, Material[] stone, Material[] tuft, bool inRoom)
        {
            float roll = ProcArt.Hash01(x, y, 20);
            var c = new Vector3(x + .2f + ProcArt.Hash01(x, y, 21) * .6f, 0f, y + .2f + ProcArt.Hash01(x, y, 22) * .6f);
            if (roll < (inRoom ? 0.10f : 0.22f))
            {
                var m = tuft[ProcArt.Hash01(x, y, 23) < .5f ? 0 : 1];
                for (int i = 0; i < 3; i++)
                {
                    var blade = MeshGen.Prism(3, 0.028f, 0.006f, 0.15f + ProcArt.Hash01(x, y, 24 + i) * 0.07f, x * 7 + y + i, 0.2f);
                    Piece(parent, blade, m, c + new Vector3((i - 1) * 0.055f, 0f, (ProcArt.Hash01(x, y, 27 + i) - .5f) * 0.07f),
                          Quaternion.Euler((i - 1) * 14f, ProcArt.Hash01(x, y, 31 + i) * 360f, (i - 1) * -17f));
                }
            }
            else if (roll > (inRoom ? 0.95f : 0.9f))
            {
                float sz = 0.16f + ProcArt.Hash01(x, y, 27) * 0.12f;
                var mesh = MeshGen.Slab(new Vector3(sz, sz * 0.4f, sz * 0.8f), x * 31 + y, 0.3f, 0.7f);
                Piece(parent, mesh, stone[(int)(ProcArt.Hash01(x, y, 28) * 3f) % 3], c, Quaternion.Euler(0, roll * 900f, 0));
            }
        }

        void BuildFloor(Texture2D tex, int apron)
        {
            var floor = new GameObject("Tiles");
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(_w * 0.5f, 0f, _h * 0.5f);
            floor.transform.localRotation = Quaternion.Euler(90, 0, 0);
            floor.transform.localScale = new Vector3(_w, _h, 1);
            floor.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(PrimitiveType.Quad);
            floor.AddComponent<MeshRenderer>().sharedMaterial = ProcArt.Textured(tex, Color.white);

            // The plinth sits under the apron, so its sienna edge is out beyond the scenery.
            var plinth = new GameObject("Plinth");
            plinth.transform.SetParent(transform, false);
            plinth.transform.localPosition = new Vector3(_w * 0.5f, -0.26f, _h * 0.5f);
            plinth.transform.localScale = new Vector3(_w + 2 * apron + 0.6f, 0.5f, _h + 2 * apron + 0.6f);
            plinth.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(PrimitiveType.Cube);
            plinth.AddComponent<MeshRenderer>().sharedMaterial = ProcArt.Solid(Palette.Sienna);
        }

        /// <summary>
        /// The scenery band around the map: forest-floor ground, and in the poster style a
        /// thinning stand of pines. Placeholder style keeps only the ground.
        /// </summary>
        void BuildApron(Transform root, LevelDef level, Material[] needles, Material bark, Material snow)
        {
            int a = apronCells;
            if (a <= 0) return;

            // Between the plinth's top (y = -0.01) and the map floor (y = 0), so neither
            // z-fights with it and the map quad still covers its middle.
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);
            ground.transform.localPosition = new Vector3(_w * 0.5f, -0.004f, _h * 0.5f);
            ground.transform.localRotation = Quaternion.Euler(90, 0, 0);
            ground.transform.localScale = new Vector3(_w + 2 * a, _h + 2 * a, 1);
            ground.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(PrimitiveType.Quad);
            ground.AddComponent<MeshRenderer>().sharedMaterial = ProcArt.Solid(Color.Lerp(Palette.Forest, Palette.Forest2, 0.4f));

            if (Art.Simple) return;

            var stand = new GameObject("Stand").transform;
            stand.SetParent(root, false);
            for (int y = -a; y < _h + a; y++)
            for (int x = -a; x < _w + a; x++)
            {
                if (x >= 0 && y >= 0 && x < _w && y < _h) continue;
                // Densest against the map, thinning outward, so the map reads as a clearing.
                int d = Mathf.Max(Mathf.Max(-x, x - (_w - 1)), Mathf.Max(-y, y - (_h - 1)));
                float density = Mathf.Lerp(0.75f, 0.3f, d / (float)a);
                if (ProcArt.Hash01(x + 1000, y + 1000, 3) < density)
                    Pine(stand, x, y, needles, bark, snow);
            }
            Merge(stand, root, "ApronPines");
        }

        /// <summary>
        /// Folds every piece under <paramref name="source"/> into one mesh per material under
        /// <paramref name="target"/>, then removes the pieces. The apron can hold thousands of
        /// pines, and as separate objects they would cost far more than they are worth.
        /// </summary>
        static void Merge(Transform source, Transform target, string name)
        {
            var groups = new System.Collections.Generic.Dictionary<Material, System.Collections.Generic.List<CombineInstance>>();
            var toLocal = target.worldToLocalMatrix;
            foreach (var mf in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var mat = mf.GetComponent<MeshRenderer>().sharedMaterial;
                if (!groups.TryGetValue(mat, out var list)) groups[mat] = list = new System.Collections.Generic.List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toLocal * mf.transform.localToWorldMatrix });
            }

            int i = 0;
            foreach (var kv in groups)
            {
                var mesh = new Mesh { name = $"{name}_{i}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(target, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = kv.Key;
                i++;
            }
            DestroyImmediate(source.gameObject);
        }

        /// <summary>
        /// Roots this component owns. Anything else under the same transform (the entity and
        /// phantom roots WorldView keeps, say) is somebody else's and must survive a rebuild.
        /// </summary>
        static readonly string[] Generated = { "Tiles", "Plinth", "Apron", "Walls", "Trees", "Scatter", "ScenePreview" };

        /// <summary>
        /// Hand-placed set dressing belongs under a child with this name. Nothing generates it
        /// and nothing clears it, so whatever is parked there survives into a play session.
        /// Never add this to <see cref="Generated"/>.
        /// </summary>
        public const string HandmadeRoot = "Handmade";

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var go = transform.GetChild(i).gameObject;
                if (go.name == HandmadeRoot) continue; // hand-placed work is never ours to delete
                if (System.Array.IndexOf(Generated, go.name) < 0) continue;
                // Immediate on purpose: a rebuild recreates these same names right away, and a
                // deferred Destroy would leave the old copy standing for the rest of the frame.
                DestroyImmediate(go);
            }
        }

        Transform Child(string n)
        {
            var t = new GameObject(n).transform;
            t.SetParent(transform, false);
            return t;
        }

        static GameObject Piece(Transform parent, Mesh mesh, Material mat, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        void Start()
        {
            if (transform.Find("Tiles") == null) Build(grid);
        }
    }
}
