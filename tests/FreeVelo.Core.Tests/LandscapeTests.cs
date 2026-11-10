using FreeVelo.Core.World.Gen;

namespace FreeVelo.Core.Tests;

public class LandscapeTests
{
    private static readonly Lazy<Terrain> Ground = new(() => Terrain.Build(WorldMap.Default));

    [Fact]
    public void VillagesHaveOneChurchInnsShopsAndSigns()
    {
        var terrain = Ground.Value;
        var l = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        Assert.Equal(7, l.Villages.Count);
        foreach (var v in l.Villages)
        {
            var inside = l.Items.Where(i => Math.Sqrt(Math.Pow(i.X - v.X, 2) + Math.Pow(i.Z - v.Z, 2)) < v.Radius + 40).ToList();
            Assert.True(inside.Count(i => i.Kind == SceneryKind.Church) <= 1, $"{v.Name}: at most one church");
            Assert.True(inside.Count(i => i.Kind is SceneryKind.AlpineHouse or SceneryKind.AlpineHut) >= 3, $"{v.Name}: houses");
            Assert.Contains(inside, i => i.Kind == SceneryKind.Inn);
            Assert.Contains(l.Signs, s => s.Name == v.Name);
        }

        Assert.True(l.Items.Count(i => i.Kind == SceneryKind.Church) >= 4);
        Assert.True(l.Items.Count(i => i.Kind == SceneryKind.Farmhouse) >= 8, "farms on the valley floors");
        Assert.NotEmpty(l.Fields);
        Assert.NotEmpty(l.Pastures);
        Assert.Contains(l.Items, i => i.Kind == SceneryKind.BeerTable); // the inns' Gastgarten
        Assert.True(l.Items.Count(i => i.Kind == SceneryKind.FireStation) >= 4, "a fire station in the church villages");
        Assert.True(l.Items.Count(i => i.Kind == SceneryKind.Workshop) >= 2, "car workshops");
        Assert.True(l.Items.Count(i => i.Kind == SceneryKind.Sawmill) >= 2, "sawmills");
    }

    [Fact]
    public void JunctionsHaveSignpostsAndTheAlmHasFencedHuts()
    {
        var terrain = Ground.Value;
        var map = terrain.Map;
        var l = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        Assert.True(l.Signposts.Count >= 8, $"{l.Signposts.Count} signposts");
        foreach (var (item, boards) in l.Signposts)
        {
            Assert.InRange(boards.Length, 1, 3);
            Assert.All(boards, b => Assert.False(string.IsNullOrWhiteSpace(b.Name)));
            Assert.All(boards, b => Assert.Contains(b.Arrow, new[] { "←", "↑", "→" }));
            Assert.True(terrain.DistanceToRoad(item.X, item.Z, 8, out var road, out _, out _) >= (road?.Width ?? 0) / 2, "signpost beside the road");
        }

        // Huts up on the Alm, away from the villages, each standing in a fenced pasture.
        var huts = l.Items.Where(i => i.Kind == SceneryKind.AlpineHut && i.Y > 950 && map.Nodes.All(n => Math.Abs(n.X - i.X) + Math.Abs(n.Z - i.Z) > 350)).ToList();
        int fenced = huts.Count(h => l.Pastures.Any(p => Math.Abs(p.Cx - h.X) < 1 && Math.Abs(p.Cz - h.Z) < 1));
        Assert.True(fenced >= 6, $"{fenced} alpine huts in fenced pastures");
    }

    [Fact]
    public void NoTreeOrObjectStandsOnARoad()
    {
        var terrain = Ground.Value;
        var layout = SceneryLayout.Generate(terrain, new DateTime(2026, 5, 10));
        foreach (var i in layout.Items.Concat(terrain.Landscape!.Items).Where(i => i.Kind is not (SceneryKind.Lane or SceneryKind.PathStone)))
        {
            double d = terrain.DistanceToRoad(i.X, i.Z, 10, out var road, out _, out _);
            Assert.True(road == null || d >= (road.Width / 2) + 0.3, $"{i.Kind} at ({i.X:0},{i.Z:0}) on the road");
        }
    }

