using System;
using System.Collections.Generic;
using System.Numerics;

namespace FreeVelo.Core.Avatar
{
    /// <summary>Signed distance function: negative inside, positive outside (metres).</summary>
    public delegate float Sdf(Vector3 p);

    /// <summary>
    /// Signed distance primitives and operators (after Inigo Quilez) used to sculpt organic shapes that are then
    /// turned into meshes by <see cref="Polygonizer"/>. Smooth unions blend parts without visible seams.
    /// </summary>
    public static class SdfShapes
    {
        public static Sdf Sphere(Vector3 c, float r) => p => Vector3.Distance(p, c) - r;

        /// <summary>Ellipsoid (approximate distance, good near the surface).</summary>
        public static Sdf Ellipsoid(Vector3 c, Vector3 radii, Rot? rotation = null)
        {
            var inv = (rotation ?? Rot.Identity).Transposed;
            var r2 = radii * radii;
            return p =>
            {
                var q = inv * (p - c);
                float k0 = (q / radii).Length();
                float k1 = (q / r2).Length();
                return k1 < 1e-9f ? -MathF.Min(radii.X, MathF.Min(radii.Y, radii.Z)) : k0 * (k0 - 1f) / k1;
            };
        }

        public static Sdf Capsule(Vector3 a, Vector3 b, float r) => RoundCone(a, b, r, r);

        /// <summary>Exact round cone: spheres of radius <paramref name="ra"/> at a and <paramref name="rb"/> at b, joined by a cone.</summary>
        public static Sdf RoundCone(Vector3 a, Vector3 b, float ra, float rb)
        {
            var ba = b - a;
            float l2 = Vector3.Dot(ba, ba);
            if (l2 < 1e-12f) return Sphere(a, MathF.Max(ra, rb));
            float rr = ra - rb;
            float a2 = l2 - (rr * rr);
            float il2 = 1f / l2;
            return p =>
            {
                var pa = p - a;
                float y = Vector3.Dot(pa, ba);
                float z = y - l2;
                var xv = (pa * l2) - (ba * y);
                float x2 = Vector3.Dot(xv, xv);
                float y2 = y * y * l2;
                float z2 = z * z * l2;
                float k = MathF.Sign(rr) * rr * rr * x2;
                if (MathF.Sign(z) * a2 * z2 > k) return (MathF.Sqrt(x2 + z2) * il2) - rb;
                if (MathF.Sign(y) * a2 * y2 < k) return (MathF.Sqrt(x2 + y2) * il2) - ra;
                return ((MathF.Sqrt(x2 * a2 * il2) + (y * rr)) * il2) - ra;
            };
        }

        /// <summary>Chain of round cones through points with radii (tapered tubes, fingers, straps).</summary>
        public static Sdf Chain(IList<Vector3> points, IList<float> radii)
        {
            var parts = new List<Sdf>();
            float minRadius = float.MaxValue;
            for (int i = 0; i < points.Count - 1; i++)
            {
                parts.Add(RoundCone(points[i], points[i + 1], radii[i], radii[i + 1]));
                minRadius = MathF.Min(minRadius, MathF.Min(radii[i], radii[i + 1]));
            }

            // Smooth joints (no creases where segments meet).
            return parts.Count == 1 ? parts[0] : Blend(minRadius * 0.5f, parts.ToArray());
        }

        /// <summary>Chain with elliptical cross-section: scaled by <paramref name="squash"/> along <paramref name="axis"/> (≤ 1 flattens).</summary>
        public static Sdf Squashed(Sdf shape, Vector3 center, Vector3 axis, float squash)
        {
            var n = Vector3.Normalize(axis);
            return p =>
            {
                var d = p - center;
                float along = Vector3.Dot(d, n);
                var q = center + d + (n * (along * ((1f / squash) - 1f)));
                return shape(q) * MathF.Max(squash, 0.3f);
            };
        }

        /// <summary>Rounded box with half extents <paramref name="half"/> (including the rounding radius).</summary>
        public static Sdf RoundBox(Vector3 c, Vector3 half, float radius, Rot? rotation = null)
        {
            var inv = (rotation ?? Rot.Identity).Transposed;
            var b = half - new Vector3(radius);
            return p =>
            {
                var q = Vector3.Abs(inv * (p - c)) - b;
                return Vector3.Max(q, Vector3.Zero).Length() + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0f) - radius;
            };
        }

        /// <summary>Torus around <paramref name="axis"/> with major radius <paramref name="major"/> and tube radius <paramref name="minor"/>.</summary>
        public static Sdf Torus(Vector3 c, Vector3 axis, float major, float minor)
        {
            var n = Vector3.Normalize(axis);
            return p =>
            {
                var d = p - c;
                float h = Vector3.Dot(d, n);
                float r = (d - (n * h)).Length() - major;
                return MathF.Sqrt((r * r) + (h * h)) - minor;
            };
        }

        /// <summary>Half space: negative on the side the normal points away from (dot(p - point, normal) &lt; 0).</summary>
        public static Sdf Plane(Vector3 point, Vector3 normal)
        {
            var n = Vector3.Normalize(normal);
            return p => Vector3.Dot(p - point, n);
        }

        public static Sdf Union(params Sdf[] shapes) => p =>
        {
            float d = float.MaxValue;
            foreach (var s in shapes) d = MathF.Min(d, s(p));
            return d;
        };

        /// <summary>Smooth union of all shapes with blend radius <paramref name="k"/>.</summary>
        public static Sdf Blend(float k, params Sdf[] shapes) => p =>
        {
            float d = shapes[0](p);
            for (int i = 1; i < shapes.Length; i++) d = SMin(d, shapes[i](p), k);
            return d;
        };

        public static Sdf Subtract(Sdf shape, Sdf cut) => p => MathF.Max(shape(p), -cut(p));

        public static Sdf SmoothSubtract(Sdf shape, Sdf cut, float k) => p => -SMin(-shape(p), cut(p), k);

        public static Sdf Intersect(Sdf a, Sdf b) => p => MathF.Max(a(p), b(p));

        public static Sdf SmoothIntersect(Sdf a, Sdf b, float k) => p => -SMin(-a(p), -b(p), k);

        /// <summary>Offset surface (positive grows the shape).</summary>
        public static Sdf Grow(Sdf shape, float amount) => p => shape(p) - amount;

        /// <summary>Hollow shell of the given thickness around the surface.</summary>
        public static Sdf Shell(Sdf shape, float thickness) => p => MathF.Abs(shape(p)) - (thickness / 2f);

        /// <summary>Evaluates <paramref name="shape"/> in a transformed frame (shape modelled around the origin).</summary>
        public static Sdf Transformed(Sdf shape, Vector3 position, Rot rotation)
        {
            var inv = rotation.Transposed;
            return p => shape(inv * (p - position));
        }

        public static Sdf MirrorX(Sdf shape) => p => shape(new Vector3(MathF.Abs(p.X), p.Y, p.Z));

        /// <summary>Polynomial smooth minimum (blend radius k).</summary>
        public static float SMin(float a, float b, float k)
        {
            if (k <= 0f) return MathF.Min(a, b);
            float h = MathF.Max(k - MathF.Abs(a - b), 0f) / k;
            return MathF.Min(a, b) - (h * h * k * 0.25f);
        }
    }
}
