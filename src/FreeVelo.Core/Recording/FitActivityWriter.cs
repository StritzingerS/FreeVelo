using System;
using System.IO;
using System.Text;
using FreeVelo.Core.Training;

namespace FreeVelo.Core.Recording
{
    /// <summary>Optional values written to the FIT session.</summary>
    public sealed class FitSessionInfo
    {
        public int? FtpW { get; set; }

        public double? NormalizedPowerW { get; set; }

        public double? IntensityFactor { get; set; }

        public double? TrainingStressScore { get; set; }
    }

    /// <summary>
    /// Writes a ride as a Garmin FIT activity file (virtual ride): file_id, timer events, 1-second records,
    /// laps, session and activity – readable by Strava, Garmin Connect, TrainingPeaks, intervals.icu.
    /// Implemented from the public FIT protocol description (no Garmin SDK needed).
    /// </summary>
    public static class FitActivityWriter
    {
        private static readonly DateTime FitEpoch = new DateTime(1989, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        // Global message numbers
        private const ushort MsgFileId = 0;
        private const ushort MsgSession = 18;
        private const ushort MsgLap = 19;
        private const ushort MsgRecord = 20;
        private const ushort MsgEvent = 21;
        private const ushort MsgActivity = 34;

        // Base types
        private const byte Enum = 0x00;
        private const byte UInt8 = 0x02;
        private const byte UInt16 = 0x84;
        private const byte UInt32 = 0x86;
        private const byte UInt32Z = 0x8C;

        // Enum values
        private const byte FileActivity = 4;
        private const ushort ManufacturerDevelopment = 255;
        private const byte SportCycling = 2;
        private const byte SubSportVirtualActivity = 58;
        private const byte EventTimer = 0;
        private const byte EventSession = 8;
        private const byte EventLap = 9;
        private const byte EventActivity = 26;
        private const byte EventTypeStart = 0;
        private const byte EventTypeStop = 1;
        private const byte EventTypeStopAll = 4;

        public static byte[] Write(RideRecording ride, FitSessionInfo? info = null)
        {
            if (ride.Samples.Count == 0) throw new InvalidOperationException("The ride has no samples.");
            info ??= new FitSessionInfo();

            var body = new MemoryStream();
            var w = new BinaryWriter(body);

            uint start = Timestamp(ride.StartUtc);
            uint end = Timestamp(ride.EndUtc);

            // file_id
            Define(w, 0, MsgFileId, (0, 1, Enum), (1, 2, UInt16), (2, 2, UInt16), (3, 4, UInt32Z), (4, 4, UInt32));
            w.Write((byte)0);
            w.Write(FileActivity);
            w.Write(ManufacturerDevelopment);
            w.Write((ushort)0);
            w.Write((uint)(start | 1)); // serial number (uint32z must not be 0)
            w.Write(start);

            // event: timer start
            Define(w, 1, MsgEvent, (253, 4, UInt32), (0, 1, Enum), (1, 1, Enum));
            WriteEvent(w, start, EventTimer, EventTypeStart);

            // records
            Define(w, 2, MsgRecord, (253, 4, UInt32), (5, 4, UInt32), (6, 2, UInt16), (2, 2, UInt16), (7, 2, UInt16), (3, 1, UInt8), (4, 1, UInt8));
            foreach (var s in ride.Samples)
            {
                w.Write((byte)2);
                w.Write(Timestamp(s.TimeUtc));
                w.Write(Scale32(s.DistanceM, 100));
                w.Write(Scale16(s.SpeedMps, 1000));
                w.Write(Scale16(s.AltitudeM + 500, 5));
                w.Write(s.PowerW.HasValue ? (ushort)Math.Max(0, Math.Min(65534, s.PowerW.Value)) : ushort.MaxValue);
                w.Write(s.HeartRateBpm.HasValue ? (byte)Math.Max(0, Math.Min(254, s.HeartRateBpm.Value)) : byte.MaxValue);
                w.Write(s.CadenceRpm.HasValue ? (byte)Math.Max(0, Math.Min(254, s.CadenceRpm.Value)) : byte.MaxValue);
            }

            // event: timer stop
            w.Write((byte)1);
            w.Write(end);
            w.Write(EventTimer);
            w.Write(EventTypeStopAll);

            // laps
            Define(
                w, 3, MsgLap,
                (254, 2, UInt16), (253, 4, UInt32), (0, 1, Enum), (1, 1, Enum), (2, 4, UInt32), (7, 4, UInt32), (8, 4, UInt32),
                (9, 4, UInt32), (13, 2, UInt16), (14, 2, UInt16), (15, 1, UInt8), (16, 1, UInt8), (17, 1, UInt8), (18, 1, UInt8),
                (19, 2, UInt16), (20, 2, UInt16), (21, 2, UInt16), (11, 2, UInt16), (24, 1, Enum), (25, 1, Enum), (39, 1, Enum));
            for (int i = 0; i < ride.Laps.Count; i++)
            {
                var (from, to) = ride.LapRange(i);
                if (to <= from) continue;
                var stats = ride.Stats(from, to);
                // A sample covers the second before its timestamp, so a lap starts at the previous sample.
                uint lapStart = from == 0 ? start : Timestamp(ride.Samples[from - 1].TimeUtc);
                uint lapEnd = Timestamp(ride.Samples[to - 1].TimeUtc);
                w.Write((byte)3);
                w.Write((ushort)i);
                w.Write(lapEnd);
                w.Write(EventLap);
                w.Write(EventTypeStop);
                w.Write(lapStart);
                w.Write((uint)((lapEnd - lapStart) * 1000));
                w.Write((uint)(stats.Seconds * 1000));
                w.Write(Scale32(stats.DistanceM, 100));
                w.Write(Scale16(stats.AvgSpeedMps, 1000));
                w.Write(Scale16(stats.MaxSpeedMps, 1000));
                w.Write(Byte(stats.AvgHeartRate));
                w.Write(Byte(stats.MaxHeartRate));
                w.Write(Byte(stats.AvgCadence));
                w.Write(Byte(stats.MaxCadence));
                w.Write(UShort(stats.AvgPowerW));
                w.Write(UShort(stats.MaxPowerW));
                w.Write((ushort)Math.Round(stats.AscentM));
                w.Write((ushort)Math.Round(stats.WorkKj));
                w.Write((byte)0); // lap trigger: manual
                w.Write(SportCycling);
                w.Write(SubSportVirtualActivity);
            }

            // session
            var total = ride.Stats();
            int lapCount = 0;
            for (int i = 0; i < ride.Laps.Count; i++)
            {
                var (from, to) = ride.LapRange(i);
                if (to > from) lapCount++;
            }

            Define(
                w, 4, MsgSession,
                (254, 2, UInt16), (253, 4, UInt32), (0, 1, Enum), (1, 1, Enum), (2, 4, UInt32), (5, 1, Enum), (6, 1, Enum),
                (7, 4, UInt32), (8, 4, UInt32), (9, 4, UInt32), (11, 2, UInt16), (14, 2, UInt16), (15, 2, UInt16),
                (16, 1, UInt8), (17, 1, UInt8), (18, 1, UInt8), (19, 1, UInt8), (20, 2, UInt16), (21, 2, UInt16),
                (22, 2, UInt16), (25, 2, UInt16), (26, 2, UInt16), (28, 1, Enum),
                (34, 2, UInt16), (35, 2, UInt16), (36, 2, UInt16), (45, 2, UInt16));
            w.Write((byte)4);
            w.Write((ushort)0);
            w.Write(end);
            w.Write(EventSession);
            w.Write(EventTypeStop);
            w.Write(start);
            w.Write(SportCycling);
            w.Write(SubSportVirtualActivity);
            w.Write((uint)((end - start) * 1000));
            w.Write((uint)(total.Seconds * 1000));
            w.Write(Scale32(total.DistanceM, 100));
            w.Write((ushort)Math.Round(total.WorkKj));
            w.Write(Scale16(total.AvgSpeedMps, 1000));
            w.Write(Scale16(total.MaxSpeedMps, 1000));
            w.Write(Byte(total.AvgHeartRate));
            w.Write(Byte(total.MaxHeartRate));
            w.Write(Byte(total.AvgCadence));
            w.Write(Byte(total.MaxCadence));
            w.Write(UShort(total.AvgPowerW));
            w.Write(UShort(total.MaxPowerW));
            w.Write((ushort)Math.Round(total.AscentM));
            w.Write((ushort)0);
            w.Write((ushort)lapCount);
            w.Write((byte)0); // trigger: activity end
            w.Write(UShort(info.NormalizedPowerW.HasValue ? (int?)Math.Round(info.NormalizedPowerW.Value) : null));
            w.Write(UShort(info.TrainingStressScore.HasValue ? (int?)Math.Round(info.TrainingStressScore.Value * 10) : null));
            w.Write(UShort(info.IntensityFactor.HasValue ? (int?)Math.Round(info.IntensityFactor.Value * 1000) : null));
            w.Write(UShort(info.FtpW));

            // activity
            Define(w, 5, MsgActivity, (253, 4, UInt32), (0, 4, UInt32), (1, 2, UInt16), (2, 1, Enum), (3, 1, Enum), (4, 1, Enum), (5, 4, UInt32));
            w.Write((byte)5);
            w.Write(end);
            w.Write((uint)(total.Seconds * 1000));
            w.Write((ushort)1);
            w.Write((byte)0); // manual
            w.Write(EventActivity);
            w.Write(EventTypeStop);
            long localOffset = (long)TimeZoneInfo.Local.GetUtcOffset(ride.EndUtc).TotalSeconds;
            w.Write((uint)(end + localOffset));

            w.Flush();
            byte[] data = body.ToArray();

            var file = new MemoryStream();
            var fw = new BinaryWriter(file);
            var header = new byte[12];
            header[0] = 14;          // header size
            header[1] = 0x20;        // protocol version 2.0
            BitConverter.GetBytes((ushort)2132).CopyTo(header, 2); // profile version 21.32
            BitConverter.GetBytes((uint)data.Length).CopyTo(header, 4);
            Encoding.ASCII.GetBytes(".FIT").CopyTo(header, 8);
            fw.Write(header);
            fw.Write(Crc(header, 0, header.Length));
            fw.Write(data);
            fw.Flush();

            byte[] all = file.ToArray();
            ushort crc = Crc(all, 0, all.Length);
            var result = new byte[all.Length + 2];
            all.CopyTo(result, 0);
            BitConverter.GetBytes(crc).CopyTo(result, all.Length);
            return result;
        }

        /// <summary>FIT CRC-16 (as in the FIT SDK).</summary>
        public static ushort Crc(byte[] data, int offset, int count)
        {
            ushort[] table =
            {
                0x0000, 0xCC01, 0xD801, 0x1400, 0xF001, 0x3C00, 0x2800, 0xE401,
                0xA001, 0x6C00, 0x7800, 0xB401, 0x5000, 0x9C01, 0x8801, 0x4400,
            };
            ushort crc = 0;
            for (int i = offset; i < offset + count; i++)
            {
                byte b = data[i];
                ushort tmp = table[crc & 0xF];
                crc = (ushort)((crc >> 4) & 0x0FFF);
                crc = (ushort)(crc ^ tmp ^ table[b & 0xF]);
                tmp = table[crc & 0xF];
                crc = (ushort)((crc >> 4) & 0x0FFF);
                crc = (ushort)(crc ^ tmp ^ table[(b >> 4) & 0xF]);
            }

            return crc;
        }

        public static uint Timestamp(DateTime utc) =>
            (uint)Math.Max(0, (DateTime.SpecifyKind(utc, DateTimeKind.Utc) - FitEpoch).TotalSeconds);

        private static void WriteEvent(BinaryWriter w, uint timestamp, byte ev, byte type)
        {
            w.Write((byte)1);
            w.Write(timestamp);
            w.Write(ev);
            w.Write(type);
        }

        private static void Define(BinaryWriter w, byte localType, ushort globalMessage, params (byte Field, byte Size, byte BaseType)[] fields)
        {
            w.Write((byte)(0x40 | localType));
            w.Write((byte)0);   // reserved
            w.Write((byte)0);   // little endian
            w.Write(globalMessage);
            w.Write((byte)fields.Length);
            foreach (var (field, size, baseType) in fields)
            {
                w.Write(field);
                w.Write(size);
                w.Write(baseType);
            }
        }

        private static uint Scale32(double value, double scale) => (uint)Math.Max(0, Math.Min(uint.MaxValue - 1, Math.Round(value * scale)));

        private static ushort Scale16(double value, double scale) => (ushort)Math.Max(0, Math.Min(ushort.MaxValue - 1, Math.Round(value * scale)));

        private static byte Byte(int? value) => value.HasValue ? (byte)Math.Max(0, Math.Min(254, value.Value)) : byte.MaxValue;

        private static ushort UShort(int? value) => value.HasValue ? (ushort)Math.Max(0, Math.Min(65534, value.Value)) : ushort.MaxValue;

        /// <summary>Session info from the recorded power (NP, IF, TSS) for a given FTP.</summary>
        public static FitSessionInfo SessionInfo(RideRecording ride, int ftpW)
        {
            var power = ride.PowerSeries();
            double np = PowerMetrics.NormalizedPower(power);
            return new FitSessionInfo
            {
                FtpW = ftpW,
                NormalizedPowerW = np,
                IntensityFactor = PowerMetrics.IntensityFactor(np, ftpW),
                TrainingStressScore = PowerMetrics.TrainingStressScore(power.Count, np, ftpW),
            };
        }
    }
}
