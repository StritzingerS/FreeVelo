using System;
using System.Collections.Generic;
using System.Numerics;

namespace FreeVelo.Core.Avatar
{
    /// <summary>
    /// Quadric error mesh simplification (Garland–Heckbert edge collapses) for the closed meshes produced by the
    /// polygonizer. Rejects collapses that flip faces or break the manifold; afterwards vertices can be projected
    /// back onto the source surface and normals re-evaluated from it.
    /// </summary>
    public static class Decimator
    {
        public static MeshData Simplify(MeshData mesh, int targetTriangles, float maxError, Sdf? surface = null)
        {
            int vc = mesh.Vertices.Count;
            var pos = mesh.Vertices.ToArray();
            var tris = mesh.Triangles.ToArray();
            int tc = tris.Length / 3;
            var triAlive = new bool[tc];
            var vAlive = new bool[vc];
            var version = new int[vc];
            var q = new double[vc * 10];
            var adj = new List<int>[vc];
            for (int i = 0; i < vc; i++) adj[i] = new List<int>(8);

            for (int t = 0; t < tc; t++)
            {
                triAlive[t] = true;
                int a = tris[3 * t], b = tris[(3 * t) + 1], c = tris[(3 * t) + 2];
                vAlive[a] = vAlive[b] = vAlive[c] = true;
                adj[a].Add(t);
                adj[b].Add(t);
                adj[c].Add(t);
                var n = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                float area = n.Length();
                if (area < 1e-14f) continue;
                n /= area;
                double nx = n.X, ny = n.Y, nz = n.Z, d = -Vector3.Dot(n, pos[a]);
                double w = area; // area weighting keeps big flat regions cheap to simplify
                foreach (int v in new[] { a, b, c })
                {
                    int o = v * 10;
                    q[o] += w * nx * nx;
                    q[o + 1] += w * nx * ny;
                    q[o + 2] += w * nx * nz;
                    q[o + 3] += w * nx * d;
                    q[o + 4] += w * ny * ny;
                    q[o + 5] += w * ny * nz;
                    q[o + 6] += w * ny * d;
                    q[o + 7] += w * nz * nz;
                    q[o + 8] += w * nz * d;
                    q[o + 9] += w * d * d;
                }
            }

            var heap = new EdgeHeap();
            double maxCost = (double)maxError * maxError;

            void PushEdge(int a, int b)
            {
                var p = Optimal(q, pos, a, b, out double cost);
                if (cost <= maxCost) heap.Push(cost, a, b, version[a], version[b], p);
            }

            for (int t = 0; t < tc; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = tris[(3 * t) + k], b = tris[(3 * t) + ((k + 1) % 3)];
                    if (a < b) PushEdge(a, b);
                }
            }

            int alive = tc;
            var neighborsA = new HashSet<int>();
            var neighborsB = new HashSet<int>();
            while (alive > targetTriangles && heap.Count > 0)
            {
                heap.Pop(out int a, out int b, out int va, out int vb, out var target);
                if (!vAlive[a] || !vAlive[b] || version[a] != va || version[b] != vb) continue;

                // Link condition: exactly two shared neighbours (closed manifold).
                Neighbors(adj[a], tris, triAlive, a, neighborsA);
                Neighbors(adj[b], tris, triAlive, b, neighborsB);
                if (!neighborsA.Contains(b)) continue;
                int shared = 0;
                foreach (int n in neighborsA)
                {
                    if (neighborsB.Contains(n)) shared++;
                }

                if (shared != 2) continue;
                if (Flips(adj[a], tris, triAlive, pos, a, b, target) || Flips(adj[b], tris, triAlive, pos, b, a, target)) continue;

                // Collapse b into a.
                pos[a] = target;
                for (int k = 0; k < 10; k++) q[(a * 10) + k] += q[(b * 10) + k];
                foreach (int t in adj[b])
                {
                    if (!triAlive[t]) continue;
                    int o = 3 * t;
                    if (tris[o] == a || tris[o + 1] == a || tris[o + 2] == a)
                    {
                        triAlive[t] = false;
                        alive--;
                        continue;
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        if (tris[o + k] == b) tris[o + k] = a;
                    }

                    adj[a].Add(t);
                }

                adj[a].RemoveAll(t => !triAlive[t]);
                adj[b].Clear();
                vAlive[b] = false;
                version[a]++;
                Neighbors(adj[a], tris, triAlive, a, neighborsA);
                foreach (int n in neighborsA)
                {
                    version[n]++;
                }

                foreach (int n in neighborsA)
                {
                    // Neighbours' other edges keep their (now stale) entries; re-push the edges touching them.
                    foreach (int t in adj[n])
                    {
                        if (!triAlive[t]) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int u = tris[(3 * t) + k], v = tris[(3 * t) + ((k + 1) % 3)];
                            if (u < v && (u == n || v == n)) PushEdge(u, v);
                        }
                    }
                }
            }

