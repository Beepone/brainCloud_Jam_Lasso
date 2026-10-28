using System;
using System.Collections.Generic;
using BrainCloud.JsonFx.Json; // JsonReader: turns brainCloud's JSON text into dictionaries
using UnityEngine;

namespace LootLasso
{
    // NetMatch: online 1v1 over brainCloud lobby "signals".
    //
    // How it works (the prototype in Scripts/Lasso/LassoNetworkTest.cs proved this on itch.io):
    //   1. Both players join the same brainCloud lobby ("LassoLobby"). Its owner is the HOST.
    //   2. The guest says "hello" every second until the host answers "start" with a seed. Both spawn
    //      the same treasure from that seed. The guest measures the round trip and starts its clock half
    //      a trip late, so both clocks agree.
    //   3. Treasure movement is never sent: it comes from the seed and the clock (see Treasure.cs).
    //      Only these travel:
    //        throw    "my lasso was let go from here, this way, this far, at this time"
    //        caught   (host only) "that throw caught these treasures"
    //        end      (host only) "the match is over, here are the final scores"
    //   4. The HOST decides every catch, for both lassos, and the end. Small timing differences can't
    //      make the two games disagree about who owns what.
    //
    // Sides: the host plays the bottom of the world (Side.You), the guest the top (Side.Rival). The guest's
    // camera is turned around 180 degrees, so everyone sees their own hunter at the bottom of the screen.
    //
    // MainMenu adds this to the Arena object and calls FindMatch / Cancel / Leave.
    public class NetMatch : MonoBehaviour
    {
        public static NetMatch Current { get; private set; }

        [Tooltip("brainCloud lobby type with no server (Design > Multiplayer > Lobbies). Used only to pass signals.")]
        public string lobbyType = "LassoLobby";
        [Tooltip("Seconds to look for a human rival before playing the bot instead.")]
        public float searchTimeout = 15f;
        [Tooltip("Online: every 10 s of match time, log where every treasure is. Both players log the same moments, " +
                 "so comparing the host's and the guest's console (the browser's F12 console) shows any desync.")]
        public bool logSyncChecks = true;

        public enum Phase { Idle, Connecting, Searching, Starting, Playing, Over }

        [Header("Readouts")]
        public Phase phase = Phase.Idle;
        public string status = "";
        public bool isHost;
        public string lobbyId;
        public int roundTripMs;

        // True from finding a rival until the match is over (MainMenu uses it to route Play Again).
        public bool IsOnline => phase == Phase.Starting || phase == Phase.Playing || phase == Phase.Over;
        // Seconds left before giving up and playing the bot (for the "looking for a rival" screen).
        public float SearchTimeLeft => Mathf.Max(0f, searchTimeout - (Now - searchStart));

        // Called when matchmaking gives up (time out, or an error): MainMenu starts a bot match.
        public event Action<string> SearchFailed;

        Arena arena;
        LassoSpin bottomLasso, topLasso; // the Player (host's) and the Rival (guest's) objects
        LassoBot bot;
        float searchStart;
        float nextHelloTime;
        int lobbyMembers;
        bool callbackRegistered;
        Vector3 cameraPos;
        Quaternion cameraRot;

        static float Now => (float)Time.realtimeSinceStartupAsDouble;
        static BrainCloudWrapper Bc => BrainCloudManager.Instance != null ? BrainCloudManager.Instance.Wrapper : null;

        void Awake() => Current = this;

        void Start()
        {
            arena = GetComponent<Arena>();
            // Find the two lassos by side. LassoBot's Awake already set the rival's side.
            foreach (var lasso in FindObjectsByType<LassoSpin>())
            {
                if (lasso.side == LassoSpin.Side.You) bottomLasso = lasso;
                else topLasso = lasso;
            }
            if (topLasso != null) bot = topLasso.GetComponent<LassoBot>();
            if (Camera.main != null) { cameraPos = Camera.main.transform.position; cameraRot = Camera.main.transform.rotation; }

            arena.MatchEnded += OnMatchEnded;
            foreach (var lasso in new[] { bottomLasso, topLasso })
            {
                if (lasso == null) continue;
                lasso.Thrown += OnLocalThrow;
                lasso.CatchDecided += OnCatchDecided;
            }
        }

