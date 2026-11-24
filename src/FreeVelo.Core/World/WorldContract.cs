using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FreeVelo.Core.World.Gen;

namespace FreeVelo.Core.World
{
    /// <summary>
    /// The version of what app and server must agree on in a group ride (docs/GRAPHICS_OVERHAUL.md, section 7): the
    /// road data (a hash of every node and edge, as in WorldContractTests) and the shared ride logic. The app sends it
    /// with every request; the server refuses to create or join a group ride for an app with a different version, so
    /// riders never ride together on different worlds (positions and braking would not match).
    /// </summary>
    public static class WorldContract
    {
        /// <summary>
        /// Raise by one whenever the ride logic both sides run changes without the roads changing: distances on a
        /// route, Route.CurvatureAt, Cornering, KeepTogetherGroup. 2 = junctions ridden on the corner's arc (24 Nov 2026).
        /// </summary>
        public const int RideLogicVersion = 2;

        private static string? _version;

        /// <summary>"2-1A2B3C4D5E6F": ride logic version and the first 12 hex digits of the road hash.</summary>
        public static string Version => _version ??= RideLogicVersion.ToString(CultureInfo.InvariantCulture) + "-" + RoadHash(WorldMap.Default).Substring(0, 12);

        /// <summary>SHA-256 over every node (id, name, position, elevation) and edge (ends, width, surface, points), to the millimetre.</summary>
        public static string RoadHash(WorldMap map)
        {
            var text = new StringBuilder();
            foreach (var n in map.Nodes) text.Append(n.Id).Append(n.Name).Append(F(n.X)).Append(F(n.Z)).Append(F(n.Elevation)).Append('|');
            foreach (var e in map.Edges)
            {
                text.Append(e.Id).Append(':').Append(e.From).Append('>').Append(e.To).Append(F(e.Width)).Append(e.Surface);
                for (int i = 0; i < e.X.Length; i++) text.Append(F(e.X[i])).Append(F(e.Z[i])).Append(F(e.E[i]));
                text.Append('|');
            }

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) hex.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }

        private static string F(double v) => ";" + Math.Round(v, 3).ToString("F3", CultureInfo.InvariantCulture);
    }
}
