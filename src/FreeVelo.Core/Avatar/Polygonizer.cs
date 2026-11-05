using System;
using System.Collections.Generic;
using System.Numerics;

namespace FreeVelo.Core.Avatar
{
    /// <summary>
    /// Turns a signed distance function into a smooth triangle mesh (surface nets with vertices projected onto the
    /// surface and normals from the field gradient) and splits it into material regions along exact boundaries.
    /// </summary>
    public static class Polygonizer
    {
        /// <summary>Meshes the zero level set of <paramref name="f"/> inside the box with the given cell size.</summary>
        public static MeshData Mesh(Sdf f, Vector3 min, Vector3 max, float cell)
        {
            int nx = (int)MathF.Ceiling((max.X - min.X) / cell) + 2;
            int ny = (int)MathF.Ceiling((max.Y - min.Y) / cell) + 2;
            int nz = (int)MathF.Ceiling((max.Z - min.Z) / cell) + 2;
            var origin = min - new Vector3(cell / 2f);
            var values = new float[nx * ny * nz];
            int Idx(int x, int y, int z) => (((z * ny) + y) * nx) + x;
            Vector3 P(int x, int y, int z) => origin + new Vector3(x * cell, y * cell, z * cell);

            // Coarse pass: blocks far from the surface are filled with one value (only the sign matters there).
            const int block = 4;
            float reach = cell * block * 0.87f * 1.6f; // half block diagonal with a safety factor for approximate distances
            for (int bz = 0; bz < nz; bz += block)
            {
                for (int by = 0; by < ny; by += block)
                {
                    for (int bx = 0; bx < nx; bx += block)
                    {
                        int ex = Math.Min(nx, bx + block), ey = Math.Min(ny, by + block), ez = Math.Min(nz, bz + block);
                        var center = origin + (new Vector3(bx + ex - 1, by + ey - 1, bz + ez - 1) * (cell / 2f));
                        float dc = f(center);
                        bool far = MathF.Abs(dc) > reach;
                        for (int z = bz; z < ez; z++)
                        {
                            for (int y = by; y < ey; y++)
                            {
                                for (int x = bx; x < ex; x++)
                                {
                                    // Closed surface: force "outside" on the border.
                                    bool border = x == 0 || y == 0 || z == 0 || x == nx - 1 || y == ny - 1 || z == nz - 1;
                                    float v = far ? dc : f(P(x, y, z));
                                    values[Idx(x, y, z)] = border ? MathF.Max(cell, v) : v;
                                }
                            }
                        }
                    }
                }
            }

            var mesh = new MeshData();
            var cellVertex = new Dictionary<int, int>();
            int[] cx = { 0, 1, 0, 1, 0, 1, 0, 1 };
            int[] cy = { 0, 0, 1, 1, 0, 0, 1, 1 };
            int[] cz = { 0, 0, 0, 0, 1, 1, 1, 1 };
            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            float eps = cell * 0.05f;

            for (int z = 0; z < nz - 1; z++)
            {
                for (int y = 0; y < ny - 1; y++)
                {
                    for (int x = 0; x < nx - 1; x++)
                    {
                        var corner = new float[8];
                        int inside = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            corner[c] = values[Idx(x + cx[c], y + cy[c], z + cz[c])];
                            if (corner[c] < 0) inside++;
                        }

                        if (inside == 0 || inside == 8) continue;
                        var sum = Vector3.Zero;
                        int count = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            float a = corner[edges[e, 0]], b = corner[edges[e, 1]];
                            if ((a < 0) == (b < 0)) continue;
                            float t = a / (a - b);
                            var pa = new Vector3(cx[edges[e, 0]], cy[edges[e, 0]], cz[edges[e, 0]]);
                            var pb = new Vector3(cx[edges[e, 1]], cy[edges[e, 1]], cz[edges[e, 1]]);
                            sum += Vector3.Lerp(pa, pb, t);
                            count++;
                        }

                        var p = P(x, y, z) + (sum / count * cell);

                        // Project onto the surface (a few Newton steps, limited to stay near the cell).
                        var start = p;
                        for (int it = 0; it < 4; it++)
                        {
                            float d = f(p);
                            var g = Gradient(f, p, eps);
                            float g2 = g.LengthSquared();
                            if (g2 < 1e-12f) break;
                            p -= g * (d / g2);
                        }

                        if (Vector3.Distance(p, start) > cell) p = start + (Vector3.Normalize(p - start) * cell);
                        cellVertex[Idx(x, y, z)] = mesh.Vertices.Count;
                        mesh.Vertices.Add(p);
                        mesh.Normals.Add(SafeNormal(Gradient(f, p, eps)));
                    }
                }
            }

