using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>
    /// The shape of the mountain: a height offset on top of the rolling hills, on a 16 m grid around the mountain roads.
    /// Near every road point the offset brings the ground to the road's height; in between it is filled smoothly
    /// (a harmonic fill solved coarse-to-fine), so the slopes between hairpins, the valleys and the summit follow the
    /// climbs. Away from the roads rocky ridges are added. Zero inside the home world.
    /// </summary>
    public sealed class MountainTerrain
    {
        public const double Cell = 16;
        public const double Margin = 2600;

        private readonly WorldMap _map;
        private readonly float[] _offset;
        private readonly float[] _roadDistance;

        /// <summary>Home grid rectangle (the offset is 0 there, so the two terrains meet without a seam).</summary>
        private readonly (double MinX, double MinZ, double MaxX, double MaxZ) _home;

        private MountainTerrain(WorldMap map, (double, double, double, double) home)
        {
            _map = map;
            _home = home;
            var (minX, minZ, maxX, maxZ) = map.MountainBounds;
            MinX = Math.Floor((minX - Margin) / Cell) * Cell;
            MinZ = Math.Floor((minZ - Margin) / Cell) * Cell;
            Nx = (int)Math.Ceiling((maxX + Margin - MinX) / Cell) + 1;
            Nz = (int)Math.Ceiling((maxZ + Margin - MinZ) / Cell) + 1;
            _offset = new float[Nx * Nz];
            _roadDistance = new float[Nx * Nz];
        }

        public double MinX { get; }
        public double MinZ { get; }
        public int Nx { get; }
        public int Nz { get; }
        public double MaxX => MinX + ((Nx - 1) * Cell);
        public double MaxZ => MinZ + ((Nz - 1) * Cell);

        /// <summary>Lowest and highest road point of the mountain (for the look: treeline, rock, snow).</summary>
        public double LowRoad { get; private set; }
        public double Summit { get; private set; }

        public bool Contains(double x, double z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

        /// <summary>
        /// Builds the shape around the mountain roads. <paramref name="home"/> is the home terrain's rectangle, where the
        /// offset stays 0. About a second on a phone (in the background).
        /// </summary>
        public static MountainTerrain Build(WorldMap map, (double MinX, double MinZ, double MaxX, double MaxZ) home)
        {
            var t = new MountainTerrain(map, home);
            t.Solve();
            return t;
        }

        // ------------------------------------------------------------------ queries

        private float At(float[] grid, int x, int z) => grid[(Math.Max(0, Math.Min(Nz - 1, z)) * Nx) + Math.Max(0, Math.Min(Nx - 1, x))];

        private double Bilinear(float[] grid, double x, double z)
        {
            double gx = (x - MinX) / Cell, gz = (z - MinZ) / Cell;
            int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz);
            double fx = gx - ix, fz = gz - iz;
            double a = At(grid, ix, iz), b = At(grid, ix + 1, iz), c = At(grid, ix, iz + 1), d = At(grid, ix + 1, iz + 1);
            return ((a + ((b - a) * fx)) * (1 - fz)) + ((c + ((d - c) * fx)) * fz);
        }

        /// <summary>Height offset of the mountain (0 in the home world and outside the grid's reach).</summary>
        public double Offset(double x, double z)
        {
            if (InHome(x, z)) return 0;
            return Bilinear(_offset, x, z);
        }

        /// <summary>Distance to the nearest mountain road (16 m resolution), for scenery and the look.</summary>
        public double RoadDistance(double x, double z) => Bilinear(_roadDistance, x, z);

        /// <summary>Natural ground (hills + mountain, without road beds): for distant views.</summary>
        public double Coarse(double x, double z) => _map.BaseHeight(x, z) + Offset(x, z);

        private bool InHome(double x, double z) => x >= _home.MinX && x <= _home.MaxX && z >= _home.MinZ && z <= _home.MaxZ;

        // ------------------------------------------------------------------ solving

        private void Solve()
        {
            var mountainEdges = _map.Edges.Where(e => e.Kind != RoadKind.Home).ToList();
            LowRoad = mountainEdges.Min(e => e.E.Min());
            Summit = mountainEdges.Max(e => e.E.Max());

            // Road targets at 16 m: offset = road height − hills there (averaged per cell)
            var sum = new double[Nx * Nz];
            var count = new int[Nx * Nz];
            foreach (var e in mountainEdges)
            {
                for (int i = 0; i < e.X.Length; i++)
                {
                    int gx = (int)Math.Round((e.X[i] - MinX) / Cell), gz = (int)Math.Round((e.Z[i] - MinZ) / Cell);
                    if (gx < 0 || gz < 0 || gx >= Nx || gz >= Nz) continue;
                    int k = (gz * Nx) + gx;
                    if (InHome(MinX + (gx * Cell), MinZ + (gz * Cell))) continue;
                    sum[k] += (e.E[i] - 0.4) - _map.BaseHeight(e.X[i], e.Z[i]);
                    count[k]++;
                }
            }

            // Fixed cells: road targets, and the home world (offset 0, one cell beyond its edge for a seamless join)
            var fixedValue = new double[Nx * Nz];
            var isFixed = new bool[Nx * Nz];
            for (int gz = 0; gz < Nz; gz++)
            {
                for (int gx = 0; gx < Nx; gx++)
                {
                    int k = (gz * Nx) + gx;
                    double x = MinX + (gx * Cell), z = MinZ + (gz * Cell);
                    if (x >= _home.MinX - Cell && x <= _home.MaxX + Cell && z >= _home.MinZ - Cell && z <= _home.MaxZ + Cell)
                    {
                        isFixed[k] = true;
                        fixedValue[k] = 0;
                    }
                    else if (count[k] > 0)
                    {
                        isFixed[k] = true;
                        fixedValue[k] = sum[k] / count[k];
                    }
                }
            }

            // Harmonic fill, coarse to fine (128 → 64 → 32 → 16 m), free edges (zero slope across the border)
            var levels = new[] { 8, 4, 2, 1 };
            double[]? previous = null;
            int prevNx = 0, prevNz = 0, prevStep = 0;
            foreach (int step in levels)
            {
                int nx = ((Nx - 1) / step) + 1, nz = ((Nz - 1) / step) + 1;
                var value = new double[nx * nz];
                var fix = new bool[nx * nz];
                var fixV = new double[nx * nz];
                var fixN = new int[nx * nz];

                // Fixed cells of this level: any fine fixed cell inside it (averaged)
                for (int gz = 0; gz < Nz; gz++)
                {
                    for (int gx = 0; gx < Nx; gx++)
                    {
                        int k = (gz * Nx) + gx;
                        if (!isFixed[k]) continue;
                        int cx = (int)Math.Round(gx / (double)step), cz = (int)Math.Round(gz / (double)step);
                        if (cx >= nx || cz >= nz) continue;
                        int c = (cz * nx) + cx;
                        fix[c] = true;
                        fixV[c] += fixedValue[k];
                        fixN[c]++;
                    }
                }

                for (int c = 0; c < value.Length; c++)
                {
                    if (fix[c]) value[c] = fixV[c] / fixN[c];
                    else if (previous != null)
                    {
                        // Start from the coarser solution
                        int cx = c % nx, cz = c / nx;
                        double px = cx * step / (double)prevStep, pz = cz * step / (double)prevStep;
                        int ix = Math.Min(prevNx - 1, (int)px), iz = Math.Min(prevNz - 1, (int)pz);
                        value[c] = previous[(iz * prevNx) + ix];
                    }
                }

                int iterations = step == 8 ? 1500 : step == 4 ? 500 : step == 2 ? 250 : 150;
                for (int it = 0; it < iterations; it++)
                {
                    // Red-black Gauss-Seidel with over-relaxation, in parallel over rows
                    for (int colour = 0; colour < 2; colour++)
                    {
                        int col = colour;
                        Parallel.For(0, nz, cz =>
                        {
                            for (int cx = (cz + col) % 2; cx < nx; cx += 2)
                            {
                                int c = (cz * nx) + cx;
                                if (fix[c]) continue;
                                double l = value[(cz * nx) + Math.Max(0, cx - 1)];
                                double r = value[(cz * nx) + Math.Min(nx - 1, cx + 1)];
                                double d = value[(Math.Max(0, cz - 1) * nx) + cx];
                                double u = value[(Math.Min(nz - 1, cz + 1) * nx) + cx];
                                double avg = (l + r + d + u) / 4;
                                value[c] += 1.85 * (avg - value[c]);
                            }
                        });
                    }
                }

                previous = value;
                prevNx = nx;
                prevNz = nz;
                prevStep = step;
            }

            // Distance to the roads (chamfer distance transform at 16 m)
            var dist = new float[Nx * Nz];
            for (int k = 0; k < dist.Length; k++) dist[k] = count[k] > 0 ? 0 : float.MaxValue / 2;
            float a = (float)Cell, b = (float)(Cell * Math.Sqrt(2));
            for (int gz = 0; gz < Nz; gz++)
            {
                for (int gx = 0; gx < Nx; gx++)
                {
                    int k = (gz * Nx) + gx;
                    if (gx > 0) dist[k] = Math.Min(dist[k], dist[k - 1] + a);
                    if (gz > 0) dist[k] = Math.Min(dist[k], dist[k - Nx] + a);
                    if (gx > 0 && gz > 0) dist[k] = Math.Min(dist[k], dist[k - Nx - 1] + b);
                    if (gx < Nx - 1 && gz > 0) dist[k] = Math.Min(dist[k], dist[k - Nx + 1] + b);
                }
            }

            for (int gz = Nz - 1; gz >= 0; gz--)
            {
                for (int gx = Nx - 1; gx >= 0; gx--)
                {
                    int k = (gz * Nx) + gx;
                    if (gx < Nx - 1) dist[k] = Math.Min(dist[k], dist[k + 1] + a);
                    if (gz < Nz - 1) dist[k] = Math.Min(dist[k], dist[k + Nx] + a);
                    if (gx < Nx - 1 && gz < Nz - 1) dist[k] = Math.Min(dist[k], dist[k + Nx + 1] + b);
                    if (gx > 0 && gz < Nz - 1) dist[k] = Math.Min(dist[k], dist[k + Nx - 1] + b);
                }
            }

            Array.Copy(dist, _roadDistance, dist.Length);

            // Offset = fill + rocky relief away from the roads (more relief higher up and further from the roads)
            int seed = _map.Seed + 101;
            double span = Math.Max(1, Summit - LowRoad);
            Parallel.For(0, Nz, gz =>
            {
                for (int gx = 0; gx < Nx; gx++)
                {
                    int k = (gz * Nx) + gx;
                    double x = MinX + (gx * Cell), z = MinZ + (gz * Cell);
                    double fill = previous![(gz * Nx) + gx];
                    if (InHome(x, z))
                    {
                        _offset[k] = 0;
                        continue;
                    }

                    double away = SmoothStep((dist[k] - 70) / 450);
                    double homeFade = SmoothStep((DistanceToRect(x, z) - 150) / 900);
                    double height = SmoothStep(((fill + _map.Hills(x, z)) - LowRoad) / span);
                    double ridges = 1 - Math.Abs(Noise.Fractal(x / 900, z / 900, seed, 4));
                    double relief = ((ridges * ridges * 260) + (Noise.Fractal(x / 260, z / 260, seed + 3, 3) * 45)) * (0.35 + (0.65 * height));
                    _offset[k] = (float)(fill + (relief * away * homeFade));
                }
            });
        }

        private double DistanceToRect(double x, double z)
        {
            double dx = Math.Max(0, Math.Max(_home.MinX - x, x - _home.MaxX));
            double dz = Math.Max(0, Math.Max(_home.MinZ - z, z - _home.MaxZ));
            return Math.Sqrt((dx * dx) + (dz * dz));
        }

        private static double SmoothStep(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t * t * (3 - (2 * t));
        }
    }
}
