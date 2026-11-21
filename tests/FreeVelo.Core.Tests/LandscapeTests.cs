using FreeVelo.Core.World.Gen;

namespace FreeVelo.Core.Tests;

public class LandscapeTests
{
    private static readonly Lazy<Terrain> Ground = new(() => Terrain.Build(WorldMap.Default));

    /// <summary>
    /// The invariant of docs/GRAPHICS_OVERHAUL.md section 4: nothing floats. Every placed item is checked against the
    /// ground exactly as the app draws it (Terrain.SurfaceHeight): at the corners of its coded model's footprint (trees,
    /// poles and signs at their foot; fences also in the middle, sheared by their slope), its lowest point may not be
    /// more than 5 cm above the ground. Also the Tripo house (10.5 x 8.3 m, 0.25 m under the floor) on its pad.
    /// Before this rule 2,385 fence pieces and 65 other items floated, up to 2.5 m.
    /// </summary>
    [Fact]
    public void NothingFloatsAboveTheDrawnGround()
    {
        var terrain = Terrain.Build(WorldMap.Default); // own copy: the pads change the ground
        var l = Landscape.Generate(terrain, new DateTime(2026, 5, 10));
        var floating = new List<string>();
        foreach (var it in l.Items)
        {
            var verts = SceneryModels.Get(it.Kind, it.Variant).Lod0.Values.SelectMany(m => m.Vertices).ToList();
            if (verts.Count == 0) continue;
            float minY = verts.Min(v => v.Y) * it.Scale;
            float x0 = verts.Min(v => v.X), x1 = verts.Max(v => v.X), z0 = verts.Min(v => v.Z), z1 = verts.Max(v => v.Z);
            if (it.Kind is SceneryKind.Apple or SceneryKind.Linden or SceneryKind.Maple or SceneryKind.Beech or SceneryKind.Alder
                or SceneryKind.Willow or SceneryKind.Maypole or SceneryKind.VillageSign or SceneryKind.DirectionSign)
            {
                (x0, x1, z0, z1) = (-0.5f, 0.5f, -0.5f, 0.5f);
            }

            double r = it.RotationDeg * Math.PI / 180, sin = Math.Sin(r), cos = Math.Cos(r);
            var anchors = new List<(float U, float V)> { (x0, z0), (x1, z0), (x0, z1), (x1, z1) };
            if (it.Slope != 0) anchors.Add((0, 0));
            foreach (var (u, v) in anchors)
            {
                double gap = (it.Y + minY + (it.Slope * u)) - terrain.SurfaceHeight(it.X + (u * cos) + (v * sin), it.Z - (u * sin) + (v * cos));
                if (gap > 0.05)
                {
                    floating.Add($"{it.Kind} at ({it.X:0}, {it.Z:0}): {gap:0.00} m");
                    break;
                }
            }

            if (it.Kind == SceneryKind.AlpineHouse && it.Variant % 3 == 0)
            {
                foreach (var (u, v) in new[] { (-5.25, -4.15), (5.25, -4.15), (-5.25, 4.15), (5.25, 4.15) })
                {
                    double gap = (it.Y - 0.25) - terrain.SurfaceHeight(it.X + (u * cos) + (v * sin), it.Z - (u * sin) + (v * cos));
                    if (gap > 0.05)
                    {
                        floating.Add($"Tripo house at ({it.X:0}, {it.Z:0}): {gap:0.00} m under a wall");
                        break;
                    }
                }
            }
        }

        Assert.True(floating.Count == 0, $"{floating.Count} items float, e.g. {string.Join("; ", floating.Take(5))}");
    }

    [Fact]
    public void VillagesHaveOneChurchInnsShopsAndSigns()
    {
        // Own copy: Generate levels the building plots into the terrain, and on a terrain another test already levelled
        // the hillside village Valbrune found no room at all.
        var terrain = Terrain.Build(WorldMap.Default);
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
        Assert.Equal(l.Items.Count(i => i.Kind == SceneryKind.Farmhouse), l.Items.Count(i => i.Kind == SceneryKind.Barn)); // each a Paarhof
        Assert.NotEmpty(l.Fields);
        Assert.NotEmpty(l.Pastures);
        Assert.Contains(l.Items, i => i.Kind == SceneryKind.BeerTable); // the inns' Gastgarten
        Assert.True(l.Items.Count(i => i.Kind == SceneryKind.FireStation) >= 4, "a fire station in the church villages");
        Assert.True(l.Items.Count(i => i.Kind == SceneryKind.Workshop) >= 2, "car workshops");
        Assert.True(l.Items.Count(i => i.Kind == SceneryKind.Sawmill) >= 2, "sawmills");
    }

    [Fact]
    public void NoSignpostsAndTheAlmHasFencedHuts()
    {
        var terrain = Ground.Value;
        var map = terrain.Map;
        var l = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        Assert.DoesNotContain(l.Items, i => i.Kind == SceneryKind.DirectionSign); // taken out at the owner's request

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
    public void YardsAndPathsAreGroundSurfaces()
    {
        // No more plate models (they floated or sank on slopes): gravel yards and stone paths are surfaces the app
        // drapes over the ground. Every house plot's path leads to its street; no surface lies on a road or in water.
        var terrain = Terrain.Build(WorldMap.Default);
        var l = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        Assert.DoesNotContain(l.Items, i => i.Kind is SceneryKind.Yard or SceneryKind.PathStone);
        Assert.True(l.Surfaces.Count(s => s.Crop == WorldMaterial.Stone) > 100, "stone paths to the house doors");
        Assert.True(l.Surfaces.Count(s => s.Crop == WorldMaterial.Gravel) > 20, "gravel yards and forecourts");
        foreach (var s in l.Surfaces)
        {
            Assert.False(terrain.IsWater(s.Cx, s.Cz, 0), $"{s.Crop} at ({s.Cx:0},{s.Cz:0}) in the water");
            Assert.True(terrain.DistanceToRoad(s.Cx, s.Cz, 5, out var road, out _, out _) > ((road?.Width ?? 0) / 2), $"{s.Crop} at ({s.Cx:0},{s.Cz:0}) on a road");
        }
    }

    [Fact]
    public void BuildingsStandOnLevelGround()
    {
        var terrain = Terrain.Build(WorldMap.Default); // own copy: the pads change the ground
        var l = Landscape.Generate(terrain, new DateTime(2026, 7, 1));
        int checkedBuildings = 0;
        foreach (var i in l.Items.Where(i => i.Kind is SceneryKind.AlpineHouse or SceneryKind.Inn or SceneryKind.Shop or SceneryKind.Farmhouse or SceneryKind.Barn))
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
        var gravel = l.Surfaces.Where(s => s.Crop == WorldMaterial.Gravel).ToList();
        var tables = l.Items.Where(i => i.Kind == SceneryKind.BeerTable).ToList();
        Assert.NotEmpty(tables);
        foreach (var t in tables)
        {
            // Every table stands on the gravel garden floor and on level ground.
            Assert.Contains(gravel, s => s.Contains(t.X, t.Z));
            Assert.True(Math.Abs(terrain.Height(t.X, t.Z) - terrain.Height(t.X + 1, t.Z + 1)) < 0.1, $"table at ({t.X:0},{t.Z:0}) on a slope");
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
        foreach (var i in l.Items.Where(i => i.Kind is SceneryKind.AlpineHouse or SceneryKind.Inn or SceneryKind.Shop or SceneryKind.Church or SceneryKind.Farmhouse or SceneryKind.Barn
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