            // One quad per grid edge with a sign change, connecting the four cells around that edge.
            for (int z = 1; z < nz - 1; z++)
            {
                for (int y = 1; y < ny - 1; y++)
                {
                    for (int x = 1; x < nx - 1; x++)
                    {
                        float v = values[Idx(x, y, z)];
                        if (x < nx - 1 && (v < 0) != (values[Idx(x + 1, y, z)] < 0))
                        {
                            Quad(mesh, cellVertex, Idx(x, y - 1, z - 1), Idx(x, y, z - 1), Idx(x, y, z), Idx(x, y - 1, z));
                        }

                        if (y < ny - 1 && (v < 0) != (values[Idx(x, y + 1, z)] < 0))
                        {
                            Quad(mesh, cellVertex, Idx(x - 1, y, z - 1), Idx(x, y, z - 1), Idx(x, y, z), Idx(x - 1, y, z));
                        }

                        if (z < nz - 1 && (v < 0) != (values[Idx(x, y, z + 1)] < 0))
                        {
                            Quad(mesh, cellVertex, Idx(x - 1, y - 1, z), Idx(x, y - 1, z), Idx(x, y, z), Idx(x - 1, y, z));
                        }
                    }
                }
            }

            return mesh;
        }

        private static void Quad(MeshData mesh, Dictionary<int, int> cells, int c0, int c1, int c2, int c3)
        {
            if (!cells.TryGetValue(c0, out int a) || !cells.TryGetValue(c1, out int b) || !cells.TryGetValue(c2, out int c) || !cells.TryGetValue(c3, out int d)) return;
            var v = mesh.Vertices;

            // Split along the shorter diagonal.
            if (Vector3.DistanceSquared(v[a], v[c]) <= Vector3.DistanceSquared(v[b], v[d]))
            {
                AddOriented(mesh, a, b, c);
                AddOriented(mesh, a, c, d);
            }
            else
            {
                AddOriented(mesh, a, b, d);
                AddOriented(mesh, b, c, d);
            }
        }

        /// <summary>Adds a triangle wound so that Cross(b − a, c − a) points along the surface normal (Unity front face).</summary>
        private static void AddOriented(MeshData mesh, int a, int b, int c)
        {
            var v = mesh.Vertices;
            var n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (n.LengthSquared() < 1e-20f) return;
            var normal = mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[c];
            mesh.Triangles.Add(a);
            if (Vector3.Dot(n, normal) >= 0)
            {
                mesh.Triangles.Add(b);
                mesh.Triangles.Add(c);
            }
            else
            {
                mesh.Triangles.Add(c);
                mesh.Triangles.Add(b);
            }
        }

        public static Vector3 Gradient(Sdf f, Vector3 p, float eps)
        {
            var dx = new Vector3(eps, 0, 0);
            var dy = new Vector3(0, eps, 0);
            var dz = new Vector3(0, 0, eps);
            return new Vector3(f(p + dx) - f(p - dx), f(p + dy) - f(p - dy), f(p + dz) - f(p - dz)) / (2f * eps);
        }

        private static Vector3 SafeNormal(Vector3 g)
        {
            float l = g.Length();
            return l > 1e-12f ? g / l : Vector3.UnitY;
        }

        /// <summary>
        /// Splits a mesh into material regions. <paramref name="regions"/> are evaluated in order; a point belongs to
        /// the last region whose function is negative there (index + 1), or to 0 if none. Triangles crossing a region
        /// boundary are cut exactly along it, so boundaries are crisp lines (hems, stripes, glove cuffs).
        /// </summary>
        public static MeshData[] Split(MeshData mesh, IList<Sdf> regions)
        {
            // Plain index lists (shared vertices, one cached cut vertex per edge and region pass).
            var pos = new List<Vector3>(mesh.Vertices);
            var nrm = mesh.Normals.Count == mesh.Vertices.Count ? new List<Vector3>(mesh.Normals) : new List<Vector3>(mesh.ComputeNormals());
            var tri = new List<int>(mesh.Triangles);
            var mat = new List<int>(new int[mesh.TriangleCount]);

            for (int r = 0; r < regions.Count; r++)
            {
                var f = regions[r];
                var values = new List<float>(pos.Count);
                for (int i = 0; i < pos.Count; i++) values.Add(f(pos[i]));
                var cuts = new Dictionary<long, int>();
                int Cut(int u, int v)
                {
                    long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
                    if (cuts.TryGetValue(key, out int k)) return k;
                    float t = values[u] / (values[u] - values[v]);
                    k = pos.Count;
                    pos.Add(Vector3.Lerp(pos[u], pos[v], t));
                    nrm.Add(Vector3.Normalize(Vector3.Lerp(nrm[u], nrm[v], t)));
                    values.Add(0f);
                    cuts[key] = k;
                    return k;
                }

                var nextTri = new List<int>(tri.Count + 64);
                var nextMat = new List<int>(mat.Count + 32);
                void Emit(int a, int b, int c, int m)
                {
                    nextTri.Add(a);
                    nextTri.Add(b);
                    nextTri.Add(c);
                    nextMat.Add(m);
                }

                for (int t = 0; t < mat.Count; t++)
                {
                    int a = tri[3 * t], b = tri[(3 * t) + 1], c = tri[(3 * t) + 2], m = mat[t];
                    bool ia = values[a] < 0, ib = values[b] < 0, ic = values[c] < 0;
                    if (ia == ib && ib == ic)
                    {
                        Emit(a, b, c, ia ? r + 1 : m);
                        continue;
                    }

                    // Rotate (keeping the winding) so that 'a' is alone on its side.
                    if (ib != ia && ib != ic)
                    {
                        (a, b, c) = (b, c, a);
                    }
                    else if (ic != ia && ic != ib)
                    {
                        (a, b, c) = (c, a, b);
                    }

                    int ab = Cut(a, b), ac = Cut(a, c);
                    bool aloneInside = values[a] < 0;
                    int alone = aloneInside ? r + 1 : m;
                    int rest = aloneInside ? m : r + 1;
                    Emit(a, ab, ac, alone);
                    Emit(ab, b, c, rest);
                    Emit(ab, c, ac, rest);
                }

                tri = nextTri;
                mat = nextMat;
            }

            var result = new MeshData[regions.Count + 1];
            var remap = new int[result.Length][];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new MeshData();
                remap[i] = new int[pos.Count];
                Array.Fill(remap[i], -1);
            }

            for (int t = 0; t < mat.Count; t++)
            {
                var md = result[mat[t]];
                var map = remap[mat[t]];
                for (int k = 0; k < 3; k++)
                {
                    int v = tri[(3 * t) + k];
                    if (map[v] < 0)
                    {
                        map[v] = md.Vertices.Count;
                        md.Vertices.Add(pos[v]);
                        md.Normals.Add(nrm[v]);
                    }

                    md.Triangles.Add(map[v]);
                }
            }

            return result;
        }
    }
}
