using FreeVelo.Core.Physics;
using FreeVelo.Core.Training;
using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Tests;

public class RiderPhysicsTests
{
    [Fact]
    public void FlatRoadSpeedIsRealistic()
    {
        var p = new RiderPhysics(); // 75 + 8 kg, CdA 0.32
        double kmh = p.SteadyStateSpeed(200, 0, 0.004) * 3.6;
        Assert.InRange(kmh, 31, 35);
    }

    [Fact]
    public void ClimbingIsSlowAndWeightMatters()
    {
        var light = new RiderPhysics { RiderMassKg = 60 };
        var heavy = new RiderPhysics { RiderMassKg = 90 };

        double lightKmh = light.SteadyStateSpeed(250, 8, 0.004) * 3.6;
        double heavyKmh = heavy.SteadyStateSpeed(250, 8, 0.004) * 3.6;

        Assert.InRange(lightKmh, 12, 17);
        Assert.True(lightKmh > heavyKmh + 2);
    }

    [Fact]
    public void GravelIsSlowerThanAsphalt()
    {
        var p = new RiderPhysics();
        double asphalt = p.SteadyStateSpeed(200, 0, RoadSurfaces.RollingResistance(RoadSurface.Asphalt));
        double gravel = p.SteadyStateSpeed(200, 0, RoadSurfaces.RollingResistance(RoadSurface.Gravel));
        Assert.True(asphalt > gravel);
    }

    [Fact]
    public void StepConvergesToSteadyState()
    {
        var p = new RiderPhysics();
        for (int i = 0; i < 120; i++) p.Step(200, 0, 0.004, 1.0);
        Assert.Equal(p.SteadyStateSpeed(200, 0, 0.004), p.SpeedMps, 1);
    }

    [Fact]
    public void CoastingDownhillAccelerates()
    {
        var p = new RiderPhysics { SpeedMps = 5 };
        p.Step(0, -6, 0.004, 5.0);
        Assert.True(p.SpeedMps > 5);
    }

    [Fact]
    public void SpeedNeverNegative()
    {
        var p = new RiderPhysics { SpeedMps = 2 };
        p.Step(0, 15, 0.004, 10.0);
        Assert.Equal(0, p.SpeedMps);
    }

    [Fact]
    public void PowerForSpeedIsInverseOfSteadyState()
    {
        var p = new RiderPhysics();
        double v = p.SteadyStateSpeed(300, 3, 0.004);
        Assert.Equal(300, p.PowerForSpeed(v, 3, 0.004), 0);
    }
}

public class WorkoutModelTests
{
    [Fact]
    public void FlattenExpandsNestedRepeats()
    {
        var w = BuiltInWorkouts.FortyTwenties();
        var segments = w.Flatten();

        // warm-up (3) + 3 sets of 10 × 40/20 without the last "20 off" (19), the first two with the 5 min rest (+1),
        // + cool-down (1): the built-in intervals skip their last recovery
        Assert.Equal(3 + 20 + 20 + 19 + 1, segments.Count);
        Assert.Equal(w.TotalDurationS, segments.Last().EndS);
        Assert.All(segments.Zip(segments.Skip(1)), p => Assert.Equal(p.First.EndS, p.Second.StartS));
    }

    [Fact]
    public void RepeatInfoIsSet()
    {
        var segments = BuiltInWorkouts.Vo2Max4x4().Flatten();
        var intervals = segments.Where(s => s.Block is SteadyBlock b && b.Target.Value == 113).ToList();

        Assert.Equal(4, intervals.Count);
        Assert.Equal("1/4", intervals[0].Repeat.ToString());
        Assert.Equal("4/4", intervals[3].Repeat.ToString());
    }

    [Fact]
    public void RampTargetIsInterpolated()
    {
        var seg = new Workout("t", "t") { Blocks = { new RampBlock(100, PowerTarget.Percent(50), PowerTarget.Percent(100)) } }.Flatten()[0];
        Assert.Equal(100, seg.BaseTargetW(0, 200));
        Assert.Equal(150, seg.BaseTargetW(50, 200));
        Assert.Equal(200, seg.BaseTargetW(100, 200));
    }

    [Fact]
    public void AbsoluteAndRelativeTargets()
    {
        Assert.Equal(250, PowerTarget.Percent(100).ToWatts(250));
        Assert.Equal(180, PowerTarget.Watts(180).ToWatts(250));
        Assert.Equal(72, PowerTarget.Watts(180).ToPercent(250));
    }

