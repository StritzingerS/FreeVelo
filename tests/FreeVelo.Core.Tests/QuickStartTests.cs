using FreeVelo.Core.History;
using FreeVelo.Core.Training;
using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Tests;

public class QuickStartTests
{
    private static readonly DateTime Today = new(2026, 11, 20);
    private static readonly IReadOnlyList<Workout> Templates = BuiltInWorkouts.All;

    private static RideRecord Ride(DateTime day, double tss, string workout = "", double intensity = 0.7) => new()
    {
        startUtc = new DateTimeOffset(DateTime.SpecifyKind(day.AddHours(18), DateTimeKind.Utc)).ToUnixTimeSeconds(),
        durationS = 3600,
        tss = tss,
        intensityFactor = intensity,
        workoutId = workout,
    };

    private static WorkoutSuggestion Suggest(params RideRecord[] rides) => QuickStart.Suggest(rides, Today, Templates, t => t);

    /// <summary>Steady training for six weeks (form ≈ 0), then the last days as given.</summary>
    private static List<RideRecord> Block(double dailyTss, int days = 42)
    {
        var rides = new List<RideRecord>();
        for (int d = days; d >= 2; d--) rides.Add(Ride(Today.AddDays(-d), dailyTss));
        return rides;
    }

    [Fact]
    public void NoRidesLatelyMeansAnEasyStart()
    {
        var s = Suggest(Ride(Today.AddDays(-60), 200));
        Assert.Equal(SuggestionReason.GettingStarted, s.Reason);
        Assert.Equal(WorkoutCategory.Endurance, s.Workout.Category);
    }

    [Fact]
    public void TiredRidersGetRecovery()
    {
        // A big week after a quiet month: fatigue far above fitness.
        var rides = Block(30);
        for (int d = 8; d >= 1; d--) rides.Add(Ride(Today.AddDays(-d), 150));
        var s = Suggest(rides.ToArray());
        Assert.True(s.Form < QuickStart.TiredBelow, $"form {s.Form:0}");
        Assert.Equal(SuggestionReason.Tired, s.Reason);
        Assert.Equal(WorkoutCategory.Recovery, s.Workout.Category);
    }

    [Fact]
    public void FreshRidersGetIntensityThatVaries()
    {
        // Regular training, then five days off: fresh.
        var rides = Block(60, 60).Where(r => r.StartUtc < Today.AddDays(-5)).ToList();
        var first = Suggest(rides.ToArray());
        Assert.Equal(SuggestionReason.Fresh, first.Reason);
        Assert.Contains(first.Workout.Category, new[] { WorkoutCategory.Threshold, WorkoutCategory.Vo2Max });

        // Ridden a while ago (still fresh): the next fresh suggestion is a different workout.
        rides.Add(Ride(Today.AddDays(-6).AddHours(-1), 0, first.Workout.Id));
        var second = Suggest(rides.ToArray());
        Assert.Equal(SuggestionReason.Fresh, second.Reason);
        Assert.NotEqual(first.Workout.Id, second.Workout.Id);
    }

    [Fact]
    public void NoIntensityTheDayAfterAHardRide()
    {
        var rides = Block(60, 60).Where(r => r.StartUtc < Today.AddDays(-5)).ToList();
        rides.Add(Ride(Today.AddDays(-1), 40, "vo2max-4x4", intensity: 0.92));
        var s = Suggest(rides.ToArray());
        Assert.Equal(SuggestionReason.Recovering, s.Reason);
        Assert.Equal(WorkoutCategory.Endurance, s.Workout.Category);
    }

    [Fact]
    public void BalancedFormGetsSweetSpotOrTempo()
    {
        var rides = Block(60, 200); // long steady training up to yesterday: fitness ≈ fatigue
        rides.Add(Ride(Today.AddDays(-1), 60));
        var s = Suggest(rides.ToArray());
        Assert.Equal(SuggestionReason.Balanced, s.Reason);
        Assert.Contains(s.Workout.Category, new[] { WorkoutCategory.SweetSpot, WorkoutCategory.Tempo });
    }

    [Fact]
    public void RecentWorkoutsAreNewestFirstWithoutDuplicatesTestsOrTheSuggestion()
    {
        var rides = new[]
        {
            Ride(Today.AddDays(-1), 50, "tempo-2x20"),
            Ride(Today.AddDays(-2), 50, "ramp"),
            Ride(Today.AddDays(-3), 50, "tempo-2x20"),
            Ride(Today.AddDays(-4), 50, ""),
            Ride(Today.AddDays(-5), 50, "deleted-own-workout"),
            Ride(Today.AddDays(-6), 50, "sweetspot-3x15"),
            Ride(Today.AddDays(-7), 50, "40-20s"),
            Ride(Today.AddDays(-8), 50, "sprints-8x15"),
        };
        Workout? Resolve(string id) => id == "ramp" ? FtpTests.Create(FtpTestKind.Ramp) : BuiltInWorkouts.Find(id);

        var recent = QuickStart.Recent(rides, Resolve, 2, new[] { "sweetspot-3x15" });
        Assert.Equal(new[] { "tempo-2x20", "40-20s" }, recent.Select(r => r.Workout.Id));
    }
}
