using UnityEngine;

namespace LootLasso
{
    // GameMusic: all the music.
    //
    //   MENU    on the menus (title, name entry, rankings, how to play, and while searching for a rival).
    //           The first time, the start track plays once and runs straight on into the loop track, which then
    //           repeats. Coming back to the menus later goes straight to the loop.
    //   MATCH   fades in when a match starts, loops while it runs, and fades out when it ends.
    //   STING   on the result panel: the win one, or the lose one (nothing on a draw).
    //
    // Menu and match music crossfade: one fades out while the other fades in.
    // The Arena adds this, and the clips live on the Arena (menuStart, menuLoop, gameplayMusic, winSting, loseSting).
    //
    // The loop tracks should loop seamlessly on their own: an exact number of bars, exported with the reverb
    // tail wrapped back onto the start (FL Studio: "Wrap remainder").
    public class GameMusic : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.6f;
        [Range(0f, 1f)] public float menuVolume = 0.6f;
        [Tooltip("Seconds to fade in at the start of a match (and back on the menus).")]
        public float fadeInTime = 1f;
        [Tooltip("Seconds to fade out when the match ends (and leaving the menus).")]
        public float fadeOutTime = 2f;
        [Range(0f, 1f)] public float stingVolume = 0.8f;

        [Header("Readouts")]
        public bool menuStartPlayed; // the menu's start track has been heard (or skipped)

        AudioSource source;      // the match music
        AudioSource menuStart;   // the menu's start track, played once
        AudioSource menuLoop;    // the menu's loop, after that
        AudioSource stingSource;
        Arena arena;

        void Start()
        {
            arena = GetComponent<Arena>();
            source = MakeSource(arena.gameplayMusic, loop: true);
            menuStart = MakeSource(arena.menuStart, loop: false);
            menuLoop = MakeSource(arena.menuLoop, loop: true);
            stingSource = MakeSource(null, loop: false);
            arena.MatchEnded += OnMatchEnded; // "+=" subscribes: call OnMatchEnded whenever a match ends
        }

        AudioSource MakeSource(AudioClip clip, bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 0f; // 2D
            s.volume = 0f;
            return s;
        }

        void OnDestroy()
        {
            if (arena != null) arena.MatchEnded -= OnMatchEnded;
        }

        void OnMatchEnded()
        {
            var clip = arena.result == "YOU WIN!" ? arena.winSting
                     : arena.result == "RIVAL WINS" ? arena.loseSting
                     : null; // a draw: just the music fading out
            if (clip == null) return;
            stingSource.clip = clip;
            stingSource.volume = stingVolume;
            stingSource.Play();
        }

        void Update()
        {
            // The sting stops as soon as the next match starts or you leave the result panel.
            if (!arena.isOver && stingSource.isPlaying) stingSource.Stop();

            UpdateMatchMusic();
            UpdateMenuMusic();
        }

        void UpdateMatchMusic()
        {
            if (source.clip == null) return;
            if (arena.isPlaying)
            {
                // A new match: start from the top.
                if (!source.isPlaying) { source.volume = 0f; source.Play(); }
                FadeTo(source, volume, fadeInTime);
            }
            else if (source.isPlaying)
            {
                // Match over, or back to the menu: fade out, then stop.
                if (FadeTo(source, 0f, fadeOutTime)) source.Stop();
            }
        }

        // Is the menu music on (playing, or fading out)? Kept by us rather than asking the speakers, because a
        // sound scheduled to start a moment from now doesn't count as "playing" yet.
        bool menuOn;
        double loopStartsAt; // when the loop is due to start after the start track (audio clock), 0 = not waiting

        void UpdateMenuMusic()
        {
            // The menus are showing when there's no match and no result panel.
            bool onMenus = !arena.isPlaying && !arena.isOver && arena.showMenus;

            if (onMenus)
            {
                if (!menuOn) { menuOn = true; StartMenuMusic(); }
                // Safety net: if the loop didn't start on cue after the start track, start it now.
                if (loopStartsAt > 0 && AudioSettings.dspTime > loopStartsAt + 0.5 && !menuLoop.isPlaying && menuLoop.clip != null)
                {
                    menuLoop.Play();
                    loopStartsAt = 0;
                }
                FadeTo(menuStart, menuVolume, fadeInTime);
                FadeTo(menuLoop, menuVolume, fadeInTime);
            }
            else if (menuOn)
            {
                // Leaving the menus: fade out, then stop. Next time, the loop starts again from its beginning.
                bool startDone = FadeTo(menuStart, 0f, fadeOutTime);
                bool loopDone = FadeTo(menuLoop, 0f, fadeOutTime);
                if (startDone && loopDone)
                {
                    menuStart.Stop();
                    menuLoop.Stop();
                    menuOn = false;
                    loopStartsAt = 0;
                }
            }
        }

        void StartMenuMusic()
        {
            menuStart.volume = menuLoop.volume = 0f;

            if (!menuStartPlayed && menuStart.clip != null)
            {
                // First time: the start track, then the loop exactly when it ends. PlayScheduled starts a sound
                // at a precise time on the audio clock (dspTime), so there's no gap between the two.
                // A clip's length = its number of samples / samples per second.
                double now = AudioSettings.dspTime + 0.05; // a moment from now, so both can be lined up
                double startLength = (double)menuStart.clip.samples / menuStart.clip.frequency;
                menuStart.PlayScheduled(now);
                if (menuLoop.clip != null)
                {
                    loopStartsAt = now + startLength;
                    menuLoop.PlayScheduled(loopStartsAt);
                }
                menuStartPlayed = true;
            }
            else if (menuLoop.clip != null)
            {
                menuLoop.Play(); // back on the menus later: straight to the loop
            }
        }

        // Moves a speaker's volume toward "target", taking "seconds" for a full fade (MoveTowards steps a fixed
        // amount per second). Returns true once it's there.
        bool FadeTo(AudioSource s, float target, float seconds)
        {
            float full = Mathf.Max(volume, menuVolume, 0.01f);
            s.volume = Mathf.MoveTowards(s.volume, target, full / Mathf.Max(seconds, 0.01f) * Time.deltaTime);
            return Mathf.Approximately(s.volume, target);
        }
    }
}
