using System;
using System.Globalization;

namespace FreeVelo.Devices.Ble
{
    /// <summary>Bluetooth SIG assigned numbers used by FreeVelo.</summary>
    public static class GattUuids
    {
        // Services
        public const ushort FitnessMachineService = 0x1826;
        public const ushort CyclingPowerService = 0x1818;
        public const ushort HeartRateService = 0x180D;
        public const ushort BatteryService = 0x180F;
        public const ushort CyclingSpeedCadenceService = 0x1816;

        /// <summary>Tacx proprietary ANT+ FE-C over BLE service (Tacx trainers).</summary>
        public static readonly Guid TacxFecService = Guid.Parse("6e40fec1-b5a3-f393-e0a9-e50e24dcca9e");

        // Fitness Machine Service (FTMS) characteristics
        public const ushort FitnessMachineFeature = 0x2ACC;
        public const ushort IndoorBikeData = 0x2AD2;
        public const ushort TrainingStatus = 0x2AD3;
        public const ushort SupportedPowerRange = 0x2AD8;
        public const ushort FitnessMachineControlPoint = 0x2AD9;
        public const ushort FitnessMachineStatus = 0x2ADA;

        // Cycling Power Service characteristics
        public const ushort CyclingPowerMeasurement = 0x2A63;
        public const ushort CyclingPowerFeature = 0x2A65;

        // Heart Rate Service characteristics
        public const ushort HeartRateMeasurement = 0x2A37;

        // Battery Service characteristics
        public const ushort BatteryLevel = 0x2A19;

        // Descriptors
        public const ushort ClientCharacteristicConfiguration = 0x2902;

        private const string BaseUuidSuffix = "-0000-1000-8000-00805f9b34fb";

        /// <summary>Expands a 16-bit assigned number to the full 128-bit UUID.</summary>
        public static Guid FromShort(ushort shortUuid) =>
            Guid.Parse("0000" + shortUuid.ToString("x4", CultureInfo.InvariantCulture) + BaseUuidSuffix);

        /// <summary>
        /// Returns the 16-bit assigned number if <paramref name="uuid"/> is based on the Bluetooth base UUID.
        /// </summary>
        public static bool TryGetShort(Guid uuid, out ushort shortUuid)
        {
            string text = uuid.ToString("D");
            if (text.StartsWith("0000", StringComparison.Ordinal)
                && text.EndsWith(BaseUuidSuffix, StringComparison.OrdinalIgnoreCase))
            {
                shortUuid = ushort.Parse(text.Substring(4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return true;
            }

            shortUuid = 0;
            return false;
        }
    }
}
