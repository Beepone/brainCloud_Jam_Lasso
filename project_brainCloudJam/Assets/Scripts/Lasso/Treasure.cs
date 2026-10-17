using System.Collections.Generic;
using UnityEngine;

public enum TreasureKind { Coin, Chest, Idol }
public enum TreasureState { Free, Hooked, InZone }

/// <summary>
/// A lassoable item. While Free it wanders around the field on its own.
///
/// The wandering is deterministic: the position is calculated from a seed, a start point
/// and the match clock, not simulated frame by frame. Two players' games with the same
/// seed and clock compute the same position, so treasure movement never has to be sent
/// over the network.
/// </summary>
public class Treasure : MonoBehaviour
{
    public static readonly List<Treasure> All = new List<Treasure>();

    public int id;
    public TreasureKind kind;
    public int value;
    public float weight;
    [Tooltip("Wander speed in world units per second. More valuable = faster.")]
    public float speed;
    public TreasureState state = TreasureState.Free;
    public TreasureZone zone; // Set while InZone.
    [HideInInspector] public int timesStored; // Varies the shared zone placement each time it's caught.

    // Current wander segment: move from 'from' to 'to' between segStart and segEnd (match time).
    Prng rng;
    Rect bounds;
    Vector2 from, to;
    float segStart, segEnd;
    int restarts; // Bumped each time the wander restarts (e.g. after a snap), to vary the seed.

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public static Treasure Spawn(int id, TreasureKind kind, Vector2 position, Transform parent)
    {
        Sprite sprite;
        Color color;
        float size;
        var t = PlaceholderArt.CreateSprite(kind.ToString() + " " + id, null, Color.white, 0, parent).gameObject.AddComponent<Treasure>();
        t.id = id;
        t.kind = kind;
        switch (kind)
        {
            case TreasureKind.Chest:
                t.value = 5; t.weight = 3f; t.speed = 2f;
                sprite = PlaceholderArt.Square; color = new Color(0.65f, 0.4f, 0.2f); size = 0.8f;
                break;
            case TreasureKind.Idol:
                t.value = 15; t.weight = 6f; t.speed = 3.5f;
                sprite = PlaceholderArt.Circle; color = new Color(0.3f, 1f, 0.6f); size = 0.7f;
                break;
            default:
                t.value = 1; t.weight = 1f; t.speed = 1f;
                sprite = PlaceholderArt.Circle; color = new Color(1f, 0.85f, 0.2f); size = 0.4f;
                break;
        }
        var sr = t.GetComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        t.transform.position = position;
        t.transform.localScale = Vector3.one * size;
        return t;
    }

    // ---------- Wandering ----------

    /// <summary>Start (or restart) wandering from where it is now. Same inputs = same path on every machine.</summary>
    public void StartWander(int matchSeed, Rect field, float matchTime)
    {
        rng = new Prng(matchSeed * 7919 + id * 104729 + restarts * 15485863);
        restarts++;
        bounds = field;
        from = to = transform.position;
        segStart = segEnd = matchTime;
    }

    /// <summary>Move to where this treasure should be at the given match time.</summary>
    public void UpdateWander(float matchTime)
    {
        if (rng == null) return;

        // Catch up through any segments that finished (also handles big time jumps).
        while (matchTime >= segEnd) NextSegment();

        float t = Mathf.InverseLerp(segStart, segEnd, matchTime);
        transform.position = Vector2.Lerp(from, to, t);
    }

    void NextSegment()
    {
        from = to;
        segStart = segEnd;

        // Sometimes pause for a moment, so they feel alive rather than robotic.
        if (rng.Range(0f, 1f) < 0.25f)
        {
            segEnd = segStart + rng.Range(0.3f, 1.2f);
            return;
        }

        // Otherwise run a short distance in a random direction, staying inside the field.
        float angle = rng.Range(0f, Mathf.PI * 2f);
        float distance = rng.Range(1.5f, 4f);
        Vector2 target = from + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        target.x = Mathf.Clamp(target.x, bounds.xMin, bounds.xMax);
        target.y = Mathf.Clamp(target.y, bounds.yMin, bounds.yMax);

        to = target;
        segEnd = segStart + Mathf.Max(Vector2.Distance(from, to) / speed, 0.05f);
    }
}

/// <summary>
/// Tiny deterministic random number generator (xorshift). Unlike UnityEngine.Random it has
/// no shared global state, and it gives identical results on every platform.
/// </summary>
public class Prng
{
    uint state;

    public Prng(int seed)
    {
        state = (uint)seed;
        if (state == 0) state = 0x9E3779B9;
        for (int i = 0; i < 4; i++) Next(); // Mix the seed a little.
    }

    public uint Next()
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    public float Range(float min, float max) => min + (Next() / (float)uint.MaxValue) * (max - min);
}
