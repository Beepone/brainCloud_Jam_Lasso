using System.Collections.Generic;
using UnityEngine;

public enum TreasureKind { Coin, Chest, Idol }
public enum TreasureState { Free, Hooked, InZone }

/// <summary>
/// A lassoable item. Moved kinematically (no physics) so throws can later be
/// replayed identically for ghosts.
/// </summary>
public class Treasure : MonoBehaviour
{
    public static readonly List<Treasure> All = new List<Treasure>();

    public TreasureKind kind;
    public int value;
    public float weight;
    public TreasureState state = TreasureState.Free;
    public TreasureZone zone; // Set while InZone.

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public static Treasure Spawn(TreasureKind kind, Vector2 position, Transform parent)
    {
        Sprite sprite;
        Color color;
        float size;
        var t = PlaceholderArt.CreateSprite(kind.ToString(), null, Color.white, 0, parent).gameObject.AddComponent<Treasure>();
        t.kind = kind;
        switch (kind)
        {
            case TreasureKind.Chest:
                t.value = 5; t.weight = 3f;
                sprite = PlaceholderArt.Square; color = new Color(0.65f, 0.4f, 0.2f); size = 0.8f;
                break;
            case TreasureKind.Idol:
                t.value = 15; t.weight = 6f;
                sprite = PlaceholderArt.Circle; color = new Color(0.3f, 1f, 0.6f); size = 0.7f;
                break;
            default:
                t.value = 1; t.weight = 1f;
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
}
