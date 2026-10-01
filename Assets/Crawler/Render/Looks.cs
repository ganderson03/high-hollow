using Crawler.Protocol;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>How a look responds to the entity's authoritative state byte. Purely cosmetic.</summary>
    public enum LookAnim { None, Sink, Tilt, Press, Bob, Pulse, Critter }

    /// <summary>
    /// A "look" is the appearance key. Keys 0..7 are the kind looks; the overlay's
    /// "sprite" field on the wire selects one of these. Unknown keys get a placeholder.
    ///
    /// Everything is built from flat-shaded procedural meshes (see MeshGen). The props are
    /// deliberately graphic: bold cut shapes with shallow relief, closer to poster iconography
    /// than to modelled objects, so they stay readable from the game camera. The child named
    /// "Anim" is what LookAnim moves.
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

        public static GameObject Build(int look, Transform parent, Color? accent = null,
                                       int cellX = int.MinValue, int cellY = int.MinValue)
        {
            var root = new GameObject("Look_" + LookName(look));
            root.transform.SetParent(parent, false);
            var anim = new GameObject("Anim").transform;
            anim.SetParent(root.transform, false);
            root.transform.localScale = Vector3.one * ScaleFor(look);

            // A door stands in a gap in a wall, so it faces whichever way that wall runs. Without
            // this every door is built facing the same way and half of them stand side-on in the
            // opening. Cells are only known to callers holding a snapshot or a level.
            if (look == (int)EntityKind.Door && cellX != int.MinValue && WorldMap.Known)
                root.transform.localRotation = Quaternion.Euler(0, WorldMap.AxisAt(cellX, cellY), 0);

            if (Art.Simple)
            {
                BuildPlaceholder(look, root.transform, anim, accent);
                return root;
            }

            switch (look)
            {
                case (int)EntityKind.Player:
                    BuildSnowLeopard(anim, accent);
                    break;

                case (int)EntityKind.Door:
                    BuildDoor(anim);
                    break;

                case (int)EntityKind.Lever:
                    // Cut base plate stays put; the flat arm above it tilts.
                    Flat(root.transform, "leverbase", MeshGen.Polygon(6, 0.34f), 0.07f, ProcArt.Solid(Palette.Stone),
                         new Vector3(0, 0.042f, 0), Quaternion.Euler(90, 0, 0));
                    Piece(anim, MeshGen.Slab(new Vector3(0.1f, 0.42f, 0.1f), 11, 0.05f, 0.9f), ProcArt.Solid(Palette.Pine),
                          new Vector3(0, 0.07f, 0), Quaternion.identity);
                    Flat(anim, "leverknob", MeshGen.Polygon(6, 0.15f), 0.1f, ProcArt.Solid(Palette.Sienna),
                         new Vector3(0, 0.55f, 0), Quaternion.identity);
                    break;

                case (int)EntityKind.Plate:
                    Flat(anim, "plate", MeshGen.Polygon(8, 0.42f, 22.5f), 0.06f, ProcArt.Solid(Palette.Stone),
                         new Vector3(0, 0.037f, 0), Quaternion.Euler(90, 0, 0));
                    Flat(anim, "plateinner", MeshGen.Polygon(8, 0.26f, 22.5f), 0.04f, ProcArt.Solid(Palette.Sienna),
                         new Vector3(0, 0.07f, 0), Quaternion.Euler(90, 0, 0));
                    break;

                case (int)EntityKind.Key:
                    BuildKey(anim);
                    break;

                case (int)EntityKind.Hazard:
                    BuildHazard(root.transform, anim);
                    break;

                case (int)EntityKind.Goal:
                    Flat(root.transform, "goalbase", MeshGen.Polygon(8, 0.44f, 22.5f), 0.05f, ProcArt.Solid(Palette.Sienna),
                         new Vector3(0, 0.032f, 0), Quaternion.Euler(90, 0, 0));
                    Flat(root.transform, "goalinner", MeshGen.Polygon(8, 0.3f, 22.5f), 0.03f, ProcArt.Solid(Palette.Cream),
                         new Vector3(0, 0.06f, 0), Quaternion.Euler(90, 0, 0));
                    // A cut diamond hanging over the summit marker, rather than a ball.
                    Flat(anim, "goalmark", MeshGen.Polygon(4, 0.24f), 0.08f, ProcArt.Glow(Palette.Cream, 0.2f),
                         new Vector3(0, 0.42f, 0), Quaternion.Euler(0, 0, 0));
                    break;

                case (int)EntityKind.Sightstone:
                    // A standing cut stone: tapered five-sided slab, with an almond eye.
                    Piece(root.transform, MeshGen.Prism(5, 0.26f, 0.13f, 0.86f, 5, 0.12f), ProcArt.Solid(Palette.Stone),
                          Vector3.zero, Quaternion.Euler(0, 18f, 0));
                    Piece(root.transform, MeshGen.Prism(5, 0.2f, 0.19f, 0.05f, 6, 0.1f), ProcArt.Solid(Palette.Cream),
                          new Vector3(0, 0.44f, 0), Quaternion.Euler(0, 18f, 0)); // carved band
                    Flat(anim, "eye", new[]
                    {
                        new Vector2(-0.15f, 0f), new Vector2(0f, 0.1f), new Vector2(0.15f, 0f), new Vector2(0f, -0.1f)
                    }, 0.06f, ProcArt.Glow(Palette.Cream, 0.15f), new Vector3(0, 0.63f, -0.17f), Quaternion.identity);
                    break;

                default:
                    Flat(anim, "unknown", MeshGen.Polygon(6, 0.3f), 0.12f, ProcArt.Solid(Palette.Sienna),
                         new Vector3(0, 0.35f, 0), Quaternion.identity);
                    break;
            }
            return root;
        }

        /// <summary>Plank door: a cut arch panel with two iron bands and a round handle.</summary>
        static void BuildDoor(Transform anim)
        {
            var wood = ProcArt.Solid(Palette.Umber);
            var iron = ProcArt.Solid(Color.Lerp(Palette.Night, Palette.Stone, 0.35f));

            // Arch outline, drawn once as a convex fan: straight jambs, faceted head.
            var arch = new[]
            {
                new Vector2(-0.45f, 0f), new Vector2(0.45f, 0f), new Vector2(0.45f, 0.62f),
                new Vector2(0.32f, 0.88f), new Vector2(0f, 0.98f), new Vector2(-0.32f, 0.88f),
                new Vector2(-0.45f, 0.62f),
            };
            Flat(anim, "door", arch, 0.3f, wood, Vector3.zero, Quaternion.identity);
            foreach (float by in new[] { 0.24f, 0.66f })
                Piece(anim, MeshGen.Slab(new Vector3(0.86f, 0.06f, 0.26f), 3, 0.04f, 1f), iron, new Vector3(0, by, 0), Quaternion.identity);
            Flat(anim, "handle", MeshGen.Polygon(6, 0.07f), 0.06f, ProcArt.Solid(Palette.Ochre), new Vector3(0.28f, 0.45f, -0.18f), Quaternion.identity);
        }

        /// <summary>Key: a flat ring bow, a shank and two teeth. Reads as a key from above.</summary>
        static void BuildKey(Transform anim)
        {
            var gold = ProcArt.Solid(Palette.Ochre);
            var lit = ProcArt.Glow(Palette.Ochre, 0.15f);
            var key = new GameObject("KeyBody").transform;
            key.SetParent(anim, false);
            key.localPosition = new Vector3(0, 0.45f, 0);
            key.localRotation = Quaternion.Euler(90, 0, 0); // lie flat, face the camera

            Piece(key, MeshGen.Ring(0.15f, 0.045f, 10, 5), lit, new Vector3(0, 0f, 0.12f), Quaternion.identity);
            Piece(key, MeshGen.Slab(new Vector3(0.06f, 0.06f, 0.36f), 8, 0.03f, 1f), gold, new Vector3(-0.03f, -0.03f, -0.22f), Quaternion.identity);
            Piece(key, MeshGen.Slab(new Vector3(0.14f, 0.05f, 0.06f), 9, 0.03f, 1f), gold, new Vector3(0.03f, -0.03f, -0.3f), Quaternion.identity);
            Piece(key, MeshGen.Slab(new Vector3(0.1f, 0.05f, 0.06f), 10, 0.03f, 1f), gold, new Vector3(0.01f, -0.03f, -0.18f), Quaternion.identity);
        }

        /// <summary>
        /// Hazard: an open shaft. Concentric rings step inward and darken to near black, with a
        /// broken stone lip around the mouth. No marking at all; dark and deep is the warning.
        ///
        /// The floor is one unbroken quad, so anything below y=0 would be hidden behind it. The
        /// depth is therefore faked above the floor: each ring sits a hair higher than the one
        /// outside it (which also keeps them from z-fighting) and the shading does the rest.
        /// </summary>
        static void BuildHazard(Transform root, Transform anim)
        {
            // Mouth, then three steps down into the dark.
            var steps = new[]
            {
                (r: 0.46f, y: 0.010f, t: 0.00f),
                (r: 0.37f, y: 0.016f, t: 0.35f),
                (r: 0.28f, y: 0.022f, t: 0.65f),
                (r: 0.19f, y: 0.028f, t: 1.00f),
            };
            var deep = new Color(0.04f, 0.035f, 0.03f);
            for (int i = 0; i < steps.Length; i++)
            {
                var (r, y, t) = steps[i];
                var shade = ProcArt.Solid(Color.Lerp(Palette.Pit, deep, t), 0.02f);
                var host = i == steps.Length - 1 ? anim : root; // the floor of the shaft breathes
                Flat(host, "shaft" + i, MeshGen.Polygon(8, r, 22.5f), 0.02f, shade,
                     new Vector3(0, y, 0), Quaternion.Euler(90, 0, 0));
            }

            // Broken lip: stones crowding the mouth, lower and looser than a wall course.
            for (int i = 0; i < 7; i++)
            {
                float a = (i / 7f) * Mathf.PI * 2f + 0.3f;
                float d = 0.47f + ProcArt.Hash01(i, 3, 12) * 0.05f;
                float w = 0.14f + ProcArt.Hash01(i, 4, 13) * 0.1f;
                var stone = ProcArt.Solid(Color.Lerp(Palette.Boulder, Palette.Pit, 0.15f + ProcArt.Hash01(i, 5, 14) * 0.25f));
                Piece(root, MeshGen.Slab(new Vector3(w, 0.05f + ProcArt.Hash01(i, 6, 15) * 0.05f, 0.12f), 60 + i, 0.3f, 0.8f),
                      stone, new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d),
                      Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f, 0));
            }
        }

        /// <summary>
        /// Stylized snow leopard, facing local +Z, built from faceted blocks and tapered prisms:
        /// a long low body, a short neck carrying a small blocky head, four stubby legs, and a
        /// thick tail laid along a curve that trails low and lifts at the tip. The "Tail" pivot
        /// is swayed by EntityView. The optional accent is a scarf wound around the neck.
        /// </summary>
        static void BuildSnowLeopard(Transform anim, Color? accent)
        {
            var fur = ProcArt.Fur;
            var cream = ProcArt.Solid(Palette.Cream);
            var dark = ProcArt.Solid(Palette.Rosette);

            // Body: long and low, a little narrower on top, with a haunch block behind.
            Piece(anim, MeshGen.Slab(new Vector3(0.28f, 0.2f, 0.56f), 21, 0.07f, 0.84f), fur, new Vector3(0, 0.17f, -0.02f), Quaternion.identity);
            Piece(anim, MeshGen.Slab(new Vector3(0.29f, 0.22f, 0.22f), 22, 0.09f, 0.8f), fur, new Vector3(0, 0.16f, -0.22f), Quaternion.Euler(0, 5f, 0));
            Piece(anim, MeshGen.Slab(new Vector3(0.17f, 0.07f, 0.34f), 23, 0.05f, 0.95f), cream, new Vector3(0, 0.115f, 0.03f), Quaternion.identity);

            // Legs: short tapered prisms with a flat cut paw under each.
            foreach (var (lx, lz) in new[] { (-0.095f, 0.17f), (0.095f, 0.17f), (-0.095f, -0.18f), (0.095f, -0.18f) })
            {
                Piece(anim, MeshGen.Prism(5, 0.055f, 0.045f, 0.16f, (int)(lx * 100) + (int)(lz * 10), 0.08f), fur, new Vector3(lx, 0.02f, lz), Quaternion.identity);
                Piece(anim, MeshGen.Slab(new Vector3(0.1f, 0.035f, 0.12f), (int)(lz * 50) + 3, 0.08f, 0.9f), cream, new Vector3(lx, 0f, lz + 0.015f), Quaternion.identity);
            }

            // Neck: a short block bridging shoulders and head, so the scarf has something to sit on.
            Piece(anim, MeshGen.Prism(5, 0.085f, 0.075f, 0.16f, 25, 0.06f), fur, new Vector3(0, 0.27f, 0.2f), Quaternion.Euler(38f, 0, 0));

            // Head: small block, wedge muzzle, flat ears and eyes.
            var head = new GameObject("Head").transform;
            head.SetParent(anim, false);
            head.localPosition = new Vector3(0, 0.42f, 0.29f);
            Piece(head, MeshGen.Slab(new Vector3(0.21f, 0.17f, 0.2f), 31, 0.06f, 0.88f), fur, new Vector3(0, -0.085f, 0), Quaternion.identity);
            Piece(head, MeshGen.Slab(new Vector3(0.12f, 0.07f, 0.1f), 32, 0.05f, 0.95f), cream, new Vector3(0, -0.055f, 0.12f), Quaternion.identity);
            Flat(head, "nose", MeshGen.Polygon(3, 0.022f, 90f), 0.02f, ProcArt.Solid(Palette.Umber), new Vector3(0, -0.015f, 0.17f), Quaternion.identity);
            foreach (float ex in new[] { -0.055f, 0.055f })
            {
                Flat(head, "cateye", MeshGen.Polygon(4, 0.022f), 0.015f, ProcArt.Solid(Palette.Night), new Vector3(ex, 0f, 0.102f), Quaternion.identity);
                Flat(head, "ear", MeshGen.Polygon(3, 0.05f, 90f), 0.025f, dark, new Vector3(ex * 1.5f, 0.045f, -0.02f), Quaternion.Euler(0, ex > 0 ? 22f : -22f, 0));
                Flat(head, "earin", MeshGen.Polygon(3, 0.028f, 90f), 0.015f, cream, new Vector3(ex * 1.5f, 0.042f, 0.005f), Quaternion.Euler(0, ex > 0 ? 22f : -22f, 0));
            }

            // Tail: segments laid nose-to-tail along a curve, each aimed at the next point.
            var tail = new GameObject("Tail").transform;
            tail.SetParent(anim, false);
            tail.localPosition = new Vector3(0, 0.22f, -0.28f);
            var path = new[]
            {
                new Vector3(0f, 0.00f, 0f),
                new Vector3(0f, -0.04f, -0.11f),
                new Vector3(0f, -0.06f, -0.22f),
                new Vector3(0f, -0.03f, -0.31f),
                new Vector3(0f, 0.05f, -0.35f),
                new Vector3(0f, 0.14f, -0.33f),
            };
            for (int i = 0; i < path.Length - 1; i++)
            {
                var dir = path[i + 1] - path[i];
                float r = Mathf.Lerp(0.072f, 0.04f, i / (float)(path.Length - 2));
                float r2 = Mathf.Lerp(0.072f, 0.04f, (i + 1) / (float)(path.Length - 2));
                Piece(tail, MeshGen.Prism(5, r, r2, dir.magnitude * 1.08f, 40 + i, 0.07f), fur,
                      path[i], Quaternion.FromToRotation(Vector3.up, dir.normalized));
            }
            var tip = path[path.Length - 1] - path[path.Length - 2];
            Piece(tail, MeshGen.Prism(5, 0.04f, 0.018f, 0.08f, 49, 0.1f), dark,
                  path[path.Length - 1], Quaternion.FromToRotation(Vector3.up, tip.normalized));

            // Scarf: a ring wound right around the neck, so it reads as worn rather than bibbed,
            // with a knot and a short end hanging down one side.
            if (accent.HasValue)
            {
                var cloth = ProcArt.Solid(accent.Value);
                var neck = new GameObject("Scarf").transform;
                neck.SetParent(anim, false);
                neck.localPosition = new Vector3(0, 0.305f, 0.185f);
                // The neck leans forward; tip the ring's axis to match so it encircles it.
                neck.localRotation = Quaternion.Euler(38f, 0, 0);
                Piece(neck, MeshGen.Ring(0.082f, 0.028f, 12, 5), cloth, Vector3.zero, Quaternion.identity);
                Piece(neck, MeshGen.Slab(new Vector3(0.06f, 0.045f, 0.06f), 51, 0.1f, 0.9f), cloth, new Vector3(0.062f, -0.02f, 0.02f), Quaternion.Euler(0, 20f, 0));
                Piece(neck, MeshGen.Prism(4, 0.035f, 0.024f, 0.15f, 52, 0.12f), cloth, new Vector3(0.075f, -0.05f, 0.02f), Quaternion.Euler(-38f, 35f, 14f));
            }
        }

        /// <summary>
        /// One flat-colored block per kind, built from stock primitives. Same pivots, same
        /// "Anim" child and same animation hooks as the real looks, so rules and timing behave
        /// identically; only the shapes are throwaway.
        /// </summary>
        static void BuildPlaceholder(int look, Transform root, Transform anim, Color? accent)
        {
            switch ((EntityKind)look)
            {
                case EntityKind.Player:
                    PlaceholderLeopard(anim, accent);
                    break;

                case EntityKind.Door:
                    Block(anim, Palette.Umber, new Vector3(0, 0.5f, 0), new Vector3(0.9f, 1f, 0.3f));
                    break;

                case EntityKind.Lever:
                    Block(root, Palette.Stone, new Vector3(0, 0.03f, 0), new Vector3(0.5f, 0.06f, 0.5f));
                    Block(anim, Palette.Pine, new Vector3(0, 0.3f, 0), new Vector3(0.12f, 0.6f, 0.12f));
                    break;

                case EntityKind.Plate:
                    Block(anim, Palette.Stone, new Vector3(0, 0.03f, 0), new Vector3(0.8f, 0.06f, 0.8f));
                    break;

                case EntityKind.Key:
                    Block(anim, Palette.Ochre, new Vector3(0, 0.4f, 0), new Vector3(0.25f, 0.25f, 0.25f));
                    break;

                case EntityKind.Hazard:
                    Block(root, Palette.Pit, new Vector3(0, 0.02f, 0), new Vector3(0.9f, 0.04f, 0.9f));
                    break;

                case EntityKind.Goal:
                    Block(anim, Palette.Cream, new Vector3(0, 0.35f, 0), new Vector3(0.6f, 0.7f, 0.6f));
                    break;

                case EntityKind.Sightstone:
                    Block(root, Palette.Stone, new Vector3(0, 0.45f, 0), new Vector3(0.35f, 0.9f, 0.35f));
                    break;

                default:
                    Block(anim, Palette.Sienna, new Vector3(0, 0.3f, 0), Vector3.one * 0.4f);
                    break;
            }
        }

        /// <summary>
        /// A leopard built out of nothing but boxes, sharing the poster model's proportions and
        /// its parts: body, haunch, four legs, neck, head with muzzle, eyes and ears, a scarf in
        /// the seat's color and a tail that wags. It builds as fast as the cube it replaces and
        /// still reads as an animal, and which way it faces, from the game camera.
        /// </summary>
        static void PlaceholderLeopard(Transform anim, Color? accent)
        {
            var fur = Palette.Sand;
            var cream = Palette.Cream;
            var paw = Color.Lerp(Palette.Sand, Palette.Umber, 0.25f);

            // Trunk: a deep chest and a heavy haunch over a short barrel. The first
            // version was one long even tube on thin legs, which read as a ferret.
            Block(anim, fur, new Vector3(0f, 0.28f, -0.17f), new Vector3(0.29f, 0.23f, 0.17f)); // haunch
            Block(anim, fur, new Vector3(0f, 0.27f, -0.01f), new Vector3(0.26f, 0.21f, 0.27f)); // barrel
            Block(anim, fur, new Vector3(0f, 0.28f, 0.145f), new Vector3(0.27f, 0.22f, 0.15f)); // chest
            Block(anim, cream, new Vector3(0f, 0.19f, -0.01f), new Vector3(0.18f, 0.06f, 0.24f)); // belly

            // Thick legs, set well under the body.
            foreach (var leg in new[] { new Vector2(-0.09f, 0.13f), new Vector2(0.09f, 0.13f), new Vector2(-0.095f, -0.145f), new Vector2(0.095f, -0.145f) })
                Block(anim, paw, new Vector3(leg.x, 0.08f, leg.y), new Vector3(0.085f, 0.16f, 0.085f));

            // A neck tall enough to wear something, then the head above it.
            Block(anim, fur, new Vector3(0f, 0.375f, 0.2f), new Vector3(0.155f, 0.145f, 0.145f)); // neck

            // The scarf rings the neck and tucks under the chin: wider than the head so
            // it shows from every side, since it is the only thing telling the seats apart.
            if (accent.HasValue)
                Block(anim, accent.Value, new Vector3(0f, 0.387f, 0.2f), new Vector3(0.24f, 0.085f, 0.2f));

            Block(anim, fur, new Vector3(0f, 0.5f, 0.247f), new Vector3(0.213f, 0.17f, 0.196f)); // head
            Block(anim, cream, new Vector3(0f, 0.468f, 0.353f), new Vector3(0.119f, 0.077f, 0.068f)); // muzzle
            Block(anim, Palette.Umber, new Vector3(0f, 0.497f, 0.391f), new Vector3(0.051f, 0.038f, 0.017f)); // nose
            foreach (float ex in new[] { -0.055f, 0.055f })
            {
                Block(anim, Palette.Night, new Vector3(ex, 0.523f, 0.343f), new Vector3(0.031f, 0.022f, 0.017f));
                Block(anim, Palette.Rosette, new Vector3(ex * 1.3909f, 0.6035f, 0.2295f), new Vector3(0.068f, 0.064f, 0.03f));
            }

            // Named 'Tail' so the Critter animation finds it and wags it about this pivot.
            var tail = new GameObject("Tail").transform;
            tail.SetParent(anim, false);
            tail.localPosition = new Vector3(0f, 0.357f, -0.238f);
            Block(tail, fur, new Vector3(0f, 0f, -0.068f), new Vector3(0.085f, 0.085f, 0.17f));
            Block(tail, fur, new Vector3(0f, 0.06f, -0.196f), new Vector3(0.077f, 0.077f, 0.145f));
            Block(tail, cream, new Vector3(0f, 0.11f, -0.2975f), new Vector3(0.068f, 0.068f, 0.085f));
        }

        static void Block(Transform parent, Color c, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject("Block");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = ProcArt.Mesh(PrimitiveType.Cube);
            go.AddComponent<MeshRenderer>().sharedMaterial = ProcArt.Solid(c);
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

        static GameObject Flat(Transform parent, string key, Vector2[] outline, float thickness, Material mat, Vector3 pos, Quaternion rot) =>
            Piece(parent, MeshGen.Extrude(key, outline, thickness), mat, pos, rot);

        static string LookName(int look) =>
            look >= 0 && look <= 7 ? ((EntityKind)look).ToString() : "Unknown" + look;
    }
}
