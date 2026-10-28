using System.Collections.Generic; // List<T>
using UnityEngine;

namespace LootLasso
{
    // Juice: the little extras that make the game feel alive. It only listens to what the lassos and the
    // Arena are doing, so none of the game code depends on it.
    //
    //   SCREEN SHAKE   steals (a bigger shake when YOU get robbed) and a tiny one when your loop bounces.
    //   COIN BURST     gold sparks pop out of each treasure as it lands in a hoard, and that score bumps.
    //   STEAL FLASH    the robbed hoard flashes, and STOLEN! pops up big with a wobble (see ScoreHud).
    //   FINALE         the match music's last 16 beats (4 bars, the kick on every beat): a small shake on
    //                  each kick, and on every half beat (with the guitar strums) for the last 4. The timer
    //                  counts down on the beat too (see FinalCountdown and ScoreHud).
    //
    // The Arena adds this automatically.
    public class Juice : MonoBehaviour
    {
        [Header("Screen shake (in art pixels)")]
        public float shakeYouSteal = 2f;
        public float shakeYouGetRobbed = 4f;
        public float shakeBounce = 1f;
        [Tooltip("Seconds a shake takes to die away.")]
        public float shakeTime = 0.3f;

        [Header("Coin burst")]
        public int sparksPerTreasure = 7;
        public float sparkSpeed = 2.5f;   // world units per second
        public float sparkLife = 0.45f;   // seconds
        static readonly Color[] SparkColors = { new Color(1f, 0.9f, 0.3f), new Color(1f, 0.75f, 0.15f), Color.white };

        [Header("Steal flash")]
        public float flashTime = 0.5f;
        static readonly Color FlashColor = new Color(1f, 0.45f, 0.85f, 0.6f); // pink, like STOLEN!

        [Header("Finale (the end of the match music)")]
        [Tooltip("The match music's tempo.")]
        public float bpm = 112f;
        [Tooltip("How many beats before the end the finale starts (the kick on every beat).")]
        public int finaleBeats = 16;
        [Tooltip("The last this-many beats shake on every half beat (the guitar strums).")]
        public int doubleTimeBeats = 4;
        [Tooltip("Shake on each finale hit, in art pixels.")]
        public float shakeFinale = 1f;
        [Tooltip("Nudge the finale earlier (-) or later (+), in seconds, if the shakes don't land on the kicks.")]
        public float musicOffset = 0f;

        public static Juice Current { get; private set; }
        void Awake() => Current = this;

        const float ArtPixel = 1f / 23f; // one art pixel in world units

        Arena arena;
        readonly List<LassoSpin> lassos = new List<LassoSpin>();

        // Shake: how strong right now (art pixels), and the offset we added to the camera last frame.
        float shake;
        Vector3 shakeOffset;

        // One spark of a coin burst.
        class Spark { public SpriteRenderer sr; public Vector2 velocity; public float age; }
        readonly List<Spark> sparks = new List<Spark>();
        readonly Stack<SpriteRenderer> sparePool = new Stack<SpriteRenderer>(); // reused, so bursts make no garbage

        // Steal flash: when each hoard (world side) was last robbed.
        float bottomFlashStart = -99f, topFlashStart = -99f;

        // Finale: the last half beat we shook on (so each one shakes once).
        int lastHalfBeat = -1;

        void Start()
        {
            arena = GetComponent<Arena>();
            foreach (var lasso in FindObjectsByType<LassoSpin>())
            {
                lassos.Add(lasso);
                lasso.Bounced += OnBounced;
                lasso.Stole += OnStole;
                lasso.Delivered += OnDelivered;
            }
        }

        void OnDestroy()
        {
            foreach (var lasso in lassos)
            {
                if (lasso == null) continue;
                lasso.Bounced -= OnBounced;
                lasso.Stole -= OnStole;
                lasso.Delivered -= OnDelivered;
            }
        }

        bool IsMine(LassoSpin lasso) => lasso.side == arena.localSide;

        // ===== What happened =====

        void OnBounced(LassoSpin lasso)
        {
            if (IsMine(lasso)) Shake(shakeBounce); // only your own throws: the rival bouncing isn't your business
        }

        void OnStole(LassoSpin lasso, int value)
        {
            Shake(IsMine(lasso) ? shakeYouSteal : shakeYouGetRobbed);
            // The robbed hoard is the OTHER side's.
            if (lasso.side == LassoSpin.Side.You) topFlashStart = Time.time;
            else bottomFlashStart = Time.time;
        }

        void OnDelivered(LassoSpin lasso, List<Treasure> pieces)
        {
            foreach (var t in pieces) Burst(t.transform.position);
            ScoreHud.Bump(IsMine(lasso)); // that player's score text bounces
        }

        // ===== Every frame =====

        void Update()
        {
            UpdateSparks();
            UpdateFlash();
            UpdateFinale();
        }

        // LateUpdate: after everything else has placed the camera this frame.
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            // Take last frame's shake off, then add this frame's (so the camera never drifts away).
            cam.transform.position -= shakeOffset;
            shakeOffset = Vector3.zero;
            if (shake > 0.01f && (arena.isPlaying || arena.isOver)) // (isOver: the very last hit lands as the match ends)
            {
                // A random direction each frame, as long as "shake" art pixels. The Pixel Perfect Camera snaps
                // it to whole art pixels, which suits the style.
                Vector2 jolt = Random.insideUnitCircle.normalized * shake * ArtPixel;
                shakeOffset = new Vector3(jolt.x, jolt.y, 0f);
                cam.transform.position += shakeOffset;
            }
            // Dies away: from full to nothing in shakeTime.
            shake = Mathf.MoveTowards(shake, 0f, Mathf.Max(shakeYouGetRobbed, 1f) / Mathf.Max(shakeTime, 0.01f) * Time.deltaTime);
        }

