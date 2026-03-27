using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// EXAMPLE SCRIPT: NetworkBehaviour for syncing state from the server to clients.
/// Demonstrates how to bridge events (e.g. from <see cref="CentroidAI"/> via 
/// <see cref="CentroidSubscriber"/>) to all connected headset clients via 
/// Unity Netcode for GameObjects.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CentroidNetworkManager : NetworkBehaviour
{
    // -----------------------------------------------------------------------
    // Singleton
    // -----------------------------------------------------------------------

    public static CentroidNetworkManager Instance { get; private set; }

    // -----------------------------------------------------------------------
    // Inspector
    // -----------------------------------------------------------------------

    [Tooltip("Reference to the CentroidAI component (server machine only). " +
             "Auto-found if left empty.")]
    [SerializeField] private CentroidAI m_CentroidAI;

    // -----------------------------------------------------------------------
    // Networked state
    // -----------------------------------------------------------------------

    /// <summary>
    /// True while the Centroid AI is speaking / processing a response.
    /// Synced automatically to every client — subscribe to OnValueChanged
    /// to pause / resume microphone input.
    /// </summary>
    public NetworkVariable<bool> IsProcessing = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // -----------------------------------------------------------------------
    // Local C# events (fired on whichever machine receives the state)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Fired on all clients (and server) when an emotion_state event arrives
    /// from the Centroid backend.
    /// Parameters: dominant emotion, cumulative intensity, utterance count,
    /// and per-emotion averages (joy, sadness, anger, fear, love, surprise).
    /// </summary>
    public event Action<string, float, int, EmotionAverages> OnEmotionStateReceived;

    /// <summary>
    /// Fired on all clients when the AI produces a text response.
    /// Use this to display the Centroid's words on the headset HUD.
    /// </summary>
    public event Action<string> OnCentroidResponseReceived;

    /// <summary>
    /// Fired on all clients when chunked TTS audio transfer is starting.
    /// Parameters: total chunk count, audio format (e.g. "mp3").
    /// </summary>
    public event Action<int, string> OnAudioChunkBegin;

    /// <summary>
    /// Fired on all clients for each audio chunk received.
    /// Parameters: chunk index (0-based), base64 chunk data.
    /// </summary>
    public event Action<int, string> OnAudioChunk;

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("[CentroidNetworkManager] Singleton instance set.");
        }
        else
        {
            // Only destroy the duplicate component — not the Centroid sphere
            Destroy(this);
        }
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[CentroidNetworkManager] OnNetworkSpawn() — IsServer={IsServer}, IsClient={IsClient}, IsHost={IsHost}");

        if (IsServer)
        {
            // CentroidAI lives on this same Centroid GameObject
            if (m_CentroidAI == null)
                m_CentroidAI = GetComponent<CentroidAI>();

            if (m_CentroidAI != null)
            {
                m_CentroidAI.OnProcessingStateChanged += OnServerProcessingChanged;
                m_CentroidAI.OnEmotionStateChanged += OnServerEmotionChanged;
                m_CentroidAI.OnResponseText += OnServerResponseText;
                m_CentroidAI.OnResponseAudio += OnServerResponseAudio;
                Debug.Log("[CentroidNetworkManager] Subscribed to CentroidAI events (server).");
            }
            else
            {
                Debug.LogError("[CentroidNetworkManager] Could not find CentroidAI on the server.");
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (m_CentroidAI != null)
        {
            m_CentroidAI.OnProcessingStateChanged -= OnServerProcessingChanged;
            m_CentroidAI.OnEmotionStateChanged -= OnServerEmotionChanged;
            m_CentroidAI.OnResponseText -= OnServerResponseText;
            m_CentroidAI.OnResponseAudio -= OnServerResponseAudio;
        }
    }

    // -----------------------------------------------------------------------
    // Server-side CentroidAI handlers
    // -----------------------------------------------------------------------

    private void OnServerProcessingChanged(bool processing)
    {
        // Write to the NetworkVariable — automatically replicated to all clients
        IsProcessing.Value = processing;
    }

    private void OnServerEmotionChanged(EmotionStateEvent state)
    {
        var avg = state.averages ?? new EmotionAverages();

        // Fire locally on the server (for any server-side visualizations)
        OnEmotionStateReceived?.Invoke(
            state.dominant,
            state.cumulative_intensity,
            state.utterance_count,
            avg);

        // Broadcast to headset clients
        EmotionStateRpc(
            state.dominant, state.cumulative_intensity, state.utterance_count,
            avg.joy, avg.sadness, avg.anger, avg.fear, avg.love, avg.surprise);
    }

    private void OnServerResponseText(string text)
    {
        // Broadcast the response text to headsets so they can show it on their HUD.
        CentroidResponseRpc(text);
    }

    private void OnServerResponseAudio(string audioBase64, string audioFormat)
    {
        if (string.IsNullOrEmpty(audioBase64)) return;
        Debug.Log($"[CentroidNetworkManager] Broadcasting TTS audio to clients ({audioFormat}, {audioBase64.Length} base64 chars) in chunks.");
        StartCoroutine(SendAudioInChunks(audioBase64, audioFormat));
    }

    // Max chars per RPC chunk — stays well under the 6 144-byte UnityTransport payload limit
    // after NGO serialization overhead (~3 000 chars ≈ 4.5 KB raw).
    private const int AudioChunkSize = 3000;

    private System.Collections.IEnumerator SendAudioInChunks(string base64, string format)
    {
        int totalChunks = Mathf.CeilToInt((float)base64.Length / AudioChunkSize);
        Debug.Log($"[CentroidNetworkManager] Sending audio begin: {totalChunks} chunks.");
        AudioChunkBeginRpc(totalChunks, format);
        yield return null;

        for (int i = 0; i < totalChunks; i++)
        {
            int start = i * AudioChunkSize;
            int len = Mathf.Min(AudioChunkSize, base64.Length - start);
            AudioChunkRpc(i, base64.Substring(start, len));
            yield return null; // spread across frames to avoid flooding
        }
        Debug.Log($"[CentroidNetworkManager] All {totalChunks} audio chunks sent.");
    }

    // -----------------------------------------------------------------------
    // RPCs  (server → clients)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Delivers a new emotion state to all connected headset clients.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void EmotionStateRpc(
        string dominant, float intensity, int utteranceCount,
        float joy, float sadness, float anger, float fear, float love, float surprise)
    {
        Debug.Log($"[CentroidNetworkManager] Emotion state received (client): dominant={dominant}, intensity={intensity:F2}");
        var avg = new EmotionAverages
        {
            joy = joy,
            sadness = sadness,
            anger = anger,
            fear = fear,
            love = love,
            surprise = surprise
        };
        OnEmotionStateReceived?.Invoke(dominant, intensity, utteranceCount, avg);
    }

    /// <summary>
    /// Delivers the Centroid's response text to all connected headset clients.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void CentroidResponseRpc(string text)
    {
        Debug.Log($"[CentroidNetworkManager] Centroid response received (client): \"{text}\"");
        OnCentroidResponseReceived?.Invoke(text);
    }

    /// <summary>
    /// Signals all clients that a chunked audio transfer is beginning.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void AudioChunkBeginRpc(int totalChunks, string format)
    {
        Debug.Log($"[CentroidNetworkManager] Audio transfer starting on client: {totalChunks} chunks ({format}).");
        OnAudioChunkBegin?.Invoke(totalChunks, format);
    }

    /// <summary>
    /// Delivers one chunk of the TTS audio base64 string to all clients.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void AudioChunkRpc(int chunkIndex, string data)
    {
        OnAudioChunk?.Invoke(chunkIndex, data);
    }
}
