using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Physics;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>A junction (or the start) of the road network.</summary>
    public sealed class RoadNode
    {
        public RoadNode(int id, double x, double z)
        {
            Id = id;
            X = x;
            Z = z;
        }

        public int Id { get; }

        public double X { get; }

        public double Z { get; }

        public double Elevation { get; internal set; }

        /// <summary>Edges meeting here.</summary>
        public List<int> Edges { get; } = new List<int>();
    }

    /// <summary>A road between two nodes: points every few metres with distance and elevation.</summary>
    public sealed class RoadEdge
    {
        internal RoadEdge(int id, int from, int to, double[] x, double[] z, bool mainLoop, RoadSurface surface, RoadKind kind = RoadKind.Home, string? climbId = null)
        {
            Kind = kind;
            ClimbId = climbId;
            Id = id;
            From = from;
            To = to;
            X = x;
            Z = z;
            IsMainLoop = mainLoop;
            Surface = surface;
            D = new double[x.Length];
            for (int i = 1; i < x.Length; i++) D[i] = D[i - 1] + Math.Sqrt(Sq(x[i] - x[i - 1]) + Sq(z[i] - z[i - 1]));
            E = new double[x.Length];
        }

        public int Id { get; }

        public int From { get; }

        public int To { get; }

        public double[] X { get; }

        public double[] Z { get; }

        /// <summary>Road surface elevation at each point.</summary>
        public double[] E { get; }

        /// <summary>Distance from <see cref="From"/> at each point.</summary>
        public double[] D { get; }

        public double Length => D[D.Length - 1];

        /// <summary>Part of the main loop (the route of group rides); otherwise a link road.</summary>
        public bool IsMainLoop { get; }

        public RoadSurface Surface { get; }

        /// <summary>Home world, access road, climb or the summit connector.</summary>
        public RoadKind Kind { get; }

        /// <summary>For climbs: the climb's id (<see cref="ClimbData"/>).</summary>
        public string? ClimbId { get; }

        /// <summary>Paved width in metres.</summary>
        public double Width => Surface == RoadSurface.Asphalt ? 6.5 : 4.0;

        public int Other(int node) => node == From ? To : From;

        private static double Sq(double v) => v * v;
    }

    /// <summary>Where a road crosses the river on a bridge.</summary>
    public readonly struct Bridge
    {
        public Bridge(int edge, double distanceM, double waterLevel)
        {
            Edge = edge;
            DistanceM = distanceM;
            WaterLevel = waterLevel;
        }

        public int Edge { get; }

        /// <summary>Distance along the edge (from its From node) where it crosses the river centre.</summary>
        public double DistanceM { get; }

        public double WaterLevel { get; }
    }

    public sealed class Lake
    {
        internal Lake(double x, double z, double radius, double[] shape, double level)
        {
            X = x;
            Z = z;
            Radius = radius;
            _shape = shape;
            Level = level;
        }

        private readonly double[] _shape;

        public double X { get; }

        public double Z { get; }

        public double Radius { get; }

        public double Level { get; }

        /// <summary>Shoreline radius in a direction (irregular, smooth).</summary>
        public double RadiusAt(double angle)
        {
            double a = ((angle / (2 * Math.PI)) % 1 + 1) % 1 * _shape.Length;
            int i = (int)a;
            double f = a - i;
            double s0 = _shape[i % _shape.Length], s1 = _shape[(i + 1) % _shape.Length];
            return Radius * (s0 + ((s1 - s0) * (3 - (2 * f)) * f * f));
        }

        /// <summary>Distance from the centre in units of the shoreline radius (below 1 = in the water).</summary>
        public double Relative(double x, double z) => Math.Sqrt(((x - X) * (x - X)) + ((z - Z) * (z - Z))) / RadiusAt(Math.Atan2(z - Z, x - X));
    }

    public sealed class River
    {
        internal River(double[] x, double[] z, double[] level, double halfWidth)
        {
            X = x;
            Z = z;
            Level = level;
            HalfWidth = halfWidth;
        }

        /// <summary>Centre line, in flow direction.</summary>
        public double[] X { get; }

        public double[] Z { get; }

        /// <summary>Water surface elevation (never rises downstream).</summary>
        public double[] Level { get; }

        public double HalfWidth { get; }
    }

    /// <summary>
    /// The fictional world, generated from a seed and identical everywhere (app and server): rolling terrain, a
    /// network of roads with junctions (the 10 km main loop plus link roads, one of them gravel), a river crossed on
    /// bridges and a few lakes. Roads follow the terrain smoothly (at most 9 % grade) and meet at junctions with the
    /// same height. The server only needs the roads; the app adds the detailed terrain and scenery.
    /// </summary>
    public sealed class WorldMap
    {
        public const int DefaultSeed = 1;
        public const double MainLoopLengthM = 10_000;
        public const double Spacing = 5;
        public const double MaxGrade = 0.09;

        private static readonly Lazy<WorldMap> DefaultMap = new Lazy<WorldMap>(() => Generate(DefaultSeed));
        private readonly int _seed;
        private Route? _mainLoop;

        private WorldMap(int seed) => _seed = seed;

        /// <summary>The world all rides use.</summary>
        public static WorldMap Default => DefaultMap.Value;

        public int Seed => _seed;

        public List<RoadNode> Nodes { get; } = new List<RoadNode>();

        public List<RoadEdge> Edges { get; } = new List<RoadEdge>();

        public List<Lake> Lakes { get; } = new List<Lake>();

        public River? River { get; private set; }

        public List<Bridge> Bridges { get; } = new List<Bridge>();

        /// <summary>Area of the rolling home world: its roads and water (without margin).</summary>
        public (double MinX, double MinZ, double MaxX, double MaxZ) Bounds { get; private set; }

        /// <summary>The mountain with the two famous climbs (null for worlds generated without it).</summary>
        public Mountain? Mountain { get; private set; }

        /// <summary>Area of the mountain roads (access roads, climbs, connector), without margin.</summary>
        public (double MinX, double MinZ, double MaxX, double MaxZ) MountainBounds { get; private set; }

        /// <summary>Main loop edges in riding order (starting at node 0).</summary>
        public List<int> MainLoopEdges { get; } = new List<int>();

        /// <summary>The main loop as a looping route (group rides).</summary>
        public Route MainLoop
        {
            get
            {
                lock (MainLoopEdges)
                {
                    return _mainLoop ??= BuildRoute("Loop", MainLoopEdges.Select(e => new DirectedEdge(e, true)).ToList(), isLoop: true, 0);
                }
            }
        }

        /// <summary>Rolling hills (±30 m) without the river valley.</summary>
        public double Hills(double x, double z) =>
            100
            + (28 * Noise.Fractal(x / 1500, z / 1500, _seed, 3))
            + (5 * Noise.Fractal(x / 280, z / 280, _seed + 17, 2));

        /// <summary>Half width of the river valley (from the river centre to where the hills are untouched).</summary>
        public const double ValleyHalfWidth = 220;

        /// <summary>
        /// Natural terrain: the hills with the river's valley worked in (banks a metre above the water, rising
        /// gently to the hills). Roads follow this; lakes and road beds are added by the detailed terrain.
        /// </summary>
        public double BaseHeight(double x, double z)
        {
            double hills = Hills(x, z);
            if (River == null || _riverIndex == null) return hills;
            double d = _riverIndex.Nearest(x, z, ValleyHalfWidth, out int segment, out double f);
            if (d >= ValleyHalfWidth) return hills;
            double level = River.Level[segment] + ((River.Level[segment + 1] - River.Level[segment]) * f);
            double bank = River.HalfWidth + 12;
            double floor = level + 1.2 + (Math.Max(0, d - bank) * 0.02);
            if (d <= bank) return floor;
            double t = SmoothStep((d - bank) / (ValleyHalfWidth - bank));
            return floor + ((hills - floor) * t);
        }

        private SegmentIndex? _riverIndex;

        private static double SmoothStep(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t * t * (3 - (2 * t));
        }

        // ------------------------------------------------------------------ routes

        /// <summary>A route through the given edges (in this order and direction), distances starting at <paramref name="startDistanceM"/>.</summary>
        public Route BuildRoute(string name, IReadOnlyList<DirectedEdge> steps, bool isLoop, double startDistanceM)
        {
            var points = new List<RoutePoint>();
            double distance = startDistanceM;
            foreach (var step in steps)
            {
                var e = Edges[step.Edge];
                int n = e.X.Length;
                for (int k = points.Count == 0 ? 0 : 1; k < n; k++)
                {
                    int i = step.Forward ? k : n - 1 - k;
                    double along = step.Forward ? e.D[i] : e.Length - e.D[i];
                    double grade = Grade(e, i) * (step.Forward ? 1 : -1);
                    points.Add(new RoutePoint(distance + along, e.X[i], e.Z[i], e.E[i], grade, e.Surface));
                }

                distance += e.Length;
            }

            var route = new Route(name, points, isLoop);
            var climbs = new List<ClimbSegment>();
            double at = startDistanceM;
            foreach (var step in steps)
            {
                var e = Edges[step.Edge];
                if (e.Kind == RoadKind.Climb && step.Forward)
                {
                    var data = Mountain?.Find(e.ClimbId!)?.Data;
                    climbs.Add(new ClimbSegment(e.ClimbId!, data?.Name ?? e.ClimbId!, at, at + e.Length, e.E[e.E.Length - 1] - e.E[0]));
                }

                at += e.Length;
            }

            route.Climbs = climbs;
            return route;
        }

        /// <summary>
        /// The loop around the mountain starting at the foot of <paramref name="climbId"/>: up that climb, over the
        /// connector, down the other climb and its access road, along the main loop (the shorter way) and back up the
        /// first access road to the foot. Null if the world has no such climb.
        /// </summary>
        public Route? ClimbLoop(string climbId)
        {
            var m = Mountain;
            var first = m?.Find(climbId);
            if (m == null || first == null) return null;
            var other = first == m.Stelvio ? m.Alpe : m.Stelvio;
            int accessFirst = first == m.Stelvio ? m.AccessStelvioEdge : m.AccessAlpeEdge;
            int accessOther = first == m.Stelvio ? m.AccessAlpeEdge : m.AccessStelvioEdge;
            int junctionFirst = first == m.Stelvio ? m.JunctionStelvio : m.JunctionAlpe;
            int junctionOther = first == m.Stelvio ? m.JunctionAlpe : m.JunctionStelvio;

            var steps = new List<DirectedEdge>
            {
                new DirectedEdge(first.Edge, true),
                new DirectedEdge(m.ConnectorEdge, first == m.Stelvio),
                new DirectedEdge(other.Edge, false),
                new DirectedEdge(accessOther, false),
            };
            steps.AddRange(MainLoopBetween(junctionOther, junctionFirst));
            steps.Add(new DirectedEdge(accessFirst, true));
            return BuildRoute(first.Data.Name, steps, isLoop: true, 0);
        }

        /// <summary>Main loop edges from node <paramref name="from"/> to node <paramref name="to"/>, the shorter way round.</summary>
        private List<DirectedEdge> MainLoopBetween(int from, int to)
        {
            var loop = MainLoopEdges;
            int n = loop.Count;
            int start = loop.FindIndex(e => Edges[e].From == from);
            var forward = new List<DirectedEdge>();
            double forwardLength = 0;
            for (int k = 0, i = start; k < n && Edges[loop[i]].From != to; k++, i = (i + 1) % n)
            {
                forward.Add(new DirectedEdge(loop[i], true));
                forwardLength += Edges[loop[i]].Length;
            }

            int end = loop.FindIndex(e => Edges[e].To == from);
            var backward = new List<DirectedEdge>();
            double backwardLength = 0;
            for (int k = 0, i = end; k < n && Edges[loop[i]].To != to; k++, i = (i - 1 + n) % n)
            {
                backward.Add(new DirectedEdge(loop[i], false));
                backwardLength += Edges[loop[i]].Length;
            }

            return forwardLength <= backwardLength ? forward : backward;
        }

        private static double Grade(RoadEdge e, int i)
        {
            int a = Math.Max(0, i - 2), b = Math.Min(e.X.Length - 1, i + 2);
            double run = e.D[b] - e.D[a];
            return run > 0 ? (e.E[b] - e.E[a]) / run * 100 : 0;
        }

        // ------------------------------------------------------------------ generation

        public static WorldMap Generate(int seed) => Generate(seed, searchMountain: false);

        /// <summary>
        /// <paramref name="searchMountain"/>: place the mountain by the full search instead of trying the stored placement
        /// first (several seconds; for tests and other seeds – the result is the same for the default world).
        /// </summary>
        public static WorldMap Generate(int seed, bool searchMountain)
        {
            var map = new WorldMap(seed);
            var random = new Random(seed);
            var loop = MainLoopCurve(random);
            var links = map.LinkRoads(random, loop);
            map.Bounds = BoundsOf(new[] { loop }.Concat(links.Select(l => l.Path)));
            map.River = map.MakeRiver(random);
            map._riverIndex = new SegmentIndex(map.River.X, map.River.Z, 60);

            // The mountain beside the home world (placement is deterministic; the home world stays the same).
            map.Mountain = Mountain.Plan(map, loop, links.Select(l => l.Path).ToList(), links.SelectMany(l => new[] { l.S1, l.S2 }).ToList(), useHint: !searchMountain);
            map.BuildGraph(loop, links, map.Mountain);
            map.MountainBounds = BoundsOf(map.Edges.Where(e => e.Kind != RoadKind.Home).Select(e => (IReadOnlyList<double>)e.X).Zip(map.Edges.Where(e => e.Kind != RoadKind.Home).Select(e => (IReadOnlyList<double>)e.Z), (x, z) => (x, z)));
            map.FindBridges();
            map.SetElevations();
            map.MakeLakes(random);
            return map;
        }

        /// <summary>Closed, smooth, irregular loop of 10 km, evenly sampled (last point = first point).</summary>
        private static Polyline MainLoopCurve(Random random)
        {
            const int harmonics = 5;
            var amp = new double[harmonics];
            var phase = new double[harmonics];
            for (int k = 0; k < harmonics; k++)
            {
                amp[k] = (0.16 / (k + 1)) * random.NextDouble();
                phase[k] = random.NextDouble() * Math.PI * 2;
            }

            const int fine = 4000;
            var raw = new Polyline();
            for (int i = 0; i <= fine; i++)
            {
                double t = 2 * Math.PI * i / fine;
                double r = 1.0;
                for (int k = 0; k < harmonics; k++) r += amp[k] * Math.Sin(((k + 2) * t) + phase[k]);
                raw.Add(r * Math.Sin(t), r * Math.Cos(t));
            }

            double scale = MainLoopLengthM / raw.Length;
            var scaled = new Polyline();
            for (int i = 0; i < raw.Count; i++) scaled.Add(raw.X[i] * scale, raw.Z[i] * scale);
            return scaled.Resample(Spacing);
        }

        private sealed class Link
        {
            public double S1;
            public double S2;
            public Polyline Path = new Polyline();
            public RoadSurface Surface;
        }

        /// <summary>
        /// Link roads across the inside of the loop and one detour outside, as smooth curves leaving the loop at
        /// right angles. Retries until they stay clear of the loop and of each other, with junctions well apart.
        /// </summary>
        private List<Link> LinkRoads(Random random, Polyline loop)
        {
            var links = new List<Link>();
            double cx = loop.X.Average(), cz = loop.Z.Average();
            var plans = new[] { (Inner: true, Surface: RoadSurface.Asphalt), (Inner: true, Surface: RoadSurface.CompactedGravel), (Inner: false, Surface: RoadSurface.Asphalt) };
            foreach (var plan in plans)
            {
                for (int attempt = 0; attempt < 200; attempt++)
                {
                    double s1 = random.NextDouble() * loop.Length;
                    double span = plan.Inner ? 0.28 + (random.NextDouble() * 0.17) : 0.12 + (random.NextDouble() * 0.08);
                    double s2 = (s1 + (span * loop.Length)) % loop.Length;
                    if (links.SelectMany(l => new[] { l.S1, l.S2 }).Any(s => LoopGap(s, s1, loop.Length) < 900 || LoopGap(s, s2, loop.Length) < 900)) continue;

                    var (ax, az, anx, anz) = loop.PointAndNormal(s1, cx, cz, plan.Inner);
                    var (bx, bz, bnx, bnz) = loop.PointAndNormal(s2, cx, cz, plan.Inner);
                    double gap = Math.Sqrt(((bx - ax) * (bx - ax)) + ((bz - az) * (bz - az)));
                    double reach = gap * (plan.Inner ? 0.38 : 0.75);
                    var path = Polyline.Bezier(ax, az, ax + (anx * reach), az + (anz * reach), bx + (bnx * reach), bz + (bnz * reach), bx, bz, 400).Resample(Spacing);
                    if (!Clear(path, loop, links, plan.Inner)) continue;
                    links.Add(new Link { S1 = s1, S2 = s2, Path = path, Surface = plan.Surface });
                    break;
                }
            }

            return links;
        }

        private static double LoopGap(double a, double b, double length)
        {
            double d = Math.Abs(a - b) % length;
            return Math.Min(d, length - d);
        }

        private static bool Clear(Polyline path, Polyline loop, List<Link> others, bool inner)
        {
            double total = path.Length;
            for (int i = 0; i < path.Count; i++)
            {
                double fromEnds = Math.Min(path.D[i], total - path.D[i]);
                if (fromEnds < 60) continue;
                if (loop.Contains(path.X[i], path.Z[i]) != inner) return false;
                double toLoop = loop.DistanceTo(path.X[i], path.Z[i]);
                if (toLoop < Math.Min(160, fromEnds * 0.6)) return false;
                foreach (var other in others)
                {
                    if (other.Path.DistanceTo(path.X[i], path.Z[i]) < 180) return false;
                }
            }

            return true;
        }

        private void BuildGraph(Polyline loop, List<Link> links, Mountain? mountain)
        {
            // Junctions along the loop, in riding order; node 0 is the start. Mountain access roads count as
            // "links" number links.Count (Stelvio) and links.Count + 1 (Alpe d'Huez).
            var junctions = links.SelectMany((l, i) => new[] { (S: l.S1, Link: i, End: 0), (S: l.S2, Link: i, End: 1) }).ToList();
            if (mountain != null)
            {
                junctions.Add((mountain.LoopStelvio, links.Count, 0));
                junctions.Add((mountain.LoopAlpe, links.Count + 1, 0));
            }

            junctions = junctions.OrderBy(j => j.S).ToList();
            var nodeOf = new Dictionary<(int Link, int End), int>();
            foreach (var j in junctions)
            {
                var (x, z) = loop.At(j.S);
                var node = new RoadNode(Nodes.Count, x, z);
                nodeOf[(j.Link, j.End)] = node.Id;
                Nodes.Add(node);
            }

            if (Nodes.Count == 0)
            {
                Nodes.Add(new RoadNode(0, loop.X[0], loop.Z[0]));
                junctions.Add((0, -1, 0));
            }

            for (int k = 0; k < junctions.Count; k++)
            {
                double from = junctions[k].S;
                double to = k + 1 < junctions.Count ? junctions[k + 1].S : junctions[0].S + loop.Length;
                var part = loop.Section(from, to);
                AddEdge(k, (k + 1) % junctions.Count, part, mainLoop: true, RoadSurface.Asphalt);
                MainLoopEdges.Add(Edges.Count - 1);
            }

            for (int i = 0; i < links.Count; i++)
            {
                // The curve starts and ends exactly on the loop points.
                AddEdge(nodeOf[(i, 0)], nodeOf[(i, 1)], links[i].Path, mainLoop: false, links[i].Surface);
            }

            if (mountain == null) return;

            // Mountain: feet, summits, access roads, climbs (foot → summit) and the connector between the summits.
            int Node((double X, double Z) p)
            {
                var node = new RoadNode(Nodes.Count, p.X, p.Z);
                Nodes.Add(node);
                return node.Id;
            }

            var s = mountain.Stelvio;
            var a = mountain.Alpe;
            int footS = Node(s.Foot), topS = Node(s.Point(s.Data.X.Length - 1));
            int footA = Node(a.Foot), topA = Node(a.Point(a.Data.X.Length - 1));
            Nodes[footS].Elevation = s.FootElevation;
            Nodes[topS].Elevation = s.SummitElevation;
            Nodes[footA].Elevation = a.FootElevation;
            Nodes[topA].Elevation = a.SummitElevation;
            mountain.JunctionStelvio = nodeOf[(links.Count, 0)];
            mountain.JunctionAlpe = nodeOf[(links.Count + 1, 0)];

            AddEdge(mountain.JunctionStelvio, footS, mountain.AccessStelvioPath, false, RoadSurface.Asphalt, RoadKind.Access);
            mountain.AccessStelvioEdge = Edges.Count - 1;
            AddEdge(footS, topS, ClimbPath(s), false, RoadSurface.Asphalt, RoadKind.Climb, s.Data.Id);
            s.Edge = Edges.Count - 1;
            AddEdge(topS, topA, mountain.ConnectorPath, false, RoadSurface.Asphalt, RoadKind.Connector);
            mountain.ConnectorEdge = Edges.Count - 1;
            AddEdge(footA, topA, ClimbPath(a), false, RoadSurface.Asphalt, RoadKind.Climb, a.Data.Id);
            a.Edge = Edges.Count - 1;
            AddEdge(mountain.JunctionAlpe, footA, mountain.AccessAlpePath, false, RoadSurface.Asphalt, RoadKind.Access);
            mountain.AccessAlpeEdge = Edges.Count - 1;
        }

        private static Polyline ClimbPath(PlacedClimb climb)
        {
            var path = new Polyline();
            for (int i = 0; i < climb.Data.X.Length; i++)
            {
                var (x, z) = climb.Point(i);
                path.Add(x, z);
            }

            return path;
        }

        private void AddEdge(int from, int to, Polyline path, bool mainLoop, RoadSurface surface, RoadKind kind = RoadKind.Home, string? climbId = null)
        {
            var x = path.X.ToArray();
            var z = path.Z.ToArray();
            x[0] = Nodes[from].X;
            z[0] = Nodes[from].Z;
            x[x.Length - 1] = Nodes[to].X;
            z[z.Length - 1] = Nodes[to].Z;
            var edge = new RoadEdge(Edges.Count, from, to, x, z, mainLoop, surface, kind, climbId);
            Edges.Add(edge);
            Nodes[from].Edges.Add(edge.Id);
            Nodes[to].Edges.Add(edge.Id);
        }

        private static (double, double, double, double) BoundsOf(IEnumerable<Polyline> paths) =>
            BoundsOf(paths.Select(p => ((IReadOnlyList<double>)p.X, (IReadOnlyList<double>)p.Z)));

        private static (double, double, double, double) BoundsOf(IEnumerable<(IReadOnlyList<double> X, IReadOnlyList<double> Z)> paths)
        {
            double minX = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxZ = double.MinValue;
            foreach (var (xs, zs) in paths)
            {
                minX = Math.Min(minX, xs.Min());
                maxX = Math.Max(maxX, xs.Max());
                minZ = Math.Min(minZ, zs.Min());
                maxZ = Math.Max(maxZ, zs.Max());
            }

            return (minX, minZ, maxX, maxZ);
        }

        // ------------------------------------------------------------------ water

        /// <summary>
        /// A meandering river across the whole area. Its water level follows the low ground (a little below it) and
        /// never rises downstream.
        /// </summary>
        private River MakeRiver(Random random)
        {
            var (minX, minZ, maxX, maxZ) = Bounds;
            double cx = (minX + maxX) / 2, cz = (minZ + maxZ) / 2;
            double reach = (Math.Max(maxX - minX, maxZ - minZ) / 2) + 700;
            double angle = random.NextDouble() * Math.PI;
            double dx = Math.Cos(angle), dz = Math.Sin(angle);
            double offset = (random.NextDouble() - 0.5) * 500;
            double a1 = 90 + (random.NextDouble() * 60), l1 = 650 + (random.NextDouble() * 250), p1 = random.NextDouble() * 6.3;
            double a2 = 30 + (random.NextDouble() * 30), l2 = 230 + (random.NextDouble() * 80), p2 = random.NextDouble() * 6.3;

            var path = new Polyline();
            for (double u = -reach; u <= reach; u += 10)
            {
                double side = offset + (a1 * Math.Sin((u / l1) + p1)) + (a2 * Math.Sin((u / l2) + p2));
                path.Add(cx + (dx * u) - (dz * side), cz + (dz * u) + (dx * side));
            }

            path = path.Resample(10);
            if (Hills(path.X[0], path.Z[0]) < Hills(path.X[path.Count - 1], path.Z[path.Count - 1])) path = path.Reversed();

            // Evenly falling water level from a bit below the ground at the source to a bit below at the mouth; the
            // valley around it (BaseHeight) adapts the terrain to the river, not the other way round.
            int n = path.Count;
            double source = Hills(path.X[0], path.Z[0]) - 3, mouth = Math.Min(source - 4, Hills(path.X[n - 1], path.Z[n - 1]) - 3);
            var level = new double[n];
            for (int i = 0; i < n; i++) level[i] = source + ((mouth - source) * path.D[i] / path.Length);
            return new River(path.X.ToArray(), path.Z.ToArray(), level, halfWidth: 7);
        }

        private void FindBridges()
        {
            var river = River!;
            foreach (var e in Edges)
            {
                for (int i = 0; i < e.X.Length - 1; i++)
                {
                    for (int k = 0; k < river.X.Length - 1; k++)
                    {
                        if (!Intersect(e.X[i], e.Z[i], e.X[i + 1], e.Z[i + 1], river.X[k], river.Z[k], river.X[k + 1], river.Z[k + 1], out double t, out double u)) continue;
                        double level = river.Level[k] + ((river.Level[k + 1] - river.Level[k]) * u);
                        Bridges.Add(new Bridge(e.Id, e.D[i] + ((e.D[i + 1] - e.D[i]) * t), level));
                    }
                }
            }
        }

        private static bool Intersect(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz, out double t, out double u)
        {
            double rX = bx - ax, rZ = bz - az, sX = dx - cx, sZ = dz - cz;
            double den = (rX * sZ) - (rZ * sX);
            t = u = 0;
            if (Math.Abs(den) < 1e-12) return false;
            t = (((cx - ax) * sZ) - ((cz - az) * sX)) / den;
            u = (((cx - ax) * rZ) - ((cz - az) * rX)) / den;
            return t >= 0 && t < 1 && u >= 0 && u < 1;
        }

        /// <summary>A few lakes in low ground, clear of roads and the river.</summary>
        private void MakeLakes(Random random)
        {
            var (minX, minZ, maxX, maxZ) = Bounds;
            var candidates = new List<(double X, double Z, double H)>();
            for (double x = minX - 300; x <= maxX + 300; x += 70)
            {
                for (double z = minZ - 300; z <= maxZ + 300; z += 70) candidates.Add((x, z, BaseHeight(x, z)));
            }

            foreach (var c in candidates.OrderBy(c => c.H + (random.NextDouble() * 6)))
            {
                if (Lakes.Count >= 3) break;
                double radius = 70 + (random.NextDouble() * 90);
                if (DistanceToRoads(c.X, c.Z) < radius * 1.25 + 70) continue;
                if (DistanceToRiver(c.X, c.Z) < radius * 1.25 + 90) continue;
                if (Lakes.Any(l => Math.Sqrt(((l.X - c.X) * (l.X - c.X)) + ((l.Z - c.Z) * (l.Z - c.Z))) < l.Radius + radius + 250)) continue;

                var shape = new double[24];
                for (int i = 0; i < shape.Length; i++) shape[i] = 0.82 + (0.3 * Noise.Random01(i, Lakes.Count, _seed + 5));
                for (int pass = 0; pass < 2; pass++)
                {
                    var copy = (double[])shape.Clone();
                    for (int i = 0; i < shape.Length; i++) shape[i] = (copy[(i + shape.Length - 1) % shape.Length] + (2 * copy[i]) + copy[(i + 1) % shape.Length]) / 4;
                }

                var probe = new Lake(c.X, c.Z, radius, shape, 0);
                double shore = double.MaxValue;
                for (int a = 0; a < 48; a++)
                {
                    double angle = a * Math.PI * 2 / 48;
                    for (double q = 1.0; q <= 1.6; q += 0.3)
                    {
                        double r = probe.RadiusAt(angle) * q;
                        shore = Math.Min(shore, BaseHeight(c.X + (Math.Cos(angle) * r), c.Z + (Math.Sin(angle) * r)));
                    }
                }

                Lakes.Add(new Lake(c.X, c.Z, radius, shape, shore - 0.6));
            }
        }

        public double DistanceToRoads(double x, double z)
        {
            double best = double.MaxValue;
            foreach (var e in Edges)
            {
                for (int i = 0; i < e.X.Length; i += 4) best = Math.Min(best, ((e.X[i] - x) * (e.X[i] - x)) + ((e.Z[i] - z) * (e.Z[i] - z)));
            }

            return Math.Sqrt(best);
        }

        public double DistanceToRiver(double x, double z)
        {
            if (River == null) return double.MaxValue;
            double best = double.MaxValue;
            for (int i = 0; i < River.X.Length; i++) best = Math.Min(best, ((River.X[i] - x) * (River.X[i] - x)) + ((River.Z[i] - z) * (River.Z[i] - z)));
            return Math.Sqrt(best);
        }

        // ------------------------------------------------------------------ elevation

        /// <summary>
        /// Road heights: the terrain smoothed along the road, raised to clear the river at bridges, with the same
        /// height where roads meet and at most <see cref="MaxGrade"/>.
        /// </summary>
        private void SetElevations()
        {
            var mountainNodes = new HashSet<int>(Edges.Where(e => e.Kind != RoadKind.Home).SelectMany(e => new[] { e.From, e.To }));
            var homeNodes = new HashSet<int>(Edges.Where(e => e.Kind == RoadKind.Home).SelectMany(e => new[] { e.From, e.To }));
            foreach (var node in Nodes)
            {
                if (mountainNodes.Contains(node.Id) && !homeNodes.Contains(node.Id)) continue; // feet and summits: set by the plan
                double sum = 0;
                int count = 0;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        sum += BaseHeight(node.X + (dx * 40), node.Z + (dz * 40));
                        count++;
                    }
                }

                node.Elevation = sum / count;
            }

            foreach (var e in Edges)
            {
                if (e.Kind != RoadKind.Home)
                {
                    MountainElevations(e);
                    continue;
                }

                int n = e.X.Length;
                var raw = new double[n];
                for (int i = 0; i < n; i++) raw[i] = BaseHeight(e.X[i], e.Z[i]);
                var smooth = Smooth(raw, 16);
                FitEnds(e, smooth);

                foreach (var b in Bridges.Where(b => b.Edge == e.Id))
                {
                    double deck = b.WaterLevel + 4.5;
                    for (int i = 0; i < n; i++) smooth[i] = Math.Max(smooth[i], deck - (0.055 * Math.Abs(e.D[i] - b.DistanceM)));
                }

                smooth = Smooth(smooth, 5);
                FitEnds(e, smooth);
                for (int pass = 0; pass < 4; pass++) LimitGrade(e, smooth);
                Array.Copy(smooth, e.E, n);
            }
        }

        /// <summary>
        /// Climbs: the GPX elevations (shifted to the world's height). Connector: level between the equal summits with a
        /// gentle rise in the middle. Access roads: an even gradient from the junction to the foot, eased at both ends,
        /// lifted over the river where they cross it.
        /// </summary>
        private void MountainElevations(RoadEdge e)
        {
            int n = e.X.Length;
            var m = Mountain!;
            if (e.Kind == RoadKind.Climb)
            {
                var climb = m.Find(e.ClimbId!)!;
                for (int i = 0; i < n; i++) e.E[i] = climb.Elevation(i);
                return;
            }

            double a = Nodes[e.From].Elevation, b = Nodes[e.To].Elevation;
            var h = new double[n];
            for (int i = 0; i < n; i++)
            {
                double f = e.D[i] / e.Length;
                if (e.Kind == RoadKind.Connector)
                {
                    h[i] = a + ((b - a) * f) + (12 * Math.Sin(Math.PI * f));
                }
                else
                {
                    // Even gradient with 150 m easing at both ends
                    double ease = Math.Min(1, Math.Min(e.D[i], e.Length - e.D[i]) / 150);
                    double t = f + ((SmoothStep(f) - f) * (1 - ease));
                    h[i] = a + ((b - a) * t);
                }
            }

            foreach (var bridge in Bridges.Where(br => br.Edge == e.Id))
            {
                double deck = bridge.WaterLevel + 4.5;
                for (int i = 0; i < n; i++) h[i] = Math.Max(h[i], deck - (0.055 * Math.Abs(e.D[i] - bridge.DistanceM)));
            }

            for (int pass = 0; pass < 4; pass++) LimitGrade(e, h);
            Array.Copy(h, e.E, n);
        }

        private void FitEnds(RoadEdge e, double[] h)
        {
            int n = h.Length;
            double a = Nodes[e.From].Elevation - h[0], b = Nodes[e.To].Elevation - h[n - 1];
            for (int i = 0; i < n; i++)
            {
                double f = e.D[i] / e.Length;
                h[i] += (a * (1 - f)) + (b * f);
            }
        }

        private static void LimitGrade(RoadEdge e, double[] h)
        {
            int n = h.Length;
            for (int i = 1; i < n - 1; i++)
            {
                double max = (e.D[i] - e.D[i - 1]) * MaxGrade;
                h[i] = Math.Max(h[i - 1] - max, Math.Min(h[i - 1] + max, h[i]));
            }

            for (int i = n - 2; i > 0; i--)
            {
                double max = (e.D[i + 1] - e.D[i]) * MaxGrade;
                h[i] = Math.Max(h[i + 1] - max, Math.Min(h[i + 1] + max, h[i]));
            }
        }

        private static double[] Smooth(double[] v, int radius)
        {
            int n = v.Length;
            var result = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = 0, weight = 0;
                for (int k = Math.Max(0, i - radius); k <= Math.Min(n - 1, i + radius); k++)
                {
                    double w = radius + 1 - Math.Abs(k - i);
                    sum += v[k] * w;
                    weight += w;
                }

                result[i] = sum / weight;
            }

            return result;
        }
    }

    /// <summary>One road taken in a direction.</summary>
    public readonly struct DirectedEdge : IEquatable<DirectedEdge>
    {
        public DirectedEdge(int edge, bool forward)
        {
            Edge = edge;
            Forward = forward;
        }

        public int Edge { get; }

        /// <summary>From the edge's From node to its To node.</summary>
        public bool Forward { get; }

        public bool Equals(DirectedEdge other) => Edge == other.Edge && Forward == other.Forward;

        public override bool Equals(object? obj) => obj is DirectedEdge d && Equals(d);

        public override int GetHashCode() => (Edge * 2) + (Forward ? 1 : 0);
    }
}
