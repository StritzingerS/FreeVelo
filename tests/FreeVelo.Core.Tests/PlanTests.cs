using FreeVelo.Core.History;
using FreeVelo.Core.Plans;
using FreeVelo.Core.Training;
using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Tests;

public class PlanTests
{
    // Wednesday 4 Nov 2026; the plan's first week starts Monday 2 Nov.
    private static readonly DateTime Today = new(2026, 11, 4);
    private static readonly DayOfWeek[] TueThuSat = { DayOfWeek.Saturday, DayOfWeek.Tuesday, DayOfWeek.Thursday };

    private static TrainingPlan Build(string template = "ftp-build", int weeks = 8, DayOfWeek[]? days = null) =>
        PlanTemplates.Create(PlanTemplates.Find(template)!, weeks, days ?? TueThuSat, Today);

    private static RideRecord Ride(DateTime day, string workout, double completed = 1, double intensity = 1) => new()
    {
        id = $"{day:yyyy-MM-dd}_{workout}.fit",
        startUtc = new DateTimeOffset(DateTime.SpecifyKind(day.AddHours(18), DateTimeKind.Utc)).ToUnixTimeSeconds(),
        workoutId = workout,
        workoutCompleted = completed,
        workoutIntensity = intensity,
        tss = 80,
    };

    private static DateTime Local(DateTime t) => t;

    [Fact]
    public void BuildPlanHasWeeksRecoveryWeekAndTestAtTheEnd()
    {
        var plan = Build();
        Assert.Equal("2026-11-02", plan.start);
        Assert.Equal(new DateTime(2026, 12, 27), plan.End);

        // Tuesday of week 1 is already over: the plan starts with Thursday.
        Assert.Equal(new DateTime(2026, 11, 5), plan.sessions[0].Date);
        Assert.Equal(8 * 3 - 1, plan.sessions.Count);

        // Normal week: key – endurance (long, last day) – key ... pattern for 3 days is Key, Endurance, Key
        var week2 = plan.sessions.Where(s => s.week == 1).ToList();
        Assert.Equal(new[] { SessionKind.Key, SessionKind.Endurance, SessionKind.Key }, week2.Select(s => s.Kind));
        Assert.All(week2.Where(s => s.Kind == SessionKind.Key), s => Assert.Contains(s.workoutId, PlanTemplates.Find("ftp-build")!.KeyWorkouts));

        // Week 4 is a recovery week: no key sessions
        Assert.DoesNotContain(plan.sessions.Where(s => s.week == 3), s => s.Kind == SessionKind.Key);

        // Last week: recovery before the ramp test on the last day (rule 6)
        var last = plan.sessions.Where(s => s.week == 7).ToList();
        Assert.Equal((SessionKind.Test, "ftp-ramp"), (last[^1].Kind, last[^1].workoutId));
        Assert.Equal(SessionKind.Recovery, last[^2].Kind);

        // All workouts exist
        Assert.All(plan.sessions, s => Assert.True(BuiltInWorkouts.Find(s.workoutId) != null || s.workoutId == "ftp-ramp", s.workoutId));
    }

    [Fact]
    public void MissedKeySessionMovesWithinTheWeekOthersAreDropped()
    {
        var plan = Build(days: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday });
        // Week 2: Mon key, Tue endurance, Wed key, Thu endurance
        var mon = plan.On(new DateTime(2026, 11, 9)).Single();
        var tue = plan.On(new DateTime(2026, 11, 10)).Single();
        Assert.Equal((SessionKind.Key, SessionKind.Endurance), (mon.Kind, tue.Kind));

        // Nothing ridden on Monday and Tuesday; Wednesday morning:
        Assert.True(PlanAdapter.Update(plan, new DateTime(2026, 11, 11), 0));
        Assert.Equal(SessionStatus.Missed, tue.Status);
        Assert.Equal(SessionStatus.Planned, mon.Status);
        Assert.Equal(new DateTime(2026, 11, 13), mon.Date); // Friday: first free day (Wed, Thu are taken)
        Assert.Equal(PlanAdapter.NoteMoved, mon.note);

