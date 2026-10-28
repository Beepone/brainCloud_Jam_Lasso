using UnityEngine;

namespace LootLasso
{
    // TreasureArt: picks the animation for a piece of treasure drawn with animated art (an imported
    // Aseprite file, set as the kind's animatedArt on the Arena). Treasure.Spawn adds it.
    //
    // Animations (Aseprite tags; the coin uses these, and the chest and idol should use the same names):
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
        [Range(0f, 1f)] public float shadowDarkness = 0.35f;

        [Header("Readouts")]
        public string animationName;
        public bool facingRight = true;

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

            shadow = PixelShadow.Create(transform, shadowDarkness);
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
            }

            string face = facingRight ? "rightface" : "leftface";
            string next = treasure.state switch // "switch" picks one answer by the state's value
            {
                Treasure.State.Hooked => "struggle" + face,
                Treasure.State.Free when moving => "fly" + face,
                _ => "idlefloat", // "_" = anything else: pausing, or in a hoard
            };

            if (next != animationName && Has(next))
            {
                animator.Play(next, 0, 0f);
                animationName = next;
            }
        }

        bool Has(string state) => animator.HasState(0, Animator.StringToHash(state));
    }
}
