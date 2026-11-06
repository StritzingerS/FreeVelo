using System;
using System.Collections.Generic;

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
    }

    /// <summary>
    /// The high-mountain look, by height between the valleys and the summit, slope and distance to the roads: mixed
    /// forest in the valleys, spruce and pine higher up, thinning to the treeline about 550 m below the summit, alpine
    /// meadows and scree above, rock on steep slopes and snow on the highest ground.
    /// </summary>
    public static class MountainLook
    {
        public const double TreelineBelowSummit = 550;
        public const double SnowBelowSummit = 140;
        public const double ChunkSize = 128;
        private const double ItemCell = 11;

        /// <summary>0 at the lowest mountain road, 1 at the summit (can exceed 1 on the peaks around).</summary>
        public static double Level(MountainTerrain m, double h) => (h - m.LowRoad) / Math.Max(1, m.Summit - m.LowRoad);

        public static double Treeline(MountainTerrain m) => m.Summit - TreelineBelowSummit;

        /// <summary>How much forest grows here (0–1), before height and slope.</summary>
        public static double ForestDensity(double x, double z, int seed) =>
            Math.Max(0, Math.Min(1, 0.55 + (0.7 * Noise.Fractal(x / 420, z / 420, seed + 31, 3))));

        /// <summary>Ground cover at a point. <paramref name="slope"/> = rise per metre.</summary>
        public static GroundKind Ground(MountainTerrain m, double x, double z, double h, double slope, int seed, bool distant)
        {
            double wobble = 35 * Noise.Fractal(x / 120, z / 120, seed + 41, 2);
            if (h > m.Summit - SnowBelowSummit + wobble && slope < 0.85) return GroundKind.Snow;
            if (slope > 0.95 + (0.15 * Noise.Value(x / 60, z / 60, seed + 43))) return GroundKind.Rock;
            double treeline = Treeline(m) + wobble;
            if (h > treeline + 220 && slope > 0.55) return GroundKind.Rock;
            if (h > treeline) return GroundKind.Meadow;
            if (distant && ForestDensity(x, z, seed) * TreeFactor(m, h) > 0.45 && m.RoadDistance(x, z) > 25) return GroundKind.Forest;
            return GroundKind.Grass;
        }

        /// <summary>Trees thin out over the last 120 m below the treeline.</summary>
        private static double TreeFactor(MountainTerrain m, double h)
        {
            double t = (Treeline(m) - h) / 120;
            return Math.Max(0, Math.Min(1, t));
        }

        /// <summary>
        /// Trees, bushes and rocks of one 128 m piece of the mountain (deterministic). Not on the roads or the home
        /// world's ground, no trees on cliffs.
        /// </summary>
        public static List<SceneryItem> Items(Terrain terrain, int chunkX, int chunkZ, int seed)
        {
            var items = new List<SceneryItem>();
            var m = terrain.Mountain;
            if (m == null) return items;
            double x0 = chunkX * ChunkSize, z0 = chunkZ * ChunkSize;
            int cells = (int)(ChunkSize / ItemCell);
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    int gx = (chunkX * cells) + i, gz = (chunkZ * cells) + j;
                    double r1 = Noise.Random01(gx, gz, seed), r2 = Noise.Random01(gx, gz, seed + 1), r3 = Noise.Random01(gx, gz, seed + 2);
                    double x = x0 + ((i + r1) * ItemCell), z = z0 + ((j + r2) * ItemCell);
                    if (terrain.InGrid(x, z) || !m.Contains(x, z)) continue;
                    if (m.RoadDistance(x, z) < 40 && terrain.DistanceToRoad(x, z, 9, out _, out _, out _) < 9) continue;

                    double h = terrain.Height(x, z);
                    double slope = terrain.Slope(x, z);
                    double level = Level(m, h);
                    double forest = ForestDensity(x, z, seed) * TreeFactor(m, h);
                    var item = new SceneryItem { X = (float)x, Y = (float)h, Z = (float)z, RotationDeg = (float)(r3 * 360), Variant = (int)(r2 * 97) };

                    if (forest > 0.05 && slope < 1.0 && r3 < 0.25 + (0.65 * forest))
                    {
                        double species = Noise.Random01(gx, gz, seed + 3);
                        if (level < 0.22)
                        {
                            item.Kind = species < 0.35 ? SceneryKind.Spruce : species < 0.6 ? SceneryKind.Oak : species < 0.8 ? SceneryKind.Birch : SceneryKind.Bush;
                        }
                        else
                        {
                            item.Kind = species < 0.72 ? SceneryKind.Spruce : SceneryKind.Pine;
                        }

                        // Smaller close to the treeline
                        item.Scale = (float)((0.75 + (0.5 * r1)) * (0.55 + (0.45 * Math.Min(1, forest * 1.6))));
                        item.Y -= 0.3f;
                        items.Add(item);
                    }
                    else if (h > Treeline(m) - 80 && r3 > 0.9 - (Math.Min(0.25, slope * 0.2)))
                    {
                        item.Kind = SceneryKind.Rock;
                        item.Scale = (float)(0.6 + (r1 * 1.4));
                        item.Y -= 0.4f;
                        items.Add(item);
                    }
                    else if (h < Treeline(m) + 120 && r3 > 0.965)
                    {
                        item.Kind = SceneryKind.Bush;
                        item.Scale = (float)(0.6 + (r1 * 0.5));
                        items.Add(item);
                    }
                }
            }

            return items;
        }
    }
}