    [Fact]
    public void LocalizedTextFallsBackToEnglish()
    {
        var t = new LocalizedText("Recover", "Erholen");
        Assert.Equal("Erholen", t.Get("de"));
        Assert.Equal("Recover", t.Get("fr"));
    }

    [Fact]
    public void BuiltInWorkoutsAreValid()
    {
        Assert.Equal(BuiltInWorkouts.All.Count, BuiltInWorkouts.All.Select(w => w.Id).Distinct().Count());
        foreach (var w in BuiltInWorkouts.All)
        {
            Assert.InRange(w.TotalDurationS, 20 * 60, 2 * 3600);
            Assert.False(string.IsNullOrEmpty(w.Name.Get("de")));
            Assert.NotEqual(w.Name.Get("en"), string.Empty);
        }

        Assert.Equal(90 * 60, BuiltInWorkouts.Find("endurance-90")!.TotalDurationS);
    }
}

public class WorkoutEngineTests
{
    private static Workout Simple() => new Workout("simple", "Simple")
    {
        Blocks =
        {
            new SteadyBlock(60, PowerTarget.Percent(50)) { Cues = { new TextCue(0, "Start"), new TextCue(30, "Half") } },
            new RepeatBlock(2, new WorkoutBlock[]
            {
                new SteadyBlock(30, PowerTarget.Percent(120)),
                new FreeRideBlock(30),
            }),
            new RampBlock(100, PowerTarget.Watts(100), PowerTarget.Watts(200)),
        },
    };

    [Fact]
    public void FollowsSegmentsOverTime()
    {
        var e = new WorkoutEngine(Simple(), 200);

        Assert.Equal(100, e.Current.TargetW);
        Assert.Equal(TrainerMode.Erg, e.Current.Mode);

        Assert.Equal(240, e.Update(60).TargetW);           // first 120 % interval
        var free = e.Update(30);
        Assert.Null(free.TargetW);                         // free ride → SIM
        Assert.Equal(TrainerMode.Sim, free.Mode);

        Assert.Equal(240, e.Update(30).TargetW);           // second repeat
        e.Update(60);                                      // → start of the ramp (180 s)
        Assert.Equal(150, e.Update(50).TargetW);           // ramp halfway: 100 → 200 W
        Assert.True(e.Update(100).IsFinished);
        Assert.Equal(e.TotalDurationS, e.ElapsedS);
    }

    [Fact]
    public void CuesFireOnceAtTheirOffset()
    {
        var e = new WorkoutEngine(Simple(), 200);

        Assert.Equal(new[] { "Start" }, e.Update(1).NewCues.Select(c => c.Text.Get("en")));
        Assert.Empty(e.Update(10).NewCues);
        Assert.Equal(new[] { "Half" }, e.Update(20).NewCues.Select(c => c.Text.Get("en")));
        Assert.Empty(e.Update(20).NewCues);
    }

    [Fact]
    public void PauseStopsTime()
    {
        var e = new WorkoutEngine(Simple(), 200);
        e.Update(10);
        e.Pause();
        e.Update(100);
        Assert.Equal(10, e.ElapsedS);
        e.Resume();
        e.Update(5);
        Assert.Equal(15, e.ElapsedS);
    }

    [Fact]
    public void SegmentIntensityOnlyChangesCurrentBlock()
    {
        var e = new WorkoutEngine(Simple(), 200);
        e.Update(60); // first 120 % block
        e.AdjustCurrentSegmentIntensity(-0.05);

        Assert.Equal(228, e.Current.TargetW);              // 240 × 0.95
        e.Update(60);                                      // next 120 % block (repeat 2)
        Assert.Equal(240, e.Current.TargetW);
    }

    [Fact]
    public void GlobalIntensityScalesEverything()
    {
        var e = new WorkoutEngine(Simple(), 200) { GlobalIntensity = 0.9 };
        Assert.Equal(90, e.Current.TargetW);
        e.GlobalIntensity = 3.0;
        Assert.Equal(WorkoutEngine.MaxIntensity, e.GlobalIntensity);
    }

    [Fact]
    public void SkipAndExtend()
    {
        var e = new WorkoutEngine(Simple(), 200);
        e.Update(10);
        e.SkipSegment();
        Assert.Equal(60, e.ElapsedS);
        Assert.Equal(240, e.Current.TargetW);

        e.ExtendCurrentSegment(60);
        Assert.Equal(90, e.SegmentDurationS(1));
        e.Update(80);
        Assert.Equal(240, e.Current.TargetW);              // still in the extended block
        Assert.Equal(e.Workout.TotalDurationS + 60, e.TotalDurationS);
    }

