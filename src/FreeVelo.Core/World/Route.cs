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

        /// <summary>Famous climbs ridden uphill on this route (foot → summit), for the climb timer.</summary>
        public IReadOnlyList<ClimbSegment> Climbs { get; internal set; } = Array.Empty<ClimbSegment>();

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

        /// <summary>
        /// The rider follows the road line averaged over this distance before and after the point: a smooth path
        /// through every polyline corner (junctions turn by up to 90° at a single point, hairpins by up to 90° with
        /// 5 m points). The heading is the direction of that path, so bike and movement always agree (no drifting).
        /// </summary>
        public const double HeadingWindowM = 6;

        /// <summary>Position at <paramref name="distanceM"/>; loops wrap around, open routes are clamped.</summary>
        public RouteSample Sample(double distanceM)
        {
            double d = Clamp(distanceM);
            int lo = Segment(d);
            RoutePoint a = Points[lo], b = Points[lo + 1];
            double span = b.DistanceM - a.DistanceM;
            double f = span > 0 ? (d - a.DistanceM) / span : 0;

            // Smoothed path: the mean of the road line over ±HeadingWindowM. Its direction is exactly the chord from
            // −window to +window, which is the heading.
            // (open routes: around the clamped distance, so the rider never goes past the end of the road)
            double c = IsLoop ? distanceM : d;
            var (x, z) = Average(c - HeadingWindowM, c + HeadingWindowM);
            var (bx, bz) = LinePoint(c - HeadingWindowM);
            var (ax, az) = LinePoint(c + HeadingWindowM);
            double heading = (ax - bx) * (ax - bx) + ((az - bz) * (az - bz)) > 1e-6
                ? Math.Atan2(ax - bx, az - bz)
                : Math.Atan2(b.X - a.X, b.Z - a.Z);
            return new RouteSample(
                x,
                Lerp(a.ElevationM, b.ElevationM, f),
                z,
                heading,
                Lerp(a.GradePercent, b.GradePercent, f),
                f < 0.5 ? a.Surface : b.Surface);
        }

        /// <summary>Mean position of the road line between two distances (exact, segment by segment).</summary>
        private (double X, double Z) Average(double from, double to)
        {
            double sx = 0, sz = 0, d = from;
            int guard = 0;
            while (d < to - 1e-9 && guard++ < 10_000)
            {
                double rd;
                if (IsLoop)
                {
                    rd = Mod(d, LengthM);
                }
                else if (d < Points[0].DistanceM || d >= LengthM)
                {
                    // Beyond the ends of an open route the road goes on straight: the mean of that piece is its middle.
                    double end = d < Points[0].DistanceM ? Math.Min(to, Points[0].DistanceM) : to;
                    var (mx, mz) = LinePoint((d + end) / 2);
                    sx += mx * (end - d);
                    sz += mz * (end - d);
                    d = end;
                    continue;
                }
                else
                {
                    rd = d;
                }

                int i = Segment(rd);
                RoutePoint a = Points[i], b = Points[i + 1];
                double span = b.DistanceM - a.DistanceM;
                double step = Math.Min(to - d, Math.Max(1e-6, b.DistanceM - rd));
                if (span <= 0)
                {
                    d += step;
                    continue;
                }

                // Mean of a straight piece = its middle point.
                double mid = (rd + (step / 2) - a.DistanceM) / span;
                sx += Lerp(a.X, b.X, mid) * step;
                sz += Lerp(a.Z, b.Z, mid) * step;
                d += step;
            }

            double total = Math.Max(1e-9, to - from);
            return (sx / total, sz / total);
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

        /// <summary>Open routes may start at any distance (a path through the road network): clamp to their range.</summary>
        private double Clamp(double distanceM) => IsLoop ? Mod(distanceM, LengthM) : Math.Max(Points[0].DistanceM, Math.Min(LengthM, distanceM));

        /// <summary>Index of the point that starts the segment containing <paramref name="d"/> (already clamped).</summary>
        private int Segment(double d)
        {
            int lo = 0, hi = Points.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Points[mid].DistanceM <= d) lo = mid;
                else hi = mid;
            }

            return Math.Min(lo, Points.Count - 2);
        }

        /// <summary>Point on the road line; open routes go on straight beyond their ends (along the end segments).</summary>
        private (double X, double Z) LinePoint(double distanceM)
        {
            if (IsLoop || (distanceM >= Points[0].DistanceM && distanceM <= LengthM)) return PointAt(Clamp(distanceM));
            bool before = distanceM < Points[0].DistanceM;
            RoutePoint a = before ? Points[0] : Points[Points.Count - 2], b = before ? Points[1] : Points[Points.Count - 1];
            double span = b.DistanceM - a.DistanceM;
            if (span <= 0) return before ? (a.X, a.Z) : (b.X, b.Z);
            double f = (distanceM - a.DistanceM) / span;
            return (Lerp(a.X, b.X, f), Lerp(a.Z, b.Z, f));
        }

        private (double X, double Z) PointAt(double d)
        {
            int i = Segment(d);
            RoutePoint a = Points[i], b = Points[i + 1];
            double span = b.DistanceM - a.DistanceM;
            double f = span > 0 ? (d - a.DistanceM) / span : 0;
            return (Lerp(a.X, b.X, f), Lerp(a.Z, b.Z, f));
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

    /// <summary>A climb on a route: where it starts (foot) and ends (summit), in route distance.</summary>
    public sealed class ClimbSegment
    {
        public ClimbSegment(string id, string name, double startM, double endM, double gainM)
        {
            Id = id;
            Name = name;
            StartM = startM;
            EndM = endM;
            GainM = gainM;
        }

        public string Id { get; }
        public string Name { get; }
        public double StartM { get; }
        public double EndM { get; }
        public double GainM { get; }
        public double LengthM => EndM - StartM;
    }
}
