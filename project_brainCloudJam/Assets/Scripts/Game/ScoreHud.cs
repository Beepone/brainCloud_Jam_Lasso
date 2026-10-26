using System.Collections.Generic; // List<T>
using UnityEngine;

namespace LootLasso
{
    // ScoreHud: draws both scores, plus little floating "+5" popups when treasure scores.
    //
    // It uses OnGUI, Unity's simple built-in way to draw text and boxes from code. It's a placeholder:
    // when the artists' UI kit arrives it'll be swapped for proper UI. Arena adds this component
    // automatically, so there's nothing to set up in the scene.
    public class ScoreHud : MonoBehaviour
    {
        [Tooltip("Text size, as a fraction of the screen height (so it looks the same on any screen).")]
        public float textSize = 0.035f;
        [Tooltip("How long a popup floats before it fades out, in seconds.")]
        public float popupTime = 1.2f;

        // Text colours: each player's score is shown in their hunter's colour (blue or red, picked per match).
        static readonly Color BlueText = new Color(0.55f, 0.75f, 1f);
        static readonly Color RedText = new Color(1f, 0.55f, 0.55f);
        static Color YourColor => Arena.Current != null && !Arena.Current.LocalIsBlue ? RedText : BlueText;
        static Color RivalColor => Arena.Current != null && !Arena.Current.LocalIsBlue ? BlueText : RedText;

        // ----- Popups -----
        // A "struct" is a small bundle of values (like a class, but copied around as a value).
        struct Popup
        {
            public string text;
            public Vector2 worldPos;
            public Color color;
            public float startTime;
        }

        // "static" so any script can call ScoreHud.ShowPopup(...) without needing a reference to the HUD.
        static readonly List<Popup> popups = new List<Popup>();

        public static void ShowPopup(string text, Vector2 worldPos, Color color)
        {
            popups.Add(new Popup { text = text, worldPos = worldPos, color = color, startTime = Time.time });
        }

        // Scripts keep static lists between plays (domain reload is off in this project), so clear it on start.
        void Awake() => popups.Clear();

        // Unity calls OnGUI to draw immediate-mode UI. It can run several times a frame, which is fine here.
        void OnGUI()
        {
            var arena = Arena.Current;
            if (arena == null) return;
            if (!arena.isPlaying && !arena.isOver) return; // on the menus: no HUD

            int size = Mathf.RoundToInt(Screen.height * textSize);
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            // "You" = the player at this computer (online, the guest plays the top side of the world, with
            // its camera turned around so it's at the bottom of the screen). Once over: the frozen final scores.
            int yourScore = arena.isOver ? arena.FinalLocalScore : arena.LocalScore;
            int rivalScore = arena.isOver ? arena.FinalOpponentScore : arena.OpponentScore;

            // Positions are in ART PIXELS of the 180 x 320 screen (see docs/art/UITemplates.md), not world
            // positions, so they stay in the same place on screen even when the camera is turned around.
            // Your score: centred in the strip under your hoard (row 301).
            DrawTextAtPixel($"YOU  {yourScore}", 90, 301, YourColor, style, Align.Center);

            // The rival's score and the timer, top row. Hidden once the match is over: the result panel shows
            // both scores, and the winner portrait stands up there.
            if (!arena.isOver)
            {
                // The rival's score: top-left, row 22 (below the ~17 px where phone notches sit).
                DrawTextAtPixel($"RIVAL  {rivalScore}", 6, 22, RivalColor, style, Align.Left);

                // The timer: top-right, row 22. Red in the last 10 seconds.
                // CeilToInt rounds up, so it shows 2:00 at the start and only reaches 0:00 when time is truly up.
                int secondsLeft = Mathf.CeilToInt(arena.timeLeft);
                string clock = $"{secondsLeft / 60}:{secondsLeft % 60:00}"; // "/" = whole minutes, "%" = remaining seconds
                Color clockColor = secondsLeft <= 10 ? new Color(1f, 0.35f, 0.3f) : Color.white;
                DrawTextAtPixel(clock, 174, 22, clockColor, style, Align.Right);
            }

            // Popups rise and fade. Loop backwards so removing finished ones doesn't skip any.
            style.fontSize = Mathf.RoundToInt(size * 0.85f);
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                float age = Time.time - p.startTime;
                if (age > popupTime) { popups.RemoveAt(i); continue; }

                var color = p.color;
                color.a = 1f - age / popupTime; // fade out
                // Float up the SCREEN (not the world, which is upside down for the online guest).
                Vector3 screen = Camera.main.WorldToScreenPoint(p.worldPos);
                screen.y += age * Screen.height * 0.06f;
                DrawTextAtScreen(p.text, screen, color, style, Align.Center);
            }

