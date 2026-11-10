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

        /// <summary>Chunks of the 3D world (home grid and mountain): the home grid is a whole number of them.</summary>
        public const double ChunkSize = 128;

        private Terrain(WorldMap map, double cell)
        {
            _map = map;
            Cell = cell;
            var rect = GridRect(map);
            MinX = rect.MinX;
            MinZ = rect.MinZ;
            Nx = (int)Math.Round((rect.MaxX - rect.MinX) / cell) + 1;
            Nz = (int)Math.Round((rect.MaxZ - rect.MinZ) / cell) + 1;
            foreach (var e in map.Edges) _roads.Add(new SegmentIndex(e.X, e.Z, 40));
            Corners = JunctionCorners.Build(map);
            if (map.River != null) _river = new SegmentIndex(map.River.X, map.River.Z, 40);
            _heights = new float[Nx * Nz];
        }

        public WorldMap Map => _map;

        /// <summary>The rounded corners between roads at the junctions (the ground is lowered under them).</summary>
        public List<JunctionCorner> Corners { get; }

        /// <summary>
        /// The home world's detailed grid: its roads and water plus <see cref="Margin"/>, rounded out to whole chunks
        /// (so the mountain's chunks join it exactly).
        /// </summary>
        public static (double MinX, double MinZ, double MaxX, double MaxZ) GridRect(WorldMap map)
        {
            var (minX, minZ, maxX, maxZ) = map.Bounds;
            double x0 = Math.Floor((minX - Margin) / ChunkSize) * ChunkSize, z0 = Math.Floor((minZ - Margin) / ChunkSize) * ChunkSize;
            double x1 = x0 + (Math.Ceiling((maxX + Margin - x0) / ChunkSize) * ChunkSize), z1 = z0 + (Math.Ceiling((maxZ + Margin - z0) / ChunkSize) * ChunkSize);
            return (x0, z0, x1, z1);
        }

        /// <summary>The mountain's shape (null for worlds without a mountain). Outside the home grid, heights come from it.</summary>
        public MountainTerrain? Mountain { get; private set; }

        /// <summary>Villages, farms and huts (set once laid out, see <see cref="Gen.Landscape.Generate"/>); nature keeps off them.</summary>
        public Landscape? Landscape { get; set; }

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
            if (map.Mountain != null) terrain.Mountain = MountainTerrain.Build(map, (terrain.MinX, terrain.MinZ, terrain.MaxX, terrain.MaxZ));
            Parallel.For(0, terrain.Nz, z =>
            {
                for (int x = 0; x < terrain.Nx; x++)
                {
                    terrain._heights[(z * terrain.Nx) + x] = (float)terrain.Compute(terrain.MinX + (x * cell), terrain.MinZ + (z * cell));
                }
            });
            return terrain;
        }

        // ------------------------------------------------------------------ prepared worlds

        /// <summary>Bump when the terrain generation changes (old saved worlds are then regenerated).</summary>
        public const int FormatVersion = 3;
        private const uint Magic = 0x4C565646; // "FVVL"

        /// <summary>
        /// Fingerprint of the map the terrain belongs to: format version, seed, cell size and the roads (every 4th
        /// point). A saved terrain is only used for exactly this map.
        /// </summary>
        public static ulong Fingerprint(WorldMap map, double cell = 4)
        {
            ulong h = 1469598103934665603UL;
            void Add(double v)
            {
                long bits = BitConverter.DoubleToInt64Bits(Math.Round(v, 3));
                for (int i = 0; i < 8; i++)
                {
                    h ^= (byte)(bits >> (8 * i));
                    h *= 1099511628211UL;
                }
            }

            Add(FormatVersion);
            Add(map.Seed);
            Add(cell);
            Add(map.Edges.Count);
            foreach (var e in map.Edges)
            {
                for (int i = 0; i < e.X.Length; i += 4)
                {
                    Add(e.X[i]);
                    Add(e.Z[i]);
                    Add(e.E[i]);
                }
            }

            return h;
        }

        /// <summary>
        /// Saves the generated ground (home grid and mountain shape), compressed – about 10 MB for the default world.
        /// Loading it with <see cref="TryLoad"/> skips the slow part of <see cref="Build"/>.
        /// </summary>
        public void Save(System.IO.Stream stream)
        {
            using var zip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true);
            using var w = new System.IO.BinaryWriter(zip);
            w.Write(Magic);
            w.Write(Fingerprint(_map, Cell));
            w.Write(Nx);
            w.Write(Nz);
            foreach (float f in _heights) w.Write(f);
            w.Write(Mountain != null);
            Mountain?.Write(w);
        }

        /// <summary>True if <paramref name="stream"/> holds a terrain saved for exactly this map (reads only the header).</summary>
        public static bool IsSavedFor(WorldMap map, System.IO.Stream stream, double cell = 4)
        {
            try
            {
                using var zip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true);
                using var r = new System.IO.BinaryReader(zip);
                return r.ReadUInt32() == Magic && r.ReadUInt64() == Fingerprint(map, cell);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>A terrain saved with <see cref="Save"/> for exactly this map, or null (other map, version or damaged).</summary>
        public static Terrain? TryLoad(WorldMap map, System.IO.Stream stream, double cell = 4)
        {
            try
            {
                using var zip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true);
                using var r = new System.IO.BinaryReader(new System.IO.BufferedStream(zip, 1 << 16));
                if (r.ReadUInt32() != Magic || r.ReadUInt64() != Fingerprint(map, cell)) return null;
                var terrain = new Terrain(map, cell);
                if (r.ReadInt32() != terrain.Nx || r.ReadInt32() != terrain.Nz) return null;
                for (int i = 0; i < terrain._heights.Length; i++) terrain._heights[i] = r.ReadSingle();
                if (r.ReadBoolean())
                {
                    terrain.Mountain = MountainTerrain.Read(map, (terrain.MinX, terrain.MinZ, terrain.MaxX, terrain.MaxZ), r);
                    if (terrain.Mountain == null) return null;
                }

                return terrain;
            }
            catch (Exception)
            {
                return null; // damaged or cut off: generate instead
            }
        }

        /// <summary>Grid height at a grid point.</summary>
        public float At(int x, int z) => _heights[(Math.Max(0, Math.Min(Nz - 1, z)) * Nx) + Math.Max(0, Math.Min(Nx - 1, x))];

        /// <summary>True inside the detailed home grid (outside it the mountain's ground is computed on demand).</summary>
        public bool InGrid(double x, double z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

        /// <summary>Ground height anywhere: the home grid (bilinear between grid points), or the mountain's ground.</summary>
        public double Height(double x, double z)
        {
            if (!InGrid(x, z)) return Compute(x, z);

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

        /// <summary>
        /// Highest a terrain mesh corner may be so that no triangle of a mesh with <paramref name="cellM"/> cells rises
        /// above a road next to it: the lowest road within reach, minus a margin (+∞ when no road is that close). A
        /// corner on a road's own bed is left alone, so roads above each other in hairpins keep their ground.
        /// Coarse meshes (distant views, lower detail) need this: on steep slopes their big triangles otherwise cut
        /// through the road where it is cut into the hillside.
        /// </summary>
        public double RoadCap(double x, double z, double cellM)
        {
            // Quick exit far from the mountain roads (16 m distance map; home roads only run inside the home grid).
            double reachCells = cellM * 1.42;
            var m = Mountain;
            if (m != null && !InGrid(x, z) && m.Contains(x, z) && m.RoadDistance(x, z) > reachCells + 30) return double.PositiveInfinity;

            double own = DistanceToRoad(x, z, 8, out var road, out _, out _);
            if (road != null && own <= (road.Width / 2) + 1.3) return double.PositiveInfinity;
            double cap = double.PositiveInfinity;
            for (int i = 0; i < _roads.Count; i++)
            {
                var e = _map.Edges[i];
                cap = Math.Min(cap, _roads[i].LowestWithin(x, z, (e.Width / 2) + reachCells, e.E) - 0.6);
            }

            return cap;
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

        /// <summary>Ground at a point, computed (for the home grid once, for the mountain whenever a chunk is built).</summary>
        public double Compute(double x, double z)
        {
            double h = _map.BaseHeight(x, z) + (Mountain?.Offset(x, z) ?? 0);

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
                // Where roads meet or split, the ground stays below every road surface there, not only the nearest.
                if (roadDistance <= flat + BedBlend)
                {
                    for (int i = 0; i < _roads.Count; i++)
                    {
                        var other = _map.Edges[i];
                        if (other == road) continue;
                        bed = Math.Min(bed, _roads[i].LowestWithin(x, z, (other.Width / 2) + 1.3, other.E) - 0.3);
                    }
                }

                h = roadDistance <= flat ? bed : bed + ((h - bed) * SmoothStep((roadDistance - flat) / BedBlend));
            }

            // Under the rounded junction corners, like under the roads (but a road's own bed stays as it is).
            bool onRoadBed = road != null && roadDistance <= (road.Width / 2) + 1.3;
            if (!nearRiver && !onRoadBed)
            {
                foreach (var c in Corners)
                {
                    if (x < c.MinX - BedBlend || x > c.MaxX + BedBlend || z < c.MinZ - BedBlend || z > c.MaxZ + BedBlend) continue;
                    double d = c.Distance(x, z);
                    if (d >= 1.3 + BedBlend) continue;
                    double bed = c.CY - 0.3;
                    h = d <= 1.3 ? Math.Min(h, bed) : Math.Min(h, bed + ((h - bed) * SmoothStep((d - 1.3) / BedBlend)));
                }
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
