using UnityEngine;

namespace Crawler.Render
{
    /// <summary>Server grid (x, y) maps to world XZ. One unit per cell, cell centers at +0.5.</summary>
    public static class GridSpace
    {
        /// <summary>Level size in cells. Levels are rectangular, sized to their puzzle.</summary>
        public static int Width = 32;
        public static int Height = 32;

        public static Vector3 CellToWorld(int x, int y) => new Vector3(x + 0.5f, 0f, y + 0.5f);
    }
}
