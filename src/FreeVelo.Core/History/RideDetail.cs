using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Training;

namespace FreeVelo.Core.History
{
    /// <summary>One point of the ride graphs (averaged over a few seconds).</summary>
    public readonly struct RideGraphPoint
    {
        public RideGraphPoint(double timeS, double powerW, double? heartRate, double? altitudeM, double? cadence)
        {
            TimeS = timeS;
            PowerW = powerW;
            HeartRate = heartRate;
            AltitudeM = altitudeM;
            Cadence = cadence;
        }

        public double TimeS { get; }
        public double PowerW { get; }
        public double? HeartRate { get; }
        public double? AltitudeM { get; }
        public double? Cadence { get; }
    }

    /// <summary>A lap (FreeVelo: one per workout block).</summary>
    public sealed class RideLapDetail
    {
        public int Number { get; set; }
        public int StartS { get; set; }
        public int DurationS { get; set; }
        public int AvgPowerW { get; set; }
        public int MaxPowerW { get; set; }
        public int? AvgHeartRate { get; set; }
        public int? AvgCadence { get; set; }
    }

    /// <summary>Everything the ride detail view shows, computed from the ride's FIT file.</summary>
    public sealed class RideDetail
    {
        public IReadOnlyList<RideGraphPoint> Graph { get; private set; } = Array.Empty<RideGraphPoint>();
        public IReadOnlyDictionary<int, int> TimeInPowerZones { get; private set; } = new Dictionary<int, int>();
        public IReadOnlyDictionary<int, int> TimeInHeartRateZones { get; private set; } = new Dictionary<int, int>();
        public IReadOnlyList<RideLapDetail> Laps { get; private set; } = Array.Empty<RideLapDetail>();
        public bool HasHeartRate { get; private set; }
        public int DurationS { get; private set; }

        /// <summary>Builds the detail; <paramref name="graphPoints"/> limits the graph resolution.</summary>
        public static RideDetail From(FitActivity activity, int ftpW, PowerZoneModel powerZones, HeartRateZoneModel hrZones, int graphPoints = 240)
        {
            var records = activity.Records;
            var detail = new RideDetail();
            if (records.Count == 0) return detail;
            var t0 = records[0].TimeUtc;
            detail.DurationS = records.Count;
            detail.HasHeartRate = records.Any(r => r.HeartRateBpm > 0);
            detail.TimeInPowerZones = PowerMetrics.TimeInZones(records.Select(r => (double)(r.PowerW ?? 0)).ToList(), powerZones, ftpW);
            detail.TimeInHeartRateZones = hrZones.TimeInZones(records.Select(r => r.HeartRateBpm));

            // Graph: averages over buckets of equal length
            int bucket = Math.Max(1, (int)Math.Ceiling(records.Count / (double)Math.Max(10, graphPoints)));
            var graph = new List<RideGraphPoint>();
            for (int i = 0; i < records.Count; i += bucket)
            {
                var part = records.Skip(i).Take(bucket).ToList();
                var hr = part.Where(r => r.HeartRateBpm > 0).Select(r => (double)r.HeartRateBpm!.Value).ToList();
                var alt = part.Where(r => r.AltitudeM.HasValue).Select(r => r.AltitudeM!.Value).ToList();
                var cad = part.Where(r => r.CadenceRpm > 0).Select(r => (double)r.CadenceRpm!.Value).ToList();
                graph.Add(new RideGraphPoint(
                    (part[0].TimeUtc - t0).TotalSeconds,
                    part.Average(r => (double)(r.PowerW ?? 0)),
                    hr.Count > 0 ? hr.Average() : (double?)null,
                    alt.Count > 0 ? alt.Average() : (double?)null,
                    cad.Count > 0 ? cad.Average() : (double?)null));
            }

            detail.Graph = graph;

            // Laps: from the lap starts (the first lap starts with the ride)
            var starts = activity.LapStarts.Where(s => s > t0.AddSeconds(1)).Distinct().ToList();
            starts.Insert(0, t0);
            var laps = new List<RideLapDetail>();
            for (int l = 0; l < starts.Count; l++)
            {
                var from = starts[l];
                var to = l + 1 < starts.Count ? starts[l + 1] : DateTime.MaxValue;
                var part = records.Where(r => r.TimeUtc > from.AddSeconds(l == 0 ? -1 : 0) && r.TimeUtc <= to).ToList();
                if (part.Count < 5) continue;
                var hr = part.Where(r => r.HeartRateBpm > 0).Select(r => r.HeartRateBpm!.Value).ToList();
                var cad = part.Where(r => r.CadenceRpm > 0).Select(r => r.CadenceRpm!.Value).ToList();
                laps.Add(new RideLapDetail
                {
                    Number = laps.Count + 1,
                    StartS = (int)Math.Round((part[0].TimeUtc - t0).TotalSeconds),
                    DurationS = part.Count,
                    AvgPowerW = (int)Math.Round(part.Average(r => (double)(r.PowerW ?? 0))),
                    MaxPowerW = part.Max(r => r.PowerW ?? 0),
                    AvgHeartRate = hr.Count > 0 ? (int)Math.Round(hr.Average()) : (int?)null,
                    AvgCadence = cad.Count > 0 ? (int)Math.Round(cad.Average()) : (int?)null,
                });
            }

            detail.Laps = laps.Count > 1 ? laps : (IReadOnlyList<RideLapDetail>)Array.Empty<RideLapDetail>();
            return detail;
        }
    }
}
