using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>
    /// The detailed ground of the world as a height grid: the natural terrain with lake basins, the river channel and
    /// level beds under the roads (slightly below the road surface, blending into the hills). Only the app needs it.
    /// </summary>
    public sealed class Terrain
    {
        public const double Margin = 900;
        private const double BedBlend = 24;

        private readonly WorldMap _map;
        private readonly List<SegmentIndex> _roads = new List<SegmentIndex>();
        private readonly SegmentIndex? _river;
        private readonly float[] _heights;

        private Terrain(WorldMap map, double cell)
        {
            _map = map;
            Cell = cell;
            var (minX, minZ, maxX, maxZ) = map.Bounds;
            MinX = Math.Floor((minX - Margin) / cell) * cell;
            MinZ = Math.Floor((minZ - Margin) / cell) * cell;
            Nx = (int)Math.Ceiling((maxX + Margin - MinX) / cell) + 1;
            Nz = (int)Math.Ceiling((maxZ + Margin - MinZ) / cell) + 1;
            foreach (var e in map.Edges) _roads.Add(new SegmentIndex(e.X, e.Z, 40));
            if (map.River != null) _river = new SegmentIndex(map.River.X, map.River.Z, 40);
            _heights = new float[Nx * Nz];
        }

        public WorldMap Map => _map;

        public double MinX { get; }

        public double MinZ { get; }

        public double Cell { get; }

        /// <summary>Grid points along X and Z.</summary>
        public int Nx { get; }

        public int Nz { get; }

        public double MaxX => MinX + ((Nx - 1) * Cell);

        public double MaxZ => MinZ + ((Nz - 1) * Cell);

        /// <summary>Builds the grid (in parallel; about a second on a PC for the default world at 4 m).</summary>
        public static Terrain Build(WorldMap map, double cell = 4)
        {
            var terrain = new Terrain(map, cell);
            Parallel.For(0, terrain.Nz, z =>
            {
                for (int x = 0; x < terrain.Nx; x++)
                {
                    terrain._heights[(z * terrain.Nx) + x] = (float)terrain.Compute(terrain.MinX + (x * cell), terrain.MinZ + (z * cell));
                }
            });
            return terrain;
        }

        /// <summary>Grid height at a grid point.</summary>
        public float At(int x, int z) => _heights[(Math.Max(0, Math.Min(Nz - 1, z)) * Nx) + Math.Max(0, Math.Min(Nx - 1, x))];

        /// <summary>Ground height anywhere (bilinear between grid points).</summary>
        public double Height(double x, double z)
        {
            double gx = (x - MinX) / Cell, gz = (z - MinZ) / Cell;
            int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz);
            double fx = gx - ix, fz = gz - iz;
            double a = At(ix, iz), b = At(ix + 1, iz), c = At(ix, iz + 1), d = At(ix + 1, iz + 1);
            return ((a + ((b - a) * fx)) * (1 - fz)) + ((c + ((d - c) * fx)) * fz);
        }

        /// <summary>Slope (rise per metre) at a point.</summary>
        public double Slope(double x, double z)
        {
            double dx = Height(x + Cell, z) - Height(x - Cell, z), dz = Height(x, z + Cell) - Height(x, z - Cell);
            return Math.Sqrt((dx * dx) + (dz * dz)) / (2 * Cell);
        }

        /// <summary>Distance to the nearest road centre (up to <paramref name="max"/>), with that road and its height there.</summary>
        public double DistanceToRoad(double x, double z, double max, out RoadEdge? road, out double roadHeight, out double along)
        {
            double best = max;
            road = null;
            roadHeight = 0;
            along = 0;
            for (int i = 0; i < _roads.Count; i++)
            {
                double d = _roads[i].Nearest(x, z, best, out int segment, out double f);
                if (d >= best) continue;
                best = d;
                var e = _map.Edges[i];
                road = e;
                roadHeight = e.E[segment] + ((e.E[segment + 1] - e.E[segment]) * f);
                along = e.D[segment] + ((e.D[segment + 1] - e.D[segment]) * f);
            }

            return best;
        }

        /// <summary>Distance to the river centre (up to <paramref name="max"/>) and its water level there.</summary>
        public double DistanceToRiver(double x, double z, double max, out double level)
        {
            level = 0;
            if (_river == null) return max;
            double d = _river.Nearest(x, z, max, out int segment, out double f);
            var r = _map.River!;
            level = r.Level[segment] + ((r.Level[segment + 1] - r.Level[segment]) * f);
            return d;
        }

        /// <summary>True for points in a lake or the river (with a margin in metres beyond the shore).</summary>
        public bool IsWater(double x, double z, double margin = 0)
        {
            foreach (var lake in _map.Lakes)
            {
                if (lake.Relative(x, z) < 1 + (margin / lake.Radius)) return true;
            }

            return _map.River != null && DistanceToRiver(x, z, _map.River.HalfWidth + margin + 1, out _) < _map.River.HalfWidth + margin;
        }

        private double Compute(double x, double z)
        {
            double h = _map.BaseHeight(x, z);

            foreach (var lake in _map.Lakes)
            {
                double q = lake.Relative(x, z);
                if (q >= 1.7) continue;
                if (q < 1)
                {
                    h = lake.Level - 0.4 - (3.2 * (1 - (q * q)));
                }
                else
                {
                    double t = SmoothStep((q - 1) / 0.7);
                    h = lake.Level + 0.25 + ((h - lake.Level - 0.25) * t);
                }
            }

            bool nearRiver = false;
            if (_map.River != null)
            {
                double hw = _map.River.HalfWidth;
                double d = DistanceToRiver(x, z, hw + 14, out double level);
                if (d < hw + 14)
                {
                    nearRiver = true;
                    if (d < hw) h = Math.Min(h, level - 0.5 - (1.8 * (1 - ((d / hw) * (d / hw)))));
                    else h = Math.Min(h, level + 0.3 + ((h - level - 0.3) * SmoothStep((d - hw) / 14)));
                }
            }

            // Road beds (not under bridges, where the river channel stays open).
            double roadDistance = DistanceToRoad(x, z, 6.5 + BedBlend, out var road, out double roadHeight, out _);
            if (road != null && !nearRiver)
            {
                double flat = (road.Width / 2) + 1.3;
                double bed = roadHeight - 0.3;
                h = roadDistance <= flat ? bed : bed + ((h - bed) * SmoothStep((roadDistance - flat) / BedBlend));
            }

            return h;
        }

        private static double SmoothStep(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t * t * (3 - (2 * t));
        }
    }
}
