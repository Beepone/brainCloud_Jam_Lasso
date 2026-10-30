using System.Collections.Generic; // List<T>
using UnityEngine;
using UnityEngine.InputSystem; // Keyboard (Esc opens OPTIONS)

namespace LootLasso
{
    // MainMenu: the screens around the match, and the brainCloud side of things.
    //
    //   NAME ENTRY    first launch only: pick a display name (saved on this device and in brainCloud)
    //   TITLE         Online (greyed out while offline), Offline (vs the bot), Rankings, How to Play, Options, your name
    //   SEARCHING     looking for an online rival (see NetMatch); gives up and plays the bot after a while
    //   LEADERBOARD   the top 10 best single-match scores (from brainCloud)
    //   HOW TO PLAY   a short card explaining the throw
    //   OPTIONS       music and sound volumes (SoundOptions). From the title, or Esc at any time, even
    //                 mid-match (drawn over it; the match keeps going, since an online rival can't be paused).
    //                 During a match there's also a small gear button in the bottom-right corner (phones have no Esc).
    //                 Against the bot, OPTIONS can also ESCAPE the match (greyed out online: leaving would forfeit).
    //
    // On start it logs in to brainCloud (anonymously: the same player on this device every time). After
    // each match it posts your score to the high score leaderboard, which keeps your best match.
    // (Not from the Unity editor, unless postScoresFromEditor is on, so test matches stay off it.)
    //
    // Drawn with OnGUI like the score display. The buttons use the UI art (UiKit).
    // The Arena adds this component automatically (see Arena.showMenus).
    public class MainMenu : MonoBehaviour
    {
        public static MainMenu Current { get; private set; }

        [Tooltip("brainCloud leaderboard ID: a HIGH_VALUE leaderboard (set up in the portal), so it keeps each player's best match.")]
        public string leaderboardId = "Highscores";
        [Tooltip("Post scores from matches played in the Unity editor too. Off, so test matches don't fill the real rankings.")]
        public bool postScoresFromEditor = false;
        [Tooltip("Text size, as a fraction of the screen height.")]
        public float textSize = 0.032f;

        [Header("Readouts")]
        public string screen;
        public string playerName;
        public string onlineStatus = "Connecting...";
        public string scoreStatus;   // what happened to the last match's score (shown on the result panel)
        public string botName;       // the bot's name this match (shown on the HUD), picked from BotNames

        // The bot plays as a famous movie cowboy, a new one each match. 8 letters at most, to fit the HUD.
        static readonly string[] BotNames =
        {
            "DJANGO", "WAYNE", "EASTWOOD", "BLONDIE", "TUCO", "SHANE",
            "COGBURN", "SUNDANCE", "HOLLIDAY", "EARP", "JOSEY", "DUKE",
        };

        enum Screen { NameEntry, Title, Searching, Leaderboard, HowToPlay }
        Screen current = Screen.Title;

        Arena arena;
        NetMatch net;
        bool practice; // was the last match vs the bot by choice? (PLAY AGAIN repeats the same kind)
        bool loggedIn;
        string nameInput = "";

        // Leaderboard state
        List<LeaderboardEntry> topScores;
        string leaderboardStatus = "";

        // The name is saved on this device with PlayerPrefs: a small key/value store Unity keeps for you
        // (in the browser it lives in the site's storage).
        const string NameKey = "LootLasso.PlayerName";
        const int MaxNameLength = 14;

        // Is the OPTIONS screen up? (Static so the lasso can check it. Reset on start: domain reload is off.)
        public static bool OptionsOpen { get; private set; }

        void Awake() { Current = this; OptionsOpen = false; }

        void Update()
        {
            // Esc opens or closes OPTIONS (not while typing a name, where it would lose the text box).
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && current != Screen.NameEntry)
                OptionsOpen = !OptionsOpen;
        }

        void Start()
        {
            arena = GetComponent<Arena>();
            arena.MatchEnded += OnMatchEnded; // "+=" subscribes: call OnMatchEnded whenever a match ends

            // Online matchmaking lives on the same object. When it gives up, play the bot instead.
            net = GetComponent<NetMatch>();
            if (net == null) net = gameObject.AddComponent<NetMatch>();
            net.SearchFailed += OnSearchFailed;

            playerName = PlayerPrefs.GetString(NameKey, "");
            current = string.IsNullOrEmpty(playerName) ? Screen.NameEntry : Screen.Title;

            LogIn();
        }

