using System;
using System.Collections.Generic;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>Grid of polyline segments for fast "nearest point on the line" queries.</summary>
    internal sealed class SegmentIndex
    {
        private readonly double[] _x;
        private readonly double[] _z;
        private readonly double _cell;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

        public SegmentIndex(double[] x, double[] z, double cell)
        {
            _x = x;
            _z = z;
            _cell = cell;
            for (int i = 0; i < x.Length - 1; i++)
            {
                int x0 = Cell(Math.Min(x[i], x[i + 1])), x1 = Cell(Math.Max(x[i], x[i + 1]));
                int z0 = Cell(Math.Min(z[i], z[i + 1])), z1 = Cell(Math.Max(z[i], z[i + 1]));
                for (int cx = x0; cx <= x1; cx++)
                {
                    for (int cz = z0; cz <= z1; cz++)
                    {
                        long key = Key(cx, cz);
                        if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>();
                        list.Add(i);
                    }
                }
            }
        }

        /// <summary>
        /// Distance to the nearest segment within <paramref name="maxDistance"/> (else maxDistance), with the segment
        /// and the position along it (0–1).
        /// </summary>
        public double Nearest(double x, double z, double maxDistance, out int segment, out double fraction)
        {
            double best = maxDistance * maxDistance;
            segment = 0;
            fraction = 0;
            int r = (int)Math.Ceiling(maxDistance / _cell);
            int cx0 = Cell(x), cz0 = Cell(z);
            for (int cx = cx0 - r; cx <= cx0 + r; cx++)
            {
                for (int cz = cz0 - r; cz <= cz0 + r; cz++)
                {
                    if (!_cells.TryGetValue(Key(cx, cz), out var list)) continue;
                    foreach (int i in list)
                    {
                        double ax = _x[i], az = _z[i], bx = _x[i + 1], bz = _z[i + 1];
                        double abx = bx - ax, abz = bz - az;
                        double len2 = (abx * abx) + (abz * abz);
                        double f = len2 > 0 ? Math.Max(0, Math.Min(1, (((x - ax) * abx) + ((z - az) * abz)) / len2)) : 0;
                        double dx = ax + (abx * f) - x, dz = az + (abz * f) - z;
                        double d2 = (dx * dx) + (dz * dz);
                        if (d2 < best)
                        {
                            best = d2;
                            segment = i;
                            fraction = f;
                        }
                    }
                }
            }

            return Math.Sqrt(best);
        }

        private int Cell(double v) => (int)Math.Floor(v / _cell);

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
