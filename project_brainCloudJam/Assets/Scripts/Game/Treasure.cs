using UnityEngine;

namespace LootLasso
{
    // One piece of living treasure. While free, it wanders around the field on its own:
    // runs a short way, sometimes pauses, runs again.
    //
    // The wandering is DETERMINISTIC: its position is calculated from the match seed, its id and the
    // match clock, not simulated frame by frame. Two players' games with the same seed and clock
    // put every treasure in exactly the same place, so treasure movement never has to be sent over
    // the network. (That's why it uses our own Prng instead of Unity's Random.)
    public class Treasure : MonoBehaviour
    {
        // What the treasure is doing.
        //   Free     wandering the field
        //   Hooked   caught in a lasso, being pulled (step 2)
        //   InHoard  sitting in a hoard, counting as score (step 3)
        public enum State { Free, Hooked, InHoard }

        [Header("Readouts")]
        public int id;
        public string kindName;
        public int value;
        public float weight;
        public float speed;
        public State state = State.Free;
        [Tooltip("Which hoard it's in, while InHoard. (A Rect is just the area; the Arena knows whose it is.)")]
        public Rect hoard;
        public int timesStored; // bumped each time it's put in a hoard, to vary its spot there
        [Tooltip("Match time until which it can't be caught (just landed in a hoard, or just knocked loose).")]
        public float protectedUntil;

        // Can't be caught right now? (Uses the shared match clock, so both online players agree.)
        public bool IsProtected(float matchTime) => matchTime < protectedUntil;

        // ----- Being caught, dropped and stored -----

        // Which lasso has it on its rope right now (null if none). [System.NonSerialized] = not saved or
        // shown in the Inspector; it only matters while the game runs.
        [System.NonSerialized] public LassoSpin hookedBy;

        // Caught by a lasso: stop wandering. The lasso moves it from now on.
        public void Hook(LassoSpin by)
        {
            state = State.Hooked;
            hookedBy = by;
        }

        // Dropped (the rope snapped): start wandering again from wherever it fell.
        public void Drop(Arena arena)
        {
            hookedBy = null;
            StartWander(arena.matchSeed, arena.field, arena.MatchTime);
        }

        // Delivered to a hoard. The hoard has a grid of spots about a loop's width apart (two staggered rows),
        // and it takes the free spot FARTHEST from the treasure already there, so the hoard fills evenly
        // and one throw can't scoop up most of it. Everything here comes from the hoard's contents and the
        // shared seed, so online both players put it in the same place.
        public void StoreIn(Rect hoardArea, Arena arena)
        {
            hookedBy = null;
            state = State.InHoard;
            hoard = hoardArea;
            protectedUntil = arena.MatchTime + arena.protectTime; // can't be stolen straight away
            var rng = new Prng(arena.matchSeed * 31 + id * 977 + timesStored * 7919);
            timesStored++;

            const float margin = 0.35f; // keep spots a little away from the hoard's edges
            float spacing = Mathf.Max(arena.hoardSpacing, 0.1f);
            float usableWidth = hoardArea.width - margin * 2f;
            int cols = Mathf.Max(1, Mathf.FloorToInt(usableWidth / spacing) + 1);
            int rows = hoardArea.height - margin * 2f > spacing * 0.4f ? 2 : 1;

            Vector2 best = hoardArea.center;
            float bestRoom = -1f;
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    // The second row is shifted half a spot sideways, like bricks, so the rows don't line up.
                    float shift = rows == 2 && row == 1 ? 0.5f : 0f;
                    float fx = cols == 1 ? 0.5f : (col + shift * (col < cols - 1 ? 1f : 0f)) / (cols - 1);
                    float fy = rows == 1 ? 0.5f : row;
                    var spot = new Vector2(
                        hoardArea.xMin + margin + usableWidth * fx,
                        Mathf.Lerp(hoardArea.yMin + margin, hoardArea.yMax - margin, fy));

                    // How much room this spot has: the distance to the nearest treasure already in this hoard.
                    float room = float.MaxValue;
                    foreach (var other in arena.treasures)
                    {
                        if (other == this || other.state != State.InHoard || other.hoard != hoardArea) continue;
                        room = Mathf.Min(room, Vector2.Distance(spot, other.transform.position));
                    }
                    room += rng.Range(0f, 0.01f); // breaks ties between equally empty spots, from the shared seed
                    if (room > bestRoom) { bestRoom = room; best = spot; }
                }

