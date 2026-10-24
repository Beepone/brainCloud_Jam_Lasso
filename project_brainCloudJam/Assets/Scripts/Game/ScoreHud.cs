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

        static readonly Color YourColor = new Color(0.55f, 0.75f, 1f);
        static readonly Color RivalColor = new Color(1f, 0.55f, 0.55f);

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

            // The rival's score: top-left, row 22 (below the ~17 px where phone notches sit).
            DrawTextAtPixel($"RIVAL  {rivalScore}", 6, 22, RivalColor, style, Align.Left);

            // The timer: top-right, row 22. Red in the last 10 seconds.
            // CeilToInt rounds up, so it shows 2:00 at the start and only reaches 0:00 when time is truly up.
            int secondsLeft = Mathf.CeilToInt(arena.timeLeft);
            string clock = $"{secondsLeft / 60}:{secondsLeft % 60:00}"; // "/" = whole minutes, "%" = remaining seconds
            Color clockColor = secondsLeft <= 10 ? new Color(1f, 0.35f, 0.3f) : Color.white;
            DrawTextAtPixel(clock, 174, 22, clockColor, style, Align.Right);

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
                button.fontSize = Mathf.RoundToInt(size * 0.8f); // two buttons share the width: smaller text fits
                if (GUI.Button(new Rect(panel.x + w * 0.06f, by, w * 0.42f, bh), "PLAY AGAIN", button)) menu.PlayAgain(); // same mode as last time (online or practice)
                if (GUI.Button(new Rect(panel.x + w * 0.52f, by, w * 0.42f, bh), "MENU", button)) menu.BackToTitle();
            }
            else if (GUI.Button(new Rect(panel.x + w * 0.25f, by, w * 0.5f, bh), "PLAY AGAIN", button)) arena.PlayAgain();
        }

        enum Align { Left, Center, Right }

        // Draws text at an art pixel of the 180 x 320 screen (column, row counted from the top-left).
        static void DrawTextAtPixel(string text, float column, float row, Color color, GUIStyle style, Align align)
        {
            // Viewport = the camera's picture, from (0,0) bottom-left to (1,1) top-right. Going through it
            // respects the Pixel Perfect Camera's borders, if it adds any.
            Vector3 screen = Camera.main.ViewportToScreenPoint(new Vector3(column / 180f, 1f - row / 320f, 0f));
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
