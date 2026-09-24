using System.Collections.Generic;
using System.IO;
using Crawler.Protocol;
using Crawler.Render;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Crawler.EditorTools
{
    /// <summary>
    /// Builds the level's terrain and a prop for every entity glyph straight into the open scene,
    /// so the world can be inspected in the Scene view without entering play mode. Purely a
    /// viewing aid: at runtime the real world is still built from snapshots.
    /// </summary>
    static class SceneEnvironment
    {
        const string GenDir = "Assets/Crawler/Generated";
        const string PreviewName = "ScenePreview";
        static readonly string[] Built = { "Tiles", "Plinth", "Walls", "Trees", "Scatter", PreviewName };

        [MenuItem("Crawler/Build Environment in Scene %#e")]
        static void Build()
        {
            var level = LoadLevel(out var err);
            if (level == null)
            {
                EditorUtility.DisplayDialog("Crawler", err, "ok");
                return;
            }

            // Build under whichever object already owns the component, so the scene's own
            // wiring (Bootstrap's floor reference) keeps pointing at the same place.
            var floor = Object.FindFirstObjectByType<GridFloor>();
            if (floor == null)
            {
                EditorUtility.DisplayDialog("Crawler", "no GridFloor in the scene", "ok");
                return;
            }
            var world = floor.gameObject;

            // Clear last run's generated assets FIRST, then drop the art caches that point at
            // them: deleting those assets destroys the very objects the caches hold, and a
            // destroyed material renders magenta.
            if (AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.DeleteAsset(GenDir);
            Directory.CreateDirectory(GenDir);
            AssetDatabase.Refresh();
            ProcArt.ClearCache();
            MeshGen.ClearCache();

            GridSpace.Width = level.Width;
            GridSpace.Height = level.Height;
            floor.BuildLevel(level);
            BuildProps(world.transform, level);
            int saved = PersistGeneratedAssets(world.transform);

            EditorSceneManager.MarkSceneDirty(world.scene);
            Selection.activeGameObject = world;
            SceneView.FrameLastActiveSceneView();
            Debug.Log($"[Scene] built '{level.Id}' ({level.Width}x{level.Height}); saved {saved} generated assets. " +
                      "Delete the World children before a playtest if you do not want the static copy.");
        }

        [MenuItem("Crawler/Art Style/Poster")]
        static void StylePoster() => SetStyle(ArtStyle.Poster);

        [MenuItem("Crawler/Art Style/Placeholder")]
        static void StylePlaceholder() => SetStyle(ArtStyle.Placeholder);

        [MenuItem("Crawler/Art Style/Poster", true)]
        static bool StylePosterCheck() { Menu.SetChecked("Crawler/Art Style/Poster", Art.Style == ArtStyle.Poster); return true; }

        [MenuItem("Crawler/Art Style/Placeholder", true)]
        static bool StylePlaceholderCheck() { Menu.SetChecked("Crawler/Art Style/Placeholder", Art.Simple); return true; }

        /// <summary>
        /// Switches the style and rebuilds. Note this only changes what the editor builds into
        /// the scene; what a play session uses comes from Bootstrap's own artStyle field.
        /// </summary>
        static void SetStyle(ArtStyle style)
        {
            Art.Style = style;
            Build();
        }

        [MenuItem("Crawler/Clear Environment from Scene")]
        static void Clear()
        {
            var floor = Object.FindFirstObjectByType<GridFloor>();
            if (floor == null) return;
            var world = floor.transform;
            for (int i = world.childCount - 1; i >= 0; i--)
            {
                var c = world.GetChild(i).gameObject;
                // Only the roots the builder makes; everything else belongs to the scene.
                if (System.Array.IndexOf(Built, c.name) < 0) continue;
                Undo.DestroyObjectImmediate(c);
            }
            EditorSceneManager.MarkSceneDirty(floor.gameObject.scene);
        }

        const string LevelPref = "Crawler.SceneLevel";

        /// <summary>The level the Scene view shows; remembered between editor sessions.</summary>
        static string SceneLevelId
        {
            get => EditorPrefs.GetString(LevelPref, "hollow");
            set => EditorPrefs.SetString(LevelPref, value);
        }

        [MenuItem("Crawler/Next Level in Scene %#n")]
        static void NextLevel()
        {
            var trailPath = Path.Combine(Application.streamingAssetsPath, "levels", "trail.json");
            if (!File.Exists(trailPath)) { Debug.LogWarning("[Scene] no trail.json"); return; }
            var trail = TrailCodec.Parse(File.ReadAllText(trailPath));
            var all = new List<string>();
            foreach (var b in trail.Biomes) all.AddRange(b.Levels);
            if (all.Count == 0) return;
            int i = all.IndexOf(SceneLevelId);
            SceneLevelId = all[(i + 1) % all.Count];
            Build();
        }

        static LevelDef LoadLevel(out string error)
        {
            error = null;
            string id = SceneLevelId;
            var path = Path.Combine(Application.streamingAssetsPath, "levels", id + ".json");
            if (!File.Exists(path)) { error = "no level at " + path; return null; }
            try { return LevelCodec.Parse(File.ReadAllText(path)); }
            catch (System.Exception e) { error = e.Message; return null; }
        }

        /// <summary>One prop per entity glyph, using the same looks the runtime views use.</summary>
        static void BuildProps(Transform world, LevelDef level)
        {
            var old = world.Find(PreviewName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(PreviewName).transform;
            root.SetParent(world, false);

            var scarves = new[] { Palette.Sienna, Palette.Pine, Palette.Ochre };
            int player = 0;
            for (int y = 0; y < level.Height; y++)
            for (int x = 0; x < level.Width; x++)
            {
                char t = level.At(x, y);
                EntityKind kind;
                Color? accent = null;
                switch (t)
                {
                    case 'P': case '1': case '2':
                        kind = EntityKind.Player;
                        accent = scarves[player++ % scarves.Length];
                        break;
                    case 'D': case 'F': kind = EntityKind.Door; break;
                    case 'L': kind = EntityKind.Lever; break;
                    case 'B': kind = EntityKind.Plate; break;
                    case 'K': case 'k': kind = EntityKind.Key; break;
                    case 'H': case 'h': kind = EntityKind.Hazard; break;
                    case 'G': kind = EntityKind.Goal; break;
                    case 'S': kind = EntityKind.Sightstone; break;
                    default: continue;
                }
                var holder = new GameObject($"{kind}_{x}_{y}").transform;
                holder.SetParent(root, false);
                holder.localPosition = GridSpace.CellToWorld(x, y);
                Looks.Build(Looks.ForKind(kind), holder, accent);
            }
        }

        /// <summary>
        /// Procedural materials and textures are created in memory, so a saved scene would lose
        /// them (and draw magenta). Write each one into the project once and let every renderer
        /// keep pointing at the same instance.
        /// </summary>
        static int PersistGeneratedAssets(Transform root)
        {
            var mats = new Dictionary<string, Material>();
            var texes = new Dictionary<string, Texture2D>();
            var meshes = new Dictionary<string, Mesh>();
            int saved = 0;

            // Procedural meshes live only in memory too, and a saved scene would lose them.
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || AssetDatabase.Contains(mesh)) continue; // built-in primitives are already assets
                if (meshes.TryGetValue(mesh.name, out var shared)) { mf.sharedMesh = shared; continue; }
                AssetDatabase.CreateAsset(mesh, $"{GenDir}/{Safe(mesh.name, "mesh")}.asset");
                meshes[mesh.name] = mesh;
                saved++;
            }

            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mat = r.sharedMaterial;
                if (mat == null) continue;

                // Several helpers hand out fresh instances of the same look; collapse them.
                var tex = mat.mainTexture as Texture2D;
                string key = mat.name + "|" + (tex != null ? tex.name : "");
                if (mats.TryGetValue(key, out var existing)) { r.sharedMaterial = existing; continue; }

                if (tex != null && !AssetDatabase.Contains(tex))
                {
                    if (texes.TryGetValue(tex.name, out var shared)) mat.mainTexture = shared;
                    else
                    {
                        AssetDatabase.CreateAsset(tex, $"{GenDir}/{Safe(tex.name, "tex")}.asset");
                        texes[tex.name] = tex;
                        saved++;
                    }
                }
                if (!AssetDatabase.Contains(mat))
                {
                    AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath($"{GenDir}/{Safe(mat.name, "mat")}.mat"));
                    saved++;
                }
                mats[key] = mat;
            }
            if (saved > 0) AssetDatabase.SaveAssets();
            return saved;
        }

        static string Safe(string name, string fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_').Replace('/', '_');
        }
    }
}
