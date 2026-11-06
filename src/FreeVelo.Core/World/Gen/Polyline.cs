using System;
using System.Collections.Generic;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>A horizontal path (X/Z) with cumulative distances; closed loops repeat the first point at the end.</summary>
    internal sealed class Polyline
    {
        public List<double> X { get; } = new List<double>();

        public List<double> Z { get; } = new List<double>();

        public List<double> D { get; } = new List<double>();

        public int Count => X.Count;

        public double Length => D.Count == 0 ? 0 : D[D.Count - 1];

        public void Add(double x, double z)
        {
            D.Add(Count == 0 ? 0 : D[Count - 1] + Math.Sqrt(((x - X[Count - 1]) * (x - X[Count - 1])) + ((z - Z[Count - 1]) * (z - Z[Count - 1]))));
            X.Add(x);
            Z.Add(z);
        }

        /// <summary>Point at distance <paramref name="s"/> (wrapping around for closed loops).</summary>
        public (double X, double Z) At(double s)
        {
            if (IsClosed) s = ((s % Length) + Length) % Length;
            s = Math.Max(0, Math.Min(Length, s));
            int lo = 0, hi = Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (D[mid] <= s) lo = mid;
                else hi = mid;
            }

            double span = D[hi] - D[lo];
            double f = span > 0 ? (s - D[lo]) / span : 0;
            return (X[lo] + ((X[hi] - X[lo]) * f), Z[lo] + ((Z[hi] - Z[lo]) * f));
        }

        public bool IsClosed => Count > 2 && Math.Abs(X[0] - X[Count - 1]) < 1e-6 && Math.Abs(Z[0] - Z[Count - 1]) < 1e-6;

        /// <summary>Evenly spaced copy (the last point is kept exactly).</summary>
        public Polyline Resample(double spacing)
        {
            int n = Math.Max(1, (int)Math.Round(Length / spacing));
            var result = new Polyline();
            bool closed = IsClosed;
            for (int i = 0; i <= n; i++)
            {
                if (i == n)
                {
                    result.Add(X[Count - 1], Z[Count - 1]);
                    break;
                }

                var (x, z) = At(Length * i / n);
                result.Add(x, z);
            }

            if (closed)
            {
                // Exactly closed again.
                result.X[result.Count - 1] = result.X[0];
                result.Z[result.Count - 1] = result.Z[0];
            }

            return result;
        }

        /// <summary>The part between distances <paramref name="from"/> and <paramref name="to"/> (may wrap past the end of a loop).</summary>
        public Polyline Section(double from, double to)
        {
            double length = to - from;
            int n = Math.Max(1, (int)Math.Round(length / WorldMap.Spacing));
            var result = new Polyline();
            for (int i = 0; i <= n; i++)
            {
                var (x, z) = At(from + (length * i / n));
                result.Add(x, z);
            }

            return result;
        }

        public Polyline Reversed()
        {
            var result = new Polyline();
            for (int i = Count - 1; i >= 0; i--) result.Add(X[i], Z[i]);
            return result;
        }

        /// <summary>Unit normal at point <paramref name="i"/> (to the left of the direction of travel).</summary>
        public (double X, double Z) Normal(int i)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(Count - 1, i + 1);
            double dx = X[b] - X[a], dz = Z[b] - Z[a];
            double l = Math.Sqrt((dx * dx) + (dz * dz));
            return l > 0 ? (-dz / l, dx / l) : (0, 1);
        }

        /// <summary>Point and unit normal at distance <paramref name="s"/>, pointing inside (or outside) the loop.</summary>
        public (double X, double Z, double NX, double NZ) PointAndNormal(double s, double cx, double cz, bool inside)
        {
            var (x, z) = At(s);
            var (x0, z0) = At(s - 5);
            var (x1, z1) = At(s + 5);
            double dx = x1 - x0, dz = z1 - z0;
            double l = Math.Sqrt((dx * dx) + (dz * dz));
            double nx = -dz / l, nz = dx / l;
            bool towardsInside = Contains(x + (nx * 3), z + (nz * 3));
            if (towardsInside != inside)
            {
                nx = -nx;
                nz = -nz;
            }

            return (x, z, nx, nz);
        }

        /// <summary>Point-in-polygon (even-odd) for a closed loop.</summary>
        public bool Contains(double x, double z)
        {
            bool inside = false;
            for (int i = 0, j = Count - 1; i < Count; j = i++)
            {
                if ((Z[i] > z) != (Z[j] > z) && x < ((X[j] - X[i]) * (z - Z[i]) / (Z[j] - Z[i])) + X[i]) inside = !inside;
            }

            return inside;
        }

        /// <summary>Distance to the nearest point of the path (points are a few metres apart).</summary>
        public double DistanceTo(double x, double z)
        {
            double best = double.MaxValue;
            for (int i = 0; i < Count; i++) best = Math.Min(best, ((X[i] - x) * (X[i] - x)) + ((Z[i] - z) * (Z[i] - z)));
            return Math.Sqrt(best);
        }

        public static Polyline Bezier(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz, int steps)
        {
            var result = new Polyline();
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps, u = 1 - t;
                double w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
                result.Add((w0 * ax) + (w1 * bx) + (w2 * cx) + (w3 * dx), (w0 * az) + (w1 * bz) + (w2 * cz) + (w3 * dz));
            }

            return result;
        }
    }
}
