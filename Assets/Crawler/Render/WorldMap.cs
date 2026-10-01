using Crawler.Protocol;
using UnityEngine;

namespace Crawler.Render
{
    /// <summary>
    /// The level the view layers are currently drawing, kept so a piece can ask what is beside
    /// it. A snapshot carries only a cell, and a door has to know which way the wall it sits in
    /// runs before it can face the right way.
    /// </summary>
    public static class WorldMap
    {
        static LevelDef _level;

        // Domain reload is off, so anything static has to be put back by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _level = null;

        public static void Set(LevelDef level) => _level = level;

        /// <summary>Unknown until a level is loaded, which is the case for the plain grid floor.</summary>
        public static bool Known => _level != null;

        /// <summary>
        /// Which way a run of wall goes through this cell: 0 for east to west, 90 for north to
        /// south. Walls use it to lay their courses along the run, and anything standing in a
        /// gap in that wall (a door, a summit gate) uses it to face the same way, so the panel
        /// fills the opening instead of standing side-on in it.
        /// </summary>
        public static float AxisAt(LevelDef level, int x, int y)
        {
            if (level == null) return 0f;
            bool ew = level.At(x - 1, y) == '#' || level.At(x + 1, y) == '#';
            bool ns = level.At(x, y - 1) == '#' || level.At(x, y + 1) == '#';
            if (ew && !ns) return 0f;
            if (ns && !ew) return 90f;
            return ProcArt.Hash01(x, y, 99) < 0.5f ? 0f : 90f; // corner or pillar: either reads fine
        }

        /// <summary>The same question against the level being drawn right now.</summary>
        public static float AxisAt(int x, int y) => AxisAt(_level, x, y);
    }
}
