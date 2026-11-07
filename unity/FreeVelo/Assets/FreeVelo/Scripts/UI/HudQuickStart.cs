using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.App;
using FreeVelo.Core.Training;
using FreeVelo.Core.Ui;
using FreeVelo.Core.Workouts;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>
    /// Quick start on the home page: first the training plan's next session or, without a plan, a workout that suits
    /// the rider's form; then the two workouts ridden last (filled up with classics while there are fewer).
    /// </summary>
    public sealed partial class Hud
    {
        private sealed class QuickCard
        {
            public Workout Workout;
            public string Overline;
            public bool Highlight;
            public double Intensity = 1.0;
            public string Note = "";
        }

        /// <summary>Filled up with these while fewer workouts have been ridden.</summary>
        private static readonly string[] ClassicWorkouts = { "sweetspot-3x15", "endurance-60", "vo2max-4x4", "tempo-2x20" };

        private List<QuickCard> _quickCards;
        private float _quickCardsAt = -10;

        /// <summary>The three cards (worked out at most once a second: the form looks at the whole history).</summary>
        private List<QuickCard> QuickStartCards()
        {
            if (_quickCards != null && Time.unscaledTime - _quickCardsAt < 1f) return _quickCards;
            _quickCardsAt = Time.unscaledTime;
            var cards = new List<QuickCard>();
            var today = DateTime.Now.Date;
            var templates = WorkoutStore.Library.Templates;

            var planNext = PlanStore.Active ? PlanStore.Plan.Next(today) : null;
            var planWorkout = PlanStore.WorkoutOf(planNext);
            if (planWorkout != null)
            {
                cards.Add(new QuickCard
                {
                    Workout = planWorkout,
                    Overline = L.Get("plan") + " · " + (planNext.Date == today ? L.Get("today") : DayLabel(planNext.Date)),
                    Highlight = true,
                    Intensity = planNext.Intensity,
                    Note = planNext.adjustPercent != 0 ? $" · {planNext.adjustPercent:+0;-0} %" : "",
                });
            }
            else if (templates.Count > 0)
            {
                var suggestion = QuickStart.Suggest(HistoryStore.Rides, today, templates);
                cards.Add(new QuickCard
                {
                    Workout = suggestion.Workout,
                    Overline = L.Get("suggestion") + " · " + L.Get("reason_" + suggestion.Reason.ToString().ToLowerInvariant()),
                    Highlight = true,
                });
            }

            Workout Resolve(string id) => templates.FirstOrDefault(w => w.Id == id) ?? WorkoutStore.Library.Find(id);
            var shown = cards.Select(k => k.Workout.Id).ToList();
            foreach (var (workout, lastUtc) in QuickStart.Recent(HistoryStore.Rides, Resolve, 3 - cards.Count, shown))
            {
                cards.Add(new QuickCard { Workout = workout, Overline = L.Get("last_ridden") + " · " + DaysAgo(lastUtc.ToLocalTime().Date, today) });
                shown.Add(workout.Id);
            }

            foreach (string id in ClassicWorkouts)
            {
                if (cards.Count >= 3) break;
                var w = Resolve(id);
                if (w == null || shown.Contains(id)) continue;
                cards.Add(new QuickCard { Workout = w, Overline = L.Get("classic") });
                shown.Add(id);
            }

            _quickCards = cards;
            return cards;
        }

        private static string DaysAgo(DateTime day, DateTime today)
        {
            int days = (int)(today - day).TotalDays;
            return days <= 0 ? L.Get("today") : days == 1 ? L.Get("yesterday") : string.Format(L.Get("days_ago"), days);
        }

        private void DrawQuickCard(Rect r, QuickCard card)
        {
            var w = card.Workout;
            if (card.Highlight)
            {
                Ui.Card(r, Color.Lerp(Theme.Surface, Theme.Accent, 0.12f));
                Ui.Outline(r, Theme.Alpha(Theme.Accent, 0.8f), 2, Theme.Radius);
            }
            else
            {
                Ui.Card(r);
            }

            GUI.Label(new Rect(r.x + 16, r.y + 12, r.width - 32, 18), card.Overline.ToUpperInvariant(), Clip(Theme.With(Theme.Overline, card.Highlight ? Theme.Accent : Theme.Muted)));
            GUI.Label(new Rect(r.x + 16, r.y + 32, r.width - 32, 26), w.Name.Get(AppSettings.Language), Clip(Theme.With(Theme.H3, size: 17)));
            GUI.Label(new Rect(r.x + 16, r.y + 58, r.width - 32, 20), Meta(w) + card.Note, Theme.Caption);
            WorkoutProfile(new Rect(r.x + 16, r.y + 84, r.width - 86, 50), w, false);
            if (Ui.IconButton(new Rect(r.xMax - 60, r.yMax - 60, 46, 46), Icon.Play, ButtonKind.Primary)) _ride.StartRide(w, card.Intensity);
        }
    }
}
