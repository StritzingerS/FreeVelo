using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FreeVelo.Core.World;
using FreeVelo.Core.World.Gen;

namespace FreeVelo.Core.Tests;

/// <summary>
/// The world contract (docs/GRAPHICS_OVERHAUL.md, section 7): road data and routes are what the game logic and the
/// server rely on. Graphics work must never change them. When a change to the roads is intended, update the
/// fingerprints below in the same commit (the failure message prints the new values).
/// </summary>
public class WorldContractTests
{
    private const string RoadsFingerprint = "E0EFDA59FA13B25E";

    private static readonly Dictionary<string, string> RouteFingerprints = new()
    {
        [BuiltInRoutes.DefaultId] = "EADCF4CAA85D6C66",
        [BuiltInRoutes.AlmId] = "CA0C3A13FCDFD546",
        [BuiltInRoutes.StelvioId] = "1815978D79E6838C",
        [BuiltInRoutes.AlpeId] = "BB60C03B17FF2386",
    };

    [Fact]
    public void RoadNetworkIsUnchanged()
    {
        var map = WorldMap.Default;
        var text = new StringBuilder();
        foreach (var n in map.Nodes) text.Append(n.Id).Append(n.Name).Append(F(n.X)).Append(F(n.Z)).Append(F(n.Elevation)).Append('|');
        foreach (var e in map.Edges)
        {
            text.Append(e.Id).Append(':').Append(e.From).Append('>').Append(e.To).Append(F(e.Width)).Append(e.Surface);
            for (int i = 0; i < e.X.Length; i++) text.Append(F(e.X[i])).Append(F(e.Z[i])).Append(F(e.E[i]));
            text.Append('|');
        }

        Assert.True(Hash(text) == RoadsFingerprint, $"Road network changed: fingerprint {Hash(text)}");
        // The version app and server compare is built from the same data.
        Assert.StartsWith(RoadsFingerprint, WorldContract.RoadHash(map));
    }

    /// <summary>
    /// The world version app and server compare (WorldContract). When it changes, apps and server must be updated
    /// together: update the value here in the same commit, and raise WorldContract.RideLogicVersion for ride logic
    /// changes that leave the roads alone.
    /// </summary>
    [Fact]
    public void WorldVersionIsKnown()
    {
        Assert.True(WorldContract.Version == "2-" + RoadsFingerprint[..12], $"World version is now {WorldContract.Version}");
    }

    [Theory]
    [InlineData(BuiltInRoutes.DefaultId)]
    [InlineData(BuiltInRoutes.AlmId)]
    [InlineData(BuiltInRoutes.StelvioId)]
    [InlineData(BuiltInRoutes.AlpeId)]
    public void BuiltInRouteIsUnchanged(string id)
    {
        var route = BuiltInRoutes.Get(id);
        var text = new StringBuilder();
        foreach (var p in route.Points) text.Append(F(p.DistanceM)).Append(F(p.X)).Append(F(p.Z)).Append(F(p.ElevationM));
        foreach (var c in route.Climbs) text.Append('|').Append(c.Name).Append(F(c.StartM)).Append(F(c.EndM)).Append(F(c.GainM));
        string actual = Hash(text);
        Assert.True(actual == RouteFingerprints[id], $"Route {id} changed: fingerprint {actual}, length {route.LengthM:F1} m, {route.Climbs.Count} climbs");
    }

    // Millimetres: rules out floating-point noise but catches any real change.
    private static string F(double v) => ";" + Math.Round(v, 3).ToString("F3", CultureInfo.InvariantCulture);

    private static string Hash(StringBuilder text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
}
