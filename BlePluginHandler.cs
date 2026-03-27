using UnityEngine;

using UnityEngine.Events; // Required for UnityEvents

// UnityEvent classes to create customizable callbacks in the Inspector
[System.Serializable]
public class DeviceFoundEvent : UnityEvent<string> { }

[System.Serializable]
public class ConnectionStateChangeEvent : UnityEvent<string> { }

[System.Serializable]
public class DataReceivedEvent : UnityEvent<string> { }

public class BlePluginHandler : MonoBehaviour
{
    // --- Public events for easy wiring in the Unity Editor ---
    public DeviceFoundEvent OnDeviceFoundEvent;
    public ConnectionStateChangeEvent OnConnectedEvent;
    public ConnectionStateChangeEvent OnDisconnectedEvent;
    public DataReceivedEvent OnDataReceivedEvent;

    // A constant to hold the package name and class of your plugin
    private const string PluginName = "com.example.bleplugin.BlePlugin";

    // A static reference to your BlePlugin class
    private static AndroidJavaClass _blePluginClass;

    // A static reference to the current Android activity
    private static AndroidJavaObject _currentActivity;

    void Start()
    {
        // Only run this on the Android platform
        if (Application.platform == RuntimePlatform.Android)
        {
            // Find the BlePlugin class
            _blePluginClass = new AndroidJavaClass(PluginName);

            // Get the current activity from the UnityPlayer
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }

            // Call the initialize method, passing the current activity
            Initialize();
            StartScan();
        }
        else
        {
            Debug.Log("BLE Plugin can only be used on Android.");
        }
    }

    /// <summary>
    /// Calls the static 'initialize' method on the BlePlugin object.
    /// </summary>
    private void Initialize()
    {
        // Check if the plugin class and activity are available
        if (_blePluginClass != null && _currentActivity != null)
        {
            // The 'activity' object is passed as an argument
            _blePluginClass.CallStatic("initialize", _currentActivity);
            Debug.Log("BLE Plugin Initialized");
        }
    }

    /// <summary>
    /// Calls the static 'startScan' method on the BlePlugin object.
    /// </summary>
    public void StartScan()
    {
        _blePluginClass?.CallStatic("startScan");
        Debug.Log("BLE Scan Started");
    }

    /// <summary>
    /// Calls the static 'stopScan' method on the BlePlugin object.
    /// </summary>
    public void StopScan()
    {
        _blePluginClass?.CallStatic("stopScan");
        Debug.Log("BLE Scan Stopped");
    }

    /// <summary>
    /// Calls the static 'connect' method on the BlePlugin object.
    /// </summary>
    /// <param name="address">The Bluetooth device address to connect to.</param>
    public void Connect(string address)
    {
        _blePluginClass?.CallStatic("connect", address);
        Debug.Log($"Attempting to connect to: {address}");
    }

    /// <summary>
    /// Calls the static 'disconnect' method on the BlePlugin object.
    /// </summary>
    public void Disconnect()
    {
        _blePluginClass?.CallStatic("disconnect");
        Debug.Log("Attempting to disconnect");
    }

    // This method is called by the Android plugin when a BLE device is found.
    // The name 'OnDeviceFound' must match the second argument of UnitySendMessage.
    public void OnDeviceFound(string deviceAddress)
    {
        Debug.Log($"[BlePluginCallback] Device Found: {deviceAddress}");
        // Invoke the event, which can be hooked up to other scripts in the Inspector.
        OnDeviceFoundEvent.Invoke(deviceAddress);
    }

    // Called when a connection is successfully established.
    public void OnConnected(string deviceAddress)
    {
        Debug.Log($"[BlePluginCallback] Connected to: {deviceAddress}");
        OnConnectedEvent.Invoke(deviceAddress);
    }

    // Called when a device disconnects.
    public void OnDisconnected(string deviceAddress)
    {
        Debug.Log($"[BlePluginCallback] Disconnected from: {deviceAddress}");
        OnDisconnectedEvent.Invoke(deviceAddress);
    }

    // Called when new data is received from a characteristic.
    public void OnDataReceived(string data)
    {
        Debug.Log($"[BlePluginCallback] Data Received: {data}");
        OnDataReceivedEvent.Invoke(data);
    }

    // It's good practice to dispose of the Java objects when they are no longer needed
    void OnDestroy()
    {
        _blePluginClass?.Dispose();
        _currentActivity?.Dispose();
    }
}
