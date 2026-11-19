using Crawler.Render;
using UnityEngine;

namespace Crawler.App
{
    /// <summary>Replaces the mouse cursor with the procedurally drawn paw.</summary>
    static class ProcCursor
    {
        /// <summary>Sets the cursor, hotspot in the middle of the paw.</summary>
        public static void Apply() =>
            Cursor.SetCursor(ProcArt.Paw, new Vector2(16, 16), CursorMode.Auto);
    }
}
