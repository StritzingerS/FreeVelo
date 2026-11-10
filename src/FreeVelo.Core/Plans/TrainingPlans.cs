using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FreeVelo.Core.History;
using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Plans
{
    public enum SessionKind
    {
        /// <summary>The hard session of the plan's focus (intervals).</summary>
        Key = 0,
        Endurance = 1,
        Recovery = 2,

        /// <summary>FTP test (end of a build block).</summary>
        Test = 3,
    }

    public enum SessionStatus
    {
        Planned = 0,
        Done = 1,
        Missed = 2,
    }

    /// <summary>One planned workout on a day. Public fields for Unity's JsonUtility.</summary>
    [Serializable]
    public class PlannedSession
    {
        /// <summary>Local date, yyyy-MM-dd.</summary>
        public string date = "";
        public string workoutId = "";

        /// <summary><see cref="SessionKind"/> as int.</summary>
        public int kind;

        /// <summary><see cref="SessionStatus"/> as int.</summary>
        public int status;

        /// <summary>Intensity change in percent (−3 after a hard time, +2 after strong sessions).</summary>
        public int adjustPercent;

        /// <summary>The ride that completed it.</summary>
        public string rideId = "";

        /// <summary>Why the plan changed this session (adaptation), as a key for the app's texts.</summary>
        public string note = "";

        public int week;

        /// <summary>Done at more than 100 % intensity (for rule 3).</summary>
        public bool strong;

        public SessionKind Kind
        {
            get => (SessionKind)kind;
            set => kind = (int)value;
        }

        public SessionStatus Status
        {
            get => (SessionStatus)status;
            set => status = (int)value;
        }

        public DateTime Date
        {
            get => DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            set => date = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>Intensity factor to start the workout with (1.0 = as written).</summary>
        public double Intensity => 1 + (adjustPercent / 100.0);
    }

    /// <summary>A running training plan (stored on the phone).</summary>
    [Serializable]
    public class TrainingPlan
    {
        public string templateId = "";

        /// <summary>Monday of the first week, yyyy-MM-dd.</summary>
        public string start = "";
        public int weeks;

        /// <summary>Training days as <see cref="DayOfWeek"/> values.</summary>
        public int[] weekdays = new int[0];
        public List<PlannedSession> sessions = new List<PlannedSession>();

        /// <summary>Weeks (index) whose key sessions already got the +2 % (rule 3).</summary>
        public List<int> boostedWeeks = new List<int>();

        public DateTime Start => DateTime.ParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        public DateTime End => Start.AddDays(7 * weeks - 1);

        public IEnumerable<PlannedSession> On(DateTime day)
        {
            string d = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return sessions.Where(s => s.date == d);
        }

        /// <summary>The next session still to ride, from <paramref name="today"/> on.</summary>
        public PlannedSession? Next(DateTime today) =>
            sessions.Where(s => s.Status == SessionStatus.Planned && s.Date >= today.Date).OrderBy(s => s.date).FirstOrDefault();

        public bool IsFinished(DateTime today) => today.Date > End;
    }

    /// <summary>A plan template (Base, FTP Build, VO2max, Maintenance).</summary>
    public sealed class PlanTemplate
    {
        public PlanTemplate(string id, LocalizedText name, LocalizedText description, int defaultWeeks, int minWeeks, int maxWeeks,
            string[] keyWorkouts, bool endsWithTest)
        {
            Id = id;
            Name = name;
            Description = description;
            DefaultWeeks = defaultWeeks;
            MinWeeks = minWeeks;
            MaxWeeks = maxWeeks;
            KeyWorkouts = keyWorkouts;
            EndsWithTest = endsWithTest;
        }

        public string Id { get; }
        public LocalizedText Name { get; }
        public LocalizedText Description { get; }
        public int DefaultWeeks { get; }
        public int MinWeeks { get; }
        public int MaxWeeks { get; }

        /// <summary>Key sessions, used in turn.</summary>
        public string[] KeyWorkouts { get; }

        /// <summary>Build block: FTP test at the end (rule 6).</summary>
        public bool EndsWithTest { get; }
    }

    /// <summary>Templates and plan generation (deterministic).</summary>
    public static class PlanTemplates
    {
        public const string Recovery = "recovery-40";
        public const string Endurance = "endurance-60";
        public const string LongEndurance = "endurance-90";
        public const string RampTest = "ftp-ramp";
        public const string Openers = "openers-45";
        public const int MinSessions = 3;

        /// <summary>Endurance rides in recovery weeks: 10 % easier, below ~62 % FTP.</summary>
        public const int RecoveryWeekPercent = -10;
        public const int MaxSessions = 5;

        public static readonly IReadOnlyList<PlanTemplate> All = new[]
        {
            new PlanTemplate("base", new LocalizedText("Base", "Grundlage"),
                new LocalizedText("Aerobic base: endurance rides with tempo and sweet spot.", "Aerobe Grundlage: Ausdauerfahrten mit Tempo und Sweet Spot."),
                8, 4, 12, new[] { "tempo-2x20", "sweetspot-3x15" }, false),
            new PlanTemplate("ftp-build", new LocalizedText("FTP Build", "FTP-Aufbau"),
                new LocalizedText("Raise your FTP: sweet spot, threshold, crisscross and over-unders, FTP test at the end.", "FTP steigern: Sweet Spot, Schwelle, Crisscross und Over-Unders, FTP-Test am Ende."),
                8, 4, 12, new[] { "sweetspot-3x15", "threshold-2x20", "sweetspot-crisscross", "over-unders-3x10", "sweetspot-2x20" }, true),
            new PlanTemplate("vo2max", new LocalizedText("VO2max Block", "VO2max-Block"),
                new LocalizedText("Short, hard intervals for top-end power, FTP test at the end.", "Kurze, harte Intervalle für die Spitzenleistung, FTP-Test am Ende."),
                6, 4, 8, new[] { "vo2max-4x4", "vo2max-5x5", "40-20s", "micro-bursts" }, true),
            new PlanTemplate("maintenance", new LocalizedText("Maintenance", "Erhaltung"),
                new LocalizedText("Keep your fitness with little time: sweet spot, sprints or micro-bursts, and endurance.", "Form halten mit wenig Zeit: Sweet Spot, Sprints oder Micro-Bursts und Grundlage."),
                4, 4, 12, new[] { "sweetspot-3x15", "sprints-8x15", "sweetspot-crisscross", "micro-bursts" }, false),
        };

        public static PlanTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);

        /// <summary>Session kinds of one normal week for 3–5 training days (sorted Monday → Sunday).</summary>
        public static SessionKind[] WeekPattern(int days) => days switch
        {
            3 => new[] { SessionKind.Key, SessionKind.Endurance, SessionKind.Key },
            4 => new[] { SessionKind.Key, SessionKind.Endurance, SessionKind.Key, SessionKind.Endurance },
            _ => new[] { SessionKind.Key, SessionKind.Endurance, SessionKind.Key, SessionKind.Recovery, SessionKind.Endurance },
        };

        /// <summary>
        /// Every 4th week is a recovery week (rule 5) – but not the last two weeks: the last week is the taper (or the test
        /// week), and two easy weeks in a row would lose fitness.
        /// </summary>
        public static bool IsRecoveryWeek(PlanTemplate t, int week, int weeks) => (week + 1) % 4 == 0 && week < weeks - 2;

        /// <summary>
        /// The plan's last week: less volume, a little intensity (Allen &amp; Coggan) so that the plan ends fit and fresh
        /// (form / TSB about +5 to +15). In plans that end with an FTP test it is the test week.
        /// </summary>
        public static bool IsTaperWeek(int week, int weeks) => week == weeks - 1;

        /// <summary>Monday-first order of the training days.</summary>
        public static List<DayOfWeek> Sorted(IEnumerable<DayOfWeek> days) => days.Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();

        /// <summary>Creates a plan starting in the week of <paramref name="today"/>; days already over this week stay empty.</summary>
        public static TrainingPlan Create(PlanTemplate template, int weeks, IEnumerable<DayOfWeek> weekdays, DateTime today)
        {
            var days = Sorted(weekdays);
            if (days.Count < MinSessions || days.Count > MaxSessions) throw new ArgumentException($"{MinSessions}–{MaxSessions} training days");
            weeks = Math.Max(template.MinWeeks, Math.Min(template.MaxWeeks, weeks));
            var plan = new TrainingPlan
            {
                templateId = template.Id,
                start = TrainingLoad.Monday(today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                weeks = weeks,
                weekdays = days.Select(d => (int)d).ToArray(),
            };
            plan.sessions.AddRange(Generate(template, plan, 0, today.Date));
            return plan;
        }

        /// <summary>Sessions from week <paramref name="fromWeek"/> on, only on days not before <paramref name="notBefore"/>.</summary>
        internal static List<PlannedSession> Generate(PlanTemplate template, TrainingPlan plan, int fromWeek, DateTime notBefore)
        {
            var days = Sorted(plan.weekdays.Select(d => (DayOfWeek)d));
            var pattern = WeekPattern(days.Count);
            int keysPerWeek = pattern.Count(k => k == SessionKind.Key);
            var result = new List<PlannedSession>();
            for (int week = fromWeek; week < plan.weeks; week++)
            {
                var monday = plan.Start.AddDays(7 * week);
                bool recoveryWeek = IsRecoveryWeek(template, week, plan.weeks);
                bool taperWeek = IsTaperWeek(week, plan.weeks);
                bool testWeek = template.EndsWithTest && week == plan.weeks - 1;
                int keyIndex = week * keysPerWeek;
                for (int i = 0; i < days.Count; i++)
                {
                    var kind = pattern[i];
                    string workout;
                    if (kind == SessionKind.Key)
                    {
                        workout = template.KeyWorkouts[keyIndex++ % template.KeyWorkouts.Length];
                    }
                    else if (kind == SessionKind.Endurance)
                    {
                        workout = i == days.Count - 1 ? LongEndurance : Endurance;
                    }
                    else
                    {
                        workout = Recovery;
                    }

                    int adjust = 0;
                    if (recoveryWeek)
                    {
                        // About −40 % volume: hard sessions become short endurance, endurance becomes recovery. Easy
                        // weeks stay below ~62 % FTP (Allen & Coggan): the endurance rides 10 % easier (68 → ~61 %).
                        if (kind == SessionKind.Key) (kind, workout, adjust) = (SessionKind.Endurance, Endurance, RecoveryWeekPercent);
                        else (kind, workout) = (SessionKind.Recovery, Recovery);
                    }

                    if (taperWeek)
                    {
                        // Taper: hard sessions become short openers, endurance rides 10 % easier and the last one shorter.
                        if (kind == SessionKind.Key) (kind, workout) = (SessionKind.Endurance, Openers);
                        else if (kind == SessionKind.Endurance) (workout, adjust) = (Endurance, RecoveryWeekPercent);
                    }

                    if (testWeek && i == days.Count - 1) (kind, workout, adjust) = (SessionKind.Test, RampTest, 0);
                    else if (testWeek && i == days.Count - 2) (kind, workout, adjust) = (SessionKind.Recovery, Recovery, 0); // fresh for the test

                    var date = monday.AddDays(((int)days[i] + 6) % 7);
                    if (date < notBefore) continue;
                    result.Add(new PlannedSession { Date = date, workoutId = workout, Kind = kind, week = week, adjustPercent = adjust });
                }
            }

            return result;
        }
    }

    /// <summary>The adaptation rules (concept section 7), applied whenever the app opens or a ride ends.</summary>
    public static class PlanAdapter
    {
        public const double FormLimit = -30;
        public const double CompletedShare = 0.9;
        public const double ReducedIntensity = 0.9;
        public const int EasierPercent = -3;
        public const int HarderPercent = 2;

        // Notes shown in the app (texts there)
        public const string NoteMoved = "moved";
        public const string NoteEasier = "easier";
        public const string NoteHarder = "harder";
        public const string NoteRecovery = "recovery_inserted";
        public const string NoteRamp = "ramp_recovery";

        /// <summary>Fitness (CTL) rising faster than this (TSS per day, per week) for <see cref="RampWeeks"/> weeks → recovery.</summary>
        public const double MaxRampPerWeek = 7;

        /// <summary>Weeks in a row above <see cref="MaxRampPerWeek"/> (Allen &amp; Coggan: more than 4 risks overreaching).</summary>
        public const int RampWeeks = 3;

        /// <summary>
        /// A ride ended: if it was the planned workout of that day (or a planned one of the last days not yet done), mark
        /// it done and apply rules 2 and 3. Group and free rides only count as load (CTL/ATL), not as a session.
        /// Returns the session it completed.
        /// </summary>
        public static PlannedSession? RecordRide(TrainingPlan plan, RideRecord ride, Func<DateTime, DateTime>? toLocal = null)
        {
            toLocal ??= t => t.ToLocalTime();
            if (string.IsNullOrEmpty(ride.workoutId)) return null;
            var day = toLocal(ride.StartUtc).Date;
            var session = plan.sessions
                .Where(s => s.Status == SessionStatus.Planned && s.workoutId == ride.workoutId && s.Date <= day && s.Date >= day.AddDays(-2))
                .OrderByDescending(s => s.date)
                .FirstOrDefault();
            if (session == null) return null;

            session.Status = SessionStatus.Done;
            session.rideId = ride.id;
            session.strong = ride.workoutIntensity > 1.0 && ride.workoutCompleted >= CompletedShare;

            // Rule 2: aborted early or ridden clearly easier → the next session of this kind 3 % easier.
            if (ride.workoutCompleted < CompletedShare || ride.workoutIntensity < ReducedIntensity)
            {
                var next = plan.sessions.Where(s => s.Status == SessionStatus.Planned && s.Kind == session.Kind && s.Date > session.Date)
                    .OrderBy(s => s.date).FirstOrDefault();
                if (next != null)
                {
                    next.adjustPercent += EasierPercent;
                    next.note = NoteEasier;
                }
            }

            // Rule 3: two key sessions of a week ridden stronger than planned → next week's key sessions +2 %.
            if (session.Kind == SessionKind.Key && !plan.boostedWeeks.Contains(session.week))
            {
                int strong = plan.sessions.Count(s => s.week == session.week && s.Kind == SessionKind.Key && s.Status == SessionStatus.Done && s.strong);
                if (strong >= 2)
                {
                    plan.boostedWeeks.Add(session.week);
                    foreach (var s in plan.sessions.Where(s => s.week == session.week + 1 && s.Kind == SessionKind.Key && s.Status == SessionStatus.Planned))
                    {
                        s.adjustPercent += HarderPercent;
                        s.note = NoteHarder;
                    }
                }
            }

            return session;
        }

        /// <summary>
        /// Rules 1 and 4 for <paramref name="today"/>: missed sessions (key sessions move to a free day of the same week,
        /// others are dropped), and a recovery day when the form is very low. Returns true if the plan changed.
        /// </summary>
        public static bool Update(TrainingPlan plan, DateTime today, double formToday) => Update(plan, today, formToday, null);

        /// <summary>
        /// Same, plus rule 8: <paramref name="weeklyRamps"/> (CTL change per week, newest first, see
        /// <see cref="TrainingLoad.WeeklyRamps"/>) above <see cref="MaxRampPerWeek"/> for <see cref="RampWeeks"/> weeks
        /// → the next 7 days become a recovery week.
        /// </summary>
        public static bool Update(TrainingPlan plan, DateTime today, double formToday, IReadOnlyList<double>? weeklyRamps)
        {
            today = today.Date;
            bool changed = false;
            foreach (var s in plan.sessions.Where(s => s.Status == SessionStatus.Planned && s.Date < today).OrderBy(s => s.date).ToList())
            {
                changed = true;
                if (s.Kind == SessionKind.Key || s.Kind == SessionKind.Test)
                {
                    var sunday = TrainingLoad.Monday(s.Date).AddDays(6);
                    var free = Enumerable.Range(0, 7).Select(i => today.AddDays(i)).Where(d => d <= sunday && !plan.On(d).Any()).FirstOrDefault();
                    if (free != default)
                    {
                        s.Date = free;
                        s.note = NoteMoved;
                        continue;
                    }
                }

                s.Status = SessionStatus.Missed;
            }

            // Rule 8: fitness rising too fast for weeks → recovery week now (unless an easy week is already coming).
            if (weeklyRamps != null && weeklyRamps.Count >= RampWeeks && weeklyRamps.Take(RampWeeks).All(r => r > MaxRampPerWeek))
            {
                var coming = plan.sessions.Where(s => s.Status == SessionStatus.Planned && s.Date >= today && s.Date < today.AddDays(7)).ToList();
                bool alreadyEasy = coming.All(s => s.Kind != SessionKind.Key) || coming.Any(s => s.note == NoteRamp);
                if (!alreadyEasy)
                {
                    foreach (var s in coming.Where(s => s.Kind != SessionKind.Test))
                    {
                        if (s.Kind == SessionKind.Key) (s.Kind, s.workoutId, s.adjustPercent) = (SessionKind.Endurance, PlanTemplates.Endurance, PlanTemplates.RecoveryWeekPercent);
                        else (s.Kind, s.workoutId, s.adjustPercent) = (SessionKind.Recovery, PlanTemplates.Recovery, 0);
                        s.note = NoteRamp;
                    }

                    changed = true;
                }
            }

            // Rule 4: very tired → the next hard session today/tomorrow becomes a recovery ride.
            if (formToday < FormLimit)
            {
                var next = plan.Next(today);
                if (next != null && next.Date <= today.AddDays(1) && next.Kind != SessionKind.Recovery && next.note != NoteRecovery)
                {
                    next.Kind = SessionKind.Recovery;
                    next.workoutId = PlanTemplates.Recovery;
                    next.adjustPercent = 0;
                    next.note = NoteRecovery;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>Rule 7: other training days → the plan is regenerated from today (done sessions stay).</summary>
        public static void ChangeWeekdays(TrainingPlan plan, IEnumerable<DayOfWeek> weekdays, DateTime today)
        {
            var template = PlanTemplates.Find(plan.templateId) ?? throw new InvalidOperationException("Unknown plan template");
            var days = PlanTemplates.Sorted(weekdays);
            if (days.Count < PlanTemplates.MinSessions || days.Count > PlanTemplates.MaxSessions) throw new ArgumentException("3–5 training days");
            plan.weekdays = days.Select(d => (int)d).ToArray();
            int week = Math.Max(0, (int)((TrainingLoad.Monday(today) - plan.Start).TotalDays / 7));
            plan.sessions.RemoveAll(s => s.Status == SessionStatus.Planned && s.Date >= today.Date);
            plan.sessions.AddRange(PlanTemplates.Generate(template, plan, week, today.Date)
                .Where(n => !plan.sessions.Any(o => o.date == n.date)));
            plan.sessions.Sort((a, b) => string.CompareOrdinal(a.date, b.date));
        }
    }
}
