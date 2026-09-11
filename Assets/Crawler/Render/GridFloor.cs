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
            var stone = new[]
            {
                ProcArt.Solid(Palette.Stone),
                ProcArt.Solid(Color.Lerp(Palette.Stone, Palette.SandDeep, 0.35f)),
                ProcArt.Solid(Color.Lerp(Palette.Stone, Palette.Umber, 0.25f)),
            };
            var cap = ProcArt.Solid(Color.Lerp(Palette.Stone, Palette.Cream, 0.3f));
            var needles = new[] { ProcArt.Solid(Palette.Pine), ProcArt.Solid(Palette.Needle) };
            var bark = ProcArt.Solid(Palette.Umber);
            var snow = ProcArt.Solid(Palette.Cream);

            for (int y = 0; y < grid; y++)
            for (int x = 0; x < grid; x++)
            {
                char t = level.At(x, y);
                if (t == '#')
                {
                    // Rounded stones of slightly varied height; reads as a dry-stone wall.
                    float h = 0.55f + ProcArt.Hash01(x, y, 1) * 0.3f;
                    var m = stone[(int)(ProcArt.Hash01(x, y, 2) * 3f) % 3];
                    Part(walls, PrimitiveType.Cylinder, m, new Vector3(x + .5f, h * .5f, y + .5f), new Vector3(0.98f, h * .5f, 0.98f));
                    Part(walls, PrimitiveType.Sphere, cap, new Vector3(x + .5f, h, y + .5f), new Vector3(0.96f, 0.22f, 0.96f));
                }
                else if (t == 'T' && ProcArt.Hash01(x, y, 3) < treeDensity)
                {
                    Pine(trees, x, y, needles, bark, snow);
                }
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

        static void Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(type.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(type);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void Start()
        {
            if (transform.childCount == 0) Build(grid);
        }
    }
}
