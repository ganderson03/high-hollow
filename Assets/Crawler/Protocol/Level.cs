using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Crawler.Protocol
{
    /// <summary>
    /// Static level layout, shared by the client (terrain rendering, room titles, camera)
    /// and whatever simulates the world (MockFeed today, the Rust server later). One file
    /// per level under StreamingAssets/levels/{id}.json.
    ///
    /// Tile glyphs: '#' wall, 'T' tree (both solid), anything else is floor. Entity glyphs
    /// sit on floor: P,1,2 player spawns; D door; F summit gate; L lever; B stone plate;
    /// K key; H hazard; h hidden hazard; G goal; S sightstone; k phantom key (overlay only).
    /// Rows are listed north to south: rows[0] is y = grid-1.
    /// </summary>
    public sealed class LevelDef
    {
        public string Id;
        public string Name;
        public int Grid;
        public char[,] Tiles; // [x, y]
        public List<LevelRegion> Regions = new List<LevelRegion>();

        public char At(int x, int y) =>
            x >= 0 && y >= 0 && x < Grid && y < Grid ? Tiles[x, y] : '#';

        public static bool IsSolidTile(char c) => c == '#' || c == 'T';

        /// <summary>Index of the first region containing the cell, or -1.</summary>
        public int RegionAt(int x, int y)
        {
            for (int i = 0; i < Regions.Count; i++)
                if (Regions[i].Contains(x, y)) return i;
            return -1;
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

    public static class LevelCodec
    {
        public static LevelDef Parse(string json)
        {
            var o = JObject.Parse(json);
            int grid = (int)o["grid"];
            var rows = (JArray)o["rows"] ?? throw new FormatException("level has no rows");
            if (rows.Count != grid) throw new FormatException($"level has {rows.Count} rows, expected {grid}");

            var tiles = new char[grid, grid];
            for (int r = 0; r < grid; r++)
            {
                var s = (string)rows[r];
                if (s == null || s.Length != grid)
                    throw new FormatException($"row {r} has {s?.Length ?? 0} chars, expected {grid}");
                int y = grid - 1 - r;
                for (int x = 0; x < grid; x++) tiles[x, y] = s[x];
            }

            var def = new LevelDef
            {
                Id = (string)o["id"],
                Name = (string)o["name"] ?? (string)o["id"],
                Grid = grid,
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
            return def;
        }
    }
}