    [Fact]
    public void ExtendedRampIsStretched()
    {
        var e = new WorkoutEngine(Simple(), 200);
        e.SeekTo(180);                                     // start of the ramp
        e.ExtendCurrentSegment(100);                       // 100 s → 200 s
        Assert.Equal(150, e.Update(100).TargetW);          // halfway of the stretched ramp
    }

    [Fact]
    public void SeekToForJoiningGroupWorkout()
    {
        var e = new WorkoutEngine(Simple(), 200);
        e.SeekTo(95);

        Assert.Equal(95, e.ElapsedS);
        Assert.Equal(2, e.CurrentIndex);                   // free ride of repeat 1
        Assert.Equal(5, e.Current.SegmentElapsedS);
    }
}

public class PowerMetricsTests
{
    [Fact]
    public void SteadyRideMetrics()
    {
        var watts = Enumerable.Repeat(200.0, 3600).ToList();

        Assert.Equal(200, PowerMetrics.NormalizedPower(watts), 6);
        Assert.Equal(0.8, PowerMetrics.IntensityFactor(200, 250), 6);
        Assert.Equal(64, PowerMetrics.TrainingStressScore(3600, 200, 250), 6); // 1 h at IF 0.8 = 64 TSS
        Assert.Equal(100, PowerMetrics.TrainingStressScore(3600, 250, 250), 6);
    }

    [Fact]
    public void VariableRideHasHigherNormalizedPower()
    {
        var watts = Enumerable.Range(0, 3600).Select(i => (i / 60) % 2 == 0 ? 300.0 : 100.0).ToList();
        Assert.Equal(200, PowerMetrics.Average(watts), 6);
        Assert.True(PowerMetrics.NormalizedPower(watts) > 230);
    }

    [Fact]
    public void MeanMaxAndPowerCurve()
    {
        var watts = Enumerable.Repeat(150.0, 600).ToList();
        for (int i = 100; i < 160; i++) watts[i] = 400;

        Assert.Equal(400, PowerMetrics.MeanMax(watts, 60));
        Assert.Null(PowerMetrics.MeanMax(watts, 1200));

        var curve = PowerMetrics.PowerCurve(watts);
        Assert.Equal(400, curve[5]);
        Assert.True(curve[300] < curve[60]);
        Assert.False(curve.ContainsKey(1200));
    }

    [Fact]
    public void TimeInZones()
    {
        var zones = PowerZoneModel.Coggan();
        var watts = new List<double> { 100, 100, 200, 250, 260, 400, 1000 }; // FTP 250 → 40, 40, 80, 100, 104, 160, 400 %

        var t = PowerMetrics.TimeInZones(watts, zones, 250);

        Assert.Equal(2, t[1]);   // < 55 %
        Assert.Equal(1, t[3]);   // 80 %
        Assert.Equal(2, t[4]);   // 100 %, 104 %
        Assert.Equal(2, t[7]);   // 160 %, 400 %
        Assert.Equal(0, t[5] + t[6]);
    }

    [Fact]
    public void ZonesWithAbsoluteWatts()
    {
        var zones = PowerZoneModel.Coggan();
        zones.Zones[1].Lower = PowerTarget.Watts(150); // user sets zone 2 to start at 150 W
        Assert.Equal(1, zones.ZoneFor(140, 250).Number);
        Assert.Equal(2, zones.ZoneFor(150, 250).Number);
    }
}

public class FtpTestTests
{
    [Fact]
    public void RampTestUsesBestMinute()
    {
        var test = FtpTests.Create(FtpTestKind.Ramp);
        // warm-up 8 min, then ride the steps until 340 W (13 steps) and stop
        var watts = new List<double>();
        watts.AddRange(Enumerable.Repeat(100.0, 480));
        for (int step = 0; step < 13; step++) watts.AddRange(Enumerable.Repeat(100.0 + (20 * step), 60));
        watts.AddRange(Enumerable.Repeat(0.0, 30));

        Assert.Equal(255, FtpTests.EstimateFtp(FtpTestKind.Ramp, watts, test)); // 340 × 0.75
    }

