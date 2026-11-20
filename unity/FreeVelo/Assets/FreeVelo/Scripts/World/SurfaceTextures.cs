using System;
using System.Collections.Generic;
using FreeVelo.Core.World.Gen;
using UnityEngine;

namespace FreeVelo.World
{
    /// <summary>
    /// Surface detail for the models (graphics overhaul, vertical slice): a tiling detail texture and a normal map per
    /// kind of surface – plaster, wooden boards, roof tiles, shingles, rubble stone, bark, foliage, rock, gravel.
    /// The detail textures are close to white and are tinted by the material's colour (<see cref="WorldMaterials"/>),
    /// so the palette stays where it is. Generated procedurally (seamless, deterministic) – nothing to download or
    /// license; can later be swapped for CC0 scans with the same mapping.
    /// </summary>
    public static class SurfaceTextures
    {
        public enum Surface
        {
            None,
            Plaster,
            Boards,
            RoofTiles,
            Shingles,
            Stone,
            Bark,
            Foliage,
            Rock,
            Concrete,
            Gravel,
            Earth,
        }

        /// <summary>A surface: detail (sRGB), normal map (linear) and how many metres one repeat covers.</summary>
        public sealed class Set
        {
            public Texture2D Albedo;
            public Texture2D Normal;
            public float TileM;
            public float BumpScale = 1f;

            /// <summary>
            /// Average colour (sRGB) of a scanned texture; null for the generated, near-white ones. A material's colour
            /// is divided by it, so the scan keeps its detail but takes the palette's colour (one plank scan serves
            /// dark and light wood).
            /// </summary>
            public Color? Mean;

            /// <summary>The material colour that makes this surface come out as <paramref name="target"/> on average.</summary>
            public Color Tint(Color target)
            {
                if (!Mean.HasValue) return target;
                var m = Mean.Value;
                return new Color(
                    Mathf.Min(2.5f, target.r / Mathf.Max(0.05f, m.r)),
                    Mathf.Min(2.5f, target.g / Mathf.Max(0.05f, m.g)),
                    Mathf.Min(2.5f, target.b / Mathf.Max(0.05f, m.b)),
                    1f);
            }
        }

        /// <summary>
        /// CC0 scans from Poly Haven in Resources/Surfaces (docs/THIRD_PARTY_ASSETS.md): file stem, real size of one
        /// repeat in metres and average colour. Surfaces without a scan are generated.
        /// </summary>
        private static readonly Dictionary<Surface, (string Stem, float TileM, Color Mean)> Scans = new Dictionary<Surface, (string, float, Color)>
        {
            [Surface.Plaster] = ("plastered_wall_04", 3.2f, new Color(0.553f, 0.542f, 0.531f)),
            [Surface.Boards] = ("wood_plank_wall", 1.0f, new Color(0.260f, 0.198f, 0.175f)),
            [Surface.RoofTiles] = ("clay_roof_tiles", 4.0f, new Color(0.350f, 0.201f, 0.109f)),
            [Surface.Shingles] = ("roof_slates_02", 3.0f, new Color(0.565f, 0.525f, 0.460f)),
            [Surface.Stone] = ("old_stone_wall", 2.0f, new Color(0.528f, 0.465f, 0.389f)),
            [Surface.Bark] = ("pine_bark", 1.0f, new Color(0.402f, 0.325f, 0.252f)),
            [Surface.Rock] = ("rock_face", 2.4f, new Color(0.380f, 0.273f, 0.194f)),
            [Surface.Gravel] = ("gravel_floor", 2.25f, new Color(0.636f, 0.524f, 0.395f)),
            [Surface.Earth] = ("dirt", 2.0f, new Color(0.389f, 0.323f, 0.245f)),   // high-passed: no repeating blotches
        };

        private static Set LoadScan(Surface s)
        {
            if (!Scans.TryGetValue(s, out var scan)) return null;
            var albedo = Resources.Load<Texture2D>("Surfaces/" + scan.Stem + "_diff");
            var normal = Resources.Load<Texture2D>("Surfaces/" + scan.Stem + "_nor");
            if (albedo == null || normal == null) return null;
            return new Set { Albedo = albedo, Normal = normal, TileM = scan.TileM, Mean = scan.Mean };
        }

        private static readonly Dictionary<Surface, Set> Cache = new Dictionary<Surface, Set>();

        /// <summary>Total time spent generating (for the start-up log).</summary>
        public static long GenerateMs { get; private set; }

        /// <summary>
        /// With "Enter Play Mode Options" (no domain reload) this static cache would outlive the play session while
        /// Unity destroys its textures, leaving every model flat: start empty each time.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
            GenerateMs = 0;
        }