    [Fact]
    public void JunctionCornersArePavedAboveTheGround()
    {
        var terrain = Ground.Value;
        Assert.True(terrain.Corners.Count >= 8, $"{terrain.Corners.Count} junction corners");
        foreach (var c in terrain.Corners)
        {
            for (double x = c.MinX; x <= c.MaxX; x += 1)
            {
                for (double z = c.MinZ; z <= c.MaxZ; z += 1)
                {
                    if (c.Distance(x, z) > 0) continue;
                    Assert.True(terrain.Height(x, z) <= c.CY - 0.2, $"ground above the corner at ({x:0},{z:0})");
                }
            }
        }
    }

    [Fact]
    public void YardsAndPathsDoNotFloat()
    {
        var terrain = Ground.Value;
        var l = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        foreach (var i in l.Items.Where(i => i.Kind is SceneryKind.Yard or SceneryKind.PathStone))
        {
            // The plate's base reaches 0.68 m below its seat: no ground under it may be lower than that.
            var (hx, hz) = i.Kind == SceneryKind.Yard ? (3.5, 3.0) : (0.75, 2.0);
            double r = i.RotationDeg * Math.PI / 180;
            foreach (var (u, v) in new[] { (-hx, -hz), (hx, -hz), (-hx, hz), (hx, hz) })
            {
                double g = terrain.DrawnHeight(i.X + (u * Math.Cos(r)) + (v * Math.Sin(r)), i.Z - (u * Math.Sin(r)) + (v * Math.Cos(r)));
                Assert.True(g >= i.Y - 0.01, $"{i.Kind} at ({i.X:0},{i.Z:0}) floats");
                Assert.True(g - i.Y <= 0.5, $"{i.Kind} at ({i.X:0},{i.Z:0}) buried");
            }
        }
    }

    [Fact]
    public void BuildingsStandOnLevelGround()
    {
        var terrain = Terrain.Build(WorldMap.Default); // own copy: the pads change the ground
        var l = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        int checkedBuildings = 0;
        foreach (var i in l.Items.Where(i => i.Kind is SceneryKind.AlpineHouse or SceneryKind.Inn or SceneryKind.Shop or SceneryKind.Farmhouse))
        {
            // Across the middle of the footprint the ground is within a few centimetres of the building's floor.
            foreach (var (x, z) in new[] { (0.0, 0.0), (3.0, 0.0), (-3.0, 0.0), (0.0, 3.0), (0.0, -3.0) })
            {
                double r = i.RotationDeg * Math.PI / 180;
                double g = terrain.Height(i.X + (x * Math.Cos(r)) + (z * Math.Sin(r)), i.Z - (x * Math.Sin(r)) + (z * Math.Cos(r)));
                Assert.True(Math.Abs(g - i.Y) < 0.15, $"{i.Kind} at ({i.X:0},{i.Z:0}): ground {g - i.Y:+0.00;-0.00} m from its floor");
            }

            checkedBuildings++;
        }

        Assert.True(checkedBuildings > 100);
    }

    [Fact]
    public void BeerTablesStandOnTheGardenFloor()
    {
        var terrain = Terrain.Build(WorldMap.Default);
        var l = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        var yards = l.Items.Where(i => i.Kind == SceneryKind.Yard).ToList();
        var tables = l.Items.Where(i => i.Kind == SceneryKind.BeerTable).ToList();
        Assert.NotEmpty(tables);
        foreach (var t in tables)
        {
            // Every table stands on a gravel plate (the garden floor) and on level ground.
            Assert.Contains(yards, y => Math.Abs(y.X - t.X) < 4.5 && Math.Abs(y.Z - t.Z) < 4.5);
            Assert.True(Math.Abs(terrain.Height(t.X, t.Z) - terrain.Height(t.X + 1, t.Z + 1)) < 0.1, $"table at ({t.X:0},{t.Z:0}) on a slope");
        }
    }

