using FreeVelo.Core.History;
using FreeVelo.Core.Recording;

namespace FreeVelo.Core.Tests;

public class HistoryTests
{
    private static byte[] Data(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name));

    [Fact]
    public void ReadsBackWhatTheFitWriterWrote()
    {
        var start = new DateTime(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc);
        var ride = new RideRecording(start, "Test");
        for (int i = 0; i < 600; i++)
        {
            ride.Add(new RideSample(start.AddSeconds(i), 200 + (i % 10), 140, 90, 9.5, 9.5 * i, 100 + (i < 300 ? i * 0.1 : 30), 200));
        }

        var records = FitReader.ReadRecords(FitActivityWriter.Write(ride));
        Assert.Equal(600, records.Count);
        Assert.Equal(start, records[0].TimeUtc);
        Assert.Equal(209, records[9].PowerW);
        Assert.Equal((140, 90), (records[5].HeartRateBpm, records[5].CadenceRpm));
        Assert.Equal(9.5 * 599, records[599].DistanceM!.Value, 1);
        Assert.Equal(9.5, records[10].SpeedMps!.Value, 2);

        var summary = RideAnalysis.Summarize("x.fit", "Test", start, records, 250);
        Assert.Equal((600, 204, 209), (summary.durationS, summary.avgPowerW, summary.maxPowerW));
        Assert.InRange(summary.ascentM, 29, 31);
        Assert.InRange(summary.tss, 10, 12); // 10 min at IF ≈ 0.82
        Assert.Equal(207, summary.powerCurve[0]); // best 5 s: 205…209 W
        Assert.Equal(0, summary.powerCurve[^1]); // no 60-minute value in a 10-minute ride
    }

    [Fact]
    public void ImportsTheRealRampTest()
    {
        var record = RideAnalysis.FromFit("2026-11-06_16-24_FTP-Rampentest.fit", Data("2026-11-06_16-24_FTP-Rampentest.fit"), 250);
        Assert.Equal("FTP Rampentest", record.title);
        Assert.True(record.imported);
        Assert.InRange(record.durationS, 960, 990); // 16 minutes
        int best60 = record.powerCurve[Array.IndexOf(RideAnalysis.CurveDurations, 60)];
        Assert.InRange(best60, 360, 364); // → FTP 272 (checked by hand before)
        Assert.InRange(record.maxHeartRate, 170, 180);
        Assert.Equal(new DateTime(2026, 11, 6), record.StartUtc.Date);
    }

    [Fact]
    public void FitnessFollowsTheUsualFormulas()
    {
        var day0 = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var rides = Enumerable.Range(0, 28).Select(d => new RideRecord { startUtc = new DateTimeOffset(day0.AddDays(d)).ToUnixTimeSeconds(), tss = 100 }).ToList();
        var days = TrainingLoad.Fitness(rides, day0.AddDays(34), t => t);

        Assert.Equal(35, days.Count);
        var last = days[27];
        Assert.Equal(100 * (1 - Math.Pow(1 - 1.0 / 42, 28)), last.Ctl, 6);
        Assert.Equal(100 * (1 - Math.Pow(1 - 1.0 / 7, 28)), last.Atl, 6);
        Assert.True(last.Tsb < -30); // four hard weeks in a row: tired
        Assert.True(days[^1].Tsb > 0); // a week off: fresh again
        Assert.True(days[^1].Atl < days[^1].Ctl);
    }

    [Fact]
    public void WeeklyTotalsAndBestCurve()
    {
        var monday = new DateTime(2026, 11, 2);
        RideRecord Ride(DateTime t, int s, int[] curve) => new() { startUtc = new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeSeconds(), durationS = s, tss = s / 36.0, powerCurve = curve };
        var rides = new[]
        {
            Ride(monday.AddHours(18), 3600, new[] { 500, 400 }),
            Ride(monday.AddDays(3).AddHours(18), 1800, new[] { 450, 420 }),
            Ride(monday.AddDays(-5), 2700, new[] { 300, 250 }),
        };

        var weeks = TrainingLoad.Weekly(rides, monday.AddDays(4), 2, t => t);
        Assert.Equal((monday.AddDays(-7), 1, 2700), (weeks[0].Monday, weeks[0].Rides, weeks[0].Seconds));
        Assert.Equal((monday, 2, 5400), (weeks[1].Monday, weeks[1].Rides, weeks[1].Seconds));
        Assert.Equal(new[] { 500, 420 }, TrainingLoad.BestPowerCurve(rides).Take(2));
    }
}
