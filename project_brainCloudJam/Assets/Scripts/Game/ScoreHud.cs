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

            int size = Mathf.RoundToInt(Screen.height * textSize);
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            // Your score: in the spare strip under your hoard. Rival's: in the top-left of their hoard.
            Vector2 yourSpot = new Vector2(arena.yourHoard.center.x, arena.yourHoard.yMin - 0.75f);
            DrawText($"YOU  {arena.YourScore}", yourSpot, YourColor, style);

            // The rival's score starts at the hoard's left edge (left-aligned), so it never runs off screen.
            Vector2 rivalSpot = new Vector2(arena.rivalHoard.xMin + 0.25f, arena.rivalHoard.yMax - 0.45f);
            style.alignment = TextAnchor.MiddleLeft;
            DrawText($"RIVAL  {arena.RivalScore}", rivalSpot, RivalColor, style, leftAligned: true);
            style.alignment = TextAnchor.MiddleCenter;

            // Popups rise and fade. Loop backwards so removing finished ones doesn't skip any.
            style.fontSize = Mathf.RoundToInt(size * 0.85f);
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                float age = Time.time - p.startTime;
                if (age > popupTime) { popups.RemoveAt(i); continue; }

                var color = p.color;
                color.a = 1f - age / popupTime;                     // fade out
                Vector2 pos = p.worldPos + Vector2.up * (age * 0.8f); // float upward
                DrawText(p.text, pos, color, style);
            }
        }

        // Draws text at a world position, with a dark shadow so it reads on any background.
        // Centred on the point, or (leftAligned) starting at it.
        // "bool leftAligned = false" is an optional parameter: leave it out and it's false.
        static void DrawText(string text, Vector2 worldPos, Color color, GUIStyle style, bool leftAligned = false)
        {
            // World -> screen pixels. GUI's y runs DOWN from the top, while the camera's runs UP from
            // the bottom, so flip it.
            Vector3 screen = Camera.main.WorldToScreenPoint(worldPos);
            float x = leftAligned ? screen.x : screen.x - 200f;
            var rect = new Rect(x, Screen.height - screen.y - 50f, 400f, 100f);

            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.7f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style); // shadow
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }
    }
}
