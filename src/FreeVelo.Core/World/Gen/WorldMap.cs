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

        /// <summary>Village (or summit) name, for the junction signs ("" if none).</summary>
        public string Name { get; internal set; } = "";
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
    /// The fictional world, identical everywhere (app and server): two Alpine valleys making a round through the villages
    /// (see <see cref="Valleys"/>), a river along the main valley crossed on bridges, a lake or two, the Alm road with
    /// hairpins at an even gradient up to a high plateau, and the mountain with the Stelvio and the Alpe d'Huez. The
    /// valley floors are flat; the slopes and peaks come from <see cref="MountainTerrain"/>. The server only needs the
    /// roads; the app adds the detailed terrain and scenery.
    /// </summary>
    public sealed class WorldMap
    {
        public const int DefaultSeed = 1;
        public const double Spacing = 5;
        public const double MaxGrade = 0.09;

        /// <summary>The detailed home grid covers this much around the start village (the rest is streamed).</summary>
        public const double HomeHalfSize = 350;

        private static readonly Lazy<WorldMap> DefaultMap = new Lazy<WorldMap>(() => Generate(DefaultSeed));
        private readonly int _seed;
        private Route? _mainLoop;
        private ValleyFloor? _floor;

        private WorldMap(int seed) => _seed = seed;

        /// <summary>The world all rides use.</summary>
        public static WorldMap Default => DefaultMap.Value;

        public int Seed => _seed;

        public List<RoadNode> Nodes { get; } = new List<RoadNode>();

        public List<RoadEdge> Edges { get; } = new List<RoadEdge>();

        public List<Lake> Lakes { get; } = new List<Lake>();

        public River? River { get; private set; }

        public List<Bridge> Bridges { get; } = new List<Bridge>();

        /// <summary>Area of the detailed home grid: around the start village (without margin).</summary>
        public (double MinX, double MinZ, double MaxX, double MaxZ) Bounds { get; private set; }

        /// <summary>The mountain with the two famous climbs.</summary>
        public Mountain? Mountain { get; private set; }

        /// <summary>Area of all roads, without margin (the streamed terrain covers it).</summary>
        public (double MinX, double MinZ, double MaxX, double MaxZ) MountainBounds { get; private set; }

        /// <summary>The valley round's edges in riding order (starting at node 0, the start village).</summary>
        public List<int> MainLoopEdges { get; } = new List<int>();

        /// <summary>Edges of the Alm round in riding order (from Kirchbach: up, over the plateau, down, back along the valley).</summary>
        public List<DirectedEdge> AlmLoopSteps { get; } = new List<DirectedEdge>();

        /// <summary>The valley round as a looping route (group rides, free rides without turning off).</summary>
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

        /// <summary>The Alm round (looping, from Kirchbach).</summary>
        public Route AlmLoop() => BuildRoute(AlmRouteName, AlmLoopSteps, isLoop: true, 0);

        public const string AlmRouteName = "Alm";

        /// <summary>Natural ground without the river channel: the valley floor and its slopes (±a few metres of meadow).</summary>
        public double Hills(double x, double z)
        {
            if (_floor == null) return 650;
            double d = _floor.Distance(x, z);
            var (halfWidth, rise, _) = _floor.Shape(x, z);
            double meadow = 2.5 * Noise.Fractal(x / 220, z / 220, _seed, 2) * SmoothStep((d - 40) / 200);
            double slope = (rise * SmoothStep((d - halfWidth) / 1400)) + (25 * Noise.Fractal(x / 1500, z / 1500, _seed + 9, 3) * SmoothStep((d - halfWidth + 20) / 800));
            return _floor.Height(x, z) + meadow + slope;
        }

        /// <summary>Distance to the nearest valley road (32 m resolution): the mountain's relief stays off the valley floors.</summary>
        public double ValleyDistance(double x, double z) => _floor?.Distance(x, z) ?? double.MaxValue;

        /// <summary>
        /// How much of the mountain's relief belongs here: none on the valley floors, rising beyond them; weaker beside
        /// the open, green stretch Kirchbach – Mitterbach, stronger in the narrow valley Mitterbach – Trafon.
        /// </summary>
        public double ReliefFactor(double x, double z)
        {
            if (_floor == null) return 1;
            var (halfWidth, _, relief) = _floor.Shape(x, z);
            return SmoothStep((_floor.Distance(x, z) - halfWidth - 30) / 800) * relief;
        }

        private (double X, double Z)[]? _plateau;

        /// <summary>Distance to the plateau road (Oberalm), where the alpine pasture is.</summary>
        public double DistanceToPlateau(double x, double z)
        {
            _plateau ??= Edges.Where(e => e.From == Valleys.Oberalm && e.To == Valleys.AlmEnd)
                .SelectMany(e => Enumerable.Range(0, e.X.Length).Where(i => i % 10 == 0 || i == e.X.Length - 1).Select(i => (e.X[i], e.Z[i]))).ToArray();
            double best = double.MaxValue;
            foreach (var (px, pz) in _plateau) best = Math.Min(best, ((px - x) * (px - x)) + ((pz - z) * (pz - z)));
            return Math.Sqrt(best);
        }

        /// <summary>1 on a valley floor, fading to 0 over 250 m up its sides.</summary>
        public double ValleyFloorFactor(double x, double z)
        {
            if (_floor == null) return 0;
            var (halfWidth, _, _) = _floor.Shape(x, z);
            return 1 - SmoothStep((_floor.Distance(x, z) - halfWidth) / 250);
        }

        /// <summary>The valley along each valley road: wide and gentle, narrow between high slopes, or in between.</summary>
        private static (double HalfWidth, double Rise, double Relief) ValleyShape(RoadEdge e)
        {
            bool Is(int a, int b) => (e.From == a && e.To == b) || (e.From == b && e.To == a);
            if (Is(Valleys.Kirchbach, Valleys.Mitterbach)) return (650, 80, 0.45);
            if (Is(Valleys.Mitterbach, Valleys.Hinterau) || Is(Valleys.Hinterau, Valleys.Trafon)) return (230, 320, 1.4);
            return (420, 150, 1.0);
        }

        /// <summary>Half width of the river's channel area (from the river centre to where the ground is untouched).</summary>
        public const double ValleyHalfWidth = 220;

        /// <summary>
        /// Natural terrain: the valleys with the river's banks worked in (a metre above the water, rising gently).
        /// Roads follow this; lakes and road beds are added by the detailed terrain.
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
        /// The round over the mountain starting at the foot of <paramref name="climbId"/>: up that climb, over the
        /// connector, down the other climb and back to the first foot the shortest way (not over the mountain). Null if
        /// the world has no such climb.
        /// </summary>
        public Route? ClimbLoop(string climbId)
        {
            var m = Mountain;
            var first = m?.Find(climbId);
            if (m == null || first == null) return null;
            var other = first == m.Stelvio ? m.Alpe : m.Stelvio;
            var steps = new List<DirectedEdge>
            {
                new DirectedEdge(first.Edge, true),
                new DirectedEdge(m.ConnectorEdge, first == m.Stelvio),
                new DirectedEdge(other.Edge, false),
            };
            steps.AddRange(ShortestPath(Edges[other.Edge].From, Edges[first.Edge].From, e => e.Kind == RoadKind.Climb || e.Kind == RoadKind.Connector));
            return BuildRoute(first.Data.Name, steps, isLoop: true, 0);
        }

        /// <summary>The shortest way between two nodes (Dijkstra), leaving out the edges <paramref name="skip"/> says.</summary>
        public List<DirectedEdge> ShortestPath(int from, int to, Func<RoadEdge, bool>? skip = null)
        {
            var dist = Enumerable.Repeat(double.MaxValue, Nodes.Count).ToArray();
            var via = new DirectedEdge?[Nodes.Count];
            var done = new bool[Nodes.Count];
            dist[from] = 0;
            for (int round = 0; round < Nodes.Count; round++)
            {
                int u = -1;
                for (int i = 0; i < Nodes.Count; i++)
                {
                    if (!done[i] && dist[i] < double.MaxValue && (u < 0 || dist[i] < dist[u])) u = i;
                }

                if (u < 0 || u == to) break;
                done[u] = true;
                foreach (int id in Nodes[u].Edges)
                {
                    var e = Edges[id];
                    if (skip != null && skip(e)) continue;
                    int v = e.Other(u);
                    if (dist[u] + e.Length < dist[v])
                    {
                        dist[v] = dist[u] + e.Length;
                        via[v] = new DirectedEdge(id, e.From == u);
                    }
                }
            }

            var path = new List<DirectedEdge>();
            for (int n = to; n != from;)
            {
                var step = via[n] ?? throw new InvalidOperationException($"No way from node {from} to {to}");
                path.Insert(0, step);
                var e = Edges[step.Edge];
                n = step.Forward ? e.From : e.To;
            }

            return path;
        }

        private static double Grade(RoadEdge e, int i)
        {
            int a = Math.Max(0, i - 2), b = Math.Min(e.X.Length - 1, i + 2);
            double run = e.D[b] - e.D[a];
            return run > 0 ? (e.E[b] - e.E[a]) / run * 100 : 0;
        }

        // ------------------------------------------------------------------ generation

        public static WorldMap Generate(int seed)
        {
            var map = new WorldMap(seed);
            var random = new Random(seed);
            map.Mountain = Mountain.Place(Valleys.FloorElevation[Valleys.Trafon]);
            var (nodes, roads) = Valleys.Plan(map.Mountain);
            map.BuildGraph(nodes, roads, map.Mountain);

            var start = map.Nodes[0];
            map.Bounds = (start.X - HomeHalfSize, start.Z - HomeHalfSize, start.X + HomeHalfSize, start.Z + HomeHalfSize);
            map.MountainBounds = BoundsOf(map.Edges.Select(e => ((IReadOnlyList<double>)e.X, (IReadOnlyList<double>)e.Z)));

            map.SetElevations();
            var (minX, minZ, maxX, maxZ) = map.MountainBounds;
            double margin = MountainTerrain.Margin + 600;
            map._floor = ValleyFloor.Build(map.Edges.Where(e => e.Kind == RoadKind.Valley), (minX - margin, minZ - margin, maxX + margin, maxZ + margin), ValleyShape);
            map.River = map.MakeRiver();
            map._riverIndex = new SegmentIndex(map.River.X, map.River.Z, 60);
            map.FindBridges();
            map.LiftBridges();
            map.MakeLakes(random);
            return map;
        }

        private void BuildGraph(List<(double X, double Z)> nodes, List<PlannedRoad> roads, Mountain mountain)
        {
            for (int i = 0; i < nodes.Count; i++) Nodes.Add(new RoadNode(i, nodes[i].X, nodes[i].Z) { Name = Valleys.Names[i] });
            foreach (var r in roads)
            {
                AddEdge(r.From, r.To, r.Path, r.MainLoop, RoadSurface.Asphalt, r.Kind);
                if (r.MainLoop) MainLoopEdges.Add(Edges.Count - 1);
            }

            // Alm round: Kirchbach up to Oberalm, over the plateau, down to Mitterbach, back up the valley to Kirchbach.
            int Find(int from, int to) => Edges.First(e => e.From == from && e.To == to).Id;
            AlmLoopSteps.Add(new DirectedEdge(Find(Valleys.Kirchbach, Valleys.Oberalm), true));
            AlmLoopSteps.Add(new DirectedEdge(Find(Valleys.Oberalm, Valleys.AlmEnd), true));
            AlmLoopSteps.Add(new DirectedEdge(Find(Valleys.AlmEnd, Valleys.Mitterbach), true));
            AlmLoopSteps.Add(new DirectedEdge(Find(Valleys.Kirchbach, Valleys.Mitterbach), false));

            // Mountain: the feet are Trafon and Valbrune; summits, climbs (foot → summit) and the connector.
            var s = mountain.Stelvio;
            var a = mountain.Alpe;
            int Node((double X, double Z) p, string name)
            {
                var node = new RoadNode(Nodes.Count, p.X, p.Z) { Name = name };
                Nodes.Add(node);
                return node.Id;
            }

            int topS = Node(s.Point(s.Data.X.Length - 1), s.Data.Name);
            int topA = Node(a.Point(a.Data.X.Length - 1), a.Data.Name);
            AddEdge(Valleys.Trafon, topS, ClimbPath(s), false, RoadSurface.Asphalt, RoadKind.Climb, s.Data.Id);
            s.Edge = Edges.Count - 1;
            AddEdge(topS, topA, mountain.ConnectorPath, false, RoadSurface.Asphalt, RoadKind.Connector);
            mountain.ConnectorEdge = Edges.Count - 1;
            AddEdge(Valleys.Valbrune, topA, ClimbPath(a), false, RoadSurface.Asphalt, RoadKind.Climb, a.Data.Id);
            a.Edge = Edges.Count - 1;
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

        private void AddEdge(int from, int to, Polyline path, bool mainLoop, RoadSurface surface, RoadKind kind, string? climbId = null)
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

        // ------------------------------------------------------------------ elevation

        /// <summary>
        /// Road heights. Valley roads: from village to village with a gentle wave (a saddle on the road between the two
        /// valleys). Mountain roads: an even gradient from end to end, eased over the first and last 120 m. Climbs: the
        /// GPX elevations. Connector: level between the equal summits with a gentle rise in the middle.
        /// </summary>
        private void SetElevations()
        {
            for (int i = 0; i < Valleys.FloorElevation.Length; i++) Nodes[i].Elevation = Valleys.FloorElevation[i];
            var m = Mountain!;
            Nodes[Valleys.Valbrune].Elevation = m.Alpe.FootElevation;
            Nodes[Edges[m.Stelvio.Edge].To].Elevation = m.Stelvio.SummitElevation;
            Nodes[Edges[m.Alpe.Edge].To].Elevation = m.Alpe.SummitElevation;

            Nodes[Valleys.Oberalm].Elevation = Valleys.OberalmElevation;
            Nodes[Valleys.AlmEnd].Elevation = Valleys.AlmEndElevation;

            foreach (var e in Edges)
            {
                int n = e.X.Length;
                double a = Nodes[e.From].Elevation, b = Nodes[e.To].Elevation;
                if (e.Kind == RoadKind.Climb)
                {
                    var climb = m.Find(e.ClimbId!)!;
                    for (int i = 0; i < n; i++) e.E[i] = climb.Elevation(i);
                    continue;
                }

                bool saddle = e.Kind == RoadKind.Valley && !e.IsMainLoop;
                if (e.Kind == RoadKind.Pass)
                {
                    EvenGradient(e, a, b);
                    continue;
                }

                for (int i = 0; i < n; i++)
                {
                    double f = e.D[i] / e.Length;
                    double h;
                    if (e.Kind == RoadKind.Valley)
                    {
                        // The valley round is level; only the saddle road (the short cut over the hill) rises 55 m in the middle.
                        h = a + ((b - a) * SmoothStep(f)) + (saddle ? 55 * Math.Pow(Math.Sin(Math.PI * f), 2) : 0);
                    }
                    else if (e.Kind == RoadKind.Connector)
                    {
                        h = a + ((b - a) * f) + (12 * Math.Sin(Math.PI * f));
                    }
                    else
                    {
                        // Even gradient, eased into the level at both ends.
                        double ease = Math.Min(1, Math.Min(e.D[i], e.Length - e.D[i]) / 120);
                        double t = f + ((SmoothStep(f) - f) * (1 - ease));
                        h = a + ((b - a) * t);
                    }

                    e.E[i] = h;
                }
            }
        }

        /// <summary>
        /// The same gradient all the way, easing in from level over the first 120 m and out over the last (a trapezoid
        /// of gradient along the road, scaled to the height difference).
        /// </summary>
        private static void EvenGradient(RoadEdge e, double a, double b)
        {
            int n = e.X.Length;
            var rise = new double[n];
            for (int i = 1; i < n; i++)
            {
                double mid = (e.D[i] + e.D[i - 1]) / 2;
                double w = Math.Min(1, Math.Min(mid, e.Length - mid) / 120);
                rise[i] = rise[i - 1] + ((e.D[i] - e.D[i - 1]) * w);
            }

            for (int i = 0; i < n; i++) e.E[i] = a + ((b - a) * rise[i] / rise[n - 1]);
        }

        // ------------------------------------------------------------------ water

        /// <summary>
        /// The river of the main valley: from a spring above Kirchbach along the valley to Trafon (170 m beside the road,
        /// on the side away from the mountain) and on out of the valley. Its level stays 2 m below the floor and never
        /// rises downstream.
        /// </summary>
        private River MakeRiver()
        {
            var steps = ShortestPath(Valleys.Kirchbach, Valleys.Trafon, e => !e.IsMainLoop);
            var road = new Polyline();
            foreach (var step in steps)
            {
                var e = Edges[step.Edge];
                int n = e.X.Length;
                for (int k = road.Count == 0 ? 0 : 1; k < n; k++)
                {
                    int i = step.Forward ? k : n - 1 - k;
                    road.Add(e.X[i], e.Z[i]);
                }
            }

            // Offset to the west (the right-hand side going down the valley), smoothed.
            var river = new Polyline();
            var (sx, sz) = (road.X[0] - road.X[40], road.Z[0] - road.Z[40]);
            double sl = Math.Sqrt((sx * sx) + (sz * sz));
            var (ex, ez) = (road.X[road.Count - 1] - road.X[road.Count - 41], road.Z[road.Count - 1] - road.Z[road.Count - 41]);
            double el = Math.Sqrt((ex * ex) + (ez * ez));
            for (double u = 260; u > 0; u -= 20) river.Add(road.X[0] + (sx / sl * u) - (sz / sl * 170), road.Z[0] + (sz / sl * u) + (sx / sl * 170));
            for (int i = 0; i < road.Count; i += 4)
            {
                var (nx, nz) = road.Normal(i);
                // Normal() points left of the direction of travel; going down the valley, west is to the right.
                river.Add(road.X[i] - (nx * 170), road.Z[i] - (nz * 170));
            }

            // Out of the valley: on in the last direction, bending gently.
            double px = river.X[river.Count - 1], pz = river.Z[river.Count - 1];
            double dx = ex / el, dz = ez / el;
            for (int k = 1; k <= 150; k++)
            {
                double turn = 0.0025 * Math.Sin(k / 25.0);
                (dx, dz) = ((dx * Math.Cos(turn)) - (dz * Math.Sin(turn)), (dx * Math.Sin(turn)) + (dz * Math.Cos(turn)));
                px += dx * 20;
                pz += dz * 20;
                river.Add(px, pz);
            }

            // Smoothed over about 300 m: the river meanders more gently than the road and never folds on itself
            // inside the road's tight bends (where it may cross under the road on a bridge).
            river = river.Resample(20);
            for (int pass = 0; pass < 3; pass++)
            {
                var smooth = new Polyline();
                for (int i = 0; i < river.Count; i++)
                {
                    int lo = Math.Max(0, i - 7), hi = Math.Min(river.Count - 1, i + 7);
                    double sx2 = 0, sz2 = 0;
                    for (int k = lo; k <= hi; k++)
                    {
                        sx2 += river.X[k];
                        sz2 += river.Z[k];
                    }

                    smooth.Add(sx2 / (hi - lo + 1), sz2 / (hi - lo + 1));
                }

                river = smooth;
            }

            river = river.Resample(10);
            int count = river.Count;
            var level = new double[count];
            double previous = double.MaxValue;
            for (int i = 0; i < count; i++)
            {
                level[i] = Math.Min(previous, _floor!.Height(river.X[i], river.Z[i]) - 3);
                previous = level[i];
            }

            return new River(river.X.ToArray(), river.Z.ToArray(), level, halfWidth: 7);
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

        /// <summary>
        /// Bridge decks are at least 3 m above the water. The river runs 3 m below the valley floor, so the level valley
        /// roads cross it without a ramp; only roads that come in lower rise gently onto their bridge.
        /// </summary>
        private void LiftBridges()
        {
            foreach (var b in Bridges)
            {
                var e = Edges[b.Edge];
                if (e.Kind == RoadKind.Climb) continue;
                double deck = b.WaterLevel + 3;
                for (int i = 0; i < e.E.Length; i++) e.E[i] = Math.Max(e.E[i], deck - (0.03 * Math.Abs(e.D[i] - b.DistanceM)));
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

        /// <summary>A lake or two on the floor of the second valley, beside the road and clear of everything.</summary>
        private void MakeLakes(Random random)
        {
            // The big lake: an ellipse with a slightly irregular shore; the road goes round it.
            var big = Valleys.BigLake;
            var bigShape = new double[24];
            for (int i = 0; i < bigShape.Length; i++)
            {
                double a = i * Math.PI * 2 / bigShape.Length;
                double r = 1 / Math.Sqrt(Math.Pow(Math.Cos(a) / big.RX, 2) + Math.Pow(Math.Sin(a) / big.RZ, 2));
                bigShape[i] = r / big.RZ * (0.94 + (0.1 * Noise.Random01(i, 7, _seed + 11)));
            }

            AddLake(big.X, big.Z, big.RZ, bigShape);

            var candidates = new List<(double X, double Z)>();
            foreach (var e in Edges.Where(e => e.Kind == RoadKind.Valley && e.IsMainLoop && (e.From == Valleys.Trafon || e.To == Valleys.Laerchwald || e.From == Valleys.Laerchwald)))
            {
                var path = new Polyline();
                for (int i = 0; i < e.X.Length; i++) path.Add(e.X[i], e.Z[i]);
                for (int i = 60; i < path.Count - 60; i += 40)
                {
                    var (nx, nz) = path.Normal(i);
                    foreach (double side in new[] { -300.0, 300 }) candidates.Add((e.X[i] + (nx * side), e.Z[i] + (nz * side)));
                }
            }

            foreach (var c in candidates.OrderBy(_ => random.NextDouble()))
            {
                if (Lakes.Count >= 3) break;
                double radius = 70 + (random.NextDouble() * 70);
                if (DistanceToRoads(c.X, c.Z) < radius * 1.25 + 70) continue;
                if (DistanceToRiver(c.X, c.Z) < radius * 1.25 + 90) continue;
                if (ValleyDistance(c.X, c.Z) > 380) continue; // on the flat floor
                if (Lakes.Any(l => Math.Sqrt(((l.X - c.X) * (l.X - c.X)) + ((l.Z - c.Z) * (l.Z - c.Z))) < 2500)) continue;

                var shape = new double[24];
                for (int i = 0; i < shape.Length; i++) shape[i] = 0.82 + (0.3 * Noise.Random01(i, Lakes.Count, _seed + 5));
                for (int pass = 0; pass < 2; pass++)
                {
                    var copy = (double[])shape.Clone();
                    for (int i = 0; i < shape.Length; i++) shape[i] = (copy[(i + shape.Length - 1) % shape.Length] + (2 * copy[i]) + copy[(i + 1) % shape.Length]) / 4;
                }

                AddLake(c.X, c.Z, radius, shape);
            }
        }

        /// <summary>A lake whose water is a little below the lowest ground around it.</summary>
        private void AddLake(double x, double z, double radius, double[] shape)
        {
            var probe = new Lake(x, z, radius, shape, 0);
            double shore = double.MaxValue;
            for (int a = 0; a < 48; a++)
            {
                double angle = a * Math.PI * 2 / 48;
                for (double q = 1.0; q <= 1.6; q += 0.3)
                {
                    double r = probe.RadiusAt(angle) * q;
                    shore = Math.Min(shore, BaseHeight(x + (Math.Cos(angle) * r), z + (Math.Sin(angle) * r)));
                }
            }

            Lakes.Add(new Lake(x, z, radius, shape, shore - 0.6));
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
