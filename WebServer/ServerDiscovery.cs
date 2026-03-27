using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
#if !(UNITY_ANDROID && !UNITY_EDITOR)
using Zeroconf;
#endif

#if UNITY_ANDROID && !UNITY_EDITOR

// --- Native C# Wrapper for Android's NsdManager ---
public static class AndroidNsd
{
    // --- Public Events ---
    public static event Action<NsdServiceInfo> OnServiceFound;
    public static event Action<NsdServiceInfo> OnServiceLost;
    public static event Action<NsdServiceInfo> OnServiceResolved;
    public static event Action<string> OnDiscoveryFailed;
    public static event Action<string> OnResolveFailed;

    // --- Private Android Java Objects ---
    private static AndroidJavaObject _nsdManager;
    private static DiscoveryListenerProxy _discoveryListener;
    private static ResolveListenerProxy _resolveListener;
    private static MainThreadHelper _mainThreadHelper;

    // --- Helper class to hold service info ---
    public class NsdServiceInfo
    {
        public string ServiceName { get; set; }
        public string ServiceType { get; set; }
        public string HostAddress { get; set; }
        public int Port { get; set; }
        internal AndroidJavaObject JavaObject { get; set; } // Keep a reference to the Java object
    }

    // --- Static constructor to initialize the wrapper ---
    static AndroidNsd()
    {
        // Create a hidden GameObject to run our main thread dispatcher
        var go = new GameObject("AndroidNsdHelper");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _mainThreadHelper = go.AddComponent<MainThreadHelper>();

        // Get the NsdManager system service
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var context = currentActivity.Call<AndroidJavaObject>("getApplicationContext"))
        {
            string nsdService = context.GetStatic<string>("NSD_SERVICE");
            _nsdManager = context.Call<AndroidJavaObject>("getSystemService", nsdService);
        }

        // Create instances of our listener proxies
        _discoveryListener = new DiscoveryListenerProxy();
        _resolveListener = new ResolveListenerProxy();
    }

    // --- Public Methods ---
    public static void StartDiscovery(string serviceType)
    {
        if (_nsdManager == null) return;
        _nsdManager.Call("discoverServices", serviceType, 1 /* PROTOCOL_DNS_SD */, _discoveryListener);
    }

    public static void StopDiscovery()
    {
        if (_nsdManager == null) return;
        _nsdManager.Call("stopServiceDiscovery", _discoveryListener);
    }

    public static void ResolveService(NsdServiceInfo service)
    {
        if (_nsdManager == null || service.JavaObject == null) return;
        _nsdManager.Call("resolveService", service.JavaObject, _resolveListener);
    }

    // --- Proxy Classes to implement Android Listener Interfaces ---

    private class DiscoveryListenerProxy : AndroidJavaProxy
    {
        public DiscoveryListenerProxy() : base("android.net.nsd.NsdManager$DiscoveryListener") { }

        // Forward callbacks to our C# events, marshalled to the main thread
        public void onServiceFound(AndroidJavaObject serviceInfo)
        {
            var newService = new NsdServiceInfo
            {
                ServiceName = serviceInfo.Call<string>("getServiceName"),
                ServiceType = serviceInfo.Call<string>("getServiceType"),
                JavaObject = serviceInfo
            };
            _mainThreadHelper.Enqueue(() => OnServiceFound?.Invoke(newService));
        }

        public void onServiceLost(AndroidJavaObject serviceInfo)
        {
            var lostService = new NsdServiceInfo
            {
                ServiceName = serviceInfo.Call<string>("getServiceName"),
                ServiceType = serviceInfo.Call<string>("getServiceType")
            };
            _mainThreadHelper.Enqueue(() => OnServiceLost?.Invoke(lostService));
        }

        // Other listener methods (can be left empty if not needed)
        public void onDiscoveryStarted(string serviceType) { }
        public void onDiscoveryStopped(string serviceType) { }
        public void onStartDiscoveryFailed(string serviceType, int errorCode)
        {
            _mainThreadHelper.Enqueue(() => OnDiscoveryFailed?.Invoke($"Error code: {errorCode}"));
        }
        public void onStopDiscoveryFailed(string serviceType, int errorCode) { }
    }

    private class ResolveListenerProxy : AndroidJavaProxy
    {
        public ResolveListenerProxy() : base("android.net.nsd.NsdManager$ResolveListener") { }

        public void onServiceResolved(AndroidJavaObject serviceInfo)
        {
            using (var host = serviceInfo.Call<AndroidJavaObject>("getHost"))
            {
                var resolvedService = new NsdServiceInfo
                {
                    ServiceName = serviceInfo.Call<string>("getServiceName"),
                    ServiceType = serviceInfo.Call<string>("getServiceType"),
                    HostAddress = host.Call<string>("getHostAddress"),
                    Port = serviceInfo.Call<int>("getPort"),
                    JavaObject = serviceInfo
                };
                _mainThreadHelper.Enqueue(() => OnServiceResolved?.Invoke(resolvedService));
            }
        }

        public void onResolveFailed(AndroidJavaObject serviceInfo, int errorCode)
        {
            string serviceName = serviceInfo.Call<string>("getServiceName");
            _mainThreadHelper.Enqueue(() => OnResolveFailed?.Invoke($"Failed to resolve '{serviceName}'. Error code: {errorCode}"));
        }
    }

    // --- Helper MonoBehaviour to execute actions on the main thread ---
    private class MainThreadHelper : MonoBehaviour
    {
        private readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();

        public void Enqueue(Action action)
        {
            _actionQueue.Enqueue(action);
        }

        private void Update()
        {
            while (_actionQueue.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }
    }
}

