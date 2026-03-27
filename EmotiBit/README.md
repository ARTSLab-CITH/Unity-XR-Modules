# EmotiBit Unity Plugin Documentation

This document provides an overview of the public-facing components within the EmotiBit Unity module. The module manages WiFi communications with EmotiBit devices, including discovery, connection, data exchange, and UI visualization within a Unity environment.

`EmotiBitManager.prefab` can be used to jump-start a project. It is a GameObject with required scripts, meant to be used as the basis for building an EmotiBit connected Unity APK.

## High-Level Unity API

### `EmotiBitManager` Class

A Singleton `MonoBehaviour` that manages the overarching EmotiBit lifecycle. This acts as the main API and integration point for developers in Unity to connect to an EmotiBit and receive physiological data.

### `EmotiBitGui` Class

A `MonoBehaviour` UI controller that hooks into `EmotiBitManager` to display connection statuses, provide a dropdown for device selection, show recording statuses, and dynamically spawn oscilloscopes to visualize the data.

### `ScalarOscilloscope` Class

A visual rendering component (using Unity's `LineRenderer`) that plots real-time EmotiBit data streams point-by-point over a sliding time window. It creates dynamic line graphs for signals.

### `Constants` Class

A class used to store enumerations and data layer constants such as the `DataTypes` available from the EmotiBit sensor.

## Low-Level Utilities and Network Architecture

The following utility classes and data structures are ported from the original C++ implementation to support the main `EmotiBitWiFiHost` networking.

### `EmotiBitInfo` Struct

A simple data structure that holds information about a discovered EmotiBit.

| Member | Type | Description |
| --- | --- | --- |
| `ip` | `string` | The IP address of the EmotiBit. |
| `isAvailable` | `bool` | `true` if the EmotiBit is available for connection. |
| `lastSeen` | `long` | The timestamp (in milliseconds) when the EmotiBit was last seen. |

### `EmotiBitComms` Class

Contains public constants related to the EmotiBit communication protocol.

| Constant | Type | Description |
| --- | --- | --- |
| `WIFI_ADVERTISING_PORT` | `int` | The UDP port used for device discovery broadcasts. |
| `EMOTIBIT_AVAILABLE` | `int` | A special value indicating an EmotiBit is available. |

### `DoubleBuffer<T>` Class

A generic, thread-safe double buffer implementation. It allows one thread to write data to an input buffer while another thread reads from a separate output buffer, avoiding race conditions.

- **`void push_back(T data)`**: Adds an item to the current input buffer.
- **`void get(List<T> output)`**: Swaps the input and output buffers and copies the contents of the new output buffer into the provided list.

### `EmotiBitPacket` Class

A utility class for creating and parsing EmotiBit data packets, which follow a specific CSV-like format.

#### Nested Classes

- **`Header`**: Represents the header of an EmotiBit packet, containing metadata like timestamp, packet number, and type tag.
- **`TypeTag`**: A collection of string constants representing the `typeTag` for different kinds of packets (e.g., `HELLO_EMOTIBIT`, `PING`, `DATA_CLIPPING`).
- **`PayloadLabel`**: A collection of string constants for labeling data within a packet's payload (e.g., `CONTROL_PORT`, `DEVICE_ID`).

#### Key Static Methods

- **`string createPacket(...)`**: Creates a fully formatted packet string from a type tag, packet number, and payload data.
- **`short getHeader(string packet, out Header header)`**: Parses a packet string and extracts the `Header`.
- **`short getPacketElement(string packet, out string element, int startChar)`**: Extracts the next data element from a packet's payload.
- **`short getPacketKeyedValue(string packet, string key, out string value, int startChar)`**: Extracts a value associated with a specific key from the packet's payload.

---

## `EmotiBitWiFiHost` Class

The primary class for managing the WiFi connection to an EmotiBit. It handles device discovery, connection management, and sending/receiving data packets.

### `WifiHostSettings` Nested Class

A data class containing configuration settings for the `EmotiBitWiFiHost`.

| Member | Type | Description |
| --- | --- | --- |
| `sendAdvertisingInterval` | `int` | Interval in milliseconds to send advertising packets. |
| `checkAdvertisingInterval` | `int` | Interval in milliseconds to check for responses to advertising. |
| `enableBroadcast` | `bool` | If `true`, enables sending advertising packets via broadcast. |
| `enableUnicast` | `bool` | If `true`, enables sending advertising packets via unicast. |
| `networkIncludeList` | `List<string>` | A list of IP patterns to include in network searches (e.g., "192.168.1.*"). |
| `networkExcludeList` | `List<string>` | A list of IP patterns to exclude from network searches. |

### Key Public Methods & Properties

| Member | Type | Description |
| --- | --- | --- |
| **`sbyte begin()`** | Method | Initializes the WiFi host. Starts background threads for advertising, data, and control connections. Returns `SUCCESS` (0) on success. |
| **`sbyte connect(string deviceId)`** | Method | Attempts to establish a connection with a discovered EmotiBit identified by `deviceId`. |
| **`sbyte disconnect()`** | Method | Disconnects from the currently connected EmotiBit. |
| **`bool isConnected()`** | Method | Returns `true` if the host is currently connected to an EmotiBit. |
| **`void readData(List<string> packets)`** | Method | Fills the provided list with all data packets received since the last call. |
| **`sbyte sendControl(string packet)`** | Method | Sends a packet over the reliable TCP control connection. |
| **`sbyte sendData(string packet)`** | Method | Sends a packet over the less-reliable UDP data connection. |
| **`Dictionary<string, EmotibitInfo> getdiscoveredEmotibits()`** | Method | Returns a dictionary of all EmotiBits discovered on the network, keyed by their device ID. |
| **`void parseCommSettings(string jsonStr)`** | Method | Parses a JSON string to configure the `WifiHostSettings`. |
| **`void setWifiHostSettings(WifiHostSettings settings)`** | Method | Sets the `WifiHostSettings` for the host. |
| **`WifiHostSettings getWifiHostSettings()`** | Method | Gets the current `WifiHostSettings`. |
| **`List<string> getLocalIPs()`** | Method | Returns a list of all local IPv4 addresses on the host machine. |
| **`void Dispose()`** | Method | Stops all threads and closes all network connections. |