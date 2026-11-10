using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>A planned road: its ends (node indices), shape and kind.</summary>
    internal sealed class PlannedRoad
    {
        public PlannedRoad(int from, int to, Polyline path, RoadKind kind, bool mainLoop = false)
        {
            From = from;
            To = to;
            Path = path;
            Kind = kind;
            MainLoop = mainLoop;
        }

        public int From { get; }
        public int To { get; }
        public Polyline Path { get; }
        public RoadKind Kind { get; }
        public bool MainLoop { get; }
    }

    /// <summary>
    /// The valleys around the mountain, laid out by hand (the same in every world): a main valley with the river
    /// (Kirchbach – Mitterbach – Hinterau – Trafon, where the Stelvio starts) and a second valley back over Lärchwald, so the
    /// two make a round; a road over a low saddle between Mitterbach and Lärchwald; the Alm road climbing out of Kirchbach in
    /// hairpins at an even gradient to the high plateau (Oberalm) and down again to Mitterbach along the slope; and the
    /// road from the plateau to Valbrune, where the Alpe d'Huez starts. Valley floors at about 630–680 m.
    /// </summary>
    internal static class Valleys
    {
        // Nodes (index = node id). Trafon is the Stelvio's foot; Valbrune the Alpe d'Huez's.
        public const int Laerchwald = 0, Kirchbach = 1, Mitterbach = 2, Hinterau = 3, Trafon = 4, Oberalm = 5, AlmEnd = 6, Valbrune = 7;

        public static readonly string[] Names = { "Lärchwald", "Kirchbach", "Mitterbach", "Hinterau", "Trafon", "Oberalm", "Oberalm", "Valbrune" };

        /// <summary>Valley floor heights of the villages (Oberalm and Valbrune follow from the roads and the climbs).</summary>
        public static readonly double[] FloorElevation = { 652, 682, 666, 648, 627 };

        /// <summary>Gradient of the Alm road (up and down).</summary>
        public const double AlmGrade = 0.055;

        public static readonly (double X, double Z)[] Position =
        {
            (-3400, -2400), // Laerchwald
            (300, 2300),    // Kirchbach
            (1100, -1300),  // Mitterbach
            (850, -4500),   // Hinterau
            (0, 0),         // Trafon: the Stelvio's foot (set from the climb)
            (0, 0),         // Oberalm: top of the Alm road (from the hairpins)
            (2600, 5000),   // east end of the plateau, top of the road down to Mitterbach
            (0, 0),         // Valbrune: the Alpe d'Huez's foot (set from the climb)
        };

        /// <summary>The roads. <paramref name="mountain"/> gives Trafon and Valbrune.</summary>
        public static (List<(double X, double Z)> Nodes, List<PlannedRoad> Roads) Plan(Mountain mountain)
        {
            var nodes = Position.ToList();
            nodes[Trafon] = mountain.Stelvio.Foot;
            nodes[Valbrune] = mountain.Alpe.Foot;

            var p = nodes;
            var roads = new List<PlannedRoad>();

            // Main loop (the valley round), in riding order from Laerchwald: north up the west valley to Kirchbach, down the
            // main valley to Trafon, back west to Laerchwald.
            roads.Add(new PlannedRoad(Laerchwald, Kirchbach, Curve(p[Laerchwald], (-3150, -1700), (-3380, -1100), (-2950, -500), (-2700, 250), (-2250, 650), (-1750, 1300), (-1150, 1250), (-650, 1950), p[Kirchbach]), RoadKind.Valley, true));
            roads.Add(new PlannedRoad(Kirchbach, Mitterbach, Curve(p[Kirchbach], (620, 1750), (430, 1150), (850, 600), (680, 0), (1150, -650), p[Mitterbach]), RoadKind.Valley, true));
            roads.Add(new PlannedRoad(Mitterbach, Hinterau, Curve(p[Mitterbach], (1350, -1900), (950, -2500), (1280, -3100), (880, -3700), (1120, -4100), p[Hinterau]), RoadKind.Valley, true));
            var stelvioIn = mountain.Stelvio.StartDirection;
            roads.Add(new PlannedRoad(Hinterau, Trafon, Curve(p[Hinterau], (560, -5050), (920, -5650), (560, -6250), (860, -6850), p[Trafon]), RoadKind.Valley, true));
            roads.Add(new PlannedRoad(Trafon, Laerchwald, Curve(p[Trafon], (300, -7760), (-350, -7450), (-900, -7700), (-1450, -7100), (-1750, -6400), (-2150, -5900), (-2700, -5250), (-2850, -4600), (-3250, -4000), (-3050, -3200), p[Laerchwald]), RoadKind.Valley, true));

            // Over the saddle between the two valleys.
            roads.Add(new PlannedRoad(Mitterbach, Laerchwald, Curve(p[Mitterbach], (500, -1550), (-150, -1300), (-800, -1750), (-1500, -1700), (-2200, -2150), (-2800, -2050), p[Laerchwald]), RoadKind.Valley));

            // Alm road up: north out of Kirchbach, then six long traverses with hairpins, then to Oberalm.
            var stack = Switchbacks((250, 2700), (1, 0), (0, 1), traverse: 1250, rise: 170, count: 6, radius: 16);
            var top = stack[stack.Count - 1];
            nodes[Oberalm] = (top.X + 60, top.Z + 220);
            var up = new List<(double X, double Z)> { p[Kirchbach], (265, 2520) };
            up.AddRange(stack);
            up.Add((top.X - 10, top.Z + 110));
            up.Add(nodes[Oberalm]);
            roads.Add(new PlannedRoad(Kirchbach, Oberalm, Curve(up.ToArray()), RoadKind.Pass));

            // Over the plateau, and down along the slope above the main valley to Mitterbach.
            roads.Add(new PlannedRoad(Oberalm, AlmEnd, Curve(nodes[Oberalm], (700, 4450), (1200, 4300), (1700, 4700), (2200, 4600), p[AlmEnd]), RoadKind.Pass));
            roads.Add(new PlannedRoad(AlmEnd, Mitterbach, Curve(p[AlmEnd], (2900, 4300), (2650, 3700), (2900, 3000), (2550, 2300), (2700, 1600), (2400, 1000), (2550, 350), (2300, -150), (2100, -800), (1600, -1300), p[Mitterbach]), RoadKind.Pass));

            // From the plateau up to Valbrune, arriving the way the Alpe d'Huez starts.
            var alpeOut = mountain.Alpe.StartDirection;
            var b = p[Valbrune];
            roads.Add(new PlannedRoad(Oberalm, Valbrune, Curve(nodes[Oberalm], (550, 4700), (1000, 5300), (1600, 5600), (1900, 6150), (2500, 6400), (3100, 6850), (b.X - (alpeOut.X * 450), b.Z - (alpeOut.Z * 450)), b), RoadKind.Pass));
            _ = stelvioIn;
            return (nodes, roads);
        }

        // ------------------------------------------------------------------ shapes

        /// <summary>A smooth road through the points (centripetal Catmull-Rom, no loops), every <see cref="WorldMap.Spacing"/> m.</summary>
        public static Polyline Curve(params (double X, double Z)[] pts)
        {
            var raw = new Polyline();
            int n = pts.Length;
            for (int i = 0; i < n - 1; i++)
            {
                var p0 = pts[Math.Max(0, i - 1)];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = pts[Math.Min(n - 1, i + 2)];
                if (i == 0) p0 = (p1.X - (p2.X - p1.X), p1.Z - (p2.Z - p1.Z));
                if (i == n - 2) p3 = (p2.X + (p2.X - p1.X), p2.Z + (p2.Z - p1.Z));
                double t0 = 0, t1 = t0 + Knot(p0, p1), t2 = t1 + Knot(p1, p2), t3 = t2 + Knot(p2, p3);
                double length = Math.Sqrt(Sq(p2.X - p1.X) + Sq(p2.Z - p1.Z));
                int steps = Math.Max(2, (int)(length / 1.5));
                for (int k = i == 0 ? 0 : 1; k <= steps; k++)
                {
                    double t = t1 + ((t2 - t1) * k / steps);
                    var a1 = Lerp(p0, p1, t0, t1, t);
                    var a2 = Lerp(p1, p2, t1, t2, t);
                    var a3 = Lerp(p2, p3, t2, t3, t);
                    var b1 = Lerp(a1, a2, t0, t2, t);
                    var b2 = Lerp(a2, a3, t1, t3, t);
                    var c = Lerp(b1, b2, t1, t2, t);
                    raw.Add(c.X, c.Z);
                }
            }

            return raw.Resample(WorldMap.Spacing);
        }

        private static double Knot((double X, double Z) a, (double X, double Z) b) => Math.Max(1e-3, Math.Pow(Sq(b.X - a.X) + Sq(b.Z - a.Z), 0.25));

        private static (double X, double Z) Lerp((double X, double Z) a, (double X, double Z) b, double ta, double tb, double t)
        {
            double f = (t - ta) / (tb - ta);
            return (a.X + ((b.X - a.X) * f), a.Z + ((b.Z - a.Z) * f));
        }

        /// <summary>
        /// Hairpins up a slope: <paramref name="count"/> traverses of <paramref name="traverse"/> m, each gaining
        /// <paramref name="rise"/> m uphill (<paramref name="up"/>), alternating along <paramref name="along"/>, joined by
        /// half circles of <paramref name="radius"/> m. Dense points (2 m).
        /// </summary>
        public static List<(double X, double Z)> Switchbacks((double X, double Z) start, (double X, double Z) along, (double X, double Z) up,
            double traverse, double rise, int count, double radius)
        {
            var pts = new List<(double X, double Z)>();
            var p = start;
            var dir = along;
            for (int k = 0; k < count; k++)
            {
                var q = (X: p.X + (dir.X * traverse) + (up.X * rise), Z: p.Z + (dir.Z * traverse) + (up.Z * rise));
                int steps = (int)(traverse / 2);
                for (int i = 0; i <= steps; i++)
                {
                    double f = i / (double)steps;
                    // A slight bow, so the traverses are not ruler-straight.
                    double bow = 22 * Math.Sin(Math.PI * f) * (k % 2 == 0 ? 1 : -1);
                    pts.Add((p.X + ((q.X - p.X) * f) + (up.X * bow), p.Z + ((q.Z - p.Z) * f) + (up.Z * bow)));
                }

                if (k == count - 1) break;
                var c = (X: q.X + (up.X * radius), Z: q.Z + (up.Z * radius));
                for (int i = 1; i < 24; i++)
                {
                    double t = Math.PI * i / 24;
                    pts.Add((c.X - (up.X * radius * Math.Cos(t)) + (dir.X * radius * Math.Sin(t)), c.Z - (up.Z * radius * Math.Cos(t)) + (dir.Z * radius * Math.Sin(t))));
                }

                p = (q.X + (up.X * 2 * radius), q.Z + (up.Z * 2 * radius));
                dir = (-dir.X, -dir.Z);
            }

            return pts;
        }

        private static double Sq(double v) => v * v;
    }

    /// <summary>
    /// The valley floor: for every point the height of the nearest valley road (blended smoothly away from the roads) and
    /// the distance to it, on a 32 m grid. The terrain rises from it; the mountain's relief stays off it.
    /// </summary>
    internal sealed class ValleyFloor
    {
        private const double Cell = 32;
        private readonly double _minX, _minZ;
        private readonly int _nx, _nz;
        private readonly float[] _height;
        private readonly float[] _distance;

        private ValleyFloor(double minX, double minZ, int nx, int nz)
        {
            _minX = minX;
            _minZ = minZ;
            _nx = nx;
            _nz = nz;
            _height = new float[nx * nz];
            _distance = new float[nx * nz];
        }

        public static ValleyFloor Build(IEnumerable<RoadEdge> valleyRoads, (double MinX, double MinZ, double MaxX, double MaxZ) area)
        {
            double minX = Math.Floor(area.MinX / Cell) * Cell, minZ = Math.Floor(area.MinZ / Cell) * Cell;
            int nx = (int)Math.Ceiling((area.MaxX - minX) / Cell) + 1, nz = (int)Math.Ceiling((area.MaxZ - minZ) / Cell) + 1;
            var f = new ValleyFloor(minX, minZ, nx, nz);
            var sum = new double[nx * nz];
            var count = new int[nx * nz];
            foreach (var e in valleyRoads)
            {
                for (int i = 0; i < e.X.Length; i++)
                {
                    int gx = (int)Math.Round((e.X[i] - minX) / Cell), gz = (int)Math.Round((e.Z[i] - minZ) / Cell);
                    if (gx < 0 || gz < 0 || gx >= nx || gz >= nz) continue;
                    sum[(gz * nx) + gx] += e.E[i];
                    count[(gz * nx) + gx]++;
                }
            }

            // Nearest road cell (chamfer distance transform carrying the road height along)
            var dist = new float[nx * nz];
            var near = new float[nx * nz];
            for (int k = 0; k < dist.Length; k++)
            {
                dist[k] = count[k] > 0 ? 0 : float.MaxValue / 2;
                near[k] = count[k] > 0 ? (float)(sum[k] / count[k]) : 0;
            }

            float a = (float)Cell, d = (float)(Cell * Math.Sqrt(2));
            void Relax(int k, int other, float step)
            {
                if (dist[other] + step < dist[k])
                {
                    dist[k] = dist[other] + step;
                    near[k] = near[other];
                }
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int gz = 0; gz < nz; gz++)
                {
                    for (int gx = 0; gx < nx; gx++)
                    {
                        int k = (gz * nx) + gx;
                        if (gx > 0) Relax(k, k - 1, a);
                        if (gz > 0) Relax(k, k - nx, a);
                        if (gx > 0 && gz > 0) Relax(k, k - nx - 1, d);
                        if (gx < nx - 1 && gz > 0) Relax(k, k - nx + 1, d);
                    }
                }

                for (int gz = nz - 1; gz >= 0; gz--)
                {
                    for (int gx = nx - 1; gx >= 0; gx--)
                    {
                        int k = (gz * nx) + gx;
                        if (gx < nx - 1) Relax(k, k + 1, a);
                        if (gz < nz - 1) Relax(k, k + nx, a);
                        if (gx < nx - 1 && gz < nz - 1) Relax(k, k + nx + 1, d);
                        if (gx > 0 && gz < nz - 1) Relax(k, k + nx - 1, d);
                    }
                }
            }

            // Away from the roads the nearest road's height is blurred (no creases where another road becomes nearest).
            var blurred = (float[])near.Clone();
            for (int pass = 0; pass < 3; pass++) blurred = BoxBlur(blurred, nx, nz, 8);
            for (int k = 0; k < dist.Length; k++)
            {
                double w = Math.Exp(-dist[k] / 180);
                f._height[k] = (float)((near[k] * w) + (blurred[k] * (1 - w)));
                f._distance[k] = dist[k];
            }

            return f;
        }

        private static float[] BoxBlur(float[] v, int nx, int nz, int r)
        {
            var tmp = new float[v.Length];
            var result = new float[v.Length];
            for (int gz = 0; gz < nz; gz++)
            {
                double s = 0;
                int n = 0;
                for (int gx = -r; gx < nx + r; gx++)
                {
                    int add = gx + r, remove = gx - r - 1;
                    if (add >= 0 && add < nx)
                    {
                        s += v[(gz * nx) + add];
                        n++;
                    }

                    if (remove >= 0 && remove < nx)
                    {
                        s -= v[(gz * nx) + remove];
                        n--;
                    }

                    if (gx >= 0 && gx < nx) tmp[(gz * nx) + gx] = (float)(s / n);
                }
            }

            for (int gx = 0; gx < nx; gx++)
            {
                double s = 0;
                int n = 0;
                for (int gz = -r; gz < nz + r; gz++)
                {
                    int add = gz + r, remove = gz - r - 1;
                    if (add >= 0 && add < nz)
                    {
                        s += tmp[(add * nx) + gx];
                        n++;
                    }

                    if (remove >= 0 && remove < nz)
                    {
                        s -= tmp[(remove * nx) + gx];
                        n--;
                    }

                    if (gz >= 0 && gz < nz) result[(gz * nx) + gx] = (float)(s / n);
                }
            }

            return result;
        }

        /// <summary>Floor height (the valley roads' height carried out sideways).</summary>
        public double Height(double x, double z) => Bilinear(_height, x, z);

        /// <summary>Distance to the nearest valley road (32 m resolution).</summary>
        public double Distance(double x, double z) => Bilinear(_distance, x, z);

        private double Bilinear(float[] grid, double x, double z)
        {
            double gx = (x - _minX) / Cell, gz = (z - _minZ) / Cell;
            int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz);
            double fx = gx - ix, fz = gz - iz;
            float At(int i, int k) => grid[(Math.Max(0, Math.Min(_nz - 1, k)) * _nx) + Math.Max(0, Math.Min(_nx - 1, i))];
            double a = At(ix, iz), b = At(ix + 1, iz), c = At(ix, iz + 1), d = At(ix + 1, iz + 1);
            return ((a + ((b - a) * fx)) * (1 - fz)) + ((c + ((d - c) * fx)) * fz);
        }
    }
}
