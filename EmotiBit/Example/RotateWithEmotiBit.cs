using UnityEngine;
using EmotiBit;

public class RotateWithEmotiBit : MonoBehaviour
{
    private EmotiBitManager emotiBitManager;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        emotiBitManager = EmotiBitManager.Instance;
    }

    // Update is called once per frame
    void Update()
    {
        if (emotiBitManager != null)
        {
            // Use emotiBitManager to get data and rotate the object
            // unsure if this is in radians or degrees:
            // Convert radians to degrees before creating Euler rotation
            transform.Rotate(emotiBitManager.GetGyroscope() * .01f);
        }
    }
}
