using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

public enum ThrowQuality { Perfect, Good, Sloppy }

/// <summary>
/// Pokemon GO-style lasso throw:
/// - Hold the mouse and circle it around the player. Faster circles = more power = farther throw
///   (the range ring around the player grows as you speed up).
/// - The accuracy cycle runs from large to small, then resets; the range ring's color shows how
///   good a throw would be right now. Small = the lasso lands closer to where you aimed, and a stronger rope.
/// - To throw, flick the mouse outward in the direction you want, then release the button quickly.
///   Power and accuracy are locked in at the start of the flick.
/// All tuning values are exposed in the Inspector, so you can adjust them during Play mode.
/// </summary>
public class LassoThrower : MonoBehaviour
{
    [Serializable]
    public struct QualityEffect
    {
        public float speedMultiplier;
        public float ropeStrengthMultiplier;
    }

    [Header("Spin input")]
    [Tooltip("Average circling speed (degrees per second) needed to throw at all.")]
    public float minSpinRate = 200f;
    [Tooltip("Average circling speed for full power. 1440 = 4 circles per second.")]
    public float maxSpinRate = 1440f;
    [Tooltip("Power = your average spin over this many seconds. Spinning for less than this counts the missing time as zero, so throws need a wind-up.")]
    public float windUpTime = 1f;
    [Tooltip("Stop spinning for this long and the wind-up starts over.")]
    public float pauseResetTime = 0.25f;
    [Tooltip("Spinning slower than this (degrees per second) counts as stopped.")]
    public float stoppedBelowRate = 90f;
    [Tooltip("The mouse must be at least this far from the player (world units) to count as spinning.")]
    public float minCircleRadius = 0.6f;
    [Tooltip("Size of the lasso's visual spin around the player.")]
    public float lassoSpinRadius = 1.2f;

    [Header("Flick")]
    [Tooltip("Minimum speed away from the player to count as a flick (world units/sec). Watch the 'outward' number in the HUD to tune this.")]
    public float flickMinSpeed = 15f;
    [Tooltip("How much of the mouse movement must point away from the player (0-1). Spinning is mostly sideways, so this stops fast spins counting as flicks.")]
    [Range(0f, 1f)] public float flickOutwardRatio = 0.7f;
    [Tooltip("Power is taken from your spin this many seconds before the flick, so the flick itself doesn't add power.")]
    public float flickLookback = 0.15f;
    [Tooltip("Seconds of mouse movement used to measure its velocity. Short = catches quick flicks.")]
    public float velocityWindow = 0.08f;
    [Tooltip("After a flick, you have this long to release the button.")]
    public float releaseWindow = 0.2f;

    [Header("Power")]
    public float minRange = 1.5f;
    public float maxRange = 15f;
    public float minFlightSpeed = 12f;
    public float maxFlightSpeed = 28f;
    [Tooltip("Radius of the loop when it lands. Everything inside is caught.")]
    public float catchRadius = 1f;

    [Header("Accuracy circle")]
    public float circleMaxRadius = 3f;
    public float circleMinRadius = 0.25f;
    [Tooltip("Seconds for the circle to shrink from largest to smallest before resetting.")]
    public float circleShrinkTime = 1.4f;
    [Tooltip("Release when the shrink is at least this far along for a Perfect (0 = largest, 1 = smallest).")]
    [Range(0f, 1f)] public float perfectFrom = 0.75f;
    [Range(0f, 1f)] public float goodFrom = 0.4f;
    [Tooltip("The timing ring around the player shrinks from this size...")]
    public float timingRingMaxRadius = 1.1f;
    [Tooltip("...to this size, in step with the accuracy cycle.")]
    public float timingRingMinRadius = 0.5f;

    [Header("Debug")]
    [Tooltip("Show the range ring and the landing preview after a flick. Off = play by feel.")]
    public bool showDebugPreview = false;

    [Header("Quality effects")]
    public QualityEffect perfect = new QualityEffect { speedMultiplier = 1.3f, ropeStrengthMultiplier = 1.5f };
    public QualityEffect good = new QualityEffect { speedMultiplier = 1f, ropeStrengthMultiplier = 1f };
    public QualityEffect sloppy = new QualityEffect { speedMultiplier = 0.8f, ropeStrengthMultiplier = 0.6f };

