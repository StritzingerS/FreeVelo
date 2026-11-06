using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Physics;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>One placed model.</summary>
    public struct SceneryItem
    {
        public SceneryKind Kind;
        public int Variant;
        public float X;
        public float Y;
        public float Z;

        /// <summary>Turn about the vertical axis in degrees (the model's front +Z then points to (sin, cos)).</summary>
        public float RotationDeg;

        public float Scale;
    }

    /// <summary>A field: a rotated rectangle draped over the ground.</summary>
    public sealed class FieldPatch
    {
        public FieldPatch(double cx, double cz, double halfLength, double halfWidth, double angle, WorldMaterial crop)
        {
            Cx = cx;
            Cz = cz;
            HalfLength = halfLength;
            HalfWidth = halfWidth;
            Angle = angle;
            Crop = crop;
        }

        public double Cx { get; }

        public double Cz { get; }

        /// <summary>Half size along the field's direction (the rows).</summary>
        public double HalfLength { get; }

        public double HalfWidth { get; }

        /// <summary>Direction of the rows (radians, 0 = +Z, π/2 = +X).</summary>
        public double Angle { get; }

        public WorldMaterial Crop { get; }

        /// <summary>Position in field coordinates: along the rows and across them.</summary>
        public (double U, double V) Local(double x, double z)
        {
            double dx = x - Cx, dz = z - Cz;
            double s = Math.Sin(Angle), c = Math.Cos(Angle);
            return ((dx * s) + (dz * c), (dx * c) - (dz * s));
        }

        public bool Contains(double x, double z, double margin = 0)
        {
            var (u, v) = Local(x, z);
            return Math.Abs(u) <= HalfLength + margin && Math.Abs(v) <= HalfWidth + margin;
        }

        public (double X, double Z) Point(double u, double v)
        {
            double s = Math.Sin(Angle), c = Math.Cos(Angle);
            return (Cx + (u * s) + (v * c), Cz + (u * c) - (v * s));
        }
    }

    /// <summary>
    /// Where everything stands: villages along the roads (houses facing the street, gardens), farms with barns and
    /// fields, an avenue along the gravel road, reeds and trees at the water, forests of spruce and pine or oak and
    /// birch with bushes at their edges, scattered trees, bushes and rocks in the meadows. Deterministic.
    /// </summary>
    public sealed class SceneryLayout
    {
        /// <summary>Detailed scenery reaches this far from the roads; beyond, sparse forest on the hills.</summary>
        public const double NearBand = 650;

        private const double Cell = 10;

        /// <summary>Spacing of the forest / meadow sampling grid.</summary>
        private const double NatureCell = 15;

        private readonly Terrain _terrain;
        private readonly WorldMap _map;
        private readonly Random _random;
        private readonly Dictionary<long, List<(double X, double Z, double R)>> _taken = new Dictionary<long, List<(double, double, double)>>();
        private float[] _roadDistance = Array.Empty<float>();
        private int _gx;
        private int _gz;

        private SceneryLayout(Terrain terrain, int seed)
        {
            _terrain = terrain;
            _map = terrain.Map;
            _random = new Random(seed);
        }

        public List<SceneryItem> Items { get; } = new List<SceneryItem>();

        public List<FieldPatch> Fields { get; } = new List<FieldPatch>();

        public static SceneryLayout Generate(Terrain terrain, int seed = 7)
        {
            var layout = new SceneryLayout(terrain, seed);
            layout.RoadDistanceField();
            layout.Villages();
            layout.Farms();
            layout.MoreFields();
            layout.Avenue();
            layout.Waterside();
            layout.Nature();
            return layout;
        }

        // ------------------------------------------------------------------ settlements and fields

        private void Villages()
        {
            foreach (var (edge, s) in RoadsideSpots(2, 260))
            {
                var e = _map.Edges[edge];
                for (double d = s - 170; d <= s + 170; d += 30 + (_random.NextDouble() * 14))
                {
                    int side = _random.NextDouble() < 0.5 ? -1 : 1;
                    House(e, d, side, (e.Width / 2) + 12 + (_random.NextDouble() * 6), SceneryKind.House, _random.Next(3), garden: true);
                }
            }
        }

        private void Farms()
        {
            foreach (var (edge, s) in RoadsideSpots(3, 200))
            {
                var e = _map.Edges[edge];
                int side = _random.NextDouble() < 0.5 ? -1 : 1;
                double set = (e.Width / 2) + 16;
                House(e, s - 14, side, set, SceneryKind.House, _random.Next(3), garden: true);
                House(e, s + 16, side, set + 6, SceneryKind.Barn, _random.Next(3), garden: false);
                TryField(e, s + 140, side, (e.Width / 2) + 14);
                TryField(e, s - 150, side, (e.Width / 2) + 14);
                TryField(e, s, -side, (e.Width / 2) + 14);
            }
        }

        private void MoreFields()
        {
            for (int i = 0; i < 40 && Fields.Count < 14; i++)
            {
                var e = _map.Edges[_random.Next(_map.Edges.Count)];
                TryField(e, 120 + (_random.NextDouble() * (e.Length - 240)), _random.NextDouble() < 0.5 ? -1 : 1, (e.Width / 2) + 14 + (_random.NextDouble() * 60));
            }
        }

        /// <summary>Spots along the roads away from junctions, bridges and water.</summary>
        private IEnumerable<(int Edge, double S)> RoadsideSpots(int count, double clear)
        {
            int found = 0;
            for (int attempt = 0; attempt < 400 && found < count; attempt++)
            {
                var e = _map.Edges[_random.Next(_map.Edges.Count)];
                if (e.Length < 2 * (clear + 200)) continue;
                double s = clear + 150 + (_random.NextDouble() * (e.Length - (2 * (clear + 150))));
                if (_map.Bridges.Any(b => b.Edge == e.Id && Math.Abs(b.DistanceM - s) < clear + 200)) continue;
                var (x, z) = At(e, s);
                if (_terrain.IsWater(x, z, 150) || IsTaken(x, z, 300)) continue;
                Take(x, z, 60);
                found++;
                yield return (e.Id, s);
            }
        }

        private void House(RoadEdge e, double s, int side, double setback, SceneryKind kind, int variant, bool garden)
        {
            if (s < 30 || s > e.Length - 30) return;
            var (x, z) = At(e, s);
            var (nx, nz) = Normal(e, s);
            double hx = x + (nx * side * setback), hz = z + (nz * side * setback);
            double r = SceneryModels.Radius(kind);
            if (_terrain.IsWater(hx, hz, r + 5) || _terrain.Slope(hx, hz) > 0.14 || IsTaken(hx, hz, r) || RoadDistance(hx, hz) < r + 4) return;
            // Front (+Z) towards the road.
            double fx = -nx * side, fz = -nz * side;
            float rot = (float)(Math.Atan2(fx, fz) * 180 / Math.PI);
            Add(kind, variant, hx, hz, rot, 1, r, -0.05);
            if (!garden) return;
            for (int i = 0; i < 3; i++)
            {
                double a = _random.NextDouble() * Math.PI * 2;
                double d = r + 3 + (_random.NextDouble() * 6);
                double gx = hx + (Math.Cos(a) * d), gz = hz + (Math.Sin(a) * d);
                if (RoadDistance(gx, gz) < (e.Width / 2) + 3) continue;
                if (i == 0) TryAdd(_random.NextDouble() < 0.5 ? SceneryKind.Oak : SceneryKind.Birch, gx, gz, 0.75 + (_random.NextDouble() * 0.2));
                else TryAdd(SceneryKind.Bush, gx, gz, 0.8 + (_random.NextDouble() * 0.4));
            }
        }

        private void TryField(RoadEdge e, double s, int side, double gap)
        {
            if (s < 80 || s > e.Length - 80) return;
            var (x, z) = At(e, s);
            var (nx, nz) = Normal(e, s);
            double halfLength = 45 + (_random.NextDouble() * 60), halfWidth = 30 + (_random.NextDouble() * 45);
            double cx = x + (nx * side * (gap + halfWidth)), cz = z + (nz * side * (gap + halfWidth));
            var (tx, tz) = Tangent(e, s);
            var field = new FieldPatch(cx, cz, halfLength, halfWidth, Math.Atan2(tx, tz), Crop());
            for (double u = -1; u <= 1; u += 0.25)
            {
                for (double v = -1; v <= 1; v += 0.25)
                {
                    var (px, pz) = field.Point(u * halfLength, v * halfWidth);
                    if (_terrain.IsWater(px, pz, 6) || RoadDistance(px, pz) < 8 || IsTaken(px, pz, 2) || _terrain.Slope(px, pz) > 0.13) return;
                    if (Fields.Any(f => f.Contains(px, pz, 6))) return;
                }
            }

            Fields.Add(field);

            // A hedge along one long side now and then.
            if (_random.NextDouble() < 0.5)
            {
                double v = (_random.NextDouble() < 0.5 ? -1 : 1) * (halfWidth + 2.5);
                for (double u = -halfLength; u <= halfLength; u += 3.5 + (_random.NextDouble() * 2))
                {
                    var (px, pz) = field.Point(u, v);
                    TryAdd(_random.NextDouble() < 0.12 ? SceneryKind.Oak : SceneryKind.Bush, px, pz, 0.9 + (_random.NextDouble() * 0.4));
                }
            }
        }

        private WorldMaterial Crop()
        {
            double r = _random.NextDouble();
            return r < 0.35 ? WorldMaterial.Wheat : r < 0.65 ? WorldMaterial.Crop : r < 0.82 ? WorldMaterial.Meadow : WorldMaterial.Plowed;
        }

        /// <summary>Poplars along part of the gravel road, both sides.</summary>
        private void Avenue()
        {
            var gravel = _map.Edges.FirstOrDefault(e => e.Surface != RoadSurface.Asphalt) ?? _map.Edges[_map.Edges.Count - 1];
            double start = 200 + (_random.NextDouble() * Math.Max(0, gravel.Length - 1100));
            for (double s = start; s < Math.Min(gravel.Length - 150, start + 700); s += 13)
            {
                if (_map.Bridges.Any(b => b.Edge == gravel.Id && Math.Abs(b.DistanceM - s) < 40)) continue;
                var (x, z) = At(gravel, s);
                var (nx, nz) = Normal(gravel, s);
                for (int side = -1; side <= 1; side += 2)
                {
                    double d = (gravel.Width / 2) + 3.2;
                    TryAdd(SceneryKind.Poplar, x + (nx * side * d), z + (nz * side * d), 0.85 + (_random.NextDouble() * 0.25), keepFromRoad: false);
                }
            }
        }

        private void Waterside()
        {
            var river = _map.River;
            if (river != null)
            {
                for (int i = 0; i < river.X.Length; i += 1)
                {
                    var (nx, nz) = NormalAt(river.X, river.Z, i);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (_random.NextDouble() < 0.35)
                        {
                            double d = river.HalfWidth + 0.5 + (_random.NextDouble() * 1.5);
                            TryAdd(SceneryKind.Reed, river.X[i] + (nx * side * d), river.Z[i] + (nz * side * d), 0.8 + (_random.NextDouble() * 0.5), avoidWater: false);
                        }

                        if (_random.NextDouble() < 0.07)
                        {
                            double d = river.HalfWidth + 7 + (_random.NextDouble() * 14);
                            var kind = _random.NextDouble() < 0.6 ? SceneryKind.Poplar : SceneryKind.Birch;
                            TryAdd(kind, river.X[i] + (nx * side * d), river.Z[i] + (nz * side * d), 0.8 + (_random.NextDouble() * 0.3));
                        }
                    }
                }
            }

            foreach (var lake in _map.Lakes)
            {
                int n = (int)(lake.Radius * 0.9);
                for (int i = 0; i < n; i++)
                {
                    double a = _random.NextDouble() * Math.PI * 2;
                    double q = 0.97 + (_random.NextDouble() * 0.06);
                    double r = lake.RadiusAt(a) * q;
                    TryAdd(SceneryKind.Reed, lake.X + (Math.Cos(a) * r), lake.Z + (Math.Sin(a) * r), 0.8 + (_random.NextDouble() * 0.6), avoidWater: false);
                    if (_random.NextDouble() < 0.3)
                    {
                        double r2 = lake.RadiusAt(a) * (1.15 + (_random.NextDouble() * 0.3));
                        TryAdd(_random.NextDouble() < 0.6 ? SceneryKind.Birch : SceneryKind.Bush, lake.X + (Math.Cos(a) * r2), lake.Z + (Math.Sin(a) * r2), 0.8 + (_random.NextDouble() * 0.3));
                    }
                }
            }
        }

        // ------------------------------------------------------------------ nature

        /// <summary>Forests (by a noise mask), forest edges, meadow trees, bushes and rocks.</summary>
        private void Nature()
        {
            int seed = _map.Seed;
            for (double z = _terrain.MinZ + 20; z < _terrain.MaxZ - 20; z += NatureCell)
            {
                for (double x = _terrain.MinX + 20; x < _terrain.MaxX - 20; x += NatureCell)
                {
                    double road = RoadDistance(x, z);
                    bool near = road < NearBand;
                    // Far away: half the cells, forest only.
                    if (!near && ((int)Math.Floor(x / NatureCell) + (int)Math.Floor(z / NatureCell)) % 2 != 0) continue;
                    double px = x + ((_random.NextDouble() - 0.5) * NatureCell * 0.9), pz = z + ((_random.NextDouble() - 0.5) * NatureCell * 0.9);
                    double forest = Noise.Fractal(px / 650, pz / 650, seed + 31, 3) + (road < 40 ? -0.3 : 0);
                    double conifer = Noise.Fractal(px / 420, pz / 420, seed + 41, 2);
                    double r = _random.NextDouble();
                    if (forest > 0.1)
                    {
                        if (r < (near ? 0.85 : 0.9)) TryAdd(ForestTree(conifer), px, pz, 0.8 + (_random.NextDouble() * 0.45));
                        else if (near && r < 0.89) TryAdd(SceneryKind.Rock, px, pz, 0.6 + (_random.NextDouble() * 0.9));
                    }
                    else if (!near)
                    {
                        continue;
                    }
                    else if (forest > 0.04)
                    {
                        if (r < 0.45) TryAdd(SceneryKind.Bush, px, pz, 0.8 + (_random.NextDouble() * 0.6));
                        else if (r < 0.65) TryAdd(conifer > 0.1 ? SceneryKind.Spruce : SceneryKind.Birch, px, pz, 0.7 + (_random.NextDouble() * 0.4));
                    }
                    else
                    {
                        if (r < 0.035) TryAdd(_random.NextDouble() < 0.65 ? SceneryKind.Oak : SceneryKind.Birch, px, pz, 0.85 + (_random.NextDouble() * 0.35));
                        else if (r < 0.09) TryAdd(SceneryKind.Bush, px, pz, 0.7 + (_random.NextDouble() * 0.6));
                        else if (r < 0.1) TryAdd(SceneryKind.Rock, px, pz, 0.5 + (_random.NextDouble() * 0.8));
                    }
                }
            }
        }

        private SceneryKind ForestTree(double conifer)
        {
            double r = _random.NextDouble();
            if (conifer > 0) return r < 0.72 ? SceneryKind.Spruce : r < 0.95 ? SceneryKind.Pine : SceneryKind.Birch;
            return r < 0.5 ? SceneryKind.Oak : r < 0.82 ? SceneryKind.Birch : SceneryKind.Spruce;
        }

        // ------------------------------------------------------------------ placement helpers

        private void TryAdd(SceneryKind kind, double x, double z, double scale, bool keepFromRoad = true, bool avoidWater = true)
        {
            if (x < _terrain.MinX + 5 || z < _terrain.MinZ + 5 || x > _terrain.MaxX - 5 || z > _terrain.MaxZ - 5) return;
            double r = SceneryModels.Radius(kind) * scale;
            if (avoidWater && _terrain.IsWater(x, z, r + 2)) return;
            if (keepFromRoad && RoadDistance(x, z) < 6 + r) return;
            if (IsTaken(x, z, kind is SceneryKind.Reed or SceneryKind.Bush or SceneryKind.Rock ? r * 0.5 : r * 0.8)) return;
            if (Fields.Any(f => f.Contains(x, z, 1))) return;
            Add(kind, _random.Next(SceneryModels.Variants), x, z, (float)(_random.NextDouble() * 360), scale, r * 0.8, kind == SceneryKind.Reed ? -0.25 : -0.12);
        }

        private void Add(SceneryKind kind, int variant, double x, double z, float rotation, double scale, double radius, double sink)
        {
            Items.Add(new SceneryItem
            {
                Kind = kind,
                Variant = variant,
                X = (float)x,
                Z = (float)z,
                Y = (float)(_terrain.Height(x, z) + sink),
                RotationDeg = rotation,
                Scale = (float)scale,
            });
            Take(x, z, radius);
        }

        private void Take(double x, double z, double r)
        {
            long key = Key(x, z);
            if (!_taken.TryGetValue(key, out var list)) _taken[key] = list = new List<(double, double, double)>();
            list.Add((x, z, r));
        }

        private bool IsTaken(double x, double z, double r)
        {
            int cx = (int)Math.Floor(x / 32), cz = (int)Math.Floor(z / 32);
            int reach = (int)Math.Ceiling((r + 12) / 32);
            for (int i = cx - reach; i <= cx + reach; i++)
            {
                for (int j = cz - reach; j <= cz + reach; j++)
                {
                    if (!_taken.TryGetValue(((long)i << 32) ^ (uint)j, out var list)) continue;
                    foreach (var (ox, oz, or) in list)
                    {
                        if (((ox - x) * (ox - x)) + ((oz - z) * (oz - z)) < (or + r) * (or + r)) return true;
                    }
                }
            }

            return false;
        }

        private static long Key(double x, double z) => ((long)(int)Math.Floor(x / 32) << 32) ^ (uint)(int)Math.Floor(z / 32);

        /// <summary>Approximate distance to the nearest road everywhere (10 m grid, chamfer distance transform).</summary>
        private void RoadDistanceField()
        {
            _gx = (int)Math.Ceiling((_terrain.MaxX - _terrain.MinX) / Cell) + 1;
            _gz = (int)Math.Ceiling((_terrain.MaxZ - _terrain.MinZ) / Cell) + 1;
            var d = new float[_gx * _gz];
            for (int i = 0; i < d.Length; i++) d[i] = float.MaxValue;
            foreach (var e in _map.Edges)
            {
                for (int i = 0; i < e.X.Length; i++)
                {
                    int ix = (int)Math.Round((e.X[i] - _terrain.MinX) / Cell), iz = (int)Math.Round((e.Z[i] - _terrain.MinZ) / Cell);
                    if (ix >= 0 && iz >= 0 && ix < _gx && iz < _gz) d[(iz * _gx) + ix] = 0;
                }
            }

            float a = (float)Cell, b = (float)(Cell * Math.Sqrt(2));
            for (int z = 0; z < _gz; z++)
            {
                for (int x = 0; x < _gx; x++)
                {
                    int i = (z * _gx) + x;
                    if (x > 0) d[i] = Math.Min(d[i], d[i - 1] + a);
                    if (z > 0) d[i] = Math.Min(d[i], d[i - _gx] + a);
                    if (x > 0 && z > 0) d[i] = Math.Min(d[i], d[i - _gx - 1] + b);
                    if (x < _gx - 1 && z > 0) d[i] = Math.Min(d[i], d[i - _gx + 1] + b);
                }
            }

            for (int z = _gz - 1; z >= 0; z--)
            {
                for (int x = _gx - 1; x >= 0; x--)
                {
                    int i = (z * _gx) + x;
                    if (x < _gx - 1) d[i] = Math.Min(d[i], d[i + 1] + a);
                    if (z < _gz - 1) d[i] = Math.Min(d[i], d[i + _gx] + a);
                    if (x < _gx - 1 && z < _gz - 1) d[i] = Math.Min(d[i], d[i + _gx + 1] + b);
                    if (x > 0 && z < _gz - 1) d[i] = Math.Min(d[i], d[i + _gx - 1] + b);
                }
            }

            _roadDistance = d;
        }

        /// <summary>Distance to the nearest road: exact close by, from the distance field further away.</summary>
        private double RoadDistance(double x, double z)
        {
            int ix = (int)Math.Round((x - _terrain.MinX) / Cell), iz = (int)Math.Round((z - _terrain.MinZ) / Cell);
            double coarse = ix >= 0 && iz >= 0 && ix < _gx && iz < _gz ? _roadDistance[(iz * _gx) + ix] : double.MaxValue;
            if (coarse > 40) return coarse;
            return _terrain.DistanceToRoad(x, z, 60, out _, out _, out _);
        }

        private static (double X, double Z) At(RoadEdge e, double s)
        {
            int i = Math.Max(1, Math.Min(e.X.Length - 1, Array.BinarySearch(e.D, s) is int k && k >= 0 ? k : ~k));
            double f = (s - e.D[i - 1]) / (e.D[i] - e.D[i - 1]);
            return (e.X[i - 1] + ((e.X[i] - e.X[i - 1]) * f), e.Z[i - 1] + ((e.Z[i] - e.Z[i - 1]) * f));
        }

        private static (double X, double Z) Tangent(RoadEdge e, double s)
        {
            var (x0, z0) = At(e, Math.Max(0, s - 5));
            var (x1, z1) = At(e, Math.Min(e.Length, s + 5));
            double l = Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((z1 - z0) * (z1 - z0)));
            return ((x1 - x0) / l, (z1 - z0) / l);
        }

        private static (double X, double Z) Normal(RoadEdge e, double s)
        {
            var (tx, tz) = Tangent(e, s);
            return (tz, -tx);
        }

        private static (double X, double Z) NormalAt(double[] xs, double[] zs, int i)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(xs.Length - 1, i + 1);
            double dx = xs[b] - xs[a], dz = zs[b] - zs[a];
            double l = Math.Sqrt((dx * dx) + (dz * dz));
            return (dz / l, -dx / l);
        }
    }
}
