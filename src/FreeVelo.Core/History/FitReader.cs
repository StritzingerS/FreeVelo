using System;
using System.Collections.Generic;
using System.IO;

namespace FreeVelo.Core.History
{
    /// <summary>One record (usually one second) read from a FIT activity.</summary>
    public readonly struct FitRecord
    {
        public FitRecord(DateTime timeUtc, int? powerW, int? heartRateBpm, int? cadenceRpm, double? distanceM, double? speedMps, double? altitudeM)
        {
            TimeUtc = timeUtc;
            PowerW = powerW;
            HeartRateBpm = heartRateBpm;
            CadenceRpm = cadenceRpm;
            DistanceM = distanceM;
            SpeedMps = speedMps;
            AltitudeM = altitudeM;
        }

        public DateTime TimeUtc { get; }
        public int? PowerW { get; }
        public int? HeartRateBpm { get; }
        public int? CadenceRpm { get; }
        public double? DistanceM { get; }
        public double? SpeedMps { get; }
        public double? AltitudeM { get; }
    }

    /// <summary>
    /// Minimal FIT reader for activity files: the "record" messages (time, power, heart rate, cadence, distance,
    /// speed, altitude). Enough to rebuild the history from FIT files – FreeVelo's own and other apps'.
    /// Handles normal and compressed-timestamp headers, both byte orders and developer fields (skipped).
    /// </summary>
    /// <summary>Records and lap starts of a FIT activity.</summary>
    public sealed class FitActivity
    {
        public List<FitRecord> Records { get; } = new List<FitRecord>();

        /// <summary>Start of each lap (FreeVelo writes one lap per workout block).</summary>
        public List<DateTime> LapStarts { get; } = new List<DateTime>();
    }

    public static class FitReader
    {
        private const int RecordMessage = 20;
        private const int LapMessage = 19;
        private static readonly DateTime FitEpoch = new DateTime(1989, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Definition
        {
            public int Global;
            public bool BigEndian;
            public readonly List<(int Field, int Size)> Fields = new List<(int, int)>();
            public int DeveloperBytes;
        }

        /// <summary>Reads the records; throws <see cref="InvalidDataException"/> if it is not a FIT file.</summary>
        public static List<FitRecord> ReadRecords(byte[] data) => ReadActivity(data).Records;

        /// <summary>Reads records and laps; throws <see cref="InvalidDataException"/> if it is not a FIT file.</summary>
        public static FitActivity ReadActivity(byte[] data)
        {
            if (data.Length < 14 || data[8] != (byte)'.' || data[9] != (byte)'F' || data[10] != (byte)'I' || data[11] != (byte)'T')
            {
                throw new InvalidDataException("Not a FIT file");
            }

            int headerSize = data[0];
            int dataSize = BitConverter.ToInt32(data, 4);
            int end = Math.Min(data.Length, headerSize + dataSize);
            var definitions = new Definition?[16];
            var activity = new FitActivity();
            var records = activity.Records;
            uint lastTimestamp = 0;
            int i = headerSize;

            while (i < end)
            {
                byte header = data[i++];
                if ((header & 0x80) != 0)
                {
                    // Compressed timestamp header: 5-bit time offset to the last full timestamp.
                    int local = (header >> 5) & 0x03;
                    uint offset = (uint)(header & 0x1F);
                    uint ts = (lastTimestamp & ~0x1Fu) + offset;
                    if (ts < lastTimestamp) ts += 0x20;
                    lastTimestamp = ts;
                    var def = definitions[local] ?? throw new InvalidDataException("Data before definition");
                    i = ReadData(data, i, def, ts, activity, ref lastTimestamp);
                    continue;
                }

                int localType = header & 0x0F;
                if ((header & 0x40) != 0)
                {
                    var def = new Definition();
                    i++; // reserved
                    def.BigEndian = data[i++] == 1;
                    def.Global = def.BigEndian ? (data[i] << 8) | data[i + 1] : data[i] | (data[i + 1] << 8);
                    i += 2;
                    int count = data[i++];
                    for (int f = 0; f < count; f++, i += 3) def.Fields.Add((data[i], data[i + 1]));
                    if ((header & 0x20) != 0)
                    {
                        int devCount = data[i++];
                        for (int f = 0; f < devCount; f++, i += 3) def.DeveloperBytes += data[i + 1];
                    }

                    definitions[localType] = def;
                }
                else
                {
                    var def = definitions[localType] ?? throw new InvalidDataException("Data before definition");
                    i = ReadData(data, i, def, null, activity, ref lastTimestamp);
                }
            }

            activity.LapStarts.Sort();
            return activity;
        }

        private static int ReadData(byte[] data, int i, Definition def, uint? compressedTs, FitActivity activity, ref uint lastTimestamp)
        {
            uint? timestamp = compressedTs;
            uint? lapStart = null;
            uint? power = null, hr = null, cadence = null, distance = null, speed = null, altitude = null;
            foreach (var (field, size) in def.Fields)
            {
                if (i + size > data.Length) throw new InvalidDataException("Truncated FIT file");
                if (def.Global == LapMessage && field == 2) lapStart = Read(data, i, size, def.BigEndian);
                if (def.Global == RecordMessage || field == 253)
                {
                    uint? value = Read(data, i, size, def.BigEndian);
                    switch (field)
                    {
                        case 253:
                            if (value.HasValue)
                            {
                                timestamp = value;
                                lastTimestamp = value.Value;
                            }

                            break;
                        case 7: power = value; break;
                        case 3: hr = value; break;
                        case 4: cadence = value; break;
                        case 5: distance = value; break;
                        case 6: speed = value; break;
                        case 2: altitude = value; break;
                    }
                }

                i += size;
            }

            i += def.DeveloperBytes;
            if (def.Global == LapMessage && lapStart.HasValue) activity.LapStarts.Add(FitEpoch.AddSeconds(lapStart.Value));
            if (def.Global == RecordMessage && timestamp.HasValue)
            {
                activity.Records.Add(new FitRecord(
                    FitEpoch.AddSeconds(timestamp.Value),
                    (int?)power,
                    (int?)hr,
                    (int?)cadence,
                    distance / 100.0,
                    speed / 1000.0,
                    altitude.HasValue ? altitude.Value / 5.0 - 500 : (double?)null));
            }

            return i;
        }

        /// <summary>Unsigned field value, null for FIT's "invalid" (all bits set) or unsupported sizes.</summary>
        private static uint? Read(byte[] d, int i, int size, bool bigEndian)
        {
            switch (size)
            {
                case 1:
                    return d[i] == 0xFF ? (uint?)null : d[i];
                case 2:
                {
                    uint v = bigEndian ? (uint)((d[i] << 8) | d[i + 1]) : (uint)(d[i] | (d[i + 1] << 8));
                    return v == 0xFFFF ? (uint?)null : v;
                }

                case 4:
                {
                    uint v = bigEndian
                        ? ((uint)d[i] << 24) | ((uint)d[i + 1] << 16) | ((uint)d[i + 2] << 8) | d[i + 3]
                        : d[i] | ((uint)d[i + 1] << 8) | ((uint)d[i + 2] << 16) | ((uint)d[i + 3] << 24);
                    return v == 0xFFFFFFFF ? (uint?)null : v;
                }

                default:
                    return null;
            }
        }
    }
}
