using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Debug = UnityEngine.Debug;

// Note: This is a partial conversion containing only what's used by EmotiBitWifiHost
namespace EmotiBit
{
    public static class EmotiBitPacket
    {
        public const char PACKET_DELIMITER_CSV = '\n';
        public const char PAYLOAD_DELIMITER = ',';
        public const string TIMESTAMP_STRING_FORMAT = "yyyy-MM-dd_HH-mm-ss-ffffff";
        public const int FAIL = -1;
        public const int MALFORMED_HEADER = -3;
        public const int NO_PACKET_DATA = -2;
        public const int headerLength = 6;

        public class Header
        {
            public long timestamp;
            public uint packetNumber;
            public int dataLength;
            public string typeTag;
            public int protocolVersion;
            public int dataReliability;
        }

        public static class TypeTag
        {
            // EmotiBit Data TagTypes
            public const string EDA = "EA";
            public const string EDL = "EL";
            public const string EDR = "ER";
            public const string PPG_INFRARED = "PI";
            public const string PPG_RED = "PR";
            public const string PPG_GREEN = "PG";
            public const string SPO2 = "O2";
            public const string TEMPERATURE_0 = "T0";
            public const string TEMPERATURE_1 = "T1";
            public const string THERMOPILE = "TH";
            public const string HUMIDITY_0 = "H0";
            public const string ACCELEROMETER_X = "AX";
            public const string ACCELEROMETER_Y = "AY";
            public const string ACCELEROMETER_Z = "AZ";
            public const string GYROSCOPE_X = "GX";
            public const string GYROSCOPE_Y = "GY";
            public const string GYROSCOPE_Z = "GZ";
            public const string MAGNETOMETER_X = "MX";
            public const string MAGNETOMETER_Y = "MY";
            public const string MAGNETOMETER_Z = "MZ";
            public const string BATTERY_VOLTAGE = "BV";
            public const string BATTERY_PERCENT = "B%";
            public const string BUTTON_PRESS_SHORT = "BS";
            public const string BUTTON_PRESS_LONG = "BL";
            public const string DATA_CLIPPING = "DC";
            public const string DATA_OVERFLOW = "DO";
            public const string SD_CARD_PERCENT = "SD";
            public const string RESET = "RS";
            public const string EMOTIBIT_DEBUG = "DB";
            public const string ACK = "AK";
            public const string NACK = "NK";
            public const string REQUEST_DATA = "RD";
            public const string TIMESTAMP_EMOTIBIT = "TE";
            public const string TIMESTAMP_LOCAL = "TL";
            public const string TIMESTAMP_UTC = "TU";
            public const string TIMESTAMP_CROSS_TIME = "TX";
            public const string EMOTIBIT_MODE = "EM";
            public const string EMOTIBIT_INFO = "EI";
            public const string HEART_RATE = "HR";
            public const string INTER_BEAT_INTERVAL = "BI";
            public const string SKIN_CONDUCTANCE_RESPONSE_AMPLITUDE = "SA";
            public const string SKIN_CONDUCTANCE_RESPONSE_FREQ = "SF";
            public const string SKIN_CONDUCTANCE_RESPONSE_RISE_TIME = "SR";
            // Computer data TypeTags
            public const string GPS_LATLNG = "GL";
            public const string GPS_SPEED = "GS";
            public const string GPS_BEARING = "GB";
            public const string GPS_ALTITUDE = "GA";
            public const string USER_NOTE = "UN";
            public const string LSL_MARKER = "LM";
            // Control TypeTags
            public const string RECORD_BEGIN = "RB";
            public const string RECORD_END = "RE";
            public const string MODE_NORMAL_POWER = "MN";
            public const string MODE_LOW_POWER = "ML";
            public const string MODE_MAX_LOW_POWER = "MM";
            public const string MODE_WIRELESS_OFF = "MO";
            public const string MODE_HIBERNATE = "MH";
            public const string EMOTIBIT_DISCONNECT = "ED";
            public const string SERIAL_DATA_ON = "S+";
            public const string SERIAL_DATA_OFF = "S-";
            // Advertising TypeTags
            public const string PING = "PN"; // Note: C++ source has "PN" for advertising ping, but C# had "PI". Let's assume "PN" is correct for advertising. The original C# had PI for PING. The C++ has PI for PPG_INFRARED and PN for PING. Let's stick to the C++ source.
            public const string PONG = "PO";
            public const string HELLO_EMOTIBIT = "HE";
            public const string HELLO_HOST = "HH";
            public const string EMOTIBIT_CONNECT = "EC";
            // WiFi Credential management TypeTags
            public const string WIFI_ADD = "WA";
            public const string WIFI_DELETE = "WD";
            //Information Exchange TypeTags
            public const string LIST = "LS";
        }

