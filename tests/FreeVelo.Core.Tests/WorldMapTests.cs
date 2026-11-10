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
        Assert.True(Map.Nodes.Count >= 8, "villages and summits");
        Assert.True(Map.Edges.Count(e => !e.IsMainLoop) >= 2, "roads besides the valley round");
        // The villages where roads meet are junctions; every village has a name.
        foreach (string name in new[] { "Lärchwald", "Kirchbach", "Mitterbach", "Trafon" })
        {
            var node = Assert.Single(Map.Nodes, n => n.Name == name);
            Assert.True(node.Edges.Count >= 3, $"{name} is a junction");
        }

        Assert.All(Map.Nodes, n => Assert.False(string.IsNullOrEmpty(n.Name)));
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
                // The famous climbs are copied as they are (up to ~15 % locally); everything else stays within 9 %.
                double max = e.Kind == RoadKind.Climb ? 0.25 : WorldMap.MaxGrade + 0.002;
                Assert.True(Math.Abs(e.E[i] - e.E[i - 1]) / ds <= max, $"edge {e.Id} too steep at {e.D[i]:0} m");
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
        Assert.Contains(Map.Lakes, l => l.Radius > 700); // the big lake the road goes round
        foreach (var lake in Map.Lakes)
        {
            for (int a = 0; a < 48; a++)
            {
                double angle = a * Math.PI * 2 / 48, r = lake.RadiusAt(angle) * 0.98;
                Assert.True(Map.DistanceToRoads(lake.X + (Math.Cos(angle) * r), lake.Z + (Math.Sin(angle) * r)) > 60, "no road through a lake");
            }
        }
    }

    [Fact]
    public void MainLoopIsTheGroupRouteAndCloses()
    {
        var loop = Map.MainLoop;
        Assert.True(loop.IsLoop);
        Assert.InRange(loop.LengthM, 24_000, 31_000);
        Assert.InRange(loop.TotalAscentM, 20, 120); // the valley round is (nearly) flat
        Assert.Equal(loop.Points[0].X, loop.Points[^1].X, 3);
        Assert.Equal(loop.Points[0].ElevationM, loop.Points[^1].ElevationM, 3);
        Assert.Same(loop, BuiltInRoutes.Get(BuiltInRoutes.DefaultId));
    }

    [Fact]
    public void ValleyRoadsAreFlatAndMountainRoadsEven()
    {
        foreach (var e in Map.Edges.Where(e => e.Kind == RoadKind.Valley))
        {
            for (int i = 10; i < e.X.Length; i++)
            {
                double grade = Math.Abs(e.E[i] - e.E[i - 10]) / (e.D[i] - e.D[i - 10]);
                Assert.True(grade <= (e.IsMainLoop ? 0.031 : 0.045), $"valley road {e.Id} at {e.D[i]:0} m: {grade:P1}");
            }
        }

        // Mountain roads: the same gradient all the way (apart from easing in and out at the ends).
        foreach (var e in Map.Edges.Where(e => e.Kind == RoadKind.Pass))
        {
            double mean = (e.E[^1] - e.E[0]) / e.Length;
            for (int i = 0; i < e.X.Length - 10; i++)
            {
                if (e.D[i] < 130 || e.D[i + 10] > e.Length - 130) continue;
                double grade = (e.E[i + 10] - e.E[i]) / (e.D[i + 10] - e.D[i]);
                Assert.True(Math.Abs(grade - (mean * e.Length / (e.Length - 120))) < 0.002, $"mountain road {e.Id} at {e.D[i]:0} m: {grade:P2}");
            }
        }

        var alm = Map.AlmLoop();
        Assert.True(alm.IsLoop);
        Assert.InRange(alm.TotalAscentM, 350, 550);
    }

    [Fact]
    public void WithoutChoicesThePathFollowsTheMainLoop()
    {
        var path = NetworkPath.OnMainLoop(Map, 0, 0);
        var loop = Map.MainLoop;
        for (double d = 200; d < 25_000; d += 50) // (at the very start the path doesn't know the road before the village yet)
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
            Assert.True(Math.Abs(s.Y - last.Y) < 2.5, $"height jump at {d} m"); // climbs reach ~20 % over 10 m
            last = s;
        }

        Assert.True(path.Route.Points.Count < 4000 + Map.Edges.Max(e => e.X.Length), "old edges are dropped"); // a climb alone has ~4900 points
    }

    [Fact]
    public void PreviewShowsTheRoadBeyondTheNextJunction()
    {
        var path = NetworkPath.OnMainLoop(Map, 0, 0);
        var junction = path.Next();
        var preview = path.Preview(junction.DistanceM + 3000);

        Assert.True(preview.LengthM >= junction.DistanceM + 3000, "the preview reaches 3 km past the junction");
        // Same distances as the path, and by default it stays on the main loop.
        for (double d = 0; d < path.Route.LengthM; d += 100)
        {
            Assert.Equal(path.Route.Sample(d).Y, preview.Sample(d).Y, 3);
        }

        for (double d = junction.DistanceM + 100; d < junction.DistanceM + 3000; d += 100)
        {
            var a = preview.Sample(d);
            var b = Map.MainLoop.Sample(d);
            Assert.True(Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Z - b.Z) < 1, $"preview left the main loop at {d} m");
        }

        // Choosing the other way changes the preview.
        path.Choose(1 - junction.Chosen);
        var other = path.Preview(junction.DistanceM + 3000);
        var p = other.Sample(junction.DistanceM + 500);
        var q = preview.Sample(junction.DistanceM + 500);
        Assert.True(Math.Abs(p.X - q.X) + Math.Abs(p.Z - q.Z) > 5, "the preview follows the choice");
    }
}
