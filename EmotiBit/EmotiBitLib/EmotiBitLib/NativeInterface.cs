using System;
using System.Runtime.InteropServices;
using EmotiBit;

namespace EmotiBitLib
{
    public static class NativeInterface
    {
        private static EmotiBitManager? _manager;

        [UnmanagedCallersOnly(EntryPoint = "InitEmotiBit")]
        public static void InitEmotiBit()
        {
            if (_manager == null)
            {
                _manager = new EmotiBitManager();
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "UpdateEmotiBit")]
        public static void UpdateEmotiBit()
        {
            _manager?.Update();
        }

        [UnmanagedCallersOnly(EntryPoint = "ConnectEmotiBit")]
        public static void ConnectEmotiBit(IntPtr deviceIdPtr)
        {
            string? deviceId = Marshal.PtrToStringAnsi(deviceIdPtr);
            if (deviceId != null)
            {
                _manager?.Connect(deviceId);
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "DisconnectEmotiBit")]
        public static void DisconnectEmotiBit()
        {
            _manager?.Disconnect();
        }

        [UnmanagedCallersOnly(EntryPoint = "IsConnected")]
        public static bool IsConnected()
        {
            return _manager?.IsConnected() ?? false;
        }

        [UnmanagedCallersOnly(EntryPoint = "GetDiscoveredDevicesList")]
        public static IntPtr GetDiscoveredDevicesList()
        {
            if (_manager == null) return Marshal.StringToCoTaskMemAnsi("");
            var list = new System.Collections.Generic.List<string>(_manager.DiscoveredDevices.Keys);
            string result = string.Join(",", list);
            return Marshal.StringToCoTaskMemAnsi(result);
        }

        [UnmanagedCallersOnly(EntryPoint = "GetBatteryLevel")]
        public static IntPtr GetBatteryLevel()
        {
            string level = _manager?.BatteryLevel ?? "?";
            return Marshal.StringToCoTaskMemAnsi(level);
        }

        [UnmanagedCallersOnly(EntryPoint = "GetData")]
        public static double GetData(int dataTypeIndex)
        {
            if (_manager != null && Enum.IsDefined(typeof(DataTypes), dataTypeIndex))
            {
                return _manager.GetData((DataTypes)dataTypeIndex);
            }
            return 0.0;
        }
    }
}
