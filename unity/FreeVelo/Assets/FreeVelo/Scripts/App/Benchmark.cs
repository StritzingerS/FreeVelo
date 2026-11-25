using System;
using System.Collections.Generic;
using FreeVelo.Core.World;
using FreeVelo.World;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>
    /// A ride without a trainer for measuring the phone (devices page → log → "Benchmark"): the rider rolls at a
    /// steady 32 km/h through four stretches of the route picked from the world itself – the densest scenery (a
    /// village or forest), the emptiest, the steepest climb and the highest point (wide view) – about half a minute
    /// each, and every stretch writes its own perf line into the app log (same fields as the minute lines).
    /// The first seconds of a stretch (camera cut, streaming) are not measured; the ride HUD is not drawn.
    /// </summary>
    public sealed class Benchmark : MonoBehaviour
    {
        private const double SpeedMps = 9.0, StretchM = 300.0, WarmupS = 3.0;

        public static Benchmark Instance { get; private set; }

        /// <summary>While running: where the rider is on the route (FreeVeloApp places rider and camera there).</summary>
        public static double? DistanceM { get; private set; }

        public static bool Running => DistanceM.HasValue;

        /// <summary>
        /// The route of the run: the Alm round (valley villages, the hairpin climb, the plateau, open valley road). The
        /// menu's own route is only the short way to the next junctions (the first run, 25 Nov 2026, squeezed all four
        /// stretches into its first 2 km).
        /// </summary>
        public static Route CurrentRoute { get; private set; }

        /// <summary>For the overlay: "2/4 · Dorf", progress 0–1.</summary>
        public static string Status { get; private set; } = "";
        public static float Progress { get; private set; }

        public const float CadenceRpm = 85f, Intensity = 0.7f;
        public static float Speed => (float)SpeedMps;

        private Func<Route> _route;
        private WorldBuilder _world;
        private PerfMonitor _perf;
        private Action<string> _log;
        private readonly List<(string Name, string Key, double FromM)> _stretches = new List<(string, string, double)>();
        private int _index;
        private double _travelled;
        private bool _measuring;

        public void Begin(Func<Route> route, WorldBuilder world, PerfMonitor perf, Action<string> log)
        {
            _route = route;
            _world = world;
            _perf = perf;
            _log = log;
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DistanceM = null;
        }

        /// <summary>Starts the run (only from the menus, with the world built).</summary>
        public void Run()
        {
            var route = BuiltInRoutes.Get(BuiltInRoutes.AlmId) ?? _route?.Invoke();
            if (route == null || _world == null || !_world.IsBuilt || Running) return;
            CurrentRoute = route;
            PickStretches(route);
            if (_stretches.Count == 0) return;
            _log($"Benchmark: {_stretches.Count} stretches of {StretchM:0} m at {SpeedMps * 3.6:0} km/h on the Alm round ({route.LengthM / 1000:0.0} km) · quality {(AppSettings.Quality == GraphicsQuality.Low ? "low" : "high")} · {SystemInfo.deviceModel}");
            _index = 0;
            BeginStretch();
        }

        public void Cancel()
        {
            if (!Running) return;
            _log("Benchmark cancelled");
            DistanceM = null;
            _perf?.Restart();
        }

        private void BeginStretch()
        {
            _travelled = 0;
            _measuring = false;
            DistanceM = _stretches[_index].FromM;
            Status = $"{_index + 1}/{_stretches.Count} · {L.Get(_stretches[_index].Key)}";
            Progress = 0;
        }

        private void Update()
        {
            if (!Running) return;
            double dt = Math.Min(Time.unscaledDeltaTime, 0.1);
            _travelled += SpeedMps * dt;
            DistanceM = _stretches[_index].FromM + _travelled;
            Progress = (float)(_travelled / (StretchM + (SpeedMps * WarmupS)));

            if (!_measuring && _travelled >= SpeedMps * WarmupS)
            {
                _measuring = true;
                _perf?.Restart();
            }

            if (_travelled < StretchM + (SpeedMps * WarmupS)) return;
            var s = _stretches[_index];
            _perf?.Report($"Bench {_index + 1}/{_stretches.Count} {s.Name} (km {s.FromM / 1000:0.0})");
            _index++;
            if (_index < _stretches.Count)
            {
                BeginStretch();
                return;
            }

            _log("Benchmark done");
            DistanceM = null;
            FreeVelo.UI.Ui.Toast("✓ " + L.Get("bench_done"));
        }

        /// <summary>The four stretches, at least 600 m apart, from samples every 50 m along the route.</summary>
        private void PickStretches(Route route)
        {
            _stretches.Clear();
            double length = route.LengthM, run = StretchM + (SpeedMps * WarmupS);
            var samples = new List<(double D, int Items, float Y, double Climb)>();
            for (double d = 0; d + run < length; d += 50)
            {
                var p = route.Sample(d + (run / 2));
                var end = route.Sample(d + run);
                var start = route.Sample(d);
                double climb = (end.Y - start.Y) / run;
                samples.Add((d, _world.ItemsNear((float)p.X, (float)p.Z, 150f), (float)p.Y, climb));
            }

            if (samples.Count == 0) return;
            var taken = new List<double>();
            bool Free(double d)
            {
                foreach (double t in taken)
                {
                    if (Math.Abs(t - d) < 600) return false;
                }

                return true;
            }

            void Take(string name, string key, Func<(double D, int Items, float Y, double Climb), double> score)
            {
                double best = double.MinValue, at = -1;
                foreach (var s in samples)
                {
                    if (!Free(s.D)) continue;
                    double v = score(s);
                    if (v > best)
                    {
                        best = v;
                        at = s.D;
                    }
                }

                if (at < 0) return;
                taken.Add(at);
                _stretches.Add((name, key, at));
            }

            Take("dense", "bench_dense", s => s.Items);
            Take("climb", "bench_climb", s => s.Climb);
            Take("high view", "bench_view", s => s.Y);
            Take("open", "bench_open", s => -s.Items);
        }
    }
}