        // Missed again (Wednesday's key session too): Wednesday's takes Saturday, Monday's Sunday
        var wed = plan.On(new DateTime(2026, 11, 11)).Single();
        PlanAdapter.Update(plan, new DateTime(2026, 11, 14), 0);
        Assert.Equal(new DateTime(2026, 11, 14), wed.Date);
        Assert.Equal(new DateTime(2026, 11, 15), mon.Date);
        PlanAdapter.Update(plan, new DateTime(2026, 11, 16), 0);
        Assert.Equal(SessionStatus.Missed, mon.Status); // the week is over
    }

    [Fact]
    public void AbortedWorkoutMakesTheNextOfItsKindEasier()
    {
        var plan = Build();
        var first = plan.sessions.First(s => s.Kind == SessionKind.Key); // Saturday
        var done = PlanAdapter.RecordRide(plan, Ride(first.Date, first.workoutId, completed: 0.6), Local);
        Assert.Same(first, done);
        Assert.Equal(SessionStatus.Done, first.Status);

        var nextKey = plan.sessions.First(s => s.Kind == SessionKind.Key && s.Date > first.Date);
        Assert.Equal(-3, nextKey.adjustPercent);
        Assert.Equal(0.97, nextKey.Intensity, 3);
    }

    [Fact]
    public void TwoStrongKeySessionsMakeNextWeekHarder()
    {
        var plan = Build();
        var keys = plan.sessions.Where(s => s.week == 1 && s.Kind == SessionKind.Key).ToList();
        foreach (var k in keys) PlanAdapter.RecordRide(plan, Ride(k.Date, k.workoutId, intensity: 1.04), Local);

        Assert.All(plan.sessions.Where(s => s.week == 2 && s.Kind == SessionKind.Key), s => Assert.Equal(2, s.adjustPercent));
        Assert.Contains(1, plan.boostedWeeks);
    }

    [Fact]
    public void FreeAndGroupRidesDoNotCompleteSessions()
    {
        var plan = Build();
        var first = plan.sessions[0];
        Assert.Null(PlanAdapter.RecordRide(plan, Ride(first.Date, ""), Local));
        Assert.Null(PlanAdapter.RecordRide(plan, Ride(first.Date, "endurance-90"), Local)); // other workout
        Assert.Equal(SessionStatus.Planned, first.Status);
    }

    [Fact]
    public void VeryLowFormInsertsARecoveryDay()
    {
        var plan = Build();
        var next = plan.sessions.First(s => s.Kind == SessionKind.Key);
        PlanAdapter.RecordRide(plan, Ride(plan.sessions[0].Date, plan.sessions[0].workoutId), Local); // Thursday done
        Assert.True(PlanAdapter.Update(plan, next.Date.AddDays(-1), -35));
        Assert.Equal((SessionKind.Recovery, "recovery-40"), (next.Kind, next.workoutId));
        Assert.False(PlanAdapter.Update(plan, next.Date.AddDays(-1), -35)); // once
    }

    [Fact]
    public void OtherTrainingDaysRegenerateFromToday()
    {
        var plan = Build();
        var first = plan.sessions[0];
        PlanAdapter.RecordRide(plan, Ride(first.Date, first.workoutId), Local);

        PlanAdapter.ChangeWeekdays(plan, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Sunday }, new DateTime(2026, 11, 6));
        Assert.Equal(SessionStatus.Done, plan.sessions[0].Status); // kept
        var future = plan.sessions.Where(s => s.Date >= new DateTime(2026, 11, 6)).ToList();
        Assert.All(future, s => Assert.Contains(s.Date.DayOfWeek, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Sunday }));
        Assert.Equal(4, future.Count(s => s.week == 1));
        Assert.Equal(new DateTime(2026, 11, 6), future[0].Date); // today is a training day now
    }

    [Fact]
    public void PlanSurvivesJsonRoundTrip()
    {
        var plan = Build("vo2max", 6, new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Sunday });
        var json = System.Text.Json.JsonSerializer.Serialize(plan, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        var back = System.Text.Json.JsonSerializer.Deserialize<TrainingPlan>(json, new System.Text.Json.JsonSerializerOptions { IncludeFields = true })!;
        Assert.Equal(plan.sessions.Count, back.sessions.Count);
        Assert.Equal(plan.sessions[3].workoutId, back.sessions[3].workoutId);
        Assert.Equal(5, back.weekdays.Length);
    }

    [Fact]
    public void RecoveryWeeksStayBelow62PercentAndNewWorkoutsAreUsed()
    {
        var days = new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday };
        var plan = PlanTemplates.Create(PlanTemplates.Find("ftp-build")!, 8, days, new DateTime(2026, 11, 9));
        foreach (var s in plan.sessions.Where(s => s.week == 3))
        {
            var w = BuiltInWorkouts.Find(s.workoutId)!;
            double maxPercent = w.Flatten().Max(seg => seg.Block is SteadyBlock b ? b.Target.Value : 0) * s.Intensity;
            Assert.True(maxPercent <= 62, $"{s.workoutId}: {maxPercent:0} %");
        }

        // All key workouts of the templates exist and stay under 2 hours (long indoor sessions are skipped).
        foreach (var t in PlanTemplates.All)
        {
            foreach (string id in t.KeyWorkouts)
            {
                var w = BuiltInWorkouts.Find(id);
                Assert.NotNull(w);
                Assert.True(w!.TotalDurationS <= 2 * 3600, id);
            }
        }

        Assert.Contains(plan.sessions, s => s.workoutId == "sweetspot-crisscross");
    }

    [Fact]
    public void LastWeekIsATaperAndNotPrecededByARecoveryWeek()
    {
        var days = new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday };
        // Base, 5 weeks: week 4 (index 3) would be a recovery week, but it is right before the taper → stays hard.
        var plan = PlanTemplates.Create(PlanTemplates.Find("base")!, 5, days, new DateTime(2026, 11, 9));
        Assert.Contains(plan.sessions, s => s.week == 3 && s.Kind == SessionKind.Key);
        var taper = plan.sessions.Where(s => s.week == 4).ToList();
        Assert.DoesNotContain(taper, s => s.Kind == SessionKind.Key);
        Assert.Contains(taper, s => s.workoutId == PlanTemplates.Openers);

        // A plan ending with an FTP test: openers, recovery the day before, then the test.
        var build = PlanTemplates.Create(PlanTemplates.Find("ftp-build")!, 8, days, new DateTime(2026, 11, 9));
        var last = build.sessions.Where(s => s.week == 7).OrderBy(s => s.date).ToList();
        Assert.Equal(new[] { PlanTemplates.Openers, PlanTemplates.Recovery, PlanTemplates.RampTest }, last.Select(s => s.workoutId));
        Assert.Contains(build.sessions, s => s.week == 3 && s.Kind != SessionKind.Key); // normal recovery week 4 stays
    }

    [Fact]
    public void FitnessRisingTooFastBringsTheRecoveryWeekForward()
    {
        var days = new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday };
        var today = new DateTime(2026, 11, 9);
        var plan = PlanTemplates.Create(PlanTemplates.Find("ftp-build")!, 8, days, today);

        // Two fast weeks: no change yet.
        Assert.False(PlanAdapter.Update(plan, today, 0, new[] { 8.0, 9.0, 4.0 }));
        // Three fast weeks in a row: the next 7 days become easy.
        Assert.True(PlanAdapter.Update(plan, today, 0, new[] { 8.0, 9.0, 7.5 }));
        var week = plan.sessions.Where(s => s.Date >= today && s.Date < today.AddDays(7)).ToList();
        Assert.DoesNotContain(week, s => s.Kind == SessionKind.Key);
        Assert.All(week, s => Assert.Equal(PlanAdapter.NoteRamp, s.note));
        // Only once.
        Assert.False(PlanAdapter.Update(plan, today.AddDays(1), 0, new[] { 8.0, 9.0, 7.5 }));
    }

    [Fact]
    public void WeeklyRampsFollowFitness()
    {
        // Every day 100 TSS from zero: fitness rises fast at first.
        var start = new DateTime(2026, 9, 1);
        var rides = Enumerable.Range(0, 30).Select(i => new RideRecord
        {
            startUtc = new DateTimeOffset(DateTime.SpecifyKind(start.AddDays(i).AddHours(18), DateTimeKind.Utc)).ToUnixTimeSeconds(),
            tss = 100,
        }).ToList();
        var ramps = TrainingLoad.WeeklyRamps(rides, start.AddDays(29), 3, t => t);
        Assert.Equal(3, ramps.Count);
        Assert.All(ramps, r => Assert.InRange(r, 7, 20));
        Assert.True(ramps[0] < ramps[2]); // the rise slows down (newest first)
    }

    [Fact]
    public void ThePlanEndsWithTheChosenFtpTest()
    {
        var days = new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday };
        var plan = PlanTemplates.Create(PlanTemplates.Find("ftp-build")!, 8, days, new DateTime(2026, 11, 9), "ftp-20min");
        Assert.Equal("ftp-20min", plan.sessions.Single(s => s.Kind == SessionKind.Test).workoutId);

        // Other training days later: the plan is regenerated and keeps its test.
        PlanAdapter.ChangeWeekdays(plan, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Sunday }, new DateTime(2026, 11, 10));
        Assert.Equal("ftp-20min", plan.sessions.Single(s => s.Kind == SessionKind.Test).workoutId);
    }
}
