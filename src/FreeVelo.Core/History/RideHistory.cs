using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FreeVelo.Core.Training;

namespace FreeVelo.Core.History
{
    /// <summary>
    /// Summary of one ride, kept on the phone next to its FIT file. Public fields so Unity's JsonUtility can store it.
    /// </summary>
    [Serializable]
    public class RideRecord
    {
        /// <summary>FIT file name (unique per ride).</summary>
        public string id = "";
        public string title = "";

        /// <summary>Start, Unix seconds UTC.</summary>
        public long startUtc;
        public int durationS;
        public double distanceM;
        public double ascentM;
        public int avgPowerW;
        public int maxPowerW;
        public int normalizedPowerW;
        public double intensityFactor;
        public double tss;
        public int avgHeartRate;
        public int maxHeartRate;
        public int avgCadence;
        public double kj;

        /// <summary>FTP used for IF / TSS.</summary>
        public int ftpW;

        /// <summary>Workout id if a workout was ridden ("" for a free ride).</summary>
        public string workoutId = "";

        /// <summary>Share of the workout ridden (0–1, 1 = to the end).</summary>
        public double workoutCompleted;

        /// <summary>Average intensity setting during the workout (1.0 = as planned).</summary>
        public double workoutIntensity = 1;

        public bool groupRide;

        /// <summary>Set when the ride was rebuilt from a FIT file (imported).</summary>
        public bool imported;

        /// <summary>FTP estimated by an FTP test (0 if none).</summary>
        public int estimatedFtpW;

        /// <summary>Best average power for <see cref="RideAnalysis.CurveDurations"/> (0 if the ride was shorter).</summary>
        public int[] powerCurve = new int[0];

        /// <summary>Famous climbs ridden from the foot to the top in this ride.</summary>
        public ClimbEffort[] climbs = new ClimbEffort[0];

        public DateTime StartUtc => DateTimeOffset.FromUnixTimeSeconds(startUtc).UtcDateTime;
    }

    /// <summary>A famous climb ridden from the foot to the top.</summary>
    [Serializable]
    public class ClimbEffort
    {
        public string id = "";
        public string name = "";
        public double timeS;
    }

    /// <summary>A change of the FTP setting.</summary>
    [Serializable]
    public class FtpChange
    {
        public long timeUtc;
        public int ftpW;

        /// <summary>"test" (accepted from an FTP test) or "manual".</summary>
        public string source = "manual";
    }

    /// <summary>All rides and the FTP history (stored as one JSON file).</summary>
    [Serializable]
    public class RideHistoryData
    {
        public List<RideRecord> rides = new List<RideRecord>();
        public List<FtpChange> ftpHistory = new List<FtpChange>();
    }

    /// <summary>Builds <see cref="RideRecord"/>s from 1-second samples (a live recording or a FIT file).</summary>
    public static class RideAnalysis
    {
        /// <summary>Durations of the stored power curve: 5 s … 60 min.</summary>
        public static readonly int[] CurveDurations = { 5, 15, 30, 60, 120, 300, 600, 1200, 1800, 3600 };

