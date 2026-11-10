using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Tests;

public class WorkoutEditorTests
{
    [Fact]
    public void EditsACopyAndSupportsUndoRedo()
    {
        var original = BuiltInWorkouts.All.First(w => w.Blocks.OfType<RepeatBlock>().Any());
        string before = WorkoutFiles.ToZwo(original);
        var editor = new WorkoutEditor(original, 250);

        editor.Selected = 0;
        editor.AddSteady(95, 600);
        Assert.Equal(1, editor.Selected);
        Assert.True(editor.IsDirty);
        Assert.Equal(before, WorkoutFiles.ToZwo(original)); // the built-in workout is untouched

        editor.SetPercent(1, 100);
        Assert.Equal(PowerTarget.Percent(100), ((SteadyBlock)editor.Blocks[1]).Target);
        editor.SetDuration(1, 5);
        Assert.Equal(WorkoutEditor.MinDurationS, ((SteadyBlock)editor.Blocks[1]).DurationS);

        editor.Undo();
        editor.Undo();
        Assert.Equal(PowerTarget.Percent(95), ((SteadyBlock)editor.Blocks[1]).Target);
        editor.Redo();
        Assert.Equal(PowerTarget.Percent(100), ((SteadyBlock)editor.Blocks[1]).Target);
    }

    [Fact]
    public void DragAndDropReordersBlocks()
    {
        var editor = WorkoutEditor.CreateNew("Test", 250);
        var a = editor.AddSteady(60, 300);
        var b = editor.AddSteady(90, 300);
        var c = editor.AddRamp(60, 40, 300);

        editor.Move(3, 1); // drag the ramp to second position
        Assert.Same(c, editor.Blocks[1]);
        Assert.Same(a, editor.Blocks[2]);
        Assert.Same(b, editor.Blocks[3]);
        Assert.Equal(1, editor.Selected);
    }

    [Fact]
    public void IntervalsCanBeOpenedAndEdited()
    {
        var editor = WorkoutEditor.CreateNew("Intervals", 300);
        var repeat = editor.AddIntervals(5, 180, 120, 120, 50);
        int index = editor.Selected;
        editor.SetRepeatCount(index, 6);
        Assert.Equal(6, repeat.Count);

        editor.OpenSelectedRepeat();
        Assert.Same(repeat, editor.OpenRepeat);
        editor.Selected = 0;
        editor.SetPercent(0, 125);
        editor.Delete();
        editor.Delete(); // keeps at least one block
        Assert.Single(repeat.Blocks);
        editor.CloseRepeat();
        Assert.Equal(index, editor.Selected);
        Assert.Equal(600 + (6 * 120), editor.Workout.TotalDurationS);
    }

    [Fact]
    public void UnitSwitchKeepsPowerAndEditsInTheBlocksUnit()
    {
        var editor = WorkoutEditor.CreateNew("Watts", 200);
        editor.AddSteady(90, 300);
        editor.SetUnit(watts: true);
        Assert.Equal(PowerTarget.Watts(180), ((SteadyBlock)editor.Blocks[1]).Target);
        editor.SetPercent(1, 100);
        Assert.Equal(PowerTarget.Watts(200), ((SteadyBlock)editor.Blocks[1]).Target);
    }

    [Fact]
    public void EstimatesTss()
    {
        var editor = new WorkoutEditor(new Workout("one-hour", "FTP hour") { Blocks = { new SteadyBlock(3600, PowerTarget.Percent(100)) } }, 250);
        Assert.InRange(editor.EstimatedTss(), 99, 101);
    }

