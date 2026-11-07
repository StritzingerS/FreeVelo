using FreeVelo.Core.World;
using FreeVelo.Core.World.Gen;

namespace FreeVelo.Core.Tests;

public class MountainTests
{
    private static readonly WorldMap Map = WorldMap.Default;

    [Theory]
    [InlineData(ClimbData.Stelvio, 24.3, 1842)]
    [InlineData(ClimbData.AlpeDHuez, 13.8, 1118)]
    public void ClimbsAreExactCopiesOfTheGpx(string id, double km, double gain)
    {
        var climb = Map.Mountain!.Find(id)!;
        var data = climb.Data;
        var edge = Map.Edges[climb.Edge];
        Assert.Equal(RoadKind.Climb, edge.Kind);
        Assert.Equal(km, edge.Length / 1000, 1);
        Assert.InRange(edge.E[^1] - edge.E[0], gain - 1, gain + 1);
        Assert.Equal(data.Length, edge.Length, 3);

        // Every point keeps its distance to every other (only turned and moved): all hairpins as they are.
        for (int i = 0; i < data.X.Length; i += 97)
        {
            for (int j = i + 13; j < data.X.Length; j += 211)
            {
                double original = Math.Sqrt(Math.Pow(data.X[i] - data.X[j], 2) + Math.Pow(data.Z[i] - data.Z[j], 2));
                double placed = Math.Sqrt(Math.Pow(edge.X[i] - edge.X[j], 2) + Math.Pow(edge.Z[i] - edge.Z[j], 2));
                Assert.Equal(original, placed, 6);
            }

            // Same gradient everywhere
            Assert.Equal(data.E[i] - data.E[0], edge.E[i] - edge.E[0], 6);
        }
    }

    [Fact]
    public void SharedSummitShortConnectorGentleAccessRoads()
    {
        var m = Map.Mountain!;
        Assert.Equal(m.Stelvio.SummitElevation, m.Alpe.SummitElevation, 6);
        var connector = Map.Edges[m.ConnectorEdge];
        Assert.InRange(connector.Length, 500, 2200);
        Assert.Equal(connector.E[0], connector.E[^1], 6);

        foreach (int id in new[] { m.AccessStelvioEdge, m.AccessAlpeEdge })
        {
            var e = Map.Edges[id];
            Assert.Equal(RoadKind.Access, e.Kind);
            for (int i = 20; i < e.X.Length; i++) Assert.True(Math.Abs(e.E[i] - e.E[i - 20]) / (e.D[i] - e.D[i - 20]) <= WorldMap.MaxGrade + 1e-9);
            Assert.True(Map.Nodes[e.From].Edges.Count >= 3, "starts at a junction on the main loop");
        }

        // The climbs' feet are where the access roads end
        Assert.Equal(Map.Edges[m.AccessStelvioEdge].To, Map.Edges[m.Stelvio.Edge].From);
        Assert.Equal(Map.Edges[m.AccessAlpeEdge].To, Map.Edges[m.Alpe.Edge].From);
    }

    [Fact]
    public void RidingOnTheMainLoopNeverTurnsOffToTheMountain()
    {
        var path = NetworkPath.OnMainLoop(Map, 0, 0);
        double length = Map.MainLoop.LengthM;
        for (double d = 0; d < 3 * length; d += 50)
        {
            path.Update(d);
            var next = path.Next();
            Assert.True(next.Options[next.Chosen].IsMainLoop);
        }

        // ... but the access roads are offered at their junctions
        var m = Map.Mountain!;
        var offered = new HashSet<int>();
        path = NetworkPath.OnMainLoop(Map, 0, 0);
        for (double d = 0; d < length + 100; d += 20)
        {
            path.Update(d);
            foreach (var o in path.Next().Options) offered.Add(o.Edge.Edge);
        }

        Assert.Contains(m.AccessStelvioEdge, offered);
        Assert.Contains(m.AccessAlpeEdge, offered);
    }

    [Theory]
    [InlineData(BuiltInRoutes.StelvioId, ClimbData.Stelvio)]
    [InlineData(BuiltInRoutes.AlpeId, ClimbData.AlpeDHuez)]
    public void ClimbLoopsStartAtTheFootAndCloseTheLoop(string routeId, string climbId)
    {
        var route = BuiltInRoutes.Get(routeId);
        Assert.True(route.IsLoop);
        var first = route.Points[0];
        var last = route.Points[^1];
        Assert.True(Math.Abs(first.X - last.X) < 0.01 && Math.Abs(first.Z - last.Z) < 0.01 && Math.Abs(first.ElevationM - last.ElevationM) < 0.5);

        var climb = Assert.Single(route.Climbs);
        Assert.Equal(climbId, climb.Id);
        Assert.Equal(0, climb.StartM, 6);
        Assert.Equal(Map.Mountain!.Find(climbId)!.Data.Length, climb.LengthM, 3);
        Assert.True(route.LengthM > 40_000);
    }

    [Fact]
    public void TheStoredPlacementIsTheSearchResult()
    {
        // The default world tries a stored placement first; the full search must come to the same result.
        var searched = WorldMap.Generate(WorldMap.DefaultSeed, searchMountain: true).Mountain!;
        var stored = Map.Mountain!;
        Assert.Equal(stored.Stelvio.SummitX, searched.Stelvio.SummitX, 6);
        Assert.Equal(stored.Alpe.Angle, searched.Alpe.Angle, 9);
        Assert.Equal(stored.Stelvio.FootElevation, searched.Stelvio.FootElevation, 6);
    }

    private static readonly Lazy<Terrain> Ground = new(() => Terrain.Build(Map));

