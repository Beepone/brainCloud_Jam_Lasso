using UnityEngine;

namespace LootLasso
{
    // UiKit: draws the OnGUI menus with the UI art (Assets/Sprites/UI_Components.ase).
    //
    // The Aseprite file has one layer per piece, named "<Name> <width>x<height>":
    //   Normal / Hover / Pressed / Disabled 108x24   the big buttons (PLAY ONLINE, BACK...)
    //   Normal / Hover / Pressed / Disabled 64x14    the small ones (change name, the result panel's two)
    //   Panel 96x64                                  the result panel (stretched, see DrawSliced)
    //   Field 126x22                                 the name box
    //   Row 150x14                                   one leaderboard line
    // It's imported with "Individual Layers", so each layer becomes its own sprite with the layer's name.
    // The sprites are kept on the Arena (uiSprites), filled in automatically in the editor.
    //
    // Everything is drawn at the game's pixel scale (one art pixel = the same size as in the 180 x 320 world),
    // so the pixel art stays crisp and lines up with everything else.
    // If a sprite is missing, the plain Unity button is drawn instead.
    //
    // Buttons also make the UI sounds (on the Arena): a click when pressed, and a hover blip when the mouse
    // moves onto one (not on phones, which have no hover).
    public static class UiKit
    {
        // UI sound volumes, 0 to 1. (Static, so they aren't in the Inspector: change them here.)
        public static float clickVolume = 0.8f;
        public static float hoverVolume = 0.4f;

        // One speaker for the UI sounds, made the first time it's needed (on the Arena, which lasts all game).
        static AudioSource uiSource;
        // The button the mouse is over (its control ID), so the hover sound plays once per button entered.
        static int hoveredButton;

        static void PlayUiSound(AudioClip clip, float volume)
        {
            var arena = Arena.Current;
            if (clip == null || arena == null) return;
            if (uiSource == null)
            {
                uiSource = arena.gameObject.AddComponent<AudioSource>();
                uiSource.playOnAwake = false;
                uiSource.spatialBlend = 0f;
            }
            uiSource.PlayOneShot(clip, volume);
        }

        // How many screen pixels one art pixel covers: the screen fits 320 rows tall or 180 columns wide,
        // whichever is tighter (Min). Same as the title logo.
        public static float Scale => Mathf.Min(Screen.height / 320f, Screen.width / 180f);

        public const string Big = "108x24";
        public const string Small = "64x14";
        public static Sprite Panel => Get("Panel", "96x64");
        public static Sprite Field => Get("Field", "126x22");
        public static Sprite Row => Get("Row", "150x14");

        // Finds a sprite from the UI file by its layer name, e.g. Get("Normal", Big).
        public static Sprite Get(string state, string size)
        {
            var arena = Arena.Current;
            if (arena == null || arena.uiSprites == null) return null;
            string name = state + " " + size;
            foreach (var sprite in arena.uiSprites)
                if (sprite != null && sprite.name == name) return sprite;
            return null;
        }

        // The rectangle a button of this size takes, centred left-to-right with its middle at y
        // (a fraction of the screen height). Without the art: the given fallback width (a fraction of the
        // screen width) and height (screen pixels).
        public static Rect Centered(string size, float y, float fallbackWidth, float fallbackHeight)
        {
            var normal = Get("Normal", size);
            if (normal == null)
                return new Rect((Screen.width - Screen.width * fallbackWidth) / 2f, Screen.height * y - fallbackHeight / 2f, Screen.width * fallbackWidth, fallbackHeight);
            return Centered(normal, y);
        }

        // Where a sprite goes at the game's pixel scale, centred left-to-right with its middle at y.
        public static Rect Centered(Sprite sprite, float y)
        {
            float w = sprite.rect.width * Scale, h = sprite.rect.height * Scale;
            return new Rect((Screen.width - w) / 2f, Screen.height * y - h / 2f, w, h);
        }

        // Draws a whole sprite into a screen rectangle (stretched to fit it), tinted by "tint".
        public static void DrawSprite(Rect rect, Sprite sprite, Color tint)
        {
            Rect pixels = sprite.textureRect; // where the sprite sits in its texture, in texture pixels
            DrawPart(rect, sprite.texture, pixels, tint);
        }

        // Draws "pixels" (a rectangle of the texture, in texture pixels) into a screen rectangle.
        // DrawTextureWithTexCoords wants fractions (0..1) of the texture's width and height instead.
        static void DrawPart(Rect rect, Texture2D tex, Rect pixels, Color tint)
        {
            var part = new Rect(pixels.x / tex.width, pixels.y / tex.height, pixels.width / tex.width, pixels.height / tex.height);
            var oldColor = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, tex, part);
            GUI.color = oldColor;
        }

