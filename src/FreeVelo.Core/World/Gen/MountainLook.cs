using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>Ground cover of the mountain.</summary>
    public enum GroundKind
    {
        /// <summary>Valley meadows (the home world's grass).</summary>
        Grass,

        /// <summary>Forest floor – used for distant ground where forest grows (looks like forest from afar).</summary>
        Forest,

        /// <summary>Alpine meadow above the treeline.</summary>
        Meadow,

        Rock,
        Snow,

        /// <summary>Fields (as the home world's draped fields).</summary>
        Wheat,
        Crop,
        Plowed,
    }

    /// <summary>
    /// The high-mountain look, by height between the valleys and the summit, slope and distance to the roads: mixed
    /// forest in the valleys, spruce and pine higher up, thinning to the treeline about 550 m below the summit, alpine
    /// meadows and scree above, rock on steep slopes and snow on the peaks around the pass (summer: only patches at
    /// the pass itself).
    /// </summary>
    public static class MountainLook
    {
        public const double TreelineBelowSummit = 550;
        /// <summary>Snow lies on the peaks this much above the pass (plus a few patches just below it).</summary>
        public const double SnowAboveSummit = 40;
        public const double ChunkSize = 128;
        private const double ItemCell = 11;

        /// <summary>0 at the lowest mountain road, 1 at the summit (can exceed 1 on the peaks around).</summary>
        public static double Level(MountainTerrain m, double h) => (h - m.LowRoad) / Math.Max(1, m.Summit - m.LowRoad);

        public static double Treeline(MountainTerrain m) => m.Summit - TreelineBelowSummit;

        /// <summary>How much forest grows here (0–1), before height and slope.</summary>
        public static double ForestDensity(double x, double z, int seed) =>
            Math.Max(0, Math.Min(1, 0.78 + (0.6 * Noise.Fractal(x / 420, z / 420, seed + 31, 3))));

        /// <summary>Ground cover at a point. <paramref name="slope"/> = rise per metre.</summary>
        public static GroundKind Ground(MountainTerrain m, double x, double z, double h, double slope, int seed, bool distant, Landscape? landscape = null)
        {
            var field = landscape?.FieldAt(x, z);
            if (field != null) return field.Crop == WorldMaterial.Wheat ? GroundKind.Wheat : field.Crop == WorldMaterial.Plowed ? GroundKind.Plowed : GroundKind.Crop;
            double wobble = 35 * Noise.Fractal(x / 120, z / 120, seed + 41, 2);
            // Snow on the peaks above the pass, and a few patches just below it (summer)
            bool peak = h > m.Summit + SnowAboveSummit + wobble;
            bool patch = h > m.Summit - 120 && Noise.Fractal(x / 70, z / 70, seed + 47, 2) > 0.45;
            if ((peak || patch) && slope < 0.85) return GroundKind.Snow;
            if (slope > 0.95 + (0.15 * Noise.Value(x / 60, z / 60, seed + 43))) return GroundKind.Rock;
            double treeline = Treeline(m) + wobble;
            if (h > treeline + 220 && slope > 0.55) return GroundKind.Rock;
            if (h > treeline) return GroundKind.Meadow;
            if (distant && Forest(m, x, z, h, seed) > 0.45 && m.RoadDistance(x, z) > 25) return GroundKind.Forest;
            return GroundKind.Grass;
        }

        /// <summary>Trees thin out over the last 120 m below the treeline.</summary>
        private static double TreeFactor(MountainTerrain m, double h)
        {
            double t = (Treeline(m) - h) / 120;
            return Math.Max(0, Math.Min(1, t));
        }

        /// <summary>How much forest grows here (0–1): none on the valley floors, thinning to the treeline, clearings round the villages.</summary>
        public static double Forest(MountainTerrain m, double x, double z, double h, int seed)
        {
            var map = m.Map;
            double forest = ForestDensity(x, z, seed) * TreeFactor(m, h) * (1 - map.ValleyFloorFactor(x, z));
            // The Alm: open pasture round Oberalm and along the plateau (fading into the forest 250–550 m away).
            double alm = map.DistanceToPlateau(x, z);
            return forest * SmoothStep((alm - 250) / 300);
        }

        private static double SmoothStep(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t * t * (3 - (2 * t));
        }

        /// <summary>Lärchwald: larches round the village.</summary>
        private static double LarchBonus(WorldMap map, double x, double z)
        {
            var n = map.Nodes[Valleys.Laerchwald];
            double d = Math.Sqrt(((n.X - x) * (n.X - x)) + ((n.Z - z) * (n.Z - z)));
            return Math.Max(0, Math.Min(1, (2600 - d) / 1200));
        }

        /// <summary>
        /// Trees, bushes and rocks of one 128 m piece (deterministic), by the altitude zones of the Alps: valley floors
        /// are meadows, with alders and willows along the river and the lakes and a few single trees (larch groves
        /// round Lärchwald); mixed mountain forest of spruce, fir, beech and larch up to ~1350 m; spruce and larch, then
        /// Swiss stone pine up to the treeline; mountain pine scrub and green alder above it; then rock. Nothing on the
        /// roads, in the water or on what people built (<see cref="Terrain.Landscape"/>); the landscape's own items
        /// (houses, farm trees, fences …) are added.
        /// </summary>
        public static List<SceneryItem> Items(Terrain terrain, int chunkX, int chunkZ, int seed)
        {
            var items = new List<SceneryItem>();
            var m = terrain.Mountain;
            if (m == null) return items;
            var map = terrain.Map;
            var landscape = terrain.Landscape;
            if (landscape != null) items.AddRange(landscape.ItemsIn(chunkX, chunkZ));
            double x0 = chunkX * ChunkSize, z0 = chunkZ * ChunkSize;
            int cells = (int)(ChunkSize / ItemCell);
            double treeline = Treeline(m);
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    int gx = (chunkX * cells) + i, gz = (chunkZ * cells) + j;
                    double r1 = Noise.Random01(gx, gz, seed), r2 = Noise.Random01(gx, gz, seed + 1), r3 = Noise.Random01(gx, gz, seed + 2);
                    double species = Noise.Random01(gx, gz, seed + 3);
                    double x = x0 + ((i + r1) * ItemCell), z = z0 + ((j + r2) * ItemCell);
                    if (!m.Contains(x, z) && !terrain.InGrid(x, z)) continue;
                    if ((terrain.InGrid(x, z) || m.RoadDistance(x, z) < 40) && terrain.DistanceToRoad(x, z, 9, out _, out _, out _) < 9) continue;
                    if (landscape != null && landscape.Blocked(x, z)) continue;
                    if (terrain.IsWater(x, z, 2)) continue;

                    double h = terrain.Height(x, z);
                    double slope = terrain.Slope(x, z);
                    var item = new SceneryItem { X = (float)x, Y = (float)h - 0.3f, Z = (float)z, RotationDeg = (float)(r3 * 360), Variant = (int)(r2 * 97), Scale = (float)(0.8 + (0.4 * r1)) };

                    // Along the river and the lake shores: alders and willows.
                    double river = terrain.DistanceToRiver(x, z, 40, out _);
                    bool shore = river < 40 || map.Lakes.Any(l => l.Relative(x, z) is double q && q > 1.03 && q < 1.35);
                    if (shore && h < 1300)
                    {
                        if (r3 < 0.5)
                        {
                            item.Kind = species < 0.62 ? SceneryKind.Alder : SceneryKind.Willow;
                            items.Add(item);
                        }

                        continue;
                    }

                    double floor = map.ValleyFloorFactor(x, z);
                    double larch = LarchBonus(map, x, z);
                    if (floor > 0.5)
                    {
                        // Meadows; larch groves round Lärchwald, otherwise a few single trees and bushes.
                        if (larch > 0 && Noise.Fractal(x / 160, z / 160, seed + 61, 2) > 0.2 && r3 < 0.45 * larch)
                        {
                            item.Kind = SceneryKind.Larch;
                            items.Add(item);
                        }
                        else if (r3 < 0.012)
                        {
                            item.Kind = species < 0.45 ? SceneryKind.Maple : species < 0.65 ? SceneryKind.Linden : species < 0.85 ? SceneryKind.Beech : SceneryKind.Larch;
                            items.Add(item);
                        }
                        else if (r3 > 0.988)
                        {
                            item.Kind = SceneryKind.Bush;
                            item.Scale = (float)(0.6 + (r1 * 0.5));
                            items.Add(item);
                        }

                        continue;
                    }

                    double forest = Forest(m, x, z, h, seed);
                    if (forest > 0.05 && slope < 1.0 && r3 < 0.25 + (0.65 * forest))
                    {
                        if (h < 1350)
                        {
                            double l = 0.13 + (0.37 * larch);
                            item.Kind = species < l ? SceneryKind.Larch : species < l + 0.42 ? SceneryKind.Spruce : species < l + 0.62 ? SceneryKind.Fir : SceneryKind.Beech;
                        }
                        else if (h < 1700)
                        {
                            item.Kind = species < 0.58 ? SceneryKind.Spruce : species < 0.85 ? SceneryKind.Larch : species < 0.95 ? SceneryKind.Fir : SceneryKind.StonePine;
                        }
                        else
                        {
                            item.Kind = species < 0.3 ? SceneryKind.Spruce : species < 0.65 ? SceneryKind.Larch : SceneryKind.StonePine;
                        }

                        // Smaller close to the treeline
                        item.Scale = (float)((0.75 + (0.5 * r1)) * (0.55 + (0.45 * Math.Min(1, forest * 1.6))));
                        items.Add(item);
                    }
                    else if (h > treeline - 60 && h < treeline + 320 && slope < 0.9
                        && Noise.Fractal(x / 90, z / 90, seed + 67, 2) > 0.05 && r3 < 0.55)
                    {
                        // Above the treeline: mountain pine scrub and green alder in patches.
                        item.Kind = species < 0.8 ? SceneryKind.MountainPine : SceneryKind.Bush;
                        item.Y = (float)h - 0.1f;
                        items.Add(item);
                    }
                    else if (h > treeline - 80 && r3 > 0.9 - Math.Min(0.25, slope * 0.2))
                    {
                        item.Kind = SceneryKind.Rock;
                        item.Scale = (float)(0.6 + (r1 * 1.4));
                        item.Y = (float)h - 0.4f;
                        items.Add(item);
                    }
                    else if (h < treeline && r3 > 0.97)
                    {
                        item.Kind = SceneryKind.Bush;
                        item.Scale = (float)(0.6 + (r1 * 0.5));
                        item.Y = (float)h;
                        items.Add(item);
                    }
                }
            }

            return items;
        }
    }
}
