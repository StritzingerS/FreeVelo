using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace FreeVelo.App
{
    /// <summary>
    /// Main-thread time of the world's per-frame work (grass, scenery, riders, smoke, wind), summed up and
    /// written as milliseconds per frame into the perf line of the app log (<see cref="PerfMonitor"/>), so a phone log
    /// shows which part costs how much. Measure with <c>using (PerfTimes.Measure("grass")) { … }</c>.
    /// </summary>
    public static class PerfTimes
    {
        private static readonly Dictionary<string, long> Ticks = new Dictionary<string, long>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Ticks.Clear();

        public readonly struct Scope : System.IDisposable
        {
            private readonly string _name;
            private readonly long _start;

            public Scope(string name)
            {
                _name = name;
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                long elapsed = Stopwatch.GetTimestamp() - _start;
                Ticks.TryGetValue(_name, out long sum);
                Ticks[_name] = sum + elapsed;
            }
        }

        public static Scope Measure(string name) => new Scope(name);

        /// <summary>"grass 0.8, scenery 1.9, …" in ms per frame over <paramref name="frames"/> frames; then starts over.</summary>
        public static string TakeReport(int frames)
        {
            if (frames <= 0 || Ticks.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var pair in Ticks)
            {
                double ms = pair.Value * 1000.0 / Stopwatch.Frequency / frames;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(pair.Key).Append(' ').Append(ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }

            Ticks.Clear();
            return sb.ToString();
        }
    }
}
