using System;
using System.Numerics;

namespace FreeVelo.Core.Avatar
{
    /// <summary>
    /// Rotation as 3×3 matrix with Unity's conventions (left-handed: x right, y up, z forward;
    /// Euler applies Z, then X, then Y). Lets the avatar geometry be built and previewed outside Unity.
    /// </summary>
    public readonly struct Rot
    {
        // Columns = images of the x, y, z axes.
        public readonly Vector3 X;
        public readonly Vector3 Y;
        public readonly Vector3 Z;

        public Rot(Vector3 x, Vector3 y, Vector3 z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Rot Identity => new Rot(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

        public static Vector3 operator *(Rot r, Vector3 v) => (r.X * v.X) + (r.Y * v.Y) + (r.Z * v.Z);

        public static Rot operator *(Rot a, Rot b) => new Rot(a * b.X, a * b.Y, a * b.Z);

        public Rot Transposed => new Rot(new Vector3(X.X, Y.X, Z.X), new Vector3(X.Y, Y.Y, Z.Y), new Vector3(X.Z, Y.Z, Z.Z));

        /// <summary>Rotation about x (degrees): +90° turns forward (z) into down (−y), like Unity.</summary>
        public static Rot AboutX(float degrees)
        {
            float a = Rad(degrees), c = MathF.Cos(a), s = MathF.Sin(a);
            return new Rot(Vector3.UnitX, new Vector3(0, c, s), new Vector3(0, -s, c));
        }

        /// <summary>Rotation about y (degrees): +90° turns forward (z) into right (x).</summary>
        public static Rot AboutY(float degrees)
        {
            float a = Rad(degrees), c = MathF.Cos(a), s = MathF.Sin(a);
            return new Rot(new Vector3(c, 0, -s), Vector3.UnitY, new Vector3(s, 0, c));
        }

        /// <summary>Rotation about z (degrees): +90° turns right (x) into up (y).</summary>
        public static Rot AboutZ(float degrees)
        {
            float a = Rad(degrees), c = MathF.Cos(a), s = MathF.Sin(a);
            return new Rot(new Vector3(c, s, 0), new Vector3(-s, c, 0), Vector3.UnitZ);
        }

        /// <summary>Same as UnityEngine.Quaternion.Euler(x, y, z).</summary>
        public static Rot Euler(float x, float y, float z) => AboutY(y) * AboutX(x) * AboutZ(z);

        /// <summary>Same as UnityEngine.Quaternion.AngleAxis.</summary>
        public static Rot AngleAxis(float degrees, Vector3 axis)
        {
            var n = Vector3.Normalize(axis);
            float a = Rad(degrees); // same matrix as Unity: AngleAxis(90, right) turns up into forward
            float c = MathF.Cos(a), s = MathF.Sin(a), t = 1 - c;
            return new Rot(
                new Vector3((t * n.X * n.X) + c, (t * n.X * n.Y) + (s * n.Z), (t * n.X * n.Z) - (s * n.Y)),
                new Vector3((t * n.X * n.Y) - (s * n.Z), (t * n.Y * n.Y) + c, (t * n.Y * n.Z) + (s * n.X)),
                new Vector3((t * n.X * n.Z) + (s * n.Y), (t * n.Y * n.Z) - (s * n.X), (t * n.Z * n.Z) + c));
        }

        /// <summary>Same as UnityEngine.Quaternion.LookRotation(forward, up).</summary>
        public static Rot LookRotation(Vector3 forward, Vector3 up)
        {
            var z = Vector3.Normalize(forward);
            var x = Vector3.Cross(up, z);
            if (x.LengthSquared() < 1e-12f) x = Vector3.Cross(Math.Abs(z.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX, z);
            x = Vector3.Normalize(x);
            var y = Vector3.Cross(z, x);
            return new Rot(x, y, z);
        }

        /// <summary>Rotation that turns direction <paramref name="from"/> into <paramref name="to"/> (shortest arc).</summary>
        public static Rot FromTo(Vector3 from, Vector3 to)
        {
            var a = Vector3.Normalize(from);
            var b = Vector3.Normalize(to);
            var v = Vector3.Cross(a, b);
            float c = Vector3.Dot(a, b);
            if (c < -0.9999f)
            {
                var axis = Vector3.Cross(a, Math.Abs(a.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
                return AngleAxis(180, axis);
            }

            float k = 1f / (1f + c);
            // Rodrigues: R = I + [v]x + [v]x² / (1 + c)
            return new Rot(
                new Vector3(1 - ((v.Y * v.Y) + (v.Z * v.Z)) * k, v.Z + (v.X * v.Y * k), -v.Y + (v.X * v.Z * k)),
                new Vector3(-v.Z + (v.X * v.Y * k), 1 - ((v.X * v.X) + (v.Z * v.Z)) * k, v.X + (v.Y * v.Z * k)),
                new Vector3(v.Y + (v.X * v.Z * k), -v.X + (v.Y * v.Z * k), 1 - ((v.X * v.X) + (v.Y * v.Y)) * k));
        }

        /// <summary>Quaternion (x, y, z, w) of this rotation, directly usable as UnityEngine.Quaternion.</summary>
        public Vector4 ToQuaternion()
        {
            float m00 = X.X, m11 = Y.Y, m22 = Z.Z;
            float m01 = Y.X, m02 = Z.X, m10 = X.Y, m12 = Z.Y, m20 = X.Z, m21 = Y.Z;
            float trace = m00 + m11 + m22;
            float x, y, z, w;
            if (trace > 0)
            {
                float s = MathF.Sqrt(trace + 1f) * 2f;
                w = 0.25f * s;
                x = (m21 - m12) / s;
                y = (m02 - m20) / s;
                z = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
                w = (m21 - m12) / s;
                x = 0.25f * s;
                y = (m01 + m10) / s;
                z = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
                w = (m02 - m20) / s;
                x = (m01 + m10) / s;
                y = 0.25f * s;
                z = (m12 + m21) / s;
            }
            else
            {
                float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
                w = (m10 - m01) / s;
                x = (m02 + m20) / s;
                y = (m12 + m21) / s;
                z = 0.25f * s;
            }

            return new Vector4(x, y, z, w);
        }

        private static float Rad(float degrees) => degrees * MathF.PI / 180f;
    }
}
