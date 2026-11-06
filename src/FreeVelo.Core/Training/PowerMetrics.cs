using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.Training
{
    /// <summary>Training metrics from 1-second power samples.</summary>
    public static class PowerMetrics
    {
        public static double Average(IReadOnlyList<double> watts) => watts.Count == 0 ? 0 : watts.Average();

        /// <summary>
        /// Normalized Power: 4th root of the mean of the 4th powers of the 30 s rolling average.
        /// Falls back to the average for rides shorter than 30 s.
        /// </summary>
        public static double NormalizedPower(IReadOnlyList<double> watts)
        {
            const int window = 30;
            if (watts.Count < window) return Average(watts);

            double sum = 0;
            for (int i = 0; i < window; i++) sum += watts[i];

            double total = 0;
            int count = 0;
            for (int i = window - 1; i < watts.Count; i++)
            {
                if (i >= window) sum += watts[i] - watts[i - window];
                double avg = sum / window;
                total += avg * avg * avg * avg;
                count++;
            }

            return Math.Pow(total / count, 0.25);
        }

        public static double IntensityFactor(double normalizedPower, int ftpW) => ftpW > 0 ? normalizedPower / ftpW : 0;

        /// <summary>Training Stress Score: (s × NP × IF) / (FTP × 3600) × 100.</summary>
        public static double TrainingStressScore(int durationS, double normalizedPower, int ftpW) =>
            ftpW > 0 ? durationS * normalizedPower * IntensityFactor(normalizedPower, ftpW) / (ftpW * 3600.0) * 100.0 : 0;

        /// <summary>Best average power over <paramref name="durationS"/> seconds (mean maximal power); null if the ride is shorter.</summary>
        public static double? MeanMax(IReadOnlyList<double> watts, int durationS)
        {
            if (durationS <= 0 || watts.Count < durationS) return null;
            double sum = 0;
            for (int i = 0; i < durationS; i++) sum += watts[i];
            double best = sum;
            for (int i = durationS; i < watts.Count; i++)
            {
                sum += watts[i] - watts[i - durationS];
                if (sum > best) best = sum;
            }

            return best / durationS;
        }

        /// <summary>Power curve: best average power for standard durations.</summary>
        public static IReadOnlyDictionary<int, double> PowerCurve(IReadOnlyList<double> watts)
        {
            int[] durations = { 5, 15, 30, 60, 120, 300, 600, 1200, 1800, 3600 };
            var result = new SortedDictionary<int, double>();
            foreach (int d in durations)
            {
                double? best = MeanMax(watts, d);
                if (best.HasValue) result[d] = best.Value;
            }

            return result;
        }

        /// <summary>Seconds spent in each zone (key = zone number).</summary>
        public static IReadOnlyDictionary<int, int> TimeInZones(IReadOnlyList<double> watts, PowerZoneModel zones, int ftpW)
        {
            var result = zones.Zones.ToDictionary(z => z.Number, _ => 0);
            foreach (double w in watts)
            {
                result[zones.ZoneFor(w, ftpW).Number]++;
            }

            return result;
        }
    }
}
