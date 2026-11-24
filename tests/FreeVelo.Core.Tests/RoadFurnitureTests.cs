using FreeVelo.Core.World.Gen;

namespace FreeVelo.Core.Tests;

public class RoadFurnitureTests
{
    private static readonly Lazy<(Terrain Terrain, List<GuardRail> Rails)> World = new(() =>
    {
        var terrain = Terrain.Build(WorldMap.Default);
        terrain.Landscape = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        return (terrain, RoadFurniture.GuardRails(terrain));
    });

    [Fact]
    public void GuardRailsStandOnTheMountainRoads()
    {
        var (_, rails) = World.Value;
        double total = rails.Sum(r => r.Length);
        Console.WriteLine($"{rails.Count} guard rails, {total / 1000:0.0} km");
        var map = World.Value.Terrain.Map;
        foreach (var r in rails.OrderByDescending(r => r.Length).Take(6))
        {
            var e = map.Edges[r.Edge];
            var (x, z, h, rx, rz) = RoadFurniture.At(e, r.FromD + 15);
            var (x2, z2, _, _, _) = RoadFurniture.At(e, r.FromD + 16);
            Console.WriteLine($"  edge {r.Edge} side {r.Side} {r.FromD:0}-{r.ToD:0} m ({r.Length:0} m): at 15 m ({x:0.0}, {h:0.0}, {z:0.0}) forward ({x2 - x:0.00}, {z2 - z:0.00}) right ({rx:0.00}, {rz:0.00})");
        }
        Assert.NotEmpty(rails);
        Assert.All(rails, r => Assert.True(r.Length >= 20, $"rail on edge {r.Edge} is {r.Length:0} m"));
    }

    /// <summary>Section 7 of docs/GRAPHICS_OVERHAUL.md: no road furniture within lane reach of a centre line.</summary>
    [Fact]
    public void NoGuardRailInALaneOrOnAnotherRoad()
    {
        var (terrain, rails) = World.Value;
        var map = terrain.Map;
        foreach (var r in rails)
        {
            var e = map.Edges[r.Edge];
            for (double d = r.FromD; d <= r.ToD; d += 2)
            {
                var (x, z, _, rx, rz) = RoadFurniture.At(e, d);
                double o = (e.Width / 2) + RoadFurniture.RailOffset;
                double px = x + (rx * r.Side * o), pz = z + (rz * r.Side * o);
                double dist = terrain.DistanceToRoad(px, pz, 12, out var road, out _, out _);
                Assert.True(road == null || dist >= (road.Width / 2) + 1.0, $"rail on edge {r.Edge} at {d:0} m is {dist:0.0} m from the centre of edge {road?.Id}");
            }
        }
    }
}