        void OnDestroy()
        {
            if (arena != null) arena.MatchEnded -= OnMatchEnded; // "-=" unsubscribes (tidy up)
            if (net != null) net.SearchFailed -= OnSearchFailed;
        }

        // ===== brainCloud =====

        void LogIn()
        {
            var bc = BrainCloudManager.Instance;
            if (bc == null) { onlineStatus = "Offline: scores won't be saved"; return; }

            onlineStatus = "Connecting...";
            // "() => { ... }" is a lambda: a small method written inline. brainCloud calls it when login succeeds.
            bc.Login(
                () =>
                {
                    loggedIn = true;
                    onlineStatus = "Online";
                    // Make sure brainCloud has the name you picked (e.g. chosen while still connecting).
                    if (!string.IsNullOrEmpty(playerName) && bc.PlayerName != playerName) bc.SetPlayerName(playerName);
                },
                error => onlineStatus = "Offline: scores won't be saved");
        }

        void OnMatchEnded()
        {
            int score = arena.FinalLocalScore; // yours, whichever side you played
            if (!loggedIn) { scoreStatus = "Offline: not saved"; return; }
            if (score <= 0) { scoreStatus = ""; return; }
            if (Application.isEditor && !postScoresFromEditor) { scoreStatus = "Editor: score not saved"; return; }

            scoreStatus = "Saving score...";
            BrainCloudManager.Instance.PostScore(leaderboardId, score,
                () => scoreStatus = $"Score {score} saved",
                error => scoreStatus = "Couldn't save score");
        }

        void OpenLeaderboard()
        {
            current = Screen.Leaderboard;
            topScores = null;
            if (!loggedIn) { leaderboardStatus = "Offline: can't load rankings"; return; }

            leaderboardStatus = "Loading...";
            BrainCloudManager.Instance.GetTopScores(leaderboardId, 10,
                entries => { topScores = entries; leaderboardStatus = entries.Count == 0 ? "No scores yet. Be the first!" : ""; },
                error => leaderboardStatus = "Couldn't load rankings");
        }

        // ===== Navigation (also used by the result panel) =====

        // ONLINE: look for an online rival (NetMatch). The match starts by itself when one is found.
        public void Play()
        {
            practice = false;
            scoreStatus = "";
            net.Leave();        // leave any previous online match, back to the normal setup
            arena.ClearArena();
            current = Screen.Searching;
            net.FindMatch();
        }

        // OFFLINE: straight into a match against the bot.
        public void Practice()
        {
            practice = true;
            StartBotMatch();
        }

        // The result panel's PLAY AGAIN: the same kind of match as last time.
        public void PlayAgain()
        {
            if (practice) Practice();
            else Play();
        }

        public void BackToTitle()
        {
            net.Leave();
            arena.ClearArena();
            current = Screen.Title;
        }

        void StartBotMatch()
        {
            scoreStatus = "";
            botName = BotNames[Random.Range(0, BotNames.Length)];
            net.SetUpSolo();
            arena.PlayAgain();
        }

        // Matchmaking gave up (no rival in time, or offline): play the bot, and say why.
        void OnSearchFailed(string why)
        {
            if (current != Screen.Searching) return; // already cancelled
            current = Screen.Title;                  // (shown again after the match)
            StartBotMatch();
            ScoreHud.ShowPopup(why, new Vector2(0f, arena.MirrorLineY), Palette.Bone);
        }

        // ===== Drawing =====

