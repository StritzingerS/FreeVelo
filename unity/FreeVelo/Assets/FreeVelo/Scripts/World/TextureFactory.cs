using System;
using UnityEngine;

namespace FreeVelo.World
{
    /// <summary>Procedural, seamlessly tiling textures (asphalt, grass, gravel) generated at startup.</summary>
    public static class TextureFactory
    {
        /// <summary>Asphalt across the full road width (u = 0 … 1), with lane tyre tracks.</summary>
        public static Texture2D Asphalt(int size = 512)
        {
            var random = new System.Random(11);
            return Create(size, "Asphalt", (x, y, u, v) =>
            {
                float patches = (Fbm(u, v, 6f, 3, 1) - 0.5f) * 0.07f;
                float grain = ((float)random.NextDouble() - 0.5f) * 0.09f;
                float speckle = random.NextDouble() > 0.985 ? 0.12f : 0f;
                float tracks = -0.035f * (Track(u, 0.2f) + Track(u, 0.33f) + Track(u, 0.67f) + Track(u, 0.8f));
                float g = 0.27f + patches + grain + speckle + tracks;
                return new Color(g, g, g * 1.04f);
            });
        }

        /// <summary>Grass with colour patches, darker clumps and dry spots.</summary>
        public static Texture2D Grass(int size = 512)
        {
            var random = new System.Random(23);
            var dark = new Color(0.19f, 0.33f, 0.12f);
            var light = new Color(0.41f, 0.56f, 0.24f);
            var dry = new Color(0.56f, 0.55f, 0.31f);
            return Create(size, "Grass", (x, y, u, v) =>
            {
                float large = Fbm(u, v, 3f, 4, 2);
                float detail = Fbm(u, v, 24f, 2, 3);
                var c = Color.Lerp(dark, light, Mathf.Clamp01((large * 0.8f) + (detail * 0.4f) - 0.1f));
                float dryness = Mathf.Clamp01((Fbm(u, v, 5f, 3, 4) - 0.62f) * 4f);
                c = Color.Lerp(c, dry, dryness * 0.6f);
                float blade = ((float)random.NextDouble() - 0.5f) * 0.08f;
                return new Color(c.r + blade, c.g + blade, c.b + (blade * 0.5f));
            });
        }

        /// <summary>Gravel for the road shoulders.</summary>
        public static Texture2D Gravel(int size = 256)
        {
            var random = new System.Random(37);
            return Create(size, "Gravel", (x, y, u, v) =>
            {
                float stones = Fbm(u, v, 40f, 2, 5);
                float grain = ((float)random.NextDouble() - 0.5f) * 0.18f;
                float g = 0.42f + ((stones - 0.5f) * 0.25f) + grain;
                return new Color(g * 1.08f, g, g * 0.88f);
            });
        }

        private static float Track(float u, float center)
        {
            float d = (u - center) / 0.035f;
            return Mathf.Exp(-d * d);
        }

        private static Texture2D Create(int size, string name, Func<int, int, float, float, Color> pixel)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[(y * size) + x] = pixel(x, y, (float)x / size, (float)y / size);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>Fractal noise that tiles seamlessly on the unit square (0 … 1).</summary>
        private static float Fbm(float u, float v, float frequency, int octaves, int seed)
        {
            float sum = 0, amplitude = 0.5f, total = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += TileableNoise(u, v, frequency, seed + (o * 17)) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return sum / total;
        }

        /// <summary>Perlin noise blended across the borders so that it repeats with period 1.</summary>
        private static float TileableNoise(float u, float v, float frequency, int seed)
        {
            float ox = seed * 31.7f, oy = seed * 17.3f;
            float x = u * frequency, y = v * frequency, p = frequency;
            float a = Mathf.PerlinNoise(ox + x, oy + y);
            float b = Mathf.PerlinNoise(ox + x - p, oy + y);
            float c = Mathf.PerlinNoise(ox + x, oy + y - p);
            float d = Mathf.PerlinNoise(ox + x - p, oy + y - p);
            return (((1 - u) * (1 - v) * a) + (u * (1 - v) * b) + ((1 - u) * v * c) + (u * v * d));
        }
    }
}
