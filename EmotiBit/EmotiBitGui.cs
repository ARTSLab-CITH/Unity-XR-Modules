// EmotiBitGui.cs
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Linq;

namespace EmotiBit
{
    [RequireComponent(typeof(EmotiBitManager))]
    public class EmotiBitGui : MonoBehaviour
    {
        private EmotiBitManager _emotiBitManager;

        [Header("UI References")]
        public TextMeshProUGUI connectionStatusText;
        public TextMeshProUGUI deviceInfoText;
        public TextMeshProUGUI recordingStatusText;
        public TextMeshProUGUI statsText;
        public Dropdown deviceDropdown;
        public Toggle connectToggle;

        [Header("Oscilloscope")]
        public GameObject oscilloscopePrefab;
        public Transform oscilloscopeContainer;

        private Dictionary<DataTypes, ScalarOscilloscope> _oscilloscopes = new Dictionary<DataTypes, ScalarOscilloscope>();
        private List<CustomOscilloscope> _customOscilloscopes = new List<CustomOscilloscope>();
        private Dictionary<DataTypes, List<double>> _dataBuffers = new Dictionary<DataTypes, List<double>>();

        public class CustomOscilloscope
        {
            public string Label;
            public List<DataTypes> Dependencies;
            public int WindowSize;
            public System.Func<Dictionary<DataTypes, double[]>, object> Transform;
            public ScalarOscilloscope Scope;
            public event System.Action<object> OnDataGenerated;

            public void InvokeOnDataGenerated(object data)
            {
                OnDataGenerated?.Invoke(data);
            }
        }

        private List<string> _discoveredDeviceIds = new List<string>();
        private bool _isUpdatingUI = false;

        public List<DataTypes> DisplayedDataTypes = new List<DataTypes>
        {
            DataTypes.EDA,
            DataTypes.EDL,

            DataTypes.PPG_INFRARED,
            DataTypes.PPG_RED,
            DataTypes.PPG_GREEN,

            DataTypes.TEMPERATURE_1,
            DataTypes.THERMOPILE,
    
            // these are calculated on board:
            DataTypes.HEART_RATE,
            DataTypes.INTER_BEAT_INTERVAL,

            DataTypes.SKIN_CONDUCTANCE_RESPONSE_AMPLITUDE,
            DataTypes.SKIN_CONDUCTANCE_RESPONSE_FREQ,
            DataTypes.SKIN_CONDUCTANCE_RESPONSE_RISE_TIME,
        };

        void Start()
        {
            _emotiBitManager = GetComponent<EmotiBitManager>();
            if (_emotiBitManager == null)
            {
                Debug.LogError("EmotiBitManager component not found!");
                enabled = false;
                return;
            }

            connectToggle.onValueChanged.AddListener(OnConnectToggleChanged);
            _emotiBitManager.OnScalarDataReceived += OnScalarDataReceived;
            CreateOscilloscopes();

            // Test brainflow:
            Debug.Log("Testing BrainFlow DataFilter...");
            double stddev = brainflow.DataFilter.calc_stddev(new double[]{0, 1, 2}, 0, 2);
            Debug.Log($"BrainFlow DataFilter calc_stddev result: {stddev}");
        }

        void OnDestroy()
        {
            if (_emotiBitManager != null)
            {
                _emotiBitManager.OnScalarDataReceived -= OnScalarDataReceived;
            }
        }

        void Update()
        {
            if (_emotiBitManager == null) return;

            _isUpdatingUI = true;
            UpdateConnectionStatus();
            UpdateDeviceInfo();
            UpdateDiscoveredDevices();
            _isUpdatingUI = false;
        }

        void OnScalarDataReceived(DataTypes dataType, double value)
        {
            // Add new value to buffer
            if (!_dataBuffers.ContainsKey(dataType))
            {
                _dataBuffers[dataType] = new List<double>();
            }
            _dataBuffers[dataType].Add(value);

            // Keep buffer size in check (optional, depends on memory constraints)
            // For now, we let custom scopes manage the data they need

            if (_oscilloscopes.TryGetValue(dataType, out ScalarOscilloscope scope))
            {
                scope.AddDataPoint((float)value);
            }

            UpdateCustomOscilloscopes(dataType);
        }

        void CreateOscilloscopes()
        {
            if (oscilloscopePrefab == null || oscilloscopeContainer == null)
            {
                Debug.LogWarning("Oscilloscope prefab or container not set.");
                return;
            }

            foreach (DataTypes dataType in DisplayedDataTypes)
            {
                GameObject instance = Instantiate(oscilloscopePrefab, oscilloscopeContainer);
                ScalarOscilloscope scope = instance.GetComponent<ScalarOscilloscope>();
                if (scope != null)
                {
                    scope.SetLabel(dataType.ToString());
                    _oscilloscopes.Add(dataType, scope);
                }
                else
                {
                    Debug.LogError($"Oscilloscope prefab does not have a ScalarOscilloscope component.");
                    Destroy(instance);
                }
            }

            DefineCustomOscilloscopes();
        }

