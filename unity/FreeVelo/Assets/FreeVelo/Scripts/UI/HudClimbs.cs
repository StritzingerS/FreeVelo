using System.Collections.Generic;
using System.Linq;
using FreeVelo.App;
using FreeVelo.Core.History;
using FreeVelo.Core.Ui;
using FreeVelo.Core.World.Gen;
using FreeVelo.Ride;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>
    /// The legendary climbs: start cards on the home page, the climb timer while riding (with the gap to the best
    /// time), best times in the history and the result in the ride summary.
    /// </summary>
    public sealed partial class Hud
    {
        private static readonly string[] ClimbIds = { ClimbData.Stelvio, ClimbData.AlpeDHuez };

        /// <summary>Height of the climbs section on the home page (title + cards).</summary>
        private const float ClimbSectionH = 26 + 196;

        private static Dictionary<string, (double BestS, int Count)> ClimbRecords() => TrainingLoad.ClimbRecords(HistoryStore.Rides);

        // ------------------------------------------------------------------ home

        private void DrawClimbCards(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, 400, 20), L.Get("climbs").ToUpperInvariant(), Theme.Overline);
            bool inGroup = _group?.Lobby != null;
            if (inGroup) GUI.Label(new Rect(x + w - 500, y, 500, 20), L.Get("climb_group_hint"), Theme.With(Theme.Caption, anchor: TextAnchor.MiddleRight));
            var records = ClimbRecords();
            float cw = (w - 16) / 2f;
            for (int i = 0; i < ClimbIds.Length; i++)
            {
                var data = ClimbData.Load(ClimbIds[i]);
                var r = new Rect(x + (i * (cw + 16)), y + 26, cw, 196);
                Ui.Card(r);
                GUI.Label(new Rect(r.x + 16, r.y + 12, r.width - 32, 28), data.Name, Clip(Theme.With(Theme.H3, size: 20)));
                GUI.Label(new Rect(r.x + 16, r.y + 40, r.width - 32, 20),
                    $"{data.Length / 1000:0.0} km · {data.Gain:0} m ↑ · Ø {data.Gain / data.Length * 100:0.0} %", Theme.Caption);
                ClimbProfile(new Rect(r.x + 16, r.y + 70, r.width - 96, 76), data);
                string best = records.TryGetValue(data.Id, out var rec)
                    ? $"{L.Get("best_time")} <b>{Ui.Clock(rec.BestS)}</b> · {string.Format(L.Get("ascents"), rec.Count)}"
                    : L.Get("no_ascent_yet");
                GUI.Label(new Rect(r.x + 16, r.y + 156, r.width - 96, 26), best, Clip(Theme.With(Theme.Body, size: 15)));
                if (Ui.IconButton(new Rect(r.xMax - 60, r.yMax - 60, 46, 46), Icon.Play, ButtonKind.Primary, !inGroup)) _ride.StartClimb(data.Id);
            }
        }

        /// <summary>Elevation profile of a climb as bars, coloured by gradient (like the signs at the roadside).</summary>
        private static void ClimbProfile(Rect r, ClimbData data)
        {
            const int n = 40;
            double min = data.E.Min(), max = data.E.Max();
            float bw = r.width / n;
            int last = data.E.Length - 1;
            for (int i = 0; i < n; i++)
            {
                int a = (int)((long)last * i / n), b = (int)((long)last * (i + 1) / n);
                double grade = (data.E[b] - data.E[a]) / System.Math.Max(1, data.D[b] - data.D[a]) * 100;
                float h = 4 + (float)((data.E[b] - min) / System.Math.Max(1, max - min)) * (r.height - 4);
                var color = grade < 5 ? Theme.Success : grade < 8 ? Theme.Warning : grade < 10 ? Theme.Danger : new Color(0.55f, 0.15f, 0.2f);
                Ui.Rect(new Rect(r.x + (i * bw), r.yMax - h, Mathf.Max(1, bw - 1), h), color, 1);
            }
        }

        // ------------------------------------------------------------------ riding

        /// <summary>Climb timer: time on the climb, distance to the summit and the gap to the best time.</summary>
        private void DrawClimbTimer(bool workout)
        {
            var c = _ride.ActiveClimb;
            if (c == null) return;
            float top = workout ? _sideTop + (_ride.WorkoutComplete ? 150 : 330) + 16 : _leftTop;
            var card = new Rect(workout ? _w - 344 : 24, top, 320, 204);
            Ui.Card(card, Theme.Alpha(Theme.Background, 0.88f));
            GUI.Label(new Rect(card.x + 22, card.y + 16, card.width - 44, 18), L.Get("climb").ToUpperInvariant(), Theme.With(Theme.Overline, Theme.Accent));
            GUI.Label(new Rect(card.x + 22, card.y + 34, card.width - 44, 28), c.Name, Clip(Theme.With(Theme.H3, size: 20)));

            double elapsed = _ride.ElapsedS - _ride.ActiveClimbStartS;
            GUI.Label(new Rect(card.x + 22, card.y + 62, card.width - 44, 56), Ui.Clock(elapsed), Theme.With(Theme.Display, size: 48, anchor: TextAnchor.MiddleLeft));

            float frac = c.LengthM > 0 ? Mathf.Clamp01((float)(_ride.ActiveClimbProgressM / c.LengthM)) : 0;
            var pb = new Rect(card.x + 22, card.y + 124, card.width - 44, 8);
            Ui.Rect(pb, Theme.Surface3, 4);
            Ui.Rect(new Rect(pb.x, pb.y, pb.width * frac, pb.height), Theme.Accent, 4);

            double toGo = System.Math.Max(0, c.LengthM - _ride.ActiveClimbProgressM);
            GUI.Label(new Rect(card.x + 22, card.y + 140, card.width - 44, 22), $"{toGo / 1000:0.0} km {L.Get("to_summit")}", Theme.Caption);

            // Elevation gained on the climb so far, of its total gain.
            double gained = System.Math.Max(0, _ride.Position.Y - _ride.Route.Sample(c.StartM).Y);
            Ui.DrawIcon(new Rect(card.x + 22, card.y + 170, 18, 18), Icon.Ascent, Theme.Success);
            GUI.Label(new Rect(card.x + 48, card.y + 168, card.width - 70, 22),
                $"<b>{System.Math.Min(gained, c.GainM):0} m</b> <color=#B3BECA>/ {c.GainM:0} m</color>", Theme.With(Theme.Body, size: 16));

            // Gap to the best time, assuming the best ascent went at an even pace over the distance.
            if (ClimbRecords().TryGetValue(c.Id, out var rec) && frac > 0.02f)
            {
                double gap = elapsed - (rec.BestS * frac);
                string text = (gap > 0 ? "+" : "−") + Ui.Clock(System.Math.Abs(gap));
                GUI.Label(new Rect(card.x + 22, card.y + 62, card.width - 44, 56), text, Theme.With(Theme.H3, gap > 0 ? Theme.Danger : Theme.Success, 20, TextAnchor.MiddleRight));
                GUI.Label(new Rect(card.x + 22, card.y + 140, card.width - 44, 22), $"{L.Get("best_time")} {Ui.Clock(rec.BestS)}", Theme.With(Theme.Caption, anchor: TextAnchor.MiddleRight));
            }
        }

        // ------------------------------------------------------------------ summary and history

        /// <summary>Height of the climb results in the ride summary.</summary>
        private static float SummaryClimbsH(RideSummary s) => s.Climbs != null && s.Climbs.Count > 0 ? (s.Climbs.Count * 60) + 12 : 0;

        private float DrawSummaryClimbs(RideSummary s, float x, float y, float w)
        {
            if (s.Climbs == null || s.Climbs.Count == 0) return y;
            var records = ClimbRecords();
            foreach (var c in s.Climbs)
            {
                var row = new Rect(x, y, w, 52);
                Ui.Rect(row, Theme.Alpha(Theme.Accent, 0.14f), Theme.RadiusSmall);
                Ui.DrawIcon(new Rect(row.x + 16, row.center.y - 13, 26, 26), Icon.Timer, Theme.Accent);
                GUI.Label(new Rect(row.x + 54, row.y, w - 300, row.height), $"{c.name}: <b>{Ui.Clock(c.timeS)}</b>", Theme.Body);
                // The ride may or may not be in the history already: the best is this time or a slower one.
                bool best = !records.TryGetValue(c.id, out var rec) || c.timeS <= rec.BestS + 0.05;
                if (best) GUI.Label(new Rect(row.xMax - 260, row.y, 244, row.height), L.Get("new_best"), Theme.With(Theme.H3, Theme.Success, 17, TextAnchor.MiddleRight));
                y += 60;
            }

            return y + 12;
        }

        /// <summary>Height of the climbs card in the history (0 when no climb has been ridden).</summary>
        private static float HistoryClimbsH() => ClimbRecords().Count > 0 ? 150 : 0;

        private void DrawHistoryClimbs(float x, float y, float w)
        {
            var records = ClimbRecords();
            if (records.Count == 0) return;
            var card = new Rect(x, y, w, 150);
            Ui.Card(card);
            SectionTitle(card, L.Get("climbs"), Icon.Timer);
            float cw = (w - 48) / ClimbIds.Length;
            for (int i = 0; i < ClimbIds.Length; i++)
            {
                var data = ClimbData.Load(ClimbIds[i]);
                float cx = card.x + 24 + (i * cw);
                GUI.Label(new Rect(cx, card.y + 66, cw - 16, 24), data.Name, Clip(Theme.With(Theme.Body, size: 16)));
                if (records.TryGetValue(data.Id, out var rec))
                {
                    GUI.Label(new Rect(cx, card.y + 92, cw - 16, 34), $"{Ui.Clock(rec.BestS)} <size=14><color=#B3BECA>{string.Format(L.Get("ascents"), rec.Count)}</color></size>", Theme.With(Theme.H3, size: 24));
                }
                else
                {
                    GUI.Label(new Rect(cx, card.y + 92, cw - 16, 34), L.Get("no_ascent_yet"), Theme.BodyMuted);
                }
            }
        }

        /// <summary>Climbs of a stored ride, for the detail page's subtitle.</summary>
        private static string ClimbsText(RideRecord r) =>
            r.climbs == null || r.climbs.Length == 0 ? "" : " · " + string.Join(" · ", r.climbs.Select(c => $"{c.name} {Ui.Clock(c.timeS)}"));
    }
}
