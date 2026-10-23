using System.Collections.Generic; // List<T>
using UnityEngine;

// A "namespace" groups names together, like a folder for code. Everything in here is
// LootLasso.Arena, LootLasso.Treasure, ... so it can't clash with the old prototype's classes
// (which also have a Treasure class) or with code from other packages.
namespace LootLasso
{
    // The arena: the field the treasure wanders in, the two hoard zones, and the match clock.
    // It spawns the treasure at the start and moves the wandering treasure every frame.
    //
    // The WORLD is symmetric: it mirrors top-to-bottom around the middle of the field, so both players
    // have the same size hoard and the same spin area. That way a steal lands in the same place on both
    // players' screens (online). The SCREEN is not quite symmetric: the camera sits a little low, so
    // there's a spare strip below your hoard and the rival's hoard runs right up to the top edge.
    //
    //      world                                  your screen (camera size 7: y -7 to 7)
    //      y =  7.0  ---------------------------  top of the screen
    //                rival's hoard (mirror of yours), rival at its front edge (y = 5.38)
    //      y =  5.38 ---------------------------
    //                rival's spin room
    //      y =  4.3  ---------------------------
    //                FIELD: treasure wanders here. Mirror line through its middle (y = 0.69)
    //      y = -2.92 ---------------------------
    //                your spin room
    //      y = -4.0  ---------------------------  you stand here, at your hoard's front edge
    //                your hoard
    //      y = -5.6  ---------------------------
    //                (spare strip; its mirror is just off the top of the screen)
    //      y = -7.0  ---------------------------  bottom of the screen
    public class Arena : MonoBehaviour
    {
        // "static" = belongs to the class itself, not to one object. So any script can write
        // Arena.Current to find the arena, without needing a reference to it.
        public static Arena Current { get; private set; }

        [Header("Layout (world units). Rect = x, y of the bottom-left corner, then width, height.")]
        [Tooltip("Where treasure wanders. Its middle (top to bottom) is the mirror line between the two sides.")]
        public Rect field = new Rect(-3.6f, -2.92f, 7.2f, 7.22f);
        [Tooltip("Your hoard: a strip behind your character. The rival's hoard is its mirror image.")]
        public Rect yourHoard = new Rect(-3.9f, -5.6f, 7.8f, 1.6f);

        [Header("Calculated (read only: change Field and Your Hoard instead)")]
        [Tooltip("The mirror image of Your Hoard, so both hoards are always exactly the same size.")]
        public Rect rivalHoard;

        // [System.Serializable] lets Unity show this little bundle of settings in the Inspector,
        // so each kind of treasure can be edited there (and the artists can drop their sprites in).
        [System.Serializable]
        public class TreasureKind
        {
            public string name = "Coin";
            [Tooltip("Score when it's in your hoard.")]
            public int value = 1;
            [Tooltip("How heavy it is for the rope. Rope strength: 4 (big loop) to 9 (small loop).")]
            public float weight = 1f;
            [Tooltip("Wander speed, world units per second. More valuable = faster.")]
            public float speed = 1f;
            [Tooltip("Diameter, world units.")]
            public float size = 0.4f;
            [Tooltip("The art. Empty = a placeholder circle in 'color'.")]
            public Sprite sprite;
            public Color color = Color.yellow;
            [Tooltip("How many of these are in the field at the start of a match.")]
            public int count = 10;
        }

        [Header("Treasure")]
        // A List of TreasureKinds, editable in the Inspector (+ and - buttons add and remove kinds).
        public List<TreasureKind> kinds = new List<TreasureKind>
        {
            new TreasureKind { name = "Coin",  value = 1,  weight = 1f, speed = 1f,   size = 0.4f, color = new Color(1f, 0.85f, 0.2f),  count = 10 },
            new TreasureKind { name = "Chest", value = 5,  weight = 3f, speed = 2f,   size = 0.7f, color = new Color(0.65f, 0.4f, 0.2f), count = 4 },
            new TreasureKind { name = "Idol",  value = 15, weight = 6f, speed = 3.5f, size = 0.6f, color = new Color(0.3f, 1f, 0.6f),   count = 2 },
        };

        [Tooltip("Same seed = same starting layout and same wandering every match. 0 = a new random seed each match.")]
        public int seed = 0;

