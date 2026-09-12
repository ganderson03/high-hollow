using Crawler.Protocol;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>
    /// Static terrain: the floor quad, a sienna plinth, and (once a level is known) rounded
    /// stone walls and a pine forest built from the level's tiles. Purely visual; collision
    /// belongs to whatever simulates the world.
    /// </summary>
    public sealed class GridFloor : MonoBehaviour
    {
        [SerializeField] int grid = 32;
        [SerializeField] int texelsPerCell = 16;
        [SerializeField, Range(0f, 1f)] float treeDensity = 0.6f;

        public void Build(int size)
        {
            grid = size;
            Clear();
            BuildFloor(ProcArt.FloorTiles(grid, texelsPerCell));
        }

        public void BuildLevel(LevelDef level)
        {
            grid = level.Grid;
            Clear();
            BuildFloor(ProcArt.LevelFloor(level, texelsPerCell));

            var walls = Child("Walls");
            var trees = Child("Trees");
            var scatter = Child("Scatter");
            var grey = Palette.Boulder;

            // Warm sky, sienna-tinted ground bounce: keeps shadows from going cold.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(Palette.Cream, Palette.Ochre, 0.3f) * 0.75f;
            RenderSettings.ambientEquatorColor = Color.Lerp(Palette.Sand, Palette.Moss, 0.3f) * 0.55f;
            RenderSettings.ambientGroundColor = Palette.Sienna * 0.35f;
            var stone = new[]
            {
                ProcArt.Solid(grey),
                ProcArt.Solid(Color.Lerp(grey, Palette.MossDeep, 0.3f)),
                ProcArt.Solid(Color.Lerp(grey, Palette.Cream, 0.15f)),
            };
            var lichen = ProcArt.Solid(Color.Lerp(Palette.Ochre, Palette.Moss, 0.4f));
            var tuft = new[] { ProcArt.Solid(Palette.Moss), ProcArt.Solid(Color.Lerp(Palette.Moss, Palette.Ochre, 0.35f)) };
            var needles = new[] { ProcArt.Solid(Palette.Pine), ProcArt.Solid(Palette.Needle) };
            var bark = ProcArt.Solid(Palette.Umber);
            var snow = ProcArt.Solid(Palette.Cream);

            for (int y = 0; y < grid; y++)
            for (int x = 0; x < grid; x++)
            {
                char t = level.At(x, y);
                if (t == '#')
                {
                    Boulders(walls, x, y, stone, lichen);
                }
                else if (!LevelDef.IsSolidTile(t) && t != 'T')
                {
                    Scatter(scatter, x, y, stone, tuft, level.RegionAt(x, y) >= 0);
                }
                else if (t == 'T' && ProcArt.Hash01(x, y, 3) < treeDensity)
                {
                    Pine(trees, x, y, needles, bark, snow);
                }
            }
        }

        /// <summary>A heap of two or three squashed boulders per wall cell; low and grey, like scree.</summary>
        static void Boulders(Transform parent, int x, int y, Material[] stone, Material lichen)
        {
            var c = new Vector3(x + .5f, 0f, y + .5f);
            // Base boulder fills the cell so walls stay continuous.
            float h = 0.42f + ProcArt.Hash01(x, y, 1) * 0.22f;
            var m0 = stone[(int)(ProcArt.Hash01(x, y, 2) * 3f) % 3];
            Part(parent, PrimitiveType.Sphere, m0, c + new Vector3(0, h * .45f, 0), new Vector3(1.12f, h, 1.12f), ProcArt.Hash01(x, y, 9) * 360f);

            int extra = ProcArt.Hash01(x, y, 10) < 0.55f ? 2 : 1;
            for (int i = 0; i < extra; i++)
            {
                float a = ProcArt.Hash01(x, y, 11 + i) * Mathf.PI * 2f;
                float r = 0.18f + ProcArt.Hash01(x, y, 13 + i) * 0.15f;
                float s = 0.38f + ProcArt.Hash01(x, y, 15 + i) * 0.22f;
                var m = stone[(int)(ProcArt.Hash01(x, y, 17 + i) * 3f) % 3];
                var p = c + new Vector3(Mathf.Cos(a) * r, h * 0.8f + s * 0.25f, Mathf.Sin(a) * r);
                Part(parent, PrimitiveType.Sphere, m, p, new Vector3(s, s * 0.7f, s * 0.85f), a * Mathf.Rad2Deg);
            }
            if (ProcArt.Hash01(x, y, 19) < 0.3f)
                Part(parent, PrimitiveType.Sphere, lichen, c + new Vector3(0.1f, h * 0.9f, -0.1f), new Vector3(0.3f, 0.06f, 0.24f));
        }

        /// <summary>Sparse pebbles and grass tufts on walkable ground. Decoration only; never blocks.</summary>
        static void Scatter(Transform parent, int x, int y, Material[] stone, Material[] tuft, bool inRoom)
        {
            float roll = ProcArt.Hash01(x, y, 20);
            var c = new Vector3(x + .2f + ProcArt.Hash01(x, y, 21) * .6f, 0f, y + .2f + ProcArt.Hash01(x, y, 22) * .6f);
            if (roll < (inRoom ? 0.10f : 0.22f))
            {
                var m = tuft[ProcArt.Hash01(x, y, 23) < .5f ? 0 : 1];
                for (int i = 0; i < 3; i++)
                {
                    var o = new Vector3((i - 1) * 0.06f, 0.07f, (ProcArt.Hash01(x, y, 24 + i) - .5f) * 0.08f);
                    var go = Part(parent, PrimitiveType.Capsule, m, c + o, new Vector3(0.035f, 0.08f, 0.035f), 0f);
                    go.transform.localRotation = Quaternion.Euler((i - 1) * 18f, 0, (i - 1) * -22f);
                }
            }
            else if (roll > (inRoom ? 0.95f : 0.9f))
            {
                float s = 0.12f + ProcArt.Hash01(x, y, 27) * 0.1f;
                Part(parent, PrimitiveType.Sphere, stone[(int)(ProcArt.Hash01(x, y, 28) * 3f) % 3], c + new Vector3(0, s * 0.25f, 0), new Vector3(s, s * 0.55f, s * 0.8f), roll * 900f);
            }
        }

        void Pine(Transform parent, int x, int y, Material[] needles, Material bark, Material snow)
        {
            float s = 0.75f + ProcArt.Hash01(x, y, 4) * 0.5f;
            var jitter = new Vector3((ProcArt.Hash01(x, y, 5) - .5f) * .35f, 0f, (ProcArt.Hash01(x, y, 6) - .5f) * .35f);
            var root = new GameObject("Pine").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(x + .5f, 0f, y + .5f) + jitter;
            root.localScale = Vector3.one * s;

            var m = needles[ProcArt.Hash01(x, y, 7) < .5f ? 0 : 1];
            Part(root, PrimitiveType.Cylinder, bark, new Vector3(0, 0.12f, 0), new Vector3(0.14f, 0.12f, 0.14f));
            Part(root, PrimitiveType.Sphere, m, new Vector3(0, 0.45f, 0), new Vector3(0.85f, 0.45f, 0.85f));
            Part(root, PrimitiveType.Sphere, m, new Vector3(0, 0.85f, 0), new Vector3(0.62f, 0.38f, 0.62f));
            Part(root, PrimitiveType.Sphere, m, new Vector3(0, 1.18f, 0), new Vector3(0.38f, 0.30f, 0.38f));
            if (ProcArt.Hash01(x, y, 8) < 0.35f)
                Part(root, PrimitiveType.Sphere, snow, new Vector3(0, 1.34f, 0), new Vector3(0.2f, 0.1f, 0.2f));
        }

        void BuildFloor(Texture2D tex)
        {
            var floor = new GameObject("Tiles");
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(grid * 0.5f, 0f, grid * 0.5f);
            floor.transform.localRotation = Quaternion.Euler(90, 0, 0);
            floor.transform.localScale = new Vector3(grid, grid, 1);
            floor.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(PrimitiveType.Quad);
            floor.AddComponent<MeshRenderer>().sharedMaterial = ProcArt.Textured(tex, Color.white);

            var plinth = new GameObject("Plinth");
            plinth.transform.SetParent(transform, false);
            plinth.transform.localPosition = new Vector3(grid * 0.5f, -0.26f, grid * 0.5f);
            plinth.transform.localScale = new Vector3(grid + 0.6f, 0.5f, grid + 0.6f);
            plinth.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(PrimitiveType.Cube);
            plinth.AddComponent<MeshRenderer>().sharedMaterial = ProcArt.Solid(Palette.Sienna);
        }

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        }

        Transform Child(string n)
        {
            var t = new GameObject(n).transform;
            t.SetParent(transform, false);
            return t;
        }

        static GameObject Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale, float yaw = 0f)
        {
            var go = new GameObject(type.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(type);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        void Start()
        {
            if (transform.childCount == 0) Build(grid);
        }
    }
}
