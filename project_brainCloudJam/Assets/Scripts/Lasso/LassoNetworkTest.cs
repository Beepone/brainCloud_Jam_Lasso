using System;
using System.Collections.Generic;
using System.Text;
using BrainCloud;
using BrainCloud.JsonFx.Json;
using UnityEngine;

/// <summary>
/// Networking spike: proves two players can find each other and exchange throws, including from a
/// WebGL build on itch.io.
///
/// Two ways to send messages (pick with 'transport'):
/// - LobbySignals (default): both players stay in a brainCloud lobby and send "signals" to each
///   other over RTT. RTT uses a secure connection to brainCloud's servers, so it works on https pages.
///   Slower than the relay (~100-200 ms), but the game only sends occasional throws.
/// - Relay: the lobby launches a relay server and both players connect to it. Faster, but the
///   relay only offers plain ws://, which browsers block on https pages (itch.io). Editor/desktop only.
///
/// Only throws travel over the network. Treasure movement comes from the shared seed and clock,
/// and catches are worked out on each machine. That can disagree in close calls; the real game
/// will let the host decide those. This test is only about the connection.
/// </summary>
public class LassoNetworkTest : MonoBehaviour
{
    public enum Transport { LobbySignals, Relay }

    public Transport transport = Transport.LobbySignals;
    [Tooltip("Lobby type for LobbySignals: no server, never starts (Design > Multiplayer > Lobbies).")]
    public string signalLobbyType = "LassoLobby";
    [Tooltip("Lobby type for Relay: uses the relay server.")]
    public string relayLobbyType = "LassoMatch";
    [Tooltip("Log in as a brand new anonymous player every launch, so two tabs in the same browser count as two players.")]
    public bool freshIdentityEachLaunch = true;

    enum Phase { Idle, LoggingIn, EnablingRtt, Searching, InLobby, Connecting, WaitingForStart, Playing, Failed }
    Phase phase = Phase.Idle;
    string status = "Solo practice. Click Find Match to play online.";
    readonly List<string> log = new List<string>();

    BrainCloudWrapper bc;
    string lobbyId;
    int lobbyMembers;
    bool isHost;
    float nextHelloTime;
    Rect panelRect;

    string LobbyType => transport == Transport.Relay ? relayLobbyType : signalLobbyType;

    [Serializable]
    class NetMessage
    {
        public string type;  // "hello", "start", "throw"
        public int seed;
        public float x, y;   // Throw landing point (world space, same for both players).
        public float speed;  // Throw flight speed.
        public float strength; // Rope strength.
        public float sentAt; // hello: the guest's clock when sent. start: that value echoed back, to measure the round trip. throw: match time of the throw.
    }

    static float Now => (float)Time.realtimeSinceStartupAsDouble;

    // ---------- Matchmaking ----------

    void FindMatch()
    {
        var manager = BrainCloudManager.Instance;
        bc = manager.Wrapper;
        if (bc == null) { Fail("brainCloud isn't initialized (check BrainCloudConfig.json)."); return; }

        if (manager.IsLoggedIn) { EnableRtt(); return; }

        if (freshIdentityEachLaunch)
        {
            bc.ResetStoredProfileId();
            bc.SetStoredAnonymousId(""); // Empty = a new anonymous ID is generated on login.
        }
        SetPhase(Phase.LoggingIn, "Logging in...");
        manager.Login(EnableRtt, Fail);
    }

    void EnableRtt()
    {
        SetPhase(Phase.EnablingRtt, "Connecting to real-time service (RTT)...");
        bc.RTTService.RegisterRTTLobbyCallback(OnLobbyEvent);
        bc.RTTService.EnableRTT(
            (json, _) => SearchLobby(),
            (status, reason, error, _) => Fail($"RTT failed ({reason}). Is RTT enabled for the app, and the jam voucher applied?\n{error}"));
    }