    [Fact]
    public void EditedBuiltInWorkoutsAreStoredAndCanBeReset()
    {
        string folder = Path.Combine(Path.GetTempPath(), "fv-editor-" + Guid.NewGuid());
        try
        {
            var library = new WorkoutLibrary(folder);
            library.Reload();
            var builtIn = BuiltInWorkouts.All[0];
            var editor = new WorkoutEditor(library.Templates[0], 250);
            editor.Selected = 0;
            editor.AddSteady(80, 120);
            library.Save(editor.Workout);

            var reloaded = new WorkoutLibrary(folder);
            reloaded.Reload();
            Assert.Empty(reloaded.Workouts); // not listed under "my workouts"
            Assert.True(reloaded.IsModified(builtIn.Id));
            Assert.Equal(builtIn.TotalDurationS + 120, reloaded.Templates[0].TotalDurationS);

            reloaded.ResetToDefault(builtIn.Id);
            Assert.False(reloaded.IsModified(builtIn.Id));
            Assert.Equal(builtIn.TotalDurationS, reloaded.Templates[0].TotalDurationS);
            reloaded.Reload();
            Assert.False(reloaded.IsModified(builtIn.Id));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void DragGestureIsOneUndoStep()
    {
        var editor = WorkoutEditor.CreateNew("Gesture", 250);
        editor.AddSteady(90, 300);
        editor.BeginGesture();
        for (int p = 91; p <= 105; p++) editor.SetPercent(1, p);
        editor.EndGesture();
        editor.Undo();
        Assert.Equal(PowerTarget.Percent(90), ((SteadyBlock)editor.Blocks[1]).Target);
    }

    [Fact]
    public void TargetsAheadFollowTheWorkoutWithoutAdvancingIt()
    {
        var w = new Workout("t", "t");
        w.Blocks.Add(new SteadyBlock(60, PowerTarget.Percent(50)));
        w.Blocks.Add(new RepeatBlock(2, new WorkoutBlock[] { new SteadyBlock(30, PowerTarget.Percent(120)), new SteadyBlock(30, PowerTarget.Percent(60)) }));
        w.Blocks.Add(new RampBlock(20, PowerTarget.Percent(60), PowerTarget.Percent(40)));
        var engine = new WorkoutEngine(w, 200);
        engine.Update(45);
        engine.AdjustCurrentSegmentIntensity(0.1);

        var ahead = engine.TargetsAhead(3600);
        Assert.Equal(45, engine.ElapsedS); // unchanged
        Assert.Equal((0.0, (int?)110), ahead[0]); // 50 % × 1.1 of 200 W
        Assert.Equal((15.0, (int?)240), ahead[1]);
        Assert.Equal((45.0, (int?)120), ahead[2]);
        Assert.Equal((75.0, (int?)240), ahead[3]);
        Assert.Equal((105.0, (int?)120), ahead[4]); // ramp at 135 s starts at 60 % = the same → no extra entry
        Assert.Equal((140.0, (int?)110), ahead[5]); // ramp sampled every 5 s
        Assert.True(ahead[ahead.Count - 1].TargetW < 120);
        Assert.Equal(2, engine.TargetsAhead(20).Count); // horizon
    }

    [Fact]
    public void IntervalsInsideIntervalsCanBeEdited()
    {
        // 40/20s: sets (outer repeat) of 10 × 40/20 (inner repeat).
        var ed = new WorkoutEditor(BuiltInWorkouts.FortyTwenties(), 250);
        int outer = ed.Blocks.FindIndex(b => b is RepeatBlock);
        ed.Selected = outer;
        ed.OpenSelectedRepeat();
        Assert.Equal(1, ed.OpenDepth);

        int inner = ed.Blocks.FindIndex(b => b is RepeatBlock);
        ed.Selected = inner;
        ed.OpenSelectedRepeat();
        Assert.Equal(2, ed.OpenDepth);
        Assert.Equal(10, ed.OpenRepeat!.Count);
        Assert.Equal(40, ed.Blocks[0].TotalDurationS);

        ed.SetDuration(0, 30); // 30/30 instead of 40/20
        ed.Undo();
        Assert.Equal(2, ed.OpenDepth); // still inside after undo
        Assert.Equal(40, ed.Blocks[0].TotalDurationS);

        ed.CloseRepeat();
        Assert.Equal(1, ed.OpenDepth);
        Assert.Equal(inner, ed.Selected);
        ed.CloseRepeat();
        Assert.Equal(0, ed.OpenDepth);
        Assert.Equal(outer, ed.Selected);
    }
}