    [Fact]
    public void MountainGroundCarriesTheRoadsAndJoinsTheHomeGroundWithoutASeam()
    {
        var terrain = Ground.Value;
        Assert.NotNull(terrain.Mountain);

        // Road beds 0.3 m under the road surface along all mountain roads
        foreach (var e in Map.Edges.Where(e => e.Kind != RoadKind.Home))
        {
            for (int i = 0; i < e.X.Length; i += 37) Assert.Equal(e.E[i] - 0.3, terrain.Height(e.X[i], e.Z[i]), 1);
        }

        // At the edge of the home grid the computed mountain ground continues the grid
        for (double x = terrain.MinX + 50; x < terrain.MaxX - 50; x += 173)
        {
            double inside = terrain.Height(x, terrain.MaxZ - 0.01), outside = terrain.Height(x, terrain.MaxZ + 0.01);
            Assert.True(Math.Abs(inside - outside) < 0.6, $"seam at x {x:0}: {inside:0.0} / {outside:0.0}");
        }

        // The mountain rises towards the summit
        var summit = Map.Mountain!.Stelvio;
        var top = summit.Point(summit.Data.X.Length - 1);
        Assert.True(terrain.Mountain!.Coarse(top.X + 300, top.Z + 300) > terrain.Mountain.LowRoad + 800);
    }

    [Fact]
    public void ForestBelowTheTreelineMeadowsAndRockAboveNothingOnTheRoads()
    {
        var terrain = Ground.Value;
        var m = terrain.Mountain!;
        var climb = Map.Mountain!.Stelvio;
        int trees = 0, high = 0;
        foreach (int index in new[] { 0, 1500, 3000, 4000, climb.Data.X.Length - 1 })
        {
            var (px, pz) = climb.Point(index);
            int cx = (int)Math.Floor(px / MountainLook.ChunkSize), cz = (int)Math.Floor(pz / MountainLook.ChunkSize);
            foreach (var item in MountainLook.Items(terrain, cx, cz, 8))
            {
                Assert.True(terrain.DistanceToRoad(item.X, item.Z, 9, out _, out _, out _) >= 9, "nothing on the road");
                if (item.Kind is SceneryKind.Spruce or SceneryKind.Pine or SceneryKind.Oak or SceneryKind.Birch)
                {
                    trees++;
                    Assert.True(item.Y < MountainLook.Treeline(m) + 1, $"tree at {item.Y:0} m above the treeline {MountainLook.Treeline(m):0}");
                }
                else if (item.Y > MountainLook.Treeline(m)) high++;
            }
        }

        Assert.True(trees > 100, "forest in the valleys");
        Assert.True(high > 10, "rocks above the treeline");

        // At the pass: mostly meadow and rock in summer, only some snow
        var top = climb.Point(climb.Data.X.Length - 1);
        int snow = 0, total = 0;
        for (double x = top.X - 250; x <= top.X + 250; x += 25)
        {
            for (double z = top.Z - 250; z <= top.Z + 250; z += 25)
            {
                var kind = MountainLook.Ground(m, x, z, terrain.Height(x, z), terrain.Slope(x, z), 8, false);
                if (kind == GroundKind.Snow) snow++;
                total++;
            }
        }

        Assert.InRange(snow / (double)total, 0, 0.35);
    }

    /// <summary>
    /// Share of mountain road points (across the road) where a terrain mesh with <paramref name="cell"/> m cells rises
    /// more than 0.5 m above the road, with the corners kept below the roads (RoadCap).
    /// </summary>
    private static double CoveredShare(Terrain terrain, double cell, bool distant)
    {
        var map = terrain.Map;
        var m = terrain.Mountain!;
        double Corner(double x, double z) => Math.Min(distant ? m.Coarse(x, z) - 1.5 : terrain.Height(x, z), terrain.RoadCap(x, z, cell));
        int covered = 0, total = 0;
        foreach (var e in map.Edges.Where(e => e.Kind != RoadKind.Home))
        {
            for (int i = 2; i < e.X.Length - 2; i += 6)
            {
                double hx = e.X[i + 1] - e.X[i - 1], hz = e.Z[i + 1] - e.Z[i - 1], l = Math.Sqrt((hx * hx) + (hz * hz));
                foreach (double o in new[] { (-e.Width / 2) + 0.3, 0, (e.Width / 2) - 0.3 })
                {
                    double x = e.X[i] + (hz / l * o), z = e.Z[i] - (hx / l * o);
                    if (terrain.InGrid(x, z)) continue;
                    double gx = Math.Floor(x / cell) * cell, gz = Math.Floor(z / cell) * cell, fx = (x - gx) / cell, fz = (z - gz) / cell;
                    double h = (((Corner(gx, gz) * (1 - fx)) + (Corner(gx + cell, gz) * fx)) * (1 - fz)) + (((Corner(gx, gz + cell) * (1 - fx)) + (Corner(gx + cell, gz + cell) * fx)) * fz);
                    total++;
                    if (h - e.E[i] > 0.5) covered++;
                }
            }
        }

        return (double)covered / total;
    }

    [Fact]
    public void TerrainMeshesStayBelowTheRoads()
    {
        // Without the cap the distant 32 m tiles covered ~21 % of the road points, the 16 m version ~5 %.
        var terrain = Ground.Value;
        Assert.True(CoveredShare(terrain, 4, false) < 0.002, "4 m near detail");
        Assert.True(CoveredShare(terrain, 8, false) < 0.006, "8 m lower detail");
        Assert.True(CoveredShare(terrain, 32, true) < 0.01, "32 m distant tiles");
    }
}