        public CustomOscilloscope GetCustomOscilloscope(string label)
        {
            return _customOscilloscopes.FirstOrDefault(s => s.Label == label);
        }

        void DefineCustomOscilloscopes()
        {
            // Example 1: Ratio of latest values (simple transform)
            _customOscilloscopes.Add(new CustomOscilloscope
            {
                Label = "EDA / EDL Ratio",
                Dependencies = new List<DataTypes> { DataTypes.EDA, DataTypes.EDL },
                WindowSize = 10,
                Transform = (data) =>
                {
                    double edaAvg = data[DataTypes.EDA].Average();
                    double edlAvg = data[DataTypes.EDL].Average();
                    return edlAvg != 0 ? edaAvg / edlAvg : 0;
                }
            });

            // Example 2: Signal processing on a window of data
            _customOscilloscopes.Add(new CustomOscilloscope
            {
                Label = "EDA StdDev (50 samples)",
                Dependencies = new List<DataTypes> { DataTypes.EDA },
                WindowSize = 50,
                Transform = (data) =>
                {
                    return brainflow.DataFilter.calc_stddev(data[DataTypes.EDA], 0, data[DataTypes.EDA].Length-1);
                }
            });

            // Example 3: Transform returning a double[]
            // _customOscilloscopes.Add(new CustomOscilloscope
            // {
            //     Label = "EDA Lowpass",
            //     Dependencies = new List<DataTypes> { DataTypes.EDA },
            //     WindowSize = 250, // Window size for filtering
            //     Transform = (data) =>
            //     {
            //         // This is a placeholder for the actual sampling rate.
            //         // You might need to get this from EmotiBitManager.
            //         int samplingRate = 15;
            //         double cutoff = 5.0;
            //         int order = 4;
            //         int filterType = (int)brainflow.FilterTypes.BUTTERWORTH;
            //         double ripple = 0;

            //         // Perform lowpass filter
            //         return brainflow.DataFilter.perform_lowpass(data[DataTypes.EDA], samplingRate, cutoff, order, filterType, ripple);
            //     }
            // });
            _customOscilloscopes.Add(new CustomOscilloscope
            {
                Label = "Pulse",
                Dependencies = new List<DataTypes> { DataTypes.PPG_GREEN },
                WindowSize = 256, // Window size for filtering
                Transform = (data) =>
                {
                    // This is a placeholder for the actual sampling rate.
                    // You might need to get this from EmotiBitManager.
                    int samplingRate = 25;
                    int order = 4;
                    int filterType = (int)brainflow.FilterTypes.BUTTERWORTH;
                    double ripple = 0;

                    // Perform bandpass filter
                    var result = brainflow.DataFilter.perform_bandpass(data[DataTypes.PPG_GREEN], samplingRate, 0.5, 4.0, order, filterType, ripple);
                    return result;
                }
            });

            // Create the UI for custom oscilloscopes
            foreach (var customScope in _customOscilloscopes)
            {
                if (oscilloscopePrefab != null && oscilloscopeContainer != null)
                {
                    GameObject instance = Instantiate(oscilloscopePrefab, oscilloscopeContainer);
                    customScope.Scope = instance.GetComponent<ScalarOscilloscope>();
                    if (customScope.Scope != null)
                    {
                        customScope.Scope.SetLabel(customScope.Label);
                    }
                    else
                    {
                        Debug.LogError($"Oscilloscope prefab does not have a ScalarOscilloscope component.");
                        Destroy(instance);
                    }
                }
            }
        }

