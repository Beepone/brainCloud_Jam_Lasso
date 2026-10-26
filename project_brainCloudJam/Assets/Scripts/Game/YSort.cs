using UnityEngine;
using UnityEngine.Rendering; // SortingGroup

namespace LootLasso
{
    // YSort: draws things lower on the screen in FRONT of things higher up, so hunters and treasure
    // overlap like they're standing on the ground (nearer the bottom = closer to you).
    //
    // Unity has a built-in "sort by world Y" setting, but online the guest's camera is upside down, which
    // would flip the order. So this sorts by where the object's centre (its position) is on SCREEN, which is
    // right for both players. Using the position, not the drawn pixels, keeps the order steady while sprites
    // animate (wings flapping or a lasso swinging can't make things swap back and forth).
    //
    // Draw order bands (higher = drawn on top), used everywhere so nothing fights:
    //   Ground      the field and hoards, always at the back
    //   YSorted     hunters and treasure, -10000 (screen top) to 0 (screen bottom)
    //   Rope, Loop  always on top, so you can see where your lasso is
    public class YSort : MonoBehaviour
    {
        public const int Ground = -32000;
        public const int Rope = 32000;
        public const int Loop = 32001;

        [Header("Readouts")]
        public int order;

        // What we set the order on, looked up once and kept (looking them up every frame makes garbage,
        // which causes hitches in the browser). Looked up again if our children change (e.g. HunterArt
        // swapping its sprite object).
        SortingGroup[] groups;
        SpriteRenderer own;
        int childCount = -1;

        // LateUpdate: after everything has moved this frame.
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (childCount != transform.childCount || HasDestroyed())
            {
                childCount = transform.childCount;
                groups = GetComponentsInChildren<SortingGroup>(true); // the artists' imported objects draw through one
                own = GetComponent<SpriteRenderer>();                // the placeholder circle, when there's no art
            }

            // Screen bottom (y = 0) -> order 0; screen top -> -10000. Lower on screen = higher order = in front.
            float screenY = cam.WorldToScreenPoint(transform.position).y;
            order = -Mathf.RoundToInt(screenY / Mathf.Max(1, Screen.height) * 10000f);
            foreach (var group in groups) if (group != null) group.sortingOrder = order;
            if (own != null) own.sortingOrder = order;
        }

        // A destroyed object compares equal to null in Unity, so this spots a cached group that's gone.
        bool HasDestroyed()
        {
            if (groups == null) return true;
            foreach (var group in groups) if (group == null) return true;
            return false;
        }
    }
}
