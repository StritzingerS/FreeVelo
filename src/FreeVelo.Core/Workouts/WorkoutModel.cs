using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.Workouts
{
    /// <summary>Text in several languages ("en", "de"); falls back to English, then any language.</summary>
    public sealed class LocalizedText
    {
        public LocalizedText(string english, string? german = null)
        {
            Values["en"] = english;
            if (german != null) Values["de"] = german;
        }

        public LocalizedText(IDictionary<string, string> values)
        {
            foreach (var kv in values) Values[kv.Key] = kv.Value;
        }

        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>();

        public string Get(string language) =>
            Values.TryGetValue(language, out var text) ? text
            : Values.TryGetValue("en", out var en) ? en
            : Values.Values.FirstOrDefault() ?? string.Empty;

        public override string ToString() => Get("en");

        public static implicit operator LocalizedText(string english) => new LocalizedText(english);
    }

    public enum TargetUnit
    {
        /// <summary>Percent of FTP (e.g. 95 = 95 %).</summary>
        PercentFtp,

        /// <summary>Absolute watts.</summary>
        Watts,
    }

    /// <summary>A power target, relative to FTP or absolute.</summary>
    public readonly struct PowerTarget : IEquatable<PowerTarget>
    {
        public PowerTarget(double value, TargetUnit unit)
        {
            Value = value;
            Unit = unit;
        }

        public double Value { get; }

        public TargetUnit Unit { get; }

        public static PowerTarget Percent(double percentFtp) => new PowerTarget(percentFtp, TargetUnit.PercentFtp);

        public static PowerTarget Watts(double watts) => new PowerTarget(watts, TargetUnit.Watts);

        public double ToWatts(int ftpW) => Unit == TargetUnit.Watts ? Value : Value / 100.0 * ftpW;

        public double ToPercent(int ftpW) => Unit == TargetUnit.PercentFtp ? Value : ftpW > 0 ? Value / ftpW * 100.0 : 0.0;

        public bool Equals(PowerTarget other) => Value.Equals(other.Value) && Unit == other.Unit;

        public override bool Equals(object? obj) => obj is PowerTarget other && Equals(other);

        public override int GetHashCode() => (Value, Unit).GetHashCode();

        public override string ToString() => Unit == TargetUnit.Watts ? $"{Value:0} W" : $"{Value:0.#} % FTP";
    }

    /// <summary>Allowed cadence range in rpm.</summary>
    public readonly struct CadenceRange
    {
        public CadenceRange(int min, int max)
        {
            Min = Math.Min(min, max);
            Max = Math.Max(min, max);
        }

        public int Min { get; }

        public int Max { get; }

        public bool Contains(double rpm) => rpm >= Min && rpm <= Max;

        public override string ToString() => $"{Min}–{Max} rpm";
    }

    /// <summary>Text shown at an offset (seconds) within a block.</summary>
    public sealed class TextCue
    {
        public TextCue(int offsetS, LocalizedText text)
        {
            OffsetS = offsetS;
            Text = text;
        }

        public int OffsetS { get; }

        public LocalizedText Text { get; }
    }

    /// <summary>Base class of all workout blocks.</summary>
    public abstract class WorkoutBlock
    {
        /// <summary>Total duration including repetitions, in seconds.</summary>
        public abstract int TotalDurationS { get; }
    }

    /// <summary>Block with a duration and optional cadence target and cues.</summary>
    public abstract class TimedBlock : WorkoutBlock
    {
        protected TimedBlock(int durationS)
        {
            if (durationS <= 0) throw new ArgumentOutOfRangeException(nameof(durationS), "Duration must be positive.");
            DurationS = durationS;
        }

        public int DurationS { get; set; }

        public CadenceRange? Cadence { get; set; }

        public List<TextCue> Cues { get; } = new List<TextCue>();

        public override int TotalDurationS => DurationS;
    }

    /// <summary>Constant target power (ERG).</summary>
    public sealed class SteadyBlock : TimedBlock
    {
        public SteadyBlock(int durationS, PowerTarget target)
            : base(durationS)
        {
            Target = target;
        }

        public PowerTarget Target { get; set; }
    }

    /// <summary>Linear change of the target power (ERG), e.g. warm-up or ramp test.</summary>
    public sealed class RampBlock : TimedBlock
    {
        public RampBlock(int durationS, PowerTarget from, PowerTarget to)
            : base(durationS)
        {
            From = from;
            To = to;
        }

        public PowerTarget From { get; set; }

        public PowerTarget To { get; set; }
    }

    /// <summary>No target: the trainer switches to SIM mode (riding the route), e.g. for an all-out test effort.</summary>
    public sealed class FreeRideBlock : TimedBlock
    {
        public FreeRideBlock(int durationS, bool isMaxEffort = false)
            : base(durationS)
        {
            IsMaxEffort = isMaxEffort;
        }

        /// <summary>True for all-out test efforts (evaluated by FTP tests).</summary>
        public bool IsMaxEffort { get; set; }
    }

    /// <summary>Repeats its child blocks <see cref="Count"/> times (nestable).</summary>
    public sealed class RepeatBlock : WorkoutBlock
    {
        public RepeatBlock(int count, IEnumerable<WorkoutBlock> blocks)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
            Blocks.AddRange(blocks);
        }

        public int Count { get; set; }

        public List<WorkoutBlock> Blocks { get; } = new List<WorkoutBlock>();

        public override int TotalDurationS => Count * Blocks.Sum(b => b.TotalDurationS);
    }

    public enum WorkoutCategory
    {
        Recovery,
        Endurance,
        Tempo,
        SweetSpot,
        Threshold,
        Vo2Max,
        Anaerobic,
        Sprint,
        FtpTest,
        Custom,
    }

    /// <summary>A structured workout.</summary>
    public sealed class Workout
    {
        public Workout(string id, LocalizedText name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; set; }

        public LocalizedText Name { get; set; }

        public LocalizedText? Description { get; set; }

        public WorkoutCategory Category { get; set; } = WorkoutCategory.Custom;

        public string? Author { get; set; }

        public List<WorkoutBlock> Blocks { get; } = new List<WorkoutBlock>();

        public int TotalDurationS => Blocks.Sum(b => b.TotalDurationS);

        /// <summary>Expands repeat blocks into a flat list of segments with absolute start times.</summary>
        public IReadOnlyList<WorkoutSegment> Flatten()
        {
            var result = new List<WorkoutSegment>();
            int time = 0;
            Expand(Blocks, null, ref time, result);
            return result;
        }

        private static void Expand(IEnumerable<WorkoutBlock> blocks, RepeatInfo? repeat, ref int time, List<WorkoutSegment> result)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case RepeatBlock r:
                        for (int i = 0; i < r.Count; i++)
                        {
                            Expand(r.Blocks, new RepeatInfo(i + 1, r.Count), ref time, result);
                        }

                        break;

                    case TimedBlock t:
                        result.Add(new WorkoutSegment(result.Count, time, t, repeat));
                        time += t.DurationS;
                        break;
                }
            }
        }
    }

    /// <summary>Position inside a repeat block, e.g. interval 2 of 4.</summary>
    public readonly struct RepeatInfo
    {
        public RepeatInfo(int current, int total)
        {
            Current = current;
            Total = total;
        }

        public int Current { get; }

        public int Total { get; }

        public override string ToString() => $"{Current}/{Total}";
    }

    /// <summary>One flattened, timed block of a workout.</summary>
    public sealed class WorkoutSegment
    {
        public WorkoutSegment(int index, int startS, TimedBlock block, RepeatInfo? repeat)
        {
            Index = index;
            StartS = startS;
            Block = block;
            Repeat = repeat;
        }

        public int Index { get; }

        public int StartS { get; }

        public int DurationS => Block.DurationS;

        public int EndS => StartS + DurationS;

        public TimedBlock Block { get; }

        public RepeatInfo? Repeat { get; }

        public bool IsFreeRide => Block is FreeRideBlock;

        /// <summary>Base target in watts at <paramref name="offsetS"/> into the segment; null for free ride.</summary>
        public double? BaseTargetW(double offsetS, int ftpW)
        {
            switch (Block)
            {
                case SteadyBlock s:
                    return s.Target.ToWatts(ftpW);
                case RampBlock r:
                    double f = Math.Max(0.0, Math.Min(1.0, offsetS / r.DurationS));
                    double from = r.From.ToWatts(ftpW);
                    return from + ((r.To.ToWatts(ftpW) - from) * f);
                default:
                    return null;
            }
        }
    }
}
