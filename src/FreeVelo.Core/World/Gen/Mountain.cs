using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>What a road is part of.</summary>
    public enum RoadKind
    {
        /// <summary>Main loop and link roads of the rolling home world.</summary>
        Home = 0,

        /// <summary>From the main loop to the foot of a climb.</summary>
        Access = 1,

        /// <summary>A famous climb, copied from GPX (foot → summit).</summary>
        Climb = 2,

        /// <summary>The short road between the two summits.</summary>
        Connector = 3,
    }

    /// <summary>A climb as placed in the world.</summary>
    public sealed class PlacedClimb
    {
        internal PlacedClimb(ClimbData data, double angle, double summitX, double summitZ, double footElevation)
        {
            Data = data;
            Angle = angle;
            SummitX = summitX;
            SummitZ = summitZ;
            FootElevation = footElevation;
        }

        public ClimbData Data { get; }

        /// <summary>Rotation of the GPX track around its summit (radians).</summary>
        public double Angle { get; }

        public double SummitX { get; }

        public double SummitZ { get; }

        /// <summary>World elevation of the foot (the GPX elevations are shifted to it).</summary>
        public double FootElevation { get; }

        public double SummitElevation => FootElevation + Data.Gain;

        /// <summary>Edge id of the climb (From = foot, To = summit).</summary>
        public int Edge { get; internal set; } = -1;

        public (double X, double Z) Point(int i)
        {
            double c = Math.Cos(Angle), s = Math.Sin(Angle);
            return (SummitX + (Data.X[i] * c) - (Data.Z[i] * s), SummitZ + (Data.X[i] * s) + (Data.Z[i] * c));
        }

        public double Elevation(int i) => FootElevation + (Data.E[i] - Data.E[0]);

        public (double X, double Z) Foot => Point(0);

        /// <summary>Direction of the road leaving the foot (unit vector).</summary>
        public (double X, double Z) StartDirection
        {
            get
            {
                var a = Point(0);
                var b = Point(Math.Min(Data.X.Length - 1, 8)); // 40 m on
                return Unit(b.X - a.X, b.Z - a.Z);
            }
        }

        /// <summary>Direction of the road arriving at the summit (unit vector).</summary>
        public (double X, double Z) ArrivalDirection
        {
            get
            {
                int n = Data.X.Length;
                var a = Point(Math.Max(0, n - 9));
                var b = Point(n - 1);
                return Unit(b.X - a.X, b.Z - a.Z);
            }
        }

        internal static (double X, double Z) Unit(double x, double z)
        {
            double l = Math.Sqrt((x * x) + (z * z));
            return l < 1e-9 ? (0, 1) : (x / l, z / l);
        }
    }

    /// <summary>
    /// The mountain next to the home world: Passo dello Stelvio and Alpe d'Huez as exact copies of their GPX tracks,
    /// meeting at a shared summit with a short road between them, each reached from the main loop by its own access
    /// road through its own valley. The summits are at the same height, so the Alpe d'Huez valley lies higher; the two
    /// access roads share that difference at the same gentle gradient (one down to the Stelvio valley, one up to the
    /// Alpe d'Huez valley).
    /// </summary>
    public sealed class Mountain
    {

        public const double ConnectorLength = 1000;
        private const double HomeClearance = 900;

        /// <summary>The climbs stay this far outside the home world's detailed terrain grid (whose ground stays as it is).</summary>
        internal const double GridClearance = 350;
        private const double RiverClearance = 250;
        private const double ClimbsApart = 400;
        private const double AccessTarget = 3500;
        private const double MaxAccessGrade = 0.065;

        internal Mountain(PlacedClimb stelvio, PlacedClimb alpe, Polyline connector, Polyline accessStelvio, Polyline accessAlpe,
            double loopStelvio, double loopAlpe, double junctionElevationStelvio, double junctionElevationAlpe)
        {
            Stelvio = stelvio;
            Alpe = alpe;
            ConnectorPath = connector;
            AccessStelvioPath = accessStelvio;
            AccessAlpePath = accessAlpe;
            LoopStelvio = loopStelvio;
            LoopAlpe = loopAlpe;
            JunctionElevationStelvio = junctionElevationStelvio;
            JunctionElevationAlpe = junctionElevationAlpe;
        }

        public PlacedClimb Stelvio { get; }

        public PlacedClimb Alpe { get; }

        public IEnumerable<PlacedClimb> Climbs => new[] { Stelvio, Alpe };

        public double SummitElevation => Stelvio.SummitElevation;

        internal Polyline ConnectorPath { get; }

        internal Polyline AccessStelvioPath { get; }

        internal Polyline AccessAlpePath { get; }

        /// <summary>Main loop distance of the junction to the Stelvio access road.</summary>
        internal double LoopStelvio { get; }

        internal double LoopAlpe { get; }

        internal double JunctionElevationStelvio { get; }

        internal double JunctionElevationAlpe { get; }

        public int ConnectorEdge { get; internal set; } = -1;

        public int AccessStelvioEdge { get; internal set; } = -1;

        public int AccessAlpeEdge { get; internal set; } = -1;

        /// <summary>Main loop node where the Stelvio access road starts.</summary>
        public int JunctionStelvio { get; internal set; } = -1;

        public int JunctionAlpe { get; internal set; } = -1;

        public PlacedClimb? Find(string id) => Climbs.FirstOrDefault(c => c.Data.Id == id);

        // ------------------------------------------------------------------ planning

        /// <summary>
        /// Finds the placement: direction from the home world, rotation of each climb around its summit, the side of
        /// the second summit and the two junctions on the main loop. Deterministic (no randomness).
        /// </summary>
        /// <summary>
        /// The placement the search finds for the default world (direction index, Stelvio turn, side, Alpe d'Huez turn).
        /// Tried first, so the world is generated in well under a second; the full search (several seconds) only runs
        /// when it doesn't fit, e.g. for another seed. A test checks that it still is the search's best result.
        /// </summary>
        internal static readonly (int Theta, double G1, int Side, double G2) DefaultHint = (6, 30, 1, -60);

        /// <summary>The parameters the plan was made with (see <see cref="DefaultHint"/>).</summary>
        internal (int Theta, double G1, int Side, double G2) Key { get; private set; }

        internal static Mountain Plan(WorldMap map, Polyline loop, IReadOnlyList<Polyline> links, IReadOnlyList<double> linkJunctions, bool useHint = true)
        {
            var stelvio = ClimbData.Load(ClimbData.Stelvio);
            var alpe = ClimbData.Load(ClimbData.AlpeDHuez);
            var home = new List<Polyline> { loop };
            home.AddRange(links);
            var homeIndex = home.Select(p => new SegmentIndex(p.X.ToArray(), p.Z.ToArray(), 100)).ToList();
            var river = map.River!;
            var riverIndex = new SegmentIndex(river.X, river.Z, 100);
            double cx = loop.X.Average(), cz = loop.Z.Average();

            double HomeDistance(double x, double z, double max)
            {
                double best = max;
                foreach (var index in homeIndex) best = Math.Min(best, index.Nearest(x, z, best, out _, out _));
                return best;
            }

            // Every 10th point (50 m) is enough for the clearance checks.
            static int[] Sample(ClimbData c) => Enumerable.Range(0, c.X.Length).Where(i => i % 10 == 0 || i == c.X.Length - 1).ToArray();
            var sampleStelvio = Sample(stelvio);
            var sampleAlpe = Sample(alpe);

            var (bminX, bminZ, bmaxX, bmaxZ) = map.Bounds;
            double grid = Terrain.Margin + GridClearance;
            bool InHomeGrid(double x, double z) => x > bminX - grid && x < bmaxX + grid && z > bminZ - grid && z < bmaxZ + grid;

            bool ClearOfHome(PlacedClimb p, int[] sample)
            {
                foreach (int i in sample)
                {
                    var (x, z) = p.Point(i);
                    if (InHomeGrid(x, z)) return false;
                    if (HomeDistance(x, z, HomeClearance) < HomeClearance) return false;
                    if (riverIndex.Nearest(x, z, RiverClearance, out _, out _) < RiverClearance) return false;
                }

                return true;
            }

            Mountain? best = null;
            double bestScore = double.MaxValue;
            bool hinted = useHint && DefaultHint.Theta >= 0;
            for (int pass = hinted ? 0 : 1; pass < 2 && best == null; pass++)
            for (int t = 0; t < 36; t++)
            {
                if (pass == 0 && t != DefaultHint.Theta) continue;
                double theta = t * Math.PI / 18;
                double ux = Math.Cos(theta), uz = Math.Sin(theta);
                double wx = -uz, wz = ux;
                foreach (double g1 in new[] { 0.0, -15, 15, -30, 30 })
                {
                    if (pass == 0 && g1 != DefaultHint.G1) continue;
                    // Stelvio: its foot points back towards the home world (turned by g1)
                    double footAngle1 = Math.Atan2(stelvio.Z[0], stelvio.X[0]);
                    double want1 = Math.Atan2(-uz, -ux) + (g1 * Math.PI / 180);
                    double a1 = want1 - footAngle1;

                    // Summit distance: the foot ends up about AccessTarget from the home roads.
                    PlacedClimb? s1 = null;
                    for (double ds = 3000; ds <= 32000; ds += 250)
                    {
                        var probe = new PlacedClimb(stelvio, a1, cx + (ux * ds), cz + (uz * ds), 0);
                        var f = probe.Foot;
                        if (HomeDistance(f.X, f.Z, AccessTarget) >= AccessTarget && ClearOfHome(probe, sampleStelvio))
                        {
                            s1 = probe;
                            break;
                        }
                    }

                    if (s1 == null) continue;

                    foreach (int side in new[] { 1, -1 })
                    {
                        if (pass == 0 && side != DefaultHint.Side) continue;
                        // Second summit next to the first; its exact spot follows from the connector's length.
                        double t2x = s1.SummitX + (side * wx * ConnectorLength * 0.75), t2z = s1.SummitZ + (side * wz * ConnectorLength * 0.75);
                        foreach (double g2 in new[] { 20.0, 40, 60, -20, -40, -60 })
                        {
                            if (pass == 0 && g2 != DefaultHint.G2) continue;
                            double footAngle2 = Math.Atan2(alpe.Z[0], alpe.X[0]);
                            double want2 = Math.Atan2(-uz, -ux) + (side * g2 * Math.PI / 180);
                            var s2 = new PlacedClimb(alpe, want2 - footAngle2, t2x, t2z, 0);
                            if (!ClearOfHome(s2, sampleAlpe)) continue;
                            if (!Apart(s1, sampleStelvio, s2, sampleAlpe)) continue;

                            var connector = Connector(s1, s2);
                            if (connector == null) continue;

                            var plan = WithAccess(map, loop, cx, cz, linkJunctions, s1, s2, connector, HomeDistance);
                            if (plan == null) continue;
                            double score = plan.AccessStelvioPath.Length + plan.AccessAlpePath.Length;
                            if (score < bestScore)
                            {
                                bestScore = score;
                                best = plan;
                                best.Key = (t, g1, side, g2);
                            }
                        }
                    }
                }
            }

            return best ?? throw new InvalidOperationException("No place for the mountain");
        }

        /// <summary>The two climbs stay apart (except near the shared summit).</summary>
        private static bool Apart(PlacedClimb a, int[] sa, PlacedClimb b, int[] sb)
        {
            double la = a.Data.Length, lb = b.Data.Length;
            foreach (int i in sa)
            {
                if (la - a.Data.D[i] < 600) continue;
                var (ax, az) = a.Point(i);
                foreach (int k in sb)
                {
                    if (lb - b.Data.D[k] < 600) continue;
                    var (bx, bz) = b.Point(k);
                    if (((ax - bx) * (ax - bx)) + ((az - bz) * (az - bz)) < ClimbsApart * ClimbsApart) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The road between the summits: it leaves the first summit the way the climb arrived and joins the second the
        /// way that climb arrived, so riding over the top feels like one road. Null if it would touch the climbs.
        /// </summary>
        private static Polyline? Connector(PlacedClimb a, PlacedClimb b)
        {
            var (ax, az) = a.Point(a.Data.X.Length - 1);
            var (bx, bz) = b.Point(b.Data.X.Length - 1);
            var ta = a.ArrivalDirection;
            var tb = b.ArrivalDirection;
            double gap = Math.Sqrt(((bx - ax) * (bx - ax)) + ((bz - az) * (bz - az)));
            double k = Math.Max(150, gap * 0.45);
            var path = Polyline.Bezier(ax, az, ax + (ta.X * k), az + (ta.Z * k), bx + (tb.X * k), bz + (tb.Z * k), bx, bz, 300).Resample(WorldMap.Spacing);
            if (path.Length > 2.2 * ConnectorLength) return null;

            // Clear of both climbs, apart from where it starts and ends
            foreach (var c in new[] { a, b })
            {
                int n = c.Data.X.Length;
                for (int i = 0; i < n - 30; i += 4)
                {
                    var (x, z) = c.Point(i);
                    for (int j = 20; j < path.Count - 20; j += 4)
                    {
                        if (((path.X[j] - x) * (path.X[j] - x)) + ((path.Z[j] - z) * (path.Z[j] - z)) < 30 * 30) return null;
                    }
                }
            }

            return path;
        }

        /// <summary>Junctions on the main loop and the two access roads; null if they don't fit.</summary>
        private static Mountain? WithAccess(WorldMap map, Polyline loop, double cx, double cz, IReadOnlyList<double> linkJunctions,
            PlacedClimb s1, PlacedClimb s2, Polyline connector, Func<double, double, double, double> homeDistance)
        {
            double Closest((double X, double Z) p)
            {
                int best = 0;
                double bestD = double.MaxValue;
                for (int i = 0; i < loop.Count; i += 2)
                {
                    double d = ((loop.X[i] - p.X) * (loop.X[i] - p.X)) + ((loop.Z[i] - p.Z) * (loop.Z[i] - p.Z));
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }

                return loop.D[best];
            }

            double l1 = Closest(s1.Foot), l2 = Closest(s2.Foot);
            double length = loop.Length;
            double Gap(double a, double b)
            {
                double d = Math.Abs(a - b) % length;
                return Math.Min(d, length - d);
            }

            // Junctions apart from each other and from the link roads' junctions
            if (Gap(l1, l2) < 500) l2 = (l2 + (Math.Sign(((l2 - l1 + length + (length / 2)) % length) - (length / 2)) * 500) + length) % length;
            foreach (double j in linkJunctions)
            {
                if (Gap(l1, j) < 350 || Gap(l2, j) < 350) return null;
            }

            if (Gap(l1, l2) < 450) return null;

            var accessA = Access(loop, cx, cz, l1, s1);
            var accessB = Access(loop, cx, cz, l2, s2);

            // Clear of the home world (after leaving the loop), of the other climb and of each other
            if (!Clear(accessA, homeDistance, s2, s1, connector)) return null;
            if (!Clear(accessB, homeDistance, s1, s2, connector)) return null;
            for (int i = 0; i < accessA.Count; i += 10)
            {
                if (accessB.DistanceTo(accessA.X[i], accessA.Z[i]) < 300 && Math.Min(accessA.D[i], accessA.Length - accessA.D[i]) > 400) return null;
            }

            // Heights: summits equal, so the Alpe d'Huez foot is higher; the access roads share the difference.
            double e1 = JunctionElevation(map, loop, l1), e2 = JunctionElevation(map, loop, l2);
            double difference = s1.Data.Gain - s2.Data.Gain + (e1 - e2); // how much higher foot 2 is than foot 1, relative to their junctions
            // Only the part outside the home grid takes up the height difference (inside, the road follows the ground).
            double outsideA = OutsideGrid(map, accessA), outsideB = OutsideGrid(map, accessB);
            double total = outsideA + outsideB;
            if (total < 1000 || Math.Abs(difference) / total > MaxAccessGrade) return null;
            double down = difference * outsideA / total;
            double foot1 = e1 - down;
            var c1 = new PlacedClimb(s1.Data, s1.Angle, s1.SummitX, s1.SummitZ, foot1);
            var c2 = new PlacedClimb(s2.Data, s2.Angle, s2.SummitX, s2.SummitZ, c1.SummitElevation - s2.Data.Gain);
            return new Mountain(c1, c2, connector, accessA, accessB, l1, l2, e1, e2);
        }

        /// <summary>Length of an access road after it has left the home world's terrain grid.</summary>
        internal static double OutsideGrid(WorldMap map, Polyline path)
        {
            var (minX, minZ, maxX, maxZ) = map.Bounds;
            double m = Terrain.Margin + 60;
            double exit = 0;
            for (int i = 0; i < path.Count; i++)
            {
                if (path.X[i] > minX - m && path.X[i] < maxX + m && path.Z[i] > minZ - m && path.Z[i] < maxZ + m) exit = path.D[i];
            }

            return path.Length - exit;
        }

        /// <summary>Same height as the node the world puts there (average ground around it).</summary>
        internal static double JunctionElevation(WorldMap map, Polyline loop, double s)
        {
            var (x, z) = loop.At(s);
            double sum = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++) sum += map.BaseHeight(x + (dx * 40), z + (dz * 40));
            }

            return sum / 9;
        }

        /// <summary>From the loop (leaving at a right angle, outwards) to the foot, arriving in the climb's direction.</summary>
        private static Polyline Access(Polyline loop, double cx, double cz, double s, PlacedClimb climb)
        {
            var (ax, az, nx, nz) = loop.PointAndNormal(s, cx, cz, inside: false);
            var (fx, fz) = climb.Foot;
            var (dx, dz) = climb.StartDirection;
            double gap = Math.Sqrt(((fx - ax) * (fx - ax)) + ((fz - az) * (fz - az)));
            double k = gap * 0.4;
            return Polyline.Bezier(ax, az, ax + (nx * k), az + (nz * k), fx - (dx * k), fz - (dz * k), fx, fz, 600).Resample(WorldMap.Spacing);
        }

        private static bool Clear(Polyline access, Func<double, double, double, double> homeDistance, PlacedClimb other, PlacedClimb own, Polyline connector)
        {
            for (int i = 0; i < access.Count; i += 6)
            {
                double x = access.X[i], z = access.Z[i];
                double fromLoop = access.D[i], toFoot = access.Length - access.D[i];
                if (fromLoop > 300 && homeDistance(x, z, 150) < 150) return false;
                if (connector.DistanceTo(x, z) < 200) return false;
                for (int k = 0; k < other.Data.X.Length; k += 10)
                {
                    var (ox, oz) = other.Point(k);
                    if (((ox - x) * (ox - x)) + ((oz - z) * (oz - z)) < 150 * 150) return false;
                }

                if (toFoot < 300) continue;
                for (int k = 0; k < own.Data.X.Length; k += 10)
                {
                    var (ox, oz) = own.Point(k);
                    if (((ox - x) * (ox - x)) + ((oz - z) * (oz - z)) < 120 * 120) return false;
                }
            }

            return true;
        }
    }
}