            // A little wobble so it doesn't look like a perfect grid.
            best += new Vector2(rng.Range(-0.12f, 0.12f), rng.Range(-0.08f, 0.08f));
            transform.position = best;
        }

        // Knocked loose (a steal's rope snapped at the centre line): hops a short way in a direction picked
        // from the shared seed, then wanders again. It's protected (and blinks) for a moment, so nobody can
        // catch it the instant it lands.
        public void KnockLoose(Arena arena)
        {
            hookedBy = null;
            float now = arena.MatchTime;
            StartWander(arena.matchSeed, arena.field, now);

            // The hop: the first wander segment, a quick dash.
            var hopRng = new Prng(arena.matchSeed * 13 + id * 7727 + restarts * 31);
            float angle = hopRng.Range(0f, Mathf.PI * 2f);
            Vector2 target = from + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * arena.knockHopDistance;
            target.x = Mathf.Clamp(target.x, bounds.xMin, bounds.xMax);
            target.y = Mathf.Clamp(target.y, bounds.yMin, bounds.yMax);
            to = target;
            segEnd = segStart + Mathf.Max(arena.knockHopTime, 0.05f);

            protectedUntil = now + arena.protectTime;
        }

        // ----- Blinking while protected -----
        // The parts that blink: the art's sprites (not its shadow, and not the hidden placeholder).
        SpriteRenderer[] blinkParts;
        bool blinking;

        void LateUpdate()
        {
            var arena = Arena.Current;
            bool protect = arena != null && arena.isPlaying && IsProtected(arena.MatchTime);
            if (!protect && !blinking) return;

            if (blinkParts == null)
            {
                var parts = new System.Collections.Generic.List<SpriteRenderer>();
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
                    if (sr.enabled && sr.gameObject.name != "Shadow") parts.Add(sr);
                blinkParts = parts.ToArray();
            }
            // On and off ten times a second while protected; always on afterwards.
            bool visible = !protect || Mathf.Repeat(Time.time * 10f, 1f) < 0.5f;
            foreach (var sr in blinkParts) if (sr != null) sr.enabled = visible;
            blinking = protect;
        }

        // ----- Wandering -----
        // The current "segment": move from 'from' to 'to', between match times segStart and segEnd.
        Prng rng;
        Rect bounds;
        Vector2 from, to;
        float segStart, segEnd;
        int restarts; // bumped each time the wander restarts (e.g. after a rope snaps), to vary the seed

        // Creates a treasure GameObject from a kind (coin, chest, idol...). "static" = called on the class,
        // like Treasure.Spawn(...), because there's no treasure object yet to call it on.
        public static Treasure Spawn(int id, Arena.TreasureKind kind, Vector2 position, Arena arena)
        {
            var go = new GameObject($"{kind.name} {id}");
            go.transform.SetParent(arena.transform, false);
            go.transform.position = position;
            // Real art is drawn at its true pixel size (imported at 23 pixels per unit, the map's scale),
            // so it's used as-is. The placeholder circle is 1 unit wide, so it's scaled to the kind's size.
            bool hasArt = kind.animatedArt != null || kind.sprite != null;
            go.transform.localScale = hasArt ? Vector3.one : Vector3.one * kind.size;

            // Animated art: a copy of the imported file as a child, with TreasureArt picking the animation.
            if (kind.animatedArt != null)
            {
                var art = Instantiate(kind.animatedArt, go.transform);
                art.transform.localPosition = Vector3.zero;
                go.AddComponent<TreasureArt>();
            }

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = kind.sprite != null ? kind.sprite : PlaceholderArt.Circle; // the art, or a placeholder
            sr.enabled = kind.animatedArt == null; // the animated art draws itself
            sr.color = kind.sprite != null ? Color.white : kind.color;               // tint only the placeholder
            go.AddComponent<YSort>(); // drawn in front of or behind hunters and other treasure by screen height

            var t = go.AddComponent<Treasure>();
            t.id = id;
            t.kindName = kind.name;
            t.value = kind.value;
            t.weight = kind.weight;
            t.speed = kind.speed;
            t.StartWander(arena.matchSeed, arena.field, 0f);
            return t;
        }

