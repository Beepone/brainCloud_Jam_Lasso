using System.Collections.Generic; // Dictionary<K, V>
using UnityEngine;

namespace LootLasso
{
    // PixelFont: draws text with the artists' pixel fonts, at the game's pixel scale, for the OnGUI screens.
    //
    // A font is one picture (e.g. Assets/Sprites/Font_Large_10x14.png): a grid of equal cells, 16 across,
    // one character per cell, in the order of Characters below. The glyphs are WHITE, so any colour can be
    // tinted on in code (tinting multiplies, which can only darken). Each glyph can sit anywhere left-to-right
    // in its cell: its width is measured from its pixels, so narrow letters (I, 1, !) take less room.
    // Up-and-down matters: every glyph shares the cell's baseline.
    //
    // Lowercase is drawn as uppercase (the fonts are capitals only). A character the font doesn't have
    // is drawn as "?".
    //
    // The textures live on the Arena (fontLarge, fontSmall), filled in automatically in the editor. They must
    // be imported with Read/Write on, so the widths can be measured.
    public class PixelFont
    {
        // The order the characters are drawn in the sheet, left to right, top to bottom.
        public const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$.,?()':;+-";
        const int Columns = 16;

        public static PixelFont Large => Get(ref large, Arena.Current != null ? Arena.Current.fontLarge : null, 10, 14, 4);
        public static PixelFont Small => Get(ref small, Arena.Current != null ? Arena.Current.fontSmall : null, 6, 9, 3);
        static PixelFont large, small;

        // Forget the measured fonts, so they're measured again from the pictures. Scripts keep static values
        // between plays (domain reload is off), so without this an edited font sheet would keep its OLD
        // letter widths and wider letters would be cut off. The Arena calls this when Play starts.
        public static void Forget() => large = small = null;

        // The font to draw some text in: the large one (if allowed and it fits), else the small one if it
        // fits, else null (draw it with Unity's font instead). screenWidth = the room there is, in screen pixels.
        public static PixelFont Pick(string text, float screenWidth, float scale, bool large)
        {
            if (large && Large != null && Large.Fits(text, screenWidth, scale)) return Large;
            if (Small != null && Small.Fits(text, screenWidth, scale)) return Small;
            return null;
        }

        // Makes the font the first time it's asked for (and again if the texture changes).
        static PixelFont Get(ref PixelFont font, Texture2D texture, int cellWidth, int cellHeight, int spaceWidth)
        {
            if (texture == null) return null;
            if (font == null || font.texture != texture)
            {
                if (!texture.isReadable) { Debug.LogWarning($"{texture.name}: turn on Read/Write in its import settings."); return null; }
                font = new PixelFont(texture, cellWidth, cellHeight, spaceWidth);
            }
            return font;
        }

        // Where one glyph is in the texture: its cell, and which columns of the cell have pixels.
        struct Glyph
        {
            public int cellX, cellY; // the cell's top-left, in texture pixels (counted from the TOP)
            public int left, width;  // the drawn part, in pixels from the cell's left
        }

        readonly Texture2D texture;
        readonly Dictionary<char, Glyph> glyphs = new Dictionary<char, Glyph>();
        readonly int cellWidth, cellHeight, spaceWidth;
        const int Spacing = 1; // empty pixels between letters

        // The capitals' top and bottom rows in a cell (measured from "H"), for centring text up and down.
        readonly int capTop, capBottom;

