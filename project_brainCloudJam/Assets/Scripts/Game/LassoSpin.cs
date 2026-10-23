// "using" lines let this file use code from other places without typing their full names.
using System.Collections.Generic; // List<T> (a list that can grow)
using UnityEngine;                // MonoBehaviour, Vector2, Mathf, Time, Camera, Transform...
using UnityEngine.InputSystem;    // Pointer (the new Input System: mouse and touch)

namespace LootLasso
{
    // LassoSpin: the lasso throw. One motion, like a real lasso or a hammer throw.
    //
    //   WIND UP  Hold the button and circle the pointer around your character. The loop has momentum: it
    //            chases your hand, always trailing a little, and only speeds up gradually.
    //            Spin too fast and it backlashes: the loop gets pulled the other way and slows down.
    //   RELEASE  Let go, and the lasso flies off along the loop's path at that moment. Spin SPEED sets the
    //            distance: normal spins give short throws, only a really fast spin reaches the far side.
    //   FLY      The loop flies out (bouncing off the left/right screen edges), leaving the rope along its path.
    //   CATCH    When it lands it catches all the free treasure inside it. The rope tightens into a straight line,
    //            then the loop is pulled back with the catch. Heavy treasure comes back slowly, so a big haul
    //            ties you up for a while. Whatever reaches you goes into your hoard.
    //
    // "public class LassoSpin : MonoBehaviour" means: a class (a blueprint for an object) named LassoSpin,
    // which is a MonoBehaviour. Being a MonoBehaviour is what lets you attach it to a GameObject as a
    // component, and what makes Unity call methods like Update() on it automatically.
    public class LassoSpin : MonoBehaviour
    {
        // ===== Settings (fields) =====
        // Fields are variables that belong to the object, declared outside any method. They keep their
        // values from frame to frame. "public" fields show up in the Inspector, so you can tweak them,
        // even while playing. [Header] and [Tooltip] only label things in the Inspector.

        [Header("Objects")]
        [Tooltip("The loop (drag the Loop object here). It orbits while spinning and flies when thrown.")]
        public Transform loop;

        // Which player this lasso belongs to. Decides which hoard is "home" (where catches are delivered,
        // and which you can't steal from). The bot will use this same script with side = Rival.
        public enum Side { You, Rival }
        [Tooltip("Whose lasso this is: its catches go to that side's hoard, and it can steal from the other one.")]
        public Side side = Side.You;

        Rect HomeHoard => side == Side.You ? Arena.Current.yourHoard : Arena.Current.rivalHoard;
        [Tooltip("The loop orbits right under your pointer, but never closer than this or farther than max (world units).")]
        public float minOrbitRadius = 0.6f;
        public float maxOrbitRadius = 1f;
        [Tooltip("Radius of the loop when it lands (world units): everything inside it is caught. The same for every throw.")]
        public float catchRadius = 0.5f;

        [Header("Wind-up")]
        [Tooltip("How hard the loop is pulled toward your hand. Higher = trails less. (Degrees per second of speed per degree of lag.)")]
        public float followStrength = 10f;
        [Tooltip("The fastest the loop can speed up, in degrees per second, per second. Lower = longer wind-up.")]
        public float maxAcceleration = 900f;
        [Tooltip("The rope's pull is capped as if your hand were at most this many degrees ahead. Top speed = followStrength x this.")]
        public float slipAngle = 120f;

        [Header("Spin speed -> distance")]
        [Tooltip("Slowest loop speed (degrees per second) that can be thrown. Shortest throw.")]
        public float minSpinSpeed = 180f;   // half a circle per second
        [Tooltip("Loop speed for the longest throw.")]
        public float maxSpinSpeed = 1080f;  // three circles per second
        public float minRange = 1.5f;
        public float maxRange = 12f;
        [Tooltip("How spin speed turns into distance. 1 = straight line. 2 = squared: normal spins stay short, only very fast spins reach far.")]
        public float distanceCurve = 2f;

        [Header("Flight and pull-back")]
        public float minFlightSpeed = 8f;
        public float maxFlightSpeed = 18f;
        [Tooltip("Pull-back speed when the loop is close to the character...")]
        public float nearReturnSpeed = 10f;
        [Tooltip("...and when it's at full throw distance (maxRange). Slow far pulls make long throws (like the rival's hoard) cost time.")]
        public float farReturnSpeed = 4f;

