using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.Workouts
{
    /// <summary>Trainer mode requested by the workout.</summary>
    public enum TrainerMode
    {
        /// <summary>Hold <see cref="WorkoutState.TargetW"/>.</summary>
        Erg,

        /// <summary>No target, simulate the road.</summary>
        Sim,
    }

    /// <summary>Snapshot of a running workout.</summary>
    public sealed class WorkoutState
    {
        internal WorkoutState(
            WorkoutSegment? segment,
            WorkoutSegment? next,
            double elapsedS,
            double segmentElapsedS,
            int? targetW,
            double segmentIntensity,
            IReadOnlyList<TextCue> newCues,
            bool isPaused,
            bool isFinished)
        {
            Segment = segment;
            NextSegment = next;
            ElapsedS = elapsedS;
            SegmentElapsedS = segmentElapsedS;
            TargetW = targetW;
            SegmentIntensity = segmentIntensity;
            NewCues = newCues;
            IsPaused = isPaused;
            IsFinished = isFinished;
        }

        /// <summary>Current segment (null when finished).</summary>
        public WorkoutSegment? Segment { get; }

        public WorkoutSegment? NextSegment { get; }

        /// <summary>Workout time in seconds (excluding pauses).</summary>
        public double ElapsedS { get; }

        public double SegmentElapsedS { get; }

        public double SegmentRemainingS => Segment == null ? 0 : Math.Max(0, Segment.DurationS - SegmentElapsedS);

        /// <summary>ERG target in watts, null in free ride segments or when finished.</summary>
        public int? TargetW { get; }

        /// <summary>Intensity factor applied to the current segment (global × per-segment).</summary>
        public double SegmentIntensity { get; }

        public CadenceRange? Cadence => Segment?.Block.Cadence;

        public TrainerMode Mode => TargetW.HasValue ? TrainerMode.Erg : TrainerMode.Sim;

        /// <summary>Cues that became due during the last update.</summary>
        public IReadOnlyList<TextCue> NewCues { get; }

        public bool IsPaused { get; }

        public bool IsFinished { get; }
    }

    /// <summary>
    /// Runs a workout: computes ERG targets over time, handles pause/skip/extend,
    /// global intensity and per-block intensity adjustments (e.g. via Zwift Click).
    /// </summary>
    public sealed class WorkoutEngine
    {
        public const double MinIntensity = 0.5;
        public const double MaxIntensity = 1.5;

        private readonly List<WorkoutSegment> _segments;
        private readonly int[] _durations;
        private readonly Dictionary<int, double> _segmentIntensity = new Dictionary<int, double>();
        private double _globalIntensity = 1.0;
        private int _index;
        private double _segmentElapsed;

        public WorkoutEngine(Workout workout, int ftpW)
        {
            if (ftpW <= 0) throw new ArgumentOutOfRangeException(nameof(ftpW));
            Workout = workout;
            FtpW = ftpW;
            _segments = workout.Flatten().ToList();
            _durations = _segments.Select(s => s.DurationS).ToArray();
        }

        public Workout Workout { get; }

        public int FtpW { get; }

        public IReadOnlyList<WorkoutSegment> Segments => _segments;

        /// <summary>Elapsed workout time in seconds (pauses excluded).</summary>
        public double ElapsedS { get; private set; }

        /// <summary>Total duration including extensions.</summary>
        public int TotalDurationS => _durations.Sum();

        public bool IsPaused { get; private set; }

        public bool IsFinished => _index >= _segments.Count;

        public int CurrentIndex => _index;

        /// <summary>Scales all targets, e.g. 0.95 for the whole workout at 95 %.</summary>
        public double GlobalIntensity
        {
            get => _globalIntensity;
            set => _globalIntensity = Clamp(value);
        }

        /// <summary>Duration of segment <paramref name="index"/> including extensions.</summary>
        public int SegmentDurationS(int index) => _durations[index];

        /// <summary>Per-segment intensity factor (1.0 = unchanged).</summary>
        public double SegmentIntensity(int index) =>
            _segmentIntensity.TryGetValue(index, out double f) ? f : 1.0;

        /// <summary>Changes the intensity of the current block only, e.g. +0.01 per Zwift Click press.</summary>
        public void AdjustCurrentSegmentIntensity(double delta)
        {
            if (IsFinished) return;
            _segmentIntensity[_index] = Clamp(SegmentIntensity(_index) + delta);
        }

        public void Pause() => IsPaused = true;

        public void Resume() => IsPaused = false;

        /// <summary>Jumps to the start of the next segment.</summary>
        public void SkipSegment()
        {
            if (IsFinished) return;
            ElapsedS += _durations[_index] - _segmentElapsed;
            _index++;
            _segmentElapsed = 0;
        }

        /// <summary>Makes the current segment longer, e.g. +60 s.</summary>
        public void ExtendCurrentSegment(int seconds)
        {
            if (IsFinished || seconds <= 0) return;
            _durations[_index] += seconds;
        }

        /// <summary>Jumps to an absolute workout time, e.g. when joining a group workout mid-ride.</summary>
        public void SeekTo(double elapsedS)
        {
            _index = 0;
            _segmentElapsed = 0;
            ElapsedS = 0;
            Advance(Math.Max(0, elapsedS), new List<TextCue>());
        }

        /// <summary>Current state without advancing time.</summary>
        public WorkoutState Current => BuildState(Array.Empty<TextCue>());

        /// <summary>Advances the workout by <paramref name="dtSeconds"/> (ignored while paused).</summary>
        public WorkoutState Update(double dtSeconds)
        {
            var cues = new List<TextCue>();
            if (!IsPaused && dtSeconds > 0)
            {
                Advance(dtSeconds, cues);
            }

            return BuildState(cues);
        }

        private void Advance(double dt, List<TextCue> cues)
        {
            while (dt > 0 && !IsFinished)
            {
                double before = _segmentElapsed;
                double remaining = _durations[_index] - _segmentElapsed;
                double step = Math.Min(dt, remaining);
                _segmentElapsed += step;
                ElapsedS += step;
                dt -= step;

                foreach (var cue in _segments[_index].Block.Cues)
                {
                    bool due = cue.OffsetS == 0 ? before == 0 : cue.OffsetS > before && cue.OffsetS <= _segmentElapsed;
                    if (due) cues.Add(cue);
                }

                if (_segmentElapsed >= _durations[_index])
                {
                    _index++;
                    _segmentElapsed = 0;
                }
            }
        }

        private WorkoutState BuildState(IReadOnlyList<TextCue> cues)
        {
            if (IsFinished)
            {
                return new WorkoutState(null, null, ElapsedS, 0, null, 1.0, cues, IsPaused, true);
            }

            var segment = _segments[_index];
            var next = _index + 1 < _segments.Count ? _segments[_index + 1] : null;
            double intensity = Clamp(_globalIntensity * SegmentIntensity(_index));

            // Ramps are stretched over an extended duration.
            double progress = segment.DurationS * (_segmentElapsed / _durations[_index]);
            double? baseW = segment.BaseTargetW(progress, FtpW);
            int? target = baseW.HasValue ? (int?)Math.Max(0, (int)Math.Round(baseW.Value * intensity)) : null;

            return new WorkoutState(segment, next, ElapsedS, _segmentElapsed, target, intensity, cues, IsPaused, false);
        }

        private static double Clamp(double value) => Math.Max(MinIntensity, Math.Min(MaxIntensity, value));
    }
}
