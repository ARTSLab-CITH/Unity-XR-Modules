// EmotiBitWifiHost.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Unity.XR.CoreUtils;
using Debug = UnityEngine.Debug;

namespace EmotiBit
{
    public class EmotiBitWiFiHost : IDisposable
    {
        #region Settings and Constants
        public class WifiHostSettings
        {
            public int SendAdvertisingInterval = 1000;
            public int CheckAdvertisingInterval = 100;
            public int AdvertisingThreadSleep = 0;
            public int DataThreadSleep = 0;
            public bool EnableBroadcast = true;
            public bool EnableUnicast = true;
            public Tuple<int, int> UnicastIpRange = new Tuple<int, int>(2, 254);
            public int NUnicastIpsPerLoop = 1;
            public int UnicastMinLoopDelay = 3;
            public List<string> NetworkIncludeList = new List<string> { "*.*.*.*" };
            public List<string> NetworkExcludeList = new List<string> { "" };
        }

        public WifiHostSettings _wifiHostSettings = new WifiHostSettings();

        public const byte SUCCESS = 0;
        public const byte FAIL = 255; // Using 255 for -1 byte

        public ushort startCxnInterval = 200;
        public ushort startCxnTimeout = 5000;
        public ushort connectionTimeout = 10000;
        public ushort availabilityTimeout = 5000;
        public ushort pingInterval = 500;
        #endregion

        #region Private Members
        private UdpClient advertisingCxn;
        private UdpClient dataCxn;
        private TcpListener controlCxn;

        private Thread dataThread;
        private Thread advertisingThread;
        private Thread controlThread;

        private readonly object controlCxnLock = new object();
        private readonly object dataCxnLock = new object();
        private readonly object discoveredEmotibitsLock = new object();

        private List<TcpClient> controlClients = new List<TcpClient>();
        private List<NetworkStream> controlClientStreams = new List<NetworkStream>();
        private int lastId = -1;

        private ushort advertisingPort;
        private ushort _dataPort;
        private ushort controlPort;

        private List<string> availableNetworks = new List<string>();
        private List<string> emotibitNetworks = new List<string>();

        private Dictionary<string, EmotibitInfo> _discoveredEmotibits = new Dictionary<string, EmotibitInfo>();
        private string connectedEmotibitIp = "";
        private string connectedEmotibitIdentifier = "";
        private bool _isConnected = false;
        private bool isStartingConnection = false;

        private long startCxnAbortTimer;
        private long connectionTimer;

        private uint advertisingPacketCounter = 0;
        private uint controlPacketCounter = 0;
        private uint dataPacketCounter = 0;
        // private uint receivedDataPacketNumber = 0;
        // private int sendDataPort = -1;

        private DoubleBuffer<string> dataPackets = new DoubleBuffer<string>();

        private volatile bool stopThreads = false;

        private Stopwatch stopwatch = new Stopwatch();
        #endregion

        public bool IsConnected() => _isConnected;

        public EmotiBitWiFiHost()
        {
            stopwatch.Start();
        }

        public byte Begin()
        {
            advertisingPort = EmotiBitComms.WIFI_ADVERTISING_PORT;
            GetAvailableNetworks();
            if (availableNetworks.Count == 0)
            {
                Debug.LogWarning("No available networks found. Check network adapters.");
                return FAIL;
            }

            // Create advertising connection without binding to a specific port
            // This allows the OS to assign an ephemeral port for receiving responses
            advertisingCxn = new UdpClient();
            advertisingCxn.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            advertisingCxn.EnableBroadcast = true;
            // DO NOT bind to advertisingPort - let OS assign ephemeral port for 2-way communication
            // advertisingCxn.Client.Bind(new IPEndPoint(IPAddress.Any, advertisingPort));

            StartDataCxn(EmotiBitComms.WIFI_ADVERTISING_PORT + 1);

            controlPort = (ushort)(_dataPort + 1);
            try
            {
                controlCxn = new TcpListener(IPAddress.Any, controlPort);
                controlCxn.Start();
                Debug.Log($"EmotiBit Control Port: {controlPort}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start TCP listener on port {controlPort}: {e.Message}");
                return FAIL;
            }

            Debug.Log($"EmotiBit Data Port: {_dataPort}");

            stopThreads = false;
            dataThread = new Thread(UpdateDataThread);
            advertisingThread = new Thread(ProcessAdvertisingThread);
            controlThread = new Thread(ControlConnectionThread);

            dataThread.Start();
            advertisingThread.Start();
            controlThread.Start();

            return SUCCESS;
        }