    void SearchLobby()
    {
        SetPhase(Phase.Searching, $"Looking for a '{LobbyType}' lobby ({transport})...");
        var algo = new Dictionary<string, object>
        {
            ["strategy"] = "ranged-absolute",
            ["alignment"] = "center",
            ["ranges"] = new List<int> { 1000 },
        };
        // Relay: ready right away, so the lobby launches the server as soon as it's full.
        // Signals: never ready, so the lobby never launches and stays open as our match room.
        bool ready = transport == Transport.Relay;
        bc.LobbyService.FindOrCreateLobby(
            LobbyType, 0, 1, algo,
            new Dictionary<string, object>(), // filter
            ready,
            new Dictionary<string, object>(), // extra
            "all",                            // team code
            new Dictionary<string, object>(), // settings
            null,
            (json, _) => Log("Lobby request accepted, waiting for lobby events..."),
            (status, reason, error, _) => Fail($"FindOrCreateLobby failed ({reason}). Does the lobby type '{LobbyType}' exist?\n{error}"));
    }

    void OnLobbyEvent(string json)
    {
        var message = JsonReader.Deserialize<Dictionary<string, object>>(json);
        string operation = message.TryGetValue("operation", out var op) ? op as string : "";
        var data = message.TryGetValue("data", out var d) ? d as Dictionary<string, object> : null;
        if (operation != "SIGNAL") Log("Lobby event: " + operation);

        if (data != null && data.TryGetValue("lobbyId", out var id)) lobbyId = id as string;
        if (data != null && data.TryGetValue("lobby", out var lobbyObj) && lobbyObj is Dictionary<string, object> lobby)
        {
            if (lobby.TryGetValue("members", out var members) && members is Array memberArray) lobbyMembers = memberArray.Length;
            // The lobby owner hosts the match (only matters for signals; the relay reports its own owner).
            if (transport == Transport.LobbySignals && lobby.TryGetValue("ownerCxId", out var owner))
                isHost = owner as string == bc.Client.RTTConnectionID;
        }

        switch (operation)
        {
            case "MEMBER_JOIN":
            case "MEMBER_UPDATE":
                if (transport == Transport.LobbySignals && lobbyMembers >= 2 && phase != Phase.Playing)
                {
                    SetPhase(Phase.WaitingForStart, isHost ? "Opponent found. Starting as HOST..." : "Opponent found. Waiting for the host to start...");
                    nextHelloTime = 0f;
                }
                else if (phase != Phase.Playing && phase != Phase.WaitingForStart)
                {
                    SetPhase(Phase.InLobby, $"In lobby with {lobbyMembers} player(s). Waiting for an opponent...");
                }
                break;

            case "MEMBER_LEFT":
                if (phase == Phase.Playing) status = "The opponent left.";
                break;

            case "SIGNAL":
                OnSignal(data);
                break;

            case "STARTING":
                SetPhase(Phase.InLobby, "Match starting, the relay server is spinning up...");
                break;

            case "ROOM_READY":
                if (transport == Transport.Relay) ConnectRelay(data);
                break;

            case "DISBANDED":
                // With "Disband on start", a relay lobby closes when the match launches (reason RTT_ROOM_READY).
                // That's expected; any other reason means the match never got going.
                int reasonCode = 0;
                if (data != null && data.TryGetValue("reason", out var r) && r is Dictionary<string, object> reason
                    && reason.TryGetValue("code", out var code))
                    reasonCode = Convert.ToInt32(code);
                bool launched = reasonCode == ReasonCodes.RTT_ROOM_READY;
                if (!launched && phase != Phase.Connecting && phase != Phase.WaitingForStart && phase != Phase.Playing)
                    Fail("Lobby disbanded before the match started.\n" + json);
                else if (!launched && phase == Phase.Playing)
                    status = "The lobby closed (match room expired).";
                break;
        }
    }

    // ---------- Transport: lobby signals ----------

    void OnSignal(Dictionary<string, object> data)
    {
        // Signals go to every member, including the sender: ignore our own.
        if (data.TryGetValue("from", out var f) && f is Dictionary<string, object> from
            && from.TryGetValue("id", out var fromId) && fromId as string == BrainCloudManager.Instance.ProfileId)
            return;
        if (!(data.TryGetValue("signalData", out var s) && s is Dictionary<string, object> signal)) return;

        HandleMessage(new NetMessage
        {
            type = signal.TryGetValue("type", out var t) ? t as string : "",
            seed = signal.TryGetValue("seed", out var seed) ? Convert.ToInt32(seed) : 0,
            x = signal.TryGetValue("x", out var x) ? Convert.ToSingle(x) : 0f,
            y = signal.TryGetValue("y", out var y) ? Convert.ToSingle(y) : 0f,
            speed = signal.TryGetValue("speed", out var sp) ? Convert.ToSingle(sp) : 0f,
            strength = signal.TryGetValue("strength", out var st) ? Convert.ToSingle(st) : 0f,
            sentAt = signal.TryGetValue("sentAt", out var at) ? Convert.ToSingle(at) : 0f,
        });
    }

