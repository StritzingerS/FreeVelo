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
        foreach (var i in l.Items.Where(i => i.Kind is SceneryKind.AlpineHouse or SceneryKind.Inn or SceneryKind.Shop or SceneryKind.Church or SceneryKind.Farmhouse))
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
