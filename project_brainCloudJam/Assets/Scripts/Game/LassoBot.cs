using UnityEngine;

namespace LootLasso
{
    // LassoBot: a computer rival. It plays with the SAME lasso as you (a LassoSpin on the same object),
    // but instead of a mouse it fakes a hand in code: it circles the hand around its character, speeds
    // up to the spin that gives the right distance, and lets go when the loop is swinging toward the target.
    // So it follows exactly the same rules (momentum, backlash, distance from spin speed).
    //
    // Each throw:
    //   THINK   wait a moment, then pick a target: treasure in the field, or in your hoard (a steal)
    //   SPIN    circle the hand, matching the spin speed that throws that far
    //   RELEASE let go as the loop passes the right angle (with a little human-like error)
    //   WAIT    until the lasso is back, then think again
    //
    // [DefaultExecutionOrder(-10)] makes Unity run this script's Update before other scripts' (default 0),
    // so the fake hand is ready before LassoSpin reads it each frame.
    // [RequireComponent] makes Unity add a LassoSpin automatically when you add a LassoBot.
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(LassoSpin))]
    public class LassoBot : MonoBehaviour
    {
        [Header("Difficulty")]
        [Tooltip("Pause between throws, in seconds (a random time between min and max).")]
        public float minThinkTime = 0.9f;
        public float maxThinkTime = 1.8f;
        [Tooltip("How many degrees off the bot's release can be. Higher = misses more.")]
        public float aimError = 12f;
        [Tooltip("How far off its distance can be, as a fraction (0.12 = up to 12% short or long).")]
        public float distanceError = 0.12f;
        [Tooltip("How much more the bot likes stealing from your hoard than grabbing from the field (1 = no preference).")]
        public float stealPreference = 1.5f;
        [Tooltip("How much the bot prefers close targets. Higher = it mostly goes for nearby treasure.")]
        public float distanceDislike = 0.25f;

        [Header("Hand")]
        [Tooltip("How far from its character the bot circles its hand (world units). Sets its loop's orbit.")]
        public float handRadius = 0.8f;

        [Header("Readouts")]
        public string state;
        public string targetName;
        public float targetSpinSpeed;

        // The hand LassoSpin reads this frame.
        public LassoSpin.Hand CurrentHand { get; private set; }

        enum State { Thinking, Spinning, Waiting }
        State phase = State.Thinking;

        LassoSpin spin;
        Treasure target;
        float thinkUntil;      // Time.time when thinking is done
        float spinStart;       // when the current spin started (to give up if it takes too long)
        float handAngle;       // where the fake hand is around the character (degrees)
        float handSpeed;       // how fast the fake hand is circling (degrees per second)
        float angleError;      // this throw's aim error (picked once per throw)
        float distanceFactor;  // this throw's distance error (picked once per throw)
        float lastAngleToRelease;
        bool wasPressed;

        void Awake()
        {
            spin = GetComponent<LassoSpin>();
            spin.side = LassoSpin.Side.Rival; // the bot plays for the rival's hoard
        }

        // Called by the Arena when a new match starts.
        public void ResetBot()
        {
            phase = State.Thinking;
            thinkUntil = Time.time + Random.Range(minThinkTime, maxThinkTime);
            target = null;
            handSpeed = 0f;
            CurrentHand = default; // "default" = everything false / zero: hand up, not touching
        }

        void Update()
        {
            var arena = Arena.Current;
            if (arena == null || arena.isOver) { CurrentHand = default; return; }

            Vector2 me = transform.position;
            bool held = false, pressed = false, released = false;

            switch (phase)
            {
                case State.Thinking:
                    if (Time.time >= thinkUntil && spin.IsIdle && PickTarget(arena, me))
                    {
                        phase = State.Spinning;
                        spinStart = Time.time;
                        handAngle = Random.Range(0f, 360f);
                        handSpeed = 0f;
                        lastAngleToRelease = float.NaN; // "not a number" = no previous frame yet
                        pressed = true;
                        held = true;
                    }
                    break;

                case State.Spinning:
                    held = true;
                    if (SpinAndMaybeRelease(arena, me)) { held = false; released = true; phase = State.Waiting; }
                    break;

                case State.Waiting:
                    // Wait for the throw to come back, then think about the next one.
                    if (spin.IsIdle)
                    {
                        phase = State.Thinking;
                        thinkUntil = Time.time + Random.Range(minThinkTime, maxThinkTime);
                    }
                    break;
            }

            // The fake hand: handRadius away from the character, at handAngle.
            CurrentHand = new LassoSpin.Hand
            {
                held = held,
                pressed = pressed,
                released = released,
                worldPos = me + Dir(handAngle) * handRadius,
            };
            state = phase.ToString();
        }

        // ----- Choosing a target -----

        // Scores every treasure it could catch and picks one of the best. Returns false if there's nothing.
        bool PickTarget(Arena arena, Vector2 me)
        {
            Rect home = arena.rivalHoard; // the bot's own hoard: never steal from it
            Treasure best = null;
            float bestScore = 0f;

            foreach (var t in arena.treasures)
            {
                bool loose = t.state == Treasure.State.Free;
                bool steal = t.state == Treasure.State.InHoard && t.hoard != home;
                if (!loose && !steal) continue;

                float travel = TravelDistance(me, t.transform.position);
                if (float.IsNaN(travel) || travel > spin.maxRange * 0.95f) continue; // out of reach

                // Worth more = better; farther away = worse (it takes longer to haul back).
                float score = t.value / (1f + travel * distanceDislike);
                if (steal) score *= stealPreference;
                score *= Random.Range(0.7f, 1.3f); // a little randomness, so it doesn't always pick the same thing

                if (score > bestScore) { bestScore = score; best = t; }
            }

            target = best;
            targetName = best != null ? best.name : "";
            angleError = Random.Range(-aimError, aimError);
            distanceFactor = 1f + Random.Range(-distanceError, distanceError);
            return best != null;
        }

        // ----- Spinning and releasing -----

        // Returns true on the frame it lets go.
        bool SpinAndMaybeRelease(Arena arena, Vector2 me)
        {
            float dt = Time.deltaTime;

            // The target may have wandered off, been caught by you, or (taking too long) give up and throw anyway.
            bool targetGone = target == null || target.state == Treasure.State.Hooked
                           || (target.state == Treasure.State.InHoard && target.hoard == arena.rivalHoard);
            if (targetGone || Time.time - spinStart > 5f) return Mathf.Abs(spin.loopSpeed) > spin.minSpinSpeed;

            Vector2 targetPos = target.transform.position;
            float orbit = Mathf.Clamp(handRadius, spin.minOrbitRadius, spin.maxOrbitRadius);

            // How fast must the loop spin to throw exactly as far as the target? (LassoSpin's distance math, backwards.)
            float travel = TravelDistance(me, targetPos) * distanceFactor;
            targetSpinSpeed = SpinSpeedFor(travel);

            // Speed the hand up toward that spin, a bit gentler than the loop's max acceleration so the
            // loop can keep up (whipping it faster would make it backlash, exactly like for you).
            handSpeed = Mathf.MoveTowards(handSpeed, targetSpinSpeed, spin.maxAcceleration * 0.8f * dt);
            handAngle = Mathf.Repeat(handAngle + handSpeed * dt, 360f);

            // Only let go once the loop itself has reached (about) the right speed.
            bool upToSpeed = Mathf.Abs(spin.loopSpeed - targetSpinSpeed) < targetSpinSpeed * 0.05f;

            // The release angle: where the loop must be so its tangent points at the target (see ReleaseAngle).
            float releaseAt = ReleaseAngle(me, targetPos, orbit) + angleError;
            float angleToRelease = Mathf.DeltaAngle(releaseAt, spin.LoopAngle); // negative = not there yet

            // Let go on the frame the loop passes the release angle (it was just before, now it's at or past).
            bool passed = !float.IsNaN(lastAngleToRelease) && lastAngleToRelease < 0f && angleToRelease >= 0f && angleToRelease < 90f;
            lastAngleToRelease = angleToRelease;
            return upToSpeed && passed;
        }

        // ----- Geometry -----

        // A released lasso flies off along the loop's circle, sideways to the rope (the "tangent"). That line
        // passes the character at exactly the orbit distance r. So for a target at distance d, the right-angled
        // triangle (character, release point, target) has sides r and d, and:
        //   - the throw travels sqrt(d^2 - r^2) (Pythagoras)
        //   - the release point is acos(r / d) degrees around from the target's direction
        // The loop spins counterclockwise (angle increasing), so the release point is that far *behind* the target.

        float TravelDistance(Vector2 me, Vector2 targetPos)
        {
            float orbit = Mathf.Clamp(handRadius, spin.minOrbitRadius, spin.maxOrbitRadius);
            float d = Vector2.Distance(me, targetPos);
            if (d <= orbit) return float.NaN; // inside the loop's own circle: can't throw there
            return Mathf.Sqrt(d * d - orbit * orbit);
        }

        static float ReleaseAngle(Vector2 me, Vector2 targetPos, float orbit)
        {
            Vector2 toTarget = targetPos - me;
            float targetAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            float offset = Mathf.Acos(Mathf.Clamp(orbit / toTarget.magnitude, -1f, 1f)) * Mathf.Rad2Deg;
            return targetAngle - offset;
        }

        // LassoSpin turns spin speed into distance: distance = Lerp(minRange, maxRange, spin01 ^ curve).
        // This does it in reverse: distance -> spin01 -> spin speed.
        float SpinSpeedFor(float distance)
        {
            float power = Mathf.InverseLerp(spin.minRange, spin.maxRange, distance); // 0..1
            float spin01 = Mathf.Pow(power, 1f / spin.distanceCurve);                // undo the curve
            return Mathf.Lerp(spin.minSpinSpeed, spin.maxSpinSpeed, spin01) * 1.02f; // tiny margin above the minimum
        }

        static Vector2 Dir(float degrees) =>
            new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
    }
}
