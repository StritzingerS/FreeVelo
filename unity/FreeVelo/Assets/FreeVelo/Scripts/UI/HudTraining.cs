using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.App;
using FreeVelo.Core.History;
using FreeVelo.Core.Plans;
using FreeVelo.Core.Ui;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>Training: the training plan (choose, follow, adapt) and the history (fitness, volume, power curve, FTP, rides).</summary>
    public sealed partial class Hud
    {
        private int _trainingTab; // 0 = plan, 1 = history
        private string _planTemplate = "ftp-build";
        private int _planWeeks = 8;
        private readonly HashSet<DayOfWeek> _planDays = new HashSet<DayOfWeek> { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday };
        private bool _editingDays;
        private bool _confirmStopPlan;

        private static readonly Color CtlColor = Theme.Info;
        private static readonly Color AtlColor = new Color32(0xE8, 0x5D, 0x9A, 0xFF);

        private static readonly DayOfWeek[] MondayFirst =
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
        };

        private static string DayShort(DayOfWeek d) => AppSettings.Language == "de"
            ? new[] { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" }[(int)d]
            : new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }[(int)d];

        private static Color KindColor(SessionKind kind) => kind switch
        {
            SessionKind.Key => Theme.Accent,
            SessionKind.Endurance => Theme.Info,
            SessionKind.Test => Theme.Danger,
            _ => Theme.Success,
        };

        private static string KindName(SessionKind kind) => L.Get("kind_" + kind.ToString().ToLowerInvariant());

        private void DrawTraining(Rect c)
        {
            Header(c, L.Get("training"));
            _trainingTab = Ui.Segmented(new Rect(c.xMax - 320, c.y, 320, 46), new[] { L.Get("plan"), L.Get("history") }, _trainingTab);
            var body = new Rect(c.x, c.y + 66, c.width, c.height - 66);
            if (_trainingTab == 0)
            {
                if (PlanStore.Plan == null || PlanStore.Plan.IsFinished(DateTime.Now)) DrawPlanChooser(body);
                else DrawPlan(body);
            }
            else
            {
                DrawHistory(body);
            }
        }

        // ------------------------------------------------------------------ plan: choose

        private void DrawPlanChooser(Rect c)
        {
            var view = new Rect(c.x - 8, c.y, c.width + 16, c.height);
            float contentH = 2 * 130 + 16 + 24 + 170 + 24;
            Ui.BeginScroll(3100, view, contentH);
            float x0 = 8, w = c.width;

            if (PlanStore.Plan != null)
            {
                GUI.Label(new Rect(x0, 0, w, 20), L.Get("plan_finished"), Theme.With(Theme.Caption, Theme.Success));
            }

            // Templates 2 × 2
            float cw = (w - 16) / 2f;
            for (int i = 0; i < PlanTemplates.All.Count; i++)
            {
                var t = PlanTemplates.All[i];
                var r = new Rect(x0 + (i % 2) * (cw + 16), 22 + (i / 2) * 146, cw, 130);
                bool selected = t.Id == _planTemplate;
                Ui.Card(r, selected ? Color.Lerp(Theme.Surface, Theme.Accent, 0.12f) : Theme.Surface);
                if (selected) Ui.Outline(r, Theme.Accent, 2, Theme.Radius);
                GUI.Label(new Rect(r.x + 20, r.y + 16, r.width - 40, 28), t.Name.Get(AppSettings.Language), Theme.With(Theme.H3, size: 19));
                GUI.Label(new Rect(r.x + 20, r.y + 46, r.width - 40, 48), t.Description.Get(AppSettings.Language), Theme.With(Theme.Caption, wrap: true));
                GUI.Label(new Rect(r.x + 20, r.yMax - 32, r.width - 40, 20), $"{t.MinWeeks}–{t.MaxWeeks} {L.Get("weeks")}", Theme.With(Theme.Caption, Theme.Muted));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    _planTemplate = t.Id;
                    _planWeeks = Mathf.Clamp(_planWeeks, t.MinWeeks, t.MaxWeeks);
                }
            }

            var template = PlanTemplates.Find(_planTemplate) ?? PlanTemplates.All[0];
            var setup = new Rect(x0, 22 + 2 * 146 + 8, w, 170);
            Ui.Card(setup);
            GUI.Label(new Rect(setup.x + 24, setup.y + 18, 200, 20), L.Get("weeks").ToUpperInvariant(), Theme.Overline);
            _planWeeks = Ui.Stepper(new Rect(setup.x + 24, setup.y + 42, 230, 52), _planWeeks, null, 1, 1, template.MinWeeks, template.MaxWeeks);
            GUI.Label(new Rect(setup.x + 290, setup.y + 18, 400, 20), L.Get("training_days").ToUpperInvariant(), Theme.Overline);
            DayToggles(new Rect(setup.x + 290, setup.y + 42, setup.width - 290 - 24, 52), _planDays);

            int n = _planDays.Count;
            bool valid = n >= PlanTemplates.MinSessions && n <= PlanTemplates.MaxSessions;
            GUI.Label(new Rect(setup.x + 24, setup.y + 110, setup.width - 260, 44),
                valid ? string.Format(L.Get("plan_summary"), _planWeeks, n) : L.Get("plan_days_hint"),
                Theme.With(Theme.Caption, valid ? Theme.Muted : Theme.Warning, wrap: true));
            if (Ui.Button(new Rect(setup.xMax - 220, setup.yMax - 70, 196, 52), L.Get("plan_start"), ButtonKind.Primary, Icon.Play, valid))
            {
                PlanStore.Start(template, _planWeeks, _planDays);
                PlanStore.UpdateForToday();
            }

            Ui.EndScroll(3100, view, contentH);
        }

        /// <summary>Seven day chips Monday → Sunday that toggle the set.</summary>
        private static void DayToggles(Rect r, HashSet<DayOfWeek> days)
        {
            float bw = (r.width - 6 * 8) / 7f;
            for (int i = 0; i < 7; i++)
            {
                var d = MondayFirst[i];
                var b = new Rect(r.x + i * (bw + 8), r.y, bw, r.height);
                bool on = days.Contains(d);
                Ui.Rect(b, on ? Theme.Accent : Theme.Surface2, Theme.RadiusSmall);
                GUI.Label(b, DayShort(d), Theme.With(Theme.Body, on ? Color.white : Theme.Muted, 16, TextAnchor.MiddleCenter));
                if (GUI.Button(b, GUIContent.none, GUIStyle.none))
                {
                    if (on) days.Remove(d);
                    else days.Add(d);
                }
            }
        }

        // ------------------------------------------------------------------ plan: running

        private void DrawPlan(Rect c)
        {
            var plan = PlanStore.Plan;
            var template = PlanTemplates.Find(plan.templateId);
            var today = DateTime.Now.Date;
            int week = Mathf.Clamp((int)((today - plan.Start).TotalDays / 7), 0, plan.weeks - 1);

            var view = new Rect(c.x - 8, c.y, c.width + 16, c.height);
            float contentH = 150 + 16 + 2 * 112 + 24 + (_editingDays ? 150 : 70);
            Ui.BeginScroll(3101, view, contentH);
            float x0 = 8, w = c.width;

            // Header card: plan, week, next session
            var head = new Rect(x0, 0, w, 150);
            Ui.Card(head);
            GUI.Label(new Rect(head.x + 24, head.y + 18, 400, 20), (template?.Name.Get(AppSettings.Language) ?? plan.templateId).ToUpperInvariant(), Theme.Overline);
            GUI.Label(new Rect(head.x + 24, head.y + 40, 400, 30), string.Format(L.Get("plan_week"), week + 1, plan.weeks), Theme.H2);
            int done = plan.sessions.Count(s => s.Status == SessionStatus.Done);
            int total = plan.sessions.Count;
            var bar = new Rect(head.x + 24, head.y + 84, 300, 8);
            Ui.Rect(bar, Theme.Surface2, 4);
            Ui.Rect(new Rect(bar.x, bar.y, bar.width * done / Mathf.Max(1, total), bar.height), Theme.Success, 4);
            GUI.Label(new Rect(head.x + 24, head.y + 100, 300, 22), string.Format(L.Get("plan_progress"), done, total), Theme.Caption);

            var next = plan.Next(today);
            if (next != null)
            {
                var nr = new Rect(head.x + 360, head.y + 16, head.width - 360 - 16, head.height - 32);
                Ui.Rect(nr, Theme.Surface2, Theme.RadiusSmall);
                var workout = PlanStore.WorkoutOf(next);
                bool isToday = next.Date == today;
                GUI.Label(new Rect(nr.x + 18, nr.y + 12, nr.width - 200, 18), (isToday ? L.Get("today") : DayLabel(next.Date)).ToUpperInvariant(), Theme.With(Theme.Overline, isToday ? Theme.Accent : Theme.Muted));
                GUI.Label(new Rect(nr.x + 18, nr.y + 32, nr.width - 200, 30), workout?.Name.Get(AppSettings.Language) ?? next.workoutId, Clip(Theme.With(Theme.H3, size: 20)));
                float chipX = nr.x + 18;
                chipX += Ui.Chip(new Rect(chipX, nr.y + 70, 0, 26), KindName(next.Kind), KindColor(next.Kind)) + 6;
                if (next.adjustPercent != 0) chipX += Ui.Chip(new Rect(chipX, nr.y + 70, 0, 26), $"{next.adjustPercent:+0;-0} %", next.adjustPercent > 0 ? Theme.Warning : Theme.Info) + 6;
                if (next.note.Length > 0) Ui.Chip(new Rect(chipX, nr.y + 70, 0, 26), L.Get("note_" + next.note), Theme.Muted);
                if (workout != null && Ui.Button(new Rect(nr.xMax - 168, nr.center.y - 26, 150, 52), L.Get("start"), isToday ? ButtonKind.Primary : ButtonKind.Secondary, Icon.Play))
                {
                    _ride.StartRide(workout, next.Intensity);
                }
            }
            else
            {
                GUI.Label(new Rect(head.x + 360, head.y, head.width - 380, head.height), L.Get("plan_nothing_left"), Theme.BodyMuted);
            }

            // This week and next week
            float y = head.yMax + 16;
            for (int wk = 0; wk < 2 && week + wk < plan.weeks; wk++)
            {
                DrawPlanWeek(new Rect(x0, y, w, 100), plan, plan.Start.AddDays(7 * (week + wk)), week + wk, today);
                y += 112;
            }

            // Training days / end plan
            y += 12;
            if (_editingDays)
            {
                var r = new Rect(x0, y, w, 138);
                Ui.Card(r);
                GUI.Label(new Rect(r.x + 24, r.y + 16, 400, 20), L.Get("training_days").ToUpperInvariant(), Theme.Overline);
                DayToggles(new Rect(r.x + 24, r.y + 42, r.width - 48 - 360, 52), _planDays);
                bool valid = _planDays.Count >= PlanTemplates.MinSessions && _planDays.Count <= PlanTemplates.MaxSessions;
                if (Ui.Button(new Rect(r.xMax - 340, r.y + 42, 150, 52), L.Get("cancel"), ButtonKind.Secondary)) _editingDays = false;
                if (Ui.Button(new Rect(r.xMax - 180, r.y + 42, 156, 52), L.Get("apply"), ButtonKind.Primary, Icon.Check, valid))
                {
                    PlanStore.ChangeDays(_planDays);
                    _editingDays = false;
                }

                GUI.Label(new Rect(r.x + 24, r.y + 104, r.width - 48, 22), L.Get("plan_days_regenerate"), Theme.Caption);
            }
            else
            {
                if (Ui.Button(new Rect(x0, y, 260, 52), L.Get("change_days"), ButtonKind.Secondary, Icon.Edit))
                {
                    _planDays.Clear();
                    foreach (int d in plan.weekdays) _planDays.Add((DayOfWeek)d);
                    _editingDays = true;
                }

                if (Ui.Button(new Rect(x0 + w - 240, y, 240, 52), _confirmStopPlan ? L.Get("confirm_stop_plan") : L.Get("stop_plan"), _confirmStopPlan ? ButtonKind.Danger : ButtonKind.Ghost, Icon.Trash))
                {
                    if (_confirmStopPlan) PlanStore.Stop();
                    _confirmStopPlan = !_confirmStopPlan;
                }
            }

            Ui.EndScroll(3101, view, contentH);
        }

        private void DrawPlanWeek(Rect r, TrainingPlan plan, DateTime monday, int week, DateTime today)
        {
            var template = PlanTemplates.Find(plan.templateId);
            bool recoveryWeek = template != null && PlanTemplates.IsRecoveryWeek(template, week, plan.weeks);
            GUI.Label(new Rect(r.x, r.y, 400, 18), (string.Format(L.Get("week_n"), week + 1) + (recoveryWeek ? " · " + L.Get("recovery_week") : "")).ToUpperInvariant(), Theme.Overline);
            float cw = (r.width - 6 * 8) / 7f;
            for (int i = 0; i < 7; i++)
            {
                var day = monday.AddDays(i);
                var cell = new Rect(r.x + i * (cw + 8), r.y + 22, cw, r.height - 22);
                var session = plan.On(day).FirstOrDefault();
                bool isToday = day == today;
                Ui.Rect(cell, session != null ? Theme.Surface : Theme.Alpha(Theme.Surface, 0.5f), Theme.RadiusSmall);
                if (isToday) Ui.Outline(cell, Theme.Accent, 2, Theme.RadiusSmall);
                GUI.Label(new Rect(cell.x + 10, cell.y + 6, cell.width - 20, 18), $"{DayShort(day.DayOfWeek)} {day:dd.MM.}", Theme.With(Theme.Caption, isToday ? Theme.Accent : Theme.Muted, 13));
                if (session == null) continue;
                Ui.Rect(new Rect(cell.x + 10, cell.y + 28, 4, cell.height - 38), KindColor(session.Kind), 2);
                var workout = PlanStore.WorkoutOf(session);
                GUI.Label(new Rect(cell.x + 20, cell.y + 26, cell.width - 26, 36), workout?.Name.Get(AppSettings.Language) ?? session.workoutId,
                    Theme.With(Theme.Body, session.Status == SessionStatus.Missed ? Theme.Faint : Theme.Text, 13, wrap: true));
                if (session.Status == SessionStatus.Done) Ui.DrawIcon(new Rect(cell.xMax - 24, cell.yMax - 24, 18, 18), Icon.Check, Theme.Success);
                else if (session.Status == SessionStatus.Missed) Ui.DrawIcon(new Rect(cell.xMax - 24, cell.yMax - 24, 18, 18), Icon.Close, Theme.Faint);
                else if (session.adjustPercent != 0) GUI.Label(new Rect(cell.x + 20, cell.yMax - 22, cell.width - 26, 18), $"{session.adjustPercent:+0;-0} %", Theme.With(Theme.Caption, Theme.Muted, 12));
            }
        }

        private static string DayLabel(DateTime day) => $"{DayShort(day.DayOfWeek)} {day:dd.MM.}";

        // ------------------------------------------------------------------ history

        private void DrawHistory(Rect c)
        {
            var rides = HistoryStore.Rides;
            var view = new Rect(c.x - 8, c.y, c.width + 16, c.height);
            int shown = Mathf.Min(rides.Count, 30);
            float contentH = 220 + 16 + 200 + 16 + 230 + 24 + 30 + (Mathf.Max(1, shown) * 62) + 16;
            Ui.BeginScroll(3102, view, contentH);
            float x0 = 8, w = c.width;
            var today = DateTime.Now.Date;

            if (rides.Count == 0)
            {
                var empty = new Rect(x0, 0, w, 96);
                Ui.Card(empty);
                GUI.Label(new Rect(empty.x + 24, empty.y, empty.width - 48, empty.height),
                    HistoryStore.Importing ? L.Get("history_importing") : L.Get("history_empty"), Theme.BodyMuted);
                Ui.EndScroll(3102, view, contentH);
                return;
            }

            // Fitness (CTL / ATL / TSB), last 90 days
            var fitness = TrainingLoad.Fitness(rides, today);
            var fit = new Rect(x0, 0, w, 220);
            Ui.Card(fit);
            SectionTitle(fit, L.Get("fitness"), Icon.Heart);
            if (fitness.Count > 0)
            {
                var last = fitness[fitness.Count - 1];
                float lx = fit.xMax - 24;
                lx -= LegendValue(lx, fit.y + 24, L.Get("form"), last.Tsb, last.Tsb >= 0 ? Theme.Success : Theme.Warning) + 18;
                lx -= LegendValue(lx, fit.y + 24, L.Get("fatigue"), last.Atl, AtlColor) + 18;
                LegendValue(lx, fit.y + 24, L.Get("fitness_ctl"), last.Ctl, CtlColor);
                DrawFitnessChart(new Rect(fit.x + 24, fit.y + 74, fit.width - 48, fit.height - 92), fitness.Skip(Math.Max(0, fitness.Count - 90)).ToList());
            }

            // Weekly volume (12 weeks)
            float y = fit.yMax + 16;
            var vol = new Rect(x0, y, w, 200);
            Ui.Card(vol);
            SectionTitle(vol, L.Get("weekly_volume"), Icon.Timer);
            var weeks = TrainingLoad.Weekly(rides, today, 12);
            DrawWeekBars(new Rect(vol.x + 24, vol.y + 70, vol.width - 48, vol.height - 88), weeks);

            // Power curve (90 days vs. all time) and FTP history
            y = vol.yMax + 16;
            float half = (w - 16) / 2f;
            var curveCard = new Rect(x0, y, half, 230);
            Ui.Card(curveCard);
            SectionTitle(curveCard, L.Get("power_curve"), Icon.Bolt);
            var recent = TrainingLoad.BestPowerCurve(rides.Where(r => r.StartUtc >= DateTime.UtcNow.AddDays(-90)));
            var allTime = TrainingLoad.BestPowerCurve(rides);
            DrawCurveBars(new Rect(curveCard.x + 20, curveCard.y + 70, curveCard.width - 40, curveCard.height - 90), recent, allTime);

            var ftpCard = new Rect(x0 + half + 16, y, half, 230);
            Ui.Card(ftpCard);
            SectionTitle(ftpCard, L.Get("ftp_history"), Icon.Target);
            float fy = ftpCard.y + 70;
            foreach (var f in HistoryStore.Data.ftpHistory.AsEnumerable().Reverse().Take(5))
            {
                var when = DateTimeOffset.FromUnixTimeSeconds(f.timeUtc).LocalDateTime;
                GUI.Label(new Rect(ftpCard.x + 24, fy, 140, 26), when.ToString("dd.MM.yyyy"), Theme.BodyMuted);
                GUI.Label(new Rect(ftpCard.x + 170, fy, 100, 26), $"{f.ftpW} W", Theme.With(Theme.H3, size: 18));
                if (f.source == "test") Ui.Chip(new Rect(ftpCard.x + 270, fy, 0, 24), L.Get("ftp_test_short"), Theme.Accent);
                fy += 30;
            }

            // Rides
            y = curveCard.yMax + 24;
            GUI.Label(new Rect(x0, y, 400, 20), string.Format(L.Get("rides_count"), rides.Count).ToUpperInvariant(), Theme.Overline);
            if (HistoryStore.Importing) GUI.Label(new Rect(x0 + w - 300, y, 300, 20), L.Get("history_importing"), Theme.With(Theme.Caption, anchor: TextAnchor.MiddleRight));
            y += 30;
            foreach (var r in rides.Reverse().Take(30))
            {
                var row = new Rect(x0, y, w, 54);
                Ui.Rect(row, Theme.Surface, Theme.RadiusSmall);
                var when = r.StartUtc.ToLocalTime();
                GUI.Label(new Rect(row.x + 16, row.y + 6, 160, 22), when.ToString("ddd dd.MM.yy"), Theme.With(Theme.Caption, size: 14));
                GUI.Label(new Rect(row.x + 16, row.y + 26, 160, 22), when.ToString("HH:mm"), Theme.With(Theme.Caption, Theme.Muted, 13));
                GUI.Label(new Rect(row.x + 170, row.y, w * 0.26f, row.height), r.title, Clip(Theme.With(Theme.Body, size: 16, anchor: TextAnchor.MiddleLeft)));

                // Actions on the FIT file: Strava, share, Downloads (as long as the file is on the phone)
                const float buttonsW = (3 * 46) + 16;
                string fitPath = System.IO.Path.Combine(HistoryStore.RidesFolder, r.id);
                if (System.IO.File.Exists(fitPath))
                {
                    float bx = row.xMax - buttonsW - 4;
                    StravaRideButton(new Rect(bx, row.y + 4, 46, 46), fitPath, r.title);
                    if (Ui.IconButton(new Rect(bx + 54, row.y + 4, 46, 46), Icon.Share, ButtonKind.Ghost)) Ui.Toast(FileActions.Share(fitPath, L.Get("share")));
                    if (Ui.IconButton(new Rect(bx + 108, row.y + 4, 46, 46), Icon.Download, ButtonKind.Ghost)) Ui.Toast(FileActions.SaveToDownloads(fitPath));
                }

                float sx = row.x + 170 + w * 0.26f + 8;
                float sw = (row.xMax - buttonsW - 16 - sx) / 4f;
                Stat(new Rect(sx, row.y, sw, row.height), Ui.Clock(r.durationS));
                Stat(new Rect(sx + sw, row.y, sw, row.height), r.normalizedPowerW > 0 ? $"{r.normalizedPowerW} W NP" : "–");
                Stat(new Rect(sx + 2 * sw, row.y, sw, row.height), $"{r.tss:0} TSS");
                Stat(new Rect(sx + 3 * sw, row.y, sw, row.height), r.distanceM > 0 ? $"{r.distanceM / 1000:0.0} km" : "–");
                y += 62;
            }

            Ui.EndScroll(3102, view, contentH);
        }

        private static void Stat(Rect r, string text) => GUI.Label(r, text, Theme.With(Theme.Body, Theme.Muted, 15, TextAnchor.MiddleRight));

        /// <summary>Value with a colour mark, right-aligned at <paramref name="right"/>; returns the width used.</summary>
        private static float LegendValue(float right, float y, string label, double value, Color color)
        {
            var style = Theme.With(Theme.H3, size: 18, anchor: TextAnchor.MiddleRight);
            string text = $"{label} <b>{value:0}</b>";
            float tw = style.CalcSize(new GUIContent(text)).x;
            GUI.Label(new Rect(right - tw, y, tw, 28), text, style);
            Ui.Rect(new Rect(right - tw - 16, y + 9, 10, 10), color, 5);
            return tw + 16;
        }

        /// <summary>CTL as filled area, ATL as dots, TSB as bars around a zero line (separate scale).</summary>
        private static void DrawFitnessChart(Rect r, List<FitnessDay> days)
        {
            if (days.Count == 0) return;
            double max = Math.Max(20, days.Max(d => Math.Max(d.Ctl, d.Atl)));
            double tsbRange = Math.Max(20, days.Max(d => Math.Abs(d.Tsb)));
            float colW = r.width / days.Count;
            float tsbH = r.height * 0.3f;
            float mainH = r.height - tsbH - 8;
            float zero = r.yMax - tsbH / 2f;
            Ui.Rect(new Rect(r.x, zero, r.width, 1), Theme.Stroke, 0);
            for (int i = 0; i < days.Count; i++)
            {
                var d = days[i];
                float x = r.x + i * colW;
                float ctlH = (float)(d.Ctl / max) * mainH;
                Ui.Rect(new Rect(x, r.y + mainH - ctlH, Mathf.Max(1, colW - 0.5f), ctlH), Theme.Alpha(CtlColor, 0.45f), 0);
                float atlY = r.y + mainH - (float)(d.Atl / max) * mainH;
                Ui.Rect(new Rect(x, atlY - 1.5f, Mathf.Max(2, colW), 3), AtlColor, 1);
                float tsb = (float)(d.Tsb / tsbRange) * (tsbH / 2f);
                var col = d.Tsb >= 0 ? Theme.Alpha(Theme.Success, 0.8f) : Theme.Alpha(Theme.Warning, 0.8f);
                Ui.Rect(tsb >= 0 ? new Rect(x, zero - tsb, Mathf.Max(1, colW - 0.5f), tsb) : new Rect(x, zero, Mathf.Max(1, colW - 0.5f), -tsb), col, 0);
            }
        }

        private static void DrawWeekBars(Rect r, List<WeekVolume> weeks)
        {
            double maxH = Math.Max(1, weeks.Max(w => w.Seconds / 3600.0));
            float bw = r.width / weeks.Count;
            for (int i = 0; i < weeks.Count; i++)
            {
                var wk = weeks[i];
                float h = (float)(wk.Seconds / 3600.0 / maxH) * (r.height - 40);
                var bar = new Rect(r.x + i * bw + 4, r.yMax - 20 - h, bw - 8, h);
                Ui.Rect(bar, i == weeks.Count - 1 ? Theme.Accent : Theme.Alpha(Theme.Accent, 0.55f), 4);
                if (wk.Seconds > 0) GUI.Label(new Rect(bar.x - 10, bar.y - 20, bar.width + 20, 18), $"{wk.Seconds / 3600.0:0.#} h", Theme.With(Theme.Caption, size: 12, anchor: TextAnchor.MiddleCenter));
                GUI.Label(new Rect(bar.x - 10, r.yMax - 18, bar.width + 20, 18), wk.Monday.ToString("dd.MM."), Theme.With(Theme.Caption, Theme.Muted, 11, TextAnchor.MiddleCenter));
            }
        }

        private static void DrawCurveBars(Rect r, int[] recent, int[] allTime)
        {
            var durations = RideAnalysis.CurveDurations;
            int max = Math.Max(1, allTime.DefaultIfEmpty(0).Max());
            float bw = r.width / durations.Length;
            for (int i = 0; i < durations.Length; i++)
            {
                float hAll = allTime[i] / (float)max * (r.height - 40);
                float hRecent = recent[i] / (float)max * (r.height - 40);
                var x = r.x + i * bw + 3;
                Ui.Rect(new Rect(x, r.yMax - 20 - hAll, bw - 6, hAll), Theme.Alpha(Theme.Accent, 0.25f), 3);
                Ui.Rect(new Rect(x, r.yMax - 20 - hRecent, bw - 6, hRecent), Theme.Accent, 3);
                if (recent[i] > 0) GUI.Label(new Rect(x - 8, r.yMax - 38 - hRecent, bw + 10, 16), recent[i].ToString(), Theme.With(Theme.Caption, size: 11, anchor: TextAnchor.MiddleCenter));
                int d = durations[i];
                GUI.Label(new Rect(x - 8, r.yMax - 18, bw + 10, 18), d < 60 ? $"{d}s" : $"{d / 60}m", Theme.With(Theme.Caption, Theme.Muted, 11, TextAnchor.MiddleCenter));
            }
        }
    }
}