        [Tooltip("After landing, the rope straightens out before the pull starts. Shortest rope (minRange) takes this long...")]
        public float minTightenTime = 0.15f;
        [Tooltip("...and the longest rope (maxRange) takes this long.")]
        public float maxTightenTime = 0.6f;

        [Tooltip("Each unit of weight being hauled slows the pull by this fraction (0.1 = 10% slower per unit). Coin = 1, Chest = 3, Idol = 6.")]
        public float weightSlowdown = 0.1f;

        [Header("Wall bounces")]
        [Tooltip("The lasso bounces off the left and right edges of the screen. The throw distance includes the bounces.")]
        public bool bounceOffWalls = true;
        [Tooltip("Also bounce off the top and bottom edges. Off = a throw that reaches them stops there (they're the hoard ends of the arena).")]
        public bool bounceOffTopAndBottom = false;
        [Tooltip("After this many bounces, the lasso just stops at the next wall.")]
        public int maxBounces = 3;

        // ===== Readouts =====
        // Public only so you can watch them in the Inspector while playing. The code writes them.
        [Header("Readouts (watch these in Play mode)")]
        public string state;          // what the lasso is doing right now
        public float handDistance;    // how far your pointer is from your character, in world units
        public float loopSpeed;       // the loop's own spin speed, degrees per second (+ = counterclockwise)
        public float lag;             // how many degrees your hand is ahead of the loop
        public bool slipping;         // true while you're spinning faster than the rope can pull
        public string lastThrow;      // summary of the last throw
        public int caughtCount;       // how many treasures are on the rope right now
        public float caughtWeight;    // their total weight (heavier = slower pull)

        // ===== Private state =====
        // No "public" = private: hidden from the Inspector, only this script uses them.

        // An "enum" is a fixed list of named options. The lasso is always in exactly one of these.
        //   Tightening  landed: the rope straightens out before the pull starts
        enum Phase { Idle, WindUp, Flying, Tightening, Returning }
        Phase phase = Phase.Idle;

        float loopAngle;        // where the loop is around the character (degrees, 0 = right, 90 = up)
        bool hasHandAngle;      // false until your hand is far enough from the character to read an angle

        // The throw in flight: the corners of its path (each bounce, then the landing point), in order.
        // A List is like an array that can grow: Add() puts an item on the end, [i] reads item i.
        readonly List<Vector2> flightPath = new List<Vector2>();
        int flightPoint;        // which corner of the path the loop is flying toward right now
        float flightSpeed;

        // Visuals made in code (placeholders until the art arrives).
        // ropePieces is an array: a fixed-size row of items. One per straight piece of rope (it bends at each wall bounce).
        SpriteRenderer[] ropePieces;
        static readonly Color RopeColor = new Color(0.85f, 0.7f, 0.45f);

        // The rope's shape: the corners it passes through, from the player to the loop.
        readonly List<Vector2> ropePoints = new List<Vector2>();
        readonly List<Vector2> slackPoints = new List<Vector2>(); // the rope's shape the moment it landed

        // What's on the rope: the caught treasures, and where each sits relative to the loop.
        readonly List<Treasure> hooked = new List<Treasure>();
        readonly List<Vector2> hookedOffsets = new List<Vector2>();
        float tightenStart, tightenTime;  // when tightening started, and how long it takes
        Vector3 loopSpinScale;   // the loop's size while spinning (from the scene)

        // ===== Start =====
        // Unity calls Start once, before the first Update.
        void Start()
        {
            loopSpinScale = loop.localScale;

            // The rope is drawn with the placeholder shapes from the old prototype
            // (Scripts/Lasso/PlaceholderArt.cs). The artists' sprites will replace them.
            // Rope pieces: one from the player to the first bounce (or the loop), then one more per bounce.
            ropePieces = new SpriteRenderer[maxBounces + 1];
            for (int i = 0; i < ropePieces.Length; i++) // "for": run this once for each i = 0, 1, 2, ...
                ropePieces[i] = PlaceholderArt.CreateSprite("Rope " + i, PlaceholderArt.Square, RopeColor, 5);
            HideRope();
            loop.gameObject.SetActive(false);
        }

