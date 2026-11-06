using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>
    /// A famous climb copied from a GPX track: one point every 5 m, positions relative to the summit, real elevations
    /// (see tools/climbs/process_gpx.py). The world places it on its mountain; length, elevation, gradients and every
    /// hairpin stay as they are – only the whole climb is turned around its summit and lifted to the world's height.
    /// </summary>
    public sealed class ClimbData
    {
        public const string Stelvio = "stelvio";
        public const string AlpeDHuez = "alpe-dhuez";

        private ClimbData(string id, string name, double[] x, double[] z, double[] e)
        {
            Id = id;
            Name = name;
            X = x;
            Z = z;
            E = e;
            D = new double[x.Length];
            for (int i = 1; i < x.Length; i++) D[i] = D[i - 1] + Math.Sqrt(((x[i] - x[i - 1]) * (x[i] - x[i - 1])) + ((z[i] - z[i - 1]) * (z[i] - z[i - 1])));
        }

        public string Id { get; }

        public string Name { get; }

        /// <summary>Metres east of the summit (foot first, summit last).</summary>
        public double[] X { get; }

        /// <summary>Metres north of the summit.</summary>
        public double[] Z { get; }

        /// <summary>Real elevation in metres.</summary>
        public double[] E { get; }

        /// <summary>Distance from the foot.</summary>
        public double[] D { get; }

        public double Length => D[D.Length - 1];

        public double Gain => E[E.Length - 1] - E[0];

        /// <summary>Compass direction (radians, 0 = north, clockwise) from the summit to the foot.</summary>
        public double FootBearing => Math.Atan2(X[0], Z[0]);

        private static readonly Dictionary<string, ClimbData> Cache = new Dictionary<string, ClimbData>();

        public static ClimbData Load(string id)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(id, out var cached)) return cached;
                using var stream = typeof(ClimbData).Assembly.GetManifestResourceStream($"FreeVelo.Climbs.{id}.csv")
                    ?? throw new ArgumentException($"Unknown climb {id}");
                using var reader = new StreamReader(stream);
                string name = id;
                var x = new List<double>();
                var z = new List<double>();
                var e = new List<double>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("#", StringComparison.Ordinal))
                    {
                        if (name == id) name = line.Substring(1).Trim();
                        continue;
                    }

                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    x.Add(double.Parse(parts[0], CultureInfo.InvariantCulture));
                    z.Add(double.Parse(parts[1], CultureInfo.InvariantCulture));
                    e.Add(double.Parse(parts[2], CultureInfo.InvariantCulture));
                }

                var climb = new ClimbData(id, name, x.ToArray(), z.ToArray(), e.ToArray());
                Cache[id] = climb;
                return climb;
            }
        }
    }
}
