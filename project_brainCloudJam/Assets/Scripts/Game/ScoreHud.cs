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
            public bool big; // pops in large and wobbles (STOLEN!)
        }

        // "static" so any script can call ScoreHud.ShowPopup(...) without needing a reference to the HUD.
        static readonly List<Popup> popups = new List<Popup>();

        public static void ShowPopup(string text, Vector2 worldPos, Color color, bool big = false)
        {
            popups.Add(new Popup { text = text, worldPos = worldPos, color = color, startTime = Time.time, big = big });
        }

        // ----- Score bumps -----
        // When treasure lands in a hoard, that player's score text jumps up in size and settles back.
        static float yourBumpTime = -99f, rivalBumpTime = -99f;
        const float BumpTime = 0.3f;

        // local = the score of the player at this computer ("YOU"), otherwise the rival's.
        public static void Bump(bool local)
        {
            if (local) yourBumpTime = Time.time; else rivalBumpTime = Time.time;
        }

        // How big the text is right now: 1.4x just after a bump, easing back to 1x.
        static float BumpScale(float since)
        {
            float t = Mathf.Clamp01(since / BumpTime);
            return 1f + 0.4f * (1f - t) * (1f - t); // (1 - t) squared: fast at first, then gently settles
        }

        // Scripts keep static lists between plays (domain reload is off in this project), so clear it on start.
        void Awake() { popups.Clear(); yourBumpTime = rivalBumpTime = -99f; }

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
            DrawTextAtPixel($"YOU  {yourScore}", 90, 301, YourColor, style, Align.Center, BumpScale(Time.time - yourBumpTime));

            // The rival's score and the timer, top row. Hidden once the match is over: the result panel shows
            // both scores, and the winner portrait stands up there.
            if (!arena.isOver)
            {
                // The rival's score: top-left, row 22 (below the ~17 px where phone notches sit).
                DrawTextAtPixel($"RIVAL  {rivalScore}", 6, 22, RivalColor, style, Align.Left, BumpScale(Time.time - rivalBumpTime));

                // The timer: top-right, row 22. Red in the last 10 seconds.
                // CeilToInt rounds up, so it shows 2:00 at the start and only reaches 0:00 when time is truly up.
                int secondsLeft = Mathf.CeilToInt(arena.timeLeft);
                // How long the current number has been showing (the number minus the exact time left).
                float shown = secondsLeft - arena.timeLeft;
                // The music's finale (its last 16 beats): the clock counts 0:08 to 0:01 ON THE BEAT instead,
                // each number landing on a kick (see Juice.FinalCountdown). Not exact seconds, but nobody can tell.
                if (Juice.Current != null && Juice.Current.FinalCountdown(out int beatNumber, out float beatShown))
                {
                    secondsLeft = beatNumber;
                    shown = beatShown;
                }
                string clock = $"{secondsLeft / 60}:{secondsLeft % 60:00}"; // "/" = whole minutes, "%" = remaining seconds
                Color clockColor = secondsLeft <= 10 ? new Color(1f, 0.35f, 0.3f) : Color.white;
                // In the last 10 seconds it also pulses: big each time the number changes, shrinking back over
                // a third of a second.
                float pulse = 1f;
                if (secondsLeft <= 10 && arena.isPlaying) pulse = 1f + 0.5f * Mathf.Clamp01(1f - shown / 0.33f);
                DrawTextAtPixel(clock, 174, 22, clockColor, style, Align.Right, pulse);
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
                if (!p.big)
                {
                    DrawTextAtScreen(p.text, screen, color, style, Align.Center);
                    continue;
                }
                // Big: starts at 1.7x the size and snaps down to 1.3x, while tilting side to side and settling.
                // (Not bigger: a long word like NIGHTFALL! would no longer fit across the screen.)
                float t = age / popupTime;
                float popScale = Mathf.Lerp(1.7f, 1.3f, Mathf.Clamp01(age / 0.15f));
                float wobble = Mathf.Sin(age * 30f) * 12f * (1f - t); // degrees, dying away
                var pivot = new Vector2(screen.x, Screen.height - screen.y);
                var oldMatrix = GUI.matrix; // GUI.matrix turns everything drawn after it, so put it back after
                GUIUtility.RotateAroundPivot(wobble, pivot);
                DrawTextAtScreen(p.text, screen, color, style, Align.Center, popScale);
                GUI.matrix = oldMatrix;
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

            // The Panel art, stretched to fit with its rough corners kept crisp (8 art pixels in from each edge).
            // Without it, a dark see-through box. (GUI.color tints everything drawn after it.)
            if (UiKit.Panel != null)
            {
                UiKit.DrawSliced(panel, UiKit.Panel, 8f);
            }
            else
            {
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.DrawTexture(panel, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            var title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size * 1.8f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var line = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = arena.result == "YOU WIN!" ? new Color(1f, 0.85f, 0.2f)
                                   : arena.result == "RIVAL WINS" ? RivalColor
                                   : Color.white;
            line.normal.textColor = Color.white;

            float row = h / 6f; // split the panel into 6 rows
            PanelText(arena.result, new Rect(panel.x, panel.y + row * 0.3f, w, row * 1.4f), title, large: true);
            PanelText(arena.resultReason, new Rect(panel.x, panel.y + row * 1.6f, w, row), line, large: false);
            PanelText($"You {arena.FinalLocalScore}  -  Rival {arena.FinalOpponentScore}", new Rect(panel.x, panel.y + row * 2.4f, w, row), line, large: false);

            // What happened to the score on the leaderboard (from the menu, if there is one).
            var menu = MainMenu.Current;
            if (menu != null && !string.IsNullOrEmpty(menu.scoreStatus))
            {
                var small = new GUIStyle(line) { fontSize = Mathf.RoundToInt(size * 0.75f) };
                small.normal.textColor = new Color(0.8f, 0.9f, 0.8f);
                PanelText(menu.scoreStatus, new Rect(panel.x, panel.y + row * 3.2f, w, row), small, large: false);
            }

            // GUI.Button draws a button and returns true on the frame it's clicked.
            // With menus: PLAY AGAIN and MENU side by side. Without: just PLAY AGAIN.
            // The small 64 x 14 buttons from the UI art (UiKit), at the game's pixel scale, centred on row 5.
            // Without the art they fall back to plain buttons of the old size.
            float by = panel.y + row * 4.4f, bh = row * 1.2f;
            var smallArt = UiKit.Get("Normal", UiKit.Small);
            float bw = smallArt != null ? smallArt.rect.width * UiKit.Scale : w * 0.42f;
            if (smallArt != null) { by += (bh - smallArt.rect.height * UiKit.Scale) / 2f; bh = smallArt.rect.height * UiKit.Scale; }
            if (menu != null)
            {
                // Two buttons side by side, a few art pixels apart. Both use the font size that fits
                // the longer label (fonts differ between the editor and browsers).
                float gap = smallArt != null ? 4f * UiKit.Scale : w * 0.04f;
                int font = UiKit.FitFont("PLAY AGAIN", bw, Mathf.RoundToInt(size * 0.8f));
                if (UiKit.Button(new Rect(panel.center.x - gap / 2f - bw, by, bw, bh), "PLAY AGAIN", UiKit.Small, font)) menu.PlayAgain(); // same mode as last time (online or practice)
                if (UiKit.Button(new Rect(panel.center.x + gap / 2f, by, bw, bh), "MENU", UiKit.Small, font)) menu.BackToTitle();
            }
            else if (UiKit.Button(new Rect(panel.center.x - bw / 2f, by, bw, bh), "PLAY AGAIN", UiKit.Small, UiKit.FitFont("PLAY AGAIN", bw, size))) arena.PlayAgain();
        }

        // A line of the result panel, centred in rect: in the pixel font (large = the big one, for the title)
        // if one fits inside the panel's rough edges, otherwise Unity's font in this style.
        static void PanelText(string text, Rect rect, GUIStyle style, bool large)
        {
            float scale = UiKit.Scale;
            var font = PixelFont.Pick(text, rect.width - 16f * scale, scale, large);
            if (font != null) font.Draw(text, rect, scale, style.normal.textColor);
            else GUI.Label(rect, text, style);
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
        static void DrawTextAtPixel(string text, float column, float row, Color color, GUIStyle style, Align align, float sizeScale = 1f)
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
            DrawTextAtScreen(text, screen, color, style, align, sizeScale);
        }

        // How many screen pixels one art pixel of the playing field covers.
        static float ArtScale()
        {
            var cam = Camera.main;
            return cam.pixelHeight / (cam.orthographicSize * 2f) / ArtPixelsPerUnit;
        }

        // Draws text at a screen position (pixels, y up from the bottom), with a dark shadow so it reads on
        // any background. Left = starts at the point, Center = centred on it, Right = ends at it.
        // sizeScale: bigger or smaller than normal (for bumps, pulses and pops).
        static void DrawTextAtScreen(string text, Vector3 screen, Color color, GUIStyle style, Align align, float sizeScale = 1f)
        {
            // GUI's y runs DOWN from the top, while the screen's runs UP from the bottom, so flip it.
            // The large pixel font if there is one (with a 1-pixel shadow), otherwise Unity's font.
            float scale = ArtScale() * sizeScale;
            var font = PixelFont.Pick(text, Screen.width, scale, large: true);
            var anchor = align == Align.Left ? TextAnchor.MiddleLeft : align == Align.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
            if (font != null) { font.Draw(text, screen.x, Screen.height - screen.y, anchor, scale, color, shadow: true); return; }

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
