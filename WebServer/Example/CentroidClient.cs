using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

// ---------------------------------------------------------------------------
// Data Models — shared by CentroidClient (REST) and CentroidSubscriber (WS)
// ---------------------------------------------------------------------------

[Serializable]
public class EmotionData
{
    public float anger;
    public float fear;
    public float joy;
    public float love;
    public float sadness;
    public float surprise;
    public string dominant;
}

[Serializable]
public class TranscribeResponse
{
    public string session_id;
    public string transcript;
    public EmotionData emotion;         // may be null
    public int pending_utterances;
}

[Serializable]
public class CentroidReplyResponse
{
    public string session_id;
    public string centroid_response;
    public int utterances_consumed;
    public string audio_base64;         // base64-encoded MP3, may be null
    public string audio_format;         // "mp3" or null
}

[Serializable]
public class PendingUtterancesResponse
{
    public string session_id;
    public int pending_utterances;
}

[Serializable]
public class SessionClearedResponse
{
    public string session_id;
    public bool cleared;
}

[Serializable]
public class SessionListResponse
{
    public string[] session_ids;
}

// ---------------------------------------------------------------------------
// WebSocket event data models (received by CentroidSubscriber)
// ---------------------------------------------------------------------------

[Serializable]
public class WebSocketMessage
{
    public string type;
    public string detail;
}

[Serializable]
public class EmotionAverages
{
    public float anger;
    public float fear;
    public float joy;
    public float love;
    public float sadness;
    public float surprise;
}

[Serializable]
public class EmotionStateEvent
{
    public string type;
    public string session_id;
    public EmotionAverages averages;
    public string dominant;
    public int utterance_count;
    public float cumulative_intensity;
}

[Serializable]
public class CentroidResponseEvent
{
    public string type;
    public string session_id;
    public string centroid_response;
    public int utterances_consumed;
    public string audio_base64;         // base64-encoded MP3, may be null
    public string audio_format;         // "mp3" or null
}

// ---------------------------------------------------------------------------
// CentroidClient MonoBehaviour (Example)
// ---------------------------------------------------------------------------

