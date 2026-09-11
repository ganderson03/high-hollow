using UnityEngine;

namespace Crawler.Render
{
    /// <summary>The 32x32 floor: one quad with a generated tile texture, plus a low sienna plinth.</summary>
    public sealed class GridFloor : MonoBehaviour
    {
        [SerializeField] int grid = 32;
        [SerializeField] int texelsPerCell = 16;

        public void Build(int size)
        {
            grid = size;
            foreach (Transform c in transform) Destroy(c.gameObject);

            var floor = new GameObject("Tiles");
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(grid * 0.5f, 0f, grid * 0.5f);
            floor.transform.localRotation = Quaternion.Euler(90, 0, 0);
            floor.transform.localScale = new Vector3(grid, grid, 1);
            floor.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(PrimitiveType.Quad);
            floor.AddComponent<MeshRenderer>().sharedMaterial =
                ProcArt.Textured(ProcArt.FloorTiles(grid, texelsPerCell), Color.white);

            var plinth = new GameObject("Plinth");
            plinth.transform.SetParent(transform, false);
            plinth.transform.localPosition = new Vector3(grid * 0.5f, -0.26f, grid * 0.5f);
            plinth.transform.localScale = new Vector3(grid + 0.6f, 0.5f, grid + 0.6f);
            plinth.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(PrimitiveType.Cube);
            plinth.AddComponent<MeshRenderer>().sharedMaterial = ProcArt.Solid(Palette.Sienna);
        }

        void Start()
        {
            if (transform.childCount == 0) Build(grid);
        }
    }
}
