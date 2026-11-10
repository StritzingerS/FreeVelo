using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>A village: its centre (the road node), how far it reaches along the roads, and what it has.</summary>
    public sealed class Village
    {
        public Village(int node, string name, double x, double z, double radius)
        {
            Node = node;
            Name = name;
            X = x;
            Z = z;
            Radius = radius;
        }

        public int Node { get; }
        public string Name { get; }
        public double X { get; }
        public double Z { get; }
        public double Radius { get; }
    }

    /// <summary>A village sign at a village entrance (the app writes the name on it).</summary>
    public readonly struct VillageSignPlace
    {
        public VillageSignPlace(SceneryItem item, string name)
        {
            Item = item;
            Name = name;
        }

        public SceneryItem Item { get; }
        public string Name { get; }
    }

    /// <summary>
    /// What people built (the same in every world, deterministic): the villages along the roads – houses facing the
    /// street, one church with the maypole (in May) and a linden in front, inns and shops near the centre, gardens with
    /// fruit trees, the name signs at the entrances and a wayside chapel outside; farms out on the valley floors every
    /// kilometre or two – the farmhouse with its barn, a big tree, an orchard, a fenced pasture and fields; hay huts in
    /// the meadows; and the alpine huts and pasture on the plateau. Nature grows around it (<see cref="MountainLook"/>),
    /// never on it (<see cref="Blocked"/>).
    /// </summary>
    public sealed class Landscape
    {
        private const double BlockCell = 8;
        private const double IndexCell = MountainLook.ChunkSize;

        private readonly Terrain _terrain;
        private readonly WorldMap _map;
        private readonly Random _random;
        private readonly bool _may;
        private readonly HashSet<long> _blocked = new HashSet<long>();
        private readonly List<(double X, double Z, double R)> _footprints = new List<(double, double, double)>();
        private readonly List<(double AX, double AZ, double BX, double BZ)> _lanes = new List<(double, double, double, double)>();

        /// <summary>The side lanes, from the road's edge outwards: asphalt in the villages, gravel to the farms (the app drapes them on the ground).</summary>
        public List<(double AX, double AZ, double BX, double BZ, bool Gravel)> Lanes { get; } = new List<(double, double, double, double, bool)>();
        private readonly Dictionary<long, List<SceneryItem>> _itemsByChunk = new Dictionary<long, List<SceneryItem>>();
        private readonly Dictionary<long, List<FieldPatch>> _fieldsByChunk = new Dictionary<long, List<FieldPatch>>();

        private Landscape(Terrain terrain, int seed, bool may)
        {
            _terrain = terrain;
            _map = terrain.Map;
            _random = new Random(seed);
            _may = may;
        }

        public List<Village> Villages { get; } = new List<Village>();

        /// <summary>Buildings, garden and farm trees, fences, chapels, hay huts, the maypoles (in May) and the signs.</summary>
        public List<SceneryItem> Items { get; } = new List<SceneryItem>();

        /// <summary>Fields (crops, draped over the ground).</summary>
        public List<FieldPatch> Fields { get; } = new List<FieldPatch>();

        /// <summary>Fenced pastures (meadow; nothing else grows there).</summary>
        public List<FieldPatch> Pastures { get; } = new List<FieldPatch>();

        public List<VillageSignPlace> Signs { get; } = new List<VillageSignPlace>();

        /// <summary>Junction signposts: the post and, per board, an arrow ("←", "↑", "→") and where the road leads.</summary>
        public List<(SceneryItem Item, (string Arrow, string Name)[] Boards)> Signposts { get; } = new List<(SceneryItem, (string, string)[])>();

        /// <summary>
        /// Lays out villages, farms and huts. <paramref name="today"/> decides about the maypoles: they only stand in May
        /// (the rest is the same every day).
        /// </summary>
        public static Landscape Generate(Terrain terrain, DateTime today, int seed = 11)
        {
            var l = new Landscape(terrain, seed, today.Month == 5);
            l.PlaceVillages();
            l.PlaceAlm();
            l.PlaceFarms();
            l.PlaceSignposts();
            l.PlaceFields();
            l.PlaceHayHuts();
            foreach (var item in l.Items) l.Index(item);
            return l;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Something built, a field, a pasture or a village street is here: no trees or rocks.</summary>
        public bool Blocked(double x, double z) => _blocked.Contains(Key((int)Math.Floor(x / BlockCell), (int)Math.Floor(z / BlockCell)));

        /// <summary>The items in one 128 m chunk (<see cref="MountainLook.ChunkSize"/>).</summary>
        public IReadOnlyList<SceneryItem> ItemsIn(int chunkX, int chunkZ) =>
            _itemsByChunk.TryGetValue(Key(chunkX, chunkZ), out var list) ? list : (IReadOnlyList<SceneryItem>)Array.Empty<SceneryItem>();

        /// <summary>The field at a point, or null.</summary>
        public FieldPatch? FieldAt(double x, double z)
        {
            if (!_fieldsByChunk.TryGetValue(Key((int)Math.Floor(x / IndexCell), (int)Math.Floor(z / IndexCell)), out var list)) return null;
            foreach (var f in list)
            {
                if (f.Contains(x, z)) return f;
            }

            return null;
        }

        /// <summary>The village a point lies in (within its radius of the centre), or null.</summary>
        public Village? VillageAt(double x, double z) =>
            Villages.FirstOrDefault(v => Math.Sqrt(((v.X - x) * (v.X - x)) + ((v.Z - z) * (v.Z - z))) < v.Radius);

        // ------------------------------------------------------------------ villages

        private sealed class Plan
        {
            public double Radius;
            public bool Church;
            public int Inns;
            public int Shops;
            public bool Hamlet;
        }

        private static Plan PlanFor(string name) => name switch
        {
            "Lärchwald" => new Plan { Radius = 420, Church = true, Inns = 2, Shops = 2 },
            "Kirchbach" => new Plan { Radius = 380, Church = true, Inns = 1, Shops = 1 },
            "Mitterbach" => new Plan { Radius = 400, Church = true, Inns = 1, Shops = 2 },
            "Hinterau" => new Plan { Radius = 280, Inns = 1 },
            "Trafon" => new Plan { Radius = 380, Church = true, Inns = 2, Shops = 1 },
            "Valbrune" => new Plan { Radius = 330, Church = true, Inns = 2, Shops = 1 },
            "Oberalm" => new Plan { Radius = 170, Inns = 1, Hamlet = true },
            _ => new Plan { Radius = 250 },
        };

        /// <summary>A spot beside a road: the road point, its direction and the left normal.</summary>
        private (double X, double Z, double TX, double TZ, double NX, double NZ) Along(RoadEdge e, double d)
        {
            int i = Math.Max(1, Math.Min(e.X.Length - 2, Array.BinarySearch(e.D, d) is int k && k >= 0 ? k : ~k));
            double tx = e.X[i + 1] - e.X[i - 1], tz = e.Z[i + 1] - e.Z[i - 1], l = Math.Sqrt((tx * tx) + (tz * tz));
            tx /= l;
            tz /= l;
            return (e.X[i], e.Z[i], tx, tz, -tz, tx);
        }

        private void PlaceVillages()
        {
            var seen = new HashSet<string>();
            foreach (var node in _map.Nodes)
            {
                if (string.IsNullOrEmpty(node.Name) || !seen.Add(node.Name)) continue;
                if (_map.Edges.Any(e => e.Kind == RoadKind.Climb && e.To == node.Id)) continue; // the summits
                var plan = PlanFor(node.Name);
                var village = new Village(node.Id, node.Name, node.X, node.Z, plan.Radius);
                Villages.Add(village);
                PlaceVillage(village, plan);
            }
        }

        private void PlaceVillage(Village village, Plan plan)
        {
            var roads = _map.Nodes[village.Node].Edges.Select(id => _map.Edges[id]).Where(e => e.Kind != RoadKind.Climb || true).ToList();

            // Lots along every road out of the village, both sides, denser near the centre.
            var lots = new List<(double S, SceneryItem Item, double TowardX, double TowardZ, double AlongX, double AlongZ)>();
            foreach (var e in roads)
            {
                bool outward = e.From == village.Node;
                double reach = Math.Min(plan.Radius, e.Length - 60);
                for (double s = 32; s < reach; s += 26 + (_random.NextDouble() * 16))
                {
                    double d = outward ? s : e.Length - s;
                    var a = Along(e, d);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        double density = 0.92 - (0.45 * s / plan.Radius);
                        if (_random.NextDouble() > density) continue;
                        double setback = (e.Width / 2) + 5 + 6.5 + (_random.NextDouble() * 4);
                        double x = a.X + (a.NX * side * setback), z = a.Z + (a.NZ * side * setback);
                        var item = new SceneryItem { X = (float)x, Z = (float)z, RotationDeg = (float)Facing(-a.NX * side, -a.NZ * side) };
                        lots.Add((s, item, -a.NX * side, -a.NZ * side, a.TX, a.TZ));
                    }
                }
            }

            lots = lots.OrderBy(l => l.S).ToList();

            // The church first: a lot near the centre with room behind (its tower towards the road).
            if (plan.Church)
            {
                foreach (var lot in lots.Where(l => l.S > 45 && l.S < 260))
                {
                    double back = 15; // further from the road than a house
                    double cx = lot.Item.X - (lot.TowardX * back), cz = lot.Item.Z - (lot.TowardZ * back);
                    if (!Free(cx, cz, SceneryModels.Radius(SceneryKind.Church) + 3, roadClear: 21)) continue;
                    if (!FootprintClear(SceneryKind.Church, 0, cx, cz, lot.Item.RotationDeg)) continue; // the graveyard wall too
                    Place(SceneryKind.Church, cx, cz, lot.Item.RotationDeg, _random.Next(2));
                    // In front: the maypole (in May) and a big linden.
                    double fx = cx + (lot.TowardX * 22), fz = cz + (lot.TowardZ * 22);
                    // Both off every road (the square in front can touch another road leaving the village).
                    bool OffRoad(double x, double z, double clear) => _terrain.DistanceToRoad(x, z, clear, out _, out _, out _) >= clear;
                    if (_may && OffRoad(fx + (lot.AlongX * 12), fz + (lot.AlongZ * 12), 6)) Place(SceneryKind.Maypole, fx + (lot.AlongX * 12), fz + (lot.AlongZ * 12), 0, 0, block: false);
                    foreach (double side in new[] { -13.0, 13.0, -20.0 })
                    {
                        double lx = fx + (lot.AlongX * side), lz = fz + (lot.AlongZ * side);
                        if (!OffRoad(lx, lz, 9)) continue;
                        Place(SceneryKind.Linden, lx, lz, _random.Next(360), 2, block: false);
                        break;
                    }
                    break;
                }
            }
            else
            {
                // Villages without a church have a chapel in the middle.
                var lot = lots.FirstOrDefault(l => l.S > 40 && Free(l.Item.X, l.Item.Z, 6, roadClear: 6));
                if (lot.Item.Z != 0 || lot.Item.X != 0) Place(SceneryKind.Shrine, lot.Item.X + (lot.TowardX * 4), lot.Item.Z + (lot.TowardZ * 4), lot.Item.RotationDeg, 0);
            }

            // Side lanes off the main roads, with a second row of houses along them: the village gets depth.
            var laneLots = new List<(double X, double Z, double TowardX, double TowardZ, double AlongX, double AlongZ, double Rot)>();
            if (!plan.Hamlet)
            {
                foreach (var e in roads)
                {
                    bool outward = e.From == village.Node;
                    for (double s = plan.Radius * 0.2; s < Math.Min(plan.Radius * 0.8, e.Length - 80); s += 110 + (_random.NextDouble() * 70))
                    {
                        var a = Along(e, outward ? s : e.Length - s);
                        int side = _random.NextDouble() < 0.5 ? -1 : 1;
                        double turn = (_random.NextDouble() - 0.5) * 0.35;
                        double lx = (a.NX * side * Math.Cos(turn)) - (a.TX * Math.Sin(turn)), lz = (a.NZ * side * Math.Cos(turn)) - (a.TZ * Math.Sin(turn));
                        double length = 70 + (_random.NextDouble() * 70);
                        double sx = a.X + (lx * ((e.Width / 2) + 0.3)), sz = a.Z + (lz * ((e.Width / 2) + 0.3));
                        if (!AddLane(sx, sz, sx + (lx * length), sz + (lz * length), gravel: false)) continue;
                        // Houses along the lane, both sides, facing it
                        double px = -lz, pz = lx;
                        for (double t = 22; t < length - 6; t += 26 + (_random.NextDouble() * 10))
                        {
                            foreach (int ls in new[] { -1, 1 })
                            {
                                double off = 1.7 + 5 + 6 + (_random.NextDouble() * 3);
                                double hx = sx + (lx * t) + (px * ls * off), hz = sz + (lz * t) + (pz * ls * off);
                                laneLots.Add((hx, hz, -px * ls, -pz * ls, lx, lz, Facing(-px * ls, -pz * ls)));
                            }
                        }
                    }
                }
            }

            int inns = plan.Inns, shops = plan.Shops;
            foreach (var lot in lots)
            {
                var kind = inns > 0 ? SceneryKind.Inn
                    : shops > 0 ? SceneryKind.Shop
                    : plan.Hamlet && _random.NextDouble() < 0.5 ? SceneryKind.AlpineHut
                    : lot.S > 0.7 * plan.Radius && _random.NextDouble() < 0.25 ? SceneryKind.Farmhouse
                    : SceneryKind.AlpineHouse;
                double r = SceneryModels.Radius(kind);
                // Bigger buildings stand a little further back, so their front lines up with the houses.
                double extra = r - 9;
                double x = lot.Item.X - (lot.TowardX * extra), z = lot.Item.Z - (lot.TowardZ * extra);
                if (!Free(x, z, r + 1.5, roadClear: (r * 0.75) + 5)) continue;
                int variant = _random.Next(3);
                if (!FootprintClear(kind, variant, x, z, lot.Item.RotationDeg)) continue;
                Place(kind, x, z, lot.Item.RotationDeg, variant);
                Plot(kind, variant, x, z, lot.TowardX, lot.TowardZ, lot.AlongX, lot.AlongZ, lot.Item.RotationDeg);
                if (kind == SceneryKind.Inn) inns--;
                else if (kind == SceneryKind.Shop) shops--;

                // A garden tree behind some of the houses.
                if (kind == SceneryKind.AlpineHouse && _random.NextDouble() < 0.4)
                {
                    double gx = x - (lot.TowardX * 15) + (lot.AlongX * (_random.NextDouble() - 0.5) * 8), gz = z - (lot.TowardZ * 15) + (lot.AlongZ * (_random.NextDouble() - 0.5) * 8);
                    if (Free(gx, gz, 4, roadClear: 8)) Place(_random.NextDouble() < 0.6 ? SceneryKind.Apple : SceneryKind.Maple, gx, gz, _random.Next(360), _random.Next(3));
                }
            }

            foreach (var lot in laneLots)
            {
                if (!Free(lot.X, lot.Z, SceneryModels.Radius(SceneryKind.AlpineHouse) + 1.5, roadClear: 12)) continue;
                int variant = _random.Next(3);
                if (!FootprintClear(SceneryKind.AlpineHouse, variant, lot.X, lot.Z, lot.Rot)) continue;
                Place(SceneryKind.AlpineHouse, lot.X, lot.Z, lot.Rot, variant);
                Plot(SceneryKind.AlpineHouse, variant, lot.X, lot.Z, lot.TowardX, lot.TowardZ, lot.AlongX, lot.AlongZ, lot.Rot);
            }

            // Trades on the outskirts: the fire station in every village with a church, a car workshop in the bigger
            // ones, sawmills by the river in the wood villages.
            if (plan.Church) Outskirts(village, plan, roads, SceneryKind.FireStation, nearRiver: false);
            if (plan.Radius >= 380) Outskirts(village, plan, roads, SceneryKind.Workshop, nearRiver: false);
            if (village.Name is "Lärchwald" or "Hinterau" or "Kirchbach") Outskirts(village, plan, roads, SceneryKind.Sawmill, nearRiver: true);

            // No forest in the village: block the land along its streets.
            foreach (var e in roads)
            {
                bool outward = e.From == village.Node;
                for (double s = 0; s < Math.Min(plan.Radius + 30, e.Length); s += 6)
                {
                    var a = Along(e, outward ? s : e.Length - s);
                    BlockCircle(a.X, a.Z, 60);
                }
            }

            // The signs at the entrances (right-hand side coming in), and a chapel on one road out.
            bool chapel = false;
            foreach (var e in roads)
            {
                bool outward = e.From == village.Node;
                double s = plan.Radius + 25;
                // Clear of houses and farms near the entrance
                while (s < plan.Radius + 120 && _footprints.Any(f => { var p = Along(e, outward ? s : e.Length - s); return Dist(f.X, f.Z, p.X, p.Z) < f.R + 12; })) s += 10;
                if (e.Length < s + 80) continue;
                var a = Along(e, outward ? s : e.Length - s);
                double ox = outward ? a.TX : -a.TX, oz = outward ? a.TZ : -a.TZ; // pointing out of the village
                // Coming in means travelling (−ox, −oz); its right-hand side is (−oz, ox).
                double x = a.X + (-oz * 5.5), z = a.Z + (ox * 5.5);
                var sign = new SceneryItem { Kind = SceneryKind.VillageSign, X = (float)x, Y = (float)_terrain.Height(x, z), Z = (float)z, RotationDeg = (float)Facing(ox, oz), Scale = 1 };
                Items.Add(sign);
                Signs.Add(new VillageSignPlace(sign, village.Name));

                if (!chapel && !plan.Hamlet && e.Length > s + 160)
                {
                    var c = Along(e, outward ? s + 70 : e.Length - s - 70);
                    double cx = c.X + (oz * 9), cz = c.Z - (ox * 9);
                    if (Free(cx, cz, 4, roadClear: 7))
                    {
                        Place(SceneryKind.Shrine, cx, cz, Facing(-oz, ox), 0);
                        chapel = true;
                    }
                }
            }
        }

        /// <summary>
        /// A trade building on the village edge (60–100 % of its radius along one of its roads), facing the road, with
        /// what belongs to it: a gravel yard and cars at the workshop, logs and timber at the sawmill, a yard in front of
        /// the fire station. <paramref name="nearRiver"/>: try spots near the river first (sawmills).
        /// </summary>
        private void Outskirts(Village village, Plan plan, List<RoadEdge> roads, SceneryKind kind, bool nearRiver)
        {
            var (w, d) = Size(kind, 0);
            for (int pass = nearRiver ? 0 : 1; pass < 2; pass++)
            {
                foreach (var e in roads.OrderBy(_ => _random.NextDouble()))
                {
                    bool outward = e.From == village.Node;
                    for (double s = plan.Radius * 0.6; s <= plan.Radius && s < e.Length - 40; s += 14)
                    {
                        var a = Along(e, outward ? s : e.Length - s);
                        foreach (int side in _random.NextDouble() < 0.5 ? new[] { 1, -1 } : new[] { -1, 1 })
                        {
                            double setback = (e.Width / 2) + 7 + (d / 2);
                            double x = a.X + (a.NX * side * setback), z = a.Z + (a.NZ * side * setback);
                            if (pass == 0 && _terrain.DistanceToRiver(x, z, 260, out _) > 250) continue;
                            double rot = Facing(-a.NX * side, -a.NZ * side);
                            if (!Free(x, z, SceneryModels.Radius(kind) + 2, roadClear: (d / 2) + 5)) continue;
                            if (!FootprintClear(kind, 0, x, z, rot)) continue;
                            Place(kind, x, z, rot, _random.Next(2));
                            Trade(kind, x, z, -a.NX * side, -a.NZ * side, a.TX, a.TZ, rot, w, d);
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>What belongs around a trade building (local frame: towards the road, along it).</summary>
        private void Trade(SceneryKind kind, double x, double z, double tx, double tz, double ax, double az, double rot, double w, double d)
        {
            (double X, double Z) P(double u, double v) => (x + (ax * u) + (tx * v), z + (az * u) + (tz * v));
            void Put(SceneryKind k, double u, double v, double r, int variant)
            {
                var (px, pz) = P(u, v);
                if (!FenceClear((px, pz)) || _terrain.DistanceToRiver(px, pz, 15, out _) < 12) return;
                Items.Add(new SceneryItem { Kind = k, Variant = variant, X = (float)px, Y = (float)(_terrain.Height(px, pz) - 0.05), Z = (float)pz, RotationDeg = (float)r, Scale = 1 });
                _footprints.Add((px, pz, SceneryModels.Radius(k)));
                BlockCircle(px, pz, SceneryModels.Radius(k) + 1);
            }

            switch (kind)
            {
                case SceneryKind.Workshop:
                    for (double u = -(w / 2) + 3.5; u <= (w / 2) - 3.5 + 0.01; u += 7) Put(SceneryKind.Yard, u, (d / 2) + 3, rot, 0);
                    Put(SceneryKind.Car, -3.6, (d / 2) + 3.2, rot + 180 + (_random.NextDouble() * 20), _random.Next(3));
                    if (_random.NextDouble() < 0.7) Put(SceneryKind.Car, (w / 2) + 3, (d / 2) + 2.5, rot + 90 + (_random.NextDouble() * 15), _random.Next(3));
                    break;
                case SceneryKind.FireStation:
                    for (double u = -(w / 2) + 3.5; u <= (w / 2) - 3.5 + 0.01; u += 7) Put(SceneryKind.Yard, u, (d / 2) + 3, rot, 0);
                    break;
                case SceneryKind.Sawmill:
                    // Logs waiting on one side, sawn timber drying on the other.
                    for (int k = 0; k < 3; k++) Put(SceneryKind.LogPile, -(w / 2) - 6, -8 + (k * 5.5), rot + 90, k % 3);
                    for (int k = 0; k < 3; k++) Put(SceneryKind.LumberStack, (w / 2) + 4.5, -6 + (k * 4.2), rot + 90, k % 3);
                    Put(SceneryKind.Car, (w / 2) + 4, (d / 2) + 1, rot + 200, _random.Next(3));
                    break;
            }
        }

        /// <summary>Width (along the road) and depth (towards it) of a building as modelled.</summary>
        private static (double W, double D) Size(SceneryKind kind, int variant) => kind switch
        {
            SceneryKind.AlpineHouse => variant == 1 ? (11.5, 13) : variant == 2 ? (9, 10) : (10.5, 12),
            SceneryKind.Inn => (14, 17),
            SceneryKind.Shop => (10.5, 12),
            SceneryKind.Farmhouse => (12.5, 27 + (2 * variant)),
            SceneryKind.AlpineHut => (8, 10),
            SceneryKind.Workshop => (16, 13),
            SceneryKind.FireStation => (14, 13),
            SceneryKind.Sawmill => (12, 30),
            _ => (8, 8),
        };

        /// <summary>
        /// The plot round a building, so it looks built and lived in: a stone path from the road to the front door, a
        /// garden fence round house plots (a gap for the path), flower beds along the front, sometimes a bench by the
        /// door; inns and shops get a gravel forecourt to the street instead, farmhouses a gravel yard.
        /// </summary>
        private void Plot(SceneryKind kind, int variant, double x, double z, double tx, double tz, double ax, double az, double rotationDeg)
        {
            var (w, d) = Size(kind, variant);
            (double X, double Z) P(double u, double v) => (x + (ax * u) + (tx * v), z + (az * u) + (tz * v)); // v towards the road
            // The street in front: the road, or the lane for houses along a side lane.
            double roadEdge = Math.Min(_terrain.DistanceToRoad(x, z, 60, out var road, out _, out _) - ((road?.Width ?? 6.5) / 2), LaneDistance(x, z) - 1.7);
            double front = (d / 2) + 0.9; // door step
            double gap = roadEdge - front;
            if (gap < 2) return;

            void Item(SceneryKind k, double u, double v, double rot, int var)
            {
                var (px, pz) = P(u, v);
                Items.Add(new SceneryItem { Kind = k, Variant = var, X = (float)px, Y = (float)(_terrain.Height(px, pz) - 0.05), Z = (float)pz, RotationDeg = (float)rot, Scale = 1 });
            }

            if (kind is SceneryKind.Inn or SceneryKind.Shop || kind == SceneryKind.Farmhouse)
            {
                // Gravel forecourt / yard between the building and the street
                for (double u = -(w / 2) + 3.5; u <= (w / 2) - 3.5 + 0.01; u += 7)
                {
                    for (double v = front + 3; v < roadEdge - 0.5; v += 6) Item(SceneryKind.Yard, u, v, rotationDeg, 0);
                }

                if (kind == SceneryKind.Inn)
                {
                    Item(SceneryKind.Bench, (w / 2) - 2, front + 0.6, rotationDeg + 180, 0);
                    BeerGarden(w, d, P, (k, u, v, r, var) => Item(k, u, v, r, var), rotationDeg);
                }

                return;
            }

            // Stone path from the road to the door
            for (double v = front + 2; v < roadEdge + 1; v += 3.8) Item(SceneryKind.PathStone, 0, v, rotationDeg, 0);

            // Flower beds along the front wall, either side of the door
            foreach (int side in new[] { -1, 1 }) Item(SceneryKind.FlowerBed, side * ((w / 4) + 0.9), (d / 2) + 0.6, rotationDeg, 0);
            if (_random.NextDouble() < 0.5) Item(SceneryKind.Bench, (_random.NextDouble() < 0.5 ? -1 : 1) * (w / 2 - 1.4), (d / 2) + 0.55, rotationDeg + 180, 0);

            // Garden fence: front (with a gap for the path, 1.5 m from the road edge), sides and back
            double fenceFront = Math.Min(roadEdge - 1.5, front + 7), back = -(d / 2) - 9, half = (w / 2) + 4;
            if (fenceFront < front + 1.5) return;
            void Run(double u0, double v0, double u1, double v1, double rot, double skipFrom = 1, double skipTo = -1)
            {
                double length = Math.Sqrt(((u1 - u0) * (u1 - u0)) + ((v1 - v0) * (v1 - v0)));
                int pieces = (int)Math.Round(length / 3);
                for (int i = 0; i < pieces; i++)
                {
                    double f = (i + 0.5) / pieces;
                    double u = u0 + ((u1 - u0) * f), v = v0 + ((v1 - v0) * f);
                    if (u > skipFrom && u < skipTo) continue;
                    // Both ends of the piece (and its middle) clear of every road and the water.
                    double du = (u1 - u0) / length * 1.6, dv = (v1 - v0) / length * 1.6;
                    if (!FenceClear(P(u - du, v - dv), P(u, v), P(u + du, v + dv))) continue;
                    Item(SceneryKind.GardenFence, u, v, rot, 0);
                }
            }

            double across = rotationDeg + 90; // the fence model runs along X: front and back follow the road
            Run(-half, fenceFront, half, fenceFront, rotationDeg, -1.8, 1.8);
            Run(-half, back, half, back, rotationDeg);
            Run(-half, back, -half, fenceFront, across);
            Run(half, back, half, fenceFront, across);
            BlockPlot(P(0, (fenceFront + back) / 2), half, (fenceFront - back) / 2, ax, az);
        }

        /// <summary>
        /// The inn's Gastgarten beside the house: gravel, tables with benches (some with parasols), a big shady tree on
        /// the outer side and a low fence round it.
        /// </summary>
        private void BeerGarden(double w, double d, Func<double, double, (double X, double Z)> p, Action<SceneryKind, double, double, double, int> item, double rotationDeg)
        {
            foreach (int side in _random.NextDouble() < 0.5 ? new[] { 1, -1 } : new[] { -1, 1 })
            {
                double cu = side * ((w / 2) + 8), half = 6.5, halfV = 8;
                var c = p(cu, 0);
                bool room = true;
                foreach (var (u, v) in new[] { (cu, 0.0), (cu - half, -halfV), (cu + half, -halfV), (cu - half, halfV), (cu + half, halfV) })
                {
                    var q = p(u, v);
                    if (_terrain.IsWater(q.X, q.Z, 2) || _terrain.DistanceToRoad(q.X, q.Z, 5, out _, out _, out _) < 5) room = false;
                }

                var inn = p(0, 0);
                if (!room || _footprints.Any(f => Dist(f.X, f.Z, c.X, c.Z) < f.R + 6 && Dist(f.X, f.Z, inn.X, inn.Z) > 0.5)) continue;

                for (double u = cu - 3.5; u <= cu + 3.5; u += 7)
                {
                    for (double v = -halfV + 3; v <= halfV - 3; v += 6) item(SceneryKind.Yard, u, v, rotationDeg, 0);
                }

                for (int i = 0; i < 2; i++)
                {
                    for (int j = 0; j < 3; j++) item(SceneryKind.BeerTable, cu - 1.8 + (i * 3.6), -5 + (j * 4.2), rotationDeg + 90, (i + j) % 3);
                }

                item(_random.NextDouble() < 0.5 ? SceneryKind.Linden : SceneryKind.Maple, cu + (side * (half + 1)), 2, _random.Next(360), 1);
                double rot = rotationDeg + 90;
                void Fence(double u, double v, double r, double du, double dv)
                {
                    if (FenceClear(p(u - du, v - dv), p(u, v), p(u + du, v + dv))) item(SceneryKind.GardenFence, u, v, r, 0);
                }

                for (int k = 0; k < 5; k++) Fence(cu + (side * (half + 0.3)), -halfV + 1.5 + (k * 3), rot, 0, 1.6);
                for (int k = 0; k < 4; k++)
                {
                    Fence(cu - half + 1.5 + (k * 3.2), -halfV, rotationDeg, 1.6, 0);
                    Fence(cu - half + 1.5 + (k * 3.2), halfV, rotationDeg, 1.6, 0);
                }

                _footprints.Add((c.X, c.Z, 9));
                BlockCircle(c.X, c.Z, 11);
                return;
            }
        }

        /// <summary>
        /// The ground a building covers, in its own frame (front = +Z): half width across, and from the back to the
        /// front – with roof overhangs, balconies, ramps, terraces and, for the church, the graveyard wall and the tower.
        /// </summary>
        public static (double HalfX, double Back, double Front, double RightExtra) Outline(SceneryKind kind, int variant)
        {
            var (w, d) = Size(kind, variant);
            return kind switch
            {
                SceneryKind.Church => (12.8, -23.6, 17.6, 0),
                SceneryKind.Workshop => ((w / 2) + 1.2, -(d / 2) - 1.2, (d / 2) + 1.2, 5.2), // the office on the right
                SceneryKind.Sawmill => ((w / 2) + 1.4, -(d / 2) - 1.4, (d / 2) + 1.4, 0),
                SceneryKind.FireStation => ((w / 2) + 1.4, -(d / 2) - 1.4, (d / 2) + 1.4, 0),
                SceneryKind.Farmhouse => ((w / 2) + 1.4, -(d / 2) - 1.6, (d / 2) + 1.6, 4.4), // the ramp on the right
                SceneryKind.Inn => ((w / 2) + 1.6, -(d / 2) - 1.6, (d / 2) + 3.2, 0),     // terrace in front
                _ => ((w / 2) + 1.5, -(d / 2) - 1.6, (d / 2) + 1.6, 0),
            };
        }

        /// <summary>A building's whole outline (see <see cref="Outline"/>) is off the roads (1.5 m from the asphalt) and dry.</summary>
        private bool FootprintClear(SceneryKind kind, int variant, double x, double z, double rotationDeg)
        {
            foreach (var (px, pz) in OutlinePoints(kind, variant, x, z, rotationDeg, 2))
            {
                if (_terrain.IsWater(px, pz, 1)) return false;
                double d = _terrain.DistanceToRoad(px, pz, 12, out var road, out _, out _);
                if (road != null && d < (road.Width / 2) + 1.5) return false;
                if (LaneDistance(px, pz) < 2.6) return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ lanes

        /// <summary>Distance to the nearest lane's centre line (∞ if there are none).</summary>
        private double LaneDistance(double x, double z)
        {
            double best = double.MaxValue;
            foreach (var (ax, az, bx, bz) in _lanes)
            {
                double dx = bx - ax, dz = bz - az, l2 = (dx * dx) + (dz * dz);
                double t = l2 > 0 ? Math.Max(0, Math.Min(1, (((x - ax) * dx) + ((z - az) * dz)) / l2)) : 0;
                best = Math.Min(best, Dist(x, z, ax + (dx * t), az + (dz * t)));
            }

            return best;
        }

        /// <summary>
        /// A narrow lane from (ax, az) – at a road's edge – to (bx, bz): asphalt in the villages, gravel to the farms.
        /// Only if all of it is dry, not too steep, clear of the river, other roads, lanes and buildings.
        /// </summary>
        private bool AddLane(double ax, double az, double bx, double bz, bool gravel)
        {
            double length = Dist(ax, az, bx, bz);
            if (length < 12) return false;
            double dx = (bx - ax) / length, dz = (bz - az) / length;
            for (double t = 0; t <= length; t += 4)
            {
                double x = ax + (dx * t), z = az + (dz * t);
                if (_terrain.IsWater(x, z, 3) || _terrain.DistanceToRiver(x, z, 16, out _) < 14 || _terrain.Slope(x, z) > 0.18) return false;
                if (t > 8 && _terrain.DistanceToRoad(x, z, 12, out _, out _, out _) < 10) return false;
                if (LaneDistance(x, z) < 12) return false;
                foreach (var f in _footprints)
                {
                    if (Dist(f.X, f.Z, x, z) < f.R + 2) return false;
                }

                foreach (var p in Fields.Concat(Pastures))
                {
                    if (p.Contains(x, z, 3)) return false;
                }
            }

            for (double t = 2.5; t < length; t += 5) BlockCircle(ax + (dx * t), az + (dz * t), 3);
            _lanes.Add((ax, az, bx, bz));
            Lanes.Add((ax, az, bx, bz, gravel));
            return true;
        }

        /// <summary>Points round a building's outline (and across it), every <paramref name="step"/> metres, in world space.</summary>
        public static IEnumerable<(double X, double Z)> OutlinePoints(SceneryKind kind, int variant, double x, double z, double rotationDeg, double step)
        {
            var (hx, back, front, right) = Outline(kind, variant);
            double r = rotationDeg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            for (double lz = back; lz <= front + 0.01; lz += step)
            {
                for (double lx = -hx; lx <= hx + right + 0.01; lx += step)
                {
                    bool edge = lz - back < step || front - lz < step || lx + hx < step || hx + right - lx < step;
                    if (!edge && ((int)((lz - back) / step) % 3 != 0)) continue; // the rim, and a few lines across
                    yield return (x + (lx * c) + (lz * s), z - (lx * s) + (lz * c));
                }
            }
        }

        /// <summary>A fence piece through these points stays off the roads (1 m from the asphalt) and out of the water.</summary>
        private bool FenceClear(params (double X, double Z)[] points)
        {
            foreach (var (x, z) in points)
            {
                if (_terrain.IsWater(x, z, 1)) return false;
                double d = _terrain.DistanceToRoad(x, z, 12, out var road, out _, out _);
                if (road != null && d < (road.Width / 2) + 1.0) return false;
                if (LaneDistance(x, z) < 2.4) return false;
            }

            return true;
        }

        private void BlockPlot((double X, double Z) c, double halfU, double halfV, double ax, double az)
        {
            double angle = Math.Atan2(ax, az);
            BlockPatch(new FieldPatch(c.X, c.Z, halfU, halfV, angle, WorldMaterial.Meadow), 1);
        }

        // ------------------------------------------------------------------ signposts

        /// <summary>
        /// At every junction, a signpost on the right of each road coming in, 30 m before it: one board per way on,
        /// with an arrow and the next village (or the climb's name).
        /// </summary>
        private void PlaceSignposts()
        {
            foreach (var node in _map.Nodes.Where(n => n.Edges.Count >= 3))
            {
                foreach (int inId in node.Edges)
                {
                    var e = _map.Edges[inId];
                    bool fromHere = e.From == node.Id;
                    if (e.Length < 80) continue;
                    // Coming in: travelling towards the node along e.
                    var a = Along(e, fromHere ? 30 : e.Length - 30);
                    double dx = fromHere ? -a.TX : a.TX, dz = fromHere ? -a.TZ : a.TZ;
                    double x = 0, z = 0;
                    bool clear = false;
                    foreach (double off in new[] { 5.6, 7.0, 9.0 })
                    {
                        x = a.X + (dz * off); z = a.Z - (dx * off); // right-hand side: (dz, −dx)
                        double d = _terrain.DistanceToRoad(x, z, 12, out var road, out _, out _);
                        if (road == null || d >= (road.Width / 2) + 1) { clear = true; break; }
                    }

                    if (!clear) continue;
                    var boards = new List<(double Turn, string Arrow, string Name)>();
                    foreach (int outId in node.Edges.Where(id => id != inId))
                    {
                        var o = _map.Edges[outId];
                        bool outFrom = o.From == node.Id;
                        var b = Along(o, outFrom ? 40 : o.Length - 40);
                        double ox = b.X - node.X, oz = b.Z - node.Z, ol = Math.Sqrt((ox * ox) + (oz * oz));
                        double turn = Math.Atan2((dx * oz) - (dz * ox), (dx * ox) + (dz * oz)) * 180 / Math.PI; // + = left
                        string arrow = Math.Abs(turn) < 30 ? "↑" : turn > 0 ? "←" : "→";
                        string name = o.Kind == RoadKind.Climb ? (_map.Mountain?.Find(o.ClimbId!)?.Data.Name ?? "") : _map.Nodes[outFrom ? o.To : o.From].Name;
                        if (o.Kind == RoadKind.Climb && o.ClimbId == ClimbData.Stelvio) name = "Passo dello Stelvio";
                        if (string.IsNullOrEmpty(name) || ol < 1) continue;
                        boards.Add((turn, arrow, name));
                    }

                    if (boards.Count == 0 || _terrain.IsWater(x, z, 1)) continue;
                    var ordered = boards.OrderByDescending(b => b.Turn).Take(3).Select(b => (b.Arrow, b.Name)).ToArray(); // left first
                    var item = new SceneryItem { Kind = SceneryKind.DirectionSign, Variant = ordered.Length - 1, X = (float)x, Y = (float)_terrain.Height(x, z), Z = (float)z, RotationDeg = (float)Facing(-dx, -dz), Scale = 1 };
                    Items.Add(item);
                    Signposts.Add((item, ordered));
                    BlockCircle(x, z, 2);
                }
            }
        }

        // ------------------------------------------------------------------ the alm

        private void PlaceAlm()
        {
            // Alpine huts, each in its own fenced yard, scattered over the Alm meadows along the plateau and up the
            // Valbrune road: the high pastures look different from the farms in the valley.
            foreach (var e in _map.Edges.Where(e => e.Kind == RoadKind.Pass))
            {
                for (double s = 120; s < e.Length - 120; s += 160 + (_random.NextDouble() * 120))
                {
                    var pt = Along(e, s);
                    double h = _terrain.Height(pt.X, pt.Z);
                    if (h < 950 || _map.Nodes.Any(n => Math.Abs(n.X - pt.X) + Math.Abs(n.Z - pt.Z) < 350)) continue;
                    if (_random.NextDouble() > 0.6) continue;
                    int side = _random.NextDouble() < 0.5 ? -1 : 1;
                    double off = 40 + (_random.NextDouble() * 140);
                    double cx = pt.X + (pt.NX * side * off), cz = pt.Z + (pt.NZ * side * off);
                    double angle = Math.Atan2(pt.TX, pt.TZ) + ((_random.NextDouble() - 0.5) * 0.6);
                    var yard = new FieldPatch(cx, cz, 15 + (_random.NextDouble() * 6), 12 + (_random.NextDouble() * 4), angle, WorldMaterial.Meadow);
                    if (_terrain.Slope(cx, cz) > 0.28 || !PatchFree(yard, roadClear: 12)) continue;
                    AddPasture(yard);
                    double rot = (angle * 180 / Math.PI) + (_random.NextDouble() < 0.5 ? 0 : 180);
                    Place(SceneryKind.AlpineHut, cx, cz, rot, _random.Next(3));
                    var (bx, bz) = yard.Point(yard.HalfLength - 4, yard.HalfWidth - 3);
                    if (_random.NextDouble() < 0.5) Place(SceneryKind.HayHut, bx, bz, rot, _random.Next(3));
                }
            }

            // The plateau road: an alpine pasture beside it, with a hut and a hay hut.
            var plateau = _map.Edges.FirstOrDefault(e => e.From == Valleys.Oberalm && e.To == Valleys.AlmEnd);
            if (plateau == null) return;
            var a = Along(plateau, plateau.Length * 0.5);
            foreach (int side in new[] { 1, -1 })
            {
                double cx = a.X + (a.NX * side * 85), cz = a.Z + (a.NZ * side * 85);
                var pasture = new FieldPatch(cx, cz, 70, 50, Math.Atan2(a.TX, a.TZ), WorldMaterial.Meadow);
                if (!PatchFree(pasture, roadClear: 12)) continue;
                AddPasture(pasture);
                Place(SceneryKind.AlpineHut, cx - (a.NX * side * 20) + (a.TX * 35), cz - (a.NZ * side * 20) + (a.TZ * 35), Facing(-a.NX * side, -a.NZ * side), 0);
                break;
            }
        }

        // ------------------------------------------------------------------ farms

        private void PlaceFarms()
        {
            foreach (var e in _map.Edges.Where(e => e.Kind == RoadKind.Valley))
            {
                for (double s = 300 + (_random.NextDouble() * 300); s < e.Length - 200; s += 650 + (_random.NextDouble() * 450))
                {
                    var a = Along(e, s);
                    if (Villages.Any(v => Dist(v.X, v.Z, a.X, a.Z) < v.Radius + 220)) continue;
                    int side = _random.NextDouble() < 0.5 ? -1 : 1;
                    if (!TryFarm(a, side, null)) TryFarm(a, -side, null);

                    // Farms further out on the valley floor, halfway to the next road farm, reached by a gravel track.
                    double sf = s + 300 + (_random.NextDouble() * 80);
                    if (sf < e.Length - 200)
                    {
                        var af = Along(e, sf);
                        if (!Villages.Any(v => Dist(v.X, v.Z, af.X, af.Z) < v.Radius + 120))
                        {
                            int fside = _random.NextDouble() < 0.5 ? -1 : 1;
                            double far = 150 + (_random.NextDouble() * 220);
                            if (!TryFarm(af, fside, far)) TryFarm(af, -fside, far);
                        }
                    }
                }
            }
        }

        private bool TryFarm((double X, double Z, double TX, double TZ, double NX, double NZ) a, int side, double? farSetback)
        {
            // Local frame: u along the road, v away from it.
            double vx = a.NX * side, vz = a.NZ * side;
            (double X, double Z) P(double u, double v) => (a.X + (a.TX * u) + (vx * v), a.Z + (a.TZ * u) + (vz * v));
            double setback = farSetback ?? (45 + (_random.NextDouble() * 25));
            var house = P(0, setback);
            if (!Free(house.X, house.Z, SceneryModels.Radius(SceneryKind.Farmhouse) + 4, roadClear: 30) || _terrain.Slope(house.X, house.Z) > 0.12) return false;
            if (_map.ValleyDistance(house.X, house.Z) > (farSetback.HasValue ? 650 : 500) || _map.ValleyFloorFactor(house.X, house.Z) < (farSetback.HasValue ? 0.6 : 0)) return false;
            if (farSetback.HasValue)
            {
                // The track from the road to the farmyard in front of the house
                var start = P(0, (_terrain.DistanceToRoad(a.X, a.Z, 1, out var r0, out _, out _) * 0) + ((r0?.Width ?? 6.5) / 2) + 0.3);
                var end = P(0, setback - 21); // stops in front of the farmyard, clear of the house
                if (!FootprintClear(SceneryKind.Farmhouse, 2, house.X, house.Z, Facing(-vx, -vz))) return false;
                if (!AddLane(start.X, start.Z, end.X, end.Z, gravel: true)) return false;
            }
            if (!FootprintClear(SceneryKind.Farmhouse, 2, house.X, house.Z, Facing(-vx, -vz))) return false;

            double roadAngle = Math.Atan2(a.TX, a.TZ);
            Place(SceneryKind.Farmhouse, house.X, house.Z, Facing(-vx, -vz), _random.Next(3));
            int across = _random.NextDouble() < 0.5 ? -1 : 1;

            // The farm tree beside the house, towards the road
            var tree = P(across * 16, setback - 14);
            if (Free(tree.X, tree.Z, 5, roadClear: 9)) Place(_random.NextDouble() < 0.6 ? SceneryKind.Linden : SceneryKind.Maple, tree.X, tree.Z, _random.Next(360), _random.Next(3));

            // Orchard behind the house
            for (int i = 0; i < 5; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    var p = P(-24 + (i * 12) + ((_random.NextDouble() - 0.5) * 3), setback + 24 + (j * 12) + ((_random.NextDouble() - 0.5) * 3));
                    if (Free(p.X, p.Z, 3.5, roadClear: 12) && !_terrain.IsWater(p.X, p.Z, 4)) Place(SceneryKind.Apple, p.X, p.Z, _random.Next(360), _random.Next(3));
                }
            }

            // A fenced pasture on one side, fields on the other
            var pc = P(across * 78, setback + 6);
            var pasture = new FieldPatch(pc.X, pc.Z, 46, 38, roadAngle, WorldMaterial.Meadow);
            if (PatchFree(pasture, roadClear: 10))
            {
                AddPasture(pasture);
                if (_random.NextDouble() < 0.5)
                {
                    var hut = pasture.Point(30, 24 * (across > 0 ? 1 : -1));
                    Place(SceneryKind.HayHut, hut.X, hut.Z, _random.Next(4) * 90 + (roadAngle * 180 / Math.PI), _random.Next(3), block: true);
                }
            }

            var crops = new[] { WorldMaterial.Wheat, WorldMaterial.Crop, WorldMaterial.Plowed };
            // Fields beside the farm and behind it (as many as fit)
            foreach (var (u, v) in new[] { (-across * 92.0, setback + 10.0), (-across * 92.0, setback + 92.0), (0.0, setback + 110.0), (across * 80.0, setback + 100.0) })
            {
                var fc = P(u, v);
                var field = new FieldPatch(fc.X, fc.Z, 58 + (_random.NextDouble() * 22), 34, roadAngle, crops[_random.Next(crops.Length)]);
                if (PatchFree(field, roadClear: 10)) AddField(field);
            }

            return true;
        }

        // ------------------------------------------------------------------ fields

        /// <summary>
        /// More fields on the valley floors between the farms (the floors are farmland: meadows with a field here and
        /// there), beside the roads, aligned with them.
        /// </summary>
        private void PlaceFields()
        {
            var crops = new[] { WorldMaterial.Wheat, WorldMaterial.Crop, WorldMaterial.Crop, WorldMaterial.Plowed };
            foreach (var e in _map.Edges.Where(e => e.Kind == RoadKind.Valley))
            {
                for (double s = 120; s < e.Length - 120; s += 180 + (_random.NextDouble() * 120))
                {
                    if (_random.NextDouble() > 0.45) continue;
                    var a = Along(e, s);
                    if (Villages.Any(v => Dist(v.X, v.Z, a.X, a.Z) < v.Radius + 60)) continue;
                    int side = _random.NextDouble() < 0.5 ? -1 : 1;
                    double off = 45 + (_random.NextDouble() * 160);
                    double x = a.X + (a.NX * side * off), z = a.Z + (a.NZ * side * off);
                    if (_map.ValleyFloorFactor(x, z) < 0.8) continue;
                    var field = new FieldPatch(x, z, 45 + (_random.NextDouble() * 35), 22 + (_random.NextDouble() * 18), Math.Atan2(a.TX, a.TZ), crops[_random.Next(crops.Length)]);
                    if (PatchFree(field, roadClear: 10)) AddField(field);
                }
            }
        }

        // ------------------------------------------------------------------ hay huts

        private void PlaceHayHuts()
        {
            // Scattered in the meadows of the valley floors and the lower slopes.
            foreach (var e in _map.Edges.Where(e => e.Kind == RoadKind.Valley || e.Kind == RoadKind.Pass))
            {
                for (double s = 150; s < e.Length - 150; s += 240)
                {
                    if (_random.NextDouble() > 0.2) continue;
                    var a = Along(e, s);
                    int side = _random.NextDouble() < 0.5 ? -1 : 1;
                    double off = 60 + (_random.NextDouble() * 140);
                    double x = a.X + (a.NX * side * off), z = a.Z + (a.NZ * side * off);
                    if (_terrain.Height(x, z) > 1500 || _terrain.Slope(x, z) > 0.3) continue;
                    if (!Free(x, z, 6, roadClear: 20)) continue;
                    Place(SceneryKind.HayHut, x, z, _random.Next(360), _random.Next(3));
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(((ax - bx) * (ax - bx)) + ((az - bz) * (az - bz)));

        /// <summary>Rotation (degrees) that turns a model's front (+Z) towards (<paramref name="dx"/>, <paramref name="dz"/>).</summary>
        private static double Facing(double dx, double dz) => Math.Atan2(dx, dz) * 180 / Math.PI;

        /// <summary>Room for something of radius <paramref name="r"/>: dry, not too steep, away from roads, the river and other things.</summary>
        private bool Free(double x, double z, double r, double roadClear)
        {
            if (_terrain.Mountain != null && !_terrain.InGrid(x, z) && !_terrain.Mountain.Contains(x, z)) return false;
            if (_terrain.IsWater(x, z, r + 4)) return false;
            if (_terrain.DistanceToRiver(x, z, r + 20, out _) < r + 18) return false;
            if (_terrain.DistanceToRoad(x, z, roadClear, out _, out _, out _) < roadClear) return false;
            if (_terrain.Slope(x, z) > 0.4) return false;
            if (LaneDistance(x, z) < r + 2.2) return false;
            foreach (var f in _footprints)
            {
                if (Dist(f.X, f.Z, x, z) < f.R + r) return false;
            }

            foreach (var p in Fields.Concat(Pastures))
            {
                if (p.Contains(x, z, r)) return false;
            }

            return true;
        }

        /// <summary>A field or pasture fits: all of it on dry, gentle ground, clear of roads, the river and buildings.</summary>
        private bool PatchFree(FieldPatch patch, double roadClear)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = -2; i <= 2; i++)
            {
                for (int j = -2; j <= 2; j++)
                {
                    var (x, z) = patch.Point(patch.HalfLength * i / 2, patch.HalfWidth * j / 2);
                    if (_terrain.IsWater(x, z, 8)) return false;
                    if (_terrain.DistanceToRiver(x, z, 25, out _) < 22) return false;
                    if (_terrain.DistanceToRoad(x, z, roadClear, out _, out _, out _) < roadClear) return false;
                    double h = _terrain.Height(x, z);
                    lo = Math.Min(lo, h);
                    hi = Math.Max(hi, h);
                    foreach (var f in _footprints)
                    {
                        if (Dist(f.X, f.Z, x, z) < f.R + 4) return false;
                    }
                }
            }

            if (hi - lo > 20) return false;
            foreach (var p in Fields.Concat(Pastures))
            {
                if (Dist(p.Cx, p.Cz, patch.Cx, patch.Cz) < Math.Max(p.HalfLength, p.HalfWidth) + Math.Max(patch.HalfLength, patch.HalfWidth)) return false;
            }

            return true;
        }

        private void Place(SceneryKind kind, double x, double z, double rotationDeg, int variant, bool block = true)
        {
            double r = SceneryModels.Radius(kind);
            var item = new SceneryItem
            {
                Kind = kind,
                Variant = variant,
                X = (float)x,
                Y = (float)GroundUnder(x, z, kind),
                Z = (float)z,
                RotationDeg = (float)rotationDeg,
                Scale = 1,
            };
            Items.Add(item);
            _footprints.Add((x, z, r));
            if (block) BlockCircle(x, z, r + 3);
        }

        /// <summary>Buildings stand on the lowest ground under them (their plinth reaches into the slope).</summary>
        private double GroundUnder(double x, double z, SceneryKind kind)
        {
            double r = SceneryModels.Radius(kind) * 0.6;
            bool building = kind is SceneryKind.AlpineHouse or SceneryKind.Farmhouse or SceneryKind.Church or SceneryKind.Inn
                or SceneryKind.Shop or SceneryKind.HayHut or SceneryKind.AlpineHut or SceneryKind.Shrine;
            if (!building) return _terrain.Height(x, z) - 0.3;
            double h = _terrain.Height(x, z);
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4;
                h = Math.Min(h, _terrain.Height(x + (Math.Cos(a) * r), z + (Math.Sin(a) * r)));
            }

            return h;
        }

        private void AddPasture(FieldPatch pasture)
        {
            Pastures.Add(pasture);
            BlockPatch(pasture, 3);

            // Slanted wooden fence round it, 4 m pieces, with a gate gap on one long side.
            double deg = pasture.Angle * 180 / Math.PI;
            void Side(double u0, double v0, double u1, double v1, double rot, bool gate)
            {
                double length = Math.Sqrt(((u1 - u0) * (u1 - u0)) + ((v1 - v0) * (v1 - v0)));
                int pieces = (int)(length / 4);
                for (int i = 0; i < pieces; i++)
                {
                    if (gate && i == pieces / 2) continue;
                    double f = (i + 0.5) / pieces;
                    var (x, z) = pasture.Point(u0 + ((u1 - u0) * f), v0 + ((v1 - v0) * f));
                    double du = (u1 - u0) / length * 2.1, dv = (v1 - v0) / length * 2.1;
                    var (u, v) = (u0 + ((u1 - u0) * f), v0 + ((v1 - v0) * f));
                    if (!FenceClear(pasture.Point(u - du, v - dv), (x, z), pasture.Point(u + du, v + dv))) continue;
                    Items.Add(new SceneryItem { Kind = SceneryKind.Fence, X = (float)x, Y = (float)_terrain.Height(x, z), Z = (float)z, RotationDeg = (float)rot, Scale = 1 });
                }
            }

            double hl = pasture.HalfLength, hw = pasture.HalfWidth;
            // The fence model runs along X: on the long sides (along u) it is turned to the field's direction.
            Side(-hl, -hw, hl, -hw, deg - 90, gate: true);
            Side(-hl, hw, hl, hw, deg - 90, gate: false);
            Side(-hl, -hw, -hl, hw, deg, gate: false);
            Side(hl, -hw, hl, hw, deg, gate: false);
        }

        private void AddField(FieldPatch field)
        {
            Fields.Add(field);
            BlockPatch(field, 2);
            double reach = Math.Sqrt((field.HalfLength * field.HalfLength) + (field.HalfWidth * field.HalfWidth));
            int c0x = (int)Math.Floor((field.Cx - reach) / IndexCell), c1x = (int)Math.Floor((field.Cx + reach) / IndexCell);
            int c0z = (int)Math.Floor((field.Cz - reach) / IndexCell), c1z = (int)Math.Floor((field.Cz + reach) / IndexCell);
            for (int cx = c0x; cx <= c1x; cx++)
            {
                for (int cz = c0z; cz <= c1z; cz++)
                {
                    long key = Key(cx, cz);
                    if (!_fieldsByChunk.TryGetValue(key, out var list)) _fieldsByChunk[key] = list = new List<FieldPatch>();
                    list.Add(field);
                }
            }
        }

        private void BlockCircle(double x, double z, double r)
        {
            int c0 = (int)Math.Floor((x - r) / BlockCell), c1 = (int)Math.Floor((x + r) / BlockCell);
            int d0 = (int)Math.Floor((z - r) / BlockCell), d1 = (int)Math.Floor((z + r) / BlockCell);
            for (int i = c0; i <= c1; i++)
            {
                for (int j = d0; j <= d1; j++)
                {
                    double cx = (i + 0.5) * BlockCell, cz = (j + 0.5) * BlockCell;
                    if (Dist(cx, cz, x, z) <= r + (BlockCell * 0.7)) _blocked.Add(Key(i, j));
                }
            }
        }

        private void BlockPatch(FieldPatch p, double margin)
        {
            double reach = Math.Sqrt((p.HalfLength * p.HalfLength) + (p.HalfWidth * p.HalfWidth)) + margin;
            int c0 = (int)Math.Floor((p.Cx - reach) / BlockCell), c1 = (int)Math.Floor((p.Cx + reach) / BlockCell);
            int d0 = (int)Math.Floor((p.Cz - reach) / BlockCell), d1 = (int)Math.Floor((p.Cz + reach) / BlockCell);
            for (int i = c0; i <= c1; i++)
            {
                for (int j = d0; j <= d1; j++)
                {
                    if (p.Contains((i + 0.5) * BlockCell, (j + 0.5) * BlockCell, margin + (BlockCell * 0.7))) _blocked.Add(Key(i, j));
                }
            }
        }

        private void Index(SceneryItem item)
        {
            long key = Key((int)Math.Floor(item.X / IndexCell), (int)Math.Floor(item.Z / IndexCell));
            if (!_itemsByChunk.TryGetValue(key, out var list)) _itemsByChunk[key] = list = new List<SceneryItem>();
            list.Add(item);
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
