using UnityEngine;

namespace LootLasso
{
    // StandInGround: a desert floor drawn in code, until the artists' ground arrives.
    //
    // One 180 x 320 pixel picture (exactly one screen, at the game's 23 pixels per unit): sand with soft
    // darker patches, specks, pebbles and dry grass tufts. It's kept plain on purpose, so the treasure stands
    // out. It repeats in every direction (tiled) to fill wide browser windows, and it's lit by the scene's
    // light like everything else, so it goes from noon to night too (see DayNight).
    //
    // The same random seed every time, so the ground never changes between plays.
    // The Arena adds this when "Stand In Ground" is ticked; untick it when the real ground art is in.
    public class StandInGround : MonoBehaviour
    {
        const int Width = 180, Height = 320; // one screen, in art pixels

        static readonly Color Sand = new Color32(222, 186, 132, 255);
        static readonly Color SandDark = new Color32(206, 166, 112, 255);
        static readonly Color SandLight = new Color32(234, 202, 152, 255);
        static readonly Color Speck = new Color32(188, 146, 96, 255);
        static readonly Color Pebble = new Color32(150, 120, 92, 255);
        static readonly Color PebbleLight = new Color32(186, 160, 132, 255);
        static readonly Color Grass = new Color32(150, 146, 74, 255);
        static readonly Color GrassDark = new Color32(118, 116, 58, 255);

        SpriteRenderer ground;

        void Start()
        {
            var sprite = MakeSprite();
            ground = PlaceholderArt.CreateSprite("Stand-in Ground", sprite, Color.white, YSort.Ground - 1, transform);
            ground.transform.position = new Vector3(0f, 0f, 0f);
            // Tiled: the picture repeats to fill "size" (world units) instead of stretching.
            // 3 screens wide and 2 tall covers any window shape, with the middle screen centred on the camera.
            ground.drawMode = SpriteDrawMode.Tiled;
            ground.size = new Vector2(Width * 3f, Height * 2f) / 23f;
        }

        static Sprite MakeSprite()
        {
            var random = new System.Random(1849); // a fixed seed (the gold rush year), so it's the same every time
            var pixels = new Color[Width * Height];

            // 1. Sand, with big soft patches of darker and lighter sand. The patches come from a coarse grid
            //    of random values (one per 20 x 20 block: 180 and 320 both divide by 20, so it tiles cleanly),
            //    smoothly blended between blocks.
            const int Block = 20;
            int gw = Width / Block + 2, gh = Height / Block + 2;
            var grid = new float[gw, gh];
            for (int x = 0; x < gw; x++)
                for (int y = 0; y < gh; y++)
                    grid[x, y] = (float)random.NextDouble();
            // So the picture tiles without a visible edge, the grid wraps around: the last column copies the first.
            int cols = Width / Block, rows = Height / Block;
            for (int y = 0; y < gh; y++) { grid[cols, y] = grid[0, y]; grid[cols + 1, y] = grid[1, y]; }
            for (int x = 0; x < gw; x++) { grid[x, rows] = grid[x, 0]; grid[x, rows + 1] = grid[x, 1]; }

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float fx = (float)x / Block, fy = (float)y / Block;
                    int ix = (int)fx, iy = (int)fy;
                    float tx = Smooth(fx - ix), ty = Smooth(fy - iy);
                    float value = Mathf.Lerp(Mathf.Lerp(grid[ix, iy], grid[ix + 1, iy], tx),
                                             Mathf.Lerp(grid[ix, iy + 1], grid[ix + 1, iy + 1], tx), ty);
                    // A checkerboard "dither" at the patch edges mixes the two sands pixel by pixel,
                    // the classic pixel-art way to blend without extra colours.
                    bool checker = ((x + y) & 1) == 0;
                    Color c = value < 0.3f ? SandDark
                            : value < 0.36f ? (checker ? SandDark : Sand)
                            : value > 0.78f ? SandLight
                            : value > 0.72f ? (checker ? SandLight : Sand)
                            : Sand;
                    pixels[y * Width + x] = c;
                }

            // 2. Scattered single-pixel specks.
            for (int i = 0; i < 260; i++) Set(pixels, random.Next(Width), random.Next(Height), Speck);

            // 3. Pebbles: 2 x 1 or 2 x 2, with a lighter top-left pixel (the sun is up and to the left).
            for (int i = 0; i < 30; i++)
            {
                int x = random.Next(Width), y = random.Next(Height);
                bool big = random.Next(3) == 0;
                Set(pixels, x, y, Pebble);
                Set(pixels, x + 1, y, Pebble);
                if (big) { Set(pixels, x, y + 1, PebbleLight); Set(pixels, x + 1, y + 1, Pebble); }
                else Set(pixels, x, y, PebbleLight);
            }

            // 4. Dry grass tufts: a few blades fanning up from one root.
            for (int i = 0; i < 22; i++)
            {
                int x = random.Next(Width), y = random.Next(Height);
                Set(pixels, x, y, GrassDark);         // root
                Set(pixels, x, y + 1, Grass);         // middle blade
                Set(pixels, x, y + 2, Grass);
                Set(pixels, x - 1, y + 1, GrassDark); // side blades
                Set(pixels, x + 1, y + 1, GrassDark);
                if (random.Next(2) == 0) Set(pixels, x - 2, y + 2, Grass);
                if (random.Next(2) == 0) Set(pixels, x + 2, y + 2, Grass);
            }

            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,   // crisp pixels
                wrapMode = TextureWrapMode.Repeat, // so it tiles
            };
            tex.SetPixels(pixels);
            tex.Apply();
            // FullRect: tiling needs the sprite to be a plain rectangle (not trimmed to its shape).
            return Sprite.Create(tex, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0.5f), 23f, 0, SpriteMeshType.FullRect);
        }

        // Eases 0..1 so blends between blocks have no sharp corners.
        static float Smooth(float t) => t * t * (3f - 2f * t);

        // Sets a pixel, wrapping around the edges (so things near an edge continue on the other side when tiled).
        static void Set(Color[] pixels, int x, int y, Color c)
        {
            x = (x % Width + Width) % Width;
            y = (y % Height + Height) % Height;
            pixels[y * Width + x] = c;
        }
    }
}