        public static Surface Of(WorldMaterial m)
        {
            switch (m)
            {
                case WorldMaterial.WallWhite:
                case WorldMaterial.WallCream:
                case WorldMaterial.WallOchre:
                case WorldMaterial.WallBrick:
                    return Surface.Plaster;
                case WorldMaterial.WallWood:
                case WorldMaterial.WoodDark:
                case WorldMaterial.WoodLight:
                case WorldMaterial.Door:
                case WorldMaterial.Shutter:
                case WorldMaterial.SignBoard:
                    return Surface.Boards;
                case WorldMaterial.RoofRed:
                case WorldMaterial.RoofBrown:
                case WorldMaterial.RoofGrey:
                    return Surface.RoofTiles;
                case WorldMaterial.Shingle:
                    return Surface.Shingles;
                case WorldMaterial.Plinth:
                case WorldMaterial.Stone:
                    return Surface.Stone;
                case WorldMaterial.Bark:
                case WorldMaterial.PineBark:
                case WorldMaterial.BirchBark:
                case WorldMaterial.BeechBark:
                    return Surface.Bark;
                case WorldMaterial.SpruceNeedles:
                case WorldMaterial.SpruceNeedlesDark:
                case WorldMaterial.PineNeedles:
                case WorldMaterial.LeavesDark:
                case WorldMaterial.LeavesMid:
                case WorldMaterial.LeavesLight:
                case WorldMaterial.BirchLeaves:
                case WorldMaterial.PoplarLeaves:
                case WorldMaterial.Bush:
                case WorldMaterial.LarchNeedles:
                case WorldMaterial.FirNeedles:
                case WorldMaterial.StonePineNeedles:
                case WorldMaterial.BeechLeaves:
                case WorldMaterial.MapleLeaves:
                case WorldMaterial.LindenLeaves:
                case WorldMaterial.AlderLeaves:
                case WorldMaterial.WillowLeaves:
                case WorldMaterial.AppleLeaves:
                    return Surface.Foliage;
                case WorldMaterial.Rock:
                    return Surface.Rock;
                case WorldMaterial.Concrete:
                    return Surface.Concrete;
                case WorldMaterial.Gravel:
                case WorldMaterial.LaneAsphalt:
                    return Surface.Gravel;
                case WorldMaterial.YardEarth:
                    return Surface.Earth;
                default:
                    return Surface.None;
            }
        }

        /// <summary>The textures of a surface (generated on first use); null for <see cref="Surface.None"/>.</summary>
        public static Set Get(Surface s)
        {
            if (s == Surface.None) return null;
            if (Cache.TryGetValue(s, out var set) && set.Albedo != null && set.Normal != null) return set;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            set = LoadScan(s) ?? Generate(s);
            GenerateMs += clock.ElapsedMilliseconds;
            Cache[s] = set;
            return set;
        }

        /// <summary>Metres per texture repeat for UVs (1 m for surfaces without texture).</summary>
        public static float TileM(WorldMaterial m)
        {
            var s = Of(m);
            return s == Surface.None ? 1f : Get(s).TileM;
        }

        /// <summary>
        /// Texture coordinates in metres / <paramref name="tile"/>, projected onto the surface's own plane: u runs
        /// horizontally along a wall or the eaves of a roof, v up the wall or the slope (so boards stand upright and
        /// roof tiles lie in rows). Flat faces use x/z.
        /// </summary>
        public static Vector2 UV(Vector3 p, Vector3 n, float tile)
        {
            if (Mathf.Abs(n.y) > 0.985f) return new Vector2(p.x, p.z) / tile;
            var t = new Vector3(n.z, 0, -n.x).normalized;
            var b = Vector3.Cross(n, t);
            return new Vector2(Vector3.Dot(p, t), Vector3.Dot(p, b)) / tile;
        }

        // ------------------------------------------------------------------ generators

        private const int Size = 256;