#endif // UNITY_ANDROID && !UNITY_EDITOR

// --- Main MonoBehaviour ---
public class ServerDiscovery : MonoBehaviour
{
    public static string ServerUrl { get; private set; }
    public static bool IsServerFound { get; private set; } = false;

    [Tooltip("The zeroconf/mDNS service type to discover")]
    [SerializeField] private string serviceType = "_centroid._tcp";

    [Tooltip("Fallback static IP used when mDNS/NSD discovery is unavailable.")]
    [SerializeField] private string fallbackIp = "192.168.1.38";

    [Tooltip("Fallback port used when mDNS/NSD discovery is unavailable.")]
    [SerializeField] private int fallbackPort = 8000;

    [Tooltip("Timeout in seconds when probing the fallback server address.")]
    [SerializeField] private float fallbackTimeoutSeconds = 3f;

    public static ServerDiscovery Instance { get; private set; }
    private bool _isDiscovering = false;

#if !(UNITY_ANDROID && !UNITY_EDITOR)
    // Zeroconf (Linux / Windows / Editor) fields
    [Tooltip("How long each Zeroconf scan lasts (seconds).")]
    [SerializeField] private float zeroconfScanSeconds = 5f;

    [Tooltip("Seconds between retries when the service is not found.")]
    [SerializeField] private float zeroconfRetryInterval = 3f;

    private CancellationTokenSource _zeroconfCts;
#endif

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        Debug.Log("[ServerDiscovery] Start() — beginning service discovery.");
#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("[ServerDiscovery] Platform: Android (NSD path).");
        // Subscribe to the events from our native plugin
        AndroidNsd.OnServiceFound += HandleServiceFound;
        AndroidNsd.OnServiceResolved += HandleServiceResolved;
        AndroidNsd.OnServiceLost += HandleServiceLost;
        AndroidNsd.OnDiscoveryFailed += HandleDiscoveryFailed;
        AndroidNsd.OnResolveFailed += HandleResolveFailed;

        StartDiscovery();
#else
        Debug.Log("[ServerDiscovery] Platform: Editor / Desktop / Linux (Zeroconf path).");
        StartCoroutine(ZeroconfDiscoveryLoop());
#endif
    }

    private void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Unsubscribe and clean up
        AndroidNsd.OnServiceFound -= HandleServiceFound;
        AndroidNsd.OnServiceResolved -= HandleServiceResolved;
        AndroidNsd.OnServiceLost -= HandleServiceLost;
        AndroidNsd.OnDiscoveryFailed -= HandleDiscoveryFailed;
        AndroidNsd.OnResolveFailed -= HandleResolveFailed;

        if (_isDiscovering)
        {
            AndroidNsd.StopDiscovery();
        }