        // ===== Update =====
        // Unity calls Update once every frame. It works out which phase we're in and runs that part.
        void Update()
        {
            var pointer = Pointer.current; // the mouse on a computer, or the first finger on a phone
            if (pointer == null) return;   // no mouse or touch at all, so do nothing

            // Match over: freeze exactly as we are until Play Again (which calls ResetLasso).
            if (Arena.Current != null && Arena.Current.isOver) return;

            Vector2 playerPos = transform.position;

            // "switch" runs the block for the current phase. "break" ends the block.
            switch (phase)
            {
                case Phase.Idle:
                    if (pointer.press.wasPressedThisFrame) StartWindUp();
                    break;

                case Phase.WindUp:
                    UpdateWindUp(pointer, playerPos);
                    break;

                case Phase.Flying:
                    UpdateFlight();
                    break;

                case Phase.Tightening:
                    UpdateTightening(playerPos);
                    break;

                case Phase.Returning:
                    // The farther out the loop is, the slower it's pulled in. The speed is worked out every
                    // frame from the distance left, so a long throw starts slow and speeds up as it gets close.
                    float out01 = Vector2.Distance(loop.position, playerPos) / maxRange; // 0 = at the player, 1 = full distance
                    float returnSpeed = Mathf.Lerp(nearReturnSpeed, farReturnSpeed, out01);
                    returnSpeed /= 1f + caughtWeight * weightSlowdown; // heavier haul = slower pull
                    loop.position = Vector2.MoveTowards(loop.position, playerPos, returnSpeed * Time.deltaTime);
                    Haul();
                    if ((Vector2)loop.position == playerPos)
                    {
                        Deliver();
                        EndThrow();
                    }
                    break;
            }

            // While the loop is out, draw the rope.
            if (phase == Phase.Flying)
            {
                // The rope lies along the path it flew: player -> each wall bounce passed -> loop.
                ropePoints.Clear();
                ropePoints.Add(playerPos);
                for (int i = 0; i < flightPoint && i < flightPath.Count; i++) ropePoints.Add(flightPath[i]);
                ropePoints.Add(loop.position);
                DrawRope(ropePoints);
            }
            else if (phase == Phase.Returning)
            {
                // Being pulled in: the rope is taut, a straight line.
                ropePoints.Clear();
                ropePoints.Add(playerPos);
                ropePoints.Add(loop.position);
                DrawRope(ropePoints);
            }
            // (Tightening draws its own rope, in UpdateTightening.)

            state = phase.ToString(); // readout
        }

        // ===== Wind-up =====

        void StartWindUp()
        {
            phase = Phase.WindUp;
            hasHandAngle = false; // the loop starts once your hand is far enough from the character
            loopSpeed = 0f;
            lag = 0f;
            slipping = false;
            handDistance = 0f;
            loop.gameObject.SetActive(false); // appears when you start circling
        }

        void UpdateWindUp(Pointer pointer, Vector2 playerPos)
        {
            float dt = Time.deltaTime; // how long this frame took, in seconds

            // ----- Let go? -----
            // wasReleasedThisFrame is true only on the one frame you let go.
            if (pointer.press.wasReleasedThisFrame)
            {
                Release();
                return;
            }

            // ----- 1. Where is your hand, relative to the character? -----
            // In world units, so the loop can sit exactly under your pointer. (The camera scales the world
            // to fit the screen, so this still feels the same on a phone and a big monitor.)
            Vector2 worldPos = Camera.main.ScreenToWorldPoint(pointer.position.ReadValue());
            Vector2 fromPlayer = worldPos - playerPos;
            handDistance = fromPlayer.magnitude;

            // ----- 2. The loop sits right under your pointer -----
            // It orbits exactly as far out as your pointer is, as long as that's between min and max.
            // Clamp keeps a number between a min and a max.
            float orbitRadius = Mathf.Clamp(handDistance, minOrbitRadius, maxOrbitRadius);

            // ----- 3. Momentum: the loop chases your hand -----
            float wantedSpeed = 0f; // hand too close to read (yet): the loop just slows down
            if (handDistance >= minOrbitRadius * 0.3f) // right on top of the character, the angle jitters
            {
                // Your hand's angle around the character (0 = right, 90 = up).
                float handAngle = Mathf.Atan2(fromPlayer.y, fromPlayer.x) * Mathf.Rad2Deg;

                if (!hasHandAngle)
                {
                    // First readable frame: put the loop where your hand is and show it.
                    loopAngle = handAngle;
                    hasHandAngle = true;
                    loop.gameObject.SetActive(true);
                }

                // How far your hand is ahead of the loop (degrees; positive = counterclockwise of the loop).
                // DeltaAngle takes the SHORTEST way around (179 -> -179 = 2). That's deliberate, and it's what
                // makes overspinning BACKLASH: get your hand more than half a turn ahead and it suddenly counts
                // as BEHIND the loop, so the loop gets pulled backwards and slows down. You have to ease off
                // to keep gaining speed, like a real lasso when you yank it. (Don't "fix" this, it's a feature!)
                lag = Mathf.DeltaAngle(loopAngle, handAngle);

                // The rope can only pull so hard: treat your hand as at most slipAngle ahead, so the loop's pull
                // (and so its top speed) is capped. Clamp keeps a number between a min and a max.
                float pull = Mathf.Clamp(lag, -slipAngle, slipAngle);
                slipping = Mathf.Abs(lag) > slipAngle;

                // The further your hand is ahead, the faster the loop WANTS to go (like a stretched spring).
                wantedSpeed = pull * followStrength;
            }

            // The loop has weight, so its speed can only change by maxAcceleration per second.
            // That's the wind-up: getting the loop fast takes a few turns of steady spinning.
            loopSpeed = Mathf.MoveTowards(loopSpeed, wantedSpeed, maxAcceleration * dt);

            if (!hasHandAngle) return; // nothing to show until you've started circling

            // Move the loop by its own speed. Repeat keeps the angle between 0 and 360.
            loopAngle = Mathf.Repeat(loopAngle + loopSpeed * dt, 360f);
            loop.position = playerPos + Dir(loopAngle) * orbitRadius;
            loop.localScale = loopSpinScale;
        }