    // ---------- Transport: relay ----------

    void ConnectRelay(Dictionary<string, object> data)
    {
        var connectData = data["connectData"] as Dictionary<string, object>;
        var ports = connectData["ports"] as Dictionary<string, object>;
        string host = connectData["address"] as string;
        string passcode = data["passcode"] as string;

        // https pages (itch.io) need wss://. The relay currently only offers plain ws, so this fails in browsers.
        bool secure = Application.platform == RuntimePlatform.WebGLPlayer || ports.ContainsKey("wss");
        int port = ports.ContainsKey("wss") ? Convert.ToInt32(ports["wss"]) : Convert.ToInt32(ports["ws"]);

        Log($"Relay at {host}, ports: {JsonWriter.Serialize(ports)}");
        SetPhase(Phase.Connecting, $"Connecting to relay {(secure ? "wss" : "ws")}://{host}:{port} ...");

        bc.RelayService.RegisterRelayCallback(OnRelayMessage);
        bc.RelayService.RegisterSystemCallback(OnRelaySystem);
        bc.RelayService.Connect(
            RelayConnectionType.WEBSOCKET,
            new RelayConnectOptions(secure, host, port, passcode, lobbyId),
            (json, _) => OnRelayConnected(),
            (status, reason, error, _) => Fail($"Relay connect failed ({reason}).\n{error}"));
    }

    void OnRelayConnected()
    {
        isHost = bc.RelayService.OwnerCxId == bc.Client.RTTConnectionID;
        SetPhase(Phase.WaitingForStart, isHost ? "Connected as HOST. Waiting for the guest to say hello..." : "Connected as GUEST. Waiting for the host to start...");
        nextHelloTime = 0f;
    }

    void OnRelaySystem(string json)
    {
        var message = JsonReader.Deserialize<Dictionary<string, object>>(json);
        string op = message.TryGetValue("op", out var o) ? o as string : "";
        Log("Relay system: " + op);
        if (op == "DISCONNECT" && phase == Phase.Playing) status = "The opponent disconnected.";
    }

    void OnRelayMessage(short netId, byte[] bytes) =>
        HandleMessage(JsonUtility.FromJson<NetMessage>(Encoding.UTF8.GetString(bytes)));

    // ---------- Messages (same for both transports) ----------

    void HandleMessage(NetMessage msg)
    {
        var arena = LassoArena.Current;
        switch (msg.type)
        {
            case "hello" when isHost && phase == Phase.WaitingForStart:
                // The guest is connected and listening: pick a seed and start, both of us.
                // Echo the guest's timestamp so it can measure how long messages take.
                int seed = UnityEngine.Random.Range(1, int.MaxValue);
                Send(new NetMessage { type = "start", seed = seed, sentAt = msg.sentAt });
                StartNetworkMatch(seed, MatchRole.Host, 0f);
                break;

            case "start" when !isHost && phase != Phase.Playing:
                // Round trip = hello there + start back. Our match clock should be one trip ahead,
                // because the host started its clock when it sent "start".
                float roundTrip = Mathf.Max(0f, Now - msg.sentAt);
                Log($"Round trip {roundTrip * 1000f:0} ms, starting the clock {roundTrip * 500f:0} ms in.");
                StartNetworkMatch(msg.seed, MatchRole.Guest, roundTrip / 2f);
                break;

            case "throw":
                // sentAt = the thrower's match time; with synced clocks, the difference is the travel time.
                if (arena.RemoteThrower != null)
                    arena.RemoteThrower.RemoteThrow(new Vector2(msg.x, msg.y), msg.speed, msg.strength, LassoArena.MatchTime - msg.sentAt);
                break;
        }
    }