        private void StartDataCxn(ushort startPort)
        {
            _dataPort = startPort;
            while (true)
            {
                try
                {
                    dataCxn = new UdpClient(_dataPort);
                    dataCxn.Client.ReceiveBufferSize = (int)Math.Pow(2, 15);
                    break;
                }
                catch (SocketException)
                {
                    _dataPort += 2; // Try next port
                    Debug.Log($"Data port {(_dataPort - 2)} in use. Trying {_dataPort}");
                }
            }
        }

        private void FlushData()
        {
            lock (dataCxnLock)
            {
                while (dataCxn.Available > 0)
                {
                    var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    dataCxn.Receive(ref remoteEP);
                }
            }
        }

        #region Network Discovery and Advertising
        /// <summary>
        /// Finds active IPv4 networks on the device, with special considerations for Android.
        /// This method avoids filtering by NetworkInterfaceType, which is unreliable on Android,
        /// and instead filters by OperationalStatus.
        /// </summary>
        private void GetAvailableNetworks()
        {
            var currentAvailableNetworks = new List<string>(availableNetworks);
            availableNetworks.Clear(); // Clear the list to rebuild it with fresh data

            try
            {
                foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // On Android, NetworkInterfaceType is often 'Unknown'.
                    // A more reliable filter is to check if the interface is active ('Up').
                    if (netInterface.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    // Get the properties of this network interface.
                    var ipProperties = netInterface.GetIPProperties();
                    if (ipProperties == null) continue;

                    // Find a valid IPv4 address on this interface.
                    foreach (var addr in ipProperties.UnicastAddresses)
                    {
                        // We only care about IPv4 addresses.
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            string ipAddress = addr.Address.ToString();

                            // Skip loopback addresses (e.g., 127.0.0.1).
                            if (IPAddress.IsLoopback(addr.Address))
                            {
                                continue;
                            }

                            // Extract the network part of the IP (e.g., "192.168.1")
                            var ipParts = ipAddress.Split('.');
                            if (ipParts.Length != 4) continue;
                            string network = $"{ipParts[0]}.{ipParts[1]}.{ipParts[2]}";

                            // Add the network if it passes filters and isn't already in the list.
                            if (!availableNetworks.Contains(network) && IsInNetworkIncludeList(network) && !IsInNetworkExcludeList(network))
                            {
                                availableNetworks.Add(network);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not get network interfaces: {e.Message}");
            }


            // Log if the list of found networks has changed.
            if (!availableNetworks.SequenceEqual(currentAvailableNetworks))
            {
                if (availableNetworks.Count > 0)
                {
                    Debug.Log($"Updated Available Networks: {string.Join(", ", availableNetworks.Select(n => n + ".*"))}");
                }
                else
                {
                    Debug.LogWarning("No available networks found. Check WiFi or network connection.");
                }
            }
        }

        private bool IsInNetworkList(string ipAddress, List<string> networkList)
        {
            string[] ipSplit = ipAddress.Split('.');
            foreach (string listIp in networkList)
            {
                string[] listIpSplit = listIp.Split('.');
                bool partMatch = true;
                for (int i = 0; i < ipSplit.Length && i < listIpSplit.Length; i++)
                {
                    if (listIpSplit[i] == "*" || listIpSplit[i] == ipSplit[i])
                    {
                        // Match
                    }
                    else
                    {
                        partMatch = false;
                        break;
                    }
                }
                if (partMatch) return true;
            }
            return false;
        }

        private bool IsInNetworkIncludeList(string ipAddress) => IsInNetworkList(ipAddress, _wifiHostSettings.NetworkIncludeList);
        private bool IsInNetworkExcludeList(string ipAddress) => IsInNetworkList(ipAddress, _wifiHostSettings.NetworkExcludeList);

        private void ProcessAdvertisingThread()
        {
            long checkAdvertisingTimer = stopwatch.ElapsedMilliseconds;
            long sendAdvertisingTimer = stopwatch.ElapsedMilliseconds;

            while (!stopThreads)
            {
                if (stopwatch.ElapsedMilliseconds - sendAdvertisingTimer >= _wifiHostSettings.SendAdvertisingInterval)
                {
                    sendAdvertisingTimer = stopwatch.ElapsedMilliseconds;
                    SendAdvertising();
                }

                if (stopwatch.ElapsedMilliseconds - checkAdvertisingTimer >= _wifiHostSettings.CheckAdvertisingInterval)
                {
                    checkAdvertisingTimer = stopwatch.ElapsedMilliseconds;
                    ReceiveAdvertising();
                }

                UpdateConnectionStatus();

                ThreadSleepFor(_wifiHostSettings.AdvertisingThreadSleep);
            }
        }

        private void SendAdvertising()
        {
            // MISSING?
            string packet = EmotiBitPacket.CreatePacket(EmotiBitPacket.TypeTag.HELLO_EMOTIBIT, advertisingPacketCounter++);
            byte[] data = Encoding.UTF8.GetBytes(packet);

            if (_wifiHostSettings.EnableBroadcast)
            {
                foreach (var network in availableNetworks)
                {
                    try
                    {
                        IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Parse(network + ".255"), advertisingPort);
                        advertisingCxn.Send(data, data.Length, broadcastEndPoint);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Broadcast failed for {network}.*: {e.Message}");
                    }
                }
            }
        }

        private void ReceiveAdvertising()
        {
            while (advertisingCxn.Available > 0)
            {
                try
                {
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedBytes = advertisingCxn.Receive(ref remoteEP);
                    string message = Encoding.UTF8.GetString(receivedBytes).TrimEnd('\0');

                    string[] packets = message.Split(new[] { EmotiBitPacket.PACKET_DELIMITER_CSV }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var packet in packets)
                    {
                        EmotiBitPacket.Header header;
                        int dataStartChar = EmotiBitPacket.GetHeader(packet, out header);

                        if (dataStartChar == EmotiBitPacket.MALFORMED_HEADER)
                        {
                            Debug.LogWarning($"Malformed header! {packet}");
                            continue;
                        }

                        string ip = remoteEP.Address.ToString();

                        if (header.typeTag == EmotiBitPacket.TypeTag.HELLO_HOST)
                        {
                            // Debug.Log("hh");
                            string deviceId = ip; // Default to IP
                            string dataPort = "0";

                            if (dataStartChar != EmotiBitPacket.NO_PACKET_DATA)
                            {
                                string temp;
                                if (EmotiBitPacket.GetPacketKeyedValue(packet, EmotiBitPacket.PayloadLabel.DEVICE_ID, out temp, dataStartChar) > 0)
                                {
                                    deviceId = temp;
                                }
                                if (EmotiBitPacket.GetPacketKeyedValue(packet, EmotiBitPacket.PayloadLabel.DATA_PORT, out temp, dataStartChar) > 0)
                                {
                                    dataPort = temp;
                                }
                            }

                            lock (discoveredEmotibitsLock)
                            {
                                bool isAvailable = int.Parse(dataPort) == EmotiBitComms.EMOTIBIT_AVAILABLE;
                                if (_discoveredEmotibits.ContainsKey(deviceId))
                                {
                                    _discoveredEmotibits[deviceId].Ip = ip;
                                    _discoveredEmotibits[deviceId].IsAvailable = isAvailable;
                                    _discoveredEmotibits[deviceId].LastSeen = stopwatch.ElapsedMilliseconds;
                                }
                                else
                                {
                                    _discoveredEmotibits.Add(deviceId, new EmotibitInfo(ip, isAvailable, stopwatch.ElapsedMilliseconds));
                                }
                            }
                        }
                        else if (header.typeTag == EmotiBitPacket.TypeTag.PONG)
                        {
                            // Debug.Log($"PONG from {remoteEP.Address}: {packet}");
                            if (ip == connectedEmotibitIp)
                            {
                                // Validate that the PONG response contains the correct DATA_PORT
                                string dataPortValue = "";
                                int valuePos = EmotiBitPacket.GetPacketKeyedValue(packet, EmotiBitPacket.PayloadLabel.DATA_PORT, out dataPortValue, dataStartChar);
                                if (valuePos > -1 && int.Parse(dataPortValue) == _dataPort)
                                {
                                    // Establish / maintain connected status
                                    if (isStartingConnection)
                                    {
                                        FlushData();
                                        _isConnected = true;
                                        isStartingConnection = false;
                                        Debug.Log($"Connected to EmotiBit: {connectedEmotibitIdentifier}");
                                    }
                                    // if (_isConnected)
                                    // {
                                    //     connectionTimer = stopwatch.ElapsedMilliseconds;
                                    // }
                                }
                            }
                        }
                        else if (header.typeTag != EmotiBitPacket.TypeTag.HELLO_EMOTIBIT)
                        {
                            Debug.LogWarning($"Unrecognized advertising Header TypeTag: {header.typeTag}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error receiving advertising packet: {e.Message}");
                }
            }
        }

        private void UpdateConnectionStatus()
        {
            // Handle connection timeout
            if (_isConnected && (stopwatch.ElapsedMilliseconds - connectionTimer > connectionTimeout))
            {
                Debug.Log("Connection timed out.");
                Disconnect();
            }

            // Handle connection attempt timeout
            if (isStartingConnection)
            {
                if (stopwatch.ElapsedMilliseconds - startCxnAbortTimer > startCxnTimeout)
                {
                    Debug.LogWarning("Connection attempt timed out.");
                    isStartingConnection = false;
                    connectedEmotibitIp = "";
                    connectedEmotibitIdentifier = "";
                }
                else if (stopwatch.ElapsedMilliseconds - connectionTimer > startCxnInterval) // Simple periodic check to resend connect packet
                {
                    var payload = new List<string> { EmotiBitPacket.PayloadLabel.CONTROL_PORT, controlPort.ToString(), EmotiBitPacket.PayloadLabel.DATA_PORT, _dataPort.ToString() };
                    string packet = EmotiBitPacket.CreatePacket(EmotiBitPacket.TypeTag.EMOTIBIT_CONNECT, advertisingPacketCounter++, payload);
                    byte[] data = Encoding.UTF8.GetBytes(packet);
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(connectedEmotibitIp), advertisingPort);
                    advertisingCxn.Send(data, data.Length, remoteEP);
                    connectionTimer = stopwatch.ElapsedMilliseconds;
                    Debug.Log("attempt connect: " + connectedEmotibitIp);
                }
            }

            // Ping connected device
            if (_isConnected && (stopwatch.ElapsedMilliseconds - connectionTimer > pingInterval))
            {
                var payload = new List<string> { EmotiBitPacket.PayloadLabel.DATA_PORT, _dataPort.ToString() };
                string packet = EmotiBitPacket.CreatePacket(EmotiBitPacket.TypeTag.PING, advertisingPacketCounter++, payload);
                byte[] data = Encoding.UTF8.GetBytes(packet);
                try
                {
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(connectedEmotibitIp), advertisingPort);
                    advertisingCxn.Send(data, data.Length, remoteEP);
                    connectionTimer = stopwatch.ElapsedMilliseconds;
                    // Debug.Log("Pinged: " + connectedEmotibitIp);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Could not send PING packet: {e.Message}");
                }
            }

            // Prune stale devices
            lock (discoveredEmotibitsLock)
            {
                var toRemove = new List<string>();
                foreach (var entry in _discoveredEmotibits)
                {
                    if (entry.Value.IsAvailable && stopwatch.ElapsedMilliseconds - entry.Value.LastSeen > availabilityTimeout)
                    {
                        Debug.Log($"Unavailable: {entry.Key}");
                        entry.Value.IsAvailable = false;
                    }
                    // Optionally add a longer purge timeout here
                }
            }
        }
        #endregion

        #region Data Handling
        private void UpdateDataThread()
        {
            while (!stopThreads)
            {
                if (dataCxn != null && dataCxn.Available > 0)
                {
                    try
                    {
                        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                        byte[] receivedBytes = dataCxn.Receive(ref remoteEP);

                        if (_isConnected && remoteEP.Address.ToString() == connectedEmotibitIp)
                        {
                            string message = Encoding.UTF8.GetString(receivedBytes);
                            string[] packets = message.Split(new[] { EmotiBitPacket.PACKET_DELIMITER_CSV }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var packet in packets)
                            {
                                dataPackets.PushBack(packet);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error receiving data packet: {e.Message}");
                    }
                }
                ThreadSleepFor(_wifiHostSettings.DataThreadSleep);
            }
        }

        public void ReadData(ref List<string> packets)
        {
            dataPackets.Get(ref packets);
        }

        private void ProcessRequestData(string packet, int dataStartChar)
        {
            // Request Data
            string element;
            string outPacket;
            int tempStartChar = dataStartChar;
            do
            {
                // Parse through requested packet elements and data
                tempStartChar = EmotiBitPacket.GetPacketElement(packet, out element, tempStartChar);

                if (element.Equals(EmotiBitPacket.TypeTag.TIMESTAMP_LOCAL))
                {
                    outPacket = EmotiBitPacket.CreatePacket(EmotiBitPacket.TypeTag.TIMESTAMP_LOCAL, dataPacketCounter++, DateTime.Now.ToString(EmotiBitPacket.TIMESTAMP_STRING_FORMAT));
                    SendData(outPacket);
                }
                if (element.Equals(EmotiBitPacket.TypeTag.TIMESTAMP_UTC))
                {
                    // ToDo: implement UTC timestamp
                }
            } while (tempStartChar > 0);

            EmotiBitPacket.Header header;
            EmotiBitPacket.GetHeader(packet, out header);
            List<string> payload = new List<string>();
            payload.Add(header.packetNumber.ToString());
            payload.Add(header.typeTag);
            outPacket = EmotiBitPacket.CreatePacket(EmotiBitPacket.TypeTag.ACK, dataPacketCounter++, payload);
            SendData(outPacket);
        }

        public byte SendData(string packet)
        {
            if (_isConnected)
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(packet);
                    lock (dataCxnLock)
                    {
                        dataCxn.Send(data, data.Length);
                    }
                    return SUCCESS;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error sending data: {e.Message}");
                    return FAIL;
                }
            }
            else
            {
                return FAIL;
            }
        }
        #endregion

        #region Control Connection
        private void ControlConnectionThread()
        {
            while (!stopThreads)
            {
                try
                {
                    if (controlCxn.Pending())
                    {
                        TcpClient client = controlCxn.AcceptTcpClient();
                        lock (controlCxnLock)
                        {
                            controlClients.Add(client);
                            controlClientStreams.Add(client.GetStream());
                            lastId++;
                        }
                        Debug.Log("New control client connected.");
                    }

                    lock (controlCxnLock)
                    {
                        for (int i = controlClients.Count - 1; i >= 0; i--)
                        {
                            if (!controlClients[i].Connected)
                            {
                                controlClients.RemoveAt(i);
                                controlClientStreams.RemoveAt(i);
                                lastId--;
                                Debug.Log("Control client disconnected.");
                                continue;
                            }

                            if (controlClientStreams[i].DataAvailable)
                            {
                                // Handle incoming control data if necessary
                                // This part is not fully implemented in the C++ version either (readControl is commented out)
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"TCP Accept error: {e.Message}");
                }
                Thread.Sleep(100);
            }
        }

        public byte SendControl(string packet)
        {
            lock (controlCxnLock)
            {
                byte[] data = Encoding.UTF8.GetBytes(packet + EmotiBitPacket.PACKET_DELIMITER_CSV);
                for (int i = 0; i < controlClients.Count; i++)
                {
                    try
                    {
                        if (controlClients[i].Connected)
                        {
                            IPEndPoint remoteIpEndPoint = controlClients[i].Client.RemoteEndPoint as IPEndPoint;
                            if (remoteIpEndPoint.Address.ToString() == connectedEmotibitIp)
                            {
                                controlClientStreams[i].Write(data, 0, data.Length);
                                Debug.Log($"Sent control packet: {packet}");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Could not send control packet to client {i}: {e.Message}");
                    }
                }
            }
            return SUCCESS;
        }
        #endregion

        #region Public API
        public byte Connect(string deviceId)
        {
            if (isStartingConnection || _isConnected)
            {
                Debug.Log($"Connect rejected: {(isStartingConnection ? "starting connection" : "already connected")}");
                return FAIL;
            }

            lock (discoveredEmotibitsLock)
            {
                if (_discoveredEmotibits.TryGetValue(deviceId, out EmotibitInfo info))
                {
                    if (info.IsAvailable && !string.IsNullOrEmpty(info.Ip))
                    {
                        connectedEmotibitIp = info.Ip;
                        connectedEmotibitIdentifier = deviceId;
                        isStartingConnection = true;
                        startCxnAbortTimer = stopwatch.ElapsedMilliseconds;
                        connectionTimer = stopwatch.ElapsedMilliseconds;
                        Debug.Log($"Attempting to connect to {deviceId} at {info.Ip}");

                        // Send connect message
                        var payload = new List<string> { EmotiBitPacket.PayloadLabel.CONTROL_PORT, controlPort.ToString(), EmotiBitPacket.PayloadLabel.DATA_PORT, _dataPort.ToString() };
                        string packet = EmotiBitPacket.CreatePacket(EmotiBitPacket.TypeTag.EMOTIBIT_CONNECT, advertisingPacketCounter++, payload);
                        byte[] data = Encoding.UTF8.GetBytes(packet);
                        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(connectedEmotibitIp), advertisingPort);
                        advertisingCxn.Send(data, data.Length, remoteEP);

                        return SUCCESS;
                    }
                }
            }
            // Debug.Log(_discoveredEmotibits.Keys.ToList().Stringify());
            Debug.LogWarning($"EmotiBit {deviceId} not found or not available.");
            return FAIL;
        }

        public byte Disconnect()
        {
            if (_isConnected)
            {
                string packet = EmotiBitPacket.CreatePacket(EmotiBitPacket.TypeTag.EMOTIBIT_DISCONNECT, controlPacketCounter++);
                SendControl(packet);
            }

            FlushData();
            connectedEmotibitIp = "";
            connectedEmotibitIdentifier = "";
            _isConnected = false;
            isStartingConnection = false;
            Debug.Log("Disconnected.");
            return SUCCESS;
        }

        public Dictionary<string, EmotibitInfo> GetDiscoveredEmotibits()
        {
            lock (discoveredEmotibitsLock)
            {
                // Return a copy to avoid threading issues on the receiver's end
                return new Dictionary<string, EmotibitInfo>(_discoveredEmotibits);
            }
        }

        public string GetConnectedDeviceIdentifier()
        {
            return connectedEmotibitIdentifier;
        }

        public void Dispose()
        {
            stopThreads = true;

            Disconnect();

            advertisingThread?.Join(500);
            dataThread?.Join(500);
            controlThread?.Join(500);

            advertisingCxn?.Close();
            dataCxn?.Close();
            controlCxn?.Stop();

            stopwatch.Stop();
        }
        #endregion

        #region Utility Methods


        private void ThreadSleepFor(int sleepMicros)
        {
            if (sleepMicros < 0) return;
            if (sleepMicros == 0)
            {
                Thread.Yield();
            }
            else
            {
                Thread.Sleep(sleepMicros / 1000); // Thread.Sleep takes milliseconds
            }
        }
        #endregion
    }
}