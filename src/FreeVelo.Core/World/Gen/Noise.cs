using System;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>Deterministic 2D value noise and fractal sums (the same on every device and the server).</summary>
    public static class Noise
    {
        /// <summary>Smooth noise in about [-1, 1] with features of about one unit.</summary>
        public static double Value(double x, double z, int seed)
        {
            int x0 = (int)Math.Floor(x), z0 = (int)Math.Floor(z);
            double fx = x - x0, fz = z - z0;
            double u = fx * fx * fx * ((fx * ((fx * 6) - 15)) + 10);
            double v = fz * fz * fz * ((fz * ((fz * 6) - 15)) + 10);
            double a = Hash(x0, z0, seed), b = Hash(x0 + 1, z0, seed), c = Hash(x0, z0 + 1, seed), d = Hash(x0 + 1, z0 + 1, seed);
            return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
        }

        /// <summary>Fractal noise: <paramref name="octaves"/> layers, each double frequency and half amplitude.</summary>
        public static double Fractal(double x, double z, int seed, int octaves)
        {
            double sum = 0, amplitude = 1, total = 0, frequency = 1;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x * frequency, z * frequency, seed + (i * 131)) * amplitude;
                total += amplitude;
                amplitude *= 0.5;
                frequency *= 2.03;
            }

            return sum / total;
        }

        /// <summary>Pseudo-random number in [0, 1) for integer coordinates.</summary>
        public static double Random01(int x, int z, int seed) => (Hash(x, z, seed) + 1) / 2;

        private static double Hash(int x, int z, int seed)
        {
            unchecked
            {
                // Mix both coordinates fully (murmur3 finalizer), so the noise has no directional streaks.
                uint h = (uint)x * 0x8DA6B343u;
                h ^= (uint)z * 0xD8163841u;
                h ^= (uint)seed * 0xCB1AB31Fu;
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return (h / (double)uint.MaxValue * 2) - 1;
            }
        }

        private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
    }
}
