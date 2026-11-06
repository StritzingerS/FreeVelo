using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Tests;

public class WorkoutFileTests
{
    private const string ZwiftFile = """
        <workout_file>
          <author>Coach</author>
          <name>Sweet Spot Mix</name>
          <description>Warm up, intervals, cool down</description>
          <sportType>bike</sportType>
          <workout>
            <Warmup Duration="600" PowerLow="0.45" PowerHigh="0.75"/>
            <SteadyState Duration="300" Power="0.9" Cadence="90">
              <textevent timeoffset="0" message="Sweet spot!"/>
              <textevent timeoffset="150" message="Halfway"/>
            </SteadyState>
            <IntervalsT Repeat="4" OnDuration="60" OffDuration="120" OnPower="1.2" OffPower="0.5" Cadence="100" CadenceResting="85"/>
            <FreeRide Duration="120" FlatRoad="1"/>
            <MaxEffort Duration="15"/>
            <Cooldown Duration="300" PowerLow="0.6" PowerHigh="0.4"/>
          </workout>
        </workout_file>
        """;

    [Fact]
    public void ReadsZwiftWorkout()
    {
        var w = WorkoutFiles.Load("ss-mix.zwo", ZwiftFile);

        Assert.Equal("ss-mix", w.Id);
        Assert.Equal("Sweet Spot Mix", w.Name.Get("en"));
        Assert.Equal("Coach", w.Author);
        Assert.Equal(600 + 300 + (4 * 180) + 120 + 15 + 300, w.TotalDurationS);

        var warmup = Assert.IsType<RampBlock>(w.Blocks[0]);
        Assert.Equal(PowerTarget.Percent(45), warmup.From);
        Assert.Equal(PowerTarget.Percent(75), warmup.To);

        var steady = Assert.IsType<SteadyBlock>(w.Blocks[1]);
        Assert.Equal(PowerTarget.Percent(90), steady.Target);
        Assert.Equal(new[] { 0, 150 }, steady.Cues.Select(c => c.OffsetS));
        Assert.Equal(85, steady.Cadence!.Value.Min);

        var intervals = Assert.IsType<RepeatBlock>(w.Blocks[2]);
        Assert.Equal(4, intervals.Count);
        Assert.Equal(PowerTarget.Percent(120), ((SteadyBlock)intervals.Blocks[0]).Target);
        Assert.Equal(60, ((SteadyBlock)intervals.Blocks[0]).DurationS);
        Assert.Equal(120, ((SteadyBlock)intervals.Blocks[1]).DurationS);

        Assert.False(Assert.IsType<FreeRideBlock>(w.Blocks[3]).IsMaxEffort);
        Assert.True(Assert.IsType<FreeRideBlock>(w.Blocks[4]).IsMaxEffort);
        Assert.Equal(PowerTarget.Percent(40), Assert.IsType<RampBlock>(w.Blocks[5]).To);
    }

    [Fact]
    public void ReadsZwoWithIndentedXmlDeclaration()
    {
        // As exported by zwo.fyi: newline and indentation before the XML declaration.
        const string xml = "\n    <?xml version=\"1.0\" ?>\n    <workout_file>\n      <name>Vo2 Max Blaster</name>\n      <sportType>bike</sportType>\n      <workout>\n        <Warmup Duration=\"600\" PowerLow=\"0.4\" PowerHigh=\"0.65\" Cadence=\"90\"/>\n<IntervalsT Cadence=\"90\" Repeat=\"5\" OnDuration=\"180\" OffDuration=\"120\" OnPower=\"1.2\" OffPower=\"0.6\"/>\n<Cooldown Duration=\"300\" PowerLow=\"0.75\" PowerHigh=\"0.55\" Cadence=\"90\"/>\n      </workout>\n    </workout_file>\n  ";
        var workout = WorkoutFiles.Load("vo2-max-blaster.zwo", xml);
        Assert.Equal("Vo2 Max Blaster", workout.Name.Get("en"));
        Assert.Equal(600 + (5 * 300) + 300, workout.TotalDurationS);
    }