/// <summary>
/// EXAMPLE SCRIPT: Implements Centroid backend API calls.
/// Demonstrates how to use <see cref="ServerDiscovery"/> to wait for the
/// discovered local web server URL before making REST API calls to it.
/// </summary>
public class CentroidClient : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Configuration
    // -----------------------------------------------------------------------

    [Tooltip("Shared session ID for this installation. All headsets must use the same value.")]
    [SerializeField] private string sessionId = "installation-2024";

    [Tooltip("Timeout in seconds for transcribe requests.")]
    [SerializeField] private float transcribeTimeout = 15f;

    [Tooltip("Timeout in seconds for respond requests.")]
    [SerializeField] private float respondTimeout = 30f;

    // -----------------------------------------------------------------------
    // Singleton
    // -----------------------------------------------------------------------

    public static CentroidClient Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // Only destroy the duplicate component — not the whole GameObject,
            // which may host other components (e.g. if accidentally placed on
            // the Player Prefab alongside WitSpeechRecognizer).
            Debug.LogWarning($"[CentroidClient] Duplicate instance on '{gameObject.name}' — destroying extra component.");
            Destroy(this);
        }
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /centroid/transcribe
    /// Send a WAV audio clip; receive transcript + emotion scores.
    /// Also silently enqueues the speech for the next Centroid response.
    /// </summary>
    /// <param name="wavBytes">Raw WAV bytes recorded from the headset microphone.</param>
    /// <param name="onSuccess">Called on the main thread with the parsed response.</param>
    /// <param name="onFailure">Called on the main thread with an error message.</param>
    public void Transcribe(byte[] wavBytes,
                           Action<TranscribeResponse> onSuccess,
                           Action<string> onFailure = null)
    {
        StartCoroutine(TranscribeCoroutine(wavBytes, onSuccess, onFailure));
    }

    /// <summary>
    /// POST /centroid/respond
    /// Trigger the Centroid AI to compose a response to all queued headset speech.
    /// </summary>
    /// <param name="onSuccess">Called on the main thread with the parsed response.</param>
    /// <param name="onFailure">Called on the main thread with an error message.</param>
    /// <param name="onNoPending">Called when the server returns 409 (no pending utterances).</param>
    public void RequestCentroidResponse(Action<CentroidReplyResponse> onSuccess,
                                        Action<string> onFailure = null,
                                        Action onNoPending = null)
    {
        StartCoroutine(RequestCentroidResponseCoroutine(onSuccess, onFailure, onNoPending));
    }

    /// <summary>
    /// GET /centroid/session/{session_id}/pending
    /// Returns how many headset clips are queued since the last Centroid response.
    /// </summary>
    public void GetPendingUtterances(Action<int> onSuccess,
                                     Action<string> onFailure = null)
    {
        StartCoroutine(GetPendingUtterancesCoroutine(onSuccess, onFailure));
    }

    /// <summary>
    /// DELETE /centroid/session/{session_id}
    /// Clears conversation history and the pending speech queue.
    /// Call between groups for a fresh experience.
    /// </summary>
    public void ResetSession(Action<SessionClearedResponse> onSuccess = null,
                             Action<string> onFailure = null)
    {
        StartCoroutine(ResetSessionCoroutine(onSuccess, onFailure));
    }

    /// <summary>
    /// GET /centroid/sessions
    /// Returns all active session IDs on the backend.
    /// </summary>
    public void GetActiveSessions(Action<SessionListResponse> onSuccess,
                                  Action<string> onFailure = null)
    {
        StartCoroutine(GetActiveSessionsCoroutine(onSuccess, onFailure));
    }

    // -----------------------------------------------------------------------
    // Coroutine Implementations
    // -----------------------------------------------------------------------

    private IEnumerator TranscribeCoroutine(byte[] wavBytes,
                                            Action<TranscribeResponse> onSuccess,
                                            Action<string> onFailure)
    {
        if (!WaitForServer(out string baseUrl))
        {
            onFailure?.Invoke("Server not yet discovered. Call after ServerDiscovery.IsServerFound is true.");
            yield break;
        }

        var form = new WWWForm();
        form.AddField("session_id", sessionId);
        form.AddBinaryData("audio", wavBytes, "clip.wav", "audio/wav");

        using var req = UnityWebRequest.Post($"{baseUrl}/centroid/transcribe", form);
        req.timeout = Mathf.RoundToInt(transcribeTimeout);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            string err = $"Transcribe failed [{req.responseCode}]: {req.error}";
            Debug.LogError(err);
            onFailure?.Invoke(err);
            yield break;
        }

        var result = JsonUtility.FromJson<TranscribeResponse>(req.downloadHandler.text);
        Debug.Log($"[CentroidClient] Transcribed: \"{result.transcript}\" | dominant emotion: {result.emotion?.dominant} | pending: {result.pending_utterances}");
        onSuccess?.Invoke(result);
    }

    private IEnumerator RequestCentroidResponseCoroutine(Action<CentroidReplyResponse> onSuccess,
                                                         Action<string> onFailure,
                                                         Action onNoPending)
    {
        if (!WaitForServer(out string baseUrl))
        {
            onFailure?.Invoke("Server not yet discovered.");
            yield break;
        }

        var form = new WWWForm();
        form.AddField("session_id", sessionId);

        using var req = UnityWebRequest.Post($"{baseUrl}/centroid/respond", form);
        req.timeout = Mathf.RoundToInt(respondTimeout);

        yield return req.SendWebRequest();

        // 409 = no pending utterances — not an error, handle gracefully
        if (req.responseCode == 409)
        {
            Debug.Log("[CentroidClient] /respond: no pending utterances (409).");
            onNoPending?.Invoke();
            yield break;
        }

        if (req.result != UnityWebRequest.Result.Success)
        {
            string err = $"Respond failed [{req.responseCode}]: {req.error}";
            Debug.LogError(err);
            onFailure?.Invoke(err);
            yield break;
        }

        var result = JsonUtility.FromJson<CentroidReplyResponse>(req.downloadHandler.text);
        Debug.Log($"[CentroidClient] Centroid response ({result.utterances_consumed} utterances consumed): \"{result.centroid_response}\"");
        onSuccess?.Invoke(result);
    }

    private IEnumerator GetPendingUtterancesCoroutine(Action<int> onSuccess,
                                                      Action<string> onFailure)
    {
        if (!WaitForServer(out string baseUrl))
        {
            onFailure?.Invoke("Server not yet discovered.");
            yield break;
        }

        using var req = UnityWebRequest.Get($"{baseUrl}/centroid/session/{Uri.EscapeDataString(sessionId)}/pending");
        req.timeout = 10;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            string err = $"GetPending failed [{req.responseCode}]: {req.error}";
            Debug.LogError(err);
            onFailure?.Invoke(err);
            yield break;
        }

        var result = JsonUtility.FromJson<PendingUtterancesResponse>(req.downloadHandler.text);
        onSuccess?.Invoke(result.pending_utterances);
    }

    private IEnumerator ResetSessionCoroutine(Action<SessionClearedResponse> onSuccess,
                                              Action<string> onFailure)
    {
        if (!WaitForServer(out string baseUrl))
        {
            onFailure?.Invoke("Server not yet discovered.");
            yield break;
        }

        using var req = UnityWebRequest.Delete($"{baseUrl}/centroid/session/{Uri.EscapeDataString(sessionId)}");
        req.downloadHandler = new DownloadHandlerBuffer();
        req.timeout = 10;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            string err = $"ResetSession failed [{req.responseCode}]: {req.error}";
            Debug.LogError(err);
            onFailure?.Invoke(err);
            yield break;
        }

        var result = JsonUtility.FromJson<SessionClearedResponse>(req.downloadHandler.text);
        Debug.Log($"[CentroidClient] Session '{result.session_id}' cleared: {result.cleared}");
        onSuccess?.Invoke(result);
    }

    private IEnumerator GetActiveSessionsCoroutine(Action<SessionListResponse> onSuccess,
                                                   Action<string> onFailure)
    {
        if (!WaitForServer(out string baseUrl))
        {
            onFailure?.Invoke("Server not yet discovered.");
            yield break;
        }

        using var req = UnityWebRequest.Get($"{baseUrl}/centroid/sessions");
        req.timeout = 10;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            string err = $"GetActiveSessions failed [{req.responseCode}]: {req.error}";
            Debug.LogError(err);
            onFailure?.Invoke(err);
            yield break;
        }

        var result = JsonUtility.FromJson<SessionListResponse>(req.downloadHandler.text);
        onSuccess?.Invoke(result);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the server base URL if discovery has completed, otherwise false.
    /// </summary>
    private static bool WaitForServer(out string baseUrl)
    {
        if (ServerDiscovery.IsServerFound && ServerDiscovery.ServerUrl != null)
        {
            baseUrl = ServerDiscovery.ServerUrl;
            return true;
        }
        baseUrl = null;
        return false;
    }
}
