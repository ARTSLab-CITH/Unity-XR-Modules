using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// EXAMPLE SCRIPT: Unity game server component.
/// Demonstrates maintaining a WebSocket connection to the discovered web server
/// (at <c>WS /centroid/subscribe/{session_id}</c>).
///
/// This script relies on <see cref="ServerDiscovery"/> to locate the external
/// server, and then pushes events via <see cref="CentroidNetworkManager"/>
/// to regular clients.
/// </summary>
public class CentroidSubscriber : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Configuration
    // -----------------------------------------------------------------------

    [Tooltip("Session ID — must match the headsets and CentroidClient.")]
    [SerializeField] private string sessionId = "installation-2024";

    [Tooltip("Seconds between keep-alive pings.")]
    [SerializeField] private float pingIntervalSeconds = 15f;

    [Tooltip("Seconds to wait before reconnecting after a drop.")]
    [SerializeField] private float reconnectBaseDelay = 2f;

    [Tooltip("Maximum reconnect delay (exponential back-off cap).")]
    [SerializeField] private float reconnectMaxDelay = 30f;

    // -----------------------------------------------------------------------
    // Events — subscribe from CentroidAI or any visualization script
    // -----------------------------------------------------------------------

    /// <summary>Fired on the main thread when an emotion_state event arrives.</summary>
    public event Action<EmotionStateEvent> OnEmotionState;

    /// <summary>Fired on the main thread when the AI decides to respond.</summary>
    public event Action<CentroidResponseEvent> OnCentroidResponse;

    /// <summary>Fired on the main thread when a WebSocket error event arrives.</summary>
    public event Action<string> OnError;

    /// <summary>True while the WebSocket is connected.</summary>
    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

    // -----------------------------------------------------------------------
    // Singleton
    // -----------------------------------------------------------------------

    public static CentroidSubscriber Instance { get; private set; }

    // -----------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
    private int _reconnectAttempts = 0;

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // ServerManager uses DontDestroyOnLoad via NetworkManager;
            // we don't call it ourselves to avoid conflicts.
        }
        else
        {
            // Destroy only the duplicate component, not the whole
            // ServerManager GameObject (which also hosts NetworkManager).
            Destroy(this);
            return;
        }
    }

    private void Start()
    {
        StartCoroutine(InitWhenReady());
    }

    /// <summary>
    /// Waits for NetworkManager to start (via <see cref="NetworkBootstrap"/>),
    /// checks if we are the server, then waits for the web-server to be
    /// discovered before connecting the WebSocket.
    /// </summary>
    private IEnumerator InitWhenReady()
    {
        Debug.Log("[CentroidSubscriber] InitWhenReady() — waiting for NetworkManager.Singleton...");

        // Wait for NetworkManager to be available
        while (NetworkManager.Singleton == null)
            yield return null;

        Debug.Log("[CentroidSubscriber] NetworkManager.Singleton found. Waiting for IsListening...");

        // Wait for NetworkBootstrap to call StartServer() / StartClient().
        // IsListening becomes true only after the network role is determined.
        while (!NetworkManager.Singleton.IsListening)
            yield return null;

        Debug.Log($"[CentroidSubscriber] IsListening=true. IsServer={NetworkManager.Singleton.IsServer}, IsHost={NetworkManager.Singleton.IsHost}, IsClient={NetworkManager.Singleton.IsClient}");

        // Only the game server opens the WebSocket
        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost)
        {
            Debug.Log("[CentroidSubscriber] Not the server — skipping WebSocket.");
            yield break;
        }

        // Now wait for web-server mDNS discovery
        Debug.Log("[CentroidSubscriber] Waiting for server discovery...");
        while (!ServerDiscovery.IsServerFound)
        {
            yield return new WaitForSeconds(0.5f);
        }

        ConnectAsync();
    }

    private void Update()
    {
        // Drain the thread-safe queue on the main thread
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Debug.LogError($"[CentroidSubscriber] Main-thread callback error: {ex}"); }
        }
    }

    private void OnDestroy()
    {
        CloseConnection();
    }

    private void OnApplicationQuit()
    {
        CloseConnection();
    }

    // -----------------------------------------------------------------------
    // Connection management
    // -----------------------------------------------------------------------

    private async void ConnectAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        _ws?.Dispose();
        _ws = new ClientWebSocket();

        string httpUrl = ServerDiscovery.ServerUrl; // e.g. http://192.168.1.100:8000
        string wsUrl = httpUrl
            .Replace("https://", "wss://")
            .Replace("http://", "ws://");

        var uri = new Uri($"{wsUrl}/centroid/subscribe/{Uri.EscapeDataString(sessionId)}");

        try
        {
            Debug.Log($"[CentroidSubscriber] Connecting to {uri} ...");
            await _ws.ConnectAsync(uri, _cts.Token);
            Debug.Log("[CentroidSubscriber] WebSocket connected.");
            _reconnectAttempts = 0;

            // Start the receive loop and ping loop concurrently
            _ = ReceiveLoop(_cts.Token);
            _ = PingLoop(_cts.Token);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CentroidSubscriber] Connection failed: {ex.Message}");
            ScheduleReconnect();
        }
    }

    private void CloseConnection()
    {
        _cts?.Cancel();

        if (_ws != null)
        {
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", CancellationToken.None)
                        .ContinueWith(_ => _ws.Dispose());
                }
                catch { /* best effort */ }
            }
            else
            {
                _ws.Dispose();
            }
            _ws = null;
        }
    }

    private void ScheduleReconnect()
    {
        _reconnectAttempts++;
        float delay = Mathf.Min(reconnectBaseDelay * Mathf.Pow(2, _reconnectAttempts - 1), reconnectMaxDelay);
        Debug.Log($"[CentroidSubscriber] Reconnecting in {delay:F1}s (attempt {_reconnectAttempts})...");
        StartCoroutine(ReconnectAfterDelay(delay));
    }

    private IEnumerator ReconnectAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        // Re-check that the server is still discovered
        if (ServerDiscovery.IsServerFound)
        {
            ConnectAsync();
        }
        else
        {
            StartCoroutine(InitWhenReady());
        }
    }

    // -----------------------------------------------------------------------
    // WebSocket receive loop (runs on a background thread via async)
    // -----------------------------------------------------------------------

    private async Task ReceiveLoop(CancellationToken ct)
    {
        // 256 KB buffer — large enough for base64-encoded MP3 payloads
        var buffer = new byte[1024 * 256];

        try
        {
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                int totalBytes = 0;

                // Handle multi-frame messages (audio payloads can be large)
                do
                {
                    // Grow buffer if needed
                    if (totalBytes >= buffer.Length)
                    {
                        var newBuffer = new byte[buffer.Length * 2];
                        Buffer.BlockCopy(buffer, 0, newBuffer, 0, totalBytes);
                        buffer = newBuffer;
                    }

                    result = await _ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer, totalBytes, buffer.Length - totalBytes), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[CentroidSubscriber] Server closed WebSocket.");
                        _mainThreadQueue.Enqueue(() => ScheduleReconnect());
                        return;
                    }

                    totalBytes += result.Count;
                }
                while (!result.EndOfMessage);

                string json = Encoding.UTF8.GetString(buffer, 0, totalBytes);
                HandleMessage(json);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (WebSocketException ex)
        {
            Debug.LogWarning($"[CentroidSubscriber] WebSocket error: {ex.Message}");
            _mainThreadQueue.Enqueue(() => ScheduleReconnect());
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CentroidSubscriber] Unexpected error in receive loop: {ex}");
            _mainThreadQueue.Enqueue(() => ScheduleReconnect());
        }
    }

    // -----------------------------------------------------------------------
    // Keep-alive ping loop
    // -----------------------------------------------------------------------

    private async Task PingLoop(CancellationToken ct)
    {
        byte[] pingBytes = Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");

        try
        {
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(pingIntervalSeconds), ct);

                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(pingBytes),
                        WebSocketMessageType.Text,
                        true, ct);
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CentroidSubscriber] Ping failed: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Message dispatch
    // -----------------------------------------------------------------------

    private void HandleMessage(string json)
    {
        // Quick-parse just the "type" field
        var baseMsg = JsonUtility.FromJson<WebSocketMessage>(json);

        switch (baseMsg.type)
        {
            case "emotion_state":
                var state = JsonUtility.FromJson<EmotionStateEvent>(json);
                _mainThreadQueue.Enqueue(() =>
                {
                    Debug.Log($"[CentroidSubscriber] emotion_state: dominant={state.dominant}, intensity={state.cumulative_intensity:F2}, utterances={state.utterance_count}");
                    OnEmotionState?.Invoke(state);
                });
                break;

            case "centroid_response":
                var response = JsonUtility.FromJson<CentroidResponseEvent>(json);
                _mainThreadQueue.Enqueue(() =>
                {
                    Debug.Log($"[CentroidSubscriber] centroid_response: \"{Truncate(response.centroid_response, 80)}\" ({response.utterances_consumed} utterances consumed, audio={response.audio_base64 != null})");
                    OnCentroidResponse?.Invoke(response);
                });
                break;

            case "error":
                string detail = baseMsg.detail ?? "Unknown error";
                _mainThreadQueue.Enqueue(() =>
                {
                    Debug.LogWarning($"[CentroidSubscriber] Server error: {detail}");
                    OnError?.Invoke(detail);
                });
                break;

            case "pong":
                // Expected reply to our pings — no action needed
                break;

            default:
                Debug.LogWarning($"[CentroidSubscriber] Unknown message type: {baseMsg.type}");
                break;
        }
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
    }
}