    [Fact]
    public void GardenThingsStandOnTheGroundTheAppDraws()
    {
        var terrain = Terrain.Build(WorldMap.Default);
        var l = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        foreach (var i in l.Items.Where(i => i.Kind is SceneryKind.BeerTable or SceneryKind.Bench or SceneryKind.FlowerBed or SceneryKind.GardenFence))
        {
            double gap = i.Y - terrain.DrawnHeight(i.X, i.Z);
            Assert.True(gap < 0.15, $"{i.Kind} at ({i.X:0},{i.Z:0}) floats {gap:0.00} m above the ground");
        }
    }

    [Fact]
    public void TheMaypoleOnlyStandsInMay()
    {
        var terrain = Ground.Value;
        Assert.DoesNotContain(Landscape.Generate(terrain, new DateTime(2026, 4, 30)).Items, i => i.Kind == SceneryKind.Maypole);
        Assert.Contains(Landscape.Generate(terrain, new DateTime(2026, 5, 1)).Items, i => i.Kind == SceneryKind.Maypole);
        Assert.DoesNotContain(Landscape.Generate(terrain, new DateTime(2026, 6, 1)).Items, i => i.Kind == SceneryKind.Maypole);
    }

    [Fact]
    public void NothingIsBuiltOnRoadsOrWaterAndNatureKeepsOff()
    {
        var terrain = Ground.Value;
        var l = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        foreach (var i in l.Items.Where(i => i.Kind is SceneryKind.AlpineHouse or SceneryKind.Inn or SceneryKind.Shop or SceneryKind.Church or SceneryKind.Farmhouse
            or SceneryKind.Workshop or SceneryKind.Sawmill or SceneryKind.FireStation))
        {
            Assert.True(terrain.DistanceToRoad(i.X, i.Z, 8, out _, out _, out _) >= 8, $"{i.Kind} at ({i.X:0},{i.Z:0}) on a road");
            // The whole outline – walls, balconies, the church's graveyard wall and tower – stays off every road.
            foreach (var (x, z) in Landscape.OutlinePoints(i.Kind, i.Variant, i.X, i.Z, i.RotationDeg, 1))
            {
                double d = terrain.DistanceToRoad(x, z, 10, out var road, out _, out _);
                Assert.True(road == null || d >= (road.Width / 2) + 1.4, $"{i.Kind} at ({i.X:0},{i.Z:0}) reaches the road at ({x:0},{z:0})");
            }
            Assert.False(terrain.IsWater(i.X, i.Z, 5), $"{i.Kind} in the water");
            Assert.True(l.Blocked(i.X, i.Z));
        }

        // Fences never stand on a road either.
        foreach (var f in l.Items.Where(i => i.Kind is SceneryKind.GardenFence or SceneryKind.Fence))
        {
            double d = terrain.DistanceToRoad(f.X, f.Z, 10, out var road, out _, out _);
            Assert.True(road == null || d >= (road.Width / 2) + 0.9, $"fence at ({f.X:0},{f.Z:0}) on a road");
        }

        // Trees and rocks never grow where something is built.
        terrain.Landscape = l;
        var church = l.Items.First(i => i.Kind == SceneryKind.Church);
        int cx = (int)Math.Floor(church.X / MountainLook.ChunkSize), cz = (int)Math.Floor(church.Z / MountainLook.ChunkSize);
        foreach (var item in MountainLook.Items(terrain, cx, cz, SceneryLayout.NatureSeed(terrain.Map)))
        {
            if (item.Kind is SceneryKind.Spruce or SceneryKind.Fir or SceneryKind.Larch or SceneryKind.Beech or SceneryKind.Rock)
            {
                Assert.False(l.Blocked(item.X, item.Z) && !l.Items.Contains(item), "nature on built land");
            }
        }
    }
}