        void OnGUI()
        {
            screen = OptionsOpen ? "Options" : current.ToString(); // readout
            GUI.depth = 0; // (DrawOptions raises it)
            if (OptionsOpen && arena != null) { DrawOptions(); return; }
            if (arena != null && arena.isPlaying && UiKit.IconButton(CornerButtonRect(), GearIcon, Palette.Bone)) OptionsOpen = true;
            if (arena == null || arena.isPlaying || arena.isOver) return; // the match and result panel draw themselves

            int size = Mathf.RoundToInt(UnityEngine.Screen.height * textSize);
            // Dim the arena behind the menu.
            GUI.color = Palette.WithAlpha(Palette.Night, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            switch (current)
            {
                case Screen.NameEntry: DrawNameEntry(size); break;
                case Screen.Title: DrawTitle(size); break;
                case Screen.Searching: DrawSearching(size); break;
                case Screen.Leaderboard: DrawLeaderboard(size); break;
                case Screen.HowToPlay: DrawHowToPlay(size); break;
            }
        }

        // (UnityEngine.Screen is Unity's screen size; we write it out in full here because this class
        // also has its own "Screen" list of menu screens.)
        static float W => UnityEngine.Screen.width;
        static float H => UnityEngine.Screen.height;

        void DrawTitle(int size)
        {
            // The logo (140 x 64 art pixels), centred at row 52 of the 320-row screen, just below the notch zone.
            if (arena.titleLogo != null) DrawArtSprite(arena.titleLogo, 52f / 320f);
            else Label("LOOT LASSO", 0.16f, size * 1.9f, Palette.Gold, bold: true, title: true);

            Label($"Hi, {playerName}", 0.30f, size, Palette.Bone);
            if (SmallButton("EDIT NAME", 0.347f, size)) { nameInput = playerName; current = Screen.NameEntry; }

            // ONLINE needs brainCloud: greyed out while offline. OFFLINE plays the bot.
            GUI.enabled = loggedIn;
            if (Button("ONLINE", 0.45f, size)) Play();
            GUI.enabled = true;
            if (Button("OFFLINE", 0.545f, size)) Practice();
            if (Button("RANKINGS", 0.64f, size)) OpenLeaderboard(); // (LEADERBOARD is too wide for the button in the pixel font)
            if (Button("HOW TO PLAY", 0.735f, size)) current = Screen.HowToPlay;
            if (Button("OPTIONS", 0.83f, size)) OptionsOpen = true;

            Label(onlineStatus, 0.93f, size * 0.7f, loggedIn ? Palette.Mint : Palette.Orange);
        }

        void DrawSearching(int size)
        {
            Label(net.status, 0.4f, size * 1.2f, Palette.Bone, bold: true, title: true);
            // CeilToInt: counts 15, 14, ... 1 (never shows 0 while still looking).
            // Once a rival is found the countdown stops mattering, so say what's happening instead.
            string sub = net.phase == NetMatch.Phase.Starting ? "Getting ready..." : $"Playing the bot in {Mathf.CeilToInt(net.SearchTimeLeft)}...";
            Label(sub, 0.47f, size * 0.8f, Palette.Ash);
            if (Button("CANCEL", 0.6f, size))
            {
                net.Cancel();
                current = Screen.Title;
            }
        }

        void DrawNameEntry(int size)
        {
            Label("What's your name?", 0.3f, size * 1.3f, Palette.Bone, bold: true, title: true);
            Label("It's shown in the rankings.", 0.36f, size * 0.8f, Palette.Ash);

            // A text box: draws it and returns what's typed in it (we feed that back in every frame).
            // On the Field art if there is one, otherwise Unity's plain box.
            if (UiKit.Field != null)
            {
                nameInput = UiKit.TextField(UiKit.Centered(UiKit.Field, 0.44f), nameInput, MaxNameLength, size);
            }
            else
            {
                var fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = size, alignment = TextAnchor.MiddleCenter };
                nameInput = GUI.TextField(Centered(0.44f, 0.7f, size * 2.2f), nameInput, MaxNameLength, fieldStyle);
            }

            string trimmed = nameInput.Trim(); // Trim removes spaces at the start and end
            GUI.enabled = trimmed.Length > 0;  // grey out the button until there's a name
            if (Button("OK", 0.56f, size))
            {
                playerName = trimmed;
                PlayerPrefs.SetString(NameKey, playerName);
                PlayerPrefs.Save();
                if (loggedIn) BrainCloudManager.Instance.SetPlayerName(playerName);
                current = Screen.Title;
            }
            GUI.enabled = true;
        }

        void DrawLeaderboard(int size)
        {
            Label("HIGH SCORES", 0.1f, size * 1.6f, Palette.Gold, bold: true, title: true);

            if (topScores != null && topScores.Count > 0)
            {
                string me = BrainCloudManager.Instance != null ? BrainCloudManager.Instance.ProfileId : "";
                for (int i = 0; i < topScores.Count; i++)
                {
                    var e = topScores[i];
                    bool isMe = e.playerId == me;
                    string who = string.IsNullOrEmpty(e.name) ? "(no name)" : e.name;
                    float y = 0.2f + i * 0.055f;
                    // Each line sits on a Row plank. Yours is full colour, the others a little darker, so yours stands out.
                    if (UiKit.Row != null)
                        UiKit.DrawSprite(UiKit.Centered(UiKit.Row, y), UiKit.Row, isMe ? Color.white : new Color(0.7f, 0.7f, 0.7f));
                    Label($"{e.rank}.  {who}   {e.score}", y, size, isMe && UiKit.Row == null ? Palette.Teal : Palette.Bone, bold: isMe);
                }
            }
            if (!string.IsNullOrEmpty(leaderboardStatus)) Label(leaderboardStatus, 0.45f, size, Palette.Bone);

            if (Button("BACK", 0.82f, size)) current = Screen.Title;
        }

