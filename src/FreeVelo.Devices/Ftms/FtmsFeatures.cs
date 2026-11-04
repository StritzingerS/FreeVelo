using System;
using FreeVelo.Devices.Ble;

namespace FreeVelo.Devices.Ftms
{
    /// <summary>FTMS Fitness Machine Feature characteristic (0x2ACC).</summary>
    public sealed class FtmsFeatures
    {
        private FtmsFeatures(uint machineFeatures, uint targetSettingFeatures)
        {
            MachineFeatures = machineFeatures;
            TargetSettingFeatures = targetSettingFeatures;
        }

        /// <summary>Raw Fitness Machine Features bit field.</summary>
        public uint MachineFeatures { get; }

        /// <summary>Raw Target Setting Features bit field.</summary>
        public uint TargetSettingFeatures { get; }

        public bool SupportsCadence => Bit(MachineFeatures, 1);

        public bool SupportsHeartRate => Bit(MachineFeatures, 10);

        public bool SupportsPowerMeasurement => Bit(MachineFeatures, 14);

        public bool SupportsResistanceTarget => Bit(TargetSettingFeatures, 2);

        /// <summary>ERG mode.</summary>
        public bool SupportsPowerTarget => Bit(TargetSettingFeatures, 3);

        /// <summary>SIM mode.</summary>
        public bool SupportsIndoorBikeSimulation => Bit(TargetSettingFeatures, 13);

        public bool SupportsSpinDown => Bit(TargetSettingFeatures, 15);

        public static FtmsFeatures Parse(ReadOnlySpan<byte> payload)
        {
            var reader = new ByteReader(payload);
            return new FtmsFeatures(reader.ReadUInt32(), reader.ReadUInt32());
        }

        public override string ToString() =>
            $"cadence={SupportsCadence} power={SupportsPowerMeasurement} hr={SupportsHeartRate} " +
            $"erg={SupportsPowerTarget} sim={SupportsIndoorBikeSimulation} resistance={SupportsResistanceTarget} " +
            $"spindown={SupportsSpinDown}";

        private static bool Bit(uint value, int bit) => (value & (1u << bit)) != 0;
    }

    /// <summary>FTMS Supported Power Range characteristic (0x2AD8).</summary>
    public sealed class SupportedPowerRange
    {
        private SupportedPowerRange(int minimumW, int maximumW, int incrementW)
        {
            MinimumW = minimumW;
            MaximumW = maximumW;
            IncrementW = incrementW;
        }

        public int MinimumW { get; }

        public int MaximumW { get; }

        public int IncrementW { get; }

        public static SupportedPowerRange Parse(ReadOnlySpan<byte> payload)
        {
            var reader = new ByteReader(payload);
            return new SupportedPowerRange(reader.ReadInt16(), reader.ReadInt16(), reader.ReadUInt16());
        }

        /// <summary>Clamps a target to the range and snaps it to the increment.</summary>
        public int Clamp(int watts)
        {
            int clamped = Math.Max(MinimumW, Math.Min(MaximumW, watts));
            if (IncrementW > 1)
            {
                clamped = MinimumW + ((clamped - MinimumW) / IncrementW * IncrementW);
            }

            return clamped;
        }

        public override string ToString() => $"{MinimumW}–{MaximumW} W (step {IncrementW} W)";
    }
}