#else
        _zeroconfCts?.Cancel();
        _zeroconfCts?.Dispose();
        _zeroconfCts = null;
#endif
    }

    public void StartDiscovery()
    {
        if (_isDiscovering || IsServerFound)
        {
            Debug.Log($"[ServerDiscovery] StartDiscovery() skipped — isDiscovering={_isDiscovering}, IsServerFound={IsServerFound}");
            return;
        }

        _isDiscovering = true;
#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("[ServerDiscovery] Starting discovery via Android NSD...");
        AndroidNsd.StartDiscovery(serviceType);

        // Also probe the static fallback address immediately, in case mDNS
        // broadcasts are blocked by network policy and NSD never responds.
        StartCoroutine(TryFallbackServer(success =>
        {
            if (success)
            {
                Debug.Log("[ServerDiscovery] Fallback succeeded — stopping NSD discovery.");
                AndroidNsd.StopDiscovery();
            }
        }));
#else
        Debug.Log("[ServerDiscovery] Starting discovery via Zeroconf...");
        StartCoroutine(ZeroconfDiscoveryLoop());
#endif
    }

    // ===================================================================
    // Android NSD callbacks
    // ===================================================================

#if UNITY_ANDROID && !UNITY_EDITOR
    private void HandleServiceFound(AndroidNsd.NsdServiceInfo service)
    {
        Debug.Log($"[ServerDiscovery] Service found: {service.ServiceName}. Resolving...");
        AndroidNsd.ResolveService(service);
    }

    private void HandleServiceResolved(AndroidNsd.NsdServiceInfo resolvedService)
    {
        Debug.Log($"[ServerDiscovery] Service resolved: {resolvedService.ServiceName} at {resolvedService.HostAddress}:{resolvedService.Port}");
        SetServer(resolvedService.HostAddress, resolvedService.Port);
        AndroidNsd.StopDiscovery();
    }

    private void HandleServiceLost(AndroidNsd.NsdServiceInfo service)
    {
        Debug.LogWarning($"[ServerDiscovery] Service lost: {service.ServiceName}");
        if (ServerUrl != null && ServerUrl.Contains(service.HostAddress))
        {
            InvalidateServer();
        }
    }

    private void HandleDiscoveryFailed(string error)
    {
        Debug.LogError($"[ServerDiscovery] NSD Discovery failed: {error}");
        _isDiscovering = false;
        StartCoroutine(TryFallbackServer(null));
    }

    private void HandleResolveFailed(string error)
    {
        Debug.LogError($"[ServerDiscovery] NSD Resolve failed: {error}");
    }
#endif // UNITY_ANDROID && !UNITY_EDITOR

    // ===================================================================
    // Zeroconf discovery (Linux / Windows / Editor)
    // ===================================================================

