using System.Collections.Generic; // List<T>
using UnityEngine;

namespace LootLasso
{
    // MainMenu: the screens around the match, and the brainCloud side of things.
    //
    //   NAME ENTRY    first launch only: pick a display name (saved on this device and in brainCloud)
    //   TITLE         Play (online), Practice (vs the bot), Leaderboard, How to Play, and your name
    //   SEARCHING     looking for an online rival (see NetMatch); gives up and plays the bot after a while
    //   LEADERBOARD   the top 10 by total treasure ever (from brainCloud)
    //   HOW TO PLAY   a short card explaining the throw
    //
    // On start it logs in to brainCloud (anonymously: the same player on this device every time). After
    // each match it adds your score to the "total treasure ever" leaderboard.
    //
    // Drawn with OnGUI like the score display. The buttons use the UI art (UiKit).
    // The Arena adds this component automatically (see Arena.showMenus).
    public class MainMenu : MonoBehaviour
    {
        public static MainMenu Current { get; private set; }

        [Tooltip("brainCloud leaderboard ID: a CUMULATIVE leaderboard, so every match's score is added up.")]
        public string leaderboardId = "TotalTreasure";
        [Tooltip("Text size, as a fraction of the screen height.")]
        public float textSize = 0.032f;

        [Header("Readouts")]
        public string screen;
        public string playerName;
        public string onlineStatus = "Connecting...";
        public string scoreStatus;   // what happened to the last match's score (shown on the result panel)

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

        void Awake() => Current = this;

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

            scoreStatus = "Saving score...";
            BrainCloudManager.Instance.PostScore(leaderboardId, score,
                () => scoreStatus = $"+{score} treasure banked",
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

        // PLAY: look for an online rival (NetMatch). The match starts by itself when one is found.
        public void Play()
        {
            practice = false;
            scoreStatus = "";
            net.Leave();        // leave any previous online match, back to the normal setup
            arena.ClearArena();
            current = Screen.Searching;
            net.FindMatch();
        }

        // PRACTICE: straight into a match against the bot.
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
            net.SetUpSolo();
            arena.PlayAgain();
        }

        // Matchmaking gave up (no rival in time, or offline): play the bot, and say why.
        void OnSearchFailed(string why)
        {
            if (current != Screen.Searching) return; // already cancelled
            current = Screen.Title;                  // (shown again after the match)
            StartBotMatch();
            ScoreHud.ShowPopup(why, new Vector2(0f, arena.MirrorLineY), Color.white);
        }

        // ===== Drawing =====

        void OnGUI()
        {
            screen = current.ToString(); // readout
            if (arena == null || arena.isPlaying || arena.isOver) return; // the match and result panel draw themselves

            int size = Mathf.RoundToInt(UnityEngine.Screen.height * textSize);
            // Dim the arena behind the menu.
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
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
            else Label("LOOT LASSO", 0.16f, size * 1.9f, new Color(1f, 0.85f, 0.2f), bold: true, title: true);

            Label($"Hi, {playerName}", 0.30f, size, Color.white);
            if (SmallButton("EDIT NAME", 0.347f, size)) { nameInput = playerName; current = Screen.NameEntry; }

            if (Button("PLAY ONLINE", 0.45f, size)) Play();
            if (Button("PRACTICE", 0.545f, size)) Practice();
            if (Button("RANKINGS", 0.64f, size)) OpenLeaderboard(); // (LEADERBOARD is too wide for the button in the pixel font)
            if (Button("HOW TO PLAY", 0.735f, size)) current = Screen.HowToPlay;

            Label(onlineStatus, 0.9f, size * 0.7f, loggedIn ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.7f, 0.5f));
        }

        void DrawSearching(int size)
        {
            Label(net.status, 0.4f, size * 1.2f, Color.white, bold: true, title: true);
            // CeilToInt: counts 15, 14, ... 1 (never shows 0 while still looking).
            Label($"Playing the bot in {Mathf.CeilToInt(net.SearchTimeLeft)}...", 0.47f, size * 0.8f, new Color(0.8f, 0.8f, 0.8f));
            if (Button("CANCEL", 0.6f, size))
            {
                net.Cancel();
                current = Screen.Title;
            }
        }

        void DrawNameEntry(int size)
        {
            Label("What's your name?", 0.3f, size * 1.3f, Color.white, bold: true, title: true);
            Label("It's shown in the rankings.", 0.36f, size * 0.8f, new Color(0.8f, 0.8f, 0.8f));

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
            Label("TOTAL TREASURE", 0.1f, size * 1.6f, new Color(1f, 0.85f, 0.2f), bold: true, title: true);

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
                    Label($"{e.rank}.  {who}   {e.score}", y, size, isMe && UiKit.Row == null ? new Color(0.55f, 0.75f, 1f) : Color.white, bold: isMe);
                }
            }
            if (!string.IsNullOrEmpty(leaderboardStatus)) Label(leaderboardStatus, 0.45f, size, Color.white);

            if (Button("BACK", 0.82f, size)) current = Screen.Title;
        }

        void DrawHowToPlay(int size)
        {
            Label("HOW TO PLAY", 0.1f, size * 1.6f, new Color(1f, 0.85f, 0.2f), bold: true, title: true);
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
                "STEAL. Most treasure after",
                "2:00 wins!",
            };
            for (int i = 0; i < lines.Length; i++) Label(lines[i], 0.2f + i * 0.038f, size * 0.85f, Color.white);

            if (Button("BACK", 0.85f, size)) current = Screen.Title;
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

        // ...and the small 64 x 14 one.
        static bool SmallButton(string text, float y, int size)
        {
            Rect rect = UiKit.Centered(UiKit.Small, y, 0.35f, size * 1.4f);
            return UiKit.Button(rect, text, UiKit.Small, UiKit.FitFont(text, rect.width, Mathf.RoundToInt(size * 0.7f)));
        }
    }
}
