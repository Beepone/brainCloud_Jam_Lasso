using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lasso prototype bootstrap: add this to an empty GameObject in an empty scene and press Play.
/// Builds the arena, player, rival zone and treasure from code. R rebuilds it.
/// </summary>
public class LassoArena : MonoBehaviour
{
    [Header("Layout")]
    public float cameraSize = 9f;
    public Rect playerZone = new Rect(-4f, -8.5f, 8f, 3f);
    public Rect rivalZone = new Rect(-4f, 5.5f, 8f, 3f);
    public Rect field = new Rect(-11f, -4f, 22f, 8.5f);

    [Header("Treasure")]
    public int coins = 14;
    public int chests = 5;
    public int idols = 2;
    [Tooltip("Treasure the rival starts with, so you can practice stealing.")]
    public int rivalCoins = 3;
    public int rivalChests = 1;
    [Tooltip("Same seed = same layout every time. Set to 0 for a random layout.")]
    public int seed = 1234;

    TreasureZone home, rival;
    LassoThrower thrower;

    struct PopupText { public string text; public Vector2 position; public Color color; public float startTime; }
    static readonly List<PopupText> popups = new List<PopupText>();
    const float PopupDuration = 1.2f;

    void Start() => Build();

    void Build()
    {
        popups.Clear();
        if (seed != 0) Random.InitState(seed);

        var cam = Camera.main;
        cam.orthographic = true;
        cam.orthographicSize = cameraSize;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.backgroundColor = new Color(0.12f, 0.2f, 0.3f);

        home = TreasureZone.Create("Player", playerZone, new Color(0.3f, 0.6f, 1f, 0.25f), transform);
        rival = TreasureZone.Create("Rival", rivalZone, new Color(1f, 0.35f, 0.35f, 0.25f), transform);

        var player = PlaceholderArt.CreateSprite("Player", PlaceholderArt.Circle, new Color(0.5f, 0.8f, 1f), 4, transform);
        player.transform.position = new Vector2(playerZone.center.x, playerZone.center.y);
        player.transform.localScale = Vector3.one * 0.9f;
        thrower = player.gameObject.AddComponent<LassoThrower>();
        thrower.homeZone = home;

        SpawnInField(TreasureKind.Coin, coins);
        SpawnInField(TreasureKind.Chest, chests);
        SpawnInField(TreasureKind.Idol, idols);
        for (int i = 0; i < rivalCoins; i++) rival.Store(Treasure.Spawn(TreasureKind.Coin, Vector2.zero, transform), rival.RandomPoint());
        for (int i = 0; i < rivalChests; i++) rival.Store(Treasure.Spawn(TreasureKind.Chest, Vector2.zero, transform), rival.RandomPoint());
    }

    void SpawnInField(TreasureKind kind, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var pos = new Vector2(Random.Range(field.xMin, field.xMax), Random.Range(field.yMin, field.yMax));
            Treasure.Spawn(kind, pos, transform);
        }
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            // Deactivate first so Treasure.All is updated immediately, then rebuild.
            foreach (Transform child in transform)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            Build();
        }
    }

    public static void Popup(string text, Vector2 worldPosition, Color color)
    {
        popups.Add(new PopupText { text = text, position = worldPosition, color = color, startTime = Time.time });
    }

    void OnGUI()
    {
        float unit = Screen.height / 720f;
        var hud = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * unit) };
        var big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * unit), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

        int free = 0;
        foreach (var t in Treasure.All) if (t.state == TreasureState.Free) free++;

        GUI.Label(new Rect(12 * unit, 8 * unit, Screen.width, 30 * unit),
            $"Your hoard: {home.Score()}     Rival hoard: {rival.Score()}     Treasure in field: {free}", hud);
        GUI.Label(new Rect(12 * unit, 34 * unit, Screen.width, 30 * unit),
            "Hold left mouse and circle around the player: faster = farther. Flick outward to aim, then release quickly.   Right click = cancel   R = reset", hud);
        GUI.Label(new Rect(12 * unit, Screen.height - 62 * unit, Screen.width, 30 * unit), thrower.LiveSpinSummary, hud);
        GUI.Label(new Rect(12 * unit, Screen.height - 36 * unit, Screen.width, 30 * unit), thrower.LastThrowSummary, hud);

        var cam = Camera.main;
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var p = popups[i];
            float age = Time.time - p.startTime;
            if (age > PopupDuration) { popups.RemoveAt(i); continue; }

            Vector3 screen = cam.WorldToScreenPoint(p.position + Vector2.up * age);
            var color = p.color;
            color.a = 1f - age / PopupDuration;
            big.normal.textColor = color;
            GUI.Label(new Rect(screen.x - 150 * unit, Screen.height - screen.y - 20 * unit, 300 * unit, 40 * unit), p.text, big);
        }
    }
}
