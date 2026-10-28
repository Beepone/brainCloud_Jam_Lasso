using UnityEngine;

namespace LootLasso
{
    // LassoSound: the sounds of one lasso. It only listens: it watches what its LassoSpin is doing and
    // plays along, so the lasso code doesn't need to know about audio.
    //
    //   TWIRL  a loop while winding up. Pitch rises with spin speed, and it reaches full volume once
    //          you're spinning fast enough to throw (a cue that you can let go).
    //   THROW  plays when the loop flies off, and fades out quickly when it lands.
    //   GRAB     a one-shot when something is caught on the rope.
    //   BOUNCE   the loop hits a wall.
    //   DELIVER  a chime per treasure landing in the hoard, a beat apart with slightly different pitches,
    //            so a big haul sounds like a cascade. Heavier treasure (chest, idol) chimes lower.
    //   STEAL    when this lasso steals: one sound if it's YOU stealing, another if it's the rival
    //            (always full volume: losing treasure is news you need to hear).
    //
    // The clips live on the Arena (one place to swap them). The Arena adds this to every lasso.
    // The other player's lasso (bot or online rival) plays quieter, so yours stands out.
    [RequireComponent(typeof(LassoSpin))]
    public class LassoSound : MonoBehaviour
    {
        [Range(0f, 1f)] public float twirlVolume = 0.8f;
        [Range(0f, 1f)] public float throwVolume = 1f;
        [Range(0f, 1f)] public float grabVolume = 1f;
        [Range(0f, 1f)] public float bounceVolume = 0.7f;
        [Range(0f, 1f)] public float deliverVolume = 0.8f;
        [Range(0f, 1f)] public float stealVolume = 1f;
        [Tooltip("Seconds between the chimes of a multi-treasure delivery.")]
        public float deliverGap = 0.07f;
        [Tooltip("Random pitch change per chime (0.06 = up to 6% higher or lower).")]
        public float deliverPitchJitter = 0.06f;
        [Tooltip("Volume multiplier for the other player's lasso (the bot, or the online rival).")]
        [Range(0f, 1f)] public float otherPlayerVolume = 0.5f;
        [Tooltip("Twirl pitch when barely spinning, and at top speed.")]
        public float twirlMinPitch = 0.8f;
        public float twirlMaxPitch = 1.4f;
        [Tooltip("When the loop lands, the throw sound fades out over this many seconds.")]
        public float throwFadeTime = 0.1f;

        LassoSpin spin;
        AudioSource twirlSource, throwSource, grabSource, effectSource;
        // A few speakers for the delivery chimes, taken in turn: each chime has its own pitch, and changing
        // a speaker's pitch would also change a chime still ringing on it.
        readonly AudioSource[] chimeSources = new AudioSource[4];
        int nextChime;
        bool wasFlying;
        bool fadingThrow;

        // Yours at full volume; the other player's quieter.
        float Loudness => spin.control == LassoSpin.Control.Pointer ? 1f : otherPlayerVolume;

        void Start()
        {
            spin = GetComponent<LassoSpin>();
            spin.Hooked += OnHooked;
            spin.Bounced += OnBounced;
            spin.Stole += OnStole;
            spin.Delivered += OnDelivered;
            spin.Snapped += OnSnapped;

            var arena = Arena.Current;
            // Three AudioSources (speakers) on this object: one per sound, so they never cut each other off.
            twirlSource = MakeSource(arena != null ? arena.lassoTwirl : null, loop: true);
            throwSource = MakeSource(arena != null ? arena.lassoThrow : null, loop: false);
            grabSource = MakeSource(arena != null ? arena.lassoGrab : null, loop: false);
            effectSource = MakeSource(null, loop: false); // bounces and steals (PlayOneShot picks the clip)
            for (int i = 0; i < chimeSources.Length; i++) chimeSources[i] = MakeSource(arena != null ? arena.delivered : null, loop: false);
        }

        void OnDestroy()
        {
            if (spin == null) return;
            spin.Hooked -= OnHooked;
            spin.Bounced -= OnBounced;
            spin.Stole -= OnStole;
            spin.Delivered -= OnDelivered;
            spin.Snapped -= OnSnapped;
        }

        AudioSource MakeSource(AudioClip clip, bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f; // 2D sound: same volume wherever the lasso is on screen
            return source;
        }