        public static class PayloadLabel
        {
            public const string CONTROL_PORT = "CP";
            public const string DATA_PORT = "DP";
            public const string DEVICE_ID = "DI";
            public const string RECORDING_STATUS = "RS";
            public const string POWER_STATUS = "PS";
            public const string LSL_MARKER_RX_TIMESTAMP = "LR";
            public const string LSL_MARKER_SRC_TIMESTAMP = "LM";
            public const string LSL_LOCAL_CLOCK_TIMESTAMP = "LC";
            public const string LSL_MARKER_DATA = "LD";
        }

        public static class TypeTagGroups
        {
            public static readonly string[] APERIODIC = {
            TypeTag.HEART_RATE,
            TypeTag.INTER_BEAT_INTERVAL,
            TypeTag.SKIN_CONDUCTANCE_RESPONSE_AMPLITUDE,
            TypeTag.SKIN_CONDUCTANCE_RESPONSE_RISE_TIME
        };
            public static readonly int NUM_APERIODIC = APERIODIC.Length;

            public static readonly string[] USER_MESSAGES = { TypeTag.USER_NOTE };
            public static readonly int NUM_USER_MESSAGES = USER_MESSAGES.Length;

            public static readonly string[] COMPOSITE_PAYLOAD = {
            TypeTag.TIMESTAMP_CROSS_TIME,
            TypeTag.LSL_MARKER
        };
            public static readonly int NUM_COMPOSITE_PAYLOAD = COMPOSITE_PAYLOAD.Length;
        }

        public const uint maxTestLength = 512;

        public enum TestType
        {
            SAWTOOTH,
            FIXED_PACKET_LENGTH
        }

        public static string CreatePacket(string typeTag, uint packetNumber, List<string> payload, byte protocolVersion = 1, byte dataReliability = 100)
        {
            return CreatePacket(typeTag, packetNumber, payload, (uint)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds, protocolVersion, dataReliability);
        }

        public static string CreatePacket(string typeTag, uint packetNumber, List<string> payload, long timestamp, byte protocolVersion = 1, byte dataReliability = 100)
        {
            Header header = CreateHeader(typeTag, timestamp, packetNumber, (ushort)payload.Count, protocolVersion, dataReliability);
            string packet = HeaderToString(header);
            if (payload != null && payload.Count > 0)
            {
                packet += PAYLOAD_DELIMITER + string.Join(PAYLOAD_DELIMITER.ToString(), payload);
            }
            packet += PACKET_DELIMITER_CSV;
            return packet;
        }

        public static string CreatePacket(string typeTag, uint packetNumber, string data = "", int dataLength = 0, byte protocolVersion = 1, byte dataReliability = 100)
        {
            Header header = CreateHeaderWithTime(typeTag, packetNumber, (ushort)dataLength, protocolVersion, dataReliability);
            return CreatePacket(header, data);
        }

        public static string CreatePacket(Header header, string data)
        {
            string packet = HeaderToString(header);
            if (!string.IsNullOrEmpty(data))
            {
                packet += PAYLOAD_DELIMITER + data;
            }
            packet += PACKET_DELIMITER_CSV;
            return packet;
        }

        public static Header CreateHeader(string typeTag, long timestamp, uint packetNumber, ushort dataLength, byte protocolVersion = 1, byte dataReliability = 100)
        {
            return new Header
            {
                typeTag = typeTag,
                timestamp = timestamp,
                packetNumber = packetNumber,
                dataLength = dataLength,
                protocolVersion = protocolVersion,
                dataReliability = dataReliability
            };
        }

        public static Header CreateHeaderWithTime(string typeTag, uint packetNumber, ushort dataLength, byte protocolVersion = 1, byte dataReliability = 100)
        {
            // Using DateTime.UtcNow to get a timestamp. The C++ version uses ofGetElapsedTimeMillis() or 0.
            // For consistency with the rest of the C# code, we'll use a Unix-style timestamp.
            long timestamp = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;
            return CreateHeader(typeTag, timestamp, packetNumber, dataLength, protocolVersion, dataReliability);
        }

