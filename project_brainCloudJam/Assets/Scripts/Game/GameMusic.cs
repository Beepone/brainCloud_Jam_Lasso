using UnityEngine;

namespace LootLasso
{
    // GameMusic: the match music. Fades in when a match starts, loops while it runs, and fades out when it
    // ends (so the result panel is quiet). The Arena adds it, and the clip lives on the Arena (gameplayMusic).
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

        AudioSource source;
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
        }

        void Update()
        {
            if (source.clip == null) return;

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