        void Update()
        {
            var arena = Arena.Current;
            bool playing = arena != null && arena.isPlaying;

            UpdateTwirl(playing);

            // The throw: starts when the loop takes off (our throw, the bot's, or one replayed from online)...
            bool flying = playing && spin.IsFlying;
            if (flying && !wasFlying && throwSource.clip != null)
            {
                fadingThrow = false;
                throwSource.volume = throwVolume * Loudness;
                throwSource.Play();
            }
            // ...and fades out when it lands (or the match ends).
            if (!flying && wasFlying && throwSource.isPlaying) fadingThrow = true;
            wasFlying = flying;

            if (fadingThrow)
            {
                // MoveTowards: step toward 0 by a fixed amount per second, reaching it in throwFadeTime.
                float step = throwVolume / Mathf.Max(throwFadeTime, 0.001f) * Time.deltaTime;
                throwSource.volume = Mathf.MoveTowards(throwSource.volume, 0f, step);
                if (throwSource.volume <= 0f) { throwSource.Stop(); fadingThrow = false; }
            }
        }

        void UpdateTwirl(bool playing)
        {
            if (twirlSource.clip == null) return;
            if (!playing || !spin.IsWindingUp)
            {
                if (twirlSource.isPlaying) twirlSource.Stop();
                return;
            }
            if (!twirlSource.isPlaying) { twirlSource.volume = 0f; twirlSource.Play(); }

            // How fast the loop is spinning: 0 = still, 1 = top speed. Abs because it can spin either way.
            float speed = Mathf.Abs(spin.loopSpeed);
            float speed01 = Mathf.Clamp01(speed / spin.maxSpinSpeed);
            twirlSource.pitch = Mathf.Lerp(twirlMinPitch, twirlMaxPitch, speed01);
            // Fades in as you spin up; full volume from the minimum throwing speed.
            twirlSource.volume = twirlVolume * Loudness * Mathf.Clamp01(speed / spin.minSpinSpeed);
        }

        void OnHooked(LassoSpin _)
        {
            if (grabSource.clip != null) grabSource.PlayOneShot(grabSource.clip, grabVolume * Loudness);
        }

        void OnBounced(LassoSpin _)
        {
            var arena = Arena.Current;
            if (arena != null && arena.ropeBounce != null) effectSource.PlayOneShot(arena.ropeBounce, bounceVolume * Loudness);
        }

        // The rope snapping at the centre: the rope's thwap, a little louder.
        void OnSnapped(LassoSpin _)
        {
            var arena = Arena.Current;
            if (arena != null && arena.ropeBounce != null) effectSource.PlayOneShot(arena.ropeBounce, Mathf.Min(1f, bounceVolume * 1.4f));
        }

        void OnStole(LassoSpin _, int value)
        {
            var arena = Arena.Current;
            if (arena == null) return;
            // Is this lasso the player at this computer? Then YOU stole; otherwise the rival stole from you.
            bool mine = spin.side == arena.localSide;
            var clip = mine ? arena.playerSteals : arena.rivalSteals;
            if (clip != null) effectSource.PlayOneShot(clip, stealVolume);
        }

        void OnDelivered(LassoSpin _, System.Collections.Generic.List<Treasure> pieces)
        {
            var arena = Arena.Current;
            if (arena == null || arena.delivered == null) return;
            // Copy the values now: the lasso clears its list straight after telling us.
            var weights = new float[pieces.Count];
            for (int i = 0; i < pieces.Count; i++) weights[i] = pieces[i].weight;
            StartCoroutine(Chimes(weights));
        }

        // A coroutine: a method that can pause ("yield") and carry on later, here to space out the chimes.
        System.Collections.IEnumerator Chimes(float[] weights)
        {
            int count = Mathf.Min(weights.Length, 6); // a huge haul doesn't need more than 6 chimes
            for (int i = 0; i < count; i++)
            {
                var source = chimeSources[nextChime];
                nextChime = (nextChime + 1) % chimeSources.Length;
                // Heavier treasure chimes lower (weight 1 = normal pitch), plus a little randomness each time.
                float weightPitch = Mathf.Lerp(1f, 0.75f, Mathf.InverseLerp(1f, 4f, weights[i]));
                source.pitch = weightPitch * (1f + Random.Range(-deliverPitchJitter, deliverPitchJitter));
                source.PlayOneShot(source.clip, deliverVolume * Loudness);
                yield return new WaitForSeconds(deliverGap); // wait, then the next chime
            }
        }
    }
}
