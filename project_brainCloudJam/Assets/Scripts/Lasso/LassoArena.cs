using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum MatchRole { Solo, Host, Guest }

/// <summary>
/// Lasso prototype bootstrap: add this to an empty GameObject in an empty scene and press Play.
/// Builds the arena, players, zones and treasure from code. R rebuilds it (solo only).
///
/// Solo: you play alone, with some treasure in the rival zone to practice stealing.
/// Host / Guest: started by LassoNetworkTest once two players are connected. The host plays from
/// the bottom zone, the guest from the top (the guest's camera is rotated so they see themselves
/// at the bottom). Both games use the same seed, so treasure spawns and wanders identically.
/// </summary>
public class LassoArena : MonoBehaviour
{
    public static LassoArena Current { get; private set; }

    /// <summary>
    /// Seconds since the match started. Treasure positions are calculated from this.
    /// Uses the real clock, not Time.time: Time.time pauses during long frames (browser tabs stall a lot),
    /// which would make the two players' clocks drift apart.
    /// </summary>
    public static float MatchTime => (float)(Time.realtimeSinceStartupAsDouble - matchStartRealtime);
    static double matchStartRealtime;

    /// <summary>False while the network panel is waiting, so clicks don't start throws.</summary>
    public static bool InputEnabled = true;

    [Header("Layout")]
    public float cameraSize = 9f;
    public Rect bottomZone = new Rect(-4f, -8.5f, 8f, 3f);
    public Rect topZone = new Rect(-4f, 5.5f, 8f, 3f);
    public Rect field = new Rect(-11f, -4f, 22f, 8.5f);

    [Header("Treasure")]
    public int coins = 14;
    public int chests = 5;
    public int idols = 2;
    [Tooltip("Solo only: treasure the rival starts with, so you can practice stealing.")]
    public int rivalCoins = 3;
    public int rivalChests = 1;
    [Tooltip("Solo layout seed. Networked matches use a seed chosen by the host.")]
    public int seed = 1234;

    [Header("Audio (auto-filled from Assets/SFX in the editor if empty)")]
    public AudioClip lassoTwirl;
    public AudioClip lassoThrow;
    public AudioClip lassoGrab;

    public MatchRole Role { get; private set; }
    public LassoThrower LocalThrower { get; private set; }
    public LassoThrower RemoteThrower { get; private set; } // Null in solo.

    TreasureZone myZone, rivalZone;
    int matchSeed;
    public int MatchSeed => matchSeed;

    struct PopupText { public string text; public Vector2 position; public Color color; public float startTime; }
    static readonly List<PopupText> popups = new List<PopupText>();
    const float PopupDuration = 1.2f;

    void Awake() => Current = this;

    void Start()
    {
        if (GetComponent<LassoNetworkTest>() == null) gameObject.AddComponent<LassoNetworkTest>();
        StartMatch(seed, MatchRole.Solo);
    }

#if UNITY_EDITOR
    // Editor only: fill empty clip slots from Assets/SFX, so there's nothing to drag in.
    // Save the scene afterwards to keep them (and for builds).
    void OnValidate()
    {
        if (lassoTwirl == null) lassoTwirl = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/LassoTwirl.ogg");
        if (lassoThrow == null) lassoThrow = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/LassoThrow.ogg");
        if (lassoGrab == null) lassoGrab = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/LassoGrab.ogg");
    }
#endif

    /// <summary>
    /// Clears the arena and builds a fresh match. Both networked players call this with the same seed.
    /// alreadyElapsed: how far into the match we are already. The guest passes the time the start
    /// message took to arrive, so its clock lines up with the host's.
    /// </summary>
    public void StartMatch(int newSeed, MatchRole role, float alreadyElapsed = 0f)
    {
        // Deactivate first so Treasure.All is updated immediately.
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        popups.Clear();

        Role = role;
        matchSeed = newSeed;
        matchStartRealtime = Time.realtimeSinceStartupAsDouble - alreadyElapsed;

        var cam = Camera.main;
        cam.orthographic = true;
        cam.orthographicSize = cameraSize;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        // The guest plays from the top zone; rotate their view so they see themselves at the bottom.
        cam.transform.rotation = Quaternion.Euler(0f, 0f, role == MatchRole.Guest ? 180f : 0f);
        cam.backgroundColor = new Color(0.12f, 0.2f, 0.3f);

        var bottom = TreasureZone.Create("Bottom", bottomZone, new Color(0.3f, 0.6f, 1f, 0.25f), transform);
        var top = TreasureZone.Create("Top", topZone, new Color(1f, 0.35f, 0.35f, 0.25f), transform);
        bool iAmTop = role == MatchRole.Guest;
        myZone = iAmTop ? top : bottom;
        rivalZone = iAmTop ? bottom : top;

        LocalThrower = CreateThrower("You", myZone, new Color(0.5f, 0.8f, 1f), true);
        RemoteThrower = role == MatchRole.Solo ? null : CreateThrower("Opponent", rivalZone, new Color(1f, 0.6f, 0.5f), false);

        // Spawn treasure with the deterministic generator, so both players get the same layout and IDs.
        var rng = new Prng(newSeed);
        int nextId = 0;
        SpawnInField(rng, ref nextId, TreasureKind.Coin, coins);
        SpawnInField(rng, ref nextId, TreasureKind.Chest, chests);
        SpawnInField(rng, ref nextId, TreasureKind.Idol, idols);

        if (role == MatchRole.Solo)
        {
            for (int i = 0; i < rivalCoins; i++) rivalZone.Store(Treasure.Spawn(nextId++, TreasureKind.Coin, Vector2.zero, transform), rivalZone.RandomPoint());
            for (int i = 0; i < rivalChests; i++) rivalZone.Store(Treasure.Spawn(nextId++, TreasureKind.Chest, Vector2.zero, transform), rivalZone.RandomPoint());
        }
    }