        void OnDestroy()
        {
            LeaveLobby();
            if (arena != null) arena.MatchEnded -= OnMatchEnded;
        }

        // ===== Matchmaking =====

        public void FindMatch()
        {
            var bc = Bc;
            if (bc == null || !BrainCloudManager.Instance.IsLoggedIn) { GiveUp("Offline: playing the bot"); return; }

            phase = Phase.Connecting;
            status = "Connecting...";
            searchStart = Now;
            lobbyMembers = 0;
            lobbyId = null;

            if (!callbackRegistered)
            {
                bc.RTTService.RegisterRTTLobbyCallback(OnLobbyEvent);
                callbackRegistered = true;
            }
            // RTT = brainCloud's real-time connection (a secure websocket, so it works on https pages like itch.io).
            if (bc.RTTService.IsRTTEnabled()) SearchLobby();
            else bc.RTTService.EnableRTT(
                (json, _) => { if (phase == Phase.Connecting) SearchLobby(); },
                (code, reason, error, _) => GiveUp("Can't connect: playing the bot"));
        }

        void SearchLobby()
        {
            phase = Phase.Searching;
            status = "Looking for a rival...";
            // Match anyone: "ranged-absolute" around rating 0 with a range of 1000 lets everyone in.
            var algo = new Dictionary<string, object>
            {
                ["strategy"] = "ranged-absolute",
                ["alignment"] = "center",
                ["ranges"] = new List<int> { 1000 },
            };
            Bc.LobbyService.FindOrCreateLobby(
                lobbyType, 0, 1, algo,
                new Dictionary<string, object>(), // filter
                false,                            // never "ready": the lobby never launches, it's just our room
                new Dictionary<string, object>(), // extra
                "all",                            // team
                new Dictionary<string, object>(), // settings
                null,
                null,
                (code, reason, error, _) => GiveUp("No match: playing the bot"));
        }

        // Stop looking (the Cancel button, or the timeout).
        public void Cancel()
        {
            LeaveLobby();
            phase = Phase.Idle;
            status = "";
        }

        // Back to the menu after an online match, or the rival left: leave the lobby, undo the online setup.
        public void Leave()
        {
            LeaveLobby();
            phase = Phase.Idle;
            SetUpSolo();
        }

        void GiveUp(string why)
        {
            Cancel();
            SearchFailed?.Invoke(why);
        }

        void LeaveLobby()
        {
            if (!string.IsNullOrEmpty(lobbyId) && Bc != null) Bc.LobbyService.LeaveLobby(lobbyId);
            lobbyId = null;
        }

        // ===== Lobby events (from brainCloud, over RTT) =====

        void OnLobbyEvent(string json)
        {
            var message = JsonReader.Deserialize<Dictionary<string, object>>(json);
            string operation = message.TryGetValue("operation", out var op) ? op as string : "";
            var data = message.TryGetValue("data", out var d) ? d as Dictionary<string, object> : null;
            if (data == null) return;

            if (data.TryGetValue("lobbyId", out var id)) lobbyId = id as string;

            // We cancelled (or timed out), but the lobby joined anyway: leave it.
            if (phase == Phase.Idle)
            {
                if (operation == "MEMBER_JOIN") LeaveLobby();
                return;
            }

            if (data.TryGetValue("lobby", out var lobbyObj) && lobbyObj is Dictionary<string, object> lobby)
            {
                if (lobby.TryGetValue("members", out var members) && members is Array memberArray) lobbyMembers = memberArray.Length;
                // The lobby's owner hosts the match.
                if (lobby.TryGetValue("ownerCxId", out var owner)) isHost = owner as string == Bc.Client.RTTConnectionID;
            }

            switch (operation)
            {
                case "MEMBER_JOIN":
                case "MEMBER_UPDATE":
                    if (phase == Phase.Searching && lobbyMembers >= 2)
                    {
                        phase = Phase.Starting;
                        status = "Rival found!";
                        nextHelloTime = 0f; // the guest says hello right away
                    }
                    break;

                case "MEMBER_LEFT":
                case "DISBANDED":
                    // Leaving forfeits: whoever stays wins, whatever the score.
                    if (phase == Phase.Playing) arena.EndMatch("The rival left.", arena.YourScore, arena.RivalScore, localWins: true);
                    else if (phase == Phase.Starting) { phase = Phase.Searching; status = "Looking for a rival..."; }
                    break;

                case "SIGNAL":
                    OnSignal(data);
                    break;
            }
        }