        // ===== Release: the throw =====

        void Release()
        {
            // Barely spinning: nothing to throw, the lasso just drops.
            if (!hasHandAngle || Mathf.Abs(loopSpeed) < minSpinSpeed)
            {
                lastThrow = "Not enough spin. Wind up more!";
                phase = Phase.Idle;
                loop.gameObject.SetActive(false);
                return;
            }

            float power = ThrowPower();
            float distance = Mathf.Lerp(minRange, maxRange, power);
            flightSpeed = Mathf.Lerp(minFlightSpeed, maxFlightSpeed, power);

            // Work out the whole path up front, bounces included. The flight then just follows it.
            // (Same inputs = same path, which will matter for online play.)
            TracePath(loop.position, SwingDirection(), distance, flightPath);
            flightPoint = 0;

            loop.localScale = Vector3.one * catchRadius * 2f; // the loop opens up to its catch size
            phase = Phase.Flying;

            int bounces = flightPath.Count - 1;
            lastThrow = $"spin {Mathf.Abs(loopSpeed) / 360f:0.0} circles/s -> distance {distance:0.0}, {bounces} bounce(s)";
        }

        // Flies the loop along flightPath, one corner at a time.
        void UpdateFlight()
        {
            float step = flightSpeed * Time.deltaTime; // how far the loop may move this frame

            // "while" because on a fast frame the loop might pass a bounce point and keep going.
            while (step > 0f && flightPoint < flightPath.Count)
            {
                Vector2 pos = loop.position;
                Vector2 target = flightPath[flightPoint];
                float toTarget = Vector2.Distance(pos, target);

                if (step < toTarget)
                {
                    // Not there yet: move part of the way and stop for this frame.
                    loop.position = Vector2.MoveTowards(pos, target, step);
                    step = 0f;
                }
                else
                {
                    // Reached this corner (a bounce, or the landing point): use up that distance and aim for the next.
                    loop.position = target;
                    step -= toTarget;
                    flightPoint++;
                }
            }

            // Past the last corner = landed: catch what's inside, then tighten the rope.
            if (flightPoint >= flightPath.Count)
            {
                Catch();
                StartTightening();
            }
        }

        // ===== Wall bounces =====

