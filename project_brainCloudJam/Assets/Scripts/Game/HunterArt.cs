using UnityEngine;

namespace LootLasso
{
    // HunterArt: draws a hunter with the artists' sprites instead of the placeholder circle.
    //
    // Which sprite:
    //   COLOUR  blue or red, picked by the Arena each match (Arena.IsBlue).
    //   FACING  your own hunter is seen from BEHIND (you look up the field over its shoulder);
    //           the rival faces you, so it's seen from the FRONT.
    //
    // Animations (Aseprite tags, which the importer turns into Animator states):
    //   idle         standing still
    //   lassostart   starting to swing (played once, if this file has it)
    //   lassomotion  swinging the lasso (loops while winding up, faster when spinning faster)
    //
    // Online, the guest's camera is turned upside down, so the sprite is turned with it to stay upright.
    // The Arena adds this to every lasso. If no art is set, the placeholder circle stays.
    [RequireComponent(typeof(LassoSpin))]
    public class HunterArt : MonoBehaviour
    {
        [Tooltip("Swing animation speed at the slowest and fastest spin.")]
        public float minSwingSpeed = 0.6f;
        public float maxSwingSpeed = 2f;

        [Header("Readouts")]
        public string animationName;

        LassoSpin spin;
        SpriteRenderer placeholder;
        GameObject artPrefab; // which imported file is showing
        GameObject body;      // our copy of it
        Animator animator;

        void Start()
        {
            spin = GetComponent<LassoSpin>();
            placeholder = GetComponent<SpriteRenderer>();
        }

        // LateUpdate runs after every Update, so the lasso has already moved and changed phase this frame.
        void LateUpdate()
        {
            var arena = Arena.Current;
            if (arena == null) return;

            var wanted = PickArt(arena);
            if (wanted == null) { ShowPlaceholder(true); return; }
            if (wanted != artPrefab) Swap(wanted);

            // Keep the sprite upright on screen: turn it the same way as the camera.
            if (Camera.main != null) body.transform.rotation = Camera.main.transform.rotation;

            Animate();
        }

        GameObject PickArt(Arena arena)
        {
            bool blue = arena.IsBlue(spin.side);
            bool mine = spin.side == arena.localSide; // the player at this computer: seen from behind
            if (blue) return mine ? arena.hunterBlueBack : arena.hunterBlueFront;
            return mine ? arena.hunterRedBack : arena.hunterRedFront;
        }

        // Replace the current sprite object with a fresh copy of another imported file.
        void Swap(GameObject prefab)
        {
            if (body != null) Destroy(body);
            artPrefab = prefab;
            // Instantiate = make a copy in the scene. Passing our transform makes it a child, so it moves with us.
            body = Instantiate(prefab, transform);
            body.transform.localPosition = Vector3.zero;
            animator = body.GetComponent<Animator>();
            animationName = "";

            ShowPlaceholder(false);
        }

        void ShowPlaceholder(bool show)
        {
            if (placeholder != null) placeholder.enabled = show;
        }

        void Animate()
        {
            if (animator == null || !animator.isInitialized) return;

            string next;
            if (!spin.IsWindingUp)
            {
                next = "idle";
            }
            else if (animationName != "lassostart" && animationName != "lassomotion")
            {
                // Just started winding up: the start animation if this file has one, otherwise straight to the swing.
                next = Has("lassostart") ? "lassostart" : "lassomotion";
            }
            else if (animationName == "lassostart" && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f)
            {
                next = "lassomotion"; // normalizedTime 1 = the start animation has played through once
            }
            else
            {
                next = animationName;
            }

            if (next != animationName && Has(next))
            {
                animator.Play(next, 0, 0f); // layer 0, from the first frame
                animationName = next;
            }

            // The swing speeds up with the spin (idle and the start play at normal speed).
            float spin01 = Mathf.Clamp01(Mathf.Abs(spin.loopSpeed) / spin.maxSpinSpeed);
            animator.speed = animationName == "lassomotion" ? Mathf.Lerp(minSwingSpeed, maxSwingSpeed, spin01) : 1f;
        }

        // Does this file have an animation with that name? (Animator states are looked up by a number made from the name.)
        bool Has(string state) => animator.HasState(0, Animator.StringToHash(state));
    }
}
