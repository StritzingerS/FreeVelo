using FreeVelo.Core.World;
using FreeVelo.Core.World.Gen;

namespace FreeVelo.Core.Tests;

/// <summary>
/// The rider turns at junctions on the arc of the road's rounded corner (owner, 24 Nov 2026: the rider used little of
/// the road and the radius was unnaturally small). The arc must stay on the paving.
/// </summary>
public class JunctionTurnTests
{
    private static readonly Lazy<Terrain> Ground = new(() => Terrain.Build(WorldMap.Default));

    [Fact]
    public void RoutesTurnAtJunctionsOnTheCornerArcAndStayOnThePaving()
    {
        var terrain = Ground.Value;
        int rounded = 0;
        foreach (string id in BuiltInRoutes.Ids)
        {
            var route = BuiltInRoutes.Get(id);
            rounded += route.RoundedJunctions;
            for (double d = route.Points[0].DistanceM; d < route.LengthM; d += 1)
            {
                var s = route.Sample(d);
                // the path itself and the rider on the right half of the road (1.4 m to the right)
                foreach (double lateral in new[] { 0.0, 1.4 })
                {
                    double x = s.X + (Math.Cos(s.HeadingRad) * lateral), z = s.Z - (Math.Sin(s.HeadingRad) * lateral);
                    Assert.True(OnPaving(terrain, x, z), $"{id} at {d:0} m (lateral {lateral}): ({x:0.0}, {z:0.0}) is off the road");
                }
            }
        }

        Console.WriteLine($"{rounded} junction turns ridden on an arc");
        Assert.True(rounded > 0);
    }

    [Fact]
    public void ARightAngleJunctionIsRiddenOnATwelveMetreArc()
    {
        // Straight north, then a 90° right turn at a single point (like a junction), rounded with 12.25 m.
        var points = new List<RoutePoint>();
        for (int i = 0; i <= 20; i++) points.Add(new RoutePoint(i * 5, 0, i * 5, 0, 0, FreeVelo.Core.Physics.RoadSurface.Asphalt));
        for (int i = 1; i <= 20; i++) points.Add(new RoutePoint(100 + (i * 5), i * 5, 100, 0, 0, FreeVelo.Core.Physics.RoadSurface.Asphalt));
        var route = new Route("corner", points, isLoop: false);
        route.RoundJunction(100, 12.25);
        Assert.Equal(1, route.RoundedJunctions);

        // Radius from three points on the arc
        var a = route.Sample(92);
        var b = route.Sample(100);
        var c = route.Sample(108);
        double ab = Dist(a, b), bc = Dist(b, c), ca = Dist(c, a);
        double area = Math.Abs(((b.X - a.X) * (c.Z - a.Z)) - ((c.X - a.X) * (b.Z - a.Z))) / 2;
        double radius = ab * bc * ca / (4 * area);
        Assert.InRange(radius, 11, 14);
        // Joins the road line smoothly: on the line 13 m before and after, heading along it.
        Assert.Equal(0, route.Sample(86).X, 2);
        Assert.Equal(100, route.Sample(114).Z, 2);
        Assert.InRange(Math.Abs(route.Sample(87.8).HeadingRad), 0, 0.02);
    }

    private static double Dist(RouteSample a, RouteSample b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    /// <summary>On a road (within its half width, plus 0.2 m) or on a paved junction corner.</summary>
    private static bool OnPaving(Terrain terrain, double x, double z)
    {
        double d = terrain.DistanceToRoad(x, z, 20, out var road, out _, out _);
        if (road != null && d <= (road.Width / 2) + 0.2) return true;
        foreach (var c in terrain.Corners)
        {
            if (x < c.MinX - 1 || x > c.MaxX + 1 || z < c.MinZ - 1 || z > c.MaxZ + 1) continue;
            if (c.Distance(x, z) < 0.2) return true;
        }

        // the junction pad between the road ends (convex hull of the ends; approximated by the node's reach)
        foreach (var n in terrain.Map.Nodes)
        {
            if (n.Edges.Count >= 3 && (((n.X - x) * (n.X - x)) + ((n.Z - z) * (n.Z - z))) < 25) return true;
        }

        return false;
    }
}
