using System.IO.Compression;
using System.Text;
using FreeVelo.Core.History;
using FreeVelo.Shared.Garmin;

namespace FreeVelo.Server.Tests;

public class GarminTests
{
    [Fact]
    public void ParsesTheActivitySearch()
    {
        // Shape of /activitylist-service/activities/search/activities (SummarizedActivityDTO), trimmed
        const string json = """
            [{"activityId":21544123456,"activityName":"MTB Hausrunde","startTimeGMT":"2026-11-08 09:15:02","startTimeLocal":"2026-11-08 10:15:02",
              "activityType":{"typeId":5,"typeKey":"mountain_biking","parentTypeId":2},
              "duration":5420.3,"movingDuration":5101.0,"distance":38211.4,"elevationGain":712.0,"averageHR":141.0,
              "avgPower":176.0,"normPower":214.0,"trainingStressScore":96.4,"manufacturer":"GARMIN"},
             {"activityId":21544000001,"activityName":"Commute","startTimeGMT":"2026-11-07 07:00:00","duration":1200.0,"distance":6000.0}]
            """;
        var list = GarminPersonalApi.ParseActivities(json, new TestJson());
        Assert.Equal(2, list.Count);
        var mtb = list[0];
        Assert.Equal(21544123456, mtb.activityId);
        Assert.Equal(new DateTime(2026, 11, 8, 9, 15, 2, DateTimeKind.Utc), mtb.StartUtc);
        Assert.True(mtb.HasPower);
        Assert.Equal(214, mtb.normPower);
        Assert.False(list[1].HasPower); // no power meter: not imported
        Assert.Empty(GarminPersonalApi.ParseActivities("[]", new TestJson()));
    }

    [Fact]
    public void TakesTheFitFileOutOfTheDownload()
    {
        byte[] fit = { 14, 0x10, 0, 0, 0, 0, 0, 0, (byte)'.', (byte)'F', (byte)'I', (byte)'T', 1, 2, 3 };
        using var zipped = new MemoryStream();
        using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("21544123456_ACTIVITY.fit").Open();
            s.Write(fit);
        }

        Assert.Equal(fit, GarminPersonalApi.FitFromDownload(zipped.ToArray()));
        Assert.Equal(fit, GarminPersonalApi.FitFromDownload(fit)); // a bare FIT file
        Assert.Null(GarminPersonalApi.FitFromDownload(Encoding.UTF8.GetBytes("<gpx></gpx>")));
    }

    [Fact]
    public void ReadsTheTokenExpiry()
    {
        string Part(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string jwt = Part("{\"alg\":\"RS256\"}") + "." + Part("{\"sub\":\"1\",\"exp\":1794200000}") + ".sig";
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794200000).UtcDateTime, GarminPersonalApi.JwtExpiry(jwt));
        Assert.Null(GarminPersonalApi.JwtExpiry("not-a-jwt"));
    }

    [Fact]
    public void SkipsRidesAlreadyInTheHistory()
    {
        var start = new DateTime(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc);
        var trainer = new RideRecord { startUtc = new DateTimeOffset(start).ToUnixTimeSeconds(), durationS = 3600 };
        // The same ride recorded on the Garmin: starts 40 s later, 2 % shorter → duplicate
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
