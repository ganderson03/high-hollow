using UnityEngine;

namespace Crawler.Render
{
    /// <summary>1970s national-park-poster palette. Warm earth, no teal.</summary>
    public static class Palette
    {
        public static readonly Color Night     = Hex(0x232A25); // background
        public static readonly Color Sienna    = Hex(0xA33B2A); // accent
        public static readonly Color Ember     = Hex(0xE06B4F); // highlight
        public static readonly Color Cream     = Hex(0xEFE2C4);
        public static readonly Color Sand      = Hex(0xD8C29D);
        public static readonly Color SandDeep  = Hex(0xC7AC84);
        public static readonly Color Ochre     = Hex(0xD9A441);
        public static readonly Color Pine      = Hex(0x4F5B3A);
        public static readonly Color Umber     = Hex(0x7A4A2E);
        public static readonly Color Stone     = Hex(0x9C8B72);
        public static readonly Color Plum      = Hex(0x6B3F4A);
        public static readonly Color Heather   = Hex(0xB9A3C4);
        public static readonly Color Rosette   = Hex(0x3B342C);
        public static readonly Color Moss      = Hex(0x5B6544); // floor
        public static readonly Color MossDeep  = Hex(0x535C3E);
        public static readonly Color Grout     = Hex(0x444C35);
        public static readonly Color Pit       = Hex(0x2A2620);
        public static readonly Color Forest    = Hex(0x363E2C); // forest floor outside rooms
        public static readonly Color Forest2   = Hex(0x3E4732);
        public static readonly Color WallBed   = Hex(0x4A463A);
        public static readonly Color Needle    = Hex(0x3F4A2F); // darker pine
        public static readonly Color Burnt     = Hex(0xC4652F); // poster sunset band
        public static readonly Color Ridge     = Hex(0x9B6B4A); // far mountains

        public static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