        public static RideRecord Summarize(string id, string title, DateTime startUtc, IReadOnlyList<FitRecord> samples, int ftpW)
        {
            var record = new RideRecord
            {
                id = id,
                title = title,
                startUtc = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeSeconds(),
                ftpW = ftpW,
            };
            if (samples.Count == 0) return record;

            // One value per second (gaps filled with 0 W, e.g. pauses are not recorded at all)
            var power = new List<double>();
            DateTime t0 = samples[0].TimeUtc;
            foreach (var s in samples)
            {
                int second = (int)Math.Round((s.TimeUtc - t0).TotalSeconds);
                if (second < power.Count) continue;
                while (power.Count < second) power.Add(0);
                power.Add(s.PowerW ?? 0);
            }

            record.durationS = samples.Count;
            record.distanceM = samples.Select(s => s.DistanceM ?? 0).DefaultIfEmpty(0).Max();
            double ascent = 0;
            double? lastAlt = null;
            foreach (var s in samples)
            {
                if (!s.AltitudeM.HasValue) continue;
                if (lastAlt.HasValue && s.AltitudeM.Value > lastAlt.Value) ascent += s.AltitudeM.Value - lastAlt.Value;
                lastAlt = s.AltitudeM;
            }

            record.ascentM = Math.Round(ascent, 1);
            var withPower = samples.Where(s => s.PowerW.HasValue).Select(s => s.PowerW!.Value).ToList();
            record.avgPowerW = withPower.Count > 0 ? (int)Math.Round(withPower.Average()) : 0;
            record.maxPowerW = withPower.Count > 0 ? withPower.Max() : 0;
            record.kj = Math.Round(withPower.Sum() / 1000.0, 1);
            double np = PowerMetrics.NormalizedPower(power);
            record.normalizedPowerW = (int)Math.Round(np);
            record.intensityFactor = Math.Round(PowerMetrics.IntensityFactor(np, ftpW), 3);
            record.tss = Math.Round(PowerMetrics.TrainingStressScore(record.durationS, np, ftpW), 1);
            var hr = samples.Where(s => s.HeartRateBpm > 0).Select(s => s.HeartRateBpm!.Value).ToList();
            record.avgHeartRate = hr.Count > 0 ? (int)Math.Round(hr.Average()) : 0;
            record.maxHeartRate = hr.Count > 0 ? hr.Max() : 0;
            var cad = samples.Where(s => s.CadenceRpm > 0).Select(s => s.CadenceRpm!.Value).ToList();
            record.avgCadence = cad.Count > 0 ? (int)Math.Round(cad.Average()) : 0;
            record.powerCurve = CurveDurations.Select(d => (int)Math.Round(PowerMetrics.MeanMax(power, d) ?? 0)).ToArray();
            return record;
        }

        /// <summary>A ride from a FIT file; the title is taken from FreeVelo's file name ("2026-11-06_16-24_FTP-Rampentest").</summary>
        public static RideRecord FromFit(string fileName, byte[] fit, int ftpW)
        {
            var samples = FitReader.ReadRecords(fit);
            var start = samples.Count > 0 ? samples[0].TimeUtc : DateTime.UtcNow;
            var record = Summarize(fileName, TitleFromFileName(fileName), start, samples, ftpW);
            record.imported = true;
            return record;
        }

        public static string TitleFromFileName(string fileName)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(fileName);
            if (name.Length > 17 && DateTime.TryParseExact(name.Substring(0, 16), "yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                name = name.Substring(17);
            }

            return name.Replace('-', ' ').Trim();
        }
    }

    /// <summary>Fitness (CTL), fatigue (ATL) and form (TSB) for one day.</summary>
    public readonly struct FitnessDay
    {
        public FitnessDay(DateTime date, double tss, double ctl, double atl, double tsb)
        {
            Date = date;
            Tss = tss;
            Ctl = ctl;
            Atl = atl;
            Tsb = tsb;
        }

        public DateTime Date { get; }
        public double Tss { get; }

        /// <summary>Chronic training load: 42-day exponentially weighted TSS ("fitness").</summary>
        public double Ctl { get; }

        /// <summary>Acute training load: 7-day exponentially weighted TSS ("fatigue").</summary>
        public double Atl { get; }

        /// <summary>Training stress balance: yesterday's CTL − ATL ("form").</summary>
        public double Tsb { get; }
    }

    /// <summary>Weekly totals.</summary>
    public readonly struct WeekVolume
    {
        public WeekVolume(DateTime monday, int rides, int seconds, double distanceM, double tss)
        {
            Monday = monday;
            Rides = rides;
            Seconds = seconds;
            DistanceM = distanceM;
            Tss = tss;
        }

        public DateTime Monday { get; }
        public int Rides { get; }
        public int Seconds { get; }
        public double DistanceM { get; }
        public double Tss { get; }
    }

    /// <summary>Evaluations over the ride history (all dates are local calendar days).</summary>
    public static class TrainingLoad
    {
        public const int CtlDays = 42;
        public const int AtlDays = 7;

