using System;
using FreeVelo.Devices.Ble;

namespace FreeVelo.Devices.CyclingPower
{
    /// <summary>Cycling Power Measurement characteristic (0x2A63), used by power meters and trainers.</summary>
    public sealed class CyclingPowerMeasurement
    {
        [Flags]
        private enum Fields : ushort
        {
            PedalPowerBalance = 1 << 0,
            PedalPowerBalanceReference = 1 << 1,
            AccumulatedTorque = 1 << 2,
            AccumulatedTorqueSource = 1 << 3,
            WheelRevolutionData = 1 << 4,
            CrankRevolutionData = 1 << 5,
            ExtremeForceMagnitudes = 1 << 6,
            ExtremeTorqueMagnitudes = 1 << 7,
            ExtremeAngles = 1 << 8,
            TopDeadSpotAngle = 1 << 9,
            BottomDeadSpotAngle = 1 << 10,
            AccumulatedEnergy = 1 << 11,
        }

        /// <summary>Instantaneous power in watts.</summary>
        public int PowerW { get; private set; }

        /// <summary>Pedal power balance in percent (of the reference side).</summary>
        public double? PedalPowerBalancePercent { get; private set; }

        /// <summary>True if <see cref="PedalPowerBalancePercent"/> refers to the left pedal.</summary>
        public bool PedalPowerBalanceIsLeft { get; private set; }

        /// <summary>Accumulated torque in Nm.</summary>
        public double? AccumulatedTorqueNm { get; private set; }

        /// <summary>Cumulative wheel revolutions.</summary>
        public uint? CumulativeWheelRevolutions { get; private set; }

        /// <summary>Last wheel event time in 1/2048 s (rolls over).</summary>
        public ushort? LastWheelEventTime { get; private set; }

        /// <summary>Cumulative crank revolutions (rolls over).</summary>
        public ushort? CumulativeCrankRevolutions { get; private set; }

        /// <summary>Last crank event time in 1/1024 s (rolls over).</summary>
        public ushort? LastCrankEventTime { get; private set; }

        /// <summary>Accumulated energy in kJ.</summary>
        public int? AccumulatedEnergyKj { get; private set; }

        public static CyclingPowerMeasurement Parse(ReadOnlySpan<byte> payload)
        {
            var reader = new ByteReader(payload);
            var flags = (Fields)reader.ReadUInt16();
            var result = new CyclingPowerMeasurement { PowerW = reader.ReadInt16() };

            if ((flags & Fields.PedalPowerBalance) != 0)
            {
                result.PedalPowerBalancePercent = reader.ReadUInt8() / 2.0;
                result.PedalPowerBalanceIsLeft = (flags & Fields.PedalPowerBalanceReference) != 0;
            }

            if ((flags & Fields.AccumulatedTorque) != 0) result.AccumulatedTorqueNm = reader.ReadUInt16() / 32.0;

            if ((flags & Fields.WheelRevolutionData) != 0)
            {
                result.CumulativeWheelRevolutions = reader.ReadUInt32();
                result.LastWheelEventTime = reader.ReadUInt16();
            }

            if ((flags & Fields.CrankRevolutionData) != 0)
            {
                result.CumulativeCrankRevolutions = reader.ReadUInt16();
                result.LastCrankEventTime = reader.ReadUInt16();
            }

            if ((flags & Fields.ExtremeForceMagnitudes) != 0) reader.Skip(4);
            if ((flags & Fields.ExtremeTorqueMagnitudes) != 0) reader.Skip(4);
            if ((flags & Fields.ExtremeAngles) != 0) reader.Skip(3);
            if ((flags & Fields.TopDeadSpotAngle) != 0) reader.Skip(2);
            if ((flags & Fields.BottomDeadSpotAngle) != 0) reader.Skip(2);
            if ((flags & Fields.AccumulatedEnergy) != 0) result.AccumulatedEnergyKj = reader.ReadUInt16();

            return result;
        }

        public static bool TryParse(ReadOnlySpan<byte> payload, out CyclingPowerMeasurement? measurement)
        {
            try
            {
                measurement = Parse(payload);
                return true;
            }
            catch (FormatException)
            {
                measurement = null;
                return false;
            }
        }
    }
}
