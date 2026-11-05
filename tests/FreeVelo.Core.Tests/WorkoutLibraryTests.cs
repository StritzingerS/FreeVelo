using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Tests;

public sealed class WorkoutLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "freevelo-lib-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void SaveReloadDelete()
    {
        var lib = new WorkoutLibrary(_dir);
        var w = new Workout("", "My Threshold!") { Blocks = { new SteadyBlock(600, PowerTarget.Percent(95)) } };
        lib.Save(w);

        Assert.Equal("my-threshold", w.Id);
        var other = new WorkoutLibrary(_dir);
        other.Reload();
        Assert.Single(other.Workouts);
        Assert.Equal(600, other.Workouts[0].TotalDurationS);

        Assert.True(other.Delete("my-threshold"));
        Assert.Empty(other.Workouts);
        Assert.False(File.Exists(Path.Combine(_dir, "my-threshold.zwo")));
    }

    [Fact]
    public void ImportAssignsUniqueIdsAndAvoidsBuiltIns()
    {
        var lib = new WorkoutLibrary(_dir);
        const string mrc = "[COURSE DATA]\n0 50\n10 50\n[END COURSE DATA]";
        var a = lib.Import("Easy Ride.mrc", mrc);
        var b = lib.Import("Easy Ride.mrc", mrc);
        var c = lib.Import("vo2max-4x4.mrc", mrc);

        Assert.Equal("easy-ride", a.Id);
        Assert.Equal("easy-ride-2", b.Id);
        Assert.Equal("vo2max-4x4-2", c.Id); // built-in id is taken
        Assert.Equal(3, Directory.GetFiles(_dir, "*.zwo").Length);
    }

    [Fact]
    public void BrokenFilesAreReportedNotFatal()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "broken.zwo"), "<workout_file><workout/></workout_file>");
        File.WriteAllText(Path.Combine(_dir, "ok.zwo"), WorkoutFiles.ToZwo(BuiltInWorkouts.Recovery()));

        var lib = new WorkoutLibrary(_dir);
        lib.Reload();

        Assert.Single(lib.Workouts);
        Assert.Contains("broken.zwo", lib.Errors.Single());
    }

    [Fact]
    public void InvalidImportThrows()
    {
        var lib = new WorkoutLibrary(_dir);
        Assert.Throws<FormatException>(() => lib.Import("x.erg", "garbage"));
        Assert.Empty(lib.Workouts);
    }
}
