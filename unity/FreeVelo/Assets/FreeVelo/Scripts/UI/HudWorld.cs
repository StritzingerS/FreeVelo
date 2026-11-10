using System.Linq;
using FreeVelo.App;
using FreeVelo.Core.Physics;
using FreeVelo.Core.Ui;
using FreeVelo.Core.World.Gen;
using FreeVelo.World;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>
    /// The road network in the interface: the junction card (left / straight / right), the elevation ahead, and a
    /// note while the world is still being generated.
    /// </summary>
    public sealed partial class Hud
    {
        private WorldBuilder _world;

        public void InitializeWorld(WorldBuilder world) => _world = world;

        private Texture2D _splashLogo;
        private float _worldBuiltAt = -1;
        private const float LoadingFadeS = 0.45f;

        /// <summary>
        /// Start-up: a loading screen like the splash screen (same background and logo) covers the app until terrain,
        /// scenery and the mountain are built, then fades out. Returns true while it hides everything behind it.
        /// </summary>
        private bool DrawLoadingScreen()
        {
            if (_world == null) return false;
            float alpha = 1;
            if (_world.IsLoaded)
            {
                if (_worldBuiltAt < 0) _worldBuiltAt = Time.unscaledTime;
                alpha = 1 - ((Time.unscaledTime - _worldBuiltAt) / LoadingFadeS);
                if (alpha <= 0) return false;
            }

            // Ui.Rect takes its own colour (GUI.color does not apply): the alpha is set on each colour.
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            Ui.Rect(_full, Theme.Alpha(Theme.Background, alpha), 0);

            _splashLogo ??= Resources.Load<Texture2D>("FreeVeloSplash");
            float logoW = Mathf.Min(_w * 0.6f, 720);
            float logoH = _splashLogo != null ? logoW * _splashLogo.height / _splashLogo.width : 0;
            var logo = new Rect((_w - logoW) / 2f, (_h - logoH) / 2f - 30, logoW, logoH);
            if (_splashLogo != null) GUI.DrawTexture(logo, _splashLogo, ScaleMode.ScaleToFit);

            // Indeterminate progress: a short bar sweeping left to right under the logo (in at the left edge, out
            // at the right, again), eased so it speeds up and slows down.
            float barW = Mathf.Min(320, _w * 0.4f);
            var track = new Rect((_w - barW) / 2f, logo.yMax + 36, barW, 6);
            Ui.Rect(track, Theme.Alpha(Theme.Surface2, alpha), 3);
            float t = Mathf.Repeat(Time.unscaledTime / 1.3f, 1f);
            t = t * t * (3 - (2 * t));
            float seg = barW * 0.3f;
            float x0 = Mathf.Max(track.x, track.x - seg + ((barW + seg) * t));
            float x1 = Mathf.Min(track.xMax, track.x + ((barW + seg) * t));
            if (x1 > x0) Ui.Rect(new Rect(x0, track.y, x1 - x0, track.height), Theme.Alpha(Theme.Accent, alpha), 3);
            GUI.Label(new Rect(0, track.yMax + 14, _w, 26), L.Get(FreeVelo.App.FreeVeloApp.LoadingStep), Theme.With(Theme.Body, Theme.Muted, 16, TextAnchor.MiddleCenter));

            GUI.color = old;
            return !_world.IsLoaded; // during the fade the app is already usable
        }

        // ------------------------------------------------------------------ junctions

        /// <summary>Solo rides: the next junction (when close) with the ways on; the chosen one is highlighted.</summary>
        private void DrawJunctionCard(bool workout)
        {
            var junction = _ride.NextJunction();
            if (junction == null || junction.Options.Count < 2) return;
            double ahead = junction.DistanceM - _ride.DistanceM;
            if (ahead > 400 || ahead < 0) return;

            var card = workout ? new Rect(24, _leftTop, 320, 0) : new Rect(_w - 344, _sideTop, 320, 0);
            card.height = 64 + (junction.Options.Count * 62);
            Ui.Card(card, Theme.Alpha(Theme.Background, 0.9f));
            GUI.Label(new Rect(card.x + 20, card.y + 14, card.width - 40, 20), L.Get("junction").ToUpperInvariant(), Theme.Overline);
            GUI.Label(new Rect(card.x + 20, card.y + 30, card.width - 40, 28), $"{L.Get("in")} {ahead:0} m", Theme.With(Theme.H3, size: 20));

            float y = card.y + 64;
            for (int i = 0; i < junction.Options.Count; i++)
            {
                var o = junction.Options[i];
                bool chosen = i == junction.Chosen;
                var b = new Rect(card.x + 14, y, card.width - 28, 54);
                Ui.Rect(b, chosen ? Theme.Accent : Theme.Surface2, Theme.RadiusSmall);
                var icon = o.TurnDegrees < -25 ? Icon.TurnLeft : o.TurnDegrees > 25 ? Icon.TurnRight : Icon.Straight;
                var fg = chosen ? Color.white : Theme.Text;
                Ui.DrawIcon(new Rect(b.x + 14, b.center.y - 14, 28, 28), icon, fg);
                // Where the road leads: the next village, or the climb's name.
                var map = _ride.Map;
                var edge = map?.Edges[o.Edge.Edge];
                string name = edge == null ? L.Get("side_road")
                    : edge.Kind == FreeVelo.Core.World.Gen.RoadKind.Climb ? map.Mountain?.Find(edge.ClimbId)?.Data.Name ?? edge.ClimbId
                    : map.Nodes[o.Edge.Forward ? edge.To : edge.From].Name;
                GUI.Label(new Rect(b.x + 54, b.y + 6, b.width - 64, 24), name, Theme.With(Theme.Body, fg, 16, TextAnchor.MiddleLeft));
                GUI.Label(new Rect(b.x + 54, b.y + 28, b.width - 64, 20), $"{o.LengthM / 1000:0.0} km {L.Get("to_next_junction")}", Theme.With(Theme.Caption, chosen ? new Color(1, 1, 1, 0.8f) : Theme.Muted, 13, TextAnchor.MiddleLeft));
                if (GUI.Button(b, GUIContent.none, GUIStyle.none)) _ride.ChooseTurn(i);
                y += 62;
            }
        }

        // ------------------------------------------------------------------ route strip

        /// <summary>Bottom strip in free rides: elevation of the next 3 km (or of the main loop in group rides).</summary>
        /// <summary>Name of a round shown over the elevation strip: the main loop or the round over a climb.</summary>
        private static string RouteLabel(FreeVelo.Core.World.Route route) =>
            route.Climbs != null && route.Climbs.Count > 0 ? string.Format(L.Get("climb_loop"), route.Climbs[0].Name)
            : route.Name == FreeVelo.Core.World.Gen.WorldMap.AlmRouteName ? L.Get("alm_loop") : L.Get("main_loop");

        private FreeVelo.Core.World.Route _aheadRoute;
        private long _aheadFirst;
        private double[] _aheadSamples;

        private FreeVelo.Core.World.Route _climbedRoute;
        private double[] _climbed;

        /// <summary>Metres climbed from the start of a round, every 10 m (worked out once per route).</summary>
        private double[] ClimbedAlong(FreeVelo.Core.World.Route route)
        {
            if (_climbedRoute == route) return _climbed;
            int n = (int)(route.LengthM / 10) + 1;
            var climbed = new double[n];
            double last = route.Sample(0).Y;
            for (int i = 1; i < n; i++)
            {
                double y = route.Sample(i * 10.0).Y;
                climbed[i] = climbed[i - 1] + System.Math.Max(0, y - last);
                last = y;
            }

            _climbedRoute = route;
            _climbed = climbed;
            return climbed;
        }

        /// <summary>Right-aligned summary in the strip's title row.</summary>
        private static void StripCorner(Rect card, string text) =>
            GUI.Label(new Rect(card.xMax - 418, card.y + 6, 400, 22), text, Theme.With(Theme.Caption, Theme.Text, anchor: TextAnchor.MiddleRight));

        /// <summary>Climb view window: a little behind, 500 m ahead.</summary>
        private const double ClimbBehindM = 50, ClimbAheadM = 500;

        private void DrawRideStrip()
        {
            var route = _ride.Route;
            if (route == null) return;
            double d = _ride.DistanceM;
            // Free ride: the way ahead including the default turns beyond the next junction.
            var ahead = !route.IsLoop && _ride.Path != null && _ride.Route == _ride.Path.Route ? _ride.Path.Preview(d + ClimbAheadM + 100) : route;
            double to = route.IsLoop ? d + ClimbAheadM : System.Math.Min(d + ClimbAheadM, ahead.LengthM);
            var (up, _) = Climbing(ahead, d, to);
            double avg = to > d ? (ahead.Sample(to).Y - ahead.Sample(d).Y) / (to - d) * 100 : 0;
            bool climbing = _ride.ActiveClimb != null || avg >= 3;

            // Free ride: only on climbs. Rounds (main loop, climb rounds): the round overview when not climbing.
            if (!climbing && !route.IsLoop) return;

            var card = new Rect(24, _h - 120, BottomRowWidth, 96);
            Ui.Card(card, Theme.Alpha(Theme.Background, 0.85f));
            var graph = new Rect(card.x + 16, card.y + 30, card.width - 32, card.height - 40);
            if (climbing)
            {
                string title = _ride.ActiveClimb != null ? $"{_ride.ActiveClimb.Name} · {L.Get("next_500m")}" : L.Get("next_500m");
                GUI.Label(new Rect(card.x + 18, card.y + 6, 600, 22), title, Theme.Caption);
                StripCorner(card, $"<color=#4ADE80>↑</color> <b>{up:0} m</b>   Ø <b>{System.Math.Max(0, avg):0.0} %</b>");
                DrawClimbProfile(graph, ahead, d);
            }
            else
            {
                GUI.Label(new Rect(card.x + 18, card.y + 6, 600, 22), $"{RouteLabel(route)} · {route.LengthM / 1000:0.0} km", Theme.Caption);
                // Corner: climbing still to come in this round, of the whole round.
                double pos = d % route.LengthM;
                var climbed = ClimbedAlong(route);
                double total = climbed[climbed.Length - 1];
                double done = climbed[Mathf.Clamp((int)(pos / 10), 0, climbed.Length - 1)];
                StripCorner(card, string.Format(L.Get("ascent_left"), $"<color=#4ADE80>↑</color> <b>{total - done:0} m</b>", $"{total:0} m"));
                DrawElevation(graph, route, pos);
            }
        }

        /// <summary>Gradient colours as on Garmin's ClimbPro.</summary>
        private static Color ClimbColor(double gradePercent) =>
            gradePercent < 3 ? new Color(0.30f, 0.75f, 0.35f)
            : gradePercent < 6 ? new Color(0.98f, 0.82f, 0.18f)
            : gradePercent < 9 ? new Color(0.98f, 0.55f, 0.12f)
            : gradePercent < 12 ? new Color(0.90f, 0.20f, 0.18f)
            : new Color(0.55f, 0.08f, 0.10f);

        /// <summary>
        /// The next 500 m (and a little behind), coloured by gradient like ClimbPro; the ridden part dimmed. Beyond the
        /// next junction the way the rider would take by default (or has chosen there).
        /// </summary>
        private void DrawClimbProfile(Rect r, FreeVelo.Core.World.Route route, double distance)
        {
            double from = distance - ClimbBehindM, to = distance + ClimbAheadM;
            double end = route.IsLoop ? double.MaxValue : route.LengthM;
            // Beyond the next junction the way on can still change: drawn a little fainter.
            var upcoming = _ride.NextJunction();
            double fixedUntil = upcoming != null ? upcoming.DistanceM : end;

            // Samples every 5 m tied to the road (not to the screen); worked out again only when the rider has moved
            // on by one sample or the way ahead changed, so the outline scrolls smoothly.
            const double step = 5;
            // 30 m beyond both ends: the colour of each column uses the gradient over ±10 m, so the edge columns
            // need road beyond the window (else they read too flat and showed the next easier colour).
            long first = (long)System.Math.Floor(from / step) - 6;
            int count = (int)((to - from) / step) + 14;
            if (_aheadRoute != route || _aheadFirst != first || _aheadSamples == null || _aheadSamples.Length != count)
            {
                _aheadSamples = ProfileSamples(route, first * step, step, count, end);
                _aheadRoute = route;
                _aheadFirst = first;
            }

            double min = double.MaxValue, max = double.MinValue;
            for (double d = from; d <= to; d += step)
            {
                double y = SampleAt(_aheadSamples, first * step, step, d);
                min = System.Math.Min(min, y);
                max = System.Math.Max(max, y);
            }

            double span = System.Math.Max(MinAheadSpanM, max - min);
            var samples = _aheadSamples;
            double s0 = first * step;
            ProfileColumns(r, from, to, samples, s0, step, min, span, distance, d =>
            {
                // Gradient over ±10 m, from the samples
                double g = (SampleAt(samples, s0, step, d + 10) - SampleAt(samples, s0, step, d - 10)) / 20 * 100;
                var c = ClimbColor(g);
                return d > end ? Color.Lerp(Theme.Background, c, 0.25f) : d > fixedUntil ? Color.Lerp(Theme.Background, c, 0.6f) : c;
            }, 0.45f);
            float px = r.x + (r.width * (float)((distance - from) / (to - from)));
            Ui.Rect(new Rect(px - 1.5f, r.y, 3, r.height), Theme.Text, 1);

            // Junction marker: a dashed yellow line.
            if (upcoming != null && upcoming.DistanceM < to)
            {
                float jx = r.x + (r.width * (float)((upcoming.DistanceM - from) / (to - from)));
                var color = Theme.Alpha(Theme.Warning, 0.95f);
                for (float y = r.y; y < r.yMax; y += 9) Ui.Rect(new Rect(jx - 1, y, 2, Mathf.Min(5, r.yMax - y)), color, 1);
            }
        }
    }
}