    [Fact]
    public void TwentyMinuteTest()
    {
        var test = FtpTests.Create(FtpTestKind.TwentyMinute);
        var effort = test.Flatten().Last(s => s.Block is FreeRideBlock f && f.IsMaxEffort); // the 20 minutes (after the 5-min blow-out)
        var watts = Enumerable.Repeat(150.0, test.TotalDurationS).ToList();
        for (int i = effort.StartS; i < effort.EndS; i++) watts[i] = 280;

        Assert.Equal(266, FtpTests.EstimateFtp(FtpTestKind.TwentyMinute, watts, test));
        Assert.Null(FtpTests.EstimateFtp(FtpTestKind.TwentyMinute, watts.Take(effort.StartS + 100).ToList(), test));
    }

    [Fact]
    public void TwoByEightTest()
    {
        var test = FtpTests.Create(FtpTestKind.TwoByEight);
        var efforts = test.Flatten().Where(s => s.Block is FreeRideBlock f && f.IsMaxEffort).ToList();
        var watts = Enumerable.Repeat(150.0, test.TotalDurationS).ToList();
        for (int i = efforts[0].StartS; i < efforts[0].EndS; i++) watts[i] = 300;
        for (int i = efforts[1].StartS; i < efforts[1].EndS; i++) watts[i] = 280;

        Assert.Equal(261, FtpTests.EstimateFtp(FtpTestKind.TwoByEight, watts, test)); // 290 × 0.9
    }
}

public class PowerZoneEditingTests
{
    [Fact]
    public void BoundariesStayOrderedAndRoundTrip()
    {
        var zones = FreeVelo.Core.Training.PowerZoneModel.Coggan();
        Assert.Equal("%;55;75;90;105;120;150", zones.Serialize());
        Assert.Equal(74, zones.SetBoundary(0, 80)); // cannot pass the next boundary (75)
        Assert.Equal(75, zones.SetBoundary(1, 70)); // nor the previous one (74)
        Assert.Equal(75, zones.Zones[1].Upper!.Value.Value);
        Assert.Equal(75, zones.Zones[2].Lower.Value);

        var copy = FreeVelo.Core.Training.PowerZoneModel.Parse(zones.Serialize());
        Assert.Equal(zones.Serialize(), copy.Serialize());
        Assert.Equal(FreeVelo.Core.Training.PowerZoneModel.Coggan().Serialize(), FreeVelo.Core.Training.PowerZoneModel.Parse("garbage").Serialize());
    }

    [Fact]
    public void WattZonesFollowTheirOwnBoundsAndConvert()
    {
        var zones = FreeVelo.Core.Training.PowerZoneModel.Coggan();
        zones.SetUnit(watts: true, ftpW: 200);
        Assert.True(zones.UsesWatts);
        Assert.Equal("W;110;150;180;210;240;300", zones.Serialize());
        zones.SetBoundary(3, 220);
        var parsed = FreeVelo.Core.Training.PowerZoneModel.Parse(zones.Serialize());
        Assert.Equal(4, parsed.ZoneFor(215, 400).Number); // absolute watts: independent of FTP
        Assert.Equal(68, FreeVelo.Core.Training.PowerZoneModel.Coggan().TypicalPercent(1, 250) + 3);
        Assert.Equal(50, FreeVelo.Core.Training.PowerZoneModel.Coggan().TypicalPercent(0, 250));
    }

    [Fact]
    public void TwentyMinuteTestFollowsTheBookAndCountsOnlyThe20Minutes()
    {
        var w = FtpTests.Create(FtpTestKind.TwentyMinute);
        var segments = w.Flatten();
        // Fast pedalling at 100 rpm, a 5-min and a 20-min all-out effort
        Assert.Equal(3, segments.Count(s => s.Block is SteadyBlock b && b.Cadence.HasValue && b.Cadence.Value.Min >= 100));
        var efforts = segments.Where(s => s.Block is FreeRideBlock f && f.IsMaxEffort).ToList();
        Assert.Equal(new[] { 300, 1200 }, efforts.Select(e => e.DurationS));

        // 5 min at 330 W, 20 min at 280 W → FTP = 95 % of 280 = 266 (the 5 minutes don't count)
        var watts = new double[w.TotalDurationS];
        for (int i = 0; i < watts.Length; i++) watts[i] = 150;
        for (int i = efforts[0].StartS; i < efforts[0].EndS; i++) watts[i] = 330;
        for (int i = efforts[1].StartS; i < efforts[1].EndS; i++) watts[i] = 280;
        Assert.Equal(266, FtpTests.EstimateFtp(FtpTestKind.TwentyMinute, watts, w));
    }
}
