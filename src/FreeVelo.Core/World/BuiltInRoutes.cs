using System;
using System.Collections.Generic;

namespace FreeVelo.Core.World
{
    /// <summary>
    /// Routes known by id to the app and the server (the server needs the same route for the Keep Together anchor's
    /// grade). Procedural routes are deterministic, so only the id travels over the network.
    /// </summary>
    public static class BuiltInRoutes
    {
        /// <summary>The 10 km rolling loop of solo rides; used when a group ride names no route.</summary>
        public const string DefaultId = "loop-10k";

        private static readonly Dictionary<string, Func<Route>> Factories = new Dictionary<string, Func<Route>>
        {
            [DefaultId] = () => ProceduralRoutes.Generate(seed: 1, lengthM: 10_000, hilliness: 0.6),
        };

        private static readonly Dictionary<string, Route> Cache = new Dictionary<string, Route>();

        public static IEnumerable<string> Ids => Factories.Keys;

        /// <summary>The route with this id, or the default route for an empty or unknown id.</summary>
        public static Route Get(string? id)
        {
            string key = id != null && Factories.ContainsKey(id) ? id : DefaultId;
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out var route))
                {
                    route = Factories[key]();
                    Cache[key] = route;
                }

                return route;
            }
        }
    }
}
