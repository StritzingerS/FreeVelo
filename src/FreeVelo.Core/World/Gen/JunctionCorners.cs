using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Physics;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>
    /// One rounded corner between two neighbouring roads at a junction: a fan from where their edges cross, along road
    /// A's edge, across a curve to road B and back along B's edge. The terrain is lowered under it and the app paves it.
    /// </summary>
    public sealed class JunctionCorner
    {
        public int Node;
        public int EdgeA, EdgeB;
        public bool StartA, StartB;

        /// <summary>The side of each road that faces the corner (−1 left, +1 right, looking from From to To).</summary>
        public int SideA, SideB;

        /// <summary>How far from the junction the corner runs along each road.</summary>
        public double ReachA, ReachB;

        public bool Asphalt;

        /// <summary>Fan centre (x, road height, z) and outline.</summary>
        public double CX, CY, CZ;
        public double[] X = new double[0], Y = new double[0], Z = new double[0];

        public double MinX, MinZ, MaxX, MaxZ;

        /// <summary>0 inside; otherwise the distance to the outline.</summary>
        public double Distance(double x, double z)
        {
            bool inside = false;
            double best = double.MaxValue;
            for (int i = 0, j = X.Length - 1; i < X.Length; j = i++)
            {
                if (((Z[i] > z) != (Z[j] > z)) && (x < ((X[j] - X[i]) * (z - Z[i]) / (Z[j] - Z[i])) + X[i])) inside = !inside;
                double abx = X[i] - X[j], abz = Z[i] - Z[j], l2 = (abx * abx) + (abz * abz);
                double f = l2 > 0 ? Math.Max(0, Math.Min(1, (((x - X[j]) * abx) + ((z - Z[j]) * abz)) / l2)) : 0;
                double dx = X[j] + (abx * f) - x, dz = Z[j] + (abz * f) - z;
                best = Math.Min(best, (dx * dx) + (dz * dz));
            }

            return inside ? 0 : Math.Sqrt(best);
        }
    }

    public static class JunctionCorners
    {
        /// <summary>Radius of the corner curve; shallow splits get longer corners.</summary>
        public const double Radius = 9;

        public static List<JunctionCorner> Build(WorldMap map)
        {
            var result = new List<JunctionCorner>();
            foreach (var node in map.Nodes)
            {
                if (node.Edges.Count < 3) continue;
                var ends = node.Edges.Select(id =>
                {
                    var e = map.Edges[id];
                    bool start = e.From == node.Id;
                    var (px, _, pz, _, _) = At(e, start ? 15 : e.Length - 15);
                    return (Edge: e, Start: start, Angle: Math.Atan2(pz - node.Z, px - node.X));
                }).OrderBy(t => t.Angle).ToList();

                for (int k = 0; k < ends.Count; k++)
                {
                    var a = ends[k];
                    var b = ends[(k + 1) % ends.Count];
                    double gap = b.Angle - a.Angle;
                    if (gap <= 0) gap += Math.PI * 2;
                    if (gap > 150 * Math.PI / 180) continue; // nearly straight on: nothing to round

                    int sideA = FacingSide(a.Edge, a.Start, b.Edge, b.Start);
                    int sideB = FacingSide(b.Edge, b.Start, a.Edge, a.Start);
                    double sa = Crossing(a.Edge, a.Start, sideA, b.Edge, b.Start);
                    double sb = Crossing(b.Edge, b.Start, sideB, a.Edge, a.Start);
                    if (sa < 0 || sb < 0) continue;

                    var x = Side(a.Edge, a.Start, sa, sideA, 0);
                    var a5 = Side(a.Edge, a.Start, sa + 5, sideA, 0);
                    var b0 = Side(b.Edge, b.Start, sb, sideB, 0);
                    var b5 = Side(b.Edge, b.Start, sb + 5, sideB, 0);
                    double dax = a5.X - x.X, daz = a5.Z - x.Z, dbx = b5.X - b0.X, dbz = b5.Z - b0.Z;
                    double cos = ((dax * dbx) + (daz * dbz)) / Math.Max(1e-9, Math.Sqrt(((dax * dax) + (daz * daz)) * ((dbx * dbx) + (dbz * dbz))));
                    double theta = Math.Acos(Math.Max(-1, Math.Min(1, cos)));
                    double reach = Math.Max(2, Math.Min(30, Radius / Math.Tan(Math.Max(0.05, theta / 2))));
                    var pa = Side(a.Edge, a.Start, sa + reach, sideA, 0);
                    var pb = Side(b.Edge, b.Start, sb + reach, sideB, 0);

                    var rx = new List<double>();
                    var ry = new List<double>();
                    var rz = new List<double>();
                    void Add((double X, double Y, double Z) p) { rx.Add(p.X); ry.Add(p.Y); rz.Add(p.Z); }
                    for (double s = sa; s < sa + reach; s += 1) Add(Side(a.Edge, a.Start, s, sideA, 0.3)); // tucked under the asphalt
                    // A circular arc touching both road edges (centre on the bisector): the grass between the roads
                    // ends in a round nose instead of a sharp tip.
                    double ua = Math.Sqrt(Sq(pa.X - x.X) + Sq(pa.Z - x.Z)), ub = Math.Sqrt(Sq(pb.X - x.X) + Sq(pb.Z - x.Z));
                    double bx = ((pa.X - x.X) / ua) + ((pb.X - x.X) / ub), bz = ((pa.Z - x.Z) / ua) + ((pb.Z - x.Z) / ub), bl = Math.Sqrt((bx * bx) + (bz * bz));
                    double centre = Radius / Math.Sin(Math.Max(0.05, theta / 2));
                    double ox = x.X + (bx / bl * centre), oz = x.Z + (bz / bl * centre);
                    double a0 = Math.Atan2(pa.Z - oz, pa.X - ox), a1 = Math.Atan2(pb.Z - oz, pb.X - ox);
                    double sweep = a1 - a0;
                    while (sweep > Math.PI) sweep -= 2 * Math.PI;
                    while (sweep < -Math.PI) sweep += 2 * Math.PI;
                    double ra = Math.Sqrt(Sq(pa.X - ox) + Sq(pa.Z - oz)), rb = Math.Sqrt(Sq(pb.X - ox) + Sq(pb.Z - oz));
                    const int segments = 16;
                    for (int j = 0; j <= segments; j++)
                    {
                        double t = j / (double)segments, ang = a0 + (sweep * t), r = ra + ((rb - ra) * t);
                        Add((ox + (Math.Cos(ang) * r), pa.Y + ((pb.Y - pa.Y) * t), oz + (Math.Sin(ang) * r)));
                    }

                    for (double s = sb + reach - 1; s > sb; s -= 1) Add(Side(b.Edge, b.Start, s, sideB, 0.3));

                    result.Add(new JunctionCorner
                    {
                        Node = node.Id, EdgeA = a.Edge.Id, EdgeB = b.Edge.Id, StartA = a.Start, StartB = b.Start, SideA = sideA, SideB = sideB,
                        ReachA = sa + reach, ReachB = sb + reach,
                        Asphalt = a.Edge.Surface == RoadSurface.Asphalt || b.Edge.Surface == RoadSurface.Asphalt,
                        CX = x.X, CY = x.Y, CZ = x.Z, X = rx.ToArray(), Y = ry.ToArray(), Z = rz.ToArray(),
                        MinX = rx.Min(), MaxX = rx.Max(), MinZ = rz.Min(), MaxZ = rz.Max(),
                    });
                }
            }

            return result;
        }

        /// <summary>Centre point, road height and direction at distance <paramref name="d"/> from the road's From end.</summary>
        public static (double X, double Y, double Z, double FX, double FZ) At(RoadEdge e, double d)
        {
            int i = 1;
            while (i < e.D.Length - 1 && e.D[i] < d) i++;
            double span = e.D[i] - e.D[i - 1];
            double f = span > 0 ? Math.Max(0, Math.Min(1, (d - e.D[i - 1]) / span)) : 0;
            double fx = e.X[i] - e.X[i - 1], fz = e.Z[i] - e.Z[i - 1], l = Math.Sqrt((fx * fx) + (fz * fz));
            if (l > 0) { fx /= l; fz /= l; }
            return (e.X[i - 1] + ((e.X[i] - e.X[i - 1]) * f), e.E[i - 1] + ((e.E[i] - e.E[i - 1]) * f), e.Z[i - 1] + ((e.Z[i] - e.Z[i - 1]) * f), fx, fz);
        }

        /// <summary>A point on one edge of a road, <paramref name="s"/> metres from the junction end (right = (fz, −fx)).</summary>
        public static (double X, double Y, double Z) Side(RoadEdge e, bool start, double s, int side, double inset)
        {
            var (x, y, z, fx, fz) = At(e, start ? s : e.Length - s);
            double off = side * ((e.Width / 2) - inset);
            return (x + (fz * off), y, z - (fx * off));
        }

        private static int FacingSide(RoadEdge e, bool start, RoadEdge o, bool oStart)
        {
            var (tx, _, tz, _, _) = At(o, oStart ? 12 : o.Length - 12);
            var r = Side(e, start, 12, 1, 0);
            var l = Side(e, start, 12, -1, 0);
            return Sq(r.X - tx) + Sq(r.Z - tz) < Sq(l.X - tx) + Sq(l.Z - tz) ? 1 : -1;
        }

        private static double Crossing(RoadEdge e, bool start, int side, RoadEdge o, bool oStart)
        {
            for (double s = 0; s < 80; s += 0.5)
            {
                var p = Side(e, start, s, side, 0);
                if (DistanceNearEnd(o, oStart, p.X, p.Z) >= o.Width / 2) return s;
            }

            return -1;
        }

        private static double DistanceNearEnd(RoadEdge o, bool start, double x, double z)
        {
            double best = double.MaxValue;
            for (int k = 0; k < o.X.Length - 1; k++)
            {
                double along = start ? o.D[k] : o.Length - o.D[k + 1];
                if (along > 140) continue;
                double ax = o.X[k], az = o.Z[k], abx = o.X[k + 1] - ax, abz = o.Z[k + 1] - az, l2 = (abx * abx) + (abz * abz);
                double f = l2 > 0 ? Math.Max(0, Math.Min(1, (((x - ax) * abx) + ((z - az) * abz)) / l2)) : 0;
                best = Math.Min(best, Sq(ax + (abx * f) - x) + Sq(az + (abz * f) - z));
            }

            return Math.Sqrt(best);
        }

        private static double Sq(double v) => v * v;
    }
}