        void UpdateCustomOscilloscopes(DataTypes updatedDataType)
        {
            foreach (var customScope in _customOscilloscopes)
            {
                if (customScope.Dependencies.Contains(updatedDataType))
                {
                    // Check if we have enough data in all dependency buffers
                    bool allDependenciesMet = true;
                    foreach (var dependency in customScope.Dependencies)
                    {
                        if (!_dataBuffers.ContainsKey(dependency) || _dataBuffers[dependency].Count < customScope.WindowSize)
                        {
                            Debug.Log($"Not enough data for {customScope.Label}: {dependency} has {_dataBuffers.GetValueOrDefault(dependency)?.Count ?? 0} samples, needs {customScope.WindowSize}");
                            allDependenciesMet = false;
                            break;
                        }
                    }

                    if (allDependenciesMet)
                    {
                        // Prepare data for the transform function
                        var transformData = new Dictionary<DataTypes, double[]>();
                        foreach (var dependency in customScope.Dependencies)
                        {
                            var buffer = _dataBuffers[dependency];
                            // Get the last 'WindowSize' samples
                            transformData[dependency] = buffer.GetRange(buffer.Count - customScope.WindowSize, customScope.WindowSize).ToArray();
                        }

                        var result = customScope.Transform(transformData);
                        customScope.InvokeOnDataGenerated(result);

                        if (result is double)
                        {
                            customScope.Scope.AddDataPoint((float)(double)result);
                        }
                        else if (result is double[] values)
                        {
                            // If the transform returns an array, add all points.
                            // Note: This might be a lot of data at once for the visualizer.
                            // Consider downsampling or averaging if performance is an issue.
                            foreach (var val in values)
                            {
                                customScope.Scope.AddDataPoint((float)val);
                            }
                        }
                    }
                }
            }
        }

        void UpdateOscilloscopes()
        {
            
        }

        void UpdateConnectionStatus()
        {
            if (connectionStatusText == null) return;

            bool isConnected = _emotiBitManager.IsConnected();

            if (isConnected)
            {
                connectionStatusText.text = $"Status: Connected to {_emotiBitManager.GetConnectedDeviceIdentifier()}";
            }
            else
            {
                connectionStatusText.text = "Status: Disconnected";
            }

            if (connectToggle.isOn != isConnected)
            {
                connectToggle.isOn = isConnected;
            }

            if (deviceDropdown != null)
            {
                deviceDropdown.interactable = !isConnected;
            }
        }

        void UpdateDeviceInfo()
        {
            if (deviceInfoText != null)
            {
                deviceInfoText.text = $"Battery: {_emotiBitManager.BatteryLevel} | Power Mode: {_emotiBitManager.PowerMode}";
            }

            if (recordingStatusText != null)
            {
                recordingStatusText.text = _emotiBitManager.IsRecording ? $"Recording to {_emotiBitManager.RecordingFilename}" : "Not Recording";
            }

            if (statsText != null)
            {
                statsText.text = $"Clipping: {_emotiBitManager.DataClippingCount} | Overflow: {_emotiBitManager.DataOverflowCount}";
            }
        }

        void UpdateDiscoveredDevices()
        {
            if (deviceDropdown == null) return;

            var discoveredDevices = _emotiBitManager.DiscoveredDevices;
            var newDeviceIds = discoveredDevices
                .Where(d => d.Value.IsAvailable)
                .Select(d => d.Key) // Using IP as a stable identifier for the dropdown
                .ToList();

            if (!newDeviceIds.SequenceEqual(_discoveredDeviceIds))
            {
                _discoveredDeviceIds = newDeviceIds;

                string previouslySelectedDevice = null;
                if (deviceDropdown.options.Count > deviceDropdown.value && deviceDropdown.value >= 0)
                {
                    previouslySelectedDevice = deviceDropdown.options[deviceDropdown.value].text;
                }

                deviceDropdown.ClearOptions();
                deviceDropdown.AddOptions(_discoveredDeviceIds);

                if (previouslySelectedDevice != null && _discoveredDeviceIds.Contains(previouslySelectedDevice))
                {
                    deviceDropdown.value = _discoveredDeviceIds.IndexOf(previouslySelectedDevice);
                }
            }

            // If connected, ensure the dropdown shows the connected device
            if (_emotiBitManager.IsConnected())
            {
                string connectedId = _emotiBitManager.GetConnectedDeviceIdentifier();
                if (_discoveredDeviceIds.Contains(connectedId))
                {
                    int connectedIndex = _discoveredDeviceIds.IndexOf(connectedId);
                    if (deviceDropdown.value != connectedIndex)
                    {
                        deviceDropdown.value = connectedIndex;
                    }
                }
            }
        }

        public void OnConnectToggleChanged(bool isOn)
        {
            if (_isUpdatingUI) return;

            if (isOn)
            {
                if (deviceDropdown.options.Count > 0)
                {
                    string deviceId = deviceDropdown.options[deviceDropdown.value].text;
                    Debug.Log($"GUI: Connecting to {deviceId}");
                    _emotiBitManager.Connect(deviceId);
                }
                else
                {
                    Debug.LogWarning("GUI: No devices available to connect.");
                    connectToggle.isOn = false; // Revert toggle state
                }
            }
            else
            {
                Debug.Log("GUI: Disconnecting.");
                _emotiBitManager.Disconnect();
            }
        }
    }
}