            // Compact.
            var result = new MeshData();
            var map = new int[vc];
            Array.Fill(map, -1);
            for (int t = 0; t < tc; t++)
            {
                if (!triAlive[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[(3 * t) + k];
                    if (map[v] < 0)
                    {
                        map[v] = result.Vertices.Count;
                        var p = pos[v];
                        if (surface != null) p = Project(surface, p);
                        result.Vertices.Add(p);
                    }

                    result.Triangles.Add(map[v]);
                }
            }

            if (surface != null)
            {
                foreach (var p in result.Vertices)
                {
                    var g = Polygonizer.Gradient(surface, p, 0.0002f);
                    float l = g.Length();
                    result.Normals.Add(l > 1e-12f ? g / l : Vector3.UnitY);
                }
            }

            return result;
        }

        private static Vector3 Project(Sdf f, Vector3 p)
        {
            var start = p;
            for (int i = 0; i < 3; i++)
            {
                float d = f(p);
                var g = Polygonizer.Gradient(f, p, 0.0002f);
                float g2 = g.LengthSquared();
                if (g2 < 1e-12f) break;
                p -= g * (d / g2);
            }

            return Vector3.Distance(p, start) < 0.004f ? p : start;
        }

        private static void Neighbors(List<int> faces, int[] tris, bool[] alive, int v, HashSet<int> result)
        {
            result.Clear();
            foreach (int t in faces)
            {
                if (!alive[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int u = tris[(3 * t) + k];
                    if (u != v) result.Add(u);
                }
            }
        }

        private static bool Flips(List<int> faces, int[] tris, bool[] alive, Vector3[] pos, int v, int other, Vector3 p)
        {
            foreach (int t in faces)
            {
                if (!alive[t]) continue;
                int o = 3 * t;
                int a = tris[o], b = tris[o + 1], c = tris[o + 2];
                if (a == other || b == other || c == other) continue; // removed by the collapse
                var before = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                var pa = a == v ? p : pos[a];
                var pb = b == v ? p : pos[b];
                var pc = c == v ? p : pos[c];
                var after = Vector3.Cross(pb - pa, pc - pa);
                float la = after.Length();
                if (la < 1e-14f) return true;
                if (Vector3.Dot(before, after) < 0.3f * before.Length() * la) return true;

                // Avoid slivers.
                float e = MathF.Max((pb - pa).LengthSquared(), MathF.Max((pc - pb).LengthSquared(), (pa - pc).LengthSquared()));
                if (la < 0.04f * e) return true;
            }

            return false;
        }

        private static Vector3 Optimal(double[] q, Vector3[] pos, int a, int b, out double cost)
        {
            var m = new double[10];
            for (int k = 0; k < 10; k++) m[k] = q[(a * 10) + k] + q[(b * 10) + k];
            double a11 = m[0], a12 = m[1], a13 = m[2], a22 = m[4], a23 = m[5], a33 = m[7];
            double b1 = -m[3], b2 = -m[6], b3 = -m[8];
            double det = (a11 * ((a22 * a33) - (a23 * a23))) - (a12 * ((a12 * a33) - (a23 * a13))) + (a13 * ((a12 * a23) - (a22 * a13)));
            var mid = (pos[a] + pos[b]) / 2f;
            Vector3 best = mid;
            cost = double.MaxValue;
            double scale = Math.Abs(a11) + Math.Abs(a22) + Math.Abs(a33);
            if (Math.Abs(det) > 1e-12 * scale * scale * scale && scale > 0)
            {
                double x = ((b1 * ((a22 * a33) - (a23 * a23))) - (a12 * ((b2 * a33) - (a23 * b3))) + (a13 * ((b2 * a23) - (a22 * b3)))) / det;
                double y = ((a11 * ((b2 * a33) - (a23 * b3))) - (b1 * ((a12 * a33) - (a23 * a13))) + (a13 * ((a12 * b3) - (b2 * a13)))) / det;
                double z = ((a11 * ((a22 * b3) - (b2 * a23))) - (a12 * ((a12 * b3) - (b2 * a13))) + (b1 * ((a12 * a23) - (a22 * a13)))) / det;
                var p = new Vector3((float)x, (float)y, (float)z);

                // Stay near the edge (the optimum can run away on nearly flat regions).
                if (Vector3.Distance(p, mid) <= Vector3.Distance(pos[a], pos[b]))
                {
                    best = p;
                    cost = Error(m, p);
                }
            }

            foreach (var c in new[] { pos[a], pos[b], mid })
            {
                double ec = Error(m, c);
                if (ec < cost)
                {
                    cost = ec;
                    best = c;
                }
            }

            // Normalise by area-weighting: compare against squared distance using the summed weight.
            double weight = Math.Max(1e-12, m[0] + m[4] + m[7]);
            cost = Math.Max(0, cost) / weight;
            return best;
        }

        private static double Error(double[] m, Vector3 p)
        {
            double x = p.X, y = p.Y, z = p.Z;
            return (m[0] * x * x) + (2 * m[1] * x * y) + (2 * m[2] * x * z) + (2 * m[3] * x)
                + (m[4] * y * y) + (2 * m[5] * y * z) + (2 * m[6] * y)
                + (m[7] * z * z) + (2 * m[8] * z) + m[9];
        }
    
        /// <summary>Binary min-heap of candidate collapses (netstandard2.1 has no PriorityQueue).</summary>
        private sealed class EdgeHeap
        {
            private readonly List<double> _cost = new List<double>();
            private readonly List<int> _a = new List<int>();
            private readonly List<int> _b = new List<int>();
            private readonly List<int> _va = new List<int>();
            private readonly List<int> _vb = new List<int>();
            private readonly List<Vector3> _p = new List<Vector3>();

            public int Count => _cost.Count;

            public void Push(double cost, int a, int b, int va, int vb, Vector3 p)
            {
                _cost.Add(cost);
                _a.Add(a);
                _b.Add(b);
                _va.Add(va);
                _vb.Add(vb);
                _p.Add(p);
                int i = _cost.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_cost[parent] <= _cost[i]) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public void Pop(out int a, out int b, out int va, out int vb, out Vector3 p)
            {
                a = _a[0];
                b = _b[0];
                va = _va[0];
                vb = _vb[0];
                p = _p[0];
                int last = _cost.Count - 1;
                Swap(0, last);
                _cost.RemoveAt(last);
                _a.RemoveAt(last);
                _b.RemoveAt(last);
                _va.RemoveAt(last);
                _vb.RemoveAt(last);
                _p.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = (2 * i) + 1, r = l + 1, m = i;
                    if (l < _cost.Count && _cost[l] < _cost[m]) m = l;
                    if (r < _cost.Count && _cost[r] < _cost[m]) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
            }

            private void Swap(int i, int j)
            {
                (_cost[i], _cost[j]) = (_cost[j], _cost[i]);
                (_a[i], _a[j]) = (_a[j], _a[i]);
                (_b[i], _b[j]) = (_b[j], _b[i]);
                (_va[i], _va[j]) = (_va[j], _va[i]);
                (_vb[i], _vb[j]) = (_vb[j], _vb[i]);
                (_p[i], _p[j]) = (_p[j], _p[i]);
            }
        }
    }
}
