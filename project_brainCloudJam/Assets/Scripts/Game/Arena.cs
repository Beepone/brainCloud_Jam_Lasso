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
    //      world                                  your screen (y -6.96 to 6.96)           map pixel row
    //      y =  6.957 --------------------------  top of the screen                         0
    //                 rival's hoard (mirror of yours), rival at its front edge (y = 5.391)
    //      y =  5.391 --------------------------                                           36
    //                 rival's spin room
    //      y =  4.348 --------------------------                                           60
    //                 FIELD: treasure wanders here. Mirror line through its middle (y = 0.783)
    //      y = -2.783 --------------------------                                          224
    //                 your spin room
    //      y = -3.826 --------------------------  you stand here, at your hoard's front edge 248
    //                 your hoard
    //      y = -5.391 --------------------------                                          284
    //                 (spare strip; its mirror is just off the top of the screen)
    //      y = -6.957 --------------------------  bottom of the screen                     320
    public class Arena : MonoBehaviour
    {
        // "static" = belongs to the class itself, not to one object. So any script can write
        // Arena.Current to find the arena, without needing a reference to it.
        public static Arena Current { get; private set; }

        // These match the art's map template (docs/art/MapTemplate.md): a 180 x 320 pixel screen at
        // 23 pixels per world unit (the Pixel Perfect Camera's setting), so the screen is 320 / 23 = 13.91
        // units tall and every edge below lands on a whole art pixel.
        [Header("Layout (world units). Rect = x, y of the bottom-left corner, then width, height.")]
        [Tooltip("Where treasure wanders. Its middle (top to bottom) is the mirror line between the two sides.")]
        public Rect field = new Rect(-3.5652f, -2.7826f, 7.1304f, 7.1304f);
        [Tooltip("Your hoard: a strip behind your character. The rival's hoard is its mirror image.")]
        public Rect yourHoard = new Rect(-3.913f, -5.3913f, 7.8261f, 1.5652f);

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

        [Header("Practice: start the rival hoard with treasure (for testing steals)")]
        [Tooltip("Start the rival's hoard with this many coins, to test stealing. 0 for real matches (the bot fills its own hoard).")]
        public int practiceRivalCoins = 0;
        [Tooltip("...and this many chests.")]
        public int practiceRivalChests = 0;

        [Header("Menus")]
        [Tooltip("Show the title screen and menus. Off = jump straight into a match (handy for quick testing).")]
        public bool showMenus = true;

        // An "event" lets other scripts ask to be told when something happens. The menu uses this
        // to save your score to the leaderboard. "System.Action" = a method with no inputs.
        public event System.Action MatchEnded;

        [Header("Sound (filled in from Assets/SFX automatically in the editor)")]
        public AudioClip lassoTwirl;
        public AudioClip lassoThrow;
        public AudioClip lassoGrab;
        public AudioClip gameplayMusic; // loops during a match (GameMusic.cs)

        [Header("Hunter art (filled in from Assets/Sprites automatically in the editor)")]
        [Tooltip("The imported Aseprite files. Back = seen from behind (you), front = facing you (the rival).")]
        public GameObject hunterBlueBack;
        public GameObject hunterBlueFront;
        public GameObject hunterRedBack;
        public GameObject hunterRedFront;

        [Header("Match")]
        [Tooltip("Match length in seconds. The match also ends early if one player owns every treasure.")]
        public float matchLength = 120f;

        // Online, each computer plays a different side of the same world. The host plays the bottom (You),
        // the guest plays the top (Rival) with its camera turned around. "Local" = the player at THIS computer.
        [HideInInspector] public LassoSpin.Side localSide = LassoSpin.Side.You;
        // Online guest: only the host decides an early win (someone owns everything) and the final scores.
        [HideInInspector] public bool decidesEnd = true;

        [Header("Readouts")]
        [Tooltip("Which colour the bottom hunter is this match (picked from the seed, so online both players agree).")]
        public bool bottomIsBlue = true;
        public int matchSeed;
        public float matchTime;
        public float timeLeft;
        public bool isPlaying;     // a match is running right now
        public bool isOver;        // a match just ended (the result panel is showing)
        public string result;      // "YOU WIN!", "RIVAL WINS", "DRAW"
        public string resultReason; // why it ended
        public int finalYourScore, finalRivalScore; // scores frozen at the end (world sides: bottom, top)

        // ----- From the local player's point of view (what the HUD and results show) -----
        public Rect LocalHoard => localSide == LassoSpin.Side.You ? yourHoard : rivalHoard;
        public Rect OpponentHoard => localSide == LassoSpin.Side.You ? rivalHoard : yourHoard;
        public int LocalScore => Score(LocalHoard);
        public int OpponentScore => Score(OpponentHoard);
        public int FinalLocalScore => localSide == LassoSpin.Side.You ? finalYourScore : finalRivalScore;
        public int FinalOpponentScore => localSide == LassoSpin.Side.You ? finalRivalScore : finalYourScore;

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
        void OnValidate()
        {
            rivalHoard = Mirror(yourHoard);
            FindAssets();
        }

        // Editor only: fill empty sound slots from Assets/SFX, so there's nothing to drag in.
        // "#if UNITY_EDITOR" = this code only exists in the editor (AssetDatabase isn't in builds).
        // Save the scene afterwards, so the build keeps them.
        void FindAssets()
        {
#if UNITY_EDITOR
            bool wasMissing = lassoTwirl == null || lassoThrow == null || lassoGrab == null || gameplayMusic == null
                           || hunterBlueBack == null || hunterBlueFront == null || hunterRedBack == null || hunterRedFront == null;
            if (lassoTwirl == null) lassoTwirl = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/LassoTwirl.ogg");
            if (lassoThrow == null) lassoThrow = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/LassoThrow.ogg");
            if (lassoGrab == null) lassoGrab = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/LassoGrab.ogg");
            if (gameplayMusic == null) gameplayMusic = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/GameplayTrack.ogg");
            // An Aseprite file's main asset is a ready-made object (sprite + Animator with one state per tag).
            if (hunterBlueBack == null) hunterBlueBack = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sprites/explorer_blue_16x16_backsprite.aseprite");
            if (hunterBlueFront == null) hunterBlueFront = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sprites/explorer_blue_16x16_frontsprite.aseprite");
            if (hunterRedBack == null) hunterRedBack = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sprites/explorer_red_16x16_backsprite.aseprite");
            if (hunterRedFront == null) hunterRedFront = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sprites/explorer_red_16x16_frontsprite.aseprite");
            // Mark the scene as changed, so Ctrl+S actually saves the clips (filling them in here doesn't on its own).
            if (wasMissing && !Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // Reset runs when you choose Reset from the component's menu (and when the component is first added).
        void Reset() => rivalHoard = Mirror(yourHoard);

        // ----- Mirroring -----
        // The world mirrors top-to-bottom around the middle of the field.
        // A point's mirror image is as far above the line as the point is below it (and vice versa).
        public float MirrorLineY => field.center.y;

        public Vector2 Mirror(Vector2 point) => new Vector2(point.x, 2f * MirrorLineY - point.y);

        // A rectangle's mirror image: same width, height and x; its y flipped across the line.
        public Rect Mirror(Rect r) => new Rect(r.x, 2f * MirrorLineY - r.yMax, r.width, r.height);

        void Start()
        {
            // The score display and the menus: added here so the scene needs no extra setup.
            if (GetComponent<ScoreHud>() == null) gameObject.AddComponent<ScoreHud>();
            if (showMenus && GetComponent<MainMenu>() == null) gameObject.AddComponent<MainMenu>();
            if (GetComponent<GameMusic>() == null) gameObject.AddComponent<GameMusic>();
            // Every lasso gets its sounds and its hunter art.
            foreach (var lasso in FindObjectsByType<LassoSpin>())
            {
                if (lasso.GetComponent<LassoSound>() == null) lasso.gameObject.AddComponent<LassoSound>();
                if (lasso.GetComponent<HunterArt>() == null) lasso.gameObject.AddComponent<HunterArt>();
            }
            DrawZones();

            // With menus, the title screen starts the match (Play). Without, start right away.
            if (!showMenus) PlayAgain();
        }

        // Clears the arena and spawns a fresh set of treasure. Online, both players call this with the same
        // seed, so they get identical treasure. alreadyElapsed: how far into the match we already are (the
        // online guest passes the time the start message took to arrive, so both clocks line up).
        // "float alreadyElapsed = 0f" is an optional parameter: leave it out and it's 0.
        public void StartMatch(int newSeed, float alreadyElapsed = 0f)
        {
            foreach (var t in treasures) Destroy(t.gameObject);
            treasures.Clear();

            // Reset every lasso, in case one was mid-throw holding treasure we just destroyed.
            // FindObjectsByType finds every LassoSpin in the scene (yours, and later the bot's).
            foreach (var lasso in FindObjectsByType<LassoSpin>()) lasso.ResetLasso();
            foreach (var bot in FindObjectsByType<LassoBot>()) bot.ResetBot();

            // Colours: an even seed makes the bottom hunter blue, odd makes it red. Solo matches get a new
            // random seed each time, so your colour changes between matches; online, both share the seed.
            // "&" = bitwise AND: (seed & 1) is the last binary digit, 0 for even numbers and 1 for odd.
            bottomIsBlue = (newSeed & 1) == 0;
            ColorZones();

            matchSeed = newSeed;
            matchStart = Time.realtimeSinceStartupAsDouble - alreadyElapsed;
            isPlaying = true;
            isOver = false;
            result = resultReason = "";
            timeLeft = matchLength;

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

            // Practice: put some extra treasure straight into the rival's hoard, to have something to steal.
            // kinds[0] is the first kind in the list (Coin), kinds[1] the second (Chest).
            if (kinds.Count >= 2)
            {
                SpawnInRivalHoard(kinds[0], practiceRivalCoins, ref id);
                SpawnInRivalHoard(kinds[1], practiceRivalChests, ref id);
            }
        }

        // "ref id" lets this method change the caller's id counter, so every treasure still gets its own number.
        void SpawnInRivalHoard(TreasureKind kind, int count, ref int id)
        {
            for (int i = 0; i < count; i++)
            {
                var t = Treasure.Spawn(id++, kind, rivalHoard.center, this);
                t.StoreIn(rivalHoard, this);
                treasures.Add(t);
            }
        }

        void Update()
        {
            if (!isPlaying) return; // no match running (title screen, or the result panel): everything stays still

            matchTime = MatchTime; // readout
            foreach (var t in treasures) t.UpdateWander(matchTime);

            // ----- End conditions -----
            timeLeft = Mathf.Max(0f, matchLength - matchTime);
            if (timeLeft <= 0f)
            {
                EndMatch("Time's up!");
                return;
            }

            // Someone owns everything: every treasure is in the same hoard (none loose or on a rope).
            // Online, only the host checks this (and tells the guest), so both agree.
            if (!decidesEnd) return;
            if (OwnsEverything(yourHoard)) EndMatch(OwnerText(LassoSpin.Side.You));
            else if (OwnsEverything(rivalHoard)) EndMatch(OwnerText(LassoSpin.Side.Rival));
        }

        // "You took every treasure!" or "The rival took every treasure!", depending on who's looking.
        public string OwnerText(LassoSpin.Side owner) =>
            owner == localSide ? "You took every treasure!" : "The rival took every treasure!";

        bool OwnsEverything(Rect hoardArea)
        {
            if (treasures.Count == 0) return false;
            foreach (var t in treasures)
                if (t.state != Treasure.State.InHoard || t.hoard != hoardArea) return false;
            return true;
        }

        // Ends the match with the scores as they are right now.
        public void EndMatch(string reason) => EndMatch(reason, YourScore, RivalScore);

        // Ends the match with given final scores (world sides). Online, the host sends its scores and the
        // guest ends with those, so both show the same result even if their views differed slightly.
        // localWins: force a win for this player whatever the score (the rival left the match).
        public void EndMatch(string reason, int bottomScore, int topScore, bool localWins = false)
        {
            bool firstTime = !isOver; // the guest may already have ended at time-up; the host's scores then correct it
            isPlaying = false;
            isOver = true;
            resultReason = reason;
            // Freeze the scores. Anything still on a rope doesn't count.
            finalYourScore = bottomScore;
            finalRivalScore = topScore;
            result = localWins || FinalLocalScore > FinalOpponentScore ? "YOU WIN!"
                   : FinalLocalScore < FinalOpponentScore ? "RIVAL WINS"
                   : "DRAW";

            // Tell anyone listening (the menu saves the score). "?." = only if someone is listening.
            if (firstTime) MatchEnded?.Invoke();
        }

        // Back to the menus: clears the field and stops everything.
        public void ClearArena()
        {
            foreach (var t in treasures) Destroy(t.gameObject);
            treasures.Clear();
            foreach (var lasso in FindObjectsByType<LassoSpin>()) lasso.ResetLasso();
            foreach (var bot in FindObjectsByType<LassoBot>()) bot.ResetBot();
            isPlaying = false;
            isOver = false;
        }

        // Starts a fresh match: new seed (unless a fixed seed is set), new treasure, reset lassos.
        public void PlayAgain() => StartMatch(seed != 0 ? seed : Random.Range(1, int.MaxValue));

        // ----- Scores -----
        // A hoard's score = the value of everything sitting in it right now. Worked out fresh every time
        // (instead of keeping a running total), so a steal automatically lowers the hoard it came from.
        public int Score(Rect hoardArea)
        {
            int total = 0;
            foreach (var t in treasures)
                if (t.state == Treasure.State.InHoard && t.hoard == hoardArea) total += t.value;
            return total;
        }

        // World sides: the bottom hoard and the top hoard. (Use LocalScore / OpponentScore for "you" and "them".)
        public int YourScore => Score(yourHoard);
        public int RivalScore => Score(rivalHoard);

        // Draws the two hoard zones as faint rectangles (placeholders until the art arrives).
        SpriteRenderer bottomZone, topZone;
        static readonly Color BlueZoneColor = new Color(0.3f, 0.6f, 1f, 0.18f);
        static readonly Color RedZoneColor = new Color(1f, 0.35f, 0.35f, 0.18f);

        // Is the hunter on this side blue this match? And the player at this computer?
        public bool IsBlue(LassoSpin.Side side) => (side == LassoSpin.Side.You) == bottomIsBlue;
        public bool LocalIsBlue => IsBlue(localSide);

        void DrawZones()
        {
            bottomZone = DrawRect("Bottom Hoard", yourHoard);
            topZone = DrawRect("Top Hoard", rivalHoard);
            ColorZones();
        }

        // Each hoard takes the colour of its hunter.
        void ColorZones()
        {
            if (bottomZone == null || topZone == null) return;
            bottomZone.color = bottomIsBlue ? BlueZoneColor : RedZoneColor;
            topZone.color = bottomIsBlue ? RedZoneColor : BlueZoneColor;
        }

        SpriteRenderer DrawRect(string name, Rect r)
        {
            var sr = PlaceholderArt.CreateSprite(name, PlaceholderArt.Square, Color.white, -10, transform);
            sr.transform.position = r.center;
            sr.transform.localScale = new Vector3(r.width, r.height, 1f);
            return sr;
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