    [Header("Rope")]
    [Tooltip("Total weight a Good throw can pull. Coin = 1, Chest = 3, Idol = 6.")]
    public float baseRopeStrength = 6f;
    [Tooltip("If overloaded, the rope snaps after pulling this fraction of the way back.")]
    [Range(0f, 1f)] public float snapAtFraction = 0.4f;
    public float pullSpeed = 9f;
    [Tooltip("Each unit of weight slows the pull by this fraction.")]
    public float weightSlowdown = 0.12f;
    public float retractSpeed = 22f;

    [HideInInspector] public TreasureZone homeZone;

    // Read by the HUD.
    public string LastThrowSummary { get; private set; } = "";
    public string LiveSpinSummary { get; private set; } = "";

    enum State { Idle, Spinning, Flying, Pulling }
    State state = State.Idle;

    // Spin tracking.
    readonly Queue<(float time, Vector2 pos)> mouseHistory = new Queue<(float, Vector2)>();
    readonly Queue<(float time, float degrees)> spinHistory = new Queue<(float, float)>(); // Degrees spun each frame.
    float spinRate, lastMouseAngle, lassoAngle, spinStartTime;
    float windUpStart, stoppedFor; // Wind-up restarts after a pause.
    bool hasLastAngle;

    // Flick tracking. Power, spin and accuracy are saved on the first frame of a flick,
    // because circling slows down during the flick itself.
    bool inFlick;
    float flickTime = -1f, flickPower, flickSpinRate, flickShrink, peakOutwardSpeed;
    Vector2 flickDirection;

    Vector2 loopPos, landingPoint, pullStart;
    float flightSpeed, ropeStrength, snapDistance;
    bool willSnap;
    readonly List<Treasure> hooked = new List<Treasure>();
    readonly List<Vector2> hookedOffsets = new List<Vector2>();

    SpriteRenderer loop, rope, accuracyCircle, catchPreview, rangeRing, spinGuide, timingRing;

    void Start()
    {
        var parent = transform.parent;
        loop = PlaceholderArt.CreateSprite("Lasso Loop", PlaceholderArt.Ring, new Color(0.95f, 0.85f, 0.6f), 6, parent);
        rope = PlaceholderArt.CreateSprite("Rope", PlaceholderArt.Square, new Color(0.85f, 0.7f, 0.45f), 5, parent);
        accuracyCircle = PlaceholderArt.CreateSprite("Accuracy Circle", PlaceholderArt.Ring, Color.white, 2, parent);
        catchPreview = PlaceholderArt.CreateSprite("Catch Preview", PlaceholderArt.Ring, new Color(1f, 1f, 1f, 0.3f), 1, parent);
        rangeRing = PlaceholderArt.CreateSprite("Range Ring", PlaceholderArt.ThinRing, Color.white, 1, parent);
        timingRing = PlaceholderArt.CreateSprite("Timing Ring", PlaceholderArt.Ring, Color.white, 3, parent);
        spinGuide = PlaceholderArt.CreateSprite("Spin Guide", PlaceholderArt.Ring, new Color(1f, 1f, 1f, 0.12f), 1, parent);
        SetVisible(false);
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 origin = transform.position;
        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
        float dt = Time.deltaTime;

        switch (state)
        {
            case State.Idle:
                if (mouse.leftButton.wasPressedThisFrame) StartSpin(mouseWorld);
                break;

            case State.Spinning:
                if (mouse.rightButton.wasPressedThisFrame) { Cancel(); break; }
                UpdateSpin(origin, mouseWorld, dt);
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    // Releasing only throws if you flicked just before; otherwise the spin is cancelled.
                    if (Time.time - flickTime <= releaseWindow) Throw(origin);
                    else
                    {
                        LassoArena.Popup("Flick to throw!", origin + Vector2.up, Color.gray);
                        Cancel();
                    }
                }
                break;

            case State.Flying:
                loopPos = Vector2.MoveTowards(loopPos, landingPoint, flightSpeed * dt);
                if (loopPos == landingPoint) Land(origin);
                break;

            case State.Pulling:
                UpdatePull(origin, dt);
                break;
        }

