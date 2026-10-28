using UnityEngine;
using UnityEngine.Rendering.Universal; // Light2D

namespace LootLasso
{
    // DayNight: each match runs from high noon to midnight.
    //
    //   NOON → SUNSET   the light slowly warms from white to gold to a deep orange dusk.
    //   NIGHTFALL       at the song's switch-up (1:25.71, about 34 seconds left) it snaps to night, with a
    //                   shake and a big NIGHTFALL! popup: the "30 seconds left" warning.
    //   NIGHT           a dim moonlit blue for the rest of the match, and on the result panel.
    //   MENUS           back to noon.
    //
    // It works by changing the colour and brightness of the scene's Global Light 2D, which lights every
    // sprite (the HUD and menus are drawn separately, so they stay bright). Like the finale, it follows the
    // match music's position, so nightfall lands exactly on the switch-up.
    // The Arena adds this automatically.
    public class DayNight : MonoBehaviour
    {
        [Tooltip("When night falls, in seconds into the match music (1:25.71 = 85.71).")]
        public float nightfallAt = 85.71f;
        [Tooltip("Seconds the snap to night takes (0 = instant).")]
        public float nightfallTime = 0.25f;

        [Header("Light colours")]
        public Color noon = Color.white;
        public Color golden = new Color(1f, 0.88f, 0.65f);  // late afternoon
        public Color dusk = new Color(1f, 0.6f, 0.4f);      // just before nightfall
        public Color night = new Color(0.45f, 0.55f, 1f);   // moonlight
        [Header("Light brightness")]
        public float dayIntensity = 1f;
        public float duskIntensity = 0.85f;
        [Tooltip("Not too dark: the treasure still has to be easy to see.")]
        public float nightIntensity = 0.65f;

        [Header("Readouts")]
        public bool isNight;

        Arena arena;
        Light2D sun;
        bool announced; // NIGHTFALL! shown this match

        void Start()
        {
            arena = GetComponent<Arena>();
            // The scene's global light (the one that lights everything evenly).
            foreach (var light in FindObjectsByType<Light2D>())
                if (light.lightType == Light2D.LightType.Global) { sun = light; break; }
            if (sun == null) Debug.LogWarning("DayNight: no Global Light 2D in the scene, so no day and night.");
        }

        void Update()
        {
            if (sun == null) return;

            // On the menus: noon, ready for the next match.
            if (!arena.isPlaying && !arena.isOver)
            {
                Set(noon, dayIntensity);
                isNight = announced = false;
                return;
            }
            // The result panel: stays as it was when the match ended.
            if (!arena.isPlaying) return;

            float song = SongTime();
            isNight = song >= nightfallAt;
            if (!isNight)
            {
                // Noon to dusk: white for the first 40%, then warming to gold, then to dusk at nightfall.
                float t = Mathf.Clamp01(song / nightfallAt);
                Color color = t < 0.4f ? noon
                            : t < 0.75f ? Color.Lerp(noon, golden, (t - 0.4f) / 0.35f)
                            : Color.Lerp(golden, dusk, (t - 0.75f) / 0.25f);
                Set(color, Mathf.Lerp(dayIntensity, duskIntensity, Mathf.InverseLerp(0.6f, 1f, t)));
            }
            else
            {
                // Night falls quickly, then stays.
                float t = nightfallTime > 0f ? Mathf.Clamp01((song - nightfallAt) / nightfallTime) : 1f;
                Set(Color.Lerp(dusk, night, t), Mathf.Lerp(duskIntensity, nightIntensity, t));
                if (!announced)
                {
                    announced = true;
                    ScoreHud.ShowPopup("NIGHTFALL!", new Vector2(0f, arena.MirrorLineY), new Color(0.75f, 0.85f, 1f), big: true);
                    if (Juice.Current != null) Juice.Current.Kick(3f);
                }
            }
        }

        void Set(Color color, float intensity)
        {
            sun.color = color;
            sun.intensity = intensity;
        }

        // The match music's position, or the match clock if it isn't playing (same as the finale).
        float SongTime()
        {
            float song = GameMusic.Current != null ? GameMusic.Current.SongTime : -1f;
            return song >= 0f ? song : arena.MatchTime;
        }
    }
}