        public static string HeaderToString(Header header)
        {
            return $"{header.timestamp}{PAYLOAD_DELIMITER}" +
                   $"{header.packetNumber}{PAYLOAD_DELIMITER}" +
                   $"{header.dataLength}{PAYLOAD_DELIMITER}" +
                   $"{header.typeTag}{PAYLOAD_DELIMITER}" +
                   $"{header.protocolVersion}{PAYLOAD_DELIMITER}" +
                   $"{header.dataReliability}";
        }

        public static int GetHeader(string packet, out Header packetHeader)
        {
            packetHeader = new Header();
            if (string.IsNullOrEmpty(packet))
            {
                Debug.LogWarning("GetHeader failed: Packet is null or empty.");
                return MALFORMED_HEADER;
            }

            var parts = packet.Split(PAYLOAD_DELIMITER);
            if (parts.Length < headerLength)
            {
                Debug.LogWarning($"GetHeader failed: Packet has {parts.Length} parts, expected at least {headerLength}. Packet: '{packet}'");
                return MALFORMED_HEADER;
            }

            try
            {
                if (!long.TryParse(parts[0], out packetHeader.timestamp))
                {
                    Debug.LogWarning($"GetHeader failed: Could not parse timestamp '{parts[0]}'. Packet: '{packet}'");
                    return MALFORMED_HEADER;
                }
                if (!uint.TryParse(parts[1], out packetHeader.packetNumber))
                {
                    Debug.LogWarning($"GetHeader failed: Could not parse packet number '{parts[1]}'. Packet: '{packet}'");
                    return MALFORMED_HEADER;
                }
                if (!int.TryParse(parts[2], out packetHeader.dataLength))
                {
                    Debug.LogWarning($"GetHeader failed: Could not parse data length '{parts[2]}'. Packet: '{packet}'");
                    return MALFORMED_HEADER;
                }

                packetHeader.typeTag = parts[3];
                if (string.IsNullOrEmpty(packetHeader.typeTag))
                {
                    Debug.LogWarning($"GetHeader failed: TypeTag is null or empty. Packet: '{packet}'");
                    return MALFORMED_HEADER;
                }

                if (!int.TryParse(parts[4], out packetHeader.protocolVersion))
                {
                    Debug.LogWarning($"GetHeader failed: Could not parse protocol version '{parts[4]}'. Packet: '{packet}'");
                    return MALFORMED_HEADER;
                }
                if (!int.TryParse(parts[5], out packetHeader.dataReliability))
                {
                    Debug.LogWarning($"GetHeader failed: Could not parse data reliability '{parts[5]}'. Packet: '{packet}'");
                    return MALFORMED_HEADER;
                }

                if (parts.Length > headerLength)
                {
                    // The start of data is after the header and the delimiter.
                    // To find the character index, we join the header parts back together.
                    int dataStartChar = string.Join(PAYLOAD_DELIMITER.ToString(), parts.Take(headerLength)).Length + 1;
                    return dataStartChar;
                }
                else
                {
                    return NO_PACKET_DATA;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to parse header for packet '{packet}': {e}");
                return MALFORMED_HEADER;
            }
        }

        public static bool GetHeader(List<string> packet, out Header packetHeader)
        {
            packetHeader = new Header();
            if (packet.Count < headerLength)
            {
                return false;
            }

            try
            {
                if (!string.IsNullOrEmpty(packet[0])) packetHeader.timestamp = long.Parse(packet[0]); else return false;
                if (!string.IsNullOrEmpty(packet[1])) packetHeader.packetNumber = uint.Parse(packet[1]); else return false;
                if (!string.IsNullOrEmpty(packet[2])) packetHeader.dataLength = int.Parse(packet[2]); else return false;
                if (!string.IsNullOrEmpty(packet[3])) packetHeader.typeTag = packet[3]; else return false;
                if (!string.IsNullOrEmpty(packet[4])) packetHeader.protocolVersion = int.Parse(packet[4]); else return false;
                if (!string.IsNullOrEmpty(packet[5])) packetHeader.dataReliability = int.Parse(packet[5]); else return false;
            }
            catch (Exception)
            {
                return false;
            }

            if (packet.Count < headerLength + packetHeader.dataLength)
            {
                return false;
            }

            return true;
        }

        public static int GetPacketElement(string packet, out string element, int startChar)
        {
            element = "";
            if (startChar < 0 || startChar >= packet.Length)
            {
                return -1;
            }

            int commaN1 = packet.IndexOf(PAYLOAD_DELIMITER, startChar);

            if (commaN1 != -1)
            {
                element = packet.Substring(startChar, commaN1 - startChar);
                return commaN1 + 1;
            }
            else
            {
                element = packet.Substring(startChar);
                return -1; // No more elements
            }
        }

        public static int GetPacketKeyedValue(string packet, string key, out string value, int startChar)
        {
            value = "";
            string element;
            int currentIndex = startChar;

            while (currentIndex != -1)
            {
                currentIndex = GetPacketElement(packet, out element, currentIndex);
                if (element == key && currentIndex != -1)
                {
                    GetPacketElement(packet, out value, currentIndex);
                    return currentIndex;
                }
            }
            return -1;
        }

        public static int GetPacketKeyedValue(List<string> splitPacket, string key, out string value, int startIndex = 0)
        {
            value = "";
            for (int i = startIndex; i < splitPacket.Count - 1; i++)
            {
                if (splitPacket[i] == key)
                {
                    value = splitPacket[i + 1];
                    return i + 1;
                }
            }
            return -1;
        }

        private static bool testFirstMessage = true;
        private static int testCount = 0;

        public static void CreateTestDataPacket(out string dataMessage, TestType testType)
        {
            dataMessage = "";
            // First message to signify start of test
            if (testFirstMessage)
            {
                testFirstMessage = false;
                Header beginHeader = CreateHeader(TypeTag.USER_NOTE, 0, 0, 1, 0, 0);
                string data = $"Test Length: {maxTestLength}{PACKET_DELIMITER_CSV}";
                dataMessage = CreatePacket(beginHeader, data);
            }
            //ToDo: Refactor testing structure to be more modular so we can add more tests easily
            else if (testType == TestType.SAWTOOTH && testCount <= maxTestLength)
            {
                string data = CreateTestSawtoothData(out int dataLength); //Set data first so dataLength is set for the header
                Header header = CreateTestHeader((ushort)dataLength);

                dataMessage = CreatePacket(header, data);
                testCount++;
            }
            else if (testType == TestType.FIXED_PACKET_LENGTH && testCount <= maxTestLength) // Change splitter to fixedlength
            {
                dataMessage = CreateTestPacketFixedLength(testCount);
                testCount++;
            }
            // End case to visually signal end of test
            else if (testCount == maxTestLength + 1)
            {
                Header endHeader = CreateHeader(TypeTag.EDA, 0, 0, 1, 0, 0);
                string data = "0";
                dataMessage = CreatePacket(endHeader, data);
                testCount++;
            }
        }

        public static string CreateTestSawtoothData(out int outLength)
        {
            var payload = new StringBuilder();

            int numValues = 10; // Number of values to generate
            int minVal = 0; // Minimum value
            int maxVal = 100; // Maximum value

            for (int i = 0; i < numValues; ++i)
            {
                if (i > 0) payload.Append(PAYLOAD_DELIMITER);
                int value = minVal + ((maxVal - minVal) * i) / (numValues - 1);
                payload.Append(value);
            }
            outLength = numValues;
            return payload.ToString();
        }

        public static string CreateTestPacketFixedLength(int payloadLength)
        {
            string packet;
            var data = new StringBuilder();
            int payloadLengthOffset = (PAYLOAD_DELIMITER + "0" + PACKET_DELIMITER_CSV).Length;

            Header header = CreateHeader(TypeTag.USER_NOTE, (uint)testCount, (uint)testCount, 1);

            string headerString = HeaderToString(header);

            // Calculate number of dashes needed
            int dataLength = payloadLength - headerString.Length - payloadLengthOffset;
            if (dataLength < 0) dataLength = 0; // Prevent negative

            for (int i = 0; i < dataLength; i++)
            {
                data.Append("-");
            }
            data.Append("0"); // Add marker at the end, consider replacing with a packet delimiter
            data.Append(PACKET_DELIMITER_CSV); // Add delimiter

            packet = headerString + PAYLOAD_DELIMITER + data.ToString();

            return packet;
        }

        public static Header CreateTestHeader(ushort dataLength)
        {
            long timestamp = testCount;
            uint packetNumber = (uint)testCount;
            byte protocolVersion = (byte)(testCount % 256);
            byte dataReliability = (byte)(testCount % 256);

            Header header = CreateHeader(
                TypeTag.EDA,
                timestamp,
                packetNumber,
                dataLength,
                protocolVersion,
                dataReliability
            );
            return header;
        }
    }
}