        // The area the loop can fly in: what the camera sees, shrunk by the catch radius so the whole
        // loop stays on screen. orthographicSize = half the visible height; aspect = width / height.
        Rect PlayArea()
        {
            var cam = Camera.main;
            float halfHeight = cam.orthographicSize - catchRadius;
            float halfWidth = cam.orthographicSize * cam.aspect - catchRadius;
            Vector2 center = cam.transform.position;
            return new Rect(center.x - halfWidth, center.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
        }

        // Fills "path" with the corners of a throw from start, in direction dir, travelling "distance" in
        // total, bouncing off the play area's edges. The last point is where it lands.
        void TracePath(Vector2 start, Vector2 dir, float distance, List<Vector2> path)
        {
            path.Clear();
            Rect area = PlayArea();
            Vector2 pos = start;
            float remaining = distance;

            for (int bounce = 0; bounce <= maxBounces; bounce++)
            {
                // How far along dir until we'd hit a side wall, and a top/bottom wall?
                // (If we're not moving that way at all, we never hit it: infinity.)
                float toSide = dir.x > 0f ? (area.xMax - pos.x) / dir.x
                             : dir.x < 0f ? (area.xMin - pos.x) / dir.x
                             : float.PositiveInfinity;
                float toEnd = dir.y > 0f ? (area.yMax - pos.y) / dir.y
                            : dir.y < 0f ? (area.yMin - pos.y) / dir.y
                            : float.PositiveInfinity;
                float toWall = Mathf.Max(0f, Mathf.Min(toSide, toEnd)); // whichever wall comes first

                // Lands before reaching a wall (or bouncing is off): done.
                if (!bounceOffWalls || remaining <= toWall)
                {
                    path.Add(pos + dir * Mathf.Min(remaining, toWall));
                    return;
                }

                // Reached a wall: add the bounce point, then flip the direction like a mirror.
                pos += dir * toWall;
                remaining -= toWall;
                path.Add(pos);
                if (bounce == maxBounces) return; // out of bounces: it stops at this wall
                if (toEnd <= toSide && !bounceOffTopAndBottom) return; // hit the top or bottom: it stops there

                if (toSide <= toEnd) dir.x = -dir.x; // hit a side wall: reverse left/right
                if (toEnd <= toSide) dir.y = -dir.y; // hit the top or bottom: reverse up/down (both = a corner)
            }
        }

        // Back to the start: called by the Arena when a new match begins. Drops whatever was happening,
        // including a throw in mid-air (its treasure is being replaced anyway).
        public void ResetLasso()
        {
            hooked.Clear();
            hookedOffsets.Clear();
            caughtCount = 0;
            caughtWeight = 0f;
            loopSpeed = 0f;
            lastThrow = "";
            EndThrow();
        }

        void EndThrow()
        {
            phase = Phase.Idle;
            HideRope();
            loop.gameObject.SetActive(false);
        }

        // ===== Catching and hauling =====

        // The loop just landed: everything free inside it is caught.
        void Catch()
        {
            hooked.Clear();
            hookedOffsets.Clear();
            caughtWeight = 0f;
            caughtCount = 0;

            var arena = Arena.Current;
            if (arena == null) return; // no arena in the scene: just a practice throw

            Vector2 landing = loop.position;
            Rect home = HomeHoard;
            int stolenValue = 0;
            foreach (var t in arena.treasures)
            {
                if (Vector2.Distance(t.transform.position, landing) > catchRadius) continue; // outside the loop

                // Catchable: loose in the field, or sitting in the OTHER player's hoard (a steal).
                // Not catchable: already on someone's rope, or in your own hoard.
                // "||" means "or", "&&" means "and", "!=" means "is not".
                bool loose = t.state == Treasure.State.Free;
                bool steal = t.state == Treasure.State.InHoard && t.hoard != home;
                if (!loose && !steal) continue;

                if (steal) stolenValue += t.value;
                t.Hook();
                hooked.Add(t);
                hookedOffsets.Add((Vector2)t.transform.position - landing); // where it sits inside the loop
                caughtWeight += t.weight;
            }

            caughtCount = hooked.Count;
            if (caughtCount == 0) lastThrow += " | missed";
            else lastThrow += $" | caught {caughtCount} (weight {caughtWeight:0})";

            if (stolenValue > 0)
            {
                lastThrow += $" | STOLE {stolenValue}!";
                ScoreHud.ShowPopup("STOLEN!", landing, new Color(1f, 0.45f, 0.85f)); // pink, right where it landed
            }
        }

        // Every frame of the pull: the caught treasure rides along with the loop, bunching up as it's dragged.
        void Haul()
        {
            Vector2 loopPos = loop.position;
            for (int i = 0; i < hooked.Count; i++)
            {
                // Each offset slides toward zero (1 unit per second), so the treasure gathers into the loop.
                hookedOffsets[i] = Vector2.MoveTowards(hookedOffsets[i], Vector2.zero, Time.deltaTime);
                hooked[i].transform.position = loopPos + hookedOffsets[i];
            }
        }

        // The loop reached you: the catch goes into your hoard.
        void Deliver()
        {
            var arena = Arena.Current;
            int gained = 0;
            foreach (var t in hooked)
            {
                t.StoreIn(HomeHoard, arena);
                gained += t.value;
                ScoreHud.ShowPopup($"+{t.value}", t.transform.position, new Color(1f, 0.85f, 0.2f)); // over each piece as it lands
            }
            if (gained > 0) lastThrow += $" | +{gained}";
            hooked.Clear();
            hookedOffsets.Clear();
            caughtCount = 0;
            caughtWeight = 0f;
        }

        // ===== Tightening: the rope straightens before the pull =====

        void StartTightening()
        {
            // Remember the rope's slack shape (the shape it was drawn in on the last flying frame).
            slackPoints.Clear();
            slackPoints.AddRange(ropePoints); // AddRange copies every item from another list
            if (slackPoints.Count > 0) slackPoints[slackPoints.Count - 1] = loop.position;

            // Longer rope = takes longer to tighten. Its length = all its straight pieces added up.
            float length = 0f;
            for (int i = 1; i < slackPoints.Count; i++) length += Vector2.Distance(slackPoints[i - 1], slackPoints[i]);
            tightenTime = Mathf.Lerp(minTightenTime, maxTightenTime, Mathf.InverseLerp(minRange, maxRange, length));
            tightenStart = Time.time;
            phase = Phase.Tightening;
        }

        void UpdateTightening(Vector2 playerPos)
        {
            // t goes from 0 to 1 over tightenTime. SmoothStep eases it in and out, so it starts and ends gently.
            float t = Mathf.Clamp01((Time.time - tightenStart) / tightenTime);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            // Every bend in the rope slides toward the straight line between the player and the loop.
            // Each bend's spot on that line is as far along it as the bend is along the rope (by length),
            // so the rope straightens evenly instead of bunching up.
            Vector2 loopPos = loop.position;
            float total = 0f;
            for (int i = 1; i < slackPoints.Count; i++) total += Vector2.Distance(slackPoints[i - 1], slackPoints[i]);

            ropePoints.Clear();
            float along = 0f;
            for (int i = 0; i < slackPoints.Count; i++)
            {
                if (i > 0) along += Vector2.Distance(slackPoints[i - 1], slackPoints[i]);
                float fraction = total > 0f ? along / total : 0f;
                Vector2 onStraightLine = Vector2.Lerp(playerPos, loopPos, fraction);
                ropePoints.Add(Vector2.Lerp(slackPoints[i], onStraightLine, eased));
            }
            DrawRope(ropePoints);

            if (t >= 1f) phase = Phase.Returning; // taut: now the pull starts
        }

        // ===== Spin math =====

        // How fast the loop is spinning, from 0 (minSpinSpeed or slower) to 1 (maxSpinSpeed or faster),
        // bent by distanceCurve. InverseLerp: "where is this value between min and max?" as 0..1.
        // Pow(x, 2) = x squared: 0.5 becomes 0.25, so the low and middle range stay short and the
        // distance only shoots up near the top. Reaching the far side takes a great spin.
        float ThrowPower() =>
            Mathf.Pow(Mathf.InverseLerp(minSpinSpeed, maxSpinSpeed, Mathf.Abs(loopSpeed)), distanceCurve);

        // The way the loop is moving right now: along its circle, sideways to the rope (the "tangent").
        // Counterclockwise (positive speed) = 90 degrees ahead of the loop's angle; clockwise = 90 behind.
        // That's the direction a lasso or a sling flies when you let go.
        Vector2 SwingDirection() => Dir(loopAngle + (loopSpeed >= 0f ? 90f : -90f));

        // ===== Helpers =====

        // Turns an angle into a direction arrow of length 1. (The opposite of Atan2.)
        // "=>" is a short way to write a method that just returns one expression.
        static Vector2 Dir(float degrees) =>
            new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        // Draws the rope through a list of points: one straight piece between each pair of neighbours.
        void DrawRope(List<Vector2> points)
        {
            for (int i = 0; i < ropePieces.Length; i++)
            {
                bool used = i + 1 < points.Count; // piece i goes from point i to point i+1
                ropePieces[i].enabled = used;
                if (used) StretchBetween(ropePieces[i].transform, points[i], points[i + 1], 0.06f);
            }
        }

        void HideRope()
        {
            foreach (var piece in ropePieces) piece.enabled = false;
        }

        // Stretches a 1x1 square sprite into a thin line between two points.
        static void StretchBetween(Transform line, Vector2 from, Vector2 to, float thickness)
        {
            Vector2 delta = to - from;
            line.position = from + delta * 0.5f;                                                 // middle of the line
            line.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg); // turned to face along it
            line.localScale = new Vector3(delta.magnitude, thickness, 1f);                      // as long as the line, thin
        }
    }
}
