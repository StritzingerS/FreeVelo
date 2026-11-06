using System;
using System.Collections.Generic;
using FreeVelo.Core.Physics;

namespace FreeVelo.Core.World
{
    /// <summary>Point of a route; X/Z horizontal (metres), Y = elevation.</summary>
    public readonly struct RoutePoint
    {
        public RoutePoint(double distanceM, double x, double z, double elevationM, double gradePercent, RoadSurface surface)
        {
            DistanceM = distanceM;
            X = x;
            Z = z;
            ElevationM = elevationM;
            GradePercent = gradePercent;
            Surface = surface;
        }

        public double DistanceM { get; }

        public double X { get; }

        public double Z { get; }

        public double ElevationM { get; }

        public double GradePercent { get; }

        public RoadSurface Surface { get; }
    }

    /// <summary>Interpolated position on a route.</summary>
    public readonly struct RouteSample
    {
        public RouteSample(double x, double y, double z, double headingRad, double gradePercent, RoadSurface surface)
        {
            X = x;
            Y = y;
            Z = z;
            HeadingRad = headingRad;
            GradePercent = gradePercent;
            Surface = surface;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        /// <summary>Direction of travel; 0 = +Z, π/2 = +X.</summary>
        public double HeadingRad { get; }

        public double GradePercent { get; }

        public RoadSurface Surface { get; }
    }

    /// <summary>A rideable route: evenly spaced points with elevation, grade and surface.</summary>
    public sealed class Route
    {
        public Route(string name, IReadOnlyList<RoutePoint> points, bool isLoop)
        {
            if (points.Count < 2) throw new ArgumentException("A route needs at least two points.", nameof(points));
            Name = name;
            Points = points;
            IsLoop = isLoop;
        }

        public string Name { get; }

        public IReadOnlyList<RoutePoint> Points { get; }

        public bool IsLoop { get; }

        public double LengthM => Points[Points.Count - 1].DistanceM;

        public double TotalAscentM
        {
            get
            {
                double ascent = 0;
                for (int i = 1; i < Points.Count; i++) ascent += Math.Max(0, Points[i].ElevationM - Points[i - 1].ElevationM);
                return ascent;
            }
        }

        /// <summary>Position at <paramref name="distanceM"/>; loops wrap around, open routes are clamped.</summary>
        public RouteSample Sample(double distanceM)
        {
            // Open routes may start at any distance (a path through the road network): clamp to their range.
            double d = IsLoop ? Mod(distanceM, LengthM) : Math.Max(Points[0].DistanceM, Math.Min(LengthM, distanceM));

            int lo = 0, hi = Points.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Points[mid].DistanceM <= d) lo = mid;
                else hi = mid;
            }

            RoutePoint a = Points[lo], b = Points[hi];
            double span = b.DistanceM - a.DistanceM;
            double f = span > 0 ? (d - a.DistanceM) / span : 0;
            double heading = Math.Atan2(b.X - a.X, b.Z - a.Z);
            return new RouteSample(
                Lerp(a.X, b.X, f),
                Lerp(a.ElevationM, b.ElevationM, f),
                Lerp(a.Z, b.Z, f),
                heading,
                Lerp(a.GradePercent, b.GradePercent, f),
                f < 0.5 ? a.Surface : b.Surface);
        }

        /// <summary>
        /// Horizontal curvature (1/m, positive = right-hand bend) at <paramref name="distanceM"/>, from the heading
        /// change over ±<paramref name="windowM"/> metres (smooths the polyline corners).
        /// </summary>
        public double CurvatureAt(double distanceM, double windowM = 10)
        {
            double h0 = Sample(distanceM - windowM).HeadingRad;
            double h1 = Sample(distanceM + windowM).HeadingRad;
            double dh = h1 - h0;
            while (dh > Math.PI) dh -= 2 * Math.PI;
            while (dh < -Math.PI) dh += 2 * Math.PI;
            return dh / (2 * windowM);
        }

        /// <summary>
        /// Lean angle (degrees, positive = into a right-hand bend) a rider needs at <paramref name="speedMps"/> in a
        /// bend of curvature <paramref name="curvature"/>: tan(lean) = v²·κ / g, limited to ±35°.
        /// </summary>
        public static double LeanAngleDeg(double speedMps, double curvature)
        {
            double lean = Math.Atan(speedMps * speedMps * curvature / 9.81) * 180 / Math.PI;
            return Math.Max(-35, Math.Min(35, lean));
        }

