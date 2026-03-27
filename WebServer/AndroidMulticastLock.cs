using UnityEngine;

public class AndroidMulticastLock : MonoBehaviour
{
#if UNITY_ANDROID //&& !UNITY_EDITOR
    // --- Android Java objects ---
    private AndroidJavaObject wifiManager;
    private AndroidJavaObject multicastLock;

    void Awake()
    {
        // This script is only needed on Android devices.
        // Get the Wi-Fi manager from the Android context.
        AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        AndroidJavaObject context = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        wifiManager = context.Call<AndroidJavaObject>("getSystemService", "wifi");
    }

    void OnEnable()
    {
        // Called when the object becomes enabled and active.
        AcquireMulticastLock();
    }

    void OnDisable()
    {
        // Called when the object becomes disabled or inactive.
        ReleaseMulticastLock();
    }

    private void AcquireMulticastLock()
    {
        if (multicastLock == null)
        {
            // Create a new multicast lock.
            // The string "MyMulticastLock" is a tag for debugging purposes.
            multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "MyMulticastLock");
            multicastLock.Call("setReferenceCounted", true);
        }

        // Acquire the lock. This tells the OS to allow multicast packets.
        multicastLock.Call("acquire");
        Debug.Log("AndroidMulticastLock: Lock acquired.");
    }

    private void ReleaseMulticastLock()
    {
        if (multicastLock != null && multicastLock.Call<bool>("isHeld"))
        {
            // Release the lock.
            multicastLock.Call("release");
            Debug.Log("AndroidMulticastLock: Lock released.");
        }
    }
#endif
}