        [Header("Readouts")]
        public int matchSeed;
        public float matchTime;

        // Every treasure in the arena. Other scripts (the lasso) look through this to see what they caught.
        public readonly List<Treasure> treasures = new List<Treasure>();

        // When the match started, on the real clock (see MatchTime).
        double matchStart;

        // Seconds since the match started. Treasure positions are calculated from this, so both players
        // in an online match will see the same thing. It uses the real clock rather than Time.time, which
        // pauses during long frames (browser tabs stall a lot) and would make two players drift apart.
        public float MatchTime => (float)(Time.realtimeSinceStartupAsDouble - matchStart);

        // Awake runs before Start, so Current is set before any other script's Start looks for it.
        void Awake()
        {
            Current = this;
            rivalHoard = Mirror(yourHoard);
        }

        // OnValidate runs in the editor whenever a value changes in the Inspector,
        // so the rival hoard updates as you edit yours.
        void OnValidate() => rivalHoard = Mirror(yourHoard);

        // ----- Mirroring -----
        // The world mirrors top-to-bottom around the middle of the field.
        // A point's mirror image is as far above the line as the point is below it (and vice versa).
        public float MirrorLineY => field.center.y;

        public Vector2 Mirror(Vector2 point) => new Vector2(point.x, 2f * MirrorLineY - point.y);

        // A rectangle's mirror image: same width, height and x; its y flipped across the line.
        public Rect Mirror(Rect r) => new Rect(r.x, 2f * MirrorLineY - r.yMax, r.width, r.height);

        void Start()
        {
            DrawZones();
            StartMatch(seed != 0 ? seed : Random.Range(1, int.MaxValue));
        }

        // Clears the arena and spawns a fresh set of treasure. Online, both players will call this
        // with the same seed, so they get identical treasure.
        public void StartMatch(int newSeed)
        {
            foreach (var t in treasures) Destroy(t.gameObject);
            treasures.Clear();

            matchSeed = newSeed;
            matchStart = Time.realtimeSinceStartupAsDouble;

            // Prng = our own random number generator (see Treasure.cs). Same seed = same numbers,
            // on every computer. Unity's Random can't promise that.
            var rng = new Prng(newSeed);
            int id = 0;
            foreach (var kind in kinds)
            {
                for (int i = 0; i < kind.count; i++)
                {
                    var start = new Vector2(rng.Range(field.xMin, field.xMax), rng.Range(field.yMin, field.yMax));
                    treasures.Add(Treasure.Spawn(id++, kind, start, this));
                }
            }
        }

        void Update()
        {
            matchTime = MatchTime; // readout
            foreach (var t in treasures) t.UpdateWander(matchTime);
        }

        // Draws the two hoard zones as faint rectangles (placeholders until the art arrives).
        void DrawZones()
        {
            DrawRect("Your Hoard", yourHoard, new Color(0.3f, 0.6f, 1f, 0.18f));
            DrawRect("Rival Hoard", rivalHoard, new Color(1f, 0.35f, 0.35f, 0.18f));
        }

        void DrawRect(string name, Rect r, Color color)
        {
            var sr = PlaceholderArt.CreateSprite(name, PlaceholderArt.Square, color, -10, transform);
            sr.transform.position = r.center;
            sr.transform.localScale = new Vector3(r.width, r.height, 1f);
        }

        // OnDrawGizmos draws outlines in the Scene view (not in the game), so you can see the
        // layout while editing it in the Inspector. White = field, blue = yours, red = rival's.
        void OnDrawGizmos()
        {
            DrawGizmoRect(field, Color.white);
            DrawGizmoRect(yourHoard, new Color(0.3f, 0.6f, 1f));
            DrawGizmoRect(Mirror(yourHoard), new Color(1f, 0.35f, 0.35f));

            // The mirror line, dashed-looking in yellow, across the field.
            Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
            Gizmos.DrawLine(new Vector3(field.xMin, MirrorLineY), new Vector3(field.xMax, MirrorLineY));
        }

        static void DrawGizmoRect(Rect r, Color c)
        {
            Gizmos.color = c;
            Gizmos.DrawWireCube(r.center, new Vector3(r.width, r.height, 0f));
        }
    }
}