            if (arena.isOver) DrawResultPanel(arena, size);
        }

        // The end-of-match panel in the middle of the screen: who won, why, the scores, and Play Again.
        void DrawResultPanel(Arena arena, int size)
        {
            float w = Screen.width * 0.8f, h = Screen.height * 0.36f;
            var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

            // The winner's big portrait stands on top of the panel (drawn first, so the panel covers its feet).
            DrawWinnerPortrait(arena, panel);

            // A dark see-through box behind the text. GUI.color tints everything drawn after it.
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size * 1.8f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var line = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = arena.result == "YOU WIN!" ? new Color(1f, 0.85f, 0.2f)
                                   : arena.result == "RIVAL WINS" ? RivalColor
                                   : Color.white;
            line.normal.textColor = Color.white;

            float row = h / 6f; // split the panel into 6 rows
            GUI.Label(new Rect(panel.x, panel.y + row * 0.3f, w, row * 1.4f), arena.result, title);
            GUI.Label(new Rect(panel.x, panel.y + row * 1.6f, w, row), arena.resultReason, line);
            GUI.Label(new Rect(panel.x, panel.y + row * 2.4f, w, row), $"You {arena.FinalLocalScore}   -   Rival {arena.FinalOpponentScore}", line);

            // What happened to the score on the leaderboard (from the menu, if there is one).
            var menu = MainMenu.Current;
            if (menu != null && !string.IsNullOrEmpty(menu.scoreStatus))
            {
                var small = new GUIStyle(line) { fontSize = Mathf.RoundToInt(size * 0.75f) };
                small.normal.textColor = new Color(0.8f, 0.9f, 0.8f);
                GUI.Label(new Rect(panel.x, panel.y + row * 3.2f, w, row), menu.scoreStatus, small);
            }

            // GUI.Button draws a button and returns true on the frame it's clicked.
            // With menus: PLAY AGAIN and MENU side by side. Without: just PLAY AGAIN.
            var button = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold };
            float by = panel.y + row * 4.4f, bh = row * 1.2f;
            if (menu != null)
            {
                // Two buttons share the width: shrink the text until the longer label fits. (Fonts differ between
                // the editor and browsers, so a fixed size that fits in one can be clipped in the other.)
                // CalcSize measures how big a piece of text would be in this style; padding = the button's inner margins.
                float buttonWidth = w * 0.42f;
                button.fontSize = Mathf.RoundToInt(size * 0.8f);
                while (button.fontSize > 6 && button.CalcSize(new GUIContent("PLAY AGAIN")).x > buttonWidth - button.padding.horizontal)
                    button.fontSize--;
                if (GUI.Button(new Rect(panel.x + w * 0.06f, by, buttonWidth, bh), "PLAY AGAIN", button)) menu.PlayAgain(); // same mode as last time (online or practice)
                if (GUI.Button(new Rect(panel.x + w * 0.52f, by, buttonWidth, bh), "MENU", button)) menu.BackToTitle();
            }
            else if (GUI.Button(new Rect(panel.x + w * 0.25f, by, w * 0.5f, bh), "PLAY AGAIN", button)) arena.PlayAgain();
        }

        [Tooltip("Seconds per frame of the winner portrait's animation.")]
        public float portraitFrameTime = 0.12f;
        [Tooltip("How many art pixels of the portrait's bottom hide behind the panel's top edge.")]
        public float portraitOverlap = 8f;

        // The winning hunter (blue or red, whichever won), 96 x 96 art pixels, centred above the panel.
        // A draw shows nobody.
        void DrawWinnerPortrait(Arena arena, Rect panel)
        {
            bool youWon = arena.result == "YOU WIN!";
            bool rivalWon = arena.result == "RIVAL WINS";
            if (!youWon && !rivalWon) return;
            bool winnerIsBlue = youWon ? arena.LocalIsBlue : !arena.LocalIsBlue;
            Sprite[] frames = winnerIsBlue ? arena.winnerBlue : arena.winnerRed;
            if (frames == null || frames.Length == 0) return;

            // Which frame now: the clock divided by the frame time, wrapped around with "%" (the remainder).
            var sprite = frames[(int)(Time.time / portraitFrameTime) % frames.Length];
            if (sprite == null) return;

            // The panel is 144 art pixels wide, so this is how many screen pixels one art pixel covers.
            float scale = panel.width / 144f;
            // The importer trims each frame to its drawn pixels, so frames differ in size. Its pivot is the
            // middle of the 96 x 96 canvas, so line frames up by the pivot and nothing jitters.
            // (sprite.pivot is measured from the frame's bottom-left; GUI y runs down, hence the flip.)
            float canvasCentreX = panel.center.x;
            float canvasCentreY = panel.y + (portraitOverlap - 48f) * scale; // 48 = half of 96
            Rect pixels = sprite.textureRect;
            var screenRect = new Rect(
                canvasCentreX - sprite.pivot.x * scale,
                canvasCentreY - (pixels.height - sprite.pivot.y) * scale,
                pixels.width * scale,
                pixels.height * scale);

            Texture2D tex = sprite.texture;
            var part = new Rect(pixels.x / tex.width, pixels.y / tex.height, pixels.width / tex.width, pixels.height / tex.height);
            GUI.DrawTextureWithTexCoords(screenRect, tex, part);
        }

        enum Align { Left, Center, Right }

        const float ArtPixelsPerUnit = 23f; // the map art's scale (see docs/art/MapTemplate.md)

        // Draws text at an art pixel of the 180 x 320 screen (column, row counted from the top-left).
        static void DrawTextAtPixel(string text, float column, float row, Color color, GUIStyle style, Align align)
        {
            // The 180 x 320 play area sits in the middle of the screen, but the screen may be wider or taller
            // (a landscape browser window shows extra field at the sides). So measure from the play area,
            // not the screen's edges. Screen pixels per world unit = screen height / camera view height.
            var cam = Camera.main;
            float pixelsPerUnit = cam.pixelHeight / (cam.orthographicSize * 2f);
            float pixelsPerArtPixel = pixelsPerUnit / ArtPixelsPerUnit;
            Vector2 centre = cam.pixelRect.center;
            var screen = new Vector3(
                centre.x + (column - 90f) * pixelsPerArtPixel,  // 90 = half of 180
                centre.y + (160f - row) * pixelsPerArtPixel,    // 160 = half of 320; rows count down from the top
                0f);
            DrawTextAtScreen(text, screen, color, style, align);
        }

        // Draws text at a screen position (pixels, y up from the bottom), with a dark shadow so it reads on
        // any background. Left = starts at the point, Center = centred on it, Right = ends at it.
        static void DrawTextAtScreen(string text, Vector3 screen, Color color, GUIStyle style, Align align)
        {
            // GUI's y runs DOWN from the top, while the screen's runs UP from the bottom, so flip it.
            float x = align == Align.Left ? screen.x : align == Align.Right ? screen.x - 400f : screen.x - 200f;
            style.alignment = align == Align.Left ? TextAnchor.MiddleLeft
                            : align == Align.Right ? TextAnchor.MiddleRight
                            : TextAnchor.MiddleCenter;
            var rect = new Rect(x, Screen.height - screen.y - 50f, 400f, 100f);

            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.7f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style); // shadow
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }
    }
}
