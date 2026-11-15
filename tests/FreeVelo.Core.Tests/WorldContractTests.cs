using FreeVelo.Core.World;
using FreeVelo.Core.World.Gen;

namespace FreeVelo.Core.Tests;

/// <summary>
/// The world contract: the road data the rides, the server and group rides depend on. The graphics may change
/// anything about how the world looks, but not these values. If one of these tests fails after an intended change
/// of the roads, update the pinned value here, bump <see cref="Terrain.FormatVersion"/> and make sure app and server
/// ship together (the server builds the routes itself).
/// </summary>
public class WorldContractTests
{
    private static readonly WorldMap Map = WorldMap.Default;

    [Fact]
    public void RoadNetworkIsPinned()
    {
        // Every road point to a decimetre: centre lines, elevations (gradients) and the graph.
        ulong h = 1469598103934665603UL;
        void Add(long v)
        {
            for (int i = 0; i < 8; i++)
            {
                h ^= (byte)(v >> (8 * i));
                h *= 1099511628211UL;
            }
        }

        foreach (var e in Map.Edges)
        {
            Add(e.From);
            Add(e.To);
            Add((long)e.Kind);
            for (int i = 0; i < e.X.Length; i++)
            {
                Add((long)Math.Round(e.X[i] * 10));
                Add((long)Math.Round(e.Z[i] * 10));
                Add((long)Math.Round(e.E[i] * 10));
            }
        }

        Assert.Equal(13, Map.Edges.Count);
        Assert.Equal(18492, Map.Edges.Sum(e => e.X.Length));
        Assert.Equal(0xB783415939941C1BUL, h);
        Assert.Equal(
            "Lärchwald,Kirchbach,Mitterbach,Hinterau,Trafon,Oberalm,Oberalm,Valbrune,Passo dello Stelvio (from Prato),Alpe d'Huez",
            string.Join(",", Map.Nodes.Select(n => n.Name)));
    }

    [Theory]
    [InlineData(BuiltInRoutes.DefaultId, 27590.3, 0.0, "")]
    [InlineData(BuiltInRoutes.AlmId, 19591.2, 427.0, "")]
    [InlineData(BuiltInRoutes.StelvioId, 60408.1, 1859.3, "stelvio@0-24297+1842")]
    [InlineData(BuiltInRoutes.AlpeId, 60408.1, 1859.3, "alpe-dhuez@0-13772+1118")]
    public void BuiltInRoutesArePinned(string id, double lengthM, double ascentM, string climbs)
    {
        var route = BuiltInRoutes.Get(id);
        Assert.Equal(lengthM, route.LengthM, 0.5);
        Assert.Equal(ascentM, route.TotalAscentM, 0.5);
        Assert.Equal(climbs, string.Join(";", route.Climbs.Select(c => $"{c.Id}@{c.StartM:0}-{c.EndM:0}+{c.GainM:0}")));
    }

    [Fact]
    public void TerrainFingerprintBelongsToItsFormatVersion()
    {
        // A changed fingerprint means saved worlds no longer fit: bump FormatVersion with it.
        Assert.Equal(3, Terrain.FormatVersion);
        Assert.Equal(0x35E5941E8BE24C92UL, Terrain.Fingerprint(Map));
    }

    [Fact]
    public void JunctionChoicesArePinned()
    {
        // For every way into every junction: the ways on, leftmost first (what the junction card offers).
        var expected = new[]
        {
            "Lärchwald from e0: e5- e4-", "Lärchwald from e4: e0+ e5-", "Lärchwald from e5: e4- e0+",
            "Kirchbach from e0: e6+ e1+", "Kirchbach from e1: e0- e6+", "Kirchbach from e6: e1+ e0-",
            "Mitterbach from e1: e8- e2+ e5+", "Mitterbach from e2: e5+ e1- e8-", "Mitterbach from e5: e1- e8- e2+", "Mitterbach from e8: e2+ e5+ e1-",
            "Trafon from e3: e10+ e4+", "Trafon from e4: e3- e10+", "Trafon from e10: e4+ e3-",
            "Oberalm from e6: e9+ e7+", "Oberalm from e7: e6- e9+", "Oberalm from e9: e7+ e6-",
        };
        var path = NetworkPath.OnMainLoop(Map, 0, 0);
        var actual = new List<string>();
        foreach (var n in Map.Nodes.Where(n => n.Edges.Count >= 3))
        {
            foreach (int id in n.Edges)
            {
                var options = path.Options(new DirectedEdge(id, Map.Edges[id].To == n.Id));
                actual.Add($"{n.Name} from e{id}: " + string.Join(" ", options.Select(o => $"e{o.Edge.Edge}{(o.Edge.Forward ? "+" : "-")}")));
            }
        }

        Assert.Equal(expected, actual);
    }
}
