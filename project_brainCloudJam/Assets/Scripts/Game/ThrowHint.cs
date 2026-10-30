using UnityEngine;

namespace LootLasso
{
    // ThrowHint: until your first throw of each match, a cursor circles your hunter with a fading trail
    // behind it, showing the motion: hold and go round to spin the lasso up.
    //
    // It hides while you're holding or throwing (so it never sits under your finger), comes back if you let go
    // without throwing, and is gone for the rest of the match after your first real throw.
    // Drawn with OnGUI at the game's pixel scale, like the HUD. The Arena adds this component automatically.
    public class ThrowHint : MonoBehaviour
    {
        [Tooltip("How far from the hunter the cursor circles, in art pixels.")]
        public float radius = 18f;
        [Tooltip("Seconds for one lap around the hunter.")]
        public float lapTime = 1.2f;
        [Tooltip("Seconds into the match before it first shows.")]
        public float delay = 0.6f;
        [Tooltip("How many fading dots trail behind the cursor.")]
        public int trailDots = 10;

        Arena arena;
        LassoSpin mine;   // your lasso this match
        bool wasPlaying;  // was a match running last frame? (to spot a new one starting)
        bool thrown;      // you've thrown this match: no more hint
        float startTime;

        void Start() => arena = GetComponent<Arena>();

        void OnDestroy()
        {
            if (mine != null) mine.Thrown -= OnThrown;
        }

        void Update()
        {
            bool newMatch = arena.isPlaying && !wasPlaying;
            wasPlaying = arena.isPlaying;
            if (!newMatch) return;

            thrown = false;
            startTime = Time.time;
            // Your lasso: the one on your side (online, the guest plays the top one).
            if (mine != null) mine.Thrown -= OnThrown;
            mine = null;
            foreach (var lasso in FindObjectsByType<LassoSpin>())
                if (lasso.side == arena.localSide) mine = lasso;
            if (mine != null) mine.Thrown += OnThrown;
        }

        void OnThrown(LassoSpin _, LassoSpin.ThrowInfo __) => thrown = true;

        void OnGUI()
        {
            if (!arena.isPlaying || thrown || mine == null || MainMenu.OptionsOpen) return;
            if (mine.control != LassoSpin.Control.Pointer || !mine.IsIdle) return;
            float t = Time.time - startTime - delay;
            if (t < 0f || Camera.main == null) return;

            // The hunter on screen. WorldToScreenPoint counts y up from the bottom; GUI counts down from the top.
            Vector3 hunter = Camera.main.WorldToScreenPoint(mine.transform.position);
            var centre = new Vector2(hunter.x, Screen.height - hunter.y);
            float scale = UiKit.Scale;
            float r = radius * scale;
            float fadeIn = Mathf.Clamp01(t / 0.4f);
            float angle = t / lapTime * Mathf.PI * 2f;

            // The trail: dots behind the cursor, fainter the further back.
            var oldColor = GUI.color;
            float dot = Mathf.Round(scale);
            for (int i = 1; i <= trailDots; i++)
            {
                float a = angle - i * 0.22f;
                var p = centre + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * r;
                GUI.color = Palette.WithAlpha(Palette.Bone, fadeIn * 0.6f * (1f - i / (float)(trailDots + 1)));
                GUI.DrawTexture(new Rect(Mathf.Round(p.x - dot), Mathf.Round(p.y - dot), dot * 2f, dot * 2f), Texture2D.whiteTexture);
            }

            // The cursor, its tip on the circle.
            var tip = centre + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * r;
            var icon = CursorIcon;
            GUI.color = new Color(1f, 1f, 1f, fadeIn);
            GUI.DrawTexture(new Rect(Mathf.Round(tip.x), Mathf.Round(tip.y), icon.width * scale, icon.height * scale), icon);
            GUI.color = oldColor;
        }

        // The cursor picture: an arrow pointer, tip at the top-left. '#' = outline (Night), 'o' = fill (Bone).
        static readonly string[] CursorRows =
        {
            "#.......",
            "##......",
            "#o#.....",
            "#oo#....",
            "#ooo#...",
            "#oooo#..",
            "#ooooo#.",
            "#oooooo#",
            "#ooo####",
            "#o#o#...",
            "##.#o#..",
            "#..#o#..",
            "....##..",
        };
        static Texture2D cursor;
        static Texture2D CursorIcon
        {
            get
            {
                if (cursor != null) return cursor;
                int w = CursorRows[0].Length, h = CursorRows.Length;
                cursor = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) // textures count rows from the bottom
                    {
                        char c = CursorRows[y][x];
                        cursor.SetPixel(x, h - 1 - y, c == '#' ? Palette.Night : c == 'o' ? Palette.Bone : Color.clear);
                    }
                cursor.Apply();
                return cursor;
            }
        }
    }
}