        void OnSignal(Dictionary<string, object> data)
        {
            // Signals go to everyone in the lobby, including the sender: ignore our own.
            if (data.TryGetValue("from", out var f) && f is Dictionary<string, object> from
                && from.TryGetValue("id", out var fromId) && fromId as string == BrainCloudManager.Instance.ProfileId)
                return;
            if (!(data.TryGetValue("signalData", out var s) && s is Dictionary<string, object> msg)) return;

            string type = Str(msg, "type");
            switch (type)
            {
                case "hello" when isHost && phase == Phase.Starting:
                {
                    // The guest is listening: pick a seed and go. Echo its timestamp so it can time the trip.
                    int seed = UnityEngine.Random.Range(1, int.MaxValue);
                    Send(new Dictionary<string, object> { ["type"] = "start", ["seed"] = seed, ["sentAt"] = Num(msg, "sentAt") });
                    BeginMatch(seed, 0f);
                    break;
                }

                case "start" when !isHost && phase == Phase.Starting:
                {
                    // Round trip = our hello there + their start back. The host started its clock when it
                    // sent "start", about half a trip ago, so start ours that far in.
                    float roundTrip = Mathf.Max(0f, Now - Num(msg, "sentAt"));
                    roundTripMs = Mathf.RoundToInt(roundTrip * 1000f);
                    BeginMatch(Int(msg, "seed"), roundTrip / 2f);
                    break;
                }

                case "throw" when phase == Phase.Playing:
                {
                    var lasso = SideLasso(Num(msg, "side"));
                    if (lasso == null) return;
                    lasso.RemoteThrow(new LassoSpin.ThrowInfo
                    {
                        id = Int(msg, "id"),
                        start = new Vector2(Num(msg, "sx"), Num(msg, "sy")),
                        direction = new Vector2(Num(msg, "dx"), Num(msg, "dy")),
                        distance = Num(msg, "distance"),
                        speed = Num(msg, "speed"),
                        launchTime = Num(msg, "launchTime"),
                    });
                    break;
                }

                case "caught" when !isHost && phase == Phase.Playing:
                {
                    var lasso = SideLasso(Num(msg, "side"));
                    if (lasso != null) lasso.ReceiveCatchDecision(Int(msg, "id"), ParseIds(Str(msg, "ids")), ParseSpooks(Str(msg, "spooks")));
                    break;
                }

                case "end" when !isHost && (phase == Phase.Playing || phase == Phase.Over):
                {
                    // The host's final word. "time" = time's up; otherwise someone took every treasure.
                    string reason = Str(msg, "reason") == "time" ? "Time's up!"
                                  : arena.OwnerText(Num(msg, "owner") == 0 ? LassoSpin.Side.You : LassoSpin.Side.Rival);
                    arena.EndMatch(reason, Int(msg, "bottom"), Int(msg, "top"));
                    break;
                }
            }
        }

        // ===== Starting the match: who controls which lasso =====