        private static double Lerp(double a, double b, double f) => a + ((b - a) * f);

        private static double Mod(double x, double m) => ((x % m) + m) % m;
    }

    /// <summary>Generates smooth, closed fictional routes (used until GPX import exists).</summary>
    public static class ProceduralRoutes
    {
        /// <summary>
        /// Creates a closed loop of about <paramref name="lengthM"/> metres.
        /// <paramref name="hilliness"/> 0 = flat, 1 = rolling hills (max. ~8 %).
        /// </summary>
        public static Route Generate(int seed, double lengthM = 10_000, double hilliness = 0.5, double spacingM = 5)
        {
            var rng = new Random(seed);
            const int harmonics = 5;
            var shapeAmp = new double[harmonics];
            var shapePhase = new double[harmonics];
            var hillAmp = new double[harmonics + 3];
            var hillPhase = new double[harmonics + 3];
            for (int k = 0; k < harmonics; k++)
            {
                shapeAmp[k] = (0.18 / (k + 1)) * rng.NextDouble();
                shapePhase[k] = rng.NextDouble() * Math.PI * 2;
            }

            for (int k = 0; k < hillAmp.Length; k++)
            {
                hillAmp[k] = rng.NextDouble() / (k + 1);
                hillPhase[k] = rng.NextDouble() * Math.PI * 2;
            }

            // 1. Closed horizontal curve r(t), finely sampled.
            const int fine = 4000;
            var xs = new double[fine + 1];
            var zs = new double[fine + 1];
            for (int i = 0; i <= fine; i++)
            {
                double t = 2 * Math.PI * i / fine;
                double r = 1.0;
                for (int k = 0; k < harmonics; k++) r += shapeAmp[k] * Math.Sin(((k + 2) * t) + shapePhase[k]);
                xs[i] = r * Math.Sin(t);
                zs[i] = r * Math.Cos(t);
            }

            var cumulative = new double[fine + 1];
            for (int i = 1; i <= fine; i++) cumulative[i] = cumulative[i - 1] + Hypot(xs[i] - xs[i - 1], zs[i] - zs[i - 1]);
            double scale = lengthM / cumulative[fine];

            // 2. Periodic elevation profile (closes the loop), scaled to the requested hilliness.
            double Elevation(double t)
            {
                double e = 0;
                for (int k = 0; k < hillAmp.Length; k++) e += hillAmp[k] * Math.Sin(((k + 1) * t) + hillPhase[k]);
                return e;
            }

            // 3. Resample evenly by distance.
            int count = (int)Math.Round(lengthM / spacingM);
            var raw = new List<(double D, double X, double Z, double E)>(count + 1);
            int j = 0;
            for (int n = 0; n <= count; n++)
            {
                double target = n * lengthM / count / scale;
                while (j < fine - 1 && cumulative[j + 1] < target) j++;
                double seg = cumulative[j + 1] - cumulative[j];
                double f = seg > 0 ? (target - cumulative[j]) / seg : 0;
                double t = 2 * Math.PI * (j + f) / fine;
                raw.Add((n * lengthM / count, (xs[j] + ((xs[j + 1] - xs[j]) * f)) * scale, (zs[j] + ((zs[j + 1] - zs[j]) * f)) * scale, Elevation(t)));
            }

            // Scale elevation so that the steepest grade is ~8 % × hilliness.
            double maxSlope = 0;
            for (int i = 1; i < raw.Count; i++)
            {
                maxSlope = Math.Max(maxSlope, Math.Abs(raw[i].E - raw[i - 1].E) / (raw[i].D - raw[i - 1].D));
            }

            double elevationScale = maxSlope > 0 ? (0.08 * Math.Max(0, Math.Min(1, hilliness))) / maxSlope : 0;

            var points = new List<RoutePoint>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                int prev = i == 0 ? raw.Count - 2 : i - 1;
                int next = i == raw.Count - 1 ? 1 : i + 1;
                double dE = (raw[next].E - raw[prev].E) * elevationScale;
                double grade = dE / (2 * (lengthM / count)) * 100.0;
                points.Add(new RoutePoint(raw[i].D, raw[i].X, raw[i].Z, 100 + (raw[i].E * elevationScale), grade, RoadSurface.Asphalt));
            }

            return new Route($"Loop {seed}", points, isLoop: true);
        }

        private static double Hypot(double a, double b) => Math.Sqrt((a * a) + (b * b));
    }
}
