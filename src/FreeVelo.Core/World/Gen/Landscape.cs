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

        /// <summary>
        /// Gravel yards, forecourts, beer-garden floors and stone paths as ground surfaces: rectangles the app drapes over
        /// the drawn ground (Crop = <see cref="WorldMaterial.Gravel"/> or <see cref="WorldMaterial.Stone"/>). They replace
        /// the old plate models, which floated or sank on slopes.
        /// </summary>
        public List<FieldPatch> Surfaces { get; } = new List<FieldPatch>();

        /// <summary>
        /// A surface in a building's frame: centre (u along the road, v towards it), half sizes along u and v, the
        /// building's rotation (its model's +Z = towards the road).
        /// </summary>
        private void AddSurface(Func<double, double, (double X, double Z)> p, double u0, double u1, double v0, double v1, double rotationDeg, WorldMaterial material)
        {
            if (u1 - u0 < 0.5 || v1 - v0 < 0.5) return;
            var (cx, cz) = p((u0 + u1) / 2, (v0 + v1) / 2);
            // FieldPatch: its length runs along the angle's direction (= the model's +Z, towards the road), its width across.
            var patch = new FieldPatch(cx, cz, (v1 - v0) / 2, (u1 - u0) / 2, rotationDeg * Math.PI / 180, material);
            Surfaces.Add(patch);
            BlockPatch(patch, 0.5);
        }
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
            l.LevelBuildingPlots();
            l.SettleOnSurface();
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
                    if (!FootprintClear(SceneryKind.Church, 0, cx, cz, lot.Item.RotationDeg)) continue; // with the churchyard round it
                    Place(SceneryKind.Church, cx, cz, lot.Item.RotationDeg, _random.Next(2));                    // In front: the maypole (in May) and a big linden.
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
                // A farm on the edge of the village is a Paarhof too; without room for its Stadel it is a house.
                int stadel = kind == SceneryKind.Farmhouse ? StadelSide(x, z, lot.Item.RotationDeg, (r * 0.75) + 5) : 0;
                if (kind == SceneryKind.Farmhouse && stadel == 0) kind = SceneryKind.AlpineHouse;
                Place(kind, x, z, lot.Item.RotationDeg, variant);
                if (stadel != 0) PlaceStadel(Items[Items.Count - 1], stadel, byTrack: false);
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

            // A gravel apron in front of the doors (as the old yard plates: the building's width, 6 m deep).
            void Apron()
            {
                var (cx, cz) = P(0, (d / 2) + 3);
                if (FenceClear((cx, cz)) && _terrain.DistanceToRiver(cx, cz, 15, out _) >= 12) AddSurface(P, -w / 2, w / 2, d / 2, (d / 2) + 6, rot, WorldMaterial.Gravel);
            }

            switch (kind)
            {
                case SceneryKind.Workshop:
                    Apron();
                    Put(SceneryKind.Car, -3.6, (d / 2) + 3.2, rot + 180 + (_random.NextDouble() * 20), _random.Next(3));
                    if (_random.NextDouble() < 0.7) Put(SceneryKind.Car, (w / 2) + 3, (d / 2) + 2.5, rot + 90 + (_random.NextDouble() * 15), _random.Next(3));
                    break;
                case SceneryKind.FireStation:
                    // The Tripo fire station's two doors span 7.6 m in the middle of its gable: a gravel drive just
                    // wider than them, from the doors to the road edge.
                    {
                        var (cx, cz) = P(0, 0);
                        if (FenceClear(P(0, (d / 2) + 3)) && _terrain.DistanceToRiver(cx, cz, 15, out _) >= 12)
                        {
                            double front = Math.Min((d / 2) + 9, _terrain.DistanceToRoad(cx, cz, 40, out var fr, out _, out _) - ((fr?.Width ?? 6.5) / 2) + 0.3);
                            AddSurface(P, -5, 5, d / 2, front, rot, WorldMaterial.Gravel);
                        }
                    }

                    break;
                case SceneryKind.Sawmill:
                    // An earth yard under the shed and round the stacks (the Tripo sawmill stands 19.6 m long in the
                    // middle of its 30 m plot), reaching to the road edge in front.
                    {
                        var (cx, cz) = P(0, 0);
                        if (_terrain.DistanceToRiver(cx, cz, 30, out _) >= 18)
                        {
                            double front = Math.Min(13.5, _terrain.DistanceToRoad(cx, cz, 40, out var sr, out _, out _) - ((sr?.Width ?? 6.5) / 2) + 0.3);
                            AddSurface(P, -(w / 2) - 10, (w / 2) + 8, -12.5, front, rot, WorldMaterial.YardEarth);
                        }
                    }

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
            SceneryKind.Farmhouse => (12, 11.5),   // the Paarhof's dwelling (its Stadel: Barn)
            SceneryKind.Barn => (12, 14),
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
            // The door step; the Tripo inn's ground floor stands back under its overhanging upper floors.
            double front = kind == SceneryKind.Inn ? InnGroundFront - 0.05 : (d / 2) + 0.9;
            double gap = roadEdge - front;
            if (gap < 2) return;

            // The front yard (path, forecourt) is levelled with the house, up to a metre before the street.
            int own = _pads.FindLastIndex(p => Math.Abs(p.X - x) < 0.01 && Math.Abs(p.Z - z) < 0.01);
            double yardEnd = roadEdge - 1;
            if (own >= 0 && yardEnd > d / 2)
            {
                var (yx, yz) = P(0, ((d / 2) + yardEnd) / 2);
                _pads.Add(new Terrain.Pad(yx, yz, w / 2, (yardEnd - (d / 2)) / 2, rotationDeg, _pads[own].Height));
            }

            void Item(SceneryKind k, double u, double v, double rot, int var)
            {
                var (px, pz) = P(u, v);
                Items.Add(new SceneryItem { Kind = k, Variant = var, X = (float)px, Y = (float)(_terrain.Height(px, pz) - 0.05), Z = (float)pz, RotationDeg = (float)rot, Scale = 1 });
            }

            if (kind == SceneryKind.Inn)
            {
                // Meadow in front, a cobbled path from the street to the door (0.3 m off the middle of the Tripo
                // model's front, measured on a straight-on render), a bench by the wall.
                double r = rotationDeg * Math.PI / 180;
                double door = 0.3 * Math.Sign((ax * Math.Cos(r)) - (az * Math.Sin(r)));
                AddSurface(P, door - 0.9, door + 0.9, front - 0.3, roadEdge + 0.3, rotationDeg, WorldMaterial.Stone);
                Item(SceneryKind.Bench, (w / 2) - 2, front + 0.6, rotationDeg, 0);   // back to the wall, facing the street
                BeerGarden(w, d, P, (k, u, v, rot, var) => Item(k, u, v, rot, var), rotationDeg);
                return;
            }

            if (kind == SceneryKind.Farmhouse) return;   // the Paarhof's paths and driveway: PlaceStadel

            if (kind is SceneryKind.Shop)
            {
                // Gravel forecourt / yard between the building and the street
                AddSurface(P, -w / 2, w / 2, front, roadEdge - 0.5, rotationDeg, WorldMaterial.Gravel);
                return;
            }

            double doorU = 0;
            if (kind == SceneryKind.AlpineHut)
            {
                // The Tripo alpine hut stands turned by HutTurnDeg inside its own model (huts are rarely built square to
                // the road): beds and bench follow the hut's front wall, the path starts at its door and runs straight
                // to the road. Hut frame: hu along the front wall, hv out of it; s turns the model's x into this
                // plot's u (the plot frame may be mirrored).
                double rr = rotationDeg * Math.PI / 180, t = HutTurnDeg * Math.PI / 180;
                double s = Math.Sign((ax * Math.Cos(rr)) - (az * Math.Sin(rr)));
                (double U, double V) H(double hu, double hv) => (s * ((hu * Math.Cos(t)) + (hv * Math.Sin(t))), (-hu * Math.Sin(t)) + (hv * Math.Cos(t)));
                // The path leaves the door square to the hut's front (in the hut's frame) and keeps that line to the
                // road; it reaches the road edge (plot v) at hv = (v + hu sin t) / cos t.
                double HvAt(double v) => (v + (HutDoor.U * Math.Sin(t))) / Math.Cos(t);
                (double X, double Z) HutPoint(double hu, double hv) { var q = H(hu, hv); return P(q.U, q.V); }
                AddSurface(HutPoint, HutDoor.U - 0.75, HutDoor.U + 0.75, HutDoor.V - 0.3, HvAt(roadEdge + 0.3), rotationDeg + HutTurnDeg, WorldMaterial.Stone);
                doorU = H(HutDoor.U, HvAt(Math.Min(roadEdge - 1.5, front + 7))).U;   // where the path crosses the fence line
                // A flower bed either side of the door; on half of the huts a bench instead of the second bed.
                var left = H(HutDoor.U - 2.4, HutDoor.V + 0.6);
                Item(SceneryKind.FlowerBed, left.U, left.V, rotationDeg + HutTurnDeg, 0);
                bool bench = _random.NextDouble() < 0.5;
                if (bench) _random.NextDouble(); // as many draws as before, so the rest of the landscape stays
                var right = H(HutDoor.U + 2.2, HutDoor.V + (bench ? 0.55 : 0.6));
                Item(bench ? SceneryKind.Bench : SceneryKind.FlowerBed, right.U, right.V, rotationDeg + HutTurnDeg, 0);   // a bench sits with its back to the wall
            }
            else
            {
                // Stone path from the door to the road edge
                AddSurface(P, -0.75, 0.75, front - 0.3, roadEdge + 0.3, rotationDeg, WorldMaterial.Stone);

                // Flower beds along the front wall, either side of the door; sometimes a bench
                foreach (int side in new[] { -1, 1 }) Item(SceneryKind.FlowerBed, side * ((w / 4) + 0.9), (d / 2) + 0.6, rotationDeg, 0);
                if (_random.NextDouble() < 0.5) Item(SceneryKind.Bench, (_random.NextDouble() < 0.5 ? -1 : 1) * (w / 2 - 1.4), (d / 2) + 0.55, rotationDeg, 0);   // back to the wall
            }

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
            // The gap: no piece across the path (pieces are 3 m long; houses keep their old 3.6 m gap)
            if (doorU == 0) Run(-half, fenceFront, half, fenceFront, rotationDeg, -1.8, 1.8);
            else Run(-half, fenceFront, half, fenceFront, rotationDeg, doorU - 2.25, doorU + 2.25);
            Run(-half, back, half, back, rotationDeg);
            Run(-half, back, -half, fenceFront, across);
            Run(half, back, half, fenceFront, across);
            BlockPlot(P(0, (fenceFront + back) / 2), half, (fenceFront - back) / 2, ax, az);
        }

        /// <summary>
        /// The inn's small Gastgarten right against its side wall: gravel open to the forecourt, three or four table sets
        /// that guests have pushed about a little (not in a grid), a shady tree at the back corner and a low fence on the
        /// outer side and the back. Kept small: a big one would need parasols, little trees and more.
        /// </summary>
        // The Tripo inn's ground floor as modelled (Resources/Models/Inn_0, measured 0.5-1.5 m above its floor): the
        // upper floors overhang it by about a metre on every side.
        private const double InnGroundHalfWidth = 6.1, InnGroundFront = 7.65, InnGroundBack = -1.2;

        // The Tripo alpine hut (Resources/Models/AlpineHut_0): its walls are turned 12.9 degrees inside the model
        // (area-weighted wall directions); its door in the hut's own frame (along the front wall, out of it), from the
        // model's centre (0, 1.3) and a front wall 3 m before it, the door 0.4 m to the side.
        private const double HutTurnDeg = 12.9;
        private static readonly (double U, double V) HutDoor = (-0.4, 4.3);   // side measured on renders in the game

        private void BeerGarden(double w, double d, Func<double, double, (double X, double Z)> p, Action<SceneryKind, double, double, double, int> item, double rotationDeg)
        {
            const double Width = 7, Wall = 0.1;
            double vFront = InnGroundFront + 1.2, vBack = InnGroundBack + 0.5;
            foreach (int side in _random.NextDouble() < 0.5 ? new[] { 1, -1 } : new[] { -1, 1 })
            {
                // a: distance from the ground-floor side wall, outwards
                double U(double a) => side * (InnGroundHalfWidth + Wall + a);
                double vMid = (vFront + vBack) / 2, halfV = (vFront - vBack) / 2;
                var c = p(U(Width / 2), vMid);
                bool room = true;
                foreach (var (a, v) in new[] { (Width / 2, vMid), (0.0, vBack), (Width + 1, vBack), (0.0, vFront), (Width + 1, vFront) })
                {
                    var q = p(U(a), v);
                    if (_terrain.IsWater(q.X, q.Z, 2) || _terrain.DistanceToRoad(q.X, q.Z, 5, out _, out _, out _) < 5) room = false;
                }

                var inn = p(0, 0);
                if (!room || _footprints.Any(f => Dist(f.X, f.Z, c.X, c.Z) < f.R + 4 && Dist(f.X, f.Z, inn.X, inn.Z) > 0.5)) continue;

                // The garden is levelled with the inn's floor, so its gravel floor lies flat under the tables.
                int own = _pads.FindLastIndex(q => Math.Abs(q.X - inn.X) < 0.01 && Math.Abs(q.Z - inn.Z) < 0.01);
                if (own >= 0) _pads.Add(new Terrain.Pad(c.X, c.Z, (Width / 2) + Wall + 0.5, halfV + 0.5, rotationDeg, _pads[own].Height));

                AddSurface(p, Math.Min(U(-Wall - 0.2), U(Width)), Math.Max(U(-Wall - 0.2), U(Width)), vBack, vFront, rotationDeg, WorldMaterial.Gravel);

                // Its own random numbers (from the position), so the rest of the landscape stays as it was.
                var rnd = new Random(unchecked(((int)Math.Round(c.X * 7.0) * 73856093) ^ ((int)Math.Round(c.Z * 7.0) * 19349663)));
                double Jitter(double r) => (rnd.NextDouble() * 2 - 1) * r;
                // Two rows along the wall; the row by the wall under the eaves. One place stays empty now and then.
                var places = new List<(double A, double V)> { (2.0, vFront - 2.4), (2.2, vFront - 6.6), (5.2, vFront - 3.6), (5.4, vFront - 7.6) };
                if (rnd.NextDouble() < 0.4) places.RemoveAt(rnd.Next(places.Count));
                foreach (var (a, v) in places)
                {
                    // Shoved about by the guests: up to half a metre off the line and turned by up to 14 degrees.
                    item(SceneryKind.BeerTable, U(a + Jitter(0.5)), v + Jitter(0.5), rotationDeg + 90 + Jitter(14), rnd.Next(3));
                }

                item(_random.NextDouble() < 0.5 ? SceneryKind.Linden : SceneryKind.Maple, U(Width + 1.2), vBack - 0.8, _random.Next(360), 1);
                void Fence(double a, double v, double r, double da, double dv)
                {
                    if (FenceClear(p(U(a - da), v - dv), p(U(a), v), p(U(a + da), v + dv))) item(SceneryKind.GardenFence, U(a), v, r, 0);
                }

                // Outer side, back and the outer half of the front; the way in is by the house, from the forecourt. The
                // house wall closes the fourth side.
                double sideLength = vFront - vBack;
                int along = (int)Math.Round(sideLength / 3);
                for (int k = 0; k < along; k++) Fence(Width + 0.3, vBack + ((k + 0.5) * sideLength / along), rotationDeg + 90, 0, 1.5);
                for (int k = 0; k < 2; k++) Fence(Width + 0.3 - 1.5 - (k * 3), vBack, rotationDeg, 1.5, 0);
                Fence(Width + 0.3 - 1.5, vFront, rotationDeg, 1.5, 0);

                _footprints.Add((c.X, c.Z, 7.5));  // reaches the corners of the levelled pad
                BlockCircle(c.X, c.Z, 7.5);
                return;
            }
        }

        /// <summary>
        /// The ground a building covers, in its own frame (front = +Z): half width across, and from the back to the
        /// front – with roof overhangs, balconies, ramps, terraces and, for the church, the tower and an open churchyard (lawn, no wall) round it.
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
            double? floor = null;
            foreach (var (px, pz) in OutlinePoints(kind, variant, x, z, rotationDeg, 2))
            {
                if (_terrain.IsWater(px, pz, 1)) return false;
                double d = _terrain.DistanceToRoad(px, pz, 12, out var road, out _, out _);
                if (road != null && d < (road.Width / 2) + 1.5) return false;
                if (LaneDistance(px, pz) < 2.6) return false;
                // Not over a road's cutting or embankment: there the drawn ground drops below the slope (a farmhouse's
                // ramp corner hung 1 m in the air 2.5 m off the asphalt).
                if (road != null && d < (road.Width / 2) + 8 && _terrain.SurfaceHeight(px, pz) < (floor ??= GroundUnder(x, z, kind)) - 1) return false;
                // Not on another building's levelled pad (the circle check in Free misses the corners of long
                // buildings): the later pad would cut the ground under this one's corner, or lift it.
                foreach (var pad in _pads)
                {
                    double dx = px - pad.X, dz = pz - pad.Z;
                    double u = (dx * pad.Cos) - (dz * pad.Sin), v = (dx * pad.Sin) + (dz * pad.Cos);
                    if (Math.Abs(u) < pad.HalfX + 3 && Math.Abs(v) < pad.HalfZ + 3) return false;
                }
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

        /// <summary>
        /// Last step, once the ground is final (pads set): every item stands on the ground exactly as the app draws it
        /// (<see cref="Terrain.SurfaceHeight"/>) - before, heights came from a bilinear estimate that was up to 1.7 m off
        /// the drawn triangles. Fences follow the ground along their length (<see cref="SceneryItem.Slope"/>); small rigid
        /// things sit on the lowest point of their footprint, so no corner hangs in the air; trees and poles on their
        /// foot. Buildings keep their level pad.
        /// </summary>
        private void SettleOnSurface()
        {
            for (int k = 0; k < Items.Count; k++)
            {
                var item = Items[k];
                if (IsBuilding(item.Kind)) continue;
                double r = item.RotationDeg * Math.PI / 180, sin = Math.Sin(r), cos = Math.Cos(r);
                double At(double u, double v) => _terrain.SurfaceHeight(item.X + (u * cos) + (v * sin), item.Z - (u * sin) + (v * cos));
                if (item.Kind is SceneryKind.GardenFence or SceneryKind.Fence)
                {
                    // Posts at both ends of the piece on the ground, the piece sheared between them; 5 cm into the ground.
                    double half = item.Kind == SceneryKind.Fence ? 2.0 : 1.5;
                    double a = At(-half, 0), b = At(half, 0);
                    double slope = (b - a) / (2 * half), y = (a + b) / 2;
                    // Over a crest the straight piece would hang in the air in between: lower it until it nowhere
                    // stands above the ground (quarter points and middle).
                    double lift = 0;
                    foreach (double u in new[] { -half / 2, 0, half / 2 }) lift = Math.Max(lift, (y + (slope * u)) - At(u, 0));
                    item.Y = (float)(y - lift - 0.05);
                    item.Slope = (float)slope;
                }
                else if (item.Kind is SceneryKind.Maypole or SceneryKind.VillageSign or SceneryKind.DirectionSign
                         or SceneryKind.Apple or SceneryKind.Linden or SceneryKind.Maple or SceneryKind.Beech or SceneryKind.Alder or SceneryKind.Willow)
                {
                    // On their foot: the lowest ground within half a metre, 10 cm in (trees keep their sunk roots).
                    double foot = Math.Min(At(0, 0), Math.Min(Math.Min(At(0.5, 0), At(-0.5, 0)), Math.Min(At(0, 0.5), At(0, -0.5))));
                    item.Y = (float)(foot - (item.Kind is SceneryKind.Maypole or SceneryKind.VillageSign or SceneryKind.DirectionSign ? 0.1 : 0.3));
                }
                else
                {
                    // Rigid things (benches, beds, tables, piles, cars): the lowest corner of their footprint.
                    var (x0, x1, z0, z1) = Footprint(item.Kind, item.Variant);
                    double low = Math.Min(Math.Min(At(x0, z0), At(x1, z0)), Math.Min(Math.Min(At(x0, z1), At(x1, z1)), At(0, 0)));
                    item.Y = (float)(low - 0.02);
                }

                Items[k] = item;
            }

            // The signposts' copies (their boards are drawn by the app) follow their items.
            for (int s = 0; s < Signposts.Count; s++)
            {
                var (post, boards) = Signposts[s];
                var moved = Items.FirstOrDefault(i => i.Kind == SceneryKind.DirectionSign && i.X == post.X && i.Z == post.Z);
                if (moved.Kind == SceneryKind.DirectionSign) Signposts[s] = (moved, boards);
            }
        }

        private static readonly Dictionary<(SceneryKind, int), (double, double, double, double)> FootprintCache = new Dictionary<(SceneryKind, int), (double, double, double, double)>();

        /// <summary>The coded model's extent on the ground (local x and z), for seating rigid things.</summary>
        private static (double X0, double X1, double Z0, double Z1) Footprint(SceneryKind kind, int variant)
        {
            lock (FootprintCache)
            {
                if (FootprintCache.TryGetValue((kind, variant % SceneryModels.Variants), out var f)) return f;
                var verts = SceneryModels.Get(kind, variant).Lod0.Values.SelectMany(m => m.Vertices).ToList();
                f = verts.Count == 0 ? (0, 0, 0, 0) : (verts.Min(v => v.X), verts.Max(v => v.X), verts.Min(v => v.Z), verts.Max(v => v.Z));
                FootprintCache[(kind, variant % SceneryModels.Variants)] = f;
                return f;
            }
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
                    // Coming in: travelling towards the node along e (moved a few metres if a garden path is in the way).
                    double x = 0, z = 0, dx = 0, dz = 0;
                    bool clear = false;
                    foreach (double shift in new[] { 0.0, 3, -3, 6 })
                    {
                        var a = Along(e, fromHere ? 30 + shift : e.Length - 30 - shift);
                        dx = fromHere ? -a.TX : a.TX; dz = fromHere ? -a.TZ : a.TZ;
                        foreach (double off in new[] { 5.6, 7.0, 9.0 })
                        {
                            x = a.X + (dz * off); z = a.Z - (dx * off); // right-hand side: (dz, −dx)
                            double d = _terrain.DistanceToRoad(x, z, 12, out var road, out _, out _);
                            if (road == null || d >= (road.Width / 2) + 1) { clear = true; break; }
                        }

                        double px = x, pz = z;
                        if (clear && Surfaces.Any(s => s.Crop is WorldMaterial.Stone or WorldMaterial.Gravel && s.Contains(px, pz, 1))) clear = false;
                        if (clear) break;
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
            double rot = Facing(-vx, -vz);
            if (!FootprintClear(SceneryKind.Farmhouse, 0, house.X, house.Z, rot)) return false;
            int stadel = StadelSide(house.X, house.Z, rot, roadClear: 20);
            if (stadel == 0) return false;
            if (farSetback.HasValue)
            {
                // The track from the road to the yard in front of the Stadel's gate, square to the road
                var end = FarmPoint(house.X, house.Z, rot, stadel * StadelU, FarmFront + 5.5);
                double uEnd = ((end.X - a.X) * a.TX) + ((end.Z - a.Z) * a.TZ);
                _terrain.DistanceToRoad(a.X, a.Z, 1, out var r0, out _, out _);
                var start = P(uEnd, ((r0?.Width ?? 6.5) / 2) + 0.3);
                if (!AddLane(start.X, start.Z, end.X, end.Z, gravel: true)) return false;
            }

            double roadAngle = Math.Atan2(a.TX, a.TZ);
            Place(SceneryKind.Farmhouse, house.X, house.Z, rot, _random.Next(3));
            PlaceStadel(Items[Items.Count - 1], stadel, byTrack: farSetback.HasValue);
            int across = _random.NextDouble() < 0.5 ? -1 : 1;

            // The farm tree beside the dwelling, on the side away from the Stadel
            var tree = FarmPoint(house.X, house.Z, rot, -stadel * 15.5, 1);
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
            foreach (var (u, v) in new[] { (-across * 112.0, setback + 10.0), (-across * 112.0, setback + 92.0), (0.0, setback + 110.0), (across * 80.0, setback + 100.0) })
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

        // ------------------------------------------------------------------ Paarhof

        // The Paarhof (docs/GRAPHICS_OVERHAUL.md): the dwelling (Farmhouse, 12 x 11.5 m) and its Stadel (Barn, 12 x 14 m)
        // side by side, both gables to the road, their fronts in line, a gravel yard between them. In the dwelling's
        // frame (u across, v to the front): the Stadel's centre at u = ±StadelU, v = StadelV.
        private const double StadelU = 6 + 9 + 6, StadelV = 5.75 - 7, FarmFront = 5.75;

        /// <summary>A point in a building's frame (u across, v to its front), as the app turns items.</summary>
        private static (double X, double Z) FarmPoint(double x, double z, double rotationDeg, double u, double v)
        {
            double r = rotationDeg * Math.PI / 180, sin = Math.Sin(r), cos = Math.Cos(r);
            return (x + (u * cos) + (v * sin), z - (u * sin) + (v * cos));
        }

        /// <summary>Which side of a dwelling at (x, z) its Stadel fits on: +1 or -1 (a random one first), 0 if neither.</summary>
        private int StadelSide(double x, double z, double rotationDeg, double roadClear)
        {
            int first = _random.NextDouble() < 0.5 ? 1 : -1;
            foreach (int side in new[] { first, -first })
            {
                var (bx, bz) = FarmPoint(x, z, rotationDeg, side * StadelU, StadelV);
                if (!Free(bx, bz, SceneryModels.Radius(SceneryKind.Barn) + 1.5, roadClear)) continue;
                if (_terrain.Slope(bx, bz) > 0.15) continue;
                if (FootprintClear(SceneryKind.Barn, 0, bx, bz, rotationDeg)) return side;
            }

            return 0;
        }

        /// <summary>
        /// The Stadel beside the dwelling just placed, on its floor (one level farm: a pad under both). Meadow round the
        /// dwelling, with a stone footpath from its front door to the street and one along its front to the Stadel; a
        /// gravel apron before the Stadel's gate that narrows into a driveway to the street (on farms reached by a
        /// track, <paramref name="byTrack"/>, the apron only: the track ends at it).
        /// </summary>
        private void PlaceStadel(SceneryItem house, int side, bool byTrack)
        {
            double rot = house.RotationDeg;
            var (bx, bz) = FarmPoint(house.X, house.Z, rot, side * StadelU, StadelV);
            Place(SceneryKind.Barn, bx, bz, rot, _random.Next(3), floor: house.Y);
            var (yx, yz) = FarmPoint(house.X, house.Z, rot, side * StadelU / 2, StadelV);
            _pads.Add(new Terrain.Pad(yx, yz, (StadelU / 2) + 6, 7.5, rot, house.Y));
            (double X, double Z) H(double u, double v) => FarmPoint(house.X, house.Z, rot, u, v);
            (double, double) Span(double a, double b) => (Math.Min(a, b), Math.Max(a, b));

            // The street's edge, measured from the dwelling (it faces the street square on).
            double street = _terrain.DistanceToRoad(house.X, house.Z, 400, out var road, out _, out _) - ((road?.Width ?? 6.5) / 2);
            street = Math.Min(street, LaneDistance(house.X, house.Z) - 1.7);
            bool toStreet = !byTrack && street < 120;

            // Gravel before the gate (the Stadel's width), then the driveway (5 m) to the street.
            var (a0, a1) = Span(side * (StadelU - 6.5), side * (StadelU + 6.5));
            AddSurface(H, a0, a1, FarmFront - 0.2, FarmFront + 6, rot, WorldMaterial.Gravel);
            if (toStreet && street > FarmFront + 7)
            {
                var (d0, d1) = Span(side * (StadelU - 2.5), side * (StadelU + 2.5));
                AddSurface(H, d0, d1, FarmFront + 5.8, street + 0.3, rot, WorldMaterial.Gravel);
            }

            // Footpaths: from the front door to the street, and along the front to the gravel before the Stadel.
            if (toStreet) AddSurface(H, -0.75, 0.75, FarmFront - 0.3, street + 0.3, rot, WorldMaterial.Stone);
            else AddSurface(H, -0.75, 0.75, FarmFront - 0.3, FarmFront + 3, rot, WorldMaterial.Stone);
            var (p0, p1) = Span(side * 0.75, side * (StadelU - 6.5));
            AddSurface(H, p0, p1, FarmFront + 1.5, FarmFront + 3, rot, WorldMaterial.Stone);
        }

        private void Place(SceneryKind kind, double x, double z, double rotationDeg, int variant, bool block = true, double? floor = null)
        {
            double r = SceneryModels.Radius(kind);
            var item = new SceneryItem
            {
                Kind = kind,
                Variant = variant,
                X = (float)x,
                Y = (float)(floor ?? GroundUnder(x, z, kind)),
                Z = (float)z,
                RotationDeg = (float)rotationDeg,
                Scale = 1,
            };
            Items.Add(item);
            _footprints.Add((x, z, r));
            if (block) BlockCircle(x, z, r + 3);
            if (IsBuilding(kind))
            {
                var (pw, pd) = PadSize(kind, variant);
                _pads.Add(new Terrain.Pad(x, z, pw / 2, pd / 2, rotationDeg, item.Y));
            }
        }

        private static bool IsBuilding(SceneryKind kind) => kind is SceneryKind.AlpineHouse or SceneryKind.Farmhouse or SceneryKind.Barn or SceneryKind.Church
            or SceneryKind.Inn or SceneryKind.Shop or SceneryKind.HayHut or SceneryKind.AlpineHut or SceneryKind.Shrine
            or SceneryKind.Workshop or SceneryKind.Sawmill or SceneryKind.FireStation;

        /// <summary>The ground levelled for a building: its footprint (the church with its tower).</summary>
        private static (double W, double D) PadSize(SceneryKind kind, int variant) => kind switch
        {
            SceneryKind.Church => (SceneryModels.Radius(kind) * 1.4, 30), // the Tripo church is 27 m long with its tower
            SceneryKind.HayHut or SceneryKind.Shrine => (SceneryModels.Radius(kind) * 1.4, SceneryModels.Radius(kind) * 1.4),
            _ => Size(kind, variant),
        };

        private readonly List<Terrain.Pad> _pads = new List<Terrain.Pad>();

        /// <summary>
        /// Buildings stand on level ground at the average height under them: the terrain is cut uphill and filled
        /// downhill (<see cref="LevelBuildingPlots"/>), so they are neither half sunk nor on stilts.
        /// </summary>
        private double GroundUnder(double x, double z, SceneryKind kind)
        {
            if (!IsBuilding(kind)) return _terrain.Height(x, z) - 0.3;
            double r = SceneryModels.Radius(kind) * 0.6, sum = _terrain.Height(x, z);
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4;
                sum += _terrain.Height(x + (Math.Cos(a) * r), z + (Math.Sin(a) * r));
            }

            return sum / 9;
        }

        /// <summary>
        /// Levels the ground under every building, then moves everything else placed nearby (fences, benches, beds,
        /// signs …) by as much as the ground under it moved.
        /// </summary>
        private void LevelBuildingPlots()
        {
            var before = Items.Select(i => _terrain.Height(i.X, i.Z)).ToArray();
            _terrain.SetPads(_pads);
            for (int k = 0; k < Items.Count; k++)
            {
                var item = Items[k];
                if (IsBuilding(item.Kind)) continue;
                double shift = _terrain.DrawnHeight(item.X, item.Z) - before[k];
                if (Math.Abs(shift) < 0.001) continue;
                item.Y += (float)shift;
                Items[k] = item;
            }
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