        void DrawHowToPlay(int size)
        {
            Label("HOW TO PLAY", 0.1f, size * 1.6f, Palette.Gold, bold: true, title: true);
            string[] lines =
            {
                // Short lines: the small pixel font fits about 28 characters across.
                "Hold and circle your hunter",
                "to spin up the lasso.",
                "",
                "Let go when it swings toward",
                "your target. Spin faster",
                "to throw farther.",
                "",
                "Spin too wildly and it",
                "backlashes: keep a rhythm.",
                "",
                "Drag treasure to your hoard.",
                "Heavy treasure hauls slowly.",
                "",
                "Lasso the rival's hoard to",
                "knock their treasure loose.",
                "Most treasure at 2:00 wins!",
            };
            for (int i = 0; i < lines.Length; i++) Label(lines[i], 0.2f + i * 0.038f, size * 0.85f, Palette.Bone);

            if (Button("BACK", 0.85f, size)) current = Screen.Title;
        }

        // OPTIONS: each volume with a - and a + button under it, 10 quieter or louder per tap.
        // Drawn over everything, including a match or the result panel underneath.
        void DrawOptions()
        {
            GUI.depth = -10; // on top of the HUD and result panel (lower depth = drawn later, gets clicks first)
            int size = Mathf.RoundToInt(H * textSize);
            GUI.color = Palette.WithAlpha(Palette.Night, 0.75f);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;

            Label("OPTIONS", 0.22f, size * 1.6f, Palette.Gold, bold: true, title: true);

            Label($"MUSIC {SoundOptions.Describe(SoundOptions.Music)}", 0.33f, size, Palette.Bone, title: true);
            int music = VolumeButtons(SoundOptions.Music, 0.39f, size);
            if (music != 0) SoundOptions.ChangeMusic(music);

            Label($"SOUND {SoundOptions.Describe(SoundOptions.Sfx)}", 0.49f, size, Palette.Bone, title: true);
            int sfx = VolumeButtons(SoundOptions.Sfx, 0.55f, size);
            if (sfx != 0) SoundOptions.ChangeSfx(sfx);

            // During a match, ESCAPE (with BACK moved down under it) leaves for the title, saving no score.
            // Only against the bot: online it's shown greyed out, since leaving would forfeit the rival's match.
            bool inMatch = arena.isPlaying;
            if (inMatch)
            {
                GUI.enabled = !net.IsOnline;
                if (Button("ESCAPE", 0.7f, size))
                {
                    OptionsOpen = false;
                    BackToTitle();
                }
                GUI.enabled = true;
            }
            if (Button("BACK", inMatch ? 0.795f : 0.7f, size)) OptionsOpen = false;

            // Swallow any other tap, so it doesn't reach the result panel's buttons underneath.
            var e = Event.current;
            if (e.type == EventType.MouseDown || e.type == EventType.MouseUp) e.Use();
        }

        // The corner button: 15 x 14 art pixels, 3 in from the bottom-right corner of the screen.
        public static Rect CornerButtonRect()
        {
            float s = UiKit.Scale;
            float w = 15f * s, h = 14f * s, margin = 3f * s;
            return new Rect(W - w - margin, H - h - margin, w, h);
        }

        // Should the lasso ignore a press here? Yes while OPTIONS is up, or on the corner button during a match.
        // (screenPos as the Input System gives it: screen pixels counted from the BOTTOM; OnGUI counts from the top.)
        public static bool BlocksPointer(Vector2 screenPos)
        {
            if (OptionsOpen) return true;
            var arena = Arena.Current;
            return arena != null && arena.isPlaying && CornerButtonRect().Contains(new Vector2(screenPos.x, H - screenPos.y));
        }

