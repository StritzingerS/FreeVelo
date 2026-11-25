using System;
using System.Collections.Generic;
using FreeVelo.World;
using Unity.Profiling;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>
    /// Measures how the phone copes, once a minute into the app log (devices page → "Share log"): average frame rate,
    /// 95th and 99th percentile frame time, the slowest frame, CPU and GPU time per frame where the device reports them,
    /// the scenery the world submitted (instances and draw calls of its own), the thermal state and the battery.
    /// docs/GRAPHICS_OVERHAUL.md section 6: frame rate, heat and battery can only be measured on the phone.
    /// </summary>
    public sealed class PerfMonitor : MonoBehaviour
    {
        private const float Period = 60f;

        private readonly List<float> _frames = new List<float>(4096);
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private double _cpu, _gpu;
        private int _timed;
        private float _since;
        private long _instances, _draws;
        private float _batteryStart = -1;
        private ProfilerRecorder _drawCalls, _triangles;
        private Action<string> _log;
        private Func<bool> _active;

        /// <summary>Starts measuring: <paramref name="log"/> takes the lines, <paramref name="active"/> says whether the world is up.</summary>
        public void Begin(Action<string> log, Func<bool> active)
        {
            _log = log;
            _active = active;
            // Render counters: only filled in development builds; the line then says "n/a".
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        private void OnDestroy()
        {
            _drawCalls.Dispose();
            _triangles.Dispose();
        }

        private void Update()
        {
            if (_log == null || _active == null || !_active()) return;
            _frames.Add(Time.unscaledDeltaTime * 1000f);
            _instances += WorldBuilder.InstancesDrawn;
            _draws += WorldBuilder.InstanceDrawCalls;

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timing) > 0 && _timing[0].cpuFrameTime > 0)
            {
                _cpu += _timing[0].cpuFrameTime;
                _gpu += _timing[0].gpuFrameTime;
                _timed++;
            }

            _since += Time.unscaledDeltaTime;
            if (_since < Period || _frames.Count < 10 || Benchmark.Running) return;
            Report("Perf 1 min");
        }

        /// <summary>Drops what was measured so far (the benchmark starts each section fresh).</summary>
        public void Restart()
        {
            _frames.Clear();
            _cpu = _gpu = 0;
            _timed = 0;
            _since = 0;
            _instances = _draws = 0;
            PerfTimes.TakeReport(1);
        }

        /// <summary>Writes one line for the frames since the last report or restart, starting with <paramref name="label"/>.</summary>
        public void Report(string label)
        {
            int n = _frames.Count;
            if (n == 0) return;
            float sum = 0;
            foreach (float f in _frames) sum += f;
            _frames.Sort();
            float P(double q) => _frames[Math.Min(n - 1, (int)(q * n))];
            float battery = SystemInfo.batteryLevel;
            if (_batteryStart < 0 && battery >= 0) _batteryStart = battery;
            string cpuGpu = _timed > 0 ? $" · CPU {_cpu / _timed:0.0} GPU {(_gpu > 0 ? (_gpu / _timed).ToString("0.0") : "n/a")} ms" : "";
            string render = _drawCalls.Valid && _drawCalls.LastValue > 0 ? $" · draw calls {_drawCalls.LastValue} · triangles {_triangles.LastValue / 1000}k" : "";
            string battery100 = battery >= 0 ? $" · battery {battery * 100:0}% ({(_batteryStart - battery) * 100:+0;-0;0} since start)" : "";
            string parts = PerfTimes.TakeReport(n);
            if (parts.Length > 0) parts = $" · CPU ms/frame [{parts}]";
            _log($"{label}:{1000f * n / sum:0} fps avg · frame p95 {P(0.95):0.0} p99 {P(0.99):0.0} max {_frames[n - 1]:0} ms{cpuGpu}"
                 + $" · scenery {_instances / n} instances in {_draws / n} calls{render} · grass {FreeVelo.World.GrassField.Drawn} · quality {(AppSettings.Quality == GraphicsQuality.Low ? "low" : "high")} at {GraphicsQuality.RenderScale * 100:0} %{parts} · thermal {Thermal()}{battery100}");
            _frames.Clear();
            _cpu = _gpu = 0;
            _timed = 0;
            _since = 0;
            _instances = _draws = 0;
        }

        /// <summary>Android's thermal status (0 none … 6 shutdown), or "n/a".</summary>
        private static string Thermal()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var power = activity.Call<AndroidJavaObject>("getSystemService", "power");
                int status = power.Call<int>("getCurrentThermalStatus");
                string[] names = { "none", "light", "moderate", "severe", "critical", "emergency", "shutdown" };
                return status >= 0 && status < names.Length ? names[status] : status.ToString();
            }
            catch (Exception)
            {
                return "n/a";
            }
#else
            return "n/a";
#endif
        }
    }
}