        PixelFont(Texture2D texture, int cellWidth, int cellHeight, int spaceWidth)
        {
            this.texture = texture;
            this.cellWidth = cellWidth;
            this.cellHeight = cellHeight;
            this.spaceWidth = spaceWidth;

            // GetPixels32 reads every pixel at once. Its rows go from the BOTTOM up, so row y from the top
            // is at index (height - 1 - y) * width.
            Color32[] pixels = texture.GetPixels32();
            bool Ink(int x, int yFromTop) => pixels[(texture.height - 1 - yFromTop) * texture.width + x].a > 0;

            for (int i = 0; i < Characters.Length; i++)
            {
                var g = new Glyph { cellX = i % Columns * cellWidth, cellY = i / Columns * cellHeight };
                if (g.cellY + cellHeight > texture.height) break; // the sheet stops early
                int first = -1, last = -1;
                for (int x = 0; x < cellWidth; x++)
                    for (int y = 0; y < cellHeight; y++)
                        if (Ink(g.cellX + x, g.cellY + y)) { if (first < 0) first = x; last = x; break; }
                if (first < 0) continue; // an empty cell: not drawn yet
                g.left = first;
                g.width = last - first + 1;
                glyphs[Characters[i]] = g;
            }

            capTop = 0;
            capBottom = cellHeight - 1;
            if (glyphs.TryGetValue('H', out var h))
            {
                capTop = cellHeight; capBottom = -1;
                for (int y = 0; y < cellHeight; y++)
                    for (int x = 0; x < cellWidth; x++)
                        if (Ink(h.cellX + x, h.cellY + y)) { capTop = Mathf.Min(capTop, y); capBottom = Mathf.Max(capBottom, y); }
            }
        }

        // The glyph to draw for a character: uppercase, or "?" if the font doesn't have it.
        bool Find(char c, out Glyph glyph)
        {
            c = char.ToUpperInvariant(c);
            return glyphs.TryGetValue(c, out glyph) || glyphs.TryGetValue('?', out glyph);
        }

        // How wide the text is, in art pixels.
        public int Width(string text)
        {
            int width = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0) width += Spacing;
                if (text[i] == ' ') width += spaceWidth;
                else if (Find(text[i], out var g)) width += g.width;
            }
            return width;
        }

        // Does the text fit in this many screen pixels, at this scale (screen pixels per art pixel)?
        public bool Fits(string text, float screenWidth, float scale) => Width(text) * scale <= screenWidth;

        // Draws the text with the middle of its capitals at screen point (x, y) (GUI coordinates: y DOWN
        // from the top). Alignment: x is where it starts (Left), its middle (Center) or where it ends (Right).
        // shadow: a dark copy one art pixel down-right first, so it reads over the busy playing field.
        public void Draw(string text, float x, float y, TextAnchor alignment, float scale, Color color, bool shadow = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (shadow) Draw(text, x + scale, y + scale, alignment, scale, new Color(0f, 0f, 0f, color.a * 0.6f));

            float width = Width(text) * scale;
            float left = alignment == TextAnchor.MiddleLeft ? x : alignment == TextAnchor.MiddleRight ? x - width : x - width / 2f;
            // The cell's top, so the capitals' middle lands on y.
            float top = y - (capTop + capBottom + 1) / 2f * scale;

            var oldColor = GUI.color;
            GUI.color = color;
            float pen = 0f; // how far along we are, in art pixels
            foreach (char c in text)
            {
                if (c == ' ') { pen += spaceWidth + Spacing; continue; }
                if (!Find(c, out var g)) continue;

                // Snapped to whole screen pixels, so every letter's pixels are the same size.
                var screen = Rect.MinMaxRect(
                    Mathf.Round(left + pen * scale), Mathf.Round(top),
                    Mathf.Round(left + (pen + g.width) * scale), Mathf.Round(top + cellHeight * scale));
                // The part of the texture: fractions (0..1), with y counted UP from the bottom.
                var part = new Rect(
                    (float)(g.cellX + g.left) / texture.width,
                    1f - (float)(g.cellY + cellHeight) / texture.height,
                    (float)g.width / texture.width,
                    (float)cellHeight / texture.height);
                GUI.DrawTextureWithTexCoords(screen, texture, part);
                pen += g.width + Spacing;
            }
            GUI.color = oldColor;
        }

        // Draws the text centred in a rectangle.
        public void Draw(string text, Rect rect, float scale, Color color, bool shadow = false) =>
            Draw(text, rect.center.x, rect.center.y, TextAnchor.MiddleCenter, scale, color, shadow);
    }
}