    [Fact]
    public void FreeVeloFormatRoundTripsAllFeatures()
    {
        var original = BuiltInWorkouts.FortyTwenties();          // nested repeats, cues de/en, cadence
        original.Blocks.Add(new SteadyBlock(300, PowerTarget.Watts(180)));

        var copy = WorkoutFiles.FromZwo(WorkoutFiles.ToZwo(original), original.Id);

        Assert.Equal(original.Name.Get("de"), copy.Name.Get("de"));
        Assert.Equal(original.Category, copy.Category);
        Assert.Equal(original.TotalDurationS, copy.TotalDurationS);
        var a = original.Flatten();
        var b = copy.Flatten();
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].BaseTargetW(10, 250), b[i].BaseTargetW(10, 250));
            Assert.Equal(a[i].Block.Cues.Count, b[i].Block.Cues.Count);
        }

        Assert.Equal(PowerTarget.Watts(180), ((SteadyBlock)copy.Blocks.Last()).Target);
        Assert.Equal("40 an", copy.Flatten().First(s => s.Block.Cues.Count > 0 && s.Block is SteadyBlock st && st.Target.Value == 120).Block.Cues[0].Text.Get("de"));
    }

    [Fact]
    public void StrictZwoUsesIntervalsTAndPercent()
    {
        var w = new Workout("t", "Test");
        w.Blocks.Add(new RepeatBlock(5, new WorkoutBlock[] { new SteadyBlock(60, PowerTarget.Percent(110)), new SteadyBlock(60, PowerTarget.Watts(125)) }));
        w.Blocks.Add(new RepeatBlock(2, new WorkoutBlock[] { new SteadyBlock(30, PowerTarget.Percent(100)), new FreeRideBlock(10), new SteadyBlock(30, PowerTarget.Percent(50)) }));

        string xml = WorkoutFiles.ToZwo(w, strict: true, ftpW: 250);

        Assert.Contains("<IntervalsT Repeat=\"5\" OnDuration=\"60\" OffDuration=\"60\" OnPower=\"1.1\" OffPower=\"0.5\"", xml);
        Assert.DoesNotContain("Repeat Count", xml);
        Assert.DoesNotContain("PowerW", xml);
        var back = WorkoutFiles.FromZwo(xml, "t");
        Assert.Equal(w.TotalDurationS, back.TotalDurationS);
        Assert.Equal(1 + 6, back.Blocks.Count); // IntervalsT + expanded 2 × 3 blocks
    }

    [Fact]
    public void ReadsErgWithStepsRampsAndText()
    {
        const string erg = """
            [COURSE HEADER]
            VERSION = 2
            UNITS = ENGLISH
            DESCRIPTION = Over-unders
            FILE NAME = overunders.erg
            MINUTES WATTS
            [END COURSE HEADER]
            [COURSE DATA]
            0.00	100
            5.00	200
            5.00	250
            7.00	250
            9.00	250
            9.00	150
            12.00	150
            [END COURSE DATA]
            [COURSE TEXT]
            300	Go!	10
            600	Recover	10
            [END COURSE TEXT]
            """;

        var w = WorkoutFiles.Load("overunders.erg", erg);

        Assert.Equal("overunders", w.Name.Get("en"));
        Assert.Equal(3, w.Blocks.Count);
        var ramp = Assert.IsType<RampBlock>(w.Blocks[0]);
        Assert.Equal(PowerTarget.Watts(100), ramp.From);
        Assert.Equal(PowerTarget.Watts(200), ramp.To);
        var hard = Assert.IsType<SteadyBlock>(w.Blocks[1]);
        Assert.Equal(240, hard.DurationS);                        // 5–7 and 7–9 merged
        Assert.Equal(PowerTarget.Watts(250), hard.Target);
        Assert.Equal(0, hard.Cues.Single().OffsetS);              // "Go!" at 300 s = block start
        var easy = Assert.IsType<SteadyBlock>(w.Blocks[2]);
        Assert.Equal(60, easy.Cues.Single().OffsetS);             // "Recover" at 600 s, block starts at 540 s
        Assert.Equal(720, w.TotalDurationS);
    }

    [Fact]
    public void ReadsMrcAsPercent()
    {
        const string mrc = "[COURSE HEADER]\nMINUTES PERCENT\n[END COURSE HEADER]\n[COURSE DATA]\n0\t50\n10\t50\n10\t95\n30\t95\n[END COURSE DATA]\n";
        var w = WorkoutFiles.Load("tempo.MRC", mrc);

        Assert.Equal(2, w.Blocks.Count);
        Assert.Equal(PowerTarget.Percent(95), ((SteadyBlock)w.Blocks[1]).Target);
        Assert.Equal(1200, w.Blocks[1].TotalDurationS);
    }

    [Theory]
    [InlineData("x.zwo", "<workout_file><workout><Bogus Duration=\"10\"/></workout></workout_file>")]
    [InlineData("x.zwo", "<workout_file><workout/></workout_file>")]
    [InlineData("x.zwo", "not xml")]
    [InlineData("x.erg", "[COURSE DATA]\n0 100\n[END COURSE DATA]")]
    [InlineData("x.fit", "")]
    public void RejectsInvalidFiles(string name, string content)
    {
        Assert.Throws<FormatException>(() => WorkoutFiles.Load(name, content));
    }

    [Fact]
    public void AllBuiltInWorkoutsRoundTrip()
    {
        foreach (var w in BuiltInWorkouts.All)
        {
            var copy = WorkoutFiles.FromZwo(WorkoutFiles.ToZwo(w), w.Id);
            Assert.Equal(w.TotalDurationS, copy.TotalDurationS);
            Assert.Equal(w.Flatten().Count, copy.Flatten().Count);
        }
    }
}
