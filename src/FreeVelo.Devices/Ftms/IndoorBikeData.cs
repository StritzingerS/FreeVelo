using System;
using FreeVelo.Devices.Ble;

namespace FreeVelo.Devices.Ftms
{
    /// <summary>
    /// FTMS Indoor Bike Data characteristic (0x2AD2).
    /// Absent fields are <c>null</c>. Trainers may split data across several notifications
    /// (<see cref="MoreData"/>), so consumers should merge consecutive packets.
    /// </summary>
    public sealed class IndoorBikeData
    {
        [Flags]
        private enum Fields : ushort
        {
            MoreData = 1 << 0, // inverted: instantaneous speed is present when this bit is 0
            AverageSpeed = 1 << 1,
            InstantaneousCadence = 1 << 2,
            AverageCadence = 1 << 3,
            TotalDistance = 1 << 4,
            ResistanceLevel = 1 << 5,
            InstantaneousPower = 1 << 6,
            AveragePower = 1 << 7,
            ExpendedEnergy = 1 << 8,
            HeartRate = 1 << 9,
            MetabolicEquivalent = 1 << 10,
            ElapsedTime = 1 << 11,
            RemainingTime = 1 << 12,
        }

        /// <summary>True if more fields follow in another notification (instantaneous speed then absent).</summary>
        public bool MoreData { get; private set; }

        /// <summary>Instantaneous speed in km/h.</summary>
        public double? SpeedKmh { get; private set; }

        /// <summary>Average speed in km/h.</summary>
        public double? AverageSpeedKmh { get; private set; }

        /// <summary>Instantaneous cadence in rpm.</summary>
        public double? CadenceRpm { get; private set; }

        /// <summary>Average cadence in rpm.</summary>
        public double? AverageCadenceRpm { get; private set; }

        /// <summary>Total distance in metres.</summary>
        public int? TotalDistanceM { get; private set; }

        /// <summary>Resistance level (unitless, device specific).</summary>
        public int? ResistanceLevel { get; private set; }

        /// <summary>Instantaneous power in watts.</summary>
        public int? PowerW { get; private set; }

        /// <summary>Average power in watts.</summary>
        public int? AveragePowerW { get; private set; }

        /// <summary>Total expended energy in kcal (0xFFFF = not available).</summary>
        public int? TotalEnergyKcal { get; private set; }

        /// <summary>Energy per hour in kcal (0xFFFF = not available).</summary>
        public int? EnergyPerHourKcal { get; private set; }

        /// <summary>Energy per minute in kcal (0xFF = not available).</summary>
        public int? EnergyPerMinuteKcal { get; private set; }

        /// <summary>Heart rate in bpm as reported by the trainer.</summary>
        public int? HeartRateBpm { get; private set; }

        /// <summary>Metabolic equivalent.</summary>
        public double? MetabolicEquivalent { get; private set; }

        /// <summary>Elapsed time in seconds.</summary>
        public int? ElapsedTimeS { get; private set; }

        /// <summary>Remaining time in seconds.</summary>
        public int? RemainingTimeS { get; private set; }

        /// <summary>Parses a payload; throws <see cref="FormatException"/> if it is malformed.</summary>
        public static IndoorBikeData Parse(ReadOnlySpan<byte> payload)
        {
            var reader = new ByteReader(payload);
            var flags = (Fields)reader.ReadUInt16();
            var result = new IndoorBikeData { MoreData = (flags & Fields.MoreData) != 0 };

            if (!result.MoreData) result.SpeedKmh = reader.ReadUInt16() / 100.0;
            if ((flags & Fields.AverageSpeed) != 0) result.AverageSpeedKmh = reader.ReadUInt16() / 100.0;
            if ((flags & Fields.InstantaneousCadence) != 0) result.CadenceRpm = reader.ReadUInt16() / 2.0;
            if ((flags & Fields.AverageCadence) != 0) result.AverageCadenceRpm = reader.ReadUInt16() / 2.0;
            if ((flags & Fields.TotalDistance) != 0) result.TotalDistanceM = reader.ReadUInt24();
            if ((flags & Fields.ResistanceLevel) != 0) result.ResistanceLevel = reader.ReadInt16();
            if ((flags & Fields.InstantaneousPower) != 0) result.PowerW = reader.ReadInt16();
            if ((flags & Fields.AveragePower) != 0) result.AveragePowerW = reader.ReadInt16();
            if ((flags & Fields.ExpendedEnergy) != 0)
            {
                result.TotalEnergyKcal = reader.ReadUInt16();
                result.EnergyPerHourKcal = reader.ReadUInt16();
                result.EnergyPerMinuteKcal = reader.ReadUInt8();
            }

            if ((flags & Fields.HeartRate) != 0) result.HeartRateBpm = reader.ReadUInt8();
            if ((flags & Fields.MetabolicEquivalent) != 0) result.MetabolicEquivalent = reader.ReadUInt8() / 10.0;
            if ((flags & Fields.ElapsedTime) != 0) result.ElapsedTimeS = reader.ReadUInt16();
            if ((flags & Fields.RemainingTime) != 0) result.RemainingTimeS = reader.ReadUInt16();

            return result;
        }

        /// <summary>Parses a payload; returns false if it is malformed.</summary>
        public static bool TryParse(ReadOnlySpan<byte> payload, out IndoorBikeData? data)
        {
            try
            {
                data = Parse(payload);
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
