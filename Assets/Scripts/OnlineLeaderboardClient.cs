using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class LeaderboardEntry
{
    public string name;
    public int score;
    public string createdAt;
}

[Serializable]
public class LeaderboardResponse { public LeaderboardEntry[] entries; }
[Serializable]
class ScoreSubmission { public string name; public int score; }
[Serializable]
class LeaderboardConfig { public string endpoint; }

/// <summary>REST client for the Tetra leaderboard service. It has no external Unity package dependency.</summary>
public sealed class OnlineLeaderboardClient : MonoBehaviour
{
    public string Endpoint { get; private set; }
    public LeaderboardEntry[] Entries { get; private set; } = Array.Empty<LeaderboardEntry>();
    public bool IsRefreshing { get; private set; }
    public string Status { get; private set; } = "Connecting";
    public event Action Updated;

    void Awake()
    {
        var config = Resources.Load<TextAsset>("OnlineLeaderboardConfig");
        var parsed = config ? JsonUtility.FromJson<LeaderboardConfig>(config.text) : null;
        Endpoint = parsed != null && !string.IsNullOrWhiteSpace(parsed.endpoint) ? parsed.endpoint.TrimEnd('/') : "";
    }

    public void Refresh()
    {
        if (!IsRefreshing) StartCoroutine(GetScores());
    }

    public void Submit(string playerName, int score)
    {
        if (!IsRefreshing && score > 0) StartCoroutine(PostScore(playerName, score));
    }

    IEnumerator GetScores()
    {
        if (string.IsNullOrEmpty(Endpoint)) { Status = "Server not configured"; Updated?.Invoke(); yield break; }
        IsRefreshing = true; Status = "Refreshing"; Updated?.Invoke();
        using (var request = UnityWebRequest.Get(Endpoint + "/api/leaderboard"))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<LeaderboardResponse>(request.downloadHandler.text);
                Entries = response != null && response.entries != null ? response.entries : Array.Empty<LeaderboardEntry>();
                Status = "Live";
            }
            else Status = "Offline";
        }
        IsRefreshing = false; Updated?.Invoke();
    }

    IEnumerator PostScore(string playerName, int score)
    {
        if (string.IsNullOrEmpty(Endpoint)) yield break;
        IsRefreshing = true; Status = "Submitting"; Updated?.Invoke();
        var json = JsonUtility.ToJson(new ScoreSubmission { name = playerName, score = score });
        using (var request = new UnityWebRequest(Endpoint + "/api/scores", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<LeaderboardResponse>(request.downloadHandler.text);
                Entries = response != null && response.entries != null ? response.entries : Entries;
                Status = "Live";
            }
            else Status = "Offline";
        }
        IsRefreshing = false; Updated?.Invoke();
    }
}
