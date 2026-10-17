using UnityEngine;

/// <summary>
/// Runtime-generated white shapes (1 world unit wide) so the prototype needs no art.
/// Tint them with SpriteRenderer.color. Swap for real sprites later.
/// </summary>
public static class PlaceholderArt
{
    static Sprite circle, square, ring, thinRing;

    // Use Unity's == null (not ??=): it also catches sprites Unity destroyed when Play mode ended,
    // which matters because this project skips domain reload on entering Play mode.
    public static Sprite Circle => circle != null ? circle : circle = Make(r => Mathf.Clamp01((1f - r) * 32f));
    public static Sprite Ring => ring != null ? ring : ring = Make(r => Mathf.Clamp01((1f - r) * 32f) * Mathf.Clamp01((r - 0.82f) * 32f));
    public static Sprite Square => square != null ? square : square = Make(_ => 1f);
    // Higher resolution with a thin edge, for big rings like the throw range.
    public static Sprite ThinRing => thinRing != null ? thinRing : thinRing = Make(r => Mathf.Clamp01((1f - r) * 128f) * Mathf.Clamp01((r - 0.985f) * 128f), 512);

    public static SpriteRenderer CreateSprite(string name, Sprite sprite, Color color, int order, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    // alpha(r): r is distance from the center, 0 at the middle and 1 at the edge.
    static Sprite Make(System.Func<float, float> alpha, int size = 64)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f;
            float dy = (y + 0.5f) / size * 2f - 1f;
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha(Mathf.Sqrt(dx * dx + dy * dy)));
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
