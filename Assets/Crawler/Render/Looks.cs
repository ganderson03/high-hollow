using Crawler.Protocol;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>How a look responds to the entity's authoritative state byte. Purely cosmetic.</summary>
    public enum LookAnim { None, Sink, Tilt, Press, Bob, Pulse, Critter }

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
                case EntityKind.Player: return LookAnim.Critter;
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

        public static GameObject Build(int look, Transform parent, Color? accent = null)
        {
            var root = new GameObject("Look_" + LookName(look));
            root.transform.SetParent(parent, false);
            var anim = new GameObject("Anim").transform;
            anim.SetParent(root.transform, false);
            root.transform.localScale = Vector3.one * ScaleFor(look);

            switch (look)
            {
                case (int)EntityKind.Player:
                    BuildSnowLeopard(anim, accent);
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

        /// <summary>
        /// Stylized snow leopard, facing local +Z: low rosette body, round head with a cream
        /// muzzle, four stubby legs, and the long thick tail curling up behind. The "Tail"
        /// pivot is swayed by EntityView; the optional accent is a scarf (per-player color).
        /// </summary>
        static void BuildSnowLeopard(Transform anim, Color? accent)
        {
            var fur = ProcArt.Fur;
            var cream = ProcArt.Solid(Palette.Cream);
            var dark = ProcArt.Solid(Palette.Rosette);

            // Body and chest: low-slung.
            Part(anim, PrimitiveType.Capsule, fur, new Vector3(0, 0.31f, -0.02f), new Vector3(0.34f, 0.29f, 0.34f), Quaternion.Euler(90, 0, 0));
            Part(anim, PrimitiveType.Sphere, fur, new Vector3(0, 0.34f, 0.19f), new Vector3(0.36f, 0.34f, 0.34f));
            Part(anim, PrimitiveType.Sphere, cream, new Vector3(0, 0.26f, 0.22f), new Vector3(0.26f, 0.2f, 0.22f)); // pale belly/chest

            // Legs: short and stout, big soft paws.
            foreach (var (lx, lz) in new[] { (-0.1f, 0.18f), (0.1f, 0.18f), (-0.1f, -0.2f), (0.1f, -0.2f) })
            {
                Part(anim, PrimitiveType.Capsule, fur, new Vector3(lx, 0.1f, lz), new Vector3(0.11f, 0.1f, 0.11f));
                Part(anim, PrimitiveType.Sphere, cream, new Vector3(lx, 0.035f, lz + 0.025f), new Vector3(0.12f, 0.065f, 0.14f)); // paws
            }

            // Head.
            var head = new GameObject("Head").transform;
            head.SetParent(anim, false);
            head.localPosition = new Vector3(0, 0.52f, 0.36f);
            Part(head, PrimitiveType.Sphere, fur, Vector3.zero, new Vector3(0.3f, 0.27f, 0.28f));
            Part(head, PrimitiveType.Sphere, cream, new Vector3(0, -0.05f, 0.12f), new Vector3(0.17f, 0.12f, 0.12f)); // muzzle
            Part(head, PrimitiveType.Sphere, ProcArt.Solid(Palette.Umber), new Vector3(0, -0.02f, 0.18f), new Vector3(0.05f, 0.035f, 0.035f)); // nose
            Part(head, PrimitiveType.Sphere, ProcArt.Solid(Palette.Night), new Vector3(-0.07f, 0.03f, 0.125f), new Vector3(0.04f, 0.045f, 0.03f));
            Part(head, PrimitiveType.Sphere, ProcArt.Solid(Palette.Night), new Vector3(0.07f, 0.03f, 0.125f), new Vector3(0.04f, 0.045f, 0.03f));
            // Ears: small, rounded, set wide and low (snow leopard, not bear): cream with a dark rim.
            foreach (float ex in new[] { -0.105f, 0.105f })
            {
                Part(head, PrimitiveType.Sphere, dark, new Vector3(ex, 0.1f, -0.015f), new Vector3(0.07f, 0.06f, 0.04f));
                Part(head, PrimitiveType.Sphere, cream, new Vector3(ex, 0.098f, 0.0f), new Vector3(0.05f, 0.042f, 0.03f));
            }

            // Tail: long and thick, trailing low behind and curling up only at the end.
            // Overlapping segments (spacing < radius) so it reads as one tail, not beads.
            var tail = new GameObject("Tail").transform;
            tail.SetParent(anim, false);
            tail.localPosition = new Vector3(0, 0.34f, -0.3f);
            var segs = new[]
            {
                (new Vector3(0.00f,  0.00f, -0.04f), 0.155f),
                (new Vector3(0.01f, -0.03f, -0.12f), 0.15f),
                (new Vector3(0.03f, -0.05f, -0.20f), 0.145f),
                (new Vector3(0.06f, -0.05f, -0.28f), 0.14f),
                (new Vector3(0.10f, -0.02f, -0.34f), 0.135f),
                (new Vector3(0.15f,  0.04f, -0.37f), 0.13f),
                (new Vector3(0.19f,  0.12f, -0.36f), 0.125f),
                (new Vector3(0.22f,  0.20f, -0.32f), 0.12f),
            };
            foreach (var (p, r) in segs) Part(tail, PrimitiveType.Sphere, fur, p, Vector3.one * r);
            Part(tail, PrimitiveType.Sphere, dark, new Vector3(0.23f, 0.27f, -0.27f), Vector3.one * 0.12f); // dark tail tip

            // Scarf: a clear band at the neck, between chest and head, with a short end hanging off one side.
            if (accent.HasValue)
            {
                var scarf = ProcArt.Solid(accent.Value);
                Part(anim, PrimitiveType.Cylinder, scarf, new Vector3(0, 0.46f, 0.29f), new Vector3(0.36f, 0.045f, 0.34f), Quaternion.Euler(-35, 0, 0));
                Part(anim, PrimitiveType.Capsule, scarf, new Vector3(0.15f, 0.37f, 0.3f), new Vector3(0.08f, 0.12f, 0.05f), Quaternion.Euler(0, 0, 20));
            }
        }

        /// <summary>Small pieces are scaled up for legibility; cell-filling pieces stay at 1.</summary>
        static float ScaleFor(int look)
        {
            switch ((EntityKind)look)
            {
                case EntityKind.Player: return 1.15f;
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
