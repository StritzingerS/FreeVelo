using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FreeVelo.App;
using FreeVelo.Core.History;
using FreeVelo.Core.Plans;
using FreeVelo.Core.Training;
using FreeVelo.Core.Ui;
using FreeVelo.Net;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>The detail of one ride (from its FIT file) and the month calendar of the history.</summary>
    public sealed partial class Hud
    {
        private RideRecord _detailRide;
        private Task<RideDetail> _detailTask;
        private DateTime _calendarMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

        private const float CalendarCardH = 420;

        private void OpenRideDetail(RideRecord ride)
        {
            _detailRide = ride;
            string path = Path.Combine(HistoryStore.RidesFolder, ride.id);
            var powerZones = AppSettings.Zones;
            var hrZones = AppSettings.HrZones;
            int ftp = ride.ftpW > 0 ? ride.ftpW : AppSettings.FtpW;
            _detailTask = Task.Run(() => File.Exists(path)
                ? RideDetail.From(FitReader.ReadActivity(File.ReadAllBytes(path)), ftp, powerZones, hrZones)
                : null);
            Navigate(Page.RideDetail);
        }

        private void DrawRideDetail(Rect c)
        {
            var r = _detailRide;
            if (r == null)
            {
                Navigate(Page.Training);
                return;
            }

            if (Ui.IconButton(new Rect(c.x, c.y, 52, 52), Icon.ChevronLeft, ButtonKind.Secondary)) Navigate(Page.Training);
            var when = r.StartUtc.ToLocalTime();
            GUI.Label(new Rect(c.x + 68, c.y - 2, c.width - 260, 34), r.title, Clip(Theme.H2));
            GUI.Label(new Rect(c.x + 68, c.y + 30, c.width - 260, 22), when.ToString("dddd, dd.MM.yyyy · HH:mm") + (r.groupRide ? " · " + L.Get("group") : "") + ClimbsText(r), Clip(Theme.Caption));

            string fit = Path.Combine(HistoryStore.RidesFolder, r.id);
            if (r.stravaId != 0)
            {
                // Imported from Strava: it is there already.
                if (Ui.IconButton(new Rect(c.xMax - 50, c.y, 50, 50), Icon.Share, ButtonKind.Secondary)) StravaService.OpenActivity(r.stravaId);
            }
            else if (File.Exists(fit))
            {
                float bx = c.xMax - 166;
                StravaRideButton(new Rect(bx, c.y, 50, 50), fit, r.title);
                if (Ui.IconButton(new Rect(bx + 58, c.y, 50, 50), Icon.Share, ButtonKind.Secondary)) Ui.Toast(FileActions.Share(fit, L.Get("share")));
                if (Ui.IconButton(new Rect(bx + 116, c.y, 50, 50), Icon.Download, ButtonKind.Secondary)) Ui.Toast(FileActions.SaveToDownloads(fit));
            }

            var detail = _detailTask != null && _detailTask.IsCompleted && !_detailTask.IsFaulted ? _detailTask.Result : null;
            var powerZones = AppSettings.Zones;
            var hrZones = AppSettings.HrZones;
            int lapCount = detail?.Laps.Count ?? 0;
            float zonesH = 70 + Mathf.Max(powerZones.Zones.Count, hrZones.Zones.Count) * 30;
            float lapsH = lapCount > 0 ? 100 + lapCount * 34 : 0;
            var view = new Rect(c.x - 8, c.y + 70, c.width + 16, c.height - 70);
            float contentH = 176 + 16 + 250 + 16 + zonesH + 16 + lapsH + 16;
            Ui.BeginScroll(3103, view, contentH);
            float x0 = 8, w = c.width;

            // Tiles: 2 rows of 4
            var tiles = new (string Label, string Value)[]
            {
                (L.Get("time"), Ui.Clock(r.durationS)),
                (L.Get("distance"), r.distanceM > 0 ? $"{r.distanceM / 1000:0.0} km" : "–"),
                (L.Get("avg_power"), $"{r.avgPowerW} W"),
                ("NP", $"{r.normalizedPowerW} W"),
                ("IF · TSS", $"{r.intensityFactor:0.00} · {r.tss:0}"),
                (L.Get("heart_rate"), r.avgHeartRate > 0 ? $"{r.avgHeartRate} / {r.maxHeartRate}" : "–"),
                (L.Get("cadence"), r.avgCadence > 0 ? $"{r.avgCadence} rpm" : "–"),
                ("kJ · " + L.Get("ascent"), $"{r.kj:0} · {r.ascentM:0} m"),
            };
            float tw = (w - 3 * 12) / 4f;
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = new Rect(x0 + (i % 4) * (tw + 12), (i / 4) * 88, tw, 80);
                Ui.Card(t);
                GUI.Label(new Rect(t.x + 16, t.y + 12, t.width - 32, 18), tiles[i].Label.ToUpperInvariant(), Theme.Overline);
                GUI.Label(new Rect(t.x + 16, t.y + 32, t.width - 32, 34), tiles[i].Value, Theme.With(Theme.H3, size: 22));
            }

            float y = 176 + 16;
            var graphCard = new Rect(x0, y, w, 250);
            Ui.Card(graphCard);
            SectionTitle(graphCard, L.Get("power") + (detail != null && detail.HasHeartRate ? " · " + L.Get("heart_rate") : ""), Icon.Bolt);
            if (detail == null)
            {
                bool missing = _detailTask != null && _detailTask.IsCompleted && (_detailTask.IsFaulted || _detailTask.Result == null);
                GUI.Label(new Rect(graphCard.x + 24, graphCard.y + 70, graphCard.width - 48, 40), missing ? L.Get("detail_no_file") : L.Get("loading"), Theme.BodyMuted);
                Ui.EndScroll(3103, view, contentH);
                return;
            }

            DrawRideGraph(new Rect(graphCard.x + 24, graphCard.y + 70, graphCard.width - 48, graphCard.height - 88), detail);

            // Time in zones (power | heart rate)
            y = graphCard.yMax + 16;
            float half = (w - 16) / 2f;
            var pz = new Rect(x0, y, half, zonesH);
            Ui.Card(pz);
            SectionTitle(pz, L.Get("time_in_zones"), Icon.Bolt);
            int total = Math.Max(1, detail.TimeInPowerZones.Values.Sum());
            float zy = pz.y + 66;
            foreach (var z in powerZones.Zones)
            {
                ColorUtility.TryParseHtmlString(z.ColorHex, out var col);
                detail.TimeInPowerZones.TryGetValue(z.Number, out int secs);
                ZoneBar(new Rect(pz.x + 20, zy, pz.width - 40, 26), $"Z{z.Number}", secs, total, col);
                zy += 30;
            }

            var hz = new Rect(x0 + half + 16, y, half, zonesH);
            Ui.Card(hz);
            SectionTitle(hz, L.Get("hr_zones"), Icon.Heart);
            if (!detail.HasHeartRate)
            {
                GUI.Label(new Rect(hz.x + 24, hz.y + 70, hz.width - 48, 30), L.Get("no_heart_rate"), Theme.BodyMuted);
            }
            else
            {
                int hrTotal = Math.Max(1, detail.TimeInHeartRateZones.Values.Sum());
                zy = hz.y + 66;
                foreach (var z in hrZones.Zones)
                {
                    ColorUtility.TryParseHtmlString(z.ColorHex, out var col);
                    detail.TimeInHeartRateZones.TryGetValue(z.Number, out int secs);
                    ZoneBar(new Rect(hz.x + 20, zy, hz.width - 40, 26), $"Z{z.Number}", secs, hrTotal, col);
                    zy += 30;
                }
            }

            // Laps (one per workout block)
            if (lapCount > 0)
            {
                y = pz.yMax + 16;
                var laps = new Rect(x0, y, w, lapsH);
                Ui.Card(laps);
                SectionTitle(laps, L.Get("laps"), Icon.List);
                var cols = new[] { "#", L.Get("start"), L.Get("time"), L.Get("avg_power"), "Max", L.Get("heart_rate"), L.Get("cadence") };
                float cw = (laps.width - 48) / cols.Length;
                for (int i = 0; i < cols.Length; i++)
                {
                    GUI.Label(new Rect(laps.x + 24 + i * cw, laps.y + 62, cw, 20), cols[i].ToUpperInvariant(), Theme.With(Theme.Overline, anchor: i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight));
                }

                float ly = laps.y + 88;
                foreach (var lap in detail.Laps)
                {
                    var cells = new[]
                    {
                        lap.Number.ToString(), Ui.Clock(lap.StartS), Ui.Clock(lap.DurationS), $"{lap.AvgPowerW} W", $"{lap.MaxPowerW} W",
                        lap.AvgHeartRate.HasValue ? $"{lap.AvgHeartRate} bpm" : "–", lap.AvgCadence.HasValue ? $"{lap.AvgCadence} rpm" : "–",
                    };
                    if (lap.Number % 2 == 0) Ui.Rect(new Rect(laps.x + 12, ly - 2, laps.width - 24, 32), Theme.Alpha(Theme.Surface2, 0.5f), 6);
                    for (int i = 0; i < cells.Length; i++)
                    {
                        GUI.Label(new Rect(laps.x + 24 + i * cw, ly, cw, 28), cells[i], Theme.With(Theme.Body, size: 15, anchor: i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight));
                    }

                    ly += 34;
                }
            }

            Ui.EndScroll(3103, view, contentH);
        }

        private static void ZoneBar(Rect r, string label, int seconds, int total, Color color)
        {
            GUI.Label(new Rect(r.x, r.y, 36, r.height), label, Theme.With(Theme.Body, size: 14));
            var bar = new Rect(r.x + 40, r.y + 5, r.width - 40 - 110, r.height - 10);
            Ui.Rect(bar, Theme.Surface2, 4);
            Ui.Rect(new Rect(bar.x, bar.y, bar.width * seconds / total, bar.height), color, 4);
            GUI.Label(new Rect(r.xMax - 106, r.y, 106, r.height), $"{Ui.Clock(seconds)} · {100.0 * seconds / total:0}%", Theme.With(Theme.Caption, size: 13, anchor: TextAnchor.MiddleRight));
        }

        /// <summary>Power as filled columns, heart rate as dots (own scale), elevation as a faint band at the bottom.</summary>
        private static void DrawRideGraph(Rect r, RideDetail d)
        {
            var g = d.Graph;
            if (g.Count == 0) return;
            double maxP = Math.Max(100, g.Max(p => p.PowerW));
            var hrs = g.Where(p => p.HeartRate.HasValue).Select(p => p.HeartRate.Value).ToList();
            double minHr = hrs.Count > 0 ? hrs.Min() - 10 : 0, maxHr = hrs.Count > 0 ? hrs.Max() + 5 : 1;
            var alts = g.Where(p => p.AltitudeM.HasValue).Select(p => p.AltitudeM.Value).ToList();
            double minAlt = alts.Count > 0 ? alts.Min() : 0, altRange = alts.Count > 0 ? Math.Max(10, alts.Max() - minAlt) : 1;
            float h = r.height - 20;
            float colW = r.width / g.Count;
            for (int i = 0; i < g.Count; i++)
            {
                var p = g[i];
                float x = r.x + i * colW;
                if (p.AltitudeM.HasValue)
                {
                    float ah = (float)((p.AltitudeM.Value - minAlt) / altRange) * h * 0.25f;
                    Ui.Rect(new Rect(x, r.y + h - ah, Mathf.Max(1, colW), ah), Theme.Alpha(Theme.Muted, 0.18f), 0);
                }

                float ph = (float)(p.PowerW / maxP) * h;
                Ui.Rect(new Rect(x, r.y + h - ph, Mathf.Max(1, colW - 0.5f), ph), Theme.Alpha(Theme.Accent, 0.7f), 0);
                if (p.HeartRate.HasValue)
                {
                    float hy = r.y + h - (float)((p.HeartRate.Value - minHr) / (maxHr - minHr)) * h;
                    Ui.Rect(new Rect(x, hy - 1.5f, Mathf.Max(2, colW), 3), Theme.HeartRed, 1);
                }
            }

            // Time axis
            double total = g[g.Count - 1].TimeS;
            for (int k = 0; k <= 4; k++)
            {
                float x = r.x + r.width * k / 4f;
                GUI.Label(new Rect(x - 40, r.yMax - 18, 80, 18), Ui.Clock(total * k / 4), Theme.With(Theme.Caption, Theme.Muted, 11, TextAnchor.MiddleCenter));
            }

            GUI.Label(new Rect(r.x, r.y - 2, 120, 18), $"{maxP:0} W", Theme.With(Theme.Caption, Theme.Muted, 11));
        }

        // ------------------------------------------------------------------ calendar

        /// <summary>History: a month with the rides (TSS) and the plan's sessions; tap a ride day for its detail.</summary>
        private void DrawCalendar(Rect card)
        {
            Ui.Card(card);
            var month = _calendarMonth;
            string title = month.ToString("MMMM yyyy", AppSettings.Language == "de" ? new System.Globalization.CultureInfo("de-DE") : System.Globalization.CultureInfo.InvariantCulture);
            SectionTitle(card, title, Icon.Timer);
            if (Ui.IconButton(new Rect(card.xMax - 120, card.y + 16, 44, 44), Icon.ChevronLeft, ButtonKind.Ghost)) _calendarMonth = month.AddMonths(-1);
            if (Ui.IconButton(new Rect(card.xMax - 68, card.y + 16, 44, 44), Icon.ChevronRight, ButtonKind.Ghost)) _calendarMonth = month.AddMonths(1);

            var grid = new Rect(card.x + 20, card.y + 70, card.width - 40, card.height - 86);
            float cw = (grid.width - 6 * 6) / 7f;
            for (int i = 0; i < 7; i++)
            {
                GUI.Label(new Rect(grid.x + i * (cw + 6), grid.y, cw, 18), DayShort(MondayFirst[i]).ToUpperInvariant(), Theme.With(Theme.Overline, anchor: TextAnchor.MiddleCenter));
            }

            var first = TrainingLoad.Monday(month);
            float ch = (grid.height - 24 - 5 * 6) / 6f;
            var today = DateTime.Now.Date;
            var rides = HistoryStore.Rides;
            var plan = PlanStore.Plan;
            for (int k = 0; k < 42; k++)
            {
                var day = first.AddDays(k);
                var cell = new Rect(grid.x + (k % 7) * (cw + 6), grid.y + 24 + (k / 7) * (ch + 6), cw, ch);
                bool inMonth = day.Month == month.Month;
                var dayRides = rides.Where(x => x.StartUtc.ToLocalTime().Date == day).ToList();
                Ui.Rect(cell, inMonth ? Theme.Surface2 : Theme.Alpha(Theme.Surface2, 0.35f), 8);
                if (day == today) Ui.Outline(cell, Theme.Accent, 2, 8);
                GUI.Label(new Rect(cell.x + 8, cell.y + 4, 40, 18), day.Day.ToString(), Theme.With(Theme.Caption, inMonth ? Theme.Text : Theme.Faint, 13));

                var session = plan?.On(day).FirstOrDefault();
                if (session != null)
                {
                    var color = session.Status == SessionStatus.Missed ? Theme.Faint : KindColor(session.Kind);
                    Ui.Dot(new Vector2(cell.xMax - 10, cell.y + 12), 4, color);
                }

                if (dayRides.Count > 0)
                {
                    double tss = dayRides.Sum(x => x.tss);
                    var chip = new Rect(cell.x + 6, cell.yMax - 26, cell.width - 12, 20);
                    Ui.Rect(chip, Theme.Alpha(Theme.Accent, Mathf.Clamp01(0.3f + (float)tss / 150f)), 6);
                    GUI.Label(chip, dayRides.Count > 1 ? $"{tss:0} · {dayRides.Count}×" : $"{tss:0} TSS", Theme.With(Theme.Caption, Color.white, 12, TextAnchor.MiddleCenter));
                    if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) OpenRideDetail(dayRides.OrderByDescending(x => x.tss).First());
                }
            }
        }
    }
}
