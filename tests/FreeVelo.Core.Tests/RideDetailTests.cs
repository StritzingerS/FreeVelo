using FreeVelo.Core.History;
using FreeVelo.Core.Recording;
using FreeVelo.Core.Training;

namespace FreeVelo.Core.Tests;

public class RideDetailTests
{
    [Fact]
    public void HeartRateZonesFromMaxAndLthr()
    {
        var max = HeartRateZoneModel.Create(HeartRateZoneBasis.MaxHr, 190, 170);
        Assert.Equal(5, max.Zones.Count);
        Assert.Equal((114, 133), (max.Zones[1].LowerBpm, max.Zones[2].LowerBpm)); // 60 %, 70 % of 190
        Assert.Equal(4, max.ZoneFor(160).Number); // 84 %
        Assert.Equal(1, max.ZoneFor(90).Number);

        var lthr = HeartRateZoneModel.Create(HeartRateZoneBasis.Lthr, 190, 170);
        Assert.Equal(7, lthr.Zones.Count);
        Assert.Equal(5, lthr.ZoneFor(171).Number); // 100–102 %
        Assert.Equal(7, lthr.ZoneFor(185).Number);
        Assert.Null(lthr.Zones[^1].UpperBpm);

        var time = max.TimeInZones(new int?[] { 100, 120, 120, null, 0, 175 });
        Assert.Equal((1, 2, 1), (time[1], time[2], time[5]));
    }

    [Fact]
    public void DetailWithLapsZonesAndGraph()
    {
        var start = new DateTime(2026, 11, 7, 17, 0, 0, DateTimeKind.Utc);
        var ride = new RideRecording(start, "2 blocks");
        for (int i = 0; i < 1200; i++)
        {
            if (i == 600) ride.StartLap("hard");
            bool hard = i >= 600;
            ride.Add(new RideSample(start.AddSeconds(i + 1), hard ? 290 : 150, hard ? 170 : 130, 90, 9, 9 * i, 100, null));
        }

        var activity = FitReader.ReadActivity(FitActivityWriter.Write(ride));
        Assert.Equal(1200, activity.Records.Count);
        Assert.Equal(2, activity.LapStarts.Count);

        var detail = RideDetail.From(activity, 250, PowerZoneModel.Coggan(), HeartRateZoneModel.Create(HeartRateZoneBasis.MaxHr, 190, 170), 120);
        Assert.Equal(2, detail.Laps.Count);
        Assert.Equal((600, 150, 130), (detail.Laps[0].DurationS, detail.Laps[0].AvgPowerW, detail.Laps[0].AvgHeartRate));
        Assert.Equal((600, 600, 290), (detail.Laps[1].StartS, detail.Laps[1].DurationS, detail.Laps[1].AvgPowerW));
        Assert.Equal(120, detail.Graph.Count);
        Assert.True(detail.HasHeartRate);

        // 150 W = 60 % FTP (zone 2), 290 W = 116 % FTP (zone 5); 130 bpm = 68 % (zone 2), 170 bpm = 89 % (zone 4)
        Assert.Equal((600, 600), (detail.TimeInPowerZones[2], detail.TimeInPowerZones[5]));
        Assert.Equal((600, 600), (detail.TimeInHeartRateZones[2], detail.TimeInHeartRateZones[4]));
    }
}
