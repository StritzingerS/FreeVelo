using System;
using FreeVelo.Devices.Ble;

namespace FreeVelo.Devices.Tacx
{
    /// <summary>
    /// Tacx "ANT+ FE-C over BLE": ANT+ fitness equipment data pages wrapped in ANT messages and
    /// exchanged over a proprietary BLE service. Used as fallback for Tacx trainers without FTMS.
    /// </summary>
    public static class TacxFec
    {
        /// <summary>Tacx FE-C service.</summary>
        public static readonly Guid Service = Guid.Parse("6e40fec1-b5a3-f393-e0a9-e50e24dcca9e");

        /// <summary>Notifications from the trainer (ANT broadcast messages).</summary>
        public static readonly Guid RxCharacteristic = Guid.Parse("6e40fec2-b5a3-f393-e0a9-e50e24dcca9e");

        /// <summary>Commands to the trainer (ANT acknowledged messages).</summary>
        public static readonly Guid TxCharacteristic = Guid.Parse("6e40fec3-b5a3-f393-e0a9-e50e24dcca9e");

        private const byte Sync = 0xA4;
        private const byte PayloadLength = 0x09;
        private const byte BroadcastData = 0x4E;
        private const byte AcknowledgedData = 0x4F;
        private const byte Channel = 0x05;

        /// <summary>Wraps an 8-byte data page into an ANT acknowledged message for the TX characteristic.</summary>
        public static byte[] WrapPage(byte[] page)
        {
            if (page.Length != 8)
            {
                throw new ArgumentException("An ANT data page has exactly 8 bytes.", nameof(page));
            }

            var message = new byte[13];
            message[0] = Sync;
            message[1] = PayloadLength;
            message[2] = AcknowledgedData;
            message[3] = Channel;
            Array.Copy(page, 0, message, 4, 8);
            message[12] = Checksum(message, 12);
            return message;
        }

        /// <summary>Extracts the 8-byte data page from an ANT message received on the RX characteristic.</summary>
        public static byte[] UnwrapPage(ReadOnlySpan<byte> message)
        {
            if (message.Length < 13 || message[0] != Sync || message[1] != PayloadLength
                || (message[2] != BroadcastData && message[2] != AcknowledgedData))
            {
                throw new FormatException("Not an ANT data message.");
            }

            byte checksum = 0;
            for (int i = 0; i < 12; i++)
            {
                checksum ^= message[i];
            }

            if (checksum != message[12])
            {
                throw new FormatException("ANT message checksum mismatch.");
            }

            return message.Slice(4, 8).ToArray();
        }

        /// <summary>ERG mode – data page 49 (0x31), target power in 0.25 W.</summary>
        public static byte[] SetTargetPower(int watts)
        {
            int raw = Math.Max(0, Math.Min(4000, watts)) * 4;
            return WrapPage(new byte[] { 0x31, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, (byte)(raw & 0xFF), (byte)(raw >> 8) });
        }

        /// <summary>SIM mode – data page 51 (0x33), grade in 0.01 % with −200 % offset, Crr in 5·10⁻⁵.</summary>
        public static byte[] SetTrackResistance(double gradePercent, double rollingResistance = 0.004)
        {
            int grade = (int)Math.Round((Math.Max(-200.0, Math.Min(200.0, gradePercent)) + 200.0) * 100.0);
            int crr = (int)Math.Max(0, Math.Min(254, Math.Round(rollingResistance / 0.00005)));
            return WrapPage(new byte[] { 0x33, 0xFF, 0xFF, 0xFF, 0xFF, (byte)(grade & 0xFF), (byte)(grade >> 8), (byte)crr });
        }

        /// <summary>
        /// Data page 55 (0x37): rider and bike weight, wheel diameter. Trainers use it for SIM mode physics.
        /// </summary>
        public static byte[] SetUserConfiguration(double riderWeightKg, double bikeWeightKg = 9.0, double wheelDiameterM = 0.70)
        {
            int rider = (int)Math.Round(Math.Max(0, Math.Min(655.34, riderWeightKg)) * 100.0);
            int bike = (int)Math.Round(Math.Max(0, Math.Min(50.0, bikeWeightKg)) / 0.05);
            int wheel = (int)Math.Round(Math.Max(0, Math.Min(2.54, wheelDiameterM)) * 100.0);
            return WrapPage(new byte[]
            {
                0x37,
                (byte)(rider & 0xFF), (byte)(rider >> 8),
                0xFF,
                (byte)((bike & 0x0F) << 4), // low nibble: wheel diameter offset 0 mm
                (byte)(bike >> 4),
                (byte)wheel,
                0x00, // gear ratio: not set
            });
        }

        private static byte Checksum(byte[] data, int count)
        {
            byte checksum = 0;
            for (int i = 0; i < count; i++)
            {
                checksum ^= data[i];
            }

            return checksum;
        }
    }

    /// <summary>Decoded FE-C data pages relevant for FreeVelo.</summary>
    public sealed class FecData
    {
        /// <summary>Data page number (0x10 general FE data, 0x19 specific trainer data, …).</summary>
        public byte Page { get; private set; }

        /// <summary>Speed in km/h (page 0x10).</summary>
        public double? SpeedKmh { get; private set; }

        /// <summary>Heart rate in bpm (page 0x10, if a strap is paired with the trainer).</summary>
        public int? HeartRateBpm { get; private set; }

        /// <summary>Instantaneous power in watts (page 0x19).</summary>
        public int? PowerW { get; private set; }

        /// <summary>Instantaneous cadence in rpm (page 0x19).</summary>
        public int? CadenceRpm { get; private set; }

        /// <summary>Parses an ANT message from the Tacx RX characteristic.</summary>
        public static FecData Parse(ReadOnlySpan<byte> message)
        {
            byte[] page = TacxFec.UnwrapPage(message);
            var data = new FecData { Page = page[0] };
            switch (page[0])
            {
                case 0x10:
                {
                    int speed = page[4] | (page[5] << 8);
                    if (speed != 0xFFFF) data.SpeedKmh = speed * 0.001 * 3.6;
                    if (page[6] != 0xFF) data.HeartRateBpm = page[6];
                    break;
                }

                case 0x19:
                {
                    if (page[2] != 0xFF) data.CadenceRpm = page[2];
                    int power = page[5] | ((page[6] & 0x0F) << 8);
                    if (power != 0xFFF) data.PowerW = power;
                    break;
                }
            }

            return data;
        }

        public static bool TryParse(ReadOnlySpan<byte> message, out FecData? data)
        {
            try
            {
                data = Parse(message);
                return true;
            }
            catch (FormatException)
            {
                data = null;
                return false;
            }
        }
    }
}