        void BeginMatch(int seed, float alreadyElapsed)
        {
            phase = Phase.Playing;
            nextSyncCheck = 1;
            status = isHost ? "Online: you're the host" : $"Online: {roundTripMs} ms round trip";
            if (bot != null) bot.enabled = false; // a real person plays the other lasso now

            // Host: plays the bottom with the pointer, replays the top. Decides catches for both.
            // Guest: plays the top with the pointer, replays the bottom. Waits for the host's catches.
            var mine = isHost ? bottomLasso : topLasso;
            var theirs = isHost ? topLasso : bottomLasso;
            mine.control = LassoSpin.Control.Pointer;
            theirs.control = LassoSpin.Control.Remote;
            mine.decideCatches = theirs.decideCatches = isHost;

            arena.localSide = isHost ? LassoSpin.Side.You : LassoSpin.Side.Rival;
            arena.decidesEnd = isHost;

            // The guest's camera: moved to the mirror point and turned upside down, so its hunter is at the bottom.
            var cam = Camera.main.transform;
            cam.position = isHost ? cameraPos : new Vector3(cameraPos.x, 2f * arena.MirrorLineY - cameraPos.y, cameraPos.z);
            cam.rotation = isHost ? cameraRot : cameraRot * Quaternion.Euler(0f, 0f, 180f);

            arena.StartMatch(seed, alreadyElapsed);
        }

        // Solo vs the bot: the original setup.
        public void SetUpSolo()
        {
            if (bottomLasso != null) { bottomLasso.control = LassoSpin.Control.Pointer; bottomLasso.decideCatches = true; }
            if (topLasso != null)
            {
                topLasso.decideCatches = true;
                topLasso.control = bot != null ? LassoSpin.Control.Bot : LassoSpin.Control.Remote;
            }
            if (bot != null) bot.enabled = true;
            arena.localSide = LassoSpin.Side.You;
            arena.decidesEnd = true;
            if (Camera.main != null) { Camera.main.transform.position = cameraPos; Camera.main.transform.rotation = cameraRot; }
        }

        // ===== Sending =====

        // Our player let go of their lasso: tell the other player.
        void OnLocalThrow(LassoSpin lasso, LassoSpin.ThrowInfo t)
        {
            // Any lasso played on this computer (normally the pointer; a bot in playtests), never the remote one.
            if (phase != Phase.Playing || lasso.control == LassoSpin.Control.Remote) return;
            Send(new Dictionary<string, object>
            {
                ["type"] = "throw", ["side"] = (int)lasso.side, ["id"] = t.id,
                ["sx"] = t.start.x, ["sy"] = t.start.y, ["dx"] = t.direction.x, ["dy"] = t.direction.y,
                ["distance"] = t.distance, ["speed"] = t.speed, ["launchTime"] = t.launchTime,
            });
        }

        // The host decided what a throw caught (either lasso), and what it spooked: tell the guest.
        void OnCatchDecided(LassoSpin lasso, int throwId, List<int> ids)
        {
            if (phase != Phase.Playing || !isHost) return;
            // The ids as one string like "3,7,12": simple to send and to read back.
            Send(new Dictionary<string, object>
            {
                ["type"] = "caught", ["side"] = (int)lasso.side, ["id"] = throwId, ["ids"] = string.Join(",", ids),
                ["spooks"] = FormatSpooks(lasso.LastSpooks),
            });
        }

        // Spooks as one string: "id,x,y,time,restarts" for each, separated by ";". Numbers are written the same
        // way on every computer (InvariantCulture: always "1.5", never "1,5") and exactly ("R" = round-trip).
        static string FormatSpooks(List<Treasure.SpookInfo> spooks)
        {
            var parts = new List<string>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var s in spooks)
                parts.Add(string.Join(",", s.id.ToString(inv), s.from.x.ToString("R", inv), s.from.y.ToString("R", inv),
                                           s.time.ToString("R", inv), s.restarts.ToString(inv)));
            return string.Join(";", parts);
        }

