using UnityEngine;

namespace LootLasso
{
    // Palette: the colours the code draws with (text, popups, hoard bands, sparks), all taken from the
    // artists' 32-colour palette, so code-drawn things match the art. The art itself uses the same palette.
    //
    // Only the ones the code uses are named here. Sprite tints stay pure white (white = "the art as drawn").
    // The day-night light colours (DayNight) aren't here: they're light, multiplied onto the art, not paint.
    public static class Palette
    {
        public static readonly Color Bone = new Color32(0xec, 0xeb, 0xe7, 255);   // text
        public static readonly Color Ash = new Color32(0xcb, 0xc6, 0xc1, 255);    // quieter text
        public static readonly Color Tan = new Color32(0xb2, 0x8b, 0x78, 255);    // the rope
        public static readonly Color Gold = new Color32(0xd3, 0xbd, 0x46, 255);   // titles, treasure, winning
        public static readonly Color Orange = new Color32(0xdc, 0x6a, 0x36, 255); // warnings (last 10 seconds, offline)
        public static readonly Color Pink = new Color32(0xf6, 0x82, 0xbf, 255);   // stealing
        public static readonly Color Red = new Color32(0xd7, 0x40, 0x60, 255);    // the red hunter
        public static readonly Color Teal = new Color32(0x49, 0xa3, 0xb4, 255);   // the blue hunter, night
        public static readonly Color Mint = new Color32(0x99, 0xe0, 0xa8, 255);   // online
        public static readonly Color Night = new Color32(0x1a, 0x0a, 0x01, 255);  // the darkest: dimming, text shadows

        // A palette colour, see-through: e.g. WithAlpha(Night, 0.6f) to dim the screen behind a menu.
        public static Color WithAlpha(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);
    }
}
