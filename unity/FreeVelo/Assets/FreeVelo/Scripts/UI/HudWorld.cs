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

        /// <summary>While terrain and scenery are generated (a few seconds after the start).</summary>
        private void DrawWorldLoading()
        {
            if (_world == null || _world.IsBuilt) return;
            var style = Theme.With(Theme.Body, Theme.Text, 17, TextAnchor.MiddleCenter);
            string text = L.Get("loading_world");
            float w = style.CalcSize(new GUIContent(text)).x + 56;
            var r = new Rect(_w - w - 24, _h - 72, w, 48);
            Ui.Card(r, Theme.Alpha(Theme.Background, 0.9f), 24);
            float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 4f));
            Ui.Dot(new Vector2(r.x + 22, r.center.y), 6, Theme.Alpha(Theme.Accent, 0.4f + (0.6f * pulse)));
            GUI.Label(new Rect(r.x + 18, r.y, r.width - 18, r.height), text, style);
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
                GUI.Label(new Rect(card.x + 18, card.y + 6, 600, 22), $"{L.Get("main_loop")} · {route.LengthM / 1000:0.0} km", Theme.Caption);
                DrawElevation(graph, route, d % route.LengthM);
            }
            else
            {
                GUI.Label(new Rect(card.x + 18, card.y + 6, 600, 22), L.Get("ahead_3km"), Theme.Caption);
                DrawElevationAhead(graph, route, d);
            }
        }

        /// <summary>Profile from 300 m behind to 3 km ahead (as far as the way is known: up to the next junction).</summary>
        private void DrawElevationAhead(Rect r, FreeVelo.Core.World.Route route, double distance)
        {
            const int n = 90;
            double from = distance - 300, to = distance + 3000;
            double end = route.LengthM;
            double min = double.MaxValue, max = double.MinValue;
            var e = new double[n];
            for (int i = 0; i < n; i++)
            {
                double at = from + ((to - from) * i / (n - 1));
                e[i] = route.Sample(System.Math.Min(at, end)).Y;
                min = System.Math.Min(min, e[i]);
                max = System.Math.Max(max, e[i]);
            }

            double span = System.Math.Max(20, max - min);
            float bw = r.width / n;
            for (int i = 0; i < n; i++)
            {
                double at = from + ((to - from) * i / (n - 1));
                float h = 8 + (float)((e[i] - min) / span) * (r.height - 8);
                bool done = at <= distance;
                float alpha = at > end ? 0.12f : done ? 0.75f : 0.35f;
                Ui.Rect(new Rect(r.x + (i * bw) - 0.6f, r.yMax - h, bw + 1.2f, h), Theme.Alpha(done ? Theme.Accent : Theme.Info, alpha), 0);
            }

            float px = r.x + (r.width * (float)(300 / (to - from)));
            Ui.Rect(new Rect(px - 1.5f, r.y, 3, r.height), Theme.Text, 1);

            // Junction marker.
            var junction = _ride.NextJunction();
            if (junction != null && junction.DistanceM < to)
            {
                float jx = r.x + (r.width * (float)((junction.DistanceM - from) / (to - from)));
                Ui.Rect(new Rect(jx - 1, r.y, 2, r.height), Theme.Alpha(Theme.Warning, 0.9f), 1);
            }
        }
    }
}