    void Send(NetMessage msg)
    {
        if (transport == Transport.Relay)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
            bc.RelayService.Send(bytes, BrainCloudRelay.TO_ALL_PLAYERS, true, true, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1);
        }
        else
        {
            var signal = new Dictionary<string, object>
            {
                ["type"] = msg.type, ["seed"] = msg.seed,
                ["x"] = msg.x, ["y"] = msg.y, ["speed"] = msg.speed, ["strength"] = msg.strength,
                ["sentAt"] = msg.sentAt,
            };
            bc.LobbyService.SendSignal(lobbyId, signal, null,
                (status, reason, error, _) => Log($"Signal failed ({reason}): {error}"));
        }
    }

    void StartNetworkMatch(int seed, MatchRole role, float alreadyElapsed)
    {
        var arena = LassoArena.Current;
        arena.StartMatch(seed, role, alreadyElapsed);
        arena.LocalThrower.Thrown += (landing, speed, strength) =>
            Send(new NetMessage { type = "throw", x = landing.x, y = landing.y, speed = speed, strength = strength, sentAt = LassoArena.MatchTime });
        SetPhase(Phase.Playing, $"Playing online as {role} via {transport}. Seed {seed}.");
    }

    void Update()
    {
        // The guest repeats HELLO until the host replies, in case the host wasn't listening yet.
        // (Every second: lobby signals are server calls, so don't spam them.)
        if (phase == Phase.WaitingForStart && !isHost && Time.time >= nextHelloTime)
        {
            Send(new NetMessage { type = "hello", sentAt = Now });
            nextHelloTime = Time.time + 1f;
        }
        if (phase == Phase.Playing && transport == Transport.Relay && bc != null && bc.RelayService.IsConnected())
            status = $"Playing online as {LassoArena.Current.Role} via Relay. Ping {bc.RelayService.LastPing} ms";

        // No throwing while matchmaking, or while the mouse is over this panel (clicking its buttons).
        bool overPanel = false;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            Vector2 p = mouse.position.ReadValue();
            overPanel = panelRect.Contains(new Vector2(p.x, Screen.height - p.y)); // GUI y runs top-down.
        }
        LassoArena.InputEnabled = !overPanel && (phase == Phase.Idle || phase == Phase.Playing || phase == Phase.Failed);
    }

    void Leave()
    {
        if (bc != null)
        {
            if (bc.RelayService.IsConnected()) bc.RelayService.Disconnect();
            if (!string.IsNullOrEmpty(lobbyId)) bc.LobbyService.LeaveLobby(lobbyId);
            bc.RTTService.DeregisterRTTLobbyCallback();
            bc.RelayService.DeregisterRelayCallback();
            bc.RelayService.DeregisterSystemCallback();
        }
        lobbyId = null;
        LassoArena.Current.StartMatch(LassoArena.Current.seed, MatchRole.Solo);
        SetPhase(Phase.Idle, "Solo practice. Click Find Match to play online.");
    }

    void OnDestroy()
    {
        if (bc == null) return;
        if (bc.RelayService.IsConnected()) bc.RelayService.Disconnect();
        if (!string.IsNullOrEmpty(lobbyId)) bc.LobbyService.LeaveLobby(lobbyId);
    }

    // ---------- UI ----------

    void SetPhase(Phase p, string text)
    {
        phase = p;
        status = text;
        Log(text);
    }

    void Fail(string error)
    {
        SetPhase(Phase.Failed, "Failed: " + error);
        Debug.LogWarning("[LassoNetworkTest] " + error);
    }

    void Log(string line)
    {
        log.Add($"{Time.time:0.0}s  {line}");
        if (log.Count > 8) log.RemoveAt(0);
        Debug.Log("[LassoNetworkTest] " + line);
    }

    void OnGUI()
    {
        float unit = Screen.height / 720f;
        var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * unit), wordWrap = true };
        var button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(16 * unit) };
        float w = 420 * unit;
        panelRect = new Rect(Screen.width - w - 10 * unit, 70 * unit, w, 420 * unit);
        GUILayout.BeginArea(panelRect, GUI.skin.box);

        GUILayout.Label($"<b>Network test</b> ({transport})", new GUIStyle(style) { richText = true });
        GUILayout.Label(status, style);

        if (phase == Phase.Idle || phase == Phase.Failed)
        {
            if (GUILayout.Button("Find Match", button, GUILayout.Height(34 * unit))) FindMatch();
        }
        else if (GUILayout.Button(phase == Phase.Playing ? "Leave Match" : "Cancel", button, GUILayout.Height(34 * unit)))
        {
            Leave();
        }

        foreach (var line in log) GUILayout.Label(line, style);
        GUILayout.EndArea();
    }
}
