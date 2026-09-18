namespace Crawler.Render
{
    /// <summary>Which set of shapes the render layer builds.</summary>
    public enum ArtStyle
    {
        /// <summary>The real thing: faceted stone, pines, snow leopards, cut props.</summary>
        Poster,

        /// <summary>
        /// Blocks and bars in flat colors. An order of magnitude fewer objects, builds in a
        /// blink, and every entity is a single readable shape. For testing rules and timing,
        /// not for looking at.
        /// </summary>
        Placeholder,
    }

    /// <summary>
    /// The active art style. Set once at startup (Bootstrap) or from the Crawler menu in the
    /// editor; the view layers read it while building. Purely presentational: nothing here
    /// changes an input or anything the server would hash.
    /// </summary>
    public static class Art
    {
        public static ArtStyle Style = ArtStyle.Poster;

        public static bool Simple => Style == ArtStyle.Placeholder;
    }
}
