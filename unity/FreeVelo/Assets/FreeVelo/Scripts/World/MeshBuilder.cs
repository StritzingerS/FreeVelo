using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FreeVelo.World
{
    /// <summary>
    /// Builds low-poly meshes from simple shapes (tubes, ellipsoids, tori, boxes).
    /// All triangles are wound clockwise as seen from outside (Unity front faces).
    /// </summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();

        public int VertexCount => _vertices.Count;

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(_vertices);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Tapered tube from <paramref name="a"/> (radius <paramref name="ra"/>) to <paramref name="b"/>.</summary>
        public void Tube(Vector3 a, Vector3 b, float ra, float rb, int sides = 8, bool caps = true)
        {
            Vector3 axis = b - a;
            float length = axis.magnitude;
            if (length < 1e-5f) return;
            Vector3 dir = axis / length;
            Basis(dir, out var u, out var w);

            int start = _vertices.Count;
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                Vector3 offset = (u * Mathf.Cos(angle)) + (w * Mathf.Sin(angle));
                _vertices.Add(a + (offset * ra));
                _vertices.Add(b + (offset * rb));
            }

            for (int i = 0; i < sides; i++)
            {
                int k = start + (i * 2);
                Tri(k, k + 2, k + 1);
                Tri(k + 1, k + 2, k + 3);
            }

            if (!caps) return;

            // Separate cap vertices so the side normals stay smooth and the caps flat.
            int ca = _vertices.Count;
            _vertices.Add(a);
            int cb = _vertices.Count;
            _vertices.Add(b);
            int ringA = _vertices.Count;
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                Vector3 offset = (u * Mathf.Cos(angle)) + (w * Mathf.Sin(angle));
                _vertices.Add(a + (offset * ra));
                _vertices.Add(b + (offset * rb));
            }

            for (int i = 0; i < sides; i++)
            {
                int k = ringA + (i * 2);
                Tri(ca, k + 2, k);
                Tri(cb, k + 1, k + 3);
            }
        }

        /// <summary>
        /// Sweeps an elliptical cross-section along <paramref name="centers"/>. radii[i].x lies along
        /// <paramref name="side"/> (projected perpendicular to the path), radii[i].y perpendicular to both.
        /// Used for organic shapes (limbs, torso, saddle) and aero tubes.
        /// </summary>
        public void Sweep(IList<Vector3> centers, IList<Vector2> radii, Vector3 side, int sides = 12, bool capStart = true, bool capEnd = true)
        {
            int n = centers.Count;
            if (n < 2) return;
            var us = new Vector3[n];
            var ws = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = (centers[Mathf.Min(n - 1, i + 1)] - centers[Mathf.Max(0, i - 1)]).normalized;
                Vector3 u = side - (Vector3.Dot(side, dir) * dir);
                if (u.sqrMagnitude < 1e-8f) Basis(dir, out u, out _);
                u.Normalize();
                us[i] = u;
                ws[i] = Vector3.Cross(dir, u);
            }

            int start = _vertices.Count;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j <= sides; j++)
                {
                    float angle = j * Mathf.PI * 2f / sides;
                    _vertices.Add(centers[i] + (us[i] * (Mathf.Cos(angle) * radii[i].x)) + (ws[i] * (Mathf.Sin(angle) * radii[i].y)));
                }
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

            if (capStart) Cap(centers[0], us[0], ws[0], radii[0], sides, reverse: false);
            if (capEnd) Cap(centers[n - 1], us[n - 1], ws[n - 1], radii[n - 1], sides, reverse: true);
        }

        /// <summary>Straight tube with elliptical ends (e.g. aero frame tubes).</summary>
        public void EllipticTube(Vector3 a, Vector3 b, Vector2 radiiA, Vector2 radiiB, Vector3 side, int sides = 12) =>
            Sweep(new[] { a, b }, new[] { radiiA, radiiB }, side, sides);

        private void Cap(Vector3 center, Vector3 u, Vector3 w, Vector2 r, int sides, bool reverse)
        {
            int c = _vertices.Count;
            _vertices.Add(center);
            int ring = _vertices.Count;
            for (int j = 0; j <= sides; j++)
            {
                float angle = j * Mathf.PI * 2f / sides;
                _vertices.Add(center + (u * (Mathf.Cos(angle) * r.x)) + (w * (Mathf.Sin(angle) * r.y)));
            }

            for (int j = 0; j < sides; j++)
            {
                if (reverse) Tri(c, ring + j, ring + j + 1);
                else Tri(c, ring + j + 1, ring + j);
            }
        }

        /// <summary>Tube through several points.</summary>
        public void Path(Vector3[] points, float radius, int sides = 8)
        {
            for (int i = 0; i < points.Length - 1; i++) Tube(points[i], points[i + 1], radius, radius, sides, false);
            for (int i = 1; i < points.Length - 1; i++) Ellipsoid(points[i], Vector3.one * radius, Quaternion.identity, sides, 4);
        }

        /// <summary>Ellipsoid (or upper half of it) with local radii, rotated by <paramref name="rotation"/>.</summary>
        public void Ellipsoid(Vector3 center, Vector3 radii, Quaternion rotation, int slices = 12, int stacks = 8, bool upperHalf = false)
        {
            int start = _vertices.Count;
            float maxTheta = upperHalf ? Mathf.PI / 2f : Mathf.PI;
            for (int i = 0; i <= stacks; i++)
            {
                float theta = maxTheta * i / stacks;
                for (int j = 0; j <= slices; j++)
                {
                    float phi = Mathf.PI * 2f * j / slices;
                    var local = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    _vertices.Add(center + (rotation * Vector3.Scale(local, radii)));
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

        /// <summary>Torus around <paramref name="axis"/>; tube cross-section is an ellipse (radial × axial).</summary>
        public void Torus(Vector3 center, Vector3 axis, float majorRadius, float radialRadius, float axialRadius, int segments = 32, int sides = 8)
        {
            Vector3 n = axis.normalized;
            Basis(n, out var u, out var w);
            int start = _vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float alpha = Mathf.PI * 2f * i / segments;
                Vector3 d = (u * Mathf.Cos(alpha)) + (w * Mathf.Sin(alpha));
                for (int j = 0; j <= sides; j++)
                {
                    float beta = Mathf.PI * 2f * j / sides;
                    _vertices.Add(center + (d * (majorRadius + (radialRadius * Mathf.Cos(beta)))) + (n * (axialRadius * Mathf.Sin(beta))));
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

        /// <summary>Box with full <paramref name="size"/>, rotated around its centre.</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 h = size / 2f;
            Face(center, rotation, h, Vector3.right, Vector3.up);
            Face(center, rotation, h, Vector3.left, Vector3.up);
            Face(center, rotation, h, Vector3.forward, Vector3.up);
            Face(center, rotation, h, Vector3.back, Vector3.up);
            Face(center, rotation, h, Vector3.up, Vector3.forward);
            Face(center, rotation, h, Vector3.down, Vector3.forward);
        }

        /// <summary>Body part: tapered tube with rounded ends.</summary>
        public void Limb(Vector3 a, Vector3 b, float ra, float rb, int sides = 10)
        {
            Tube(a, b, ra, rb, sides, false);
            Ellipsoid(a, Vector3.one * ra, Quaternion.identity, sides, 6);
            Ellipsoid(b, Vector3.one * rb, Quaternion.identity, sides, 6);
        }

        private void Face(Vector3 center, Quaternion rotation, Vector3 half, Vector3 n, Vector3 up)
        {
            // Viewer outside looks along -n; right = Cross(up, forward) as in Unity.
            Vector3 r = Vector3.Cross(up, -n);
            float hn = Vector3.Scale(Abs(n), half).magnitude;
            float hr = Vector3.Scale(Abs(r), half).magnitude;
            float hu = Vector3.Scale(Abs(up), half).magnitude;
            Vector3 c = (n * hn);
            int start = _vertices.Count;
            _vertices.Add(center + (rotation * (c - (r * hr) + (up * hu))));
            _vertices.Add(center + (rotation * (c + (r * hr) + (up * hu))));
            _vertices.Add(center + (rotation * (c + (r * hr) - (up * hu))));
            _vertices.Add(center + (rotation * (c - (r * hr) - (up * hu))));
            Tri(start, start + 1, start + 2);
            Tri(start, start + 2, start + 3);
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static void Basis(Vector3 dir, out Vector3 u, out Vector3 w)
        {
            Vector3 helper = Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right;
            u = Vector3.Cross(dir, helper).normalized;
            w = Vector3.Cross(dir, u);
        }

        private void Tri(int a, int b, int c)
        {
            _triangles.Add(a);
            _triangles.Add(b);
            _triangles.Add(c);
        }
    }
}
