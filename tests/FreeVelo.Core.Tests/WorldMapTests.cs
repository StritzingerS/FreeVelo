using FreeVelo.Core.World;
using FreeVelo.Core.World.Gen;

namespace FreeVelo.Core.Tests;

public class WorldMapTests
{
    private static readonly WorldMap Map = WorldMap.Default;

    [Fact]
    public void SameSeedSameWorld()
    {
        var again = WorldMap.Generate(WorldMap.DefaultSeed);
        Assert.Equal(Map.Edges.Count, again.Edges.Count);
        for (int i = 0; i < Map.Edges.Count; i++)
        {
            Assert.Equal(Map.Edges[i].X, again.Edges[i].X);
            Assert.Equal(Map.Edges[i].E, again.Edges[i].E);
        }
    }

    [Fact]
    public void NetworkHasJunctionsWithChoices()
    {
        Assert.True(Map.Nodes.Count >= 4, "several junctions");
        Assert.True(Map.Edges.Count(e => !e.IsMainLoop) >= 2, "link roads besides the main loop");
        Assert.Contains(Map.Edges, e => e.Surface != Physics.RoadSurface.Asphalt);
        foreach (var node in Map.Nodes) Assert.True(node.Edges.Count >= 3, $"node {node.Id} is a junction");
    }

    [Fact]
    public void RoadsMeetAtTheSameHeightAndStayRideable()
    {
        foreach (var e in Map.Edges)
        {
            Assert.Equal(Map.Nodes[e.From].Elevation, e.E[0], 6);
            Assert.Equal(Map.Nodes[e.To].Elevation, e.E[^1], 6);
            Assert.Equal(Map.Nodes[e.From].X, e.X[0], 6);
            Assert.Equal(Map.Nodes[e.To].Z, e.Z[^1], 6);
            for (int i = 1; i < e.E.Length; i++)
            {
                double ds = e.D[i] - e.D[i - 1];
                Assert.InRange(ds, 3, 7); // evenly sampled
                Assert.True(Math.Abs(e.E[i] - e.E[i - 1]) / ds <= WorldMap.MaxGrade + 0.002, $"edge {e.Id} too steep at {e.D[i]:0} m");
            }
        }
    }

    [Fact]
    public void BridgesClearTheRiverAndLakesStayAwayFromRoads()
    {
        Assert.NotNull(Map.River);
        Assert.NotEmpty(Map.Bridges);
        foreach (var b in Map.Bridges)
        {
            var e = Map.Edges[b.Edge];
            int i = Array.FindIndex(e.D, d => d >= b.DistanceM);
            Assert.True(e.E[i] - b.WaterLevel >= 3.5, $"bridge on edge {e.Id}: {e.E[i] - b.WaterLevel:0.0} m above the water");
        }

        var river = Map.River!;
        for (int i = 1; i < river.Level.Length; i++) Assert.True(river.Level[i] <= river.Level[i - 1] + 1e-9, "the river never flows uphill");

        Assert.NotEmpty(Map.Lakes);
        foreach (var lake in Map.Lakes) Assert.True(Map.DistanceToRoads(lake.X, lake.Z) > lake.Radius * 1.25, "no road through a lake");
    }

    [Fact]
    public void MainLoopIsTheGroupRouteAndCloses()
    {
        var loop = Map.MainLoop;
        Assert.True(loop.IsLoop);
        Assert.InRange(loop.LengthM, 9_900, 10_100);
        Assert.Equal(loop.Points[0].X, loop.Points[^1].X, 3);
        Assert.Equal(loop.Points[0].ElevationM, loop.Points[^1].ElevationM, 3);
        Assert.Same(loop, BuiltInRoutes.Get(BuiltInRoutes.DefaultId));
    }

    [Fact]
    public void WithoutChoicesThePathFollowsTheMainLoop()
    {
        var path = NetworkPath.OnMainLoop(Map, 0, 0);
        var loop = Map.MainLoop;
        for (double d = 0; d < 25_000; d += 50)
        {
            path.Update(d);
            var a = path.Route.Sample(d);
            var b = loop.Sample(d);
            Assert.True(Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Z - b.Z) < 1, $"left the loop at {d} m");
        }
    }

    [Fact]
    public void TurningAtAJunctionTakesTheLinkRoad()
    {
        var path = NetworkPath.OnMainLoop(Map, 0, 0);
        var next = path.Next();
        Assert.Equal(2, next.Options.Count); // T junction: two ways on, never back
        Assert.True(next.Options[0].TurnDegrees < next.Options[1].TurnDegrees, "leftmost first");
        Assert.True(next.Options[next.Chosen].IsMainLoop, "default stays on the main loop");

        int link = next.Options.ToList().FindIndex(o => !o.IsMainLoop);
        path.Choose(link);
        double junction = next.DistanceM;
        path.Update(junction - 10);
        var onLink = Map.Edges[next.Options[link].Edge.Edge];
        var p = path.Route.Sample(junction + 200);
        double nearest = Enumerable.Range(0, onLink.X.Length).Min(i => Math.Sqrt(Math.Pow(onLink.X[i] - p.X, 2) + Math.Pow(onLink.Z[i] - p.Z, 2)));
        Assert.True(nearest < 3, "200 m after the junction the rider is on the link road");
    }

    [Fact]
    public void PathRouteIsContinuousWhileRidingForLong()
    {
        var path = NetworkPath.OnMainLoop(Map, 1234, 500);
        var random = new Random(3);
        var last = path.Route.Sample(500);
        for (double d = 500; d < 60_000; d += 10)
        {
            if (path.Update(d) || random.NextDouble() < 0.002) path.Choose(random.Next(2));
            var s = path.Route.Sample(d);
            double step = Math.Sqrt(Math.Pow(s.X - last.X, 2) + Math.Pow(s.Z - last.Z, 2));
            Assert.True(step < 12, $"jump of {step:0.0} m at {d} m");
            Assert.True(Math.Abs(s.Y - last.Y) < 1.5, $"height jump at {d} m");
            last = s;
        }

        Assert.True(path.Route.Points.Count < 4000, "old edges are dropped");
    }
}
