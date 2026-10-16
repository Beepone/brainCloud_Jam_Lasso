using System.Text;
using UnityEngine;

/// <summary>
/// Throwaway test panel: drop it on any GameObject to check login and leaderboards,
/// in the editor and in the itch.io WebGL build. Delete it once the real UI exists.
/// </summary>
public class BrainCloudSmokeTest : MonoBehaviour
{
    string playerName = "Tester";
    string status = "Not logged in";
    string scores = "";

    void OnGUI()
    {
        // Fit the 360x400 panel to the window, so small itch.io embeds don't clip it.
        float scale = Mathf.Clamp(Screen.height / 420f, 0.5f, 1.5f);
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        GUILayout.BeginArea(new Rect(10, 10, 360, 400), GUI.skin.box);
        var bc = BrainCloudManager.Instance;

        GUILayout.Label("brainCloud smoke test");
        GUILayout.Label(status);

        if (GUILayout.Button("Login (anonymous)"))
        {
            status = "Logging in...";
            bc.Login(() => status = $"Logged in: {bc.ProfileId}", err => status = err);
        }

        GUILayout.BeginHorizontal();
        playerName = GUILayout.TextField(playerName);
        if (GUILayout.Button("Set name", GUILayout.Width(90)))
            bc.SetPlayerName(playerName, () => status = $"Name set: {playerName}", err => status = err);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Post random score"))
        {
            long score = Random.Range(1, 1000);
            bc.PostScore(score, () => status = $"Posted {score}", err => status = err);
        }

        if (GUILayout.Button("Get top 10"))
        {
            bc.GetTopScores(10, entries =>
            {
                var sb = new StringBuilder();
                foreach (var e in entries)
                    sb.AppendLine($"#{e.rank}  {(string.IsNullOrEmpty(e.name) ? "anon" : e.name)}  {e.score}");
                scores = entries.Count == 0 ? "(empty)" : sb.ToString();
                status = "Fetched leaderboard";
            }, err => status = err);
        }

        GUILayout.Label(scores);
        GUILayout.EndArea();
    }
}
