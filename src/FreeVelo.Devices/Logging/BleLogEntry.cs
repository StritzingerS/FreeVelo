using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FreeVelo.Devices.Logging
{
    /// <summary>Direction of a logged GATT payload.</summary>
    public enum BleLogDirection
    {
        /// <summary>Notification or indication from the device.</summary>
        Notify,

        /// <summary>Write from the app to the device.</summary>
        Write,

        /// <summary>Value read from the device.</summary>
        Read,
    }

    /// <summary>
    /// One line of a FreeVelo BLE byte log (format v1, written by the Android BLE plugin):
    /// <c>timestampMs,deviceAddress,characteristicUuid,direction(N|W|R),hexPayload</c>.
    /// Logs recorded on real devices are used as regression test input.
    /// </summary>
    public sealed class BleLogEntry
    {
        /// <summary>First line of every log file.</summary>
        public const string Header = "# FreeVelo BLE log v1";

        public BleLogEntry(long timestampMs, string deviceAddress, Guid characteristic, BleLogDirection direction, byte[] payload)
        {
            TimestampMs = timestampMs;
            DeviceAddress = deviceAddress;
            Characteristic = characteristic;
            Direction = direction;
            Payload = payload;
        }

        public long TimestampMs { get; }

        public string DeviceAddress { get; }

        public Guid Characteristic { get; }

        public BleLogDirection Direction { get; }

        public byte[] Payload { get; }

        public static BleLogEntry ParseLine(string line)
        {
            string[] parts = line.Split(',');
            if (parts.Length != 5)
            {
                throw new FormatException($"Expected 5 comma separated fields: '{line}'");
            }

            BleLogDirection direction = parts[3] switch
            {
                "N" => BleLogDirection.Notify,
                "W" => BleLogDirection.Write,
                "R" => BleLogDirection.Read,
                _ => throw new FormatException($"Unknown direction '{parts[3]}'"),
            };

            return new BleLogEntry(
                long.Parse(parts[0], CultureInfo.InvariantCulture),
                parts[1],
                Guid.Parse(parts[2]),
                direction,
                FromHex(parts[4]));
        }

        /// <summary>Reads all entries, skipping the header, comments and empty lines.</summary>
        public static IEnumerable<BleLogEntry> ReadAll(TextReader reader)
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return ParseLine(line);
            }
        }

        public string ToLine()
        {
            char direction = Direction switch
            {
                BleLogDirection.Notify => 'N',
                BleLogDirection.Write => 'W',
                _ => 'R',
            };
            return string.Join(
                ",",
                TimestampMs.ToString(CultureInfo.InvariantCulture),
                DeviceAddress,
                Characteristic.ToString("D"),
                direction.ToString(),
                ToHex(Payload));
        }

        public static string ToHex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (byte b in data)
            {
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        public static byte[] FromHex(string hex)
        {
            if (hex.Length % 2 != 0)
            {
                throw new FormatException("Hex string must have an even length.");
            }

            var data = new byte[hex.Length / 2];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return data;
        }
    }
}
