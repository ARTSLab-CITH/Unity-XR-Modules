using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Auto-starts <see cref="NetworkManager"/> as server or client based on the
/// <c>IS_SERVER</c> scripting define symbol.
///
/// Lives on the <b>ServerManager</b> GameObject alongside NetworkManager.
/// Add <c>IS_SERVER</c> to Project Settings → Player → Scripting Define Symbols
/// for the server build only.
///
/// <b>Client path:</b> waits for <see cref="ServerDiscovery"/> to find the
/// Centroid web server, then uses the same IP address (the game server runs
/// on the same machine) to configure <see cref="UnityTransport"/> before
/// calling <c>StartClient()</c>.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class NetworkBootstrap : MonoBehaviour
{
    [Header("Netcode Game Server Port")]
    [Tooltip("The port UnityTransport listens on / connects to. Must match on server and clients.")]
    [SerializeField] private ushort netcodePort = 7777;

    private void Start()
    {
        var nm = GetComponent<NetworkManager>();
        if (nm == null)
        {
            Debug.LogError("[NetworkBootstrap] NetworkManager not found on this GameObject.");
            return;
        }

#if IS_SERVER
        // Server: bind to 0.0.0.0 so any client on the LAN can connect
        var transport = nm.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData("0.0.0.0", netcodePort);
            Debug.Log($"[NetworkBootstrap] IS_SERVER — binding to 0.0.0.0:{netcodePort}");
        }

        nm.StartServer();
        Debug.Log($"[NetworkBootstrap] StartServer() called. IsServer={nm.IsServer}, IsListening={nm.IsListening}");
        nm.OnClientConnectedCallback += (id) => Debug.Log($"[NetworkBootstrap] Client connected! ClientId={id}");
        nm.OnClientDisconnectCallback += (id) => Debug.Log($"[NetworkBootstrap] Client disconnected! ClientId={id}");
#else
        // Client: must discover the game server IP first
        Debug.Log("[NetworkBootstrap] Client mode — waiting for ServerDiscovery before connecting...");
        StartCoroutine(StartClientWhenServerFound(nm));
#endif
    }

#if !IS_SERVER
    private IEnumerator StartClientWhenServerFound(NetworkManager nm)
    {
        // Wait for mDNS to locate the web server (same machine as the game server)
        float waitStart = Time.realtimeSinceStartup;
        while (!ServerDiscovery.IsServerFound)
        {
            if ((Time.realtimeSinceStartup - waitStart) % 5f < 0.6f)
            {
                Debug.Log($"[NetworkBootstrap] Still waiting for ServerDiscovery... ({Time.realtimeSinceStartup - waitStart:F0}s)");
            }
            yield return new WaitForSeconds(0.5f);
        }

        // Extract the IP from ServerDiscovery.ServerUrl (e.g. "http://192.168.1.38:8000")
        string serverIp = ExtractIpFromUrl(ServerDiscovery.ServerUrl);
        if (string.IsNullOrEmpty(serverIp))
        {
            Debug.LogError($"[NetworkBootstrap] Could not extract IP from ServerUrl: {ServerDiscovery.ServerUrl}");
            yield break;
        }

        // Configure UnityTransport to connect to the game server
        var transport = nm.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData(serverIp, netcodePort);
            Debug.Log($"[NetworkBootstrap] UnityTransport configured: {serverIp}:{netcodePort}");
        }
        else
        {
            Debug.LogError("[NetworkBootstrap] UnityTransport component not found — cannot set connection data.");
        }

        Debug.Log($"[NetworkBootstrap] Starting as Client → {serverIp}:{netcodePort}");
        nm.StartClient();
        Debug.Log($"[NetworkBootstrap] StartClient() called. IsClient={nm.IsClient}, IsListening={nm.IsListening}");

        nm.OnClientConnectedCallback += (id) => Debug.Log($"[NetworkBootstrap] Successfully connected to server! ClientId={id}");
        nm.OnClientDisconnectCallback += (id) => Debug.LogWarning($"[NetworkBootstrap] Disconnected from server. ClientId={id} | Reason: {nm.DisconnectReason}");
    }

    /// <summary>
    /// Extracts the host/IP from an HTTP URL.
    /// E.g. "http://192.168.1.38:8000" → "192.168.1.38"
    /// </summary>
    private static string ExtractIpFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        try
        {
            var uri = new System.Uri(url);
            return uri.Host;
        }
        catch
        {
            Debug.LogError($"[NetworkBootstrap] Failed to parse URL: {url}");
            return null;
        }
    }
#endif
}
