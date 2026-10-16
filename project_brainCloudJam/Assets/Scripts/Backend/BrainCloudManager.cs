using System;
using System.Collections.Generic;
using BrainCloud;
using UnityEngine;

/// <summary>
/// Single entry point for brainCloud. Lives across scenes.
/// Usage: BrainCloudManager.Instance.Login(...), PostScore(...), GetTopScores(...).
/// </summary>
public class BrainCloudManager : MonoBehaviour
{
    public static BrainCloudManager Instance { get; private set; }

    [Tooltip("Leaderboard ID as created in the brainCloud portal (Design > Leaderboards).")]
    public string defaultLeaderboardId = "Highscores";

    public bool IsReady { get; private set; }
    public bool IsLoggedIn { get; private set; }
    public string ProfileId { get; private set; }
    public string PlayerName { get; private set; }

    public BrainCloudWrapper Wrapper { get; private set; }

    // Creates the manager automatically on first access, so no scene setup is needed.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("BrainCloudManager");
        go.AddComponent<BrainCloudManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        var config = BrainCloudConfig.Load();
        if (config == null) return;

        // The wrapper is a MonoBehaviour; its Update() pumps request callbacks.
        Wrapper = gameObject.AddComponent<BrainCloudWrapper>();
        Wrapper.Init(config.serverUrl, config.appSecret, config.appId, config.appVersion);
        IsReady = true;
    }

    // ---------- Authentication ----------

    /// <summary>
    /// Anonymous login. The wrapper remembers the anonymous ID on this device
    /// (PlayerPrefs, which is IndexedDB in WebGL), so returning players keep their profile.
    /// </summary>
    public void Login(Action onSuccess, Action<string> onError = null)
    {
        if (!CheckReady(onError)) return;

        Wrapper.AuthenticateAnonymous(
            (json, _) => HandleAuthSuccess(json, onSuccess),
            (status, reason, error, _) => Fail("Login", status, reason, error, onError));
    }

    /// <summary>Username + password login. Creates the account if it doesn't exist.</summary>
    public void LoginWithUsername(string username, string password, Action onSuccess, Action<string> onError = null)
    {
        if (!CheckReady(onError)) return;

        Wrapper.AuthenticateUniversal(username, password, true,
            (json, _) => HandleAuthSuccess(json, onSuccess),
            (status, reason, error, _) => Fail("Login", status, reason, error, onError));
    }

    public void SetPlayerName(string name, Action onSuccess = null, Action<string> onError = null)
    {
        if (!CheckLoggedIn(onError)) return;

        Wrapper.PlayerStateService.UpdateUserName(name,
            (json, _) => { PlayerName = name; onSuccess?.Invoke(); },
            (status, reason, error, _) => Fail("SetPlayerName", status, reason, error, onError));
    }

    void HandleAuthSuccess(string json, Action onSuccess)
    {
        var response = JsonUtility.FromJson<AuthResponse>(json);
        IsLoggedIn = true;
        ProfileId = response.data.profileId;
        PlayerName = response.data.playerName;
        Debug.Log($"[brainCloud] Logged in as {ProfileId} ({(string.IsNullOrEmpty(PlayerName) ? "no name" : PlayerName)})");
        onSuccess?.Invoke();
    }

    // ---------- Leaderboards ----------

    public void PostScore(long score, Action onSuccess = null, Action<string> onError = null)
        => PostScore(defaultLeaderboardId, score, onSuccess, onError);

    public void PostScore(string leaderboardId, long score, Action onSuccess = null, Action<string> onError = null)
    {
        if (!CheckLoggedIn(onError)) return;

        Wrapper.LeaderboardService.PostScoreToLeaderboard(leaderboardId, score, "{}",
            (json, _) => onSuccess?.Invoke(),
            (status, reason, error, _) => Fail("PostScore", status, reason, error, onError));
    }

    public void GetTopScores(int count, Action<List<LeaderboardEntry>> onSuccess, Action<string> onError = null)
        => GetTopScores(defaultLeaderboardId, count, onSuccess, onError);

    public void GetTopScores(string leaderboardId, int count, Action<List<LeaderboardEntry>> onSuccess, Action<string> onError = null)
    {
        if (!CheckLoggedIn(onError)) return;

        Wrapper.LeaderboardService.GetGlobalLeaderboardPage(leaderboardId,
            BrainCloudSocialLeaderboard.SortOrder.HIGH_TO_LOW, 0, count - 1,
            (json, _) =>
            {
                var response = JsonUtility.FromJson<LeaderboardResponse>(json);
                onSuccess?.Invoke(response.data.leaderboard ?? new List<LeaderboardEntry>());
            },
            (status, reason, error, _) => Fail("GetTopScores", status, reason, error, onError));
    }

    // ---------- Helpers ----------

    bool CheckReady(Action<string> onError)
    {
        if (IsReady) return true;
        onError?.Invoke("brainCloud is not initialized (check BrainCloudConfig.json).");
        return false;
    }

    bool CheckLoggedIn(Action<string> onError)
    {
        if (!CheckReady(onError)) return false;
        if (IsLoggedIn) return true;
        onError?.Invoke("Not logged in.");
        return false;
    }

    static void Fail(string op, int status, int reason, string error, Action<string> onError)
    {
        Debug.LogWarning($"[brainCloud] {op} failed: status {status}, reason {reason}: {error}");
        onError?.Invoke($"{op} failed ({reason})");
    }

    // ---------- Response shapes (JsonUtility) ----------

    [Serializable] class AuthResponse { public AuthData data; }
    [Serializable] class AuthData { public string profileId; public string playerName; }

    [Serializable] class LeaderboardResponse { public LeaderboardData data; }
    [Serializable] class LeaderboardData { public List<LeaderboardEntry> leaderboard; }
}

[Serializable]
public class LeaderboardEntry
{
    public string playerId;
    public string name;
    public long score;
    public int rank;
}
