using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.Recording
{
    /// <summary>One recorded second.</summary>
    public readonly struct RideSample
    {
        public RideSample(DateTime timeUtc, int? powerW, int? heartRateBpm, int? cadenceRpm, double speedMps, double distanceM, double altitudeM, int? targetW,
            double? worldX = null, double? worldZ = null)
        {
            WorldX = worldX;
            WorldZ = worldZ;
            TimeUtc = timeUtc;
            PowerW = powerW;
            HeartRateBpm = heartRateBpm;
            CadenceRpm = cadenceRpm;
            SpeedMps = speedMps;
            DistanceM = distanceM;
            AltitudeM = altitudeM;
            TargetW = targetW;
        }

        public DateTime TimeUtc { get; }

        /// <summary>Position in the virtual world (metres east / north), written to FIT as GPS in the ocean.</summary>
        public double? WorldX { get; }

        public double? WorldZ { get; }

        public int? PowerW { get; }

        public int? HeartRateBpm { get; }

        public int? CadenceRpm { get; }

        public double SpeedMps { get; }

        /// <summary>Total distance since the start of the ride.</summary>
        public double DistanceM { get; }

        public double AltitudeM { get; }

        /// <summary>ERG target at that time (not written to FIT records).</summary>
        public int? TargetW { get; }
    }

    /// <summary>A lap: samples from <see cref="StartIndex"/> up to the next lap.</summary>
    public sealed class RideLap
    {
        public RideLap(int startIndex, string? name)
        {
            StartIndex = startIndex;
            Name = name;
        }

        public int StartIndex { get; }

        public string? Name { get; }
    }

    /// <summary>Statistics of a range of samples (whole ride or a lap).</summary>
    public sealed class RideStats
    {
        public int Seconds { get; internal set; }

        public double DistanceM { get; internal set; }

        public double AvgSpeedMps { get; internal set; }

        public double MaxSpeedMps { get; internal set; }

        public int? AvgPowerW { get; internal set; }

        public int? MaxPowerW { get; internal set; }

        public int? AvgHeartRate { get; internal set; }

        public int? MaxHeartRate { get; internal set; }

        public int? AvgCadence { get; internal set; }

        public int? MaxCadence { get; internal set; }

        public double AscentM { get; internal set; }

        /// <summary>Mechanical work in kJ (≈ kcal burned, since human efficiency ≈ 1 / 4.18).</summary>
        public double WorkKj { get; internal set; }
    }

    /// <summary>Recorded ride: 1-second samples and laps (e.g. one per workout block).</summary>
    public sealed class RideRecording
    {
        private readonly List<RideSample> _samples = new List<RideSample>();
        private readonly List<RideLap> _laps = new List<RideLap>();

        public RideRecording(DateTime startUtc, string name)
        {
            StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
            Name = name;
        }

        public DateTime StartUtc { get; }

        public string Name { get; }

        public IReadOnlyList<RideSample> Samples => _samples;

        /// <summary>Laps; there is always at least one once a sample was added.</summary>
        public IReadOnlyList<RideLap> Laps => _laps;

        public DateTime EndUtc => _samples.Count > 0 ? _samples[_samples.Count - 1].TimeUtc : StartUtc;

        public void Add(RideSample sample)
        {
            if (_laps.Count == 0) _laps.Add(new RideLap(0, null));
            _samples.Add(sample);
        }

        /// <summary>Starts a new lap with the next sample.</summary>
        public void StartLap(string? name = null)
        {
            if (_laps.Count > 0 && _laps[_laps.Count - 1].StartIndex == _samples.Count)
            {
                _laps[_laps.Count - 1] = new RideLap(_samples.Count, name); // empty lap: rename
                return;
            }

            _laps.Add(new RideLap(_samples.Count, name));
        }

        /// <summary>Power values (missing = 0), one per second, for metrics like NP.</summary>
        public IReadOnlyList<double> PowerSeries() => _samples.Select(s => (double)(s.PowerW ?? 0)).ToList();

        public (int Start, int End) LapRange(int lapIndex)
        {
            int start = _laps[lapIndex].StartIndex;
            int end = lapIndex + 1 < _laps.Count ? _laps[lapIndex + 1].StartIndex : _samples.Count;
            return (start, end);
        }

        public RideStats Stats() => Stats(0, _samples.Count);

        public RideStats Stats(int start, int end)
        {
            var stats = new RideStats();
            if (end <= start) return stats;

            var range = _samples.Skip(start).Take(end - start).ToList();
            stats.Seconds = range.Count;
            double startDistance = start > 0 ? _samples[start - 1].DistanceM : 0;
            stats.DistanceM = range[range.Count - 1].DistanceM - startDistance;
            stats.AvgSpeedMps = stats.DistanceM / range.Count;
            stats.MaxSpeedMps = range.Max(s => s.SpeedMps);

            var power = range.Where(s => s.PowerW.HasValue).Select(s => s.PowerW!.Value).ToList();
            if (power.Count > 0)
            {
                stats.AvgPowerW = (int)Math.Round(power.Average(), MidpointRounding.AwayFromZero);
                stats.MaxPowerW = power.Max();
                stats.WorkKj = power.Sum() / 1000.0;
            }

            var hr = range.Where(s => s.HeartRateBpm.HasValue).Select(s => s.HeartRateBpm!.Value).ToList();
            if (hr.Count > 0)
            {
                stats.AvgHeartRate = (int)Math.Round(hr.Average(), MidpointRounding.AwayFromZero);
                stats.MaxHeartRate = hr.Max();
            }

            // Average cadence while pedaling (as Garmin/Strava do).
            var cadence = range.Where(s => s.CadenceRpm.HasValue && s.CadenceRpm > 0).Select(s => s.CadenceRpm!.Value).ToList();
            if (cadence.Count > 0)
            {
                stats.AvgCadence = (int)Math.Round(cadence.Average(), MidpointRounding.AwayFromZero);
                stats.MaxCadence = cadence.Max();
            }

            for (int i = 1; i < range.Count; i++) stats.AscentM += Math.Max(0, range[i].AltitudeM - range[i - 1].AltitudeM);
            return stats;
        }
    }
}