        static List<Treasure.SpookInfo> ParseSpooks(string text)
        {
            var spooks = new List<Treasure.SpookInfo>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var one in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = one.Split(',');
                if (p.Length < 5) continue;
                spooks.Add(new Treasure.SpookInfo
                {
                    id = int.Parse(p[0], inv),
                    from = new Vector2(float.Parse(p[1], inv), float.Parse(p[2], inv)),
                    time = float.Parse(p[3], inv),
                    restarts = int.Parse(p[4], inv),
                });
            }
            return spooks;
        }

        void OnMatchEnded()
        {
            if (phase != Phase.Playing) return;
            phase = Phase.Over;
            if (!isHost) return;
            // Whoever owns everything has all the score (the other has 0).
            bool timeUp = arena.timeLeft <= 0f;
            Send(new Dictionary<string, object>
            {
                ["type"] = "end", ["reason"] = timeUp ? "time" : "owner",
                ["owner"] = arena.finalYourScore >= arena.finalRivalScore ? 0 : 1,
                ["bottom"] = arena.finalYourScore, ["top"] = arena.finalRivalScore,
            });
        }

        void Send(Dictionary<string, object> signal)
        {
            if (string.IsNullOrEmpty(lobbyId) || Bc == null) return;
            Bc.LobbyService.SendSignal(lobbyId, signal, null,
                (code, reason, error, _) => Debug.LogWarning($"[NetMatch] Signal failed ({reason}): {error}"));
        }

        void Update()
        {
            // Give up on finding a human after a while: play the bot instead.
            if ((phase == Phase.Connecting || phase == Phase.Searching) && SearchTimeLeft <= 0f)
                GiveUp("No rival found: playing the bot");

            // The guest repeats "hello" until the host answers (in case the host wasn't listening yet).
            // Once a second: signals are server calls, so don't spam them.
            if (phase == Phase.Starting && !isHost && Now >= nextHelloTime)
            {
                Send(new Dictionary<string, object> { ["type"] = "hello", ["sentAt"] = Now });
                nextHelloTime = Now + 1f;
            }

            if (phase == Phase.Playing && logSyncChecks && arena.isPlaying && arena.MatchTime >= nextSyncCheck * 10f)
                LogSyncCheck(nextSyncCheck++ * 10f);
        }

        // ===== Sync check =====
        // One line: where every treasure is at exactly match time "time" (id, state F/H/I = free/hooked/in hoard,
        // position), the same moment on both computers. If a treasure's numbers differ between the host's and
        // the guest's line, it has drifted apart (hooked treasure follows the rope, so it may differ a little).
        int nextSyncCheck;

        void LogSyncCheck(float time)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append(isHost ? "[sync] host " : "[sync] guest ").Append(time.ToString("0", inv)).Append("s:");
            foreach (var t in arena.treasures)
            {
                Vector2 p = t.PositionAt(time);
                sb.Append(' ').Append(t.id).Append(t.state.ToString()[0]).Append(':')
                  .Append(p.x.ToString("0.00", inv)).Append(',').Append(p.y.ToString("0.00", inv));
            }
            Debug.Log(sb.ToString());
        }

        // ===== Small helpers =====

        LassoSpin SideLasso(float side) => side == 0 ? bottomLasso : topLasso; // 0 = Side.You, 1 = Side.Rival

        // Reading values out of a signal. Numbers can arrive as int, long or double, so Convert handles them all.
        static float Num(Dictionary<string, object> msg, string key) =>
            msg.TryGetValue(key, out var v) && v != null ? Convert.ToSingle(v) : 0f;

        // Whole numbers read exactly (a float can't hold a big seed like 1234567891 exactly).
        static int Int(Dictionary<string, object> msg, string key) =>
            msg.TryGetValue(key, out var v) && v != null ? (int)Convert.ToInt64(v) : 0;

        static string Str(Dictionary<string, object> msg, string key) =>
            msg.TryGetValue(key, out var v) && v != null ? v.ToString() : "";

        static List<int> ParseIds(string text)
        {
            var ids = new List<int>();
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries)) ids.Add(int.Parse(part));
            return ids;
        }
    }
}
