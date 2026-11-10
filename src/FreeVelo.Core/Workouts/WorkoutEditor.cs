using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.Workouts
{
    /// <summary>
    /// Editing session for one workout: works on a private copy, offers the operations of the editor screen
    /// (add blocks by zone, reorder by drag &amp; drop, change duration/power/repeats, open repeat blocks) with
    /// undo/redo. The caller saves <see cref="Workout"/> when done.
    /// </summary>
    public sealed class WorkoutEditor
    {
        public const int MinDurationS = 10;
        public const int MaxDurationS = 4 * 3600;
        public const double MinPercent = 10;
        public const double MaxPercent = 300;

        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();
        private string _saved;
        private string? _gestureStart;

        public WorkoutEditor(Workout source, int ftpW)
        {
            Workout = WorkoutFiles.Clone(source);
            FtpW = Math.Max(1, ftpW);
            _saved = Snapshot();
        }

        /// <summary>Creates an editor for a new, empty workout (one warm-up block to start from).</summary>
        public static WorkoutEditor CreateNew(string name, int ftpW)
        {
            var workout = new Workout(string.Empty, new LocalizedText(name)) { Category = WorkoutCategory.Custom, Author = "FreeVelo" };
            workout.Blocks.Add(new RampBlock(600, PowerTarget.Percent(45), PowerTarget.Percent(70)));
            return new WorkoutEditor(workout, ftpW);
        }

        public Workout Workout { get; private set; }

        public int FtpW { get; set; }

        /// <summary>Repeat block whose children are being edited (null = top level).</summary>
        public RepeatBlock? OpenRepeat { get; private set; }

        /// <summary>The blocks currently shown: the workout's top level or the children of <see cref="OpenRepeat"/>.</summary>
        public List<WorkoutBlock> Blocks => OpenRepeat?.Blocks ?? Workout.Blocks;

        /// <summary>Index of the selected block in <see cref="Blocks"/> (-1 = none).</summary>
        public int Selected { get; set; } = -1;

        public WorkoutBlock? SelectedBlock => Selected >= 0 && Selected < Blocks.Count ? Blocks[Selected] : null;

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        /// <summary>True if the workout differs from the last saved/loaded state.</summary>
        public bool IsDirty => Snapshot() != _saved;

        public void MarkSaved() => _saved = Snapshot();

        // ------------------------------------------------------------------ adding blocks

        /// <summary>Adds a steady block after the selection (or at <paramref name="index"/>) and selects it.</summary>
        public SteadyBlock AddSteady(double percentFtp, int durationS = 300, int? index = null) =>
            Insert(new SteadyBlock(ClampDuration(durationS), PowerTarget.Percent(ClampPercent(percentFtp))), index);

        public RampBlock AddRamp(double fromPercent, double toPercent, int durationS = 600, int? index = null) =>
            Insert(new RampBlock(ClampDuration(durationS), PowerTarget.Percent(ClampPercent(fromPercent)), PowerTarget.Percent(ClampPercent(toPercent))), index);

        public FreeRideBlock AddFreeRide(int durationS = 300, int? index = null) =>
            Insert(new FreeRideBlock(ClampDuration(durationS)), index);

        /// <summary>Adds intervals: <paramref name="count"/> × (work, recovery).</summary>
        public RepeatBlock AddIntervals(int count = 4, int workS = 180, double workPercent = 115, int restS = 180, double restPercent = 55, int? index = null)
        {
            if (OpenRepeat != null) throw new InvalidOperationException("Repeat blocks cannot be nested in the editor.");
            var repeat = new RepeatBlock(Math.Max(1, count), new WorkoutBlock[]
            {
                new SteadyBlock(ClampDuration(workS), PowerTarget.Percent(ClampPercent(workPercent))),
                new SteadyBlock(ClampDuration(restS), PowerTarget.Percent(ClampPercent(restPercent))),
            });
            return Insert(repeat, index);
        }

        private T Insert<T>(T block, int? index)
            where T : WorkoutBlock
        {
            Record();
            int at = Math.Clamp(index ?? (Selected >= 0 ? Selected + 1 : Blocks.Count), 0, Blocks.Count);
            Blocks.Insert(at, block);
            Selected = at;
            return block;
        }

        // ------------------------------------------------------------------ editing

        public void Duplicate()
        {
            if (SelectedBlock == null) return;
            var copy = CloneBlock(SelectedBlock);
            Record();
            Blocks.Insert(Selected + 1, copy);
            Selected++;
        }

        public void Delete()
        {
            if (SelectedBlock == null) return;
            if (OpenRepeat != null && Blocks.Count == 1) return; // a repeat keeps at least one block
            Record();
            Blocks.RemoveAt(Selected);
            Selected = Math.Min(Selected, Blocks.Count - 1);
        }

        /// <summary>Drag &amp; drop: moves block <paramref name="from"/> so that it ends up at position <paramref name="to"/>.</summary>
        public void Move(int from, int to)
        {
            if (from < 0 || from >= Blocks.Count) return;
            to = Math.Clamp(to, 0, Blocks.Count - 1);
            if (from == to) return;
            Record();
            var block = Blocks[from];
            Blocks.RemoveAt(from);
            Blocks.Insert(to, block);
            Selected = to;
        }

        /// <summary>Sets the duration of a timed block (rounded to whole seconds, 10 s … 4 h).</summary>
        public void SetDuration(int index, int seconds)
        {
            if (!(Get(index) is TimedBlock t)) return;
            seconds = ClampDuration(seconds);
            if (t.DurationS == seconds) return;
            Record();
            t.DurationS = seconds;
        }

        /// <summary>Sets the power of a steady block, or one end of a ramp (<paramref name="rampEnd"/> = true → To).</summary>
        public void SetPercent(int index, double percentFtp, bool rampEnd = false)
        {
            percentFtp = ClampPercent(percentFtp);
            switch (Get(index))
            {
                case SteadyBlock s when Math.Abs(s.Target.ToPercent(FtpW) - percentFtp) > 1e-6:
                    Record();
                    s.Target = InUnitOf(s.Target, percentFtp);
                    break;
                case RampBlock r when !rampEnd && Math.Abs(r.From.ToPercent(FtpW) - percentFtp) > 1e-6:
                    Record();
                    r.From = InUnitOf(r.From, percentFtp);
                    break;
                case RampBlock r when rampEnd && Math.Abs(r.To.ToPercent(FtpW) - percentFtp) > 1e-6:
                    Record();
                    r.To = InUnitOf(r.To, percentFtp);
                    break;
            }
        }

        public void SetRepeatCount(int index, int count)
        {
            if (!(Get(index) is RepeatBlock r)) return;
            count = Math.Clamp(count, 1, 50);
            if (r.Count == count) return;
            Record();
            r.Count = count;
        }

        /// <summary>The last repetition of an interval block ends without its last block (e.g. no final recovery).</summary>
        public void SetSkipLast(int index, bool skip)
        {
            if (!(Get(index) is RepeatBlock r) || r.SkipLastBlock == skip) return;
            Record();
            r.SkipLastBlock = skip;
        }

        /// <summary>Switches all targets of the workout to watts (true) or % FTP (false), keeping the power at the current FTP.</summary>
        public void SetUnit(bool watts)
        {
            Record();
            Convert(Workout.Blocks);

            void Convert(IEnumerable<WorkoutBlock> blocks)
            {
                foreach (var b in blocks)
                {
                    switch (b)
                    {
                        case SteadyBlock s: s.Target = To(s.Target); break;
                        case RampBlock r:
                            r.From = To(r.From);
                            r.To = To(r.To);
                            break;
                        case RepeatBlock rep: Convert(rep.Blocks); break;
                    }
                }
            }

            PowerTarget To(PowerTarget t) => watts
                ? PowerTarget.Watts(Math.Round(t.ToWatts(FtpW)))
                : PowerTarget.Percent(Math.Round(t.ToPercent(FtpW), 1));
        }

        public void Rename(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name == Workout.Name.Get("en")) return;
            Record();

            // A renamed workout gets one name for all languages.
            Workout.Name = new LocalizedText(name);
        }

        // ------------------------------------------------------------------ repeat blocks

        public void OpenSelectedRepeat()
        {
            if (OpenRepeat == null && SelectedBlock is RepeatBlock r)
            {
                OpenRepeat = r;
                Selected = 0;
            }
        }

        public void CloseRepeat()
        {
            if (OpenRepeat == null) return;
            int index = Workout.Blocks.IndexOf(OpenRepeat);
            OpenRepeat = null;
            Selected = index;
        }

        // ------------------------------------------------------------------ undo / redo

        /// <summary>
        /// Starts a continuous change (e.g. dragging a block edge): all changes until <see cref="EndGesture"/> form one
        /// undo step.
        /// </summary>
        public void BeginGesture()
        {
            if (_gestureStart == null) _gestureStart = Snapshot();
        }

        public void EndGesture()
        {
            if (_gestureStart == null) return;
            string start = _gestureStart;
            _gestureStart = null;
            if (start == Snapshot()) return;
            _undo.Add(start);
            _redo.Clear();
        }

        public void Undo() => Restore(_undo, _redo);

        public void Redo() => Restore(_redo, _undo);

        private void Record()
        {
            if (_gestureStart != null) return; // recorded once at the end of the gesture
            _undo.Add(Snapshot());
            if (_undo.Count > 100) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private void Restore(List<string> from, List<string> to)
        {
            if (from.Count == 0) return;
            int openIndex = OpenRepeat != null ? Workout.Blocks.IndexOf(OpenRepeat) : -1;
            to.Add(Snapshot());
            string state = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Workout = WorkoutFiles.FromZwo(state, Workout.Id);
            OpenRepeat = openIndex >= 0 && openIndex < Workout.Blocks.Count ? Workout.Blocks[openIndex] as RepeatBlock : null;
            Selected = Math.Min(Selected, Blocks.Count - 1);
        }

        private string Snapshot() => WorkoutFiles.ToZwo(Workout);

        // ------------------------------------------------------------------ statistics

        /// <summary>Estimated training stress score at <see cref="FtpW"/> (free-ride parts counted at 75 %).</summary>
        public double EstimatedTss() => EstimateTss(Workout, FtpW);

        /// <summary>Estimated TSS of a workout at <paramref name="ftpW"/> (free-ride parts counted at 75 %).</summary>
        public static double EstimateTss(Workout workout, int ftpW)
        {
            ftpW = Math.Max(1, ftpW);
            double sum = 0;
            foreach (var seg in workout.Flatten())
            {
                for (int t = 0; t < seg.DurationS; t += 5)
                {
                    double w = seg.BaseTargetW(t + 2.5, ftpW) ?? (0.75 * ftpW);
                    double f = w / ftpW;
                    sum += f * f * Math.Min(5, seg.DurationS - t);
                }
            }

            return sum / 36.0;
        }

        // ------------------------------------------------------------------ helpers

        private WorkoutBlock? Get(int index) => index >= 0 && index < Blocks.Count ? Blocks[index] : null;

        private PowerTarget InUnitOf(PowerTarget existing, double percentFtp) => existing.Unit == TargetUnit.Watts
            ? PowerTarget.Watts(Math.Round(percentFtp / 100.0 * FtpW))
            : PowerTarget.Percent(Math.Round(percentFtp, 1));

        private static int ClampDuration(int seconds) => Math.Clamp(seconds, MinDurationS, MaxDurationS);

        private static double ClampPercent(double percent) => Math.Clamp(percent, MinPercent, MaxPercent);

        private static WorkoutBlock CloneBlock(WorkoutBlock block)
        {
            var w = new Workout("x", "x");
            w.Blocks.Add(block);
            return WorkoutFiles.Clone(w).Blocks[0];
        }
    }
}