        void Shake(float pixels) => shake = Mathf.Max(shake, pixels); // a smaller shake doesn't cut a big one short

        // ===== Coin burst =====

        void Burst(Vector2 at)
        {
            var cam = Camera.main;
            Vector2 up = cam != null ? (Vector2)cam.transform.up : Vector2.up; // screen-up (the guest's camera is upside down)
            for (int i = 0; i < sparksPerTreasure; i++)
            {
                var sr = sparePool.Count > 0 ? sparePool.Pop()
                       : PlaceholderArt.CreateSprite("Spark", PlaceholderArt.Square, Color.white, YSort.Rope - 1, transform);
                sr.gameObject.SetActive(true);
                sr.color = SparkColors[Random.Range(0, SparkColors.Length)];
                float size = (Random.value < 0.3f ? 2f : 1f) * ArtPixel; // mostly 1-pixel sparks, a few 2-pixel ones
                sr.transform.localScale = new Vector3(size, size, 1f);
                sr.transform.position = at;
                // Spray mostly upward on screen, in a fan.
                Vector2 dir = Quaternion.Euler(0f, 0f, Random.Range(-70f, 70f)) * up;
                sparks.Add(new Spark { sr = sr, velocity = dir * sparkSpeed * Random.Range(0.5f, 1f) });
            }
        }

        void UpdateSparks()
        {
            var cam = Camera.main;
            Vector2 down = cam != null ? -(Vector2)cam.transform.up : Vector2.down;
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var s = sparks[i];
                s.age += Time.deltaTime;
                if (s.age >= sparkLife)
                {
                    s.sr.gameObject.SetActive(false);
                    sparePool.Push(s.sr);
                    sparks.RemoveAt(i);
                    continue;
                }
                s.velocity += down * 9f * Time.deltaTime; // a little gravity, toward the bottom of the screen
                s.sr.transform.position += (Vector3)(s.velocity * Time.deltaTime);
                // Blink out at the end instead of fading (crisper for pixel art).
                s.sr.enabled = s.age < sparkLife * 0.7f || Mathf.Repeat(s.age * 20f, 1f) < 0.5f;
            }
        }

        // ===== Steal flash =====

        void UpdateFlash()
        {
            Flash(LassoSpin.Side.You, bottomFlashStart);
            Flash(LassoSpin.Side.Rival, topFlashStart);
        }

        void Flash(LassoSpin.Side side, float start)
        {
            var zone = arena.HoardZone(side);
            if (zone == null) return;
            float t = (Time.time - start) / flashTime; // 0 = just robbed, 1 = done
            Color normal = arena.HoardZoneColor(side);
            if (t >= 1f) { zone.color = normal; return; }
            // Two quick blinks, fading out.
            float blink = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * (1f - t);
            zone.color = Color.Lerp(normal, FlashColor, blink);
        }

        // ===== Finale =====

        float BeatLength => 60f / Mathf.Max(bpm, 1f); // seconds per beat

        // Where we are in the song: the match music's real position if it's playing (so we land exactly on its
        // beats), otherwise the match clock.
        float SongTime()
        {
            float song = GameMusic.Current != null ? GameMusic.Current.SongTime : -1f;
            return (song >= 0f ? song : arena.MatchTime) + musicOffset;
        }

        // Beats into the finale (0 = its first kick), or negative before it starts.
        float FinaleBeat()
        {
            float finaleStart = arena.matchLength - finaleBeats * BeatLength;
            return (SongTime() - finaleStart) / BeatLength;
        }

        void UpdateFinale()
        {
            if (!arena.isPlaying) { lastHalfBeat = -1; return; }
            float beat = FinaleBeat();
            if (beat < 0f || beat > finaleBeats) return;

            int halfBeat = Mathf.FloorToInt(beat * 2f); // counts every half beat
            if (halfBeat == lastHalfBeat) return;
            lastHalfBeat = halfBeat;

            bool onBeat = halfBeat % 2 == 0;
            bool doubleTime = beat >= finaleBeats - doubleTimeBeats;
            if (onBeat || doubleTime) Shake(shakeFinale);
        }

        // The timer during the finale, for ScoreHud: the number to show (8 down to 1, each held for 2 beats so
        // it changes on a kick), and how many seconds it's been showing (for the pulse).
        // Returns false outside the finale, when the timer shows the real time.
        public bool FinalCountdown(out int number, out float shownFor)
        {
            number = 0;
            shownFor = 0f;
            if (arena == null || !arena.isPlaying) return false;
            float beat = FinaleBeat();
            if (beat < 0f) return false;
            int beatsPerNumber = 2;
            int step = Mathf.FloorToInt(beat / beatsPerNumber);
            number = Mathf.Max(finaleBeats / beatsPerNumber - step, 0); // 8, 7 ... 1, then 0 on the final hit
            shownFor = (beat - step * beatsPerNumber) * BeatLength;
            return true;
        }
    }
}