    LassoThrower CreateThrower(string name, TreasureZone zone, Color color, bool isLocal)
    {
        var body = PlaceholderArt.CreateSprite(name, PlaceholderArt.Circle, color, 4, transform);
        body.transform.position = zone.area.center;
        body.transform.localScale = Vector3.one * 0.9f;
        var thrower = body.gameObject.AddComponent<LassoThrower>();
        thrower.isLocal = isLocal;
        thrower.homeZone = zone;
        thrower.twirlClip = lassoTwirl;
        thrower.throwClip = lassoThrow;
        thrower.grabClip = lassoGrab;
        return thrower;
    }

    void SpawnInField(Prng rng, ref int nextId, TreasureKind kind, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var pos = new Vector2(rng.Range(field.xMin, field.xMax), rng.Range(field.yMin, field.yMax));
            var t = Treasure.Spawn(nextId++, kind, pos, transform);
            t.StartWander(matchSeed, field, 0f);
        }
    }

    /// <summary>Treasure dropped back into play (e.g. a snapped rope) starts wandering from where it is.</summary>
    public void ReleaseToField(Treasure t)
    {
        t.state = TreasureState.Free;
        t.zone = null;
        t.StartWander(matchSeed, field, MatchTime);
    }

    void Update()
    {
        float now = MatchTime;
        foreach (var t in Treasure.All)
            if (t.state == TreasureState.Free) t.UpdateWander(now);

        if (Role == MatchRole.Solo && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            StartMatch(seed, MatchRole.Solo);
    }

    public static void Popup(string text, Vector2 worldPosition, Color color)
    {
        popups.Add(new PopupText { text = text, position = worldPosition, color = color, startTime = Time.time });
    }

    void OnGUI()
    {
        if (LocalThrower == null) return;
        float unit = Screen.height / 720f;
        var hud = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * unit) };
        var big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * unit), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

        int free = 0;
        foreach (var t in Treasure.All) if (t.state == TreasureState.Free) free++;

        string mode = Role == MatchRole.Solo ? "Solo" : $"Online ({Role})";
        GUI.Label(new Rect(12 * unit, 8 * unit, Screen.width, 30 * unit),
            $"{mode}     Match time: {MatchTime:0.0}s     Your hoard: {myZone.Score()}     Rival hoard: {rivalZone.Score()}     Treasure in field: {free}", hud);
        GUI.Label(new Rect(12 * unit, 34 * unit, Screen.width, 30 * unit),
            "Hold left mouse and circle around the player: faster = farther. Flick outward to aim, then release quickly.   Right click = cancel" +
            (Role == MatchRole.Solo ? "   R = reset" : ""), hud);
        GUI.Label(new Rect(12 * unit, Screen.height - 62 * unit, Screen.width, 30 * unit), LocalThrower.LiveSpinSummary, hud);
        GUI.Label(new Rect(12 * unit, Screen.height - 36 * unit, Screen.width, 30 * unit), LocalThrower.LastThrowSummary, hud);

        var cam = Camera.main;
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var p = popups[i];
            float age = Time.time - p.startTime;
            if (age > PopupDuration) { popups.RemoveAt(i); continue; }

            // Rise in screen space, so popups float upward for the guest's rotated camera too.
            Vector3 screen = cam.WorldToScreenPoint(p.position);
            var color = p.color;
            color.a = 1f - age / PopupDuration;
            big.normal.textColor = color;
            GUI.Label(new Rect(screen.x - 150 * unit, Screen.height - screen.y - (20 + age * 50) * unit, 300 * unit, 40 * unit), p.text, big);
        }
    }
}
