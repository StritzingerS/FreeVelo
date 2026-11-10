using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeVelo.Core.History;
using FreeVelo.Core.Plans;
using FreeVelo.Core.Training;
using FreeVelo.Core.Workouts;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>The running training plan (persistentDataPath/plan.json) and the adaptation when the app opens or a ride ends.</summary>
    public static class PlanStore
    {
        private static TrainingPlan _plan;
        private static bool _loaded;
        private static DateTime _updatedFor;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "plan.json");

        public static TrainingPlan Plan
        {
            get
            {
                if (!_loaded) Load();
                return _plan;
            }
        }

        public static bool Active => Plan != null && !Plan.IsFinished(DateTime.Now);

        private static void Load()
        {
            _loaded = true;
            try
            {
                _plan = File.Exists(FilePath) ? JsonUtility.FromJson<TrainingPlan>(File.ReadAllText(FilePath)) : null;
                if (_plan != null && string.IsNullOrEmpty(_plan.templateId)) _plan = null;
                if (_plan != null)
                {
                    _plan.sessions ??= new List<PlannedSession>();
                    _plan.boostedWeeks ??= new List<int>();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _plan = null;
            }
        }

        public static void Save()
        {
            try
            {
                if (_plan == null)
                {
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                }
                else
                {
                    File.WriteAllText(FilePath, JsonUtility.ToJson(_plan));
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static void Start(PlanTemplate template, int weeks, IEnumerable<DayOfWeek> days)
        {
            _plan = PlanTemplates.Create(template, weeks, days, DateTime.Now);
            _loaded = true;
            _updatedFor = default;
            Save();
        }

        public static void Stop()
        {
            _plan = null;
            _loaded = true;
            Save();
        }

        public static void ChangeDays(IEnumerable<DayOfWeek> days)
        {
            if (Plan == null) return;
            PlanAdapter.ChangeWeekdays(Plan, days, DateTime.Now);
            Save();
        }

        /// <summary>Rules 1 and 4 once per day (missed sessions, recovery when the form is very low).</summary>
        public static void UpdateForToday()
        {
            if (Plan == null) return;
            var today = DateTime.Now.Date;
            if (_updatedFor == today) return;
            _updatedFor = today;
            double form = TrainingLoad.FormOn(HistoryStore.Rides, today);
            var ramps = TrainingLoad.WeeklyRamps(HistoryStore.Rides, today);
            if (PlanAdapter.Update(Plan, today, form, ramps)) Save();
        }

        /// <summary>A ride ended: completes the planned session if it was that workout. Returns the session.</summary>
        public static PlannedSession RecordRide(RideRecord ride)
        {
            if (Plan == null) return null;
            var session = PlanAdapter.RecordRide(Plan, ride);
            if (session != null) Save();
            return session;
        }

        /// <summary>Today's session (planned or done), else null.</summary>
        public static PlannedSession Today => Plan?.On(DateTime.Now.Date).FirstOrDefault();

        /// <summary>The workout of a session (built-in, own, or FTP test).</summary>
        public static Workout WorkoutOf(PlannedSession session)
        {
            if (session == null) return null;
            // Built-ins as the rider edited them (Templates holds the edited versions)
            return WorkoutStore.Library.Templates.FirstOrDefault(w => w.Id == session.workoutId)
                ?? BuiltInWorkouts.Find(session.workoutId)
                ?? (session.workoutId == "ftp-ramp" ? FtpTests.Create(FtpTestKind.Ramp)
                    : session.workoutId == "ftp-20min" ? FtpTests.Create(FtpTestKind.TwentyMinute)
                    : session.workoutId == "ftp-2x8" ? FtpTests.Create(FtpTestKind.TwoByEight) : null);
        }

        public static void Replace(TrainingPlan plan)
        {
            _plan = plan != null && !string.IsNullOrEmpty(plan.templateId) ? plan : null;
            _loaded = true;
            _updatedFor = default;
            Save();
        }
    }
}
