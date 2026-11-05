using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FreeVelo.Core.Avatar
{
    /// <summary>One cross-section of a sweep (see MeshBuilder.Sweep).</summary>
    public readonly struct Ring
    {
        public Ring(Vector3 center, float right, float left, float up, float down, float power = 2f)
        {
            Center = center;
            Right = right;
            Left = left;
            Up = up;
            Down = down;
            Power = power;
        }

        public Vector3 Center { get; }

        /// <summary>Radius towards +side.</summary>
        public float Right { get; }

        /// <summary>Radius towards −side.</summary>
        public float Left { get; }

        /// <summary>Radius towards +w (w = path direction × side).</summary>
        public float Up { get; }

        /// <summary>Radius towards −w.</summary>
        public float Down { get; }

        /// <summary>Superellipse exponent: 2 = ellipse, &gt;2 = rounded square, &lt;2 = diamond.</summary>
        public float Power { get; }

        /// <summary>Symmetric ellipse.</summary>
        public static Ring E(Vector3 center, float side, float other, float power = 2f) => new Ring(center, side, side, other, other, power);

        public static Ring E(float x, float y, float z, float side, float other, float power = 2f) => E(new Vector3(x, y, z), side, other, power);

        /// <summary>
        /// Catmull-Rom interpolation of centres and radii: <paramref name="steps"/> rings per segment, so a few
        /// control rings give a smooth organic shape.
        /// </summary>
        public static Ring[] Spline(IList<Ring> rings, int steps = 4)
        {
            int n = rings.Count;
            if (n < 3 || steps < 2) return rings.ToArray();
            var result = new List<Ring>();
            for (int i = 0; i < n - 1; i++)
            {
                var p0 = rings[Math.Max(0, i - 1)];
                var p1 = rings[i];
                var p2 = rings[i + 1];
                var p3 = rings[Math.Min(n - 1, i + 2)];
                for (int k = 0; k < steps; k++)
                {
                    float t = k / (float)steps;
                    result.Add(new Ring(
                        CR(p0.Center, p1.Center, p2.Center, p3.Center, t),
                        MathF.Max(1e-4f, CR(p0.Right, p1.Right, p2.Right, p3.Right, t)),
                        MathF.Max(1e-4f, CR(p0.Left, p1.Left, p2.Left, p3.Left, t)),
                        MathF.Max(1e-4f, CR(p0.Up, p1.Up, p2.Up, p3.Up, t)),
                        MathF.Max(1e-4f, CR(p0.Down, p1.Down, p2.Down, p3.Down, t)),
                        p1.Power + ((p2.Power - p1.Power) * t)));
                }
            }

            result.Add(rings[n - 1]);
            return result.ToArray();
        }

        private static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) =>
            0.5f * ((2f * p1) + ((p2 - p0) * t) + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t * t) + (((3f * p1) - p0 - (3f * p2) + p3) * t * t * t));

        private static float CR(float p0, float p1, float p2, float p3, float t) =>
            0.5f * ((2f * p1) + ((p2 - p0) * t) + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t * t) + (((3f * p1) - p0 - (3f * p2) + p3) * t * t * t));

        public Ring Scaled(float f) => new Ring(Center, Right * f, Left * f, Up * f, Down * f, Power);

        public static Ring Lerp(Ring a, Ring b, float t) => new Ring(
            Vector3.Lerp(a.Center, b.Center, t),
            a.Right + ((b.Right - a.Right) * t),
            a.Left + ((b.Left - a.Left) * t),
            a.Up + ((b.Up - a.Up) * t),
            a.Down + ((b.Down - a.Down) * t),
            a.Power + ((b.Power - a.Power) * t));
    }

    /// <summary>Plain mesh data (no engine types): vertices and triangle indices.</summary>
    public sealed class MeshData
    {
        public List<Vector3> Vertices { get; } = new List<Vector3>();

        public List<int> Triangles { get; } = new List<int>();

        /// <summary>Explicit normals parallel to <see cref="Vertices"/> (zero = derive from the triangles).</summary>
        public List<Vector3> Normals { get; } = new List<Vector3>();

        public int TriangleCount => Triangles.Count / 3;

        /// <summary>
        /// Area-weighted vertex normals. Vertices at the same position (seams of sweeps and spheres) are
        /// welded when their normals differ by less than <paramref name="smoothingAngle"/> degrees, so seams are
        /// invisible while real edges (caps, boxes) stay sharp.
        /// </summary>
        public Vector3[] ComputeNormals(float smoothingAngle = 60f)
        {
            var own = new Vector3[Vertices.Count];
            for (int i = 0; i < Triangles.Count; i += 3)
            {
                int a = Triangles[i], b = Triangles[i + 1], c = Triangles[i + 2];
                var n = Vector3.Cross(Vertices[b] - Vertices[a], Vertices[c] - Vertices[a]);
                own[a] += n;
                own[b] += n;
                own[c] += n;
            }

            for (int i = 0; i < own.Length; i++)
            {
                float l = own[i].Length();
                own[i] = l > 1e-20f ? own[i] / l : Vector3.UnitY;
            }

            var groups = new Dictionary<(int, int, int), List<int>>();
            for (int i = 0; i < Vertices.Count; i++)
            {
                var v = Vertices[i];
                var key = ((int)MathF.Round(v.X * 1e5f), (int)MathF.Round(v.Y * 1e5f), (int)MathF.Round(v.Z * 1e5f));
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>();
                list.Add(i);
            }

            float cos = MathF.Cos(smoothingAngle * MathF.PI / 180f);
            var normals = new Vector3[Vertices.Count];
            foreach (var list in groups.Values)
            {
                foreach (int i in list)
                {
                    var sum = Vector3.Zero;
                    foreach (int j in list)
                    {
                        if (Vector3.Dot(own[i], own[j]) >= cos) sum += own[j];
                    }

                    float l = sum.Length();
                    normals[i] = l > 1e-12f ? sum / l : own[i];
                }
            }

            if (Normals.Count == Vertices.Count)
            {
                for (int i = 0; i < normals.Length; i++)
                {
                    if (Normals[i] != Vector3.Zero) normals[i] = Normals[i];
                }
            }

            return normals;
        }
    }

    /// <summary>
    /// Builds low-poly meshes from simple shapes. Triangles are wound clockwise as seen from outside
    /// (Unity's front faces); Cross(b − a, c − a) points outwards.
    /// </summary>
    public sealed class MeshBuilder
    {
        private readonly MeshData _mesh = new MeshData();
        private Rot _rot = Rot.Identity;
        private Vector3 _pos = Vector3.Zero;

        public MeshData Mesh => _mesh;

        public int VertexCount => _mesh.Vertices.Count;

        /// <summary>Transform applied to everything added afterwards (e.g. to place a part).</summary>
        public void SetTransform(Vector3 position, Rot rotation)
        {
            _pos = position;
            _rot = rotation;
        }

        public void ResetTransform() => SetTransform(Vector3.Zero, Rot.Identity);

        private void Add(Vector3 v)
        {
            _mesh.Vertices.Add((_rot * v) + _pos);
            _mesh.Normals.Add(Vector3.Zero);
        }

        private void Tri(int a, int b, int c)
        {
            _mesh.Triangles.Add(a);
            _mesh.Triangles.Add(b);
            _mesh.Triangles.Add(c);
        }

        internal static void Basis(Vector3 dir, out Vector3 u, out Vector3 w)
        {
            var helper = MathF.Abs(dir.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
            u = Vector3.Normalize(Vector3.Cross(dir, helper));
            w = Vector3.Cross(dir, u);
        }

        /// <summary>
        /// Sweeps cross-sections along a path. Ring radii: Right/Left along <paramref name="side"/> (projected
        /// perpendicular to the path), Up/Down along w = direction × side.
        /// </summary>
        public void Sweep(IList<Ring> rings, Vector3 side, int sides = 16, bool capStart = true, bool capEnd = true)
        {
            int n = rings.Count;
            if (n < 2) return;
            var us = new Vector3[n];
            var ws = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var dir = Vector3.Normalize(rings[Math.Min(n - 1, i + 1)].Center - rings[Math.Max(0, i - 1)].Center);
                var u = side - (Vector3.Dot(side, dir) * dir);
                if (u.LengthSquared() < 1e-10f) Basis(dir, out u, out _);
                u = Vector3.Normalize(u);
                us[i] = u;
                ws[i] = Vector3.Cross(dir, u);
            }

            int start = VertexCount;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j <= sides; j++) Add(RingPoint(rings[i], us[i], ws[i], j, sides));
            }

            int row = sides + 1;
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = start + (i * row) + j;
                    int b = a + 1;
                    int c = a + row;
                    int d = c + 1;
                    Tri(a, b, c);
                    Tri(c, b, d);
                }
            }

            if (capStart) Cap(rings[0], us[0], ws[0], sides, reverse: false);
            if (capEnd) Cap(rings[n - 1], us[n - 1], ws[n - 1], sides, reverse: true);
        }

        /// <summary>Sweep through a Catmull-Rom spline of the control rings.</summary>
        public void SmoothSweep(IList<Ring> rings, Vector3 side, int sides = 16, int steps = 4, bool capStart = true, bool capEnd = true) =>
            Sweep(Ring.Spline(rings, steps), side, sides, capStart, capEnd);

        /// <summary>Smooth elliptical sweep (symmetric radii) – shorthand.</summary>
        public void SmoothSweep(IList<Vector3> centers, IList<Vector2> radii, Vector3 side, int sides = 16, int steps = 4, bool capStart = true, bool capEnd = true)
        {
            var rings = new Ring[centers.Count];
            for (int i = 0; i < rings.Length; i++) rings[i] = Ring.E(centers[i], radii[i].X, radii[i].Y);
            SmoothSweep(rings, side, sides, steps, capStart, capEnd);
        }

        /// <summary>Elliptical sweep (symmetric radii) – shorthand.</summary>
        public void Sweep(IList<Vector3> centers, IList<Vector2> radii, Vector3 side, int sides = 16, bool capStart = true, bool capEnd = true)
        {
            var rings = new Ring[centers.Count];
            for (int i = 0; i < rings.Length; i++) rings[i] = Ring.E(centers[i], radii[i].X, radii[i].Y);
            Sweep(rings, side, sides, capStart, capEnd);
        }

        private static Vector3 RingPoint(Ring r, Vector3 u, Vector3 w, int j, int sides)
        {
            float angle = j * MathF.PI * 2f / sides;
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            float e = 2f / r.Power;
            float cx = MathF.Sign(c) * MathF.Pow(MathF.Abs(c), e);
            float sy = MathF.Sign(s) * MathF.Pow(MathF.Abs(s), e);
            float rx = c >= 0 ? r.Right : r.Left;
            float ry = s >= 0 ? r.Up : r.Down;
            return r.Center + (u * (cx * rx)) + (w * (sy * ry));
        }

        private void Cap(Ring r, Vector3 u, Vector3 w, int sides, bool reverse)
        {
            int c = VertexCount;
            Add(r.Center);
            int ring = VertexCount;
            for (int j = 0; j <= sides; j++) Add(RingPoint(r, u, w, j, sides));
            for (int j = 0; j < sides; j++)
            {
                if (reverse) Tri(c, ring + j, ring + j + 1);
                else Tri(c, ring + j + 1, ring + j);
            }
        }

        /// <summary>Tapered round tube.</summary>
        public void Tube(Vector3 a, Vector3 b, float ra, float rb, int sides = 10, bool caps = true)
        {
            var dir = Vector3.Normalize(b - a);
            Basis(dir, out var u, out _);
            Sweep(new[] { Ring.E(a, ra, ra), Ring.E(b, rb, rb) }, u, sides, caps, caps);
        }

        /// <summary>Straight tube with elliptical ends.</summary>
        public void EllipticTube(Vector3 a, Vector3 b, Vector2 radiiA, Vector2 radiiB, Vector3 side, int sides = 14) =>
            Sweep(new[] { Ring.E(a, radiiA.X, radiiA.Y), Ring.E(b, radiiB.X, radiiB.Y) }, side, sides);

        /// <summary>Round tube through several points with rounded joints.</summary>
        public void Path(IList<Vector3> points, float radius, int sides = 10)
        {
            var rings = new Ring[points.Count];
            for (int i = 0; i < rings.Length; i++) rings[i] = Ring.E(points[i], radius, radius);
            Sweep(rings, PerpendicularSide(points), sides);
        }

        private static Vector3 PerpendicularSide(IList<Vector3> points)
        {
            // A side vector that is not parallel to any segment (for a stable sweep frame).
            var total = points[points.Count - 1] - points[0];
            foreach (var candidate in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            {
                bool ok = true;
                for (int i = 0; i < points.Count - 1 && ok; i++)
                {
                    var d = Vector3.Normalize(points[i + 1] - points[i]);
                    if (MathF.Abs(Vector3.Dot(d, candidate)) > 0.95f) ok = false;
                }

                if (ok) return candidate;
            }

            Basis(Vector3.Normalize(total), out var u, out _);
            return u;
        }

        /// <summary>Ellipsoid (or its upper half), radii in local axes, rotated by <paramref name="rotation"/>.</summary>
        public void Ellipsoid(Vector3 center, Vector3 radii, Rot rotation, int slices = 14, int stacks = 10, bool upperHalf = false)
        {
            int start = VertexCount;
            float maxTheta = upperHalf ? MathF.PI / 2f : MathF.PI;
            for (int i = 0; i <= stacks; i++)
            {
                float theta = maxTheta * i / stacks;
                for (int j = 0; j <= slices; j++)
                {
                    float phi = MathF.PI * 2f * j / slices;
                    var local = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Sin(phi));
                    Add(center + (rotation * (local * radii)));
                }
            }

            int row = slices + 1;
            for (int i = 0; i < stacks; i++)
            {
                for (int j = 0; j < slices; j++)
                {
                    int a = start + (i * row) + j;
                    int b = a + row;
                    int c = a + 1;
                    int d = b + 1;
                    Tri(a, c, b);
                    Tri(c, d, b);
                }
            }
        }

        public void Sphere(Vector3 center, float radius, int slices = 12, int stacks = 8) =>
            Ellipsoid(center, new Vector3(radius), Rot.Identity, slices, stacks);

        /// <summary>Torus around <paramref name="axis"/>; cross-section is an ellipse (radial × axial).</summary>
        public void Torus(Vector3 center, Vector3 axis, float majorRadius, float radialRadius, float axialRadius, int segments = 32, int sides = 8)
        {
            var n = Vector3.Normalize(axis);
            Basis(n, out var u, out var w);
            int start = VertexCount;
            for (int i = 0; i <= segments; i++)
            {
                float alpha = MathF.PI * 2f * i / segments;
                var d = (u * MathF.Cos(alpha)) + (w * MathF.Sin(alpha));
                for (int j = 0; j <= sides; j++)
                {
                    float beta = MathF.PI * 2f * j / sides;
                    Add(center + (d * (majorRadius + (radialRadius * MathF.Cos(beta)))) + (n * (axialRadius * MathF.Sin(beta))));
                }
            }

            int row = sides + 1;
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = start + (i * row) + j;
                    int b = a + row;
                    int c = a + 1;
                    int d = b + 1;
                    Tri(a, b, c);
                    Tri(b, d, c);
                }
            }
        }

        /// <summary>Flat disc (both faces) around <paramref name="axis"/> between two radii (annulus).</summary>
        public void Disc(Vector3 center, Vector3 axis, float innerRadius, float outerRadius, float thickness, int segments = 32)
        {
            // A torus with a rectangular-ish cross section.
            Torus(center, axis, (innerRadius + outerRadius) / 2f, (outerRadius - innerRadius) / 2f, thickness / 2f, segments, 4);
        }

        /// <summary>Box with full <paramref name="size"/>, rotated around its centre.</summary>
        public void Box(Vector3 center, Vector3 size, Rot rotation)
        {
            var h = size / 2f;
            Face(center, rotation, h, Vector3.UnitX, Vector3.UnitY);
            Face(center, rotation, h, -Vector3.UnitX, Vector3.UnitY);
            Face(center, rotation, h, Vector3.UnitZ, Vector3.UnitY);
            Face(center, rotation, h, -Vector3.UnitZ, Vector3.UnitY);
            Face(center, rotation, h, Vector3.UnitY, Vector3.UnitZ);
            Face(center, rotation, h, -Vector3.UnitY, Vector3.UnitZ);
        }

        private void Face(Vector3 center, Rot rotation, Vector3 half, Vector3 n, Vector3 up)
        {
            var r = Vector3.Cross(up, -n);
            float hn = (Abs(n) * half).Length();
            float hr = (Abs(r) * half).Length();
            float hu = (Abs(up) * half).Length();
            var c = n * hn;
            int start = VertexCount;
            Add(center + (rotation * (c - (r * hr) + (up * hu))));
            Add(center + (rotation * (c + (r * hr) + (up * hu))));
            Add(center + (rotation * (c + (r * hr) - (up * hu))));
            Add(center + (rotation * (c - (r * hr) - (up * hu))));
            Tri(start, start + 1, start + 2);
            Tri(start, start + 2, start + 3);
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));

        /// <summary>Tapered tube with rounded ends (limbs, fingers).</summary>
        public void Limb(Vector3 a, Vector3 b, float ra, float rb, int sides = 10)
        {
            Tube(a, b, ra, rb, sides, false);
            Sphere(a, ra, sides, Math.Max(4, sides / 2));
            Sphere(b, rb, sides, Math.Max(4, sides / 2));
        }

        /// <summary>Appends another mesh (already in this builder's space).</summary>
        public void Append(MeshData other)
        {
            int offset = VertexCount;
            for (int i = 0; i < other.Vertices.Count; i++)
            {
                _mesh.Vertices.Add((_rot * other.Vertices[i]) + _pos);
                _mesh.Normals.Add(i < other.Normals.Count ? _rot * other.Normals[i] : Vector3.Zero);
            }

            foreach (int t in other.Triangles) _mesh.Triangles.Add(t + offset);
        }
    }
}