#if !(UNITY_ANDROID && !UNITY_EDITOR)
    /// <summary>
    /// Coroutine that repeatedly scans via <see cref="ZeroconfResolver"/>
    /// until the Centroid web server is found.
    /// </summary>
    private IEnumerator ZeroconfDiscoveryLoop()
    {
        if (IsServerFound) yield break;

        _isDiscovering = true;
        _zeroconfCts?.Cancel();
        _zeroconfCts = new CancellationTokenSource();

        Debug.Log("[ServerDiscovery] Zeroconf: scanning for " + serviceType + ".local. ...");

        while (!IsServerFound)
        {
            bool scanDone = false;
            bool found = false;

            // Fire-and-forget async resolve, signal back when done
            ResolveZeroconfAsync(_zeroconfCts.Token, (success) =>
            {
                found = success;
                scanDone = true;
            });

            // Wait for the async scan to finish
            while (!scanDone)
            {
                yield return null;
            }

            if (found)
            {
                _isDiscovering = false;
                yield break;
            }

            // mDNS scan failed — try the static fallback address before waiting.
            bool fallbackDone = false;
            bool fallbackFound = false;
            StartCoroutine(TryFallbackServer(success =>
            {
                fallbackFound = success;
                fallbackDone = true;
            }));
            while (!fallbackDone) yield return null;
            if (fallbackFound) { _isDiscovering = false; yield break; }

            Debug.Log($"[ServerDiscovery] Zeroconf: service not found, retrying in {zeroconfRetryInterval}s...");
            yield return new WaitForSeconds(zeroconfRetryInterval);
        }

        _isDiscovering = false;
    }

    /// <summary>
    /// Runs a single Zeroconf scan. Calls <paramref name="onComplete"/>
    /// on the main thread with true if the service was found.
    /// </summary>
    private async void ResolveZeroconfAsync(CancellationToken ct, Action<bool> onComplete)
    {
        try
        {
            var scanTime = TimeSpan.FromSeconds(zeroconfScanSeconds);
            var results = await ZeroconfResolver.ResolveAsync(
                serviceType + ".local.",
                scanTime: scanTime,
                retries: 2,
                retryDelayMilliseconds: 1000,
                cancellationToken: ct
            );

            if (ct.IsCancellationRequested)
            {
                onComplete?.Invoke(false);
                return;
            }

            var host = results.FirstOrDefault();
            if (host != null)
            {
                string ip = host.IPAddress;
                int port = 8000; // default

                // Try to read the port from the service record
                var serviceKey = serviceType + ".local.";
                if (host.Services.TryGetValue(serviceKey, out var svc))
                {
                    port = svc.Port;
                }
                else if (host.Services.Count > 0)
                {
                    // Fallback: take the first service's port
                    port = host.Services.Values.First().Port;
                }

                Debug.Log($"[ServerDiscovery] Zeroconf resolved: {host.DisplayName} at {ip}:{port}");
                SetServer(ip, port);
                onComplete?.Invoke(true);
                return;
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            Debug.LogError($"[ServerDiscovery] Zeroconf error: {ex.Message}");
        }

        onComplete?.Invoke(false);
    }
#endif // !(UNITY_ANDROID && !UNITY_EDITOR)

    // ===================================================================
    // Shared helpers
    // ===================================================================

    /// <summary>
    /// Probes <see cref="fallbackIp"/>:<see cref="fallbackPort"/> with an HTTP HEAD
    /// request. If the server responds (any HTTP status), calls <see cref="SetServer"/>.
    /// </summary>
    private IEnumerator TryFallbackServer(Action<bool> onComplete)
    {
        if (IsServerFound) { onComplete?.Invoke(false); yield break; }

        string url = $"http://{fallbackIp}:{fallbackPort}";
        Debug.Log($"[ServerDiscovery] Trying fallback server at {url}...");

        using var req = UnityWebRequest.Head(url);
        req.timeout = Mathf.Max(1, (int)fallbackTimeoutSeconds);
        yield return req.SendWebRequest();

        // Any HTTP response code means the server is reachable.
        bool reachable = req.result == UnityWebRequest.Result.Success || req.responseCode > 0;
        if (reachable)
        {
            Debug.Log($"[ServerDiscovery] Fallback server reachable at {url} (HTTP {req.responseCode})");
            SetServer(fallbackIp, fallbackPort);
            onComplete?.Invoke(true);
        }
        else
        {
            Debug.LogWarning($"[ServerDiscovery] Fallback server not reachable: {req.error}");
            onComplete?.Invoke(false);
        }
    }

    private void SetServer(string ip, int port)
    {
        ServerUrl = $"http://{ip}:{port}";
        IsServerFound = true;
        _isDiscovering = false;
        Debug.Log($"[ServerDiscovery] Server found at: {ServerUrl}");
    }

    public void InvalidateServer()
    {
        Debug.LogWarning("[ServerDiscovery] Server connection invalidated. Resetting.");
        ServerUrl = null;
        IsServerFound = false;
        StartDiscovery();
    }
}
