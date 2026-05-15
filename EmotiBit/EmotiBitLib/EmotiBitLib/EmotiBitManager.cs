// EmotiBitManager.cs
using System;
using System.Collections.Generic;

namespace EmotiBit
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
    }

    public class EmotiBitManager
    {
        public static EmotiBitManager Instance { get; private set; }

        private EmotiBitWiFiHost emotiBitHost;
        private List<string> _receivedDataPackets = new List<string>();

        public IReadOnlyDictionary<string, EmotibitInfo> DiscoveredDevices => _discoveredDevices;
        private Dictionary<string, EmotibitInfo> _discoveredDevices = new Dictionary<string, EmotibitInfo>();
        private string _selectedDeviceId = "";

        // EmotiBit State
        public string PowerMode { get; private set; } = "";
        public bool IsRecording { get; private set; } = false;
        public string RecordingFilename { get; private set; } = "";
        public string BatteryLevel { get; private set; } = "?";
        public int DataClippingCount { get; private set; } = 0;
        public int DataOverflowCount { get; private set; } = 0;

        public event Action<DataTypes, double> OnScalarDataReceived;

        public double GetScalarData(DataTypes dataType)
        {
            return dataArray[(int)dataType];
        }

        // Data:
        private double[] dataArray = new double[Enum.GetNames(typeof(DataTypes)).Length];
        private Vector3 accelerometer = Vector3.zero;
        private Vector3 gyroscope = Vector3.zero;
        private Vector3 magnetometer = Vector3.zero;

        // Controls
        public bool AUTOCONNECT = false;

        public EmotiBitManager()
        {
            Instance = this;
            Initialize();
        }

        private void Initialize()
        {
            Debug.Log("Starting EmotiBit WiFi Host...");
            emotiBitHost = new EmotiBitWiFiHost();
            if (emotiBitHost.Begin() == EmotiBitWiFiHost.FAIL)
            {
                Debug.LogError("Failed to initialize EmotiBit WiFi Host.");
                emotiBitHost = null;
            }
        }

        public void Update()
        {
            if (emotiBitHost == null) return;

            // Pull data from the networking thread to the main Unity thread
            emotiBitHost.ReadData(ref _receivedDataPackets);
            if (_receivedDataPackets.Count > 0)
            {
                foreach (var packet in _receivedDataPackets)
                {
                    ProcessDataPacket(packet);
                }
            }

            // Periodically update the list of discovered devices
            _discoveredDevices = emotiBitHost.GetDiscoveredEmotibits();

            // Example: auto-connect to the first available device
            if (AUTOCONNECT && !IsConnected() && string.IsNullOrEmpty(_selectedDeviceId))
            {
                foreach (var device in _discoveredDevices)
                {
                    if (device.Value.IsAvailable)
                    {
                        _selectedDeviceId = device.Key;
                        Debug.Log($"Found available device {_selectedDeviceId}, attempting to connect.");
                        Connect(_selectedDeviceId);
                        break;
                    }
                }
            }
        }

        void ProcessDataPacket(string packet)
        {
            var splitPacket = new List<string>(packet.Split(EmotiBitPacket.PAYLOAD_DELIMITER));
            EmotiBitPacket.Header packetHeader;

            if (EmotiBitPacket.GetHeader(splitPacket, out packetHeader))
            {
                // Note: This is a simplified version of processSlowResponseMessage
                // It focuses on non-plotting data handling.
                var data = splitPacket[EmotiBitPacket.headerLength];
                switch (packetHeader.typeTag)
                {
                    case EmotiBitPacket.TypeTag.BATTERY_VOLTAGE:
                        BatteryLevel = data + "V";
                        break;
                    case EmotiBitPacket.TypeTag.BATTERY_PERCENT:
                        BatteryLevel = data + "%";
                        break;
                    case EmotiBitPacket.TypeTag.EMOTIBIT_MODE:
                        ProcessModePacket(splitPacket);
                        break;
                    case EmotiBitPacket.TypeTag.DATA_CLIPPING:
                        DataClippingCount++;
                        break;
                    case EmotiBitPacket.TypeTag.DATA_OVERFLOW:
                        DataOverflowCount++;
                        break;
                    case EmotiBitPacket.TypeTag.RESET:
                        IsRecording = false;
                        RecordingFilename = "";
                        break;
                    // Handle data received:
                    // Independent scalar data types:
                    case EmotiBitPacket.TypeTag.EDA:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.EDA] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.EDA, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.EDL:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.EDL] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.EDL, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.EDR:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.EDR] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.EDR, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.PPG_INFRARED:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.PPG_INFRARED] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.PPG_INFRARED, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.PPG_RED:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.PPG_RED] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.PPG_RED, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.PPG_GREEN:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.PPG_GREEN] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.PPG_GREEN, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.SPO2:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.SPO2] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.SPO2, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.TEMPERATURE_0:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.TEMPERATURE_0] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.TEMPERATURE_0, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.TEMPERATURE_1:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.TEMPERATURE_1] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.TEMPERATURE_1, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.THERMOPILE:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.THERMOPILE] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.THERMOPILE, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.HUMIDITY_0:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.HUMIDITY] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.HUMIDITY, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.HEART_RATE:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.HEART_RATE] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.HEART_RATE, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.INTER_BEAT_INTERVAL:
                        {
                            double value = Convert.ToDouble(data);
                            dataArray[(int)DataTypes.INTER_BEAT_INTERVAL] = value;
                            OnScalarDataReceived?.Invoke(DataTypes.INTER_BEAT_INTERVAL, value);
                        }
                        break;
                    case EmotiBitPacket.TypeTag.SKIN_CONDUCTANCE_RESPONSE_AMPLITUDE:
                        dataArray[(int)DataTypes.SKIN_CONDUCTANCE_RESPONSE_AMPLITUDE] = Convert.ToDouble(data);
                        break;
                    case EmotiBitPacket.TypeTag.SKIN_CONDUCTANCE_RESPONSE_RISE_TIME:
                        dataArray[(int)DataTypes.SKIN_CONDUCTANCE_RESPONSE_RISE_TIME] = Convert.ToDouble(data);
                        break;
                    case EmotiBitPacket.TypeTag.SKIN_CONDUCTANCE_RESPONSE_FREQ:
                        // No specific type for SF yet, so just ignore it or log it gracefully if needed, but doing nothing removes unhandled data error
                        break;
                    case EmotiBitPacket.TypeTag.REQUEST_DATA:
                        // Do nothing, just ignore it so it isn't unhandled
                        break;
                    case EmotiBitPacket.TypeTag.ACCELEROMETER_X:
                        accelerometer.x = (float)Convert.ToDouble(data);
                        break;
                    case EmotiBitPacket.TypeTag.ACCELEROMETER_Y:
                        accelerometer.y = (float)Convert.ToDouble(data);
                        break;
                    case EmotiBitPacket.TypeTag.ACCELEROMETER_Z:
                        accelerometer.z = (float)Convert.ToDouble(data);
                        break;
                    case EmotiBitPacket.TypeTag.GYROSCOPE_X:
                        gyroscope.x = (float)Convert.ToDouble(data);
                        break;
                    case EmotiBitPacket.TypeTag.GYROSCOPE_Y:
                        gyroscope.y = (float)Convert.ToDouble(data);
                        break;
                    case EmotiBitPacket.TypeTag.GYROSCOPE_Z:
                        gyroscope.z = (float)Convert.ToDouble(data);
                        break;
                    case EmotiBitPacket.TypeTag.MAGNETOMETER_X:
                        magnetometer.x = (float)Convert.ToDouble(data);
                        break;
                    case EmotiBitPacket.TypeTag.MAGNETOMETER_Y:
                        magnetometer.y = (float)Convert.ToDouble(data);
                        break;
                    case EmotiBitPacket.TypeTag.MAGNETOMETER_Z:
                        magnetometer.z = (float)Convert.ToDouble(data);
                        break;
                    default:
                        Debug.Log($"Received Unhandled Data: {packet}");
                        break;
                }
            }
            else
            {
                Debug.LogWarning($"Malformed packet received: {packet}");
            }
        }

        public double GetData(DataTypes dataType)
        {
            return dataArray[(int)dataType];
        }

        public Vector3 GetAccelerometer()
        {
            return accelerometer;
        }

        public Vector3 GetGyroscope()
        {
            return gyroscope;
        }
        
        public Vector3 GetMagnetometer()
        {
            return magnetometer;
        }

        void ProcessModePacket(List<string> splitPacket)
        {
            string value;
            int pos = EmotiBitPacket.GetPacketKeyedValue(splitPacket, EmotiBitPacket.PayloadLabel.RECORDING_STATUS, out value);
            if (pos > -1)
            {
                if (value.Equals(EmotiBitPacket.TypeTag.RECORD_BEGIN))
                {
                    IsRecording = true;
                    if (pos + 1 < splitPacket.Count)
                    {
                        string filename = splitPacket[pos + 1];
                        RecordingFilename = filename.EndsWith(".csv") ? filename : "Recording...";
                    }
                }
                else if (value.Equals(EmotiBitPacket.TypeTag.RECORD_END))
                {
                    IsRecording = false;
                    RecordingFilename = "";
                }
            }

            if (EmotiBitPacket.GetPacketKeyedValue(splitPacket, EmotiBitPacket.PayloadLabel.POWER_STATUS, out value) > -1)
            {
                PowerMode = value;
                if (PowerMode.Equals(EmotiBitPacket.TypeTag.MODE_WIRELESS_OFF) || PowerMode.Equals(EmotiBitPacket.TypeTag.MODE_HIBERNATE))
                {
                    Disconnect();
                }
            }
        }

        public bool IsConnected()
        {
            return emotiBitHost != null && emotiBitHost.IsConnected();
        }

        public string GetConnectedDeviceIdentifier()
        {
            return emotiBitHost?.GetConnectedDeviceIdentifier() ?? "";
        }

        public void Connect(string deviceId)
        {
            if (emotiBitHost == null)
            {
                Debug.Log("Host is null!");
                return;
            }
            if (IsConnected())
            {
                Debug.Log("Disconnecting first.");
                Disconnect();
            }
            _selectedDeviceId = deviceId;
            byte result = emotiBitHost.Connect(_selectedDeviceId);
            if (result == 0)
            {
                Debug.Log("Connection request sent...");
            }
            else
            {
                Debug.Log("Connection request failed!");
            }
        }

        public void Disconnect()
        {
            if (emotiBitHost == null) return;
            emotiBitHost.Disconnect();
            _selectedDeviceId = "";
        }

        void OnApplicationQuit()
        {
            Debug.Log("Shutting down EmotiBit WiFi Host.");
            emotiBitHost?.Dispose();
        }
    }
}