using UnityEngine;

namespace LootLasso
{
    // GameMusic: the match music. Fades in when a match starts, loops while it runs, and fades out when it
    // ends. Then the result panel plays a sting: the win one, or the lose one (nothing on a draw).
    // The Arena adds it, and the clips live on the Arena (gameplayMusic, winSting, loseSting).
    //
    // The track should loop seamlessly on its own: an exact number of bars, exported with the reverb tail
    // wrapped back onto the start (FL Studio: "Wrap remainder").
    public class GameMusic : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.6f;
        [Tooltip("Seconds to fade in at the start of a match.")]
        public float fadeInTime = 1f;
        [Tooltip("Seconds to fade out when the match ends.")]
        public float fadeOutTime = 2f;
        [Range(0f, 1f)] public float stingVolume = 0.8f;

        AudioSource source;
        AudioSource stingSource;
        Arena arena;

        void Start()
        {
            arena = GetComponent<Arena>();
            source = gameObject.AddComponent<AudioSource>();
            source.clip = arena.gameplayMusic;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f; // 2D
            source.volume = 0f;

            stingSource = gameObject.AddComponent<AudioSource>();
            stingSource.playOnAwake = false;
            stingSource.spatialBlend = 0f;
            arena.MatchEnded += OnMatchEnded; // "+=" subscribes: call OnMatchEnded whenever a match ends
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
            if (source.clip == null) return;

            // The sting stops as soon as the next match starts or you leave the result panel.
            if (!arena.isOver && stingSource.isPlaying) stingSource.Stop();

            if (arena.isPlaying)
            {
                // A new match: start from the top.
                if (!source.isPlaying) { source.volume = 0f; source.Play(); }
                // MoveTowards: step a fixed amount per second toward the target, so the fade takes fadeInTime.
                source.volume = Mathf.MoveTowards(source.volume, volume, volume / Mathf.Max(fadeInTime, 0.01f) * Time.deltaTime);
            }
            else if (source.isPlaying)
            {
                // Match over, or back to the menu: fade out, then stop.
                source.volume = Mathf.MoveTowards(source.volume, 0f, volume / Mathf.Max(fadeOutTime, 0.01f) * Time.deltaTime);
                if (source.volume <= 0f) source.Stop();
            }
        }
    }
}
