using Crawler.Render;
using UnityEngine;

namespace Crawler.App
{
    static class ProcCursor
    {
        public static void Apply() =>
            Cursor.SetCursor(ProcArt.Paw, new Vector2(16, 16), CursorMode.Auto);
    }
}
