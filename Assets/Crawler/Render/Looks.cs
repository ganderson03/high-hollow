using Crawler.Protocol;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>How a look responds to the entity's authoritative state byte. Purely cosmetic.</summary>
    public enum LookAnim { None, Sink, Tilt, Press, Bob, Pulse }

    /// <summary>
    /// A "look" is the appearance key. Keys 0..7 are the kind looks; the overlay's
    /// "sprite" field on the wire selects one of these. Unknown keys get a placeholder.
    /// Each look builds a small primitive assembly; the child named "Anim" is what
    /// LookAnim moves.
    /// </summary>
    public static class Looks
    {
        public const int Unknown = -1;

        public static int ForKind(EntityKind k) => (int)k;

        public static LookAnim AnimFor(int look)
        {
            switch ((EntityKind)look)
            {
                case EntityKind.Door: return LookAnim.Sink;
                case EntityKind.Lever: return LookAnim.Tilt;
                case EntityKind.Plate: return LookAnim.Press;
                case EntityKind.Key:
                case EntityKind.Sightstone:
                case EntityKind.Goal: return LookAnim.Bob;
                case EntityKind.Hazard: return LookAnim.Pulse;
                default: return LookAnim.None;
            }
        }

        public static GameObject Build(int look, Transform parent)
        {
            var root = new GameObject("Look_" + LookName(look));
            root.transform.SetParent(parent, false);
            var anim = new GameObject("Anim").transform;
            anim.SetParent(root.transform, false);
            root.transform.localScale = Vector3.one * ScaleFor(look);

            switch (look)
            {
                case (int)EntityKind.Player:
                    Part(anim, PrimitiveType.Capsule, ProcArt.Textured(ProcArt.Rosettes, Color.white),
                         new Vector3(0, 0.45f, 0), new Vector3(0.55f, 0.45f, 0.55f));
                    // Ears: two small spheres; reads as a round-headed cat at poster scale.
                    Part(anim, PrimitiveType.Sphere, ProcArt.Solid(Palette.Cream), new Vector3(-0.15f, 0.9f, 0), Vector3.one * 0.14f);
                    Part(anim, PrimitiveType.Sphere, ProcArt.Solid(Palette.Cream), new Vector3(0.15f, 0.9f, 0), Vector3.one * 0.14f);
                    // Long tail curled on the ground.
                    Part(anim, PrimitiveType.Capsule, ProcArt.Textured(ProcArt.Rosettes, Color.white),
                         new Vector3(0, 0.08f, -0.3f), new Vector3(0.14f, 0.28f, 0.14f), Quaternion.Euler(90, 20, 0));
                    break;

                case (int)EntityKind.Door:
                    // Arched door: slab + rounded cap, sinks into the floor when open.
                    Part(anim, PrimitiveType.Cube, ProcArt.Solid(Palette.Umber), new Vector3(0, 0.5f, 0), new Vector3(0.9f, 1f, 0.28f));
                    Part(anim, PrimitiveType.Cylinder, ProcArt.Solid(Palette.Umber), new Vector3(0, 1f, 0), new Vector3(0.9f, 0.14f, 0.9f),
                         Quaternion.Euler(90, 0, 0)).transform.localScale = new Vector3(0.9f, 0.14f, 0.9f);
                    Part(anim, PrimitiveType.Sphere, ProcArt.Solid(Palette.Ochre), new Vector3(0.28f, 0.5f, -0.16f), Vector3.one * 0.1f);
                    break;

                case (int)EntityKind.Lever:
                    Part(root.transform, PrimitiveType.Cylinder, ProcArt.Solid(Palette.Stone), new Vector3(0, 0.08f, 0), new Vector3(0.55f, 0.08f, 0.55f));
                    Part(anim, PrimitiveType.Capsule, ProcArt.Solid(Palette.Pine), new Vector3(0, 0.35f, 0), new Vector3(0.1f, 0.3f, 0.1f));
                    Part(anim, PrimitiveType.Sphere, ProcArt.Solid(Palette.Sienna), new Vector3(0, 0.66f, 0), Vector3.one * 0.18f);
                    break;

                case (int)EntityKind.Plate:
                    Part(anim, PrimitiveType.Cylinder, ProcArt.Solid(Palette.Stone), new Vector3(0, 0.04f, 0), new Vector3(0.8f, 0.04f, 0.8f));
                    Part(anim, PrimitiveType.Cylinder, ProcArt.Solid(Palette.Sienna), new Vector3(0, 0.07f, 0), new Vector3(0.5f, 0.02f, 0.5f));
                    break;

                case (int)EntityKind.Key:
                    Part(anim, PrimitiveType.Sphere, ProcArt.Glow(Palette.Ochre, 0.15f), new Vector3(0, 0.45f, 0), Vector3.one * 0.26f);
                    Part(anim, PrimitiveType.Capsule, ProcArt.Solid(Palette.Ochre), new Vector3(0, 0.45f, -0.2f), new Vector3(0.08f, 0.16f, 0.08f),
                         Quaternion.Euler(90, 0, 0));
                    break;

                case (int)EntityKind.Hazard:
                    // A dark pit with a banked sienna ember in it. Reads as "don't", not as candy.
                    Part(root.transform, PrimitiveType.Cylinder, ProcArt.Solid(Palette.Pit, 0.05f), new Vector3(0, 0.015f, 0), new Vector3(0.9f, 0.015f, 0.9f));
                    Part(anim, PrimitiveType.Sphere, ProcArt.Glow(Palette.Sienna, 0.25f), new Vector3(0, 0.06f, 0), new Vector3(0.5f, 0.14f, 0.5f));
                    break;

                case (int)EntityKind.Goal:
                    Part(root.transform, PrimitiveType.Cylinder, ProcArt.Solid(Palette.Sienna), new Vector3(0, 0.04f, 0), new Vector3(0.85f, 0.04f, 0.85f));
                    Part(root.transform, PrimitiveType.Cylinder, ProcArt.Solid(Palette.Cream), new Vector3(0, 0.07f, 0), new Vector3(0.6f, 0.02f, 0.6f));
                    Part(anim, PrimitiveType.Sphere, ProcArt.Glow(Palette.Cream, 0.2f), new Vector3(0, 0.6f, 0), new Vector3(0.4f, 0.4f, 0.4f));
                    break;

                case (int)EntityKind.Sightstone:
                    // Standing stone with a cream eye near the top.
                    Part(root.transform, PrimitiveType.Capsule, ProcArt.Solid(Palette.Stone), new Vector3(0, 0.45f, 0), new Vector3(0.3f, 0.45f, 0.3f));
                    Part(anim, PrimitiveType.Sphere, ProcArt.Glow(Palette.Cream, 0.15f), new Vector3(0, 0.62f, -0.13f), new Vector3(0.14f, 0.14f, 0.08f));
                    break;

                default:
                    Part(anim, PrimitiveType.Sphere, ProcArt.Solid(Palette.Sienna), new Vector3(0, 0.35f, 0), Vector3.one * 0.5f);
                    break;
            }
            return root;
        }

        /// <summary>Small pieces are scaled up for legibility; cell-filling pieces stay at 1.</summary>
        static float ScaleFor(int look)
        {
            switch ((EntityKind)look)
            {
                case EntityKind.Player: return 1.35f;
                case EntityKind.Lever: return 1.35f;
                case EntityKind.Key: return 1.5f;
                case EntityKind.Sightstone: return 1.3f;
                case EntityKind.Goal: return 1.15f;
                default: return 1f;
            }
        }

        static GameObject Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            var go = new GameObject(type.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(type);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static string LookName(int look) =>
            look >= 0 && look <= 7 ? ((EntityKind)look).ToString() : "Unknown" + look;
    }
}
