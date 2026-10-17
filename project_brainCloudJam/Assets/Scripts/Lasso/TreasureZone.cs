using UnityEngine;

/// <summary>A player's hoard area. Treasure inside it scores for the owner and can be stolen.</summary>
public class TreasureZone : MonoBehaviour
{
    public string ownerName;
    public Rect area;

    public static TreasureZone Create(string ownerName, Rect area, Color color, Transform parent)
    {
        var sr = PlaceholderArt.CreateSprite(ownerName + " Zone", PlaceholderArt.Square, color, -10, parent);
        sr.transform.position = area.center;
        sr.transform.localScale = new Vector3(area.width, area.height, 1f);
        var zone = sr.gameObject.AddComponent<TreasureZone>();
        zone.ownerName = ownerName;
        zone.area = area;
        return zone;
    }

    public bool Contains(Vector2 point) => area.Contains(point);

    public Vector2 RandomPoint()
    {
        const float margin = 0.5f;
        return new Vector2(
            Random.Range(area.xMin + margin, area.xMax - margin),
            Random.Range(area.yMin + margin, area.yMax - margin));
    }

    public void Store(Treasure t, Vector2 position)
    {
        t.state = TreasureState.InZone;
        t.zone = this;
        t.transform.position = position;
    }

    public int Score()
    {
        int total = 0;
        foreach (var t in Treasure.All)
            if (t.state == TreasureState.InZone && t.zone == this) total += t.value;
        return total;
    }
}
