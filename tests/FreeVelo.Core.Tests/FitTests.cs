using FreeVelo.Core.Recording;

namespace FreeVelo.Core.Tests;

public class FitTests
{
    private static RideRecording SampleRide(int seconds = 600)
    {
        var start = new DateTime(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc);
        var ride = new RideRecording(start, "4x4 VO2max");
        double distance = 0;
        for (int i = 0; i < seconds; i++)
        {
            if (i == 300) ride.StartLap("Interval 1");
            int power = i < 300 ? 150 : 275;
            double speed = i < 300 ? 8.0 : 9.0;
            distance += speed;
            ride.Add(new RideSample(start.AddSeconds(i + 1), power, 120 + (i / 10), 85, speed, distance, 100 + (i * 0.01), power));
        }

        return ride;
    }

    [Fact]
    public void CrcMatchesReferenceValue()
    {
        // CRC of an empty FIT header-less byte sequence and a known example
        Assert.Equal(0, FitActivityWriter.Crc(Array.Empty<byte>(), 0, 0));
        var data = System.Text.Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0xBB3D, FitActivityWriter.Crc(data, 0, data.Length)); // CRC-16/ARC check value
    }

    [Fact]
    public void FileHasValidHeaderAndCrc()
    {
        byte[] fit = FitActivityWriter.Write(SampleRide(), FitActivityWriter.SessionInfo(SampleRide(), 250));

        Assert.Equal(14, fit[0]);
        Assert.Equal(".FIT", System.Text.Encoding.ASCII.GetString(fit, 8, 4));
        Assert.Equal(BitConverter.ToUInt16(fit, 12), FitActivityWriter.Crc(fit, 0, 12));
        Assert.Equal((uint)(fit.Length - 16), BitConverter.ToUInt32(fit, 4));
        Assert.Equal(BitConverter.ToUInt16(fit, fit.Length - 2), FitActivityWriter.Crc(fit, 0, fit.Length - 2));
    }

    [Fact]
    public void DecodesMessagesAndValues()
    {
        var ride = SampleRide();
        var messages = MiniFitReader.Read(FitActivityWriter.Write(ride, FitActivityWriter.SessionInfo(ride, 250)));

        Assert.Equal(0, messages[0].Global);                                      // file_id first
        Assert.Equal(600, messages.Count(m => m.Global == 20));                   // records
        Assert.Equal(2, messages.Count(m => m.Global == 19));                     // laps
        Assert.Single(messages, m => m.Global == 18);                             // session
        Assert.Equal(34, messages[^1].Global);                                    // activity last

        var record = messages.First(m => m.Global == 20);
        Assert.Equal(150u, record.Fields[7]);                                     // power
        Assert.Equal(800u, record.Fields[5]);                                     // 8 m in cm
        Assert.Equal(8000u, record.Fields[6]);                                    // 8 m/s × 1000
        Assert.Equal(85u, record.Fields[4]);                                      // cadence
        Assert.Equal((uint)((100 + 500) * 5), record.Fields[2]);                  // altitude 100 m

        var session = messages.Single(m => m.Global == 18);
        Assert.Equal(58u, session.Fields[6]);                                     // virtual activity
        Assert.Equal(600_000u, session.Fields[8]);                                // timer time 600 s
        Assert.Equal(213u, session.Fields[20]);                                   // avg power (150+275)/2 → 212.5
        Assert.Equal(250u, session.Fields[45]);                                   // FTP
        Assert.Equal(2u, session.Fields[26]);                                     // laps

        var laps = messages.Where(m => m.Global == 19).ToList();
        Assert.Equal(150u, laps[0].Fields[19]);
        Assert.Equal(275u, laps[1].Fields[19]);
    }

    [Fact]
    public void MissingValuesAreWrittenAsInvalid()
    {
        var start = DateTime.UtcNow;
        var ride = new RideRecording(start, "test");
        ride.Add(new RideSample(start.AddSeconds(1), null, null, null, 5, 5, 0, null));
        var record = MiniFitReader.Read(FitActivityWriter.Write(ride)).First(m => m.Global == 20);

        Assert.Equal(0xFFFFu, record.Fields[7]);
        Assert.Equal(0xFFu, record.Fields[3]);
        Assert.Equal(0xFFu, record.Fields[4]);
    }

    [Fact]
    public void StatsAndEmptyLapRename()
    {
        var ride = new RideRecording(DateTime.UtcNow, "x");
        ride.StartLap("ignored");
        ride.StartLap("Warm-up");
        ride.Add(new RideSample(DateTime.UtcNow, 100, 100, 0, 5, 5, 0, null));
        ride.Add(new RideSample(DateTime.UtcNow, 200, 110, 90, 5, 10, 2, null));

        Assert.Single(ride.Laps);
        Assert.Equal("Warm-up", ride.Laps[0].Name);
        var stats = ride.Stats();
        Assert.Equal(150, stats.AvgPowerW);
        Assert.Equal(90, stats.AvgCadence); // zeros excluded
        Assert.Equal(2, stats.AscentM, 6);
        Assert.Equal(0.3, stats.WorkKj, 6);
    }

    /// <summary>Minimal FIT decoder for tests (little-endian, normal headers only).</summary>
    private static class MiniFitReader
    {
        public sealed class Message
        {
            public int Global;
            public Dictionary<int, uint> Fields = new();
        }

        public static List<Message> Read(byte[] fit)
        {
            var result = new List<Message>();
            var defs = new Dictionary<int, (int Global, List<(int Num, int Size)> Fields)>();
            int pos = fit[0];
            int end = fit.Length - 2;
            while (pos < end)
            {
                byte header = fit[pos++];
                int local = header & 0x0F;
                if ((header & 0x40) != 0)
                {
                    pos += 2; // reserved + architecture
                    int global = BitConverter.ToUInt16(fit, pos);
                    pos += 2;
                    int count = fit[pos++];
                    var fields = new List<(int, int)>();
                    for (int i = 0; i < count; i++)
                    {
                        fields.Add((fit[pos], fit[pos + 1]));
                        pos += 3;
                    }

                    defs[local] = (global, fields);
                }
                else
                {
                    var def = defs[local];
                    var msg = new Message { Global = def.Global };
                    foreach (var (num, size) in def.Fields)
                    {
                        msg.Fields[num] = size switch
                        {
                            1 => fit[pos],
                            2 => BitConverter.ToUInt16(fit, pos),
                            _ => BitConverter.ToUInt32(fit, pos),
                        };
                        pos += size;
                    }

                    result.Add(msg);
                }
            }

            return result;
        }
    }

    [Fact]
    public void VirtualPositionIsWrittenAsGpsInTheOcean()
    {
        var start = new DateTime(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc);
        var ride = new RideRecording(start, "GPS test");
        for (int i = 0; i < 60; i++) ride.Add(new RideSample(start.AddSeconds(i + 1), 200, 130, 90, 8, 8 * i, 100 + i, null, 1000 + (8 * i), -500));
        byte[] fit = FitActivityWriter.Write(ride);

        // Read back like any other file; power / altitude unaffected by the extra fields.
        var records = FreeVelo.Core.History.FitReader.ReadRecords(fit);
        Assert.Equal(60, records.Count);
        Assert.Equal(200, records[10].PowerW);
        Assert.Equal(110, records[10].AltitudeM!.Value, 0);

        var (lat, lon) = FitActivityWriter.ToLatLon(1000, -500);
        Assert.InRange(lat, 47.51, 47.52);
        Assert.InRange(lon, 152.80, 152.82); // Ushishir (Kuril Islands), at Austria's latitude
    }
}