        if (state != State.Idle) DrawRope(origin, loopPos);
    }

    // ---------- Spinning ----------

    void StartSpin(Vector2 mouseWorld)
    {
        state = State.Spinning;
        mouseHistory.Clear();
        spinHistory.Clear();
        spinRate = 0f;
        hasLastAngle = false;
        inFlick = false;
        flickTime = -1f; // Forget flicks from the previous throw.
        peakOutwardSpeed = 0f;
        spinStartTime = Time.time;
        windUpStart = Time.time;
        stoppedFor = 0f;
        AddHistory(mouseWorld);
        SetVisible(true);
        loop.transform.localScale = Vector3.one * 0.5f;
    }

    void UpdateSpin(Vector2 origin, Vector2 mouseWorld, float dt)
    {
        AddHistory(mouseWorld);
        Vector2 center = origin; // You circle around the player.

        // Measure how fast the mouse is circling around that center.
        Vector2 offset = mouseWorld - center;
        float instantRate = 0f;
        if (offset.magnitude >= minCircleRadius && dt > 0f)
        {
            float angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
            if (hasLastAngle) instantRate = Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(lastMouseAngle, angle)) / dt, maxSpinRate * 2f);
            lastMouseAngle = angle;
            hasLastAngle = true;
        }
        else
        {
            hasLastAngle = false;
        }
        // Pause detection: stay slow for pauseResetTime and the wind-up starts over from now.
        stoppedFor = instantRate < stoppedBelowRate ? stoppedFor + dt : 0f;
        if (stoppedFor >= pauseResetTime) windUpStart = Time.time;

        // Remember how many degrees were spun this frame, for the wind-up average.
        spinHistory.Enqueue((Time.time, instantRate * dt));
        while (spinHistory.Count > 1 && Time.time - spinHistory.Peek().time > windUpTime + flickLookback + 0.1f)
            spinHistory.Dequeue();
        spinRate = AverageSpinRate(Time.time);

        // The lasso visibly spins around the player at the same rate.
        if (offset.magnitude >= minCircleRadius)
            lassoAngle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
        loopPos = origin + Dir(lassoAngle) * lassoSpinRadius;

        // Flick detection. Velocity = how far the mouse moved over the last velocityWindow seconds.
        var oldest = mouseHistory.Peek();
        float span = Time.time - oldest.time;
        Vector2 velocity = span > 0f ? (mouseWorld - oldest.pos) / span : Vector2.zero;

        // Dot product = how much of that movement points away from the player.
        // Circling moves sideways (about 0); a flick moves outward (large).
        float outwardSpeed = offset.magnitude >= minCircleRadius ? Vector2.Dot(velocity, offset.normalized) : 0f;
        peakOutwardSpeed = Mathf.Max(peakOutwardSpeed * Mathf.Exp(-3f * dt), outwardSpeed); // For the HUD readout.

        // A flick = moving outward fast enough, AND mostly outward (not sideways like spinning).
        bool isFlick = outwardSpeed >= flickMinSpeed && outwardSpeed >= flickOutwardRatio * velocity.magnitude;
        if (isFlick)
        {
            if (!inFlick)
            {
                // First frame of the flick: lock in power and accuracy before the circling slows down.
                flickSpinRate = AverageSpinRate(Time.time - flickLookback);
                flickPower = Mathf.InverseLerp(minSpinRate, maxSpinRate, flickSpinRate);
                flickShrink = ShrinkProgress();
            }
            inFlick = true;
            flickTime = Time.time;
            flickDirection = velocity.normalized; // Keeps updating, so the final direction of the flick wins.
        }
        else
        {
            inFlick = false;
        }

        // ---- Visuals ----
        float power = Power();
        float shrink = ShrinkProgress();
        var quality = QualityFor(shrink);
        bool canThrow = spinRate >= minSpinRate;

        loop.transform.position = loopPos;
        spinGuide.transform.position = origin;
        spinGuide.transform.localScale = Vector3.one * lassoSpinRadius * 2f;

        // Timing ring: stays on the player and shrinks with the accuracy cycle (Pokemon GO style).
        // Its color = the accuracy you'd get by flicking right now.
        timingRing.transform.position = origin;
        timingRing.transform.localScale = Vector3.one * Mathf.Lerp(timingRingMaxRadius, timingRingMinRadius, shrink) * 2f;
        timingRing.color = canThrow ? QualityColor(quality) : new Color(1f, 1f, 1f, 0.3f);

        // Debug only: range ring (how far you'd throw right now) and the landing preview after a flick.
        rangeRing.enabled = showDebugPreview;
        rangeRing.transform.position = origin;
        rangeRing.transform.localScale = Vector3.one * Mathf.Lerp(minRange, maxRange, power) * 2f;
        var ringColor = canThrow ? QualityColor(quality) : Color.white;
        ringColor.a = canThrow ? 0.8f : 0.25f;
        rangeRing.color = ringColor;

        // After a flick, during the release window, show where the throw will land.
        bool flicked = Time.time - flickTime <= releaseWindow;
        accuracyCircle.enabled = catchPreview.enabled = flicked && showDebugPreview;
        if (flicked)
        {
            Vector2 target = origin + flickDirection * Mathf.Lerp(minRange, maxRange, flickPower);
            accuracyCircle.transform.position = target;
            accuracyCircle.transform.localScale = Vector3.one * CircleRadius(flickShrink) * 2f;
            accuracyCircle.color = QualityColor(QualityFor(flickShrink));
            catchPreview.transform.position = target;
            catchPreview.transform.localScale = Vector3.one * catchRadius * 2f;
        }

        string readout = $"Wind-up {spinRate / 360f:0.0} circles/sec (avg over {windUpTime:0.0}s)  |  power {power:P0}  |  outward {peakOutwardSpeed:0} (flick at {flickMinSpeed:0})";
        LiveSpinSummary = flicked ? readout + "  |  RELEASE NOW!"
                        : canThrow ? readout + $"  |  flick now = {quality}"
                        : readout + "  |  spin faster!";
    }

    void AddHistory(Vector2 pos)
    {
        mouseHistory.Enqueue((Time.time, pos));
        while (mouseHistory.Count > 1 && Time.time - mouseHistory.Peek().time > velocityWindow)
            mouseHistory.Dequeue();
    }

    float Power() => Mathf.InverseLerp(minSpinRate, maxSpinRate, spinRate);

    // Average spin (degrees/sec) over the windUpTime seconds ending at endTime.
    // Always divides by the full windUpTime, so time before the spin started (or before
    // the last pause) counts as zero. That's what makes a wind-up necessary.
    float AverageSpinRate(float endTime)
    {
        float start = Mathf.Max(endTime - windUpTime, windUpStart);
        float degrees = 0f;
        foreach (var entry in spinHistory)
            if (entry.time > start && entry.time <= endTime) degrees += entry.degrees;
        return degrees / windUpTime;
    }

    // 0 = circle at its largest, 1 = smallest. Loops while you keep spinning.
    float ShrinkProgress() => Mathf.Repeat((Time.time - spinStartTime) / circleShrinkTime, 1f);

    float CircleRadius(float shrink) => Mathf.Lerp(circleMaxRadius, circleMinRadius, shrink);

    ThrowQuality QualityFor(float shrink) =>
        shrink >= perfectFrom ? ThrowQuality.Perfect : shrink >= goodFrom ? ThrowQuality.Good : ThrowQuality.Sloppy;

    void Cancel()
    {
        state = State.Idle;
        SetVisible(false);
        LiveSpinSummary = "";
    }

    // ---------- Throw ----------

    void Throw(Vector2 origin)
    {
        // Everything comes from the values saved when the flick started.
        if (flickSpinRate < minSpinRate)
        {
            LassoArena.Popup("Spin faster!", origin + Vector2.up, Color.gray);
            Cancel();
            return;
        }

        float power = flickPower;
        float shrink = flickShrink;
        float radius = CircleRadius(shrink);
        var quality = QualityFor(shrink);
        var fx = quality == ThrowQuality.Perfect ? perfect : quality == ThrowQuality.Good ? good : sloppy;

        float range = Mathf.Lerp(minRange, maxRange, power);
        Vector2 projected = origin + flickDirection * range;
        landingPoint = projected + Random.insideUnitCircle * radius; // Lands anywhere inside the accuracy circle.
        flightSpeed = Mathf.Lerp(minFlightSpeed, maxFlightSpeed, power) * fx.speedMultiplier;
        ropeStrength = baseRopeStrength * fx.ropeStrengthMultiplier;
        state = State.Flying;

        accuracyCircle.enabled = catchPreview.enabled = rangeRing.enabled = spinGuide.enabled = timingRing.enabled = false;
        loop.transform.localScale = Vector3.one * catchRadius * 2f;

        LassoArena.Popup(QualityText(quality), origin + Vector2.up, QualityColor(quality));
        LiveSpinSummary = "";
        LastThrowSummary = $"{quality}  |  spin {flickSpinRate / 360f:0.0}/sec  |  power {power:P0}  |  range {range:0.0}  |  accuracy circle {radius:0.0}  |  rope strength {ropeStrength:0.0}";
    }

    // ---------- Landing and pulling ----------

    void Land(Vector2 origin)
    {
        hooked.Clear();
        hookedOffsets.Clear();
        float totalWeight = 0f;
        bool stole = false;

        foreach (var t in Treasure.All)
        {
            if (Vector2.Distance(t.transform.position, landingPoint) > catchRadius) continue;
            bool catchable = t.state == TreasureState.Free
                || (t.state == TreasureState.InZone && t.zone != homeZone);
            if (!catchable) continue;

            stole |= t.state == TreasureState.InZone;
            t.state = TreasureState.Hooked;
            t.zone = null;
            hooked.Add(t);
            hookedOffsets.Add((Vector2)t.transform.position - landingPoint);
            totalWeight += t.weight;
        }

        pullStart = landingPoint;
        willSnap = totalWeight > ropeStrength;
        snapDistance = Vector2.Distance(landingPoint, origin) * snapAtFraction;
        state = State.Pulling;

        if (hooked.Count == 0) LassoArena.Popup("miss", landingPoint, Color.gray);
        else if (stole) LassoArena.Popup("STOLEN!", landingPoint, new Color(1f, 0.4f, 0.8f));
        if (willSnap) LastThrowSummary += $"  |  load {totalWeight:0} > strength, will snap!";
    }

    void UpdatePull(Vector2 origin, float dt)
    {
        float weight = 0f;
        foreach (var t in hooked) weight += t.weight;
        float speed = hooked.Count == 0 ? retractSpeed : pullSpeed / (1f + weight * weightSlowdown);

        loopPos = Vector2.MoveTowards(loopPos, origin, speed * dt);

        // Shrink the loop's cluster as it's dragged, so items bunch up behind the rope.
        for (int i = 0; i < hooked.Count; i++)
        {
            hookedOffsets[i] = Vector2.MoveTowards(hookedOffsets[i], Vector2.zero, dt);
            hooked[i].transform.position = loopPos + hookedOffsets[i];
        }

        if (willSnap && Vector2.Distance(loopPos, pullStart) >= snapDistance) Snap();

        if (loopPos == origin) Deliver();
    }

    void Snap()
    {
        foreach (var t in hooked) t.state = TreasureState.Free;
        hooked.Clear();
        hookedOffsets.Clear();
        willSnap = false;
        LassoArena.Popup("SNAP!", loopPos, new Color(1f, 0.3f, 0.2f));
    }

    void Deliver()
    {
        int gained = 0;
        foreach (var t in hooked)
        {
            homeZone.Store(t, homeZone.RandomPoint());
            gained += t.value;
        }
        if (gained > 0) LassoArena.Popup($"+{gained}", (Vector2)transform.position + Vector2.up, new Color(1f, 0.85f, 0.2f));

        hooked.Clear();
        hookedOffsets.Clear();
        state = State.Idle;
        SetVisible(false);
    }

    // ---------- Visuals ----------

    void DrawRope(Vector2 from, Vector2 to)
    {
        loop.transform.position = to;
        Vector2 delta = to - from;
        rope.transform.position = from + delta * 0.5f;
        rope.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        rope.transform.localScale = new Vector3(delta.magnitude, 0.06f, 1f);
    }

    void SetVisible(bool visible)
    {
        loop.enabled = rope.enabled = spinGuide.enabled = timingRing.enabled = visible;
        rangeRing.enabled = visible && showDebugPreview;
        accuracyCircle.enabled = catchPreview.enabled = false; // Only shown after a flick, in debug mode.
    }

    // ---------- Helpers ----------

    static Vector2 Dir(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

    static string QualityText(ThrowQuality q) => q == ThrowQuality.Perfect ? "PERFECT!" : q == ThrowQuality.Good ? "Good" : "Sloppy...";

    static Color QualityColor(ThrowQuality q) => q == ThrowQuality.Perfect ? new Color(0.3f, 1f, 0.4f)
                                                : q == ThrowQuality.Good ? new Color(1f, 0.9f, 0.3f)
                                                : new Color(1f, 0.4f, 0.3f);
}