        // Start (or restart) wandering from where it is now. Same inputs = same path on every computer.
        public void StartWander(int matchSeed, Rect field, float matchTime)
        {
            // Mix the match seed, this treasure's id and the restart count into one seed with big
            // prime numbers, so every treasure (and every restart) gets its own path.
            rng = new Prng(matchSeed * 7919 + id * 104729 + restarts * 15485863);
            restarts++;
            bounds = field;
            from = to = transform.position;
            segStart = segEnd = matchTime;
            state = State.Free;
        }

        // Move to where this treasure should be at the given match time. The Arena calls this every frame.
        public void UpdateWander(float matchTime)
        {
            if (state != State.Free || rng == null) return;

            // Catch up through any segments that already finished. "while", not "if": after a long
            // hitch (or a backgrounded browser tab), several segments may have passed at once.
            while (matchTime >= segEnd) NextSegment();

            // How far through the current segment we are (0..1), and the point that far along.
            float t = Mathf.InverseLerp(segStart, segEnd, matchTime);
            transform.position = Vector2.Lerp(from, to, t);
        }

        void NextSegment()
        {
            from = to;
            segStart = segEnd;

            // 1 in 4: pause for a moment, so they feel alive rather than robotic.
            if (rng.Range(0f, 1f) < 0.25f)
            {
                segEnd = segStart + rng.Range(0.3f, 1.2f);
                return;
            }

            // Otherwise run a short distance in a random direction, staying inside the field.
            float angle = rng.Range(0f, Mathf.PI * 2f); // radians: 2 PI = a full circle
            float distance = rng.Range(1f, 3f);
            Vector2 target = from + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            target.x = Mathf.Clamp(target.x, bounds.xMin, bounds.xMax);
            target.y = Mathf.Clamp(target.y, bounds.yMin, bounds.yMax);

            to = target;
            // Time = distance / speed. Max(..., 0.05) avoids a zero-length segment (e.g. stuck in a corner).
            segEnd = segStart + Mathf.Max(Vector2.Distance(from, to) / speed, 0.05f);
        }
    }

    // A tiny random number generator ("xorshift"). Unlike Unity's Random it has no shared global state,
    // and it gives exactly the same numbers from the same seed on every computer and browser. That's
    // what lets two players' games agree on where treasure goes without sending it over the network.
    public class Prng
    {
        uint state; // "uint" = a whole number that's never negative (0 to about 4 billion)

        public Prng(int seed)
        {
            state = (uint)seed;
            if (state == 0) state = 0x9E3779B9; // xorshift gets stuck at 0, so never start there
            for (int i = 0; i < 4; i++) Next(); // stir the seed a bit, so close seeds give different numbers
        }

        // The next number. ^ = XOR, << and >> = shifting the bits left and right. Together they
        // scramble the number in a way that looks random but is fully repeatable.
        public uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        // A number between min and max: Next() divided by the biggest possible uint gives 0..1.
        public float Range(float min, float max) => min + (Next() / (float)uint.MaxValue) * (max - min);
    }
}
