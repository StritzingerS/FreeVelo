using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.History;
using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Training
{
    /// <summary>Why a workout is suggested.</summary>
    public enum SuggestionReason
    {
        /// <summary>No rides in the last weeks: an easy start.</summary>
        GettingStarted,

        /// <summary>Very tired (form below −25): recovery.</summary>
        Tired,

        /// <summary>Tired (form below −10) or a hard ride yesterday or today: endurance.</summary>
        Recovering,

        /// <summary>Form between −10 and +5: sweet spot or tempo.</summary>
        Balanced,

        /// <summary>Fresh (form above +5): threshold or VO2max.</summary>
        Fresh,
    }

    public sealed class WorkoutSuggestion
    {
        public WorkoutSuggestion(Workout workout, SuggestionReason reason, double form)
        {
            Workout = workout;
            Reason = reason;
            Form = form;
        }

        public Workout Workout { get; }
        public SuggestionReason Reason { get; }

        /// <summary>Form (TSB) the suggestion is based on.</summary>
        public double Form { get; }
    }

    /// <summary>
    /// The quick start on the home page: one workout that suits the rider's form (from the ride history's fitness
    /// and fatigue), and the workouts ridden last.
    /// </summary>
    public static class QuickStart
    {
        public const double TiredBelow = -25;
        public const double RecoveringBelow = -10;
        public const double FreshAbove = 5;

        /// <summary>A ride this hard (intensity factor) yesterday or today means no intensity again.</summary>
        public const double HardRideIf = 0.85;

        /// <summary>Without a ride in this many days the suggestion is an easy start.</summary>
        public const int InactiveDays = 28;

        private static readonly WorkoutCategory[] BalancedCategories = { WorkoutCategory.SweetSpot, WorkoutCategory.Tempo };
        private static readonly WorkoutCategory[] FreshCategories = { WorkoutCategory.Threshold, WorkoutCategory.Vo2Max };

        /// <summary>
        /// The workout that suits the form on <paramref name="today"/> (local date). Among several of the same kind the
        /// one ridden longest ago (or never) is chosen, so the suggestions vary.
        /// </summary>
        public static WorkoutSuggestion Suggest(IReadOnlyList<RideRecord> rides, DateTime today, IReadOnlyList<Workout> templates, Func<DateTime, DateTime>? toLocal = null)
        {
            toLocal ??= t => t.ToLocalTime();
            today = today.Date;
            var local = toLocal;
            var recent = rides.Where(r => local(r.StartUtc).Date > today.AddDays(-InactiveDays)).ToList();
            if (recent.Count == 0) return new WorkoutSuggestion(Pick(templates, rides, WorkoutCategory.Endurance), SuggestionReason.GettingStarted, 0);

            double form = TrainingLoad.FormOn(rides, today, toLocal);
            bool hardLately = recent.Any(r => local(r.StartUtc).Date >= today.AddDays(-1) && r.intensityFactor >= HardRideIf);

            if (form < TiredBelow) return new WorkoutSuggestion(Pick(templates, rides, WorkoutCategory.Recovery), SuggestionReason.Tired, form);
            if (form < RecoveringBelow || hardLately) return new WorkoutSuggestion(Pick(templates, rides, WorkoutCategory.Endurance), SuggestionReason.Recovering, form);
            if (form <= FreshAbove) return new WorkoutSuggestion(Pick(templates, rides, BalancedCategories), SuggestionReason.Balanced, form);
            return new WorkoutSuggestion(Pick(templates, rides, FreshCategories), SuggestionReason.Fresh, form);
        }

        /// <summary>
        /// Workouts ridden last (newest first, each once), without FTP tests and <paramref name="exclude"/>.
        /// <paramref name="resolve"/> finds a workout by id (built-in or own); deleted ones are skipped.
        /// </summary>
        public static List<(Workout Workout, DateTime LastUtc)> Recent(IEnumerable<RideRecord> rides, Func<string, Workout?> resolve, int count, ICollection<string>? exclude = null)
        {
            var result = new List<(Workout, DateTime)>();
            var seen = new HashSet<string>(exclude ?? Array.Empty<string>());
            foreach (var r in rides.OrderByDescending(r => r.startUtc))
            {
                if (string.IsNullOrEmpty(r.workoutId) || !seen.Add(r.workoutId)) continue;
                var w = resolve(r.workoutId);
                if (w == null || w.Category == WorkoutCategory.FtpTest) continue;
                result.Add((w, r.StartUtc));
                if (result.Count == count) break;
            }

            return result;
        }

        /// <summary>The workout of these categories ridden longest ago (never ridden first; list order breaks ties).</summary>
        private static Workout Pick(IReadOnlyList<Workout> templates, IReadOnlyList<RideRecord> rides, params WorkoutCategory[] categories)
        {
            var last = new Dictionary<string, long>();
            foreach (var r in rides)
            {
                if (string.IsNullOrEmpty(r.workoutId)) continue;
                last[r.workoutId] = Math.Max(last.TryGetValue(r.workoutId, out long t) ? t : long.MinValue, r.startUtc);
            }

            var candidates = templates
                .Where(w => categories.Contains(w.Category))
                .OrderBy(w => Array.IndexOf(categories, w.Category))
                .ToList();
            if (candidates.Count == 0) return templates[0];
            return candidates
                .Select((w, i) => (w, i, t: last.TryGetValue(w.Id, out long t) ? t : long.MinValue))
                .OrderBy(x => x.t)
                .ThenBy(x => x.i)
                .First().w;
        }
    }
}
