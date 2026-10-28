using UnityEngine;

namespace LootLasso
{
    // PixelShadow: a small dark pixel oval on the ground under something (treasure, hunters).
    //
    // It's drawn just above the ground and hoards, below everything standing on them, and kept straight
    // down the SCREEN from its owner, so it's right for the online guest's upside-down camera too.
    public static class PixelShadow
    {
        const float ArtPixel = 1f / 23f; // one art pixel in world units

        // The oval: 10 x 4 art pixels, made once and shared.
        static Sprite sprite;
        static Sprite Sprite
        {
            get
            {
                if (sprite != null) return sprite; // (Unity's != also catches a sprite destroyed at the end of Play)
                string[] rows = { "..######..", "##########", "##########", "..######.." };
                var tex = new Texture2D(10, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 10; x++)
                        tex.SetPixel(x, 3 - y, rows[y][x] == '#' ? Color.white : Color.clear); // textures count rows from the bottom
                tex.Apply();
                // 23 pixels per unit: the same scale as all the other art.
                return sprite = Sprite.Create(tex, new Rect(0, 0, 10, 4), new Vector2(0.5f, 0.5f), 23f);
            }
        }

        // Makes a shadow as a child of "owner". widthScale stretches it sideways (1 = 10 pixels wide).
        public static Transform Create(Transform owner, float darkness, float widthScale = 1f)
        {
            var sr = PlaceholderArt.CreateSprite("Shadow", Sprite, new Color(0f, 0f, 0f, darkness), YSort.Ground + 1, owner);
            sr.transform.localScale = new Vector3(widthScale, 1f, 1f);
            return sr.transform;
        }

        // Puts the shadow "drop" art pixels below its owner on screen, lying flat (turned with the camera).
        public static void Place(Transform shadow, Transform owner, float drop)
        {
            var cam = Camera.main;
            if (shadow == null || cam == null) return;
            shadow.rotation = cam.transform.rotation;
            shadow.position = owner.position - cam.transform.up * (drop * ArtPixel);
        }
    }
}
