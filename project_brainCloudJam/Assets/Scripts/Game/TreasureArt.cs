using UnityEngine;

namespace LootLasso
{
    // TreasureArt: picks the animation for a piece of treasure drawn with animated art (an imported
    // Aseprite file, set as the kind's animatedArt on the Arena). Treasure.Spawn adds it.
    //
    // Animations (Aseprite tags; the coin uses these. The chest's Idle and eight walk directions work too,
    // and so do the idol's four floats, see FourWay. Art with none of these just keeps playing its default):
    //   idlefloat           pausing, or sitting in a hoard
    //   flyleftface         wandering, moving left on screen      flyrightface        ...moving right
    //   struggleleftface    caught on a rope, dragged leftwards   strugglerightface   ...dragged rightwards
    //
    // "Left" and "right" are worked out on SCREEN, not in the world: the online guest's camera is turned
    // around, so world-left is screen-right for them. The sprite is also turned with the camera to stay upright.
    //
    // It also gives the treasure a small pixel shadow on the ground, a few pixels below it on screen, so the
    // flying coins look like they're flying.
    [RequireComponent(typeof(Treasure))]
    public class TreasureArt : MonoBehaviour
    {
        [Tooltip("Moving slower than this (world units per second) counts as standing still.")]
        public float stillSpeed = 0.05f;

        [Tooltip("How far below the treasure its shadow sits on screen, in art pixels.")]
        public float shadowDrop = 7f;
        [Tooltip("Shadow width (1 = 10 art pixels).")]
        public float shadowWidth = 1f;
        [Range(0f, 1f)] public float shadowDarkness = 0.35f;

        [Header("Readouts")]
        public string animationName;
        public bool facingRight = true;
        [Tooltip("Eight-way art (the chest): which way it's heading on screen, as a tag name.")]
        public string heading = "walkforward";

        // The chest's eight walk tags, going anticlockwise from screen-right in 45° steps.
        // "front" faces the viewer (walking down the screen), "back" faces away (walking up).
        static readonly string[] EightWay =
        {
            "rightfacewalk", "backrightwalk", "backchestwalk", "backleftwalk",
            "leftfacewalk", "frontleftwalk", "walkforward", "frontrightwalk",
        };

        // The idol's four floats: right, away (up the screen), left, toward the viewer (also its idle).
        // Each lists the spellings to try: the art file has "rightloat" for now, so both work.
        static readonly string[][] FourWay =
        {
            new[] { "rightfloat", "rightloat" }, new[] { "backfloat" },
            new[] { "leftfloat" }, new[] { "idle/flyforward" },
        };
        int fourWay = 3; // which of FourWay it's heading, on screen (3 = toward the viewer)

        Treasure treasure;
        Animator animator;
        Transform art;
        Vector2 lastPosition;
        Transform shadow;

        void Start()
        {
            treasure = GetComponent<Treasure>();
            animator = GetComponentInChildren<Animator>(); // on the art copy (the child object)
            if (animator != null) art = animator.transform;
            lastPosition = transform.position;

            shadow = PixelShadow.Create(transform, shadowDarkness, shadowWidth);
        }

        void LateUpdate()
        {
            if (animator == null || !animator.isInitialized) return;
            var cam = Camera.main;
            if (cam != null)
            {
                art.rotation = cam.transform.rotation; // upright on screen
                PixelShadow.Place(shadow, transform, shadowDrop);
            }

            // How it moved this frame, and how much of that was to the right ON SCREEN. The camera's "right"
            // direction points to screen-right in the world (flipped for the guest), and the dot product
            // measures how much of the movement goes that way.
            Vector2 position = transform.position;
            Vector2 moved = position - lastPosition;
            lastPosition = position;
            float speed = Time.deltaTime > 0f ? moved.magnitude / Time.deltaTime : 0f;
            bool moving = speed > stillSpeed;
            if (moving && cam != null)
            {
                float screenRight = Vector2.Dot(moved, cam.transform.right);
                // Only turn around on a clear sideways move, so it doesn't flicker when moving straight up or down.
                if (Mathf.Abs(screenRight) > moved.magnitude * 0.2f) facingRight = screenRight > 0f;

                // Eight-way: the angle of the move on screen (0° = right, 90° = up), rounded to the nearest 45°.
                float screenUp = Vector2.Dot(moved, cam.transform.up);
                float angle = Mathf.Atan2(screenUp, screenRight) * Mathf.Rad2Deg;
                heading = EightWay[(Mathf.RoundToInt(angle / 45f) + 8) % 8];
                fourWay = (Mathf.RoundToInt(angle / 90f) + 4) % 4; // the same, to the nearest 90°
            }

            string face = facingRight ? "rightface" : "leftface";
            // Each state lists the coin's tag first, then the chest's eight-way walk (the chest walks instead
            // of flying, and has no struggle, so it keeps walking the way it's dragged), then the idol's float.
            string[] four = FourWay[fourWay];
            string[] wanted = treasure.state switch // "switch" picks one answer by the state's value
            {
                Treasure.State.Hooked => Join(new[] { "struggle" + face, "fly" + face, heading }, four),
                Treasure.State.Free when moving => Join(new[] { "fly" + face, heading }, four),
                _ => new[] { "idlefloat", "Idle", "idle/flyforward" }, // "_" = anything else: pausing, or in a hoard
            };

            foreach (var next in wanted)
            {
                if (!Has(next)) continue;
                if (next != animationName)
                {
                    animator.Play(next, 0, 0f);
                    animationName = next;
                }
                break;
            }
        }

        static string[] Join(string[] a, string[] b)
        {
            var all = new string[a.Length + b.Length];
            a.CopyTo(all, 0);
            b.CopyTo(all, a.Length);
            return all;
        }

        bool Has(string state) => animator.HasState(0, Animator.StringToHash(state));
    }
}