        // The gear picture on the corner button, made from these rows the first time it's needed
        // ('#' = a white pixel, tinted when drawn). 7 x 7 art pixels.
        static readonly string[] GearRows =
        {
            "..#.#..",
            ".#####.",
            "##...##",
            ".#...#.",
            "##...##",
            ".#####.",
            "..#.#..",
        };
        static Texture2D gearIcon;
        static Texture2D GearIcon
        {
            get
            {
                if (gearIcon != null) return gearIcon;
                int size = GearRows.Length;
                gearIcon = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++) // textures count rows from the bottom
                        gearIcon.SetPixel(x, size - 1 - y, GearRows[y][x] == '#' ? Color.white : Color.clear);
                gearIcon.Apply();
                return gearIcon;
            }
        }

        // A - and a + button side by side at y (greyed out at OFF and at 100). Returns -1, +1, or 0 for no tap.
        static int VolumeButtons(float level, float y, int size)
        {
            int step = 0;
            GUI.enabled = level > 0f;
            if (SmallButtonAt("-", -1, y, size)) step = -1;
            GUI.enabled = level < 1f;
            if (SmallButtonAt("+", +1, y, size)) step = +1;
            GUI.enabled = true;
            return step;
        }

        // ----- Small drawing helpers (y and widths are fractions of the screen) -----

        // Draws a sprite at the game's pixel scale (one art pixel = the same size as in the 180 x 320 world),
        // centred left-to-right with its middle at y (a fraction of the screen height).
        static void DrawArtSprite(Sprite sprite, float y)
        {
            // How many screen pixels one art pixel covers: the screen fits 320 rows tall or 180 columns wide,
            // whichever is tighter (Min).
            float scale = Mathf.Min(H / 320f, W / 180f);
            Rect pixels = sprite.textureRect; // where the sprite sits in its texture, in texture pixels
            float w = pixels.width * scale, h = pixels.height * scale;
            var screenRect = new Rect((W - w) / 2f, H * y - h / 2f, w, h);

            // DrawTextureWithTexCoords draws just part of a texture. Its coordinates are fractions (0..1) of
            // the texture's width and height.
            Texture2D tex = sprite.texture;
            var part = new Rect(pixels.x / tex.width, pixels.y / tex.height, pixels.width / tex.width, pixels.height / tex.height);
            GUI.DrawTextureWithTexCoords(screenRect, tex, part);
        }

        static Rect Centered(float y, float width, float height) =>
            new Rect((W - W * width) / 2f, H * y - height / 2f, W * width, height);

        // A line of text centred across the screen with its middle at y. In the pixel font if there is one
        // that fits (title = the large font), otherwise Unity's font at "size".
        static void Label(string text, float y, float size, Color color, bool bold = false, bool title = false)
        {
            float scale = UiKit.Scale;
            var font = PixelFont.Pick(text, W - 8f * scale, scale, large: title);
            if (font != null) { font.Draw(text, W / 2f, H * y, TextAnchor.MiddleCenter, scale, color); return; }

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(size),
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false, // one line: never break a title onto a second line
            };
            style.normal.textColor = color;
            GUI.Label(Centered(y, 1f, size * 1.6f), text, style);
        }

        // Buttons drawn with the UI art (see UiKit): the big 108 x 24 one, finger-sized...
        static bool Button(string text, float y, int size)
        {
            Rect rect = UiKit.Centered(UiKit.Big, y, 0.6f, size * 2.4f);
            return UiKit.Button(rect, text, UiKit.Big, UiKit.FitFont(text, rect.width, size));
        }

        // A small button beside the middle: side -1 = left of it, +1 = right, with a few art pixels between.
        static bool SmallButtonAt(string text, int side, float y, int size)
        {
            Rect rect = UiKit.Centered(UiKit.Small, y, 0.35f, size * 1.4f);
            rect.x += side * (rect.width / 2f + 3f * UiKit.Scale);
            return UiKit.Button(rect, text, UiKit.Small, UiKit.FitFont(text, rect.width, Mathf.RoundToInt(size * 0.7f)));
        }

        // ...and the small 64 x 14 one.
        static bool SmallButton(string text, float y, int size)
        {
            Rect rect = UiKit.Centered(UiKit.Small, y, 0.35f, size * 1.4f);
            return UiKit.Button(rect, text, UiKit.Small, UiKit.FitFont(text, rect.width, Mathf.RoundToInt(size * 0.7f)));
        }
    }
}
