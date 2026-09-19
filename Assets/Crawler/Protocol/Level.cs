using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Crawler.Protocol
{
    /// <summary>
    /// Static level layout, shared by the client (terrain rendering, room titles, camera)
    /// and whatever simulates the world (MockFeed today, the Rust server later). One file
    /// per level under StreamingAssets/levels/{id}.json. The file format is part of the
    /// contract with the server: both sides must read it identically.
    ///
    /// Tile glyphs: '#' wall, 'T' tree (both solid), anything else is floor. Entity glyphs
    /// sit on floor: P,1 the two seats' spawns; D door; F summit gate; L lever; B stone
    /// plate; K key; H pit; h hidden pit; G goal; S sightstone; k phantom key (overlay only);
    /// E a room's entrance, where a leopard who falls in that room comes back.
    /// Rows are listed north to south: rows[0] is y = height-1.
    /// </summary>
    public sealed class LevelDef
    {
        public string Id;
        public string Name;
        public string Biome;
        public int Width;
        public int Height;
        public char[,] Tiles; // [x, y]
        public List<LevelRegion> Regions = new List<LevelRegion>();
        public List<LevelLink> Links = new List<LevelLink>();

        public char At(int x, int y) =>
            x >= 0 && y >= 0 && x < Width && y < Height ? Tiles[x, y] : '#';

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public static bool IsSolidTile(char c) => c == '#' || c == 'T';

        /// <summary>Index of the first region containing the cell, or -1.</summary>
        public int RegionAt(int x, int y)
        {
            for (int i = 0; i < Regions.Count; i++)
                if (Regions[i].Contains(x, y)) return i;
            return -1;
        }

        /// <summary>The 'E' cell inside a region, if the level marks one.</summary>
        public bool EntranceOf(int region, out int ex, out int ey)
        {
            ex = ey = 0;
            if (region < 0 || region >= Regions.Count) return false;
            var r = Regions[region];
            for (int y = r.Y; y < r.Y + r.H; y++)
            for (int x = r.X; x < r.X + r.W; x++)
                if (At(x, y) == 'E') { ex = x; ey = y; return true; }
            return false;
        }
    }

    public sealed class LevelRegion
    {
        public string Name;
        public string Subtitle;
        public string Rule; // hub | lever | plate | sight | goal
        public int X, Y, W, H; // cells; (X, Y) is the south-west corner

        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + W && y < Y + H;
    }

    /// <summary>
    /// A lever or plate that drives a specific door, independent of regions. A door with any
    /// link opens from its links only; a door with none falls back to its region's rule.
    /// Cells are world cells (x from the west, y from the south).
    /// </summary>
    public readonly struct LevelLink
    {
        public readonly int FromX, FromY, ToX, ToY;
        public LevelLink(int fx, int fy, int tx, int ty) { FromX = fx; FromY = fy; ToX = tx; ToY = ty; }
    }

    public static class LevelCodec
    {
        public static LevelDef Parse(string json)
        {
            var o = JObject.Parse(json);
            var rows = (JArray)o["rows"] ?? throw new FormatException("level has no rows");

            // "grid" is the old square form. width/height default to the map itself.
            int height = o["height"] != null ? (int)o["height"] : o["grid"] != null ? (int)o["grid"] : rows.Count;
            int width = o["width"] != null ? (int)o["width"] : o["grid"] != null ? (int)o["grid"] : ((string)rows[0])?.Length ?? 0;
            if (rows.Count != height) throw new FormatException($"level has {rows.Count} rows, expected {height}");

            var tiles = new char[width, height];
            for (int r = 0; r < height; r++)
            {
                var s = (string)rows[r];
                if (s == null || s.Length != width)
                    throw new FormatException($"row {r} has {s?.Length ?? 0} chars, expected {width}");
                int y = height - 1 - r;
                for (int x = 0; x < width; x++) tiles[x, y] = s[x];
            }

            var def = new LevelDef
            {
                Id = (string)o["id"],
                Name = (string)o["name"] ?? (string)o["id"],
                Biome = (string)o["biome"] ?? "",
                Width = width,
                Height = height,
                Tiles = tiles,
            };
            foreach (var token in (JArray)o["regions"] ?? new JArray())
            {
                var ro = (JObject)token;
                var rect = (JArray)ro["rect"];
                if (rect == null || rect.Count != 4) throw new FormatException("region rect must be [x, y, w, h]");
                def.Regions.Add(new LevelRegion
                {
                    Name = (string)ro["name"] ?? "",
                    Subtitle = (string)ro["subtitle"] ?? "",
                    Rule = (string)ro["rule"] ?? "",
                    X = (int)rect[0], Y = (int)rect[1], W = (int)rect[2], H = (int)rect[3],
                });
            }

            // Links are written as [column, row] exactly as they appear in "rows" (row 0 is the
            // top line), because that is how an author reads the map. Converted to world cells here.
            foreach (var token in (JArray)o["links"] ?? new JArray())
            {
                var lo = (JObject)token;
                var from = (JArray)lo["from"];
                var to = (JArray)lo["to"];
                if (from == null || to == null || from.Count != 2 || to.Count != 2)
                    throw new FormatException("link must be { \"from\": [col, row], \"to\": [col, row] }");
                var link = new LevelLink((int)from[0], height - 1 - (int)from[1], (int)to[0], height - 1 - (int)to[1]);
                if (!def.InBounds(link.FromX, link.FromY) || !def.InBounds(link.ToX, link.ToY))
                    throw new FormatException("link points outside the map");
                def.Links.Add(link);
            }
            return def;
        }
    }
}
