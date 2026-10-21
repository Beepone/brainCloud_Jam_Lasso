// "using" lines let this file use code from other places without typing their full names.
using System.Collections.Generic; // Queue<T> (a first-in, first-out list)
using UnityEngine;                // MonoBehaviour, Vector2, Mathf, Time, Camera, Transform...
using UnityEngine.InputSystem;    // Pointer (the new Input System: mouse and touch)

// LassoSpin: the whole lasso throw.
//
//   SPIN   Hold the button and circle the pointer around the player. The loop has momentum: it chases
//          your hand, always trailing a little, and only speeds up gradually. Its speed = rope strength.
//          Push it faster than it can follow and the rope TANGLES (wind-up back to zero).
//   RING   Hold a steady spin for a moment and the timing ring appears around the player. It shrinks,
//          then resets. Flick while it's small (PERFECT) and the loop opens wider (bigger catch area).
//   FLICK  Flick the pointer outward. The flick's direction is where the lasso goes, and the flick's
//          speed is how far. Release the button right after to throw.
//   FLY    The loop flies out, lands, then is pulled back to the player.
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
    [Tooltip("How far from the player the loop circles, in world units.")]
    public float spinRadius = 1.2f;

    [Header("Spin: momentum")]
    [Tooltip("How hard the loop is pulled toward your hand. Higher = trails less. (Degrees per second of speed per degree of lag.)")]
    public float followStrength = 10f;
    [Tooltip("The fastest the loop can speed up, in degrees per second, per second. Lower = longer wind-up.")]
    public float maxAcceleration = 900f;
    [Tooltip("If your hand gets this many degrees ahead of the loop, the rope tangles.")]
    public float tangleAngle = 120f;
    [Tooltip("After tangling, the loop is stuck for this many seconds.")]
    public float tangleStun = 0.4f;

    [Header("Spin: rope strength")]
    [Tooltip("Loop speed (degrees per second) that gives the weakest rope...")]
    public float minSpinSpeed = 180f;   // half a circle per second
    [Tooltip("...and the strongest rope.")]
    public float maxSpinSpeed = 1080f;  // three circles per second
    [Tooltip("Rope strength range: the total weight it can pull without snapping. Coin = 1, Chest = 3, Idol = 6.")]
    public float minRopeStrength = 2f;
    public float maxRopeStrength = 9f;

    [Header("Timing ring")]
    [Tooltip("Hold a steady spin for this long and the ring appears.")]
    public float steadyTime = 1.2f;
    [Tooltip("'Steady' = within this fraction of your recent average speed (0.12 = within 12%).")]
    public float steadyTolerance = 0.12f;
    [Tooltip("The loop must be spinning at least this fast (degrees per second) for the ring.")]
    public float minSpeedForRing = 360f; // one circle per second
    [Tooltip("Seconds for the ring to shrink from largest to smallest before resetting.")]
    public float ringCycleTime = 1.4f;
    [Tooltip("Flick when the shrink is at least this far along for a PERFECT (0 = largest, 1 = smallest).")]
    [Range(0f, 1f)] public float perfectFrom = 0.75f;
    public float ringMaxRadius = 1.1f;
    public float ringMinRadius = 0.5f;

    [Header("Flick (measured in screen heights per second, so it feels the same on any screen)")]
    [Tooltip("How fast the pointer must move away from the player to count as a flick.")]
    public float flickMinSpeed = 1.5f;
    [Tooltip("Flick speed for the longest throw. Flicks between min and max scale the distance.")]
    public float flickMaxSpeed = 6f;
    [Tooltip("How much of the movement must point away from the player (0-1). Circling is sideways, so this filters it out.")]
    [Range(0f, 1f)] public float flickOutwardRatio = 0.7f;
    [Tooltip("Velocity is measured over this many seconds. Short, because a flick is quick.")]
    public float velocityWindow = 0.08f;
    [Tooltip("After a flick, you have this long to release the button.")]
    public float releaseWindow = 0.2f;

    [Header("Throw")]
    public float minRange = 1.5f;
    public float maxRange = 12f;
    public float minFlightSpeed = 8f;
    public float maxFlightSpeed = 18f;
    [Tooltip("Radius of the loop when it lands: everything inside will be caught.")]
    public float catchRadius = 1f;
    [Tooltip("A PERFECT throw multiplies the catch radius by this.")]
    public float perfectCatchMultiplier = 1.6f;
    public float returnSpeed = 20f;

    // ===== Readouts =====
    // Public only so you can watch them in the Inspector while playing. The code writes them.
    [Header("Readouts (watch these in Play mode)")]
    public string state;          // what the lasso is doing right now
    public float loopSpeed;       // the loop's own spin speed, degrees per second (+ = counterclockwise)
    public float lag;             // how many degrees your hand is ahead of the loop
    public float ropeStrength;    // what the current spin would give
    public float steadyFor;       // how long you've held a steady speed
    public bool ringVisible;
    public float outwardSpeed;    // flick speed away from the player, screen heights per second
    public string lastThrow;      // summary of the last throw

    // ===== Private state =====
    // No "public" = private: hidden from the Inspector, only this script uses them.

    // An "enum" is a fixed list of named options. The lasso is always in exactly one of these.
    enum Phase { Idle, Spinning, Flying, Returning }
    Phase phase = Phase.Idle;

    float loopAngle;        // where the loop is around the player (degrees, 0 = right, 90 = up)
    float tangledUntil;     // Time.time until which the loop is stuck after a tangle
    float referenceSpeed;   // slowly-following average of the loop's speed, for "is it steady?"
    float unsteadyFor;      // how long the speed has been outside the steady range
    float ringStartTime;    // when the ring appeared (its shrink cycle starts here)

    // Each entry: when it happened, and where the pointer was ON SCREEN (pixels). For flick velocity.
    readonly Queue<(float time, Vector2 screenPos)> recentPositions = new Queue<(float time, Vector2 screenPos)>();

    // Values locked in at the start of a flick (the spin slows down during the flick itself).
    bool inFlick;
    float flickTime = -1f;
    float flickRopeStrength, flickPeakSpeed;
    bool flickPerfect;
    Vector2 flickDirection;  // world direction of the flick

    // The throw in flight.
    Vector2 landingPoint;
    float flightSpeed;

    // Visuals made in code (placeholders until the art arrives).
    SpriteRenderer ring, rope, loopSprite;
    Vector3 loopSpinScale;   // the loop's size while spinning (from the scene)
    Color loopColor;         // the loop's color from the scene (we flash it red when tangled)

    // ===== Start =====
    // Unity calls Start once, before the first Update.
    void Start()
    {
        loopSpinScale = loop.localScale;
        loopSprite = loop.GetComponent<SpriteRenderer>();
        if (loopSprite != null) loopColor = loopSprite.color;

        // The ring and rope are drawn with the placeholder shapes from the old prototype
        // (Scripts/Lasso/PlaceholderArt.cs). The artists' sprites will replace them.
        ring = PlaceholderArt.CreateSprite("Timing Ring", PlaceholderArt.Ring, Color.white, 3);
        rope = PlaceholderArt.CreateSprite("Rope", PlaceholderArt.Square, new Color(0.85f, 0.7f, 0.45f), 5);
        ring.enabled = false;
        rope.enabled = false;
        loop.gameObject.SetActive(false);
    }

    // ===== Update =====
    // Unity calls Update once every frame. It works out which phase we're in and runs that part.
    void Update()
    {
        var pointer = Pointer.current; // the mouse on a computer, or the first finger on a phone
        if (pointer == null) return;   // no mouse or touch at all, so do nothing

        Vector2 playerPos = transform.position;

        // "switch" runs the block for the current phase. "break" ends the block.
        switch (phase)
        {
            case Phase.Idle:
                // Waiting. Pressing starts a spin.
                if (pointer.press.wasPressedThisFrame) StartSpin(pointer, playerPos);
                break;

            case Phase.Spinning:
                UpdateSpin(pointer, playerPos);
                break;

            case Phase.Flying:
                // MoveTowards moves a point toward a target by at most the given distance, never past it.
                loop.position = Vector2.MoveTowards(loop.position, landingPoint, flightSpeed * Time.deltaTime);
                if ((Vector2)loop.position == landingPoint) phase = Phase.Returning; // landed (catching comes in milestone 2)
                break;

            case Phase.Returning:
                loop.position = Vector2.MoveTowards(loop.position, playerPos, returnSpeed * Time.deltaTime);
                if ((Vector2)loop.position == playerPos) EndThrow();
                break;
        }

        // While the loop is out, draw the rope from the player to it.
        if (phase == Phase.Flying || phase == Phase.Returning) DrawRope(playerPos, loop.position);

        state = phase.ToString(); // readout
    }

    // ===== Spinning =====

    void StartSpin(Pointer pointer, Vector2 playerPos)
    {
        phase = Phase.Spinning;

        // Start the loop right where the pointer is, not moving yet.
        loopAngle = PointerAngle(pointer, playerPos);
        loopSpeed = 0f;
        tangledUntil = 0f;
        referenceSpeed = 0f;
        steadyFor = 0f;
        unsteadyFor = 0f;
        ringVisible = false;
        inFlick = false;
        flickTime = -1f;
        recentPositions.Clear();

        loop.localScale = loopSpinScale;
        loop.gameObject.SetActive(true);
    }

    void UpdateSpin(Pointer pointer, Vector2 playerPos)
    {
        float dt = Time.deltaTime; // how long this frame took, in seconds

        // ----- Released? -----
        // wasReleasedThisFrame is true only on the one frame you let go.
        if (pointer.press.wasReleasedThisFrame)
        {
            // Only a release right after a flick throws. Otherwise the spin just stops.
            if (Time.time - flickTime <= releaseWindow) Throw(playerPos);
            else CancelSpin();
            return;
        }

        // ----- 1. Momentum: the loop chases your hand -----
        float handAngle = PointerAngle(pointer, playerPos);

        // How far your hand is ahead of the loop. DeltaAngle takes the shortest way around (179 -> -179 = 2).
        // Positive = hand is counterclockwise of the loop, negative = clockwise.
        lag = Mathf.DeltaAngle(loopAngle, handAngle);

        if (Time.time < tangledUntil)
        {
            // Tangled: the loop is stuck and the wind-up stays at zero until the stun ends.
            loopSpeed = 0f;
        }
        else
        {
            // The further your hand is ahead, the faster the loop WANTS to go (like a stretched spring)...
            float wantedSpeed = lag * followStrength;

            // ...but it has weight, so its speed can only change by maxAcceleration per second.
            // That's the wind-up: getting the loop fast takes a few turns of steady spinning.
            loopSpeed = Mathf.MoveTowards(loopSpeed, wantedSpeed, maxAcceleration * dt);

            // ----- 2. Tangle check -----
            // Your hand pulled too far ahead of the loop, in the direction it's spinning (or while it's
            // barely moving): you whipped it faster than it can follow.
            // Mathf.Sign gives +1 or -1. If the loop is AHEAD of your hand (you slowed down), that's fine:
            // it just decelerates.
            bool handAhead = Mathf.Abs(loopSpeed) < 30f || Mathf.Sign(lag) == Mathf.Sign(loopSpeed);
            if (Mathf.Abs(lag) > tangleAngle && handAhead) Tangle();
        }

        // Move the loop by its own speed. Repeat keeps the angle between 0 and 360.
        loopAngle = Mathf.Repeat(loopAngle + loopSpeed * dt, 360f);
        loop.position = playerPos + Dir(loopAngle) * spinRadius;

        // ----- 3. Rope strength from the loop's speed -----
        // InverseLerp: where is the speed between min and max? 0..1. Lerp: turn that 0..1 into a strength.
        float spin01 = Mathf.InverseLerp(minSpinSpeed, maxSpinSpeed, Mathf.Abs(loopSpeed));
        ropeStrength = Mathf.Lerp(minRopeStrength, maxRopeStrength, spin01);

        // ----- 4. Timing ring: appears after a steady spin -----
        UpdateSteadiness(dt);
        UpdateRing(playerPos);

        // ----- 5. Flick detection -----
        UpdateFlick(pointer, playerPos);
    }

    void Tangle()
    {
        tangledUntil = Time.time + tangleStun;
        loopSpeed = 0f;
        steadyFor = 0f;
        ringVisible = false;
        if (loopSprite != null) loopSprite.color = new Color(1f, 0.3f, 0.3f); // red = tangled
        lastThrow = "TANGLED! Spun up too fast.";
    }

    void UpdateSteadiness(float dt)
    {
        if (loopSprite != null && Time.time >= tangledUntil) loopSprite.color = loopColor; // un-tangled: back to normal

        float speed = Mathf.Abs(loopSpeed);

        // referenceSpeed slowly follows the real speed. 1 - Exp(-rate * dt) is a smooth, frame-rate
        // independent way to say "move about rate * 100% of the way per second".
        referenceSpeed = Mathf.Lerp(referenceSpeed, speed, 1f - Mathf.Exp(-3f * dt));

        // Steady = fast enough, and close to the recent average.
        bool steady = speed >= minSpeedForRing
                   && Mathf.Abs(speed - referenceSpeed) <= steadyTolerance * referenceSpeed;

        if (steady)
        {
            steadyFor += dt;
            unsteadyFor = 0f;
        }
        else
        {
            // A small grace period, so one wobbly frame doesn't kill the ring.
            unsteadyFor += dt;
            if (unsteadyFor > 0.25f) steadyFor = 0f;
        }

        bool shouldShow = steadyFor >= steadyTime;
        if (shouldShow && !ringVisible) ringStartTime = Time.time; // just appeared: start its shrink cycle
        ringVisible = shouldShow;
    }

    void UpdateRing(Vector2 playerPos)
    {
        ring.enabled = ringVisible;
        if (!ringVisible) return;

        float shrink = RingShrink();
        ring.transform.position = playerPos;
        ring.transform.localScale = Vector3.one * Mathf.Lerp(ringMaxRadius, ringMinRadius, shrink) * 2f;
        ring.color = shrink >= perfectFrom ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 1f, 1f, 0.6f); // green = PERFECT now
    }

    // 0 = ring at its largest, 1 = smallest. Repeat makes it loop: 0 -> 1, back to 0, and so on.
    float RingShrink() => Mathf.Repeat((Time.time - ringStartTime) / ringCycleTime, 1f);

    // ===== Flick =====

    void UpdateFlick(Pointer pointer, Vector2 playerPos)
    {
        // We measure the flick in SCREEN space and divide by the screen's height, so the same physical
        // flick gives the same result on a phone and on a big monitor.
        Vector2 screenPos = pointer.position.ReadValue();
        recentPositions.Enqueue((Time.time, screenPos));
        while (recentPositions.Count > 1 && recentPositions.Peek().time < Time.time - velocityWindow)
            recentPositions.Dequeue();

        var oldest = recentPositions.Peek();
        float span = Time.time - oldest.time;
        if (span <= 0f) return; // first frame: nothing to compare yet

        // Velocity in screen heights per second.
        Vector2 velocity = (screenPos - oldest.screenPos) / span / Screen.height;

        // Outward direction on screen: from the player (converted to screen pixels) to the pointer.
        Vector2 playerScreen = Camera.main.WorldToScreenPoint(playerPos);
        Vector2 outward = (screenPos - playerScreen).normalized;

        // Dot product = how fast we're moving away from the player (the "shadow" of velocity on outward).
        outwardSpeed = Vector2.Dot(velocity, outward);

        // A flick = moving outward fast enough, AND mostly outward (not sideways like circling).
        bool isFlick = outwardSpeed >= flickMinSpeed && outwardSpeed >= flickOutwardRatio * velocity.magnitude;

        if (isFlick)
        {
            if (!inFlick)
            {
                // First frame of the flick: lock in the spin's results before the flick slows the loop.
                flickRopeStrength = ropeStrength;
                flickPerfect = ringVisible && RingShrink() >= perfectFrom;
                flickPeakSpeed = 0f;
            }
            inFlick = true;
            flickTime = Time.time;

            // The hardest moment of the flick decides the distance.
            flickPeakSpeed = Mathf.Max(flickPeakSpeed, outwardSpeed);

            // The flick's direction, converted to world space (the world-space arrow between the two points).
            Vector2 worldNow = Camera.main.ScreenToWorldPoint(screenPos);
            Vector2 worldOld = Camera.main.ScreenToWorldPoint(oldest.screenPos);
            flickDirection = (worldNow - worldOld).normalized;
        }
        else
        {
            inFlick = false;
        }
    }

    // ===== Throw =====

    void Throw(Vector2 playerPos)
    {
        // Flick speed -> distance. Spin -> rope strength (already locked in). Ring -> bigger loop on PERFECT.
        float flick01 = Mathf.InverseLerp(flickMinSpeed, flickMaxSpeed, flickPeakSpeed);
        float distance = Mathf.Lerp(minRange, maxRange, flick01);
        flightSpeed = Mathf.Lerp(minFlightSpeed, maxFlightSpeed, flick01);
        landingPoint = playerPos + flickDirection * distance; // exactly where you flicked, no random scatter

        float radius = catchRadius * (flickPerfect ? perfectCatchMultiplier : 1f);
        loop.localScale = Vector3.one * radius * 2f; // the loop opens up to its catch size
        loop.position = playerPos;
        ring.enabled = false;
        phase = Phase.Flying;

        lastThrow = $"{(flickPerfect ? "PERFECT" : "Normal")} | flick {flickPeakSpeed:0.0} -> distance {distance:0.0} | rope strength {flickRopeStrength:0.0} | catch radius {radius:0.0}";
    }

    void CancelSpin()
    {
        phase = Phase.Idle;
        loopSpeed = 0f;
        ring.enabled = false;
        loop.gameObject.SetActive(false);
    }

    void EndThrow()
    {
        phase = Phase.Idle;
        rope.enabled = false;
        loop.gameObject.SetActive(false);
    }

    // ===== Helpers =====

    // The pointer's angle around the player, in degrees (0 = right, 90 = up).
    float PointerAngle(Pointer pointer, Vector2 playerPos)
    {
        Vector2 worldPos = Camera.main.ScreenToWorldPoint(pointer.position.ReadValue());
        Vector2 toPointer = worldPos - playerPos;
        return Mathf.Atan2(toPointer.y, toPointer.x) * Mathf.Rad2Deg;
    }

    // Turns an angle into a direction arrow of length 1. (The opposite of Atan2.)
    static Vector2 Dir(float degrees) =>
        new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

    // Stretches the rope sprite (a 1x1 square) into a thin line between two points.
    void DrawRope(Vector2 from, Vector2 to)
    {
        rope.enabled = true;
        Vector2 delta = to - from;
        rope.transform.position = from + delta * 0.5f;                                 // middle of the line
        rope.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg); // turned to face along it
        rope.transform.localScale = new Vector3(delta.magnitude, 0.06f, 1f);          // as long as the line, thin
    }
}
