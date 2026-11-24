using System;
using System.Collections.Generic;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>A guard rail along one side of a road: from <see cref="FromD"/> to <see cref="ToD"/> metres along the edge.</summary>
    public readonly struct GuardRail
    {
        public GuardRail(int edge, int side, double fromD, double toD, bool joinsAtStart = false, bool joinsAtEnd = false)
        {
            Edge = edge;
            Side = side;
            FromD = fromD;
            ToD = toD;
            JoinsAtStart = joinsAtStart;
            JoinsAtEnd = joinsAtEnd;
        }

        /// <summary>The rail meets a bridge railing at its start (no sloping end there).</summary>
        public bool JoinsAtStart { get; }

        /// <summary>The rail meets a bridge railing at its end (no sloping end there).</summary>
        public bool JoinsAtEnd { get; }

        public int Edge { get; }

        /// <summary>−1 left, +1 right (looking from the edge's From node to its To node).</summary>
        public int Side { get; }

        public double FromD { get; }

        public double ToD { get; }

        public double Length => ToD - FromD;
    }

    /// <summary>
    /// Road furniture placed along the roads (deterministic, from the road data and the ground only). Guard rails stand
    /// where the land falls away steeply beside the road and on the outside of tight bends, never at junctions, on
    /// bridges, in villages, across side lanes or on another road.
    /// </summary>
    public static class RoadFurniture
    {
        /// <summary>Shoulder width beside the asphalt (as the app draws it).</summary>
        public const double ShoulderWidth = 0.9;

        /// <summary>The rail's face: this far beyond the asphalt's edge.</summary>
        public const double RailOffset = ShoulderWidth + 0.25;

        private const double Step = 5;
        private const double DropReach = 8;      // how far beyond the edge the fall is measured
        private const double SteepDrop = 2.5;    // metres lower there: a rail
        private const double BendRadius = 45;    // tighter bends get a rail on the outside ...
        private const double BendDrop = 0.8;     // ... if the land falls at least this much there
        private const double JunctionClear = 22;
        private const double MinLength = 20;
        private const double EndExtension = 6;   // beyond the last dangerous point (the ends slope down to the ground)

        public static List<GuardRail> GuardRails(Terrain terrain)
        {
            var map = terrain.Map;
            var rails = new List<GuardRail>();
            foreach (var e in map.Edges)
            {
                if (e.Surface != Physics.RoadSurface.Asphalt) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    double runStart = double.NaN, lastHit = double.NaN;
                    for (double d = 0; d <= e.Length; d += Step)
                    {
                        bool hit = NeedsRail(terrain, e, side, d);
                        if (hit)
                        {
                            if (double.IsNaN(runStart)) runStart = d;
                            lastHit = d;
                        }
                        else if (!double.IsNaN(runStart) && d - lastHit > Step * 1.5)
                        {
                            Add(terrain, rails, e, side, runStart, lastHit);
                            runStart = double.NaN;
                        }
                    }

                    if (!double.IsNaN(runStart)) Add(terrain, rails, e, side, runStart, lastHit);
                }
            }

            AddBridgeApproaches(terrain, rails);
            return rails;
        }

        /// <summary>
        /// Kerbs along the asphalt's edge in the villages (within each village's radius of its centre): interrupted at
        /// junctions, on bridges (they have their own kerb), where a lane, yard or path meets the road (a dropped kerb,
        /// as at a driveway) and next to another road; at least 6 m long.
        /// </summary>
        public static List<GuardRail> Kerbs(Terrain terrain)
        {
            var kerbs = new List<GuardRail>();
            var landscape = terrain.Landscape;
            if (landscape == null) return kerbs;
            var map = terrain.Map;
            foreach (var e in map.Edges)
            {
                if (e.Surface != Physics.RoadSurface.Asphalt) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    double start = double.NaN, last = double.NaN;
                    for (double d = 0; d <= e.Length; d += 1)
                    {
                        bool ok = KerbAllowed(terrain, landscape, e, side, d);
                        if (ok)
                        {
                            if (double.IsNaN(start)) start = d;
                            last = d;
                        }
                        else if (!double.IsNaN(start))
                        {
                            if (last - start >= 6) kerbs.Add(new GuardRail(e.Id, side, start, last));
                            start = double.NaN;
                        }
                    }

                    if (!double.IsNaN(start) && last - start >= 6) kerbs.Add(new GuardRail(e.Id, side, start, last));
                }
            }

            return kerbs;
        }

        private static bool KerbAllowed(Terrain terrain, Landscape landscape, RoadEdge e, int side, double d)
        {
            if (d < 10 || d > e.Length - 10) return false;
            var (x, z, _, rx, rz) = At(e, d);
            bool inVillage = false;
            foreach (var v in landscape.Villages)
            {
                if (((v.X - x) * (v.X - x)) + ((v.Z - z) * (v.Z - z)) < v.Radius * v.Radius)
                {
                    inVillage = true;
                    break;
                }
            }

            if (!inVillage) return false;
            var map = terrain.Map;
            foreach (var b in map.Bridges)
            {
                if (b.Edge == e.Id && Math.Abs(b.DistanceM - d) < BridgeHalfSpan(map) + 1) return false;
            }

            double half = e.Width / 2;
            for (double o = 0.3; o <= 1.8; o += 0.75)
            {
                double px = x + (rx * side * (half + o)), pz = z + (rz * side * (half + o));
                if (landscape.BuiltOn(px, pz)) return false; // a lane, yard or path meets the road here
                foreach (var c in terrain.Corners)
                {
                    if (px < c.MinX - 2 || px > c.MaxX + 2 || pz < c.MinZ - 2 || pz > c.MaxZ + 2) continue;
                    if (c.Distance(px, pz) < 1.5) return false;
                }
            }

            double kx = x + (rx * side * (half + 0.1)), kz = z + (rz * side * (half + 0.1));
            double dist = terrain.DistanceToRoad(kx, kz, 12, out var road, out _, out _);
            if (road != null && road.Id != e.Id && dist < (road.Width / 2) + 1.5) return false;
            if (road != null && road.Id == e.Id && dist < half - 0.05) return false; // inside a tight bend over the road itself
            return true;
        }

        /// <summary>Where the bridge railings end (this far either side of the river centre, as the app builds the bridges).</summary>
        public static double BridgeHalfSpan(WorldMap map) => (map.River?.HalfWidth ?? 10) + 14;

        /// <summary>Length of the guard rail that continues a bridge railing on the approach.</summary>
        private const double ApproachLength = 18;

        /// <summary>
        /// At each corner of a bridge the guard rail continues the railing for about 18 m on land (as on real bridges),
        /// joined to it at the bridge end and sloping into the ground at the other; cut short where a rail may not stand.
        /// </summary>
        private static void AddBridgeApproaches(Terrain terrain, List<GuardRail> rails)
        {
            var map = terrain.Map;
            double half = BridgeHalfSpan(map);
            foreach (var b in map.Bridges)
            {
                var e = map.Edges[b.Edge];
                for (int side = -1; side <= 1; side += 2)
                {
                    // before the bridge: from the approach end up to the railing
                    double end = b.DistanceM - half;
                    double start = end;
                    for (double d = end - 1; d >= end - ApproachLength && Allowed(terrain, e, side, d, ignoreBridges: true); d -= 1) start = d;
                    if (end - start >= 8 && !Overlaps(rails, e.Id, side, start, end)) rails.Add(new GuardRail(e.Id, side, start, end, joinsAtEnd: true));

                    // after the bridge: from the railing on
                    start = b.DistanceM + half;
                    end = start;
                    for (double d = start + 1; d <= start + ApproachLength && Allowed(terrain, e, side, d, ignoreBridges: true); d += 1) end = d;
                    if (end - start >= 8 && !Overlaps(rails, e.Id, side, start, end)) rails.Add(new GuardRail(e.Id, side, start, end, joinsAtStart: true));
                }
            }
        }

        private static bool Overlaps(List<GuardRail> rails, int edge, int side, double from, double to)
        {
            foreach (var r in rails)
            {
                if (r.Edge == edge && r.Side == side && r.FromD < to && r.ToD > from) return true;
            }

            return false;
        }

        private static void Add(Terrain terrain, List<GuardRail> rails, RoadEdge e, int side, double from, double to)
        {
            // Extend over the ends, but only as far as a rail may stand (not into a junction, bridge or lane).
            double a = from, b = to;
            for (double d = from - 1; d >= from - EndExtension && Allowed(terrain, e, side, d); d -= 1) a = d;
            for (double d = to + 1; d <= to + EndExtension && Allowed(terrain, e, side, d); d += 1) b = d;

            // Every metre must be allowed (the run was found in 5 m steps): split where it is not.
            double start = double.NaN, last = a;
            for (double d = a; d <= b + 1e-6; d += 1)
            {
                bool ok = Allowed(terrain, e, side, d);
                if (ok && double.IsNaN(start)) start = d;
                if (!ok && !double.IsNaN(start))
                {
                    if (last - start >= MinLength) rails.Add(new GuardRail(e.Id, side, start, last));
                    start = double.NaN;
                }

                if (ok) last = d;
            }

            if (!double.IsNaN(start) && last - start >= MinLength) rails.Add(new GuardRail(e.Id, side, start, last));
        }

        private static bool NeedsRail(Terrain terrain, RoadEdge e, int side, double d)
        {
            if (!Allowed(terrain, e, side, d)) return false;
            var (x, z, h, rx, rz) = At(e, d);
            double half = e.Width / 2;
            double far = terrain.Height(x + (rx * side * (half + DropReach)), z + (rz * side * (half + DropReach)));
            double drop = h - far;
            if (drop >= SteepDrop) return true;

            // Tight bend: the outside is the side away from the turn.
            double k = Curvature(e, d);
            bool outside = (k > 0 && side < 0) || (k < 0 && side > 0); // k > 0: turning right
            return outside && Math.Abs(k) > 1 / BendRadius && drop >= BendDrop;
        }

        /// <summary>A rail may stand here: away from junctions and bridges, not in a village, a lane, the water or on another road.</summary>
        public static bool Allowed(Terrain terrain, RoadEdge e, int side, double d, bool ignoreBridges = false)
        {
            if (d < JunctionClear || d > e.Length - JunctionClear) return false;
            var map = terrain.Map;
            foreach (var b in map.Bridges)
            {
                if (b.Edge != e.Id) continue;
                double reach = Math.Abs(b.DistanceM - d);
                if (ignoreBridges ? reach < BridgeHalfSpan(map) - 0.01 : reach < BridgeHalfSpan(map) + 6) return false;
            }

            var (x, z, _, rx, rz) = At(e, d);
            double o = (e.Width / 2) + RailOffset + 0.2;
            double px = x + (rx * side * o), pz = z + (rz * side * o);
            var landscape = terrain.Landscape;
            // Bridge approaches also in villages (the bridges lie in them); otherwise no rails in villages.
            if (landscape != null && ((!ignoreBridges && landscape.Blocked(px, pz)) || landscape.BuiltOn(px, pz))) return false;
            if (terrain.IsWater(px, pz, 1)) return false;
            foreach (var c in terrain.Corners)
            {
                if (px < c.MinX - 3 || px > c.MaxX + 3 || pz < c.MinZ - 3 || pz > c.MaxZ + 3) continue;
                if (c.Distance(px, pz) < 3) return false;
            }

            // Not on (or right beside) another road, e.g. the next leg of a hairpin below, nor over this road's own
            // other leg inside a tight hairpin.
            double dist = terrain.DistanceToRoad(px, pz, 12, out var road, out _, out _);
            if (road != null && dist < (road.Width / 2) + (road.Id == e.Id ? 1.1 : 2)) return false;
            // The rail's face itself (inside a bend it comes closer to the road than the point checked above).
            double ox = x + (rx * side * ((e.Width / 2) + RailOffset)), oz = z + (rz * side * ((e.Width / 2) + RailOffset));
            double faceDist = terrain.DistanceToRoad(ox, oz, 12, out var faceRoad, out _, out _);
            if (faceRoad != null && faceDist < (faceRoad.Width / 2) + 1.05) return false;
            return true;
        }

        /// <summary>Position, road height and the unit vector to the right at <paramref name="d"/> metres along the edge.</summary>
        public static (double X, double Z, double H, double RightX, double RightZ) At(RoadEdge e, double d)
        {
            d = Math.Max(0, Math.Min(e.Length, d));
            int i = Array.BinarySearch(e.D, d);
            if (i < 0) i = ~i - 1;
            i = Math.Max(0, Math.Min(e.D.Length - 2, i));
            double seg = e.D[i + 1] - e.D[i];
            double f = seg > 0 ? (d - e.D[i]) / seg : 0;
            double x = e.X[i] + ((e.X[i + 1] - e.X[i]) * f), z = e.Z[i] + ((e.Z[i + 1] - e.Z[i]) * f);
            double h = e.E[i] + ((e.E[i + 1] - e.E[i]) * f);
            double fx = e.X[i + 1] - e.X[i], fz = e.Z[i + 1] - e.Z[i];
            double len = Math.Sqrt((fx * fx) + (fz * fz));
            if (len < 1e-6) return (x, z, h, 1, 0);
            fx /= len;
            fz /= len;
            return (x, z, h, fz, -fx);
        }

        /// <summary>Heading change per metre round <paramref name="d"/> (positive = turning right).</summary>
        private static double Curvature(RoadEdge e, double d)
        {
            const double w = 6;
            var a = At(e, d - w);
            var b = At(e, d + w);
            // Heading clockwise from +z (seen from above), from the right vectors: forward = (-rz, rx).
            double ha = Math.Atan2(-a.RightZ, a.RightX), hb = Math.Atan2(-b.RightZ, b.RightX);
            double dh = hb - ha;
            while (dh > Math.PI) dh -= 2 * Math.PI;
            while (dh < -Math.PI) dh += 2 * Math.PI;
            return dh / (2 * w);
        }
    }
}