        /// <summary>Daily CTL / ATL / TSB from the first ride until <paramref name="until"/>.</summary>
        public static List<FitnessDay> Fitness(IEnumerable<RideRecord> rides, DateTime until, Func<DateTime, DateTime>? toLocal = null)
        {
            toLocal ??= t => t.ToLocalTime();
            var daily = new Dictionary<DateTime, double>();
            foreach (var r in rides)
            {
                var day = toLocal(r.StartUtc).Date;
                daily[day] = (daily.TryGetValue(day, out double t) ? t : 0) + r.tss;
            }

            var result = new List<FitnessDay>();
            if (daily.Count == 0) return result;
            double ctl = 0, atl = 0;
            for (var day = daily.Keys.Min(); day <= until.Date; day = day.AddDays(1))
            {
                double tsb = ctl - atl;
                double tss = daily.TryGetValue(day, out double v) ? v : 0;
                ctl += (tss - ctl) / CtlDays;
                atl += (tss - atl) / AtlDays;
                result.Add(new FitnessDay(day, tss, ctl, atl, tsb));
            }

            return result;
        }

        /// <summary>Form (TSB) on a day, 0 without rides.</summary>
        public static double FormOn(IEnumerable<RideRecord> rides, DateTime day, Func<DateTime, DateTime>? toLocal = null)
        {
            var days = Fitness(rides, day, toLocal);
            return days.Count == 0 ? 0 : days[days.Count - 1].Tsb;
        }

        /// <summary>
        /// How much fitness (CTL) changed in each of the last <paramref name="weeks"/> weeks up to <paramref name="today"/>,
        /// newest first (TSS per day, per week; Allen &amp; Coggan recommend 3–7).
        /// </summary>
        public static List<double> WeeklyRamps(IEnumerable<RideRecord> rides, DateTime today, int weeks = 4, Func<DateTime, DateTime>? toLocal = null)
        {
            var days = Fitness(rides, today, toLocal);
            var result = new List<double>();
            for (int w = 0; w < weeks; w++)
            {
                int end = days.Count - 1 - (7 * w), start = end - 7;
                if (start < 0) break;
                result.Add(days[end].Ctl - days[start].Ctl);
            }

            return result;
        }

        /// <summary>Best power per <see cref="RideAnalysis.CurveDurations"/> over the rides (e.g. the last 90 days).</summary>
        public static int[] BestPowerCurve(IEnumerable<RideRecord> rides)
        {
            var best = new int[RideAnalysis.CurveDurations.Length];
            foreach (var r in rides)
            {
                for (int i = 0; i < best.Length && i < r.powerCurve.Length; i++) best[i] = Math.Max(best[i], r.powerCurve[i]);
            }

            return best;
        }

        /// <summary>Best time and number of ascents per climb id.</summary>
        public static Dictionary<string, (double BestS, int Count)> ClimbRecords(IEnumerable<RideRecord> rides)
        {
            var result = new Dictionary<string, (double, int)>();
            foreach (var r in rides)
            {
                foreach (var c in r.climbs ?? new ClimbEffort[0])
                {
                    result[c.id] = result.TryGetValue(c.id, out var old) ? (Math.Min(old.Item1, c.timeS), old.Item2 + 1) : (c.timeS, 1);
                }
            }

            return result;
        }

        public static DateTime Monday(DateTime day) => day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));

        /// <summary>Totals per week (Monday–Sunday) for the last <paramref name="weeks"/> weeks up to <paramref name="until"/>.</summary>
        public static List<WeekVolume> Weekly(IEnumerable<RideRecord> rides, DateTime until, int weeks, Func<DateTime, DateTime>? toLocal = null)
        {
            toLocal ??= t => t.ToLocalTime();
            var list = rides.ToList();
            var result = new List<WeekVolume>();
            var last = Monday(until);
            for (int w = weeks - 1; w >= 0; w--)
            {
                var monday = last.AddDays(-7 * w);
                var inWeek = list.Where(r => Monday(toLocal(r.StartUtc)) == monday).ToList();
                result.Add(new WeekVolume(monday, inWeek.Count, inWeek.Sum(r => r.durationS), inWeek.Sum(r => r.distanceM), inWeek.Sum(r => r.tss)));
            }

            return result;
        }
    }
}
