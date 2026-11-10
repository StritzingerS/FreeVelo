using FreeVelo.Core.History;
using FreeVelo.Shared.Strava;

namespace FreeVelo.Server.Tests;

/// <summary>Reading the rider's rides from Strava for the history.</summary>
public class StravaImportTests
{
    [Fact]
    public void ParsesTheActivityList()
    {
        // Shape of GET /athlete/activities (SummaryActivity), trimmed
        const string json = """
            [{"id":12345678901,"name":"MTB Hausrunde","type":"Ride","sport_type":"MountainBikeRide","start_date":"2026-11-08T09:15:02Z",
              "start_date_local":"2026-11-08T10:15:02Z","elapsed_time":5420,"moving_time":5101,"distance":38211.4,"device_watts":true,
              "average_watts":176.0,"trainer":false,"external_id":"garmin_ping_123.fit","map":{"id":"a1","summary_polyline":"xyz"}},
             {"id":12345678902,"name":"Abendrunde","type":"Ride","start_date":"2026-11-07T16:00:00Z","moving_time":3600,"average_watts":150.0,"external_id":null},
             {"id":12345678903,"name":"Pendeln","type":"EBikeRide","start_date":"2026-11-07T07:00:00Z","moving_time":1200,"device_watts":true}]
            """;
        var list = StravaReader.ParseActivities(json, new TestJson());
        Assert.Equal(3, list.Count);
        Assert.Equal(12345678901, list[0].id);
        Assert.Equal(new DateTime(2026, 11, 8, 9, 15, 2, DateTimeKind.Utc), list[0].StartUtc);
        Assert.True(list[0].IsRideWithPower);
        Assert.False(list[1].IsRideWithPower); // power estimated by Strava, no power meter
        Assert.False(list[2].IsRideWithPower); // e-bike
        Assert.Empty(StravaReader.ParseActivities("[]", new TestJson()));
    }

    [Fact]
    public void ParsesStreamsWithGaps()
    {
        const string json = """
            {"time":{"data":[0,1,2,5],"series_type":"distance","original_size":4,"resolution":"high"},
             "watts":{"data":[200,null,210,220],"series_type":"distance","original_size":4,"resolution":"high"},
             "distance":{"data":[0.0,8.1,16.3,40.5],"series_type":"distance","original_size":4,"resolution":"high"}}
            """;
        var s = StravaStreams.Parse(json);
        Assert.Equal(4, s.Count);
        Assert.Equal(new double[] { 0, 1, 2, 5 }, s.Time);
        Assert.Null(StravaStreams.At(s.Watts, 1)); // a power drop-out
        Assert.Equal(210, StravaStreams.At(s.Watts, 2));
        Assert.Empty(s.HeartRate); // no heart rate strap
        Assert.Null(StravaStreams.At(s.HeartRate, 0));
    }

    [Fact]
    public void FillsSmartRecordingGapsButNotPauses()
    {
        var t0 = new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
        FitRecord R(int s, int w, double d) => new(t0.AddSeconds(s), w, null, null, d, null, null);
        var filled = RideAnalysis.OneHz(new[] { R(0, 200, 0), R(4, 220, 40), R(5, 230, 50), R(65, 100, 60) });
        // 0..5 at 1 Hz (gap of 4 s filled with the previous power, distance interpolated), then the pause stays a gap
        Assert.Equal(7, filled.Count);
        Assert.Equal(200, filled[2].PowerW);
        Assert.Equal(20, filled[2].DistanceM!.Value, 6);
        Assert.Equal(t0.AddSeconds(65), filled[6].TimeUtc);
    }

    [Fact]
    public void SkipsRidesAlreadyInTheHistory()
    {
        var start = new DateTime(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc);
        var trainer = new RideRecord { startUtc = new DateTimeOffset(start).ToUnixTimeSeconds(), durationS = 3600 };
        // The same ride also recorded on a bike computer: starts 40 s later, 2 % shorter → duplicate
        Assert.True(TrainingLoad.IsDuplicate(new[] { trainer }, start.AddSeconds(40), 3530));
        // A ride later that day → new
        Assert.False(TrainingLoad.IsDuplicate(new[] { trainer }, start.AddHours(3), 3600));
        // Same start but much shorter (e.g. a warm-up recorded separately) → new
        Assert.False(TrainingLoad.IsDuplicate(new[] { trainer }, start, 900));

        var ftp = new[] { new FtpChange { timeUtc = new DateTimeOffset(start.AddDays(-30)).ToUnixTimeSeconds(), ftpW = 240 },
                          new FtpChange { timeUtc = new DateTimeOffset(start.AddDays(5)).ToUnixTimeSeconds(), ftpW = 255 } };
        Assert.Equal(240, TrainingLoad.FtpOn(ftp, start, 250)); // the FTP of that day, not today's
    }
}