        private static Set Generate(Surface s)
        {
            switch (s)
            {
                case Surface.Plaster:
                    return Make("Plaster", 2.5f, 1.2f, (u, v) =>
                    {
                        float h = (0.6f * Fbm(u, v, 16, 16, 3, 1)) + (0.4f * Fbm(u, v, 64, 64, 2, 2));
                        float stain = Mathf.Max(0, Fbm(u, v, 3, 3, 3, 3) - 0.55f);
                        return (h, Grey(0.94f + ((h - 0.5f) * 0.10f) - (stain * 0.3f)));
                    });

                case Surface.Boards:
                    return Make("Boards", 1.2f, 3.5f, (u, v) =>
                    {
                        const int boards = 6;
                        float bu = u * boards;
                        int b = (int)bu;
                        float x = bu - b;
                        float grain = Fbm(u, v, 96, 3, 3, 10);
                        float knot = Mathf.Max(0, Fbm(u, v, 12, 4, 2, 11) - 0.72f) * 3f;
                        if (x < 0.045f || x > 0.975f) return (0f, Grey(0.32f));
                        float bevel = Mathf.Max(0, 0.12f - x) + Mathf.Max(0, x - 0.88f);
                        float h = 0.65f + (grain * 0.35f) - (bevel * 3f);
                        float shade = (Hash(b, 0, 5) - 0.5f) * 0.18f;
                        return (h, Grey(0.84f + shade + ((grain - 0.5f) * 0.35f) - knot));
                    });

                case Surface.RoofTiles:
                    return Make("RoofTiles", 1.6f, 4f, (u, v) =>
                    {
                        const int rows = 6, cols = 8;
                        float rv = v * rows;
                        int r = (int)rv;
                        float y = rv - r;
                        float cu = (u * cols) + ((r % 2) * 0.5f);
                        int c = (int)Mathf.Floor(cu);
                        float x = cu - c;
                        float profile = Mathf.Sin(x * Mathf.PI);
                        float grime = Fbm(u, v, 8, 8, 3, 8);
                        float h = 0.35f + (profile * 0.35f) + ((1 - y) * 0.3f);
                        float a = 0.86f + ((Hash(Wrap(c, cols), r, 7) - 0.5f) * 0.2f) + (profile * 0.08f) - ((grime - 0.5f) * 0.2f);
                        if (y < 0.08f)
                        {
                            a *= 0.55f; // the lip of the row above casts a line
                            h = 0.1f;
                        }

                        return (h, Grey(a));
                    });

                case Surface.Shingles:
                    return Make("Shingles", 1.2f, 3.5f, (u, v) =>
                    {
                        const int rows = 8, cols = 10;
                        float rv = v * rows;
                        int r = (int)rv;
                        float y = rv - r;
                        float cu = (u * cols) + Hash(r, 0, 11);
                        int c = (int)Mathf.Floor(cu);
                        float x = cu - c;
                        float grain = Fbm(u, v, 80, 6, 2, 12);
                        if (x < 0.06f) return (0.1f, Grey(0.4f));
                        float a = 0.74f + (Hash(Wrap(c, cols), r, 13) * 0.3f) + ((grain - 0.5f) * 0.25f);
                        float h = 0.5f + ((1 - y) * 0.4f) + (grain * 0.1f);
                        if (y < 0.07f)
                        {
                            a *= 0.55f;
                            h = 0.1f;
                        }

                        return (h, Grey(a));
                    });

                case Surface.Stone:
                    return Make("Stone", 2f, 4f, (u, v) =>
                    {
                        var (d1, d2, id) = Voronoi(u, v, 5, 21);
                        float edge = d2 - d1;
                        float bump = Fbm(u, v, 24, 24, 3, 22);
                        if (edge < 0.07f) return (0.1f + (bump * 0.1f), Grey(0.98f));
                        float h = 0.5f + (Mathf.Min(edge, 0.3f) * 1.5f) + (bump * 0.2f);
                        return (h, Grey(0.7f + (Hash(id, 0, 23) * 0.25f) + ((bump - 0.5f) * 0.2f)));
                    });

                case Surface.Bark:
                    return Make("Bark", 1f, 6f, (u, v) =>
                    {
                        float n = Fbm(u, v, 10, 2, 4, 31);
                        float ridge = 1 - Mathf.Abs((2 * n) - 1);
                        return (ridge, Grey(0.6f + (ridge * 0.45f)));
                    });

                case Surface.Foliage:
                    // Small, soft leaf clusters with fine speckle: reads as foliage, not as cells or voxels.
                    return Make("Foliage", 0.6f, 2.2f, (u, v) =>
                    {
                        var (d1, _, _) = Voronoi(u, v, 9, 41);
                        float cluster = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1 - (d1 * 1.25f)));
                        float fine = Fbm(u, v, 48, 48, 2, 42);
                        float h = Mathf.Clamp01((cluster * 0.7f) + (fine * 0.3f));
                        return (h, Grey(0.66f + (h * 0.4f)));
                    });

                case Surface.Rock:
                    return Make("Rock", 3f, 5f, (u, v) =>
                    {
                        float n = Fbm(u, v, 4, 4, 5, 51);
                        float crack = Mathf.Abs((2 * Fbm(u, v, 6, 6, 3, 52)) - 1) < 0.04f ? 1f : 0f;
                        return (n - (crack * 0.3f), Grey((0.78f + ((n - 0.5f) * 0.4f)) * (1 - (crack * 0.4f))));
                    });

                case Surface.Concrete:
                    return Make("Concrete", 2f, 1.5f, (u, v) =>
                    {
                        float h = (0.5f * Fbm(u, v, 24, 24, 3, 61)) + (0.5f * Fbm(u, v, 96, 96, 1, 62));
                        return (h, Grey(0.92f + ((h - 0.5f) * 0.14f)));
                    });

