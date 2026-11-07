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
            GUI.Label(new Rect(0, track.yMax + 14, _w, 26), L.Get("loading_world"), Theme.With(Theme.Body, Theme.Muted, 16, TextAnchor.MiddleCenter));

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
                string name = o.IsMainLoop ? L.Get("main_loop") : o.Surface == RoadSurface.Asphalt ? L.Get("side_road") : L.Get("gravel_road");
                var mountain = _ride.Map?.Mountain;
                if (mountain != null && o.Edge.Edge == mountain.AccessStelvioEdge) name = "Passo dello Stelvio";
                else if (mountain != null && o.Edge.Edge == mountain.AccessAlpeEdge) name = "Alpe d'Huez";
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
            route.Climbs != null && route.Climbs.Count > 0 ? string.Format(L.Get("climb_loop"), route.Climbs[0].Name) : L.Get("main_loop");

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

        private void DrawRideStrip()
        {
            var route = _ride.Route;
            if (route == null) return;
            var card = new Rect(24, _h - 120, _w - 48, 96);
            Ui.Card(card, Theme.Alpha(Theme.Background, 0.85f));
            var graph = new Rect(card.x + 16, card.y + 30, card.width - 32, card.height - 40);

            double d = _ride.DistanceM;
            if (route.IsLoop)
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
            else
            {
                GUI.Label(new Rect(card.x + 18, card.y + 6, 600, 22), L.Get("ahead_3km"), Theme.Caption);
                // Beyond the next junction: the way the rider would take by default (or has chosen there).
                var ahead = _ride.Path != null && _ride.Route == _ride.Path.Route ? _ride.Path.Preview(d + 3000) : route;
                // Corner: metres up and down in the next 3 km.
                var (up, down) = Climbing(ahead, d, System.Math.Min(d + 3000, ahead.LengthM));
                StripCorner(card, $"<color=#4ADE80>↑</color> <b>{up:0} m</b>   <color=#60A5FA>↓</color> <b>{down:0} m</b>");
                DrawElevationAhead(graph, ahead, d);
            }
        }

        /// <summary>Profile from 300 m behind to 3 km ahead (as far as the way is known: up to the next junction).</summary>
        private void DrawElevationAhead(Rect r, FreeVelo.Core.World.Route route, double distance)
        {
            int n = ProfileBars(r);
            double from = distance - 300, to = distance + 3000;
            double end = route.LengthM;
            // Beyond the next junction the way on can still change: drawn a little fainter.
            var upcoming = _ride.NextJunction();
            double fixedUntil = upcoming != null ? upcoming.DistanceM : end;
            // Bars belong to fixed stretches of road (not to fixed places on screen), so they scroll smoothly with the
            // rider – together with the junction sign – instead of changing height in steps.
            double step = (to - from) / n;
            long first = (long)System.Math.Floor(from / step);
            int m = n + 2;
            var e = ProfileHeights(route, first * step, (first + m) * step, m, end);
            double min = e.Min(), max = e.Max();
            double span = System.Math.Max(MinAheadSpanM, max - min);
            float px = r.x + (r.width * (float)((distance - from) / (to - from)));
            float scale = r.width / (float)(to - from);
            for (int j = 0; j < m; j++)
            {
                double d0 = (first + j) * step;
                double at = d0 + (step / 2); // middle of the bar's stretch
                float h = 8 + (float)((e[j] - min) / span) * (r.height - 8);
                float alpha = at > end ? 0.12f : at > fixedUntil ? 0.22f : 0.38f;
                float x0 = r.x + ((float)(d0 - from) * scale);
                ProfileBar(r, x0, x0 + ((float)step * scale), h, px, Theme.Alpha(Theme.Info, alpha));
            }

            Ui.Rect(new Rect(px - 1.5f, r.y, 3, r.height), Theme.Text, 1);

            // Junction marker: a dashed line with the side-road sign symbol on top – side road left, right or both,
            // as the junction branches off.
            if (upcoming != null && upcoming.DistanceM < to)
            {
                float jx = r.x + (r.width * (float)((upcoming.DistanceM - from) / (to - from)));
                var color = Theme.Alpha(Theme.Warning, 0.95f);
                for (float y = r.y + 20; y < r.yMax; y += 9) Ui.Rect(new Rect(jx - 1, y, 2, Mathf.Min(5, r.yMax - y)), color, 1);
                bool left = upcoming.Options.Any(o => o.TurnDegrees < -25), right = upcoming.Options.Any(o => o.TurnDegrees > 25);
                var symbol = left && !right ? Icon.SideRoadLeft : right && !left ? Icon.SideRoadRight : Icon.SideRoadBoth;
                // One colour, like the line: just the roads, as big as the triangle was.
                Ui.DrawIcon(new Rect(jx - 11, r.y - 4, 22, 22), symbol, color);
            }
        }
    }
}