        // "9-slice": draws a sprite stretched to any size while keeping its corners crisp. The sprite is cut
        // into a 3 x 3 grid, "border" art pixels in from each edge:
        //   corners  drawn at the game's pixel scale, never stretched
        //   edges    stretched along their length only
        //   middle   stretched both ways
        public static void DrawSliced(Rect rect, Sprite sprite, float border)
        {
            Rect pixels = sprite.textureRect;
            float b = Mathf.Min(border, pixels.width / 2f, pixels.height / 2f);
            float screenB = Mathf.Min(b * Scale, rect.width / 2f, rect.height / 2f);

            // Where the grid lines fall, measured from the left and from the TOP, in the sprite (art pixels)
            // and on screen.
            float[] artX = { 0, b, pixels.width - b, pixels.width };
            float[] artY = { 0, b, pixels.height - b, pixels.height };
            // Rounded to whole screen pixels: pieces that meet partway through a pixel can leave a hairline gap.
            float[] screenX = { Mathf.Round(rect.x), Mathf.Round(rect.x + screenB), Mathf.Round(rect.xMax - screenB), Mathf.Round(rect.xMax) };
            float[] screenY = { Mathf.Round(rect.y), Mathf.Round(rect.y + screenB), Mathf.Round(rect.yMax - screenB), Mathf.Round(rect.yMax) };

            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    // Textures count y up from the bottom, the screen counts down from the top, hence the flip.
                    var source = new Rect(pixels.x + artX[col], pixels.y + pixels.height - artY[row + 1],
                                          artX[col + 1] - artX[col], artY[row + 1] - artY[row]);
                    var target = Rect.MinMaxRect(screenX[col], screenY[row], screenX[col + 1], screenY[row + 1]);
                    DrawPart(target, sprite.texture, source, Color.white);
                }
        }

        // A button drawn with the art. Returns true on the frame it's clicked (like GUI.Button).
        //
        // GUI.Button can't tell us whether it's being held down, so this does the clicking itself, the way
        // Unity's own controls work: a "control ID" names this button for the frame, and while the mouse (or
        // finger) is held on it, GUIUtility.hotControl is set to that ID.
        public static bool Button(Rect rect, string text, string size, int fontSize)
        {
            var normal = Get("Normal", size);
            if (normal == null)
            {
                // No art: the plain button.
                var plain = new GUIStyle(GUI.skin.button) { fontSize = fontSize, fontStyle = FontStyle.Bold };
                return GUI.Button(rect, text, plain);
            }

            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            bool over = rect.Contains(e.mousePosition);
            bool clicked = false;

            // GetTypeForControl: what this event means for THIS button (e.g. a mouse-up elsewhere still
            // reaches the button you pressed, so letting go off it cancels the click).
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (over && GUI.enabled) { GUIUtility.hotControl = id; e.Use(); } // e.Use = "handled, nobody else gets it"
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id) e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        clicked = over; // only counts if you let go on the button
                        if (clicked) PlayUiSound(Arena.Current != null ? Arena.Current.uiClick : null, clickVolume);
                    }
                    break;
                case EventType.Repaint:
                    Draw(rect, text, size, fontSize, id, over);
                    break;
            }
            return clicked;
        }

        static void Draw(Rect rect, string text, string size, int fontSize, int id, bool over)
        {
            bool held = GUIUtility.hotControl == id;
            // Phones have no hover: the "mouse" stays where the last tap was, so skip it there.
            bool hover = over && !held && !Application.isMobilePlatform;

            // The hover blip, once when the mouse moves onto this button (not while holding it down).
            if (hover && GUI.enabled && hoveredButton != id)
            {
                hoveredButton = id;
                PlayUiSound(Arena.Current != null ? Arena.Current.uiHover : null, hoverVolume);
            }
            else if (!over && hoveredButton == id) hoveredButton = 0;
            string state = !GUI.enabled ? "Disabled" : held && over ? "Pressed" : hover ? "Hover" : "Normal";
            var sprite = Get(state, size) ?? Get("Normal", size);

            // The importer trims each layer to its drawn pixels, so Hover and Pressed are shorter than Normal
            // (the button sinks into its shadow). Line their BOTTOMS up with the full button, centred sideways.
            float scale = rect.height / Get("Normal", size).rect.height;
            Rect pixels = sprite.textureRect; // where the sprite sits in its texture, in texture pixels
            float w = pixels.width * scale, h = pixels.height * scale;
            var drawn = new Rect(rect.center.x - w / 2f, rect.yMax - h, w, h);

            DrawSprite(drawn, sprite, Color.white); // never tinted (disabled has its own art)

            // The text sits on the button's face: from the top of what's drawn down to just above the big
            // buttons' 3-pixel shadow, so it moves down with the face when pressed.
            float shadow = size == Big ? 3f * scale : 0f;
            var face = new Rect(rect.x, drawn.y, rect.width, rect.yMax - shadow - drawn.y);
            Color textColor = GUI.enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f);

            // The pixel font: large on big buttons, small on small ones, if it fits inside the rough edges.
            var font = PixelFont.Pick(text, rect.width - 8f * scale, scale, large: size == Big);
            if (font != null) { font.Draw(text, face, scale, textColor); return; }

            // Otherwise Unity's font.
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            style.normal.textColor = textColor;
            GUI.Label(face, text, style);
        }

        // A text box drawn on the Field art. It uses the LABEL style, which has no background of its own
        // (the text box style always paints its grey box over the art), and white text in every state.
        public static string TextField(Rect rect, string text, int maxLength, int fontSize)
        {
            DrawSprite(rect, Field, Color.white);
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
            };
            style.normal.textColor = style.hover.textColor = style.focused.textColor = style.active.textColor = Color.white;
            return GUI.TextField(rect, text, maxLength, style);
        }

        // The biggest font size (up to maxSize) that fits the text on a button of this width,
        // leaving a few art pixels of margin each side. (Fonts differ between the editor and browsers.)
        public static int FitFont(string text, float width, int maxSize)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = maxSize, fontStyle = FontStyle.Bold };
            float room = width - 8f * Scale;
            while (style.fontSize > 6 && style.CalcSize(new GUIContent(text)).x > room) style.fontSize--;
            return style.fontSize;
        }
    }
}