                case Surface.Gravel:
                case Surface.Earth:   // only without the scan
                    return Make("GravelStones", 1f, 4f, (u, v) =>
                    {
                        var (d1, _, id) = Voronoi(u, v, 20, 71);
                        float h = Mathf.Clamp01(1 - (d1 * 1.8f));
                        return (h, Grey(0.72f + (Hash(id, 2, 72) * 0.3f) - ((1 - h) * 0.15f)));
                    });
            }

            return null;
        }

        private static Color Grey(float g)
        {
            g = Mathf.Clamp01(g);
            return new Color(g, g, g, 1);
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        /// <summary>Builds the detail texture and its normal map from one height/colour function on the unit square.</summary>
        private static Set Make(string name, float tileM, float bump, Func<float, float, (float Height, Color Albedo)> pixel)
        {
            var heights = new float[Size * Size];
            var albedo = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var (h, c) = pixel((x + 0.5f) / Size, (y + 0.5f) / Size);
                    heights[(y * Size) + x] = h;
                    albedo[(y * Size) + x] = c;
                }
            }

            var a = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            a.SetPixels32(albedo);
            a.Apply(true, true);

            // Its average brightness, so that the material colour is compensated as for the scans: the surface comes
            // out in the palette's colour on average (foliage was about 15 % too dark otherwise).
            double r = 0, g = 0, b = 0;
            foreach (var c in albedo)
            {
                r += c.r;
                g += c.g;
                b += c.b;
            }

            float n = albedo.Length * 255f;
            var mean = new Color((float)(r / n), (float)(g / n), (float)(b / n));
            return new Set { Albedo = a, Normal = NormalMap(name + "Normal", heights, Size, bump), TileM = tileM, Mean = mean };
        }

        /// <summary>
        /// Tangent-space normal map from a tiling height field. Encoded so that every Unity unpacking path reads it
        /// (RGB, and AG with R folded in): x in R and A, y in G, z in B.
        /// </summary>
        public static Texture2D NormalMap(string name, float[] heights, int size, float strength)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = heights[(y * size) + ((x + 1) % size)] - heights[(y * size) + ((x + size - 1) % size)];
                    float dy = heights[(((y + 1) % size) * size) + x] - heights[(((y + size - 1) % size) * size) + x];
                    var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    byte r = (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255);
                    byte g = (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255);
                    byte b = (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255);
                    pixels[(y * size) + x] = new Color32(r, g, b, r);
                }
            }

            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(pixels);
            t.Apply(true, true);
            return t;
        }

        // ------------------------------------------------------------------ noise (seamless by construction)

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)((x * 374761393) + (y * 668265263) + (seed * 1442695041));
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Value noise on a lattice that repeats every (px, py) cells.</summary>
        private static float Value(float x, float y, int px, int py, int seed)
        {
            int x0 = (int)Mathf.Floor(x), y0 = (int)Mathf.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - (2 * fx));
            fy = fy * fy * (3 - (2 * fy));
            int ax = Wrap(x0, px), bx = Wrap(x0 + 1, px), ay = Wrap(y0, py), by = Wrap(y0 + 1, py);
            float a = Hash(ax, ay, seed), b = Hash(bx, ay, seed), c = Hash(ax, by, seed), d = Hash(bx, by, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Fractal noise over the unit square with whole frequencies (fx, fy), so it tiles.</summary>
        private static float Fbm(float u, float v, int fx, int fy, int octaves, int seed)
        {
            float sum = 0, amp = 0.5f, total = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(u * fx, v * fy, fx, fy, seed + (o * 17)) * amp;
                total += amp;
                amp *= 0.5f;
                fx *= 2;
                fy *= 2;
            }

            return sum / total;
        }

        /// <summary>Tiling cellular noise: distances (in cells) to the nearest and second nearest point, and the cell id.</summary>
        private static (float D1, float D2, int Id) Voronoi(float u, float v, int n, int seed)
        {
            float x = u * n, y = v * n;
            int cx = (int)Mathf.Floor(x), cy = (int)Mathf.Floor(y);
            float d1 = float.MaxValue, d2 = float.MaxValue;
            int id = 0;
            for (int j = -1; j <= 1; j++)
            {
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = Wrap(gx, n), wy = Wrap(gy, n);
                    float px = gx + 0.15f + (Hash(wx, wy, seed) * 0.7f), py = gy + 0.15f + (Hash(wx, wy, seed + 1) * 0.7f);
                    float d = Mathf.Sqrt(((px - x) * (px - x)) + ((py - y) * (py - y)));
                    if (d < d1)
                    {
                        d2 = d1;
                        d1 = d;
                        id = (wy * n) + wx;
                    }
                    else if (d < d2)
                    {
                        d2 = d;
                    }
                }
            }

            return (d1, d2, id);
        }
    }
}
