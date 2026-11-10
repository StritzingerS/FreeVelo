using System.Diagnostics;
using System.Numerics;
using FreeVelo.Core.Avatar;
using FreeVelo.Core.Physics;
using FreeVelo.Core.World.Gen;

namespace FreeVelo.ModelPreview;

/// <summary>Top-down map of the generated world: height shading, roads, river, lakes, junctions, bridges.</summary>
public static class WorldPreview
{
    /// <summary>The world as JSON (roads, villages, water, a 50 m height raster, routes) for drawing a map elsewhere.</summary>
    public static void Export(string output)
    {
        var map = WorldMap.Default;
        var mt = MountainTerrain.Build(map, Terrain.GridRect(map));
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.#", inv);
        var sb = new System.Text.StringBuilder("{");
        sb.Append("\"nodes\":[");
        sb.Append(string.Join(",", map.Nodes.Select(n => $"{{\"name\":\"{n.Name}\",\"x\":{F(n.X)},\"z\":{F(n.Z)},\"e\":{F(n.Elevation)}}}")));
        sb.Append("],\"edges\":[");
        sb.Append(string.Join(",", map.Edges.Select(e =>
        {
            var idx = Enumerable.Range(0, e.X.Length).Where(i => i % 4 == 0 || i == e.X.Length - 1).ToList();
            return $"{{\"id\":{e.Id},\"from\":{e.From},\"to\":{e.To},\"kind\":\"{e.Kind}\",\"loop\":{(e.IsMainLoop ? "true" : "false")},\"length\":{F(e.Length)}," +
                $"\"x\":[{string.Join(",", idx.Select(i => F(e.X[i])))}],\"z\":[{string.Join(",", idx.Select(i => F(e.Z[i])))}],\"e\":[{string.Join(",", idx.Select(i => F(e.E[i])))}],\"d\":[{string.Join(",", idx.Select(i => F(e.D[i])))}]}}";
        })));
        var r = map.River!;
        sb.Append($"],\"river\":{{\"x\":[{string.Join(",", r.X.Select(F))}],\"z\":[{string.Join(",", r.Z.Select(F))}]}}");
        sb.Append(",\"lakes\":[" + string.Join(",", map.Lakes.Select(l => $"{{\"x\":{F(l.X)},\"z\":{F(l.Z)},\"r\":{F(l.Radius)},\"rim\":[{string.Join(",", Enumerable.Range(0, 48).Select(i => { double a = i * Math.PI * 2 / 48, r = l.RadiusAt(a); return $"[{F(l.X + (Math.Cos(a) * r))},{F(l.Z + (Math.Sin(a) * r))}]"; }))}]}}")) + "]");
        sb.Append(",\"bridges\":[" + string.Join(",", map.Bridges.Select(b =>
        {
            var e = map.Edges[b.Edge];
            int i = Array.FindIndex(e.D, d => d >= b.DistanceM);
            return $"{{\"x\":{F(e.X[i])},\"z\":{F(e.Z[i])}}}";
        })) + "]");
        var routes = new List<(string Name, FreeVelo.Core.World.Route Route)>
        {
            ("valley", map.MainLoop), ("alm", map.AlmLoop()), ("stelvio", map.ClimbLoop(ClimbData.Stelvio)!), ("alpe", map.ClimbLoop(ClimbData.AlpeDHuez)!),
        };
        sb.Append(",\"routes\":[" + string.Join(",", routes.Select(rt =>
        {
            var pts = new List<string>();
            for (double d = 0; d <= rt.Route.LengthM; d += 50) pts.Add(F(rt.Route.Sample(d).Y));
            return $"{{\"name\":\"{rt.Name}\",\"length\":{F(rt.Route.LengthM)},\"ascent\":{F(rt.Route.TotalAscentM)},\"e\":[{string.Join(",", pts)}]}}";
        })) + "]");
        var (minX, minZ, maxX, maxZ) = map.MountainBounds;
        minX -= 1500; minZ -= 1500; maxX += 1500; maxZ += 1500;
        const double cell = 50;
        int nx = (int)((maxX - minX) / cell) + 1, nz = (int)((maxZ - minZ) / cell) + 1;
        var h = new double[nx * nz];
        Parallel.For(0, nz, j =>
        {
            for (int i = 0; i < nx; i++) h[(j * nx) + i] = mt.Coarse(minX + (i * cell), minZ + (j * cell));
        });
        sb.Append($",\"raster\":{{\"minX\":{F(minX)},\"minZ\":{F(minZ)},\"cell\":{F(cell)},\"nx\":{nx},\"nz\":{nz},\"h\":[{string.Join(",", h.Select(v => F(v)))}]}}");
        sb.Append("}");
        File.WriteAllText(output, sb.ToString());
        Console.WriteLine($"-> {output}");
    }

    public static void Map(string output, int seed)
    {
        var sw = Stopwatch.StartNew();
        var map = WorldMap.Generate(seed);
        Console.WriteLine($"world {seed}: {map.Nodes.Count} nodes, {map.Edges.Count} edges, {map.Lakes.Count} lakes, {map.Bridges.Count} bridges, {sw.ElapsedMilliseconds} ms");
        foreach (var e in map.Edges)
        {
            double maxGrade = 0;
            for (int i = 1; i < e.E.Length; i++) maxGrade = Math.Max(maxGrade, Math.Abs(e.E[i] - e.E[i - 1]) / (e.D[i] - e.D[i - 1]));
            Console.WriteLine($"  edge {e.Id}: {e.From}->{e.To} {e.Length:0} m {(e.IsMainLoop ? "loop" : "link")} {e.Surface} elev {e.E.Min():0}-{e.E.Max():0} max grade {maxGrade * 100:0.0}%");
        }

        foreach (var n in map.Nodes) Console.WriteLine($"  node {n.Id}: ({n.X:0},{n.Z:0}) elev {n.Elevation:0}");
        if (Environment.GetEnvironmentVariable("PTS") is string pts)
        {
            foreach (var id in pts.Split(','))
            {
                var e = map.Edges[int.Parse(id)];
                Console.WriteLine($"  edge {e.Id} points:");
                for (int i = 0; i < e.X.Length; i += 40) Console.WriteLine($"    d {e.D[i]:0} ({e.X[i]:0},{e.Z[i]:0}) e {e.E[i]:0}");
            }
        }

        if (map.Mountain != null)
        {
            foreach (var c in map.Mountain.Climbs) Console.WriteLine($"  climb {c.Data.Id}: angle {c.Angle:R} summit ({c.SummitX:R},{c.SummitZ:R}) foot elev {c.FootElevation:R}");
        }

        Console.WriteLine($"  home bounds {map.Bounds}, mountain bounds {map.MountainBounds}, grid {Terrain.GridRect(map)}");
        foreach (var l in map.Lakes) Console.WriteLine($"  lake at ({l.X:0},{l.Z:0}) r {l.Radius:0} level {l.Level:0.0}");
        foreach (var b in map.Bridges) Console.WriteLine($"  bridge edge {b.Edge} at {b.DistanceM:0} m, water {b.WaterLevel:0.0}, road {Sample(map.Edges[b.Edge], b.DistanceM):0.0}");
        var loop = map.MainLoop;
        Console.WriteLine($"  main loop {loop.LengthM:0} m, ascent {loop.TotalAscentM:0} m");

        var (minX, minZ, maxX, maxZ) = map.Bounds;
        double margin = 500;
        MountainTerrain? mt = null;
        if (Environment.GetEnvironmentVariable("FULL") == "1")
        {
            // Whole world: home, mountain and everything outside the home grid
            var mb = map.MountainBounds;
            minX = Math.Min(minX, mb.MinX); minZ = Math.Min(minZ, mb.MinZ); maxX = Math.Max(maxX, mb.MaxX); maxZ = Math.Max(maxZ, mb.MaxZ);
            mt = MountainTerrain.Build(map, Terrain.GridRect(map));
        }

        minX -= margin; minZ -= margin; maxX += margin; maxZ += margin;
        const int size = 1000;
        double scale = size / Math.Max(maxX - minX, maxZ - minZ);
        var rgb = new byte[size * size * 3];
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                double x = minX + (px / scale), z = maxZ - (py / scale);
                double h = mt != null ? mt.Coarse(x, z) : map.BaseHeight(x, z);
                double hx = (mt != null ? mt.Coarse(x + 5, z) : map.BaseHeight(x + 5, z)) - h;
                double shade = mt != null ? Math.Clamp(0.35 + ((h - 100) / 2400) - (hx * 0.15), 0.1, 1) : Math.Clamp(0.55 + ((h - 100) / 70) - (hx * 0.6), 0.15, 1);
                var c = (R: 0.45 * shade, G: 0.7 * shade, B: 0.35 * shade);
                foreach (var l in map.Lakes)
                {
                    if (l.Relative(x, z) < 1) c = (0.2, 0.45, 0.75);
                }

                Set(rgb, size, px, py, c.R, c.G, c.B);
            }
        }

        var river = map.River!;
        for (int i = 0; i < river.X.Length; i++) Dot(rgb, size, (river.X[i] - minX) * scale, (maxZ - river.Z[i]) * scale, river.HalfWidth * scale + 1, (0.2, 0.45, 0.8));
        foreach (var e in map.Edges)
        {
            var col = e.IsMainLoop ? (0.15, 0.15, 0.15) : e.Surface == RoadSurface.Asphalt ? (0.9, 0.4, 0.1) : (0.75, 0.6, 0.35);
            for (int i = 0; i < e.X.Length; i++) Dot(rgb, size, (e.X[i] - minX) * scale, (maxZ - e.Z[i]) * scale, 1.6, col);
        }

        foreach (var n in map.Nodes) Dot(rgb, size, (n.X - minX) * scale, (maxZ - n.Z) * scale, 6, (1, 1, 1));
        foreach (var b in map.Bridges)
        {
            var e = map.Edges[b.Edge];
            int i = Array.FindIndex(e.D, d => d >= b.DistanceM);
            Dot(rgb, size, (e.X[i] - minX) * scale, (maxZ - e.Z[i]) * scale, 5, (1, 0.9, 0.1));
        }

        Dot(rgb, size, (map.Nodes[0].X - minX) * scale, (maxZ - map.Nodes[0].Z) * scale, 9, (0.9, 0.1, 0.1));
        Renderer.WritePng(output, rgb, size, size);
        Console.WriteLine($"-> {output}");
    }

    /// <summary>All scenery models side by side (close-up and far version), lit like the game.</summary>
    public static void Gallery(string output, int lod, string? only)
    {
        var meshes = new List<SceneMesh>();
        var wanted = only?.Split(',');
        var kinds = Enum.GetValues<SceneryKind>().Where(k => wanted == null || wanted.Any(w => k.ToString().Equals(w, StringComparison.OrdinalIgnoreCase))).ToList();
        float x = 0;
        int tris = 0;
        foreach (var kind in kinds)
        {
            float spacing = Math.Max(4, (float)SceneryModels.Radius(kind) * 2.6f);
            for (int v = 0; v < SceneryModels.Variants; v++)
            {
                var model = SceneryModels.Get(kind, v);
                tris += model.Triangles(lod);
                foreach (var (material, mesh) in lod == 0 ? model.Lod0 : model.Lod1)
                {
                    var offset = new Vector3(x, 0, 0);
                    var normals = mesh.ComputeNormals(IsFoliage(material) ? 85f : 35f);
                    var (color, smooth) = WorldMaterials.All[material];
                    meshes.Add(new SceneMesh { Positions = mesh.Vertices.Select(p => p + offset).ToArray(), Normals = normals, Triangles = mesh.Triangles.ToArray(), Albedo = color, Smoothness = smooth });
                }

                x += spacing;
            }
        }

        meshes.Add(Ground(new Vector3(-30, 0, -40), new Vector3(x + 30, 0, 40)));
        var renderer = new Renderer(meshes);
        float cx = (x - 11) / 2;
        float dist = Math.Max(14, x * 0.75f);
        var cameras = new[]
        {
            new Camera { Name = "front", Position = new Vector3(cx, 6, -dist), Target = new Vector3(cx, 5, 0), FieldOfView = 50 },
            new Camera { Name = "angle", Position = new Vector3(cx + (dist * 0.5f), dist * 0.35f, -dist * 0.7f), Target = new Vector3(cx, 3, 0), FieldOfView = 50 },
        };
        const int ss = 2, w = 1400, h = 700;
        var sheet = new byte[w * h * 2 * 3];
        for (int i = 0; i < cameras.Length; i++)
        {
            var img = Renderer.ToImage(renderer.Render(cameras[i], w * ss, h * ss), w * ss, h * ss, ss, out _, out _);
            Buffer.BlockCopy(img, 0, sheet, i * w * h * 3, img.Length);
        }

        Renderer.WritePng(output, sheet, w, h * 2);
        Console.WriteLine($"{kinds.Count} kinds x {SceneryModels.Variants}, LOD{lod}: {tris} triangles -> {output}");
    }

    /// <summary>3D views of the generated world: rider's-eye views along the main loop and an overview.</summary>
    public static void Scene(string output, double[] distances)
    {
        var sw = Stopwatch.StartNew();
        var map = WorldMap.Default;
        var terrain = Terrain.Build(map);
        var layout = SceneryLayout.Generate(terrain);
        Console.WriteLine($"world generated in {sw.ElapsedMilliseconds} ms");
        var loop = map.MainLoop;
        const int ss = 2, w = 960, h = 540;
        var tiles = new List<byte[]>();
        foreach (double d in distances)
        {
            Camera camera;
            Vector3 focus;
            if (d < 0)
            {
                // Overview from high above the start.
                var s0 = loop.Sample(-d);
                focus = new Vector3((float)s0.X, (float)s0.Y, (float)s0.Z);
                var back = new Vector3(-(float)Math.Sin(s0.HeadingRad), 0, -(float)Math.Cos(s0.HeadingRad));
                camera = new Camera { Name = "over", Position = focus + (back * 260) + new Vector3(0, 160, 0), Target = focus + (Vector3.UnitY * 10) - (back * 200), FieldOfView = 60 };
            }
            else
            {
                var s = loop.Sample(d);
                var fwd = new Vector3((float)Math.Sin(s.HeadingRad), 0, (float)Math.Cos(s.HeadingRad));
                var right = new Vector3(fwd.Z, 0, -fwd.X);
                focus = new Vector3((float)s.X, (float)s.Y, (float)s.Z) + (right * 1.4f);
                camera = new Camera { Name = $"d{d}", Position = focus - (fwd * 4.6f) + new Vector3(0, 1.9f, 0), Target = focus + (fwd * 20) + new Vector3(0, 1.0f, 0), FieldOfView = 60 };
            }

            var meshes = SceneMeshes(map, terrain, layout, camera.Position, d < 0 ? 1600 : 1100);
            var renderer = new Renderer(meshes, shadowCenter: camera.Position, shadowRadius: 180);
            tiles.Add(Renderer.ToImage(renderer.Render(camera, w * ss, h * ss), w * ss, h * ss, ss, out _, out _));
            Console.WriteLine($"  view {camera.Name}: {meshes.Sum(m => m.Triangles.Length / 3) / 1000}k triangles");
        }

        int cols = Math.Min(2, tiles.Count), rows = (tiles.Count + cols - 1) / cols;
        var sheet = new byte[cols * w * rows * h * 3];
        for (int t = 0; t < tiles.Count; t++)
        {
            int ox = (t % cols) * w, oy = (t / cols) * h;
            for (int y = 0; y < h; y++) Buffer.BlockCopy(tiles[t], y * w * 3, sheet, (((oy + y) * cols * w) + ox) * 3, w * 3);
        }

        Renderer.WritePng(output, sheet, cols * w, rows * h);
        Console.WriteLine($"-> {output} ({sw.ElapsedMilliseconds} ms)");
    }

    /// <summary>Straight down onto the whole world (north up), with all scenery.</summary>
    public static void Top(string output, int size)
    {
        var map = WorldMap.Default;
        var terrain = Terrain.Build(map);
        var layout = SceneryLayout.Generate(terrain);
        var (minX, minZ, maxX, maxZ) = map.Bounds;
        var center = new Vector3((float)(minX + maxX) / 2, 0, (float)(minZ + maxZ) / 2);
        float extent = (float)Math.Max(maxX - minX, maxZ - minZ) + 500;
        const float fov = 30;
        float height = extent / 2 / MathF.Tan(fov * MathF.PI / 360);
        var camera = new Camera { Name = "top", Position = center + new Vector3(0, height, -0.01f * height), Target = center, FieldOfView = fov };
        var meshes = SceneMeshes(map, terrain, layout, center, extent * 0.75, detailRadius: 0);
        var renderer = new Renderer(meshes, shadowCenter: center, shadowRadius: extent) { Fog = false };
        var img = Renderer.ToImage(renderer.Render(camera, size * 2, size * 2), size * 2, size * 2, 2, out _, out _);
        Renderer.WritePng(output, img, size, size);
        Console.WriteLine($"top view {extent:0} m wide, {meshes.Sum(m => m.Triangles.Length / 3) / 1000}k triangles -> {output}");
    }

    private static List<SceneMesh> SceneMeshes(WorldMap map, Terrain terrain, SceneryLayout layout, Vector3 eye, double radius, double detailRadius = 110)
    {
        var meshes = new List<SceneMesh>();

        // Terrain: 4 m grid close by, 12 m further away.
        foreach (var (inner, outer, step) in detailRadius > 0 ? new[] { (0.0, 260.0, 1), (240.0, radius, 3) } : new[] { (0.0, radius, 2) })
        {
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var tri = new List<int>();
            int x0 = (int)((eye.X - outer - terrain.MinX) / terrain.Cell), x1 = (int)((eye.X + outer - terrain.MinX) / terrain.Cell);
            int z0 = (int)((eye.Z - outer - terrain.MinZ) / terrain.Cell), z1 = (int)((eye.Z + outer - terrain.MinZ) / terrain.Cell);
            x0 = Math.Max(1, x0 - (x0 % step)); z0 = Math.Max(1, z0 - (z0 % step));
            x1 = Math.Min(terrain.Nx - 2, x1); z1 = Math.Min(terrain.Nz - 2, z1);
            int nx = ((x1 - x0) / step) + 1, nz = ((z1 - z0) / step) + 1;
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    int gx = x0 + (i * step), gz = z0 + (j * step);
                    float x = (float)(terrain.MinX + (gx * terrain.Cell)), z = (float)(terrain.MinZ + (gz * terrain.Cell));
                    pos.Add(new Vector3(x, terrain.At(gx, gz) - (step > 1 ? 0.4f : 0), z));
                    float dx = terrain.At(gx + 1, gz) - terrain.At(gx - 1, gz), dz = terrain.At(gx, gz + 1) - terrain.At(gx, gz - 1);
                    nrm.Add(Vector3.Normalize(new Vector3(-dx, 2 * (float)terrain.Cell, -dz)));
                }
            }

            for (int j = 0; j < nz - 1; j++)
            {
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = (j * nx) + i;
                    var c = pos[a];
                    double r = Math.Sqrt(((c.X - eye.X) * (c.X - eye.X)) + ((c.Z - eye.Z) * (c.Z - eye.Z)));
                    if (r < inner || r > outer) continue;
                    tri.AddRange(new[] { a, a + nx, a + 1, a + 1, a + nx, a + nx + 1 });
                }
            }

            meshes.Add(new SceneMesh { Positions = pos.ToArray(), Normals = nrm.ToArray(), Triangles = tri.ToArray(), Albedo = new Vector3(0.36f, 0.5f, 0.22f), CastsShadow = false });
        }

        // Roads
        foreach (var e in map.Edges)
        {
            var pos = new List<Vector3>();
            var tri = new List<int>();
            float half = (float)e.Width / 2;
            for (int i = 0; i < e.X.Length; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(e.X.Length - 1, i + 1);
                var f = Vector3.Normalize(new Vector3((float)(e.X[b] - e.X[a]), 0, (float)(e.Z[b] - e.Z[a])));
                var r = new Vector3(f.Z, 0, -f.X);
                var c = new Vector3((float)e.X[i], (float)e.E[i] + 0.05f, (float)e.Z[i]);
                pos.Add(c - (r * half));
                pos.Add(c + (r * half));
                if (i > 0) { int k = pos.Count - 4; tri.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
            }

            var color = e.Surface == FreeVelo.Core.Physics.RoadSurface.Asphalt ? new Vector3(0.3f, 0.3f, 0.31f) : new Vector3(0.6f, 0.56f, 0.46f);
            meshes.Add(new SceneMesh { Positions = pos.ToArray(), Normals = Enumerable.Repeat(Vector3.UnitY, pos.Count).ToArray(), Triangles = tri.ToArray(), Albedo = color, CastsShadow = false });
        }

        // Water
        var wpos = new List<Vector3>();
        var wtri = new List<int>();
        foreach (var lake in map.Lakes)
        {
            int c = wpos.Count;
            wpos.Add(new Vector3((float)lake.X, (float)lake.Level, (float)lake.Z));
            for (int i = 0; i < 64; i++)
            {
                double a = i * Math.PI * 2 / 64;
                double r = lake.RadiusAt(a) * 1.06;
                wpos.Add(new Vector3((float)(lake.X + (Math.Cos(a) * r)), (float)lake.Level, (float)(lake.Z + (Math.Sin(a) * r))));
            }

            for (int i = 0; i < 64; i++) wtri.AddRange(new[] { c, c + 1 + ((i + 1) % 64), c + 1 + i });
        }

        var river = map.River!;
        for (int i = 0; i < river.X.Length; i++)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(river.X.Length - 1, i + 1);
            var f = Vector3.Normalize(new Vector3((float)(river.X[b] - river.X[a]), 0, (float)(river.Z[b] - river.Z[a])));
            var r = new Vector3(f.Z, 0, -f.X) * ((float)river.HalfWidth + 2.5f);
            var c = new Vector3((float)river.X[i], (float)river.Level[i], (float)river.Z[i]);
            wpos.Add(c - r);
            wpos.Add(c + r);
            if (i > 0) { int k = wpos.Count - 4; wtri.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
        }

        var (wc, ws) = WorldMaterials.All[WorldMaterial.Water];
        meshes.Add(new SceneMesh { Positions = wpos.ToArray(), Normals = Enumerable.Repeat(Vector3.UnitY, wpos.Count).ToArray(), Triangles = wtri.ToArray(), Albedo = wc, Smoothness = ws, CastsShadow = false });

        // Fields
        foreach (var field in layout.Fields)
        {
            var pos = new List<Vector3>();
            var tri = new List<int>();
            int nu = (int)(field.HalfLength / 4), nv = (int)(field.HalfWidth / 4);
            for (int j = 0; j <= nv; j++)
            {
                for (int i = 0; i <= nu; i++)
                {
                    var (x, z) = field.Point(-field.HalfLength + (2 * field.HalfLength * i / nu), -field.HalfWidth + (2 * field.HalfWidth * j / nv));
                    pos.Add(new Vector3((float)x, (float)terrain.Height(x, z) + 0.08f, (float)z));
                }
            }

            for (int j = 0; j < nv; j++)
            {
                for (int i = 0; i < nu; i++)
                {
                    int a = (j * (nu + 1)) + i, b = a + nu + 1;
                    tri.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            }

            var (fc, _) = WorldMaterials.All[field.Crop];
            meshes.Add(new SceneMesh { Positions = pos.ToArray(), Normals = Enumerable.Repeat(Vector3.UnitY, pos.Count).ToArray(), Triangles = tri.ToArray(), Albedo = fc, CastsShadow = false });
        }

        // Scenery, merged per material
        var merged = new Dictionary<WorldMaterial, (List<Vector3> P, List<Vector3> N, List<int> T)>();
        var normalCache = new Dictionary<MeshData, Vector3[]>();
        foreach (var item in layout.Items)
        {
            double dist = Math.Sqrt(((item.X - eye.X) * (item.X - eye.X)) + ((item.Z - eye.Z) * (item.Z - eye.Z)));
            if (dist > radius) continue;
            var model = SceneryModels.Get(item.Kind, item.Variant);
            var parts = dist < detailRadius ? model.Lod0 : model.Lod1;
            var rot = Rot.AboutY(item.RotationDeg);
            var at = new Vector3(item.X, item.Y, item.Z);
            foreach (var (material, mesh) in parts)
            {
                if (!merged.TryGetValue(material, out var m)) merged[material] = m = (new List<Vector3>(), new List<Vector3>(), new List<int>());
                if (!normalCache.TryGetValue(mesh, out var n)) normalCache[mesh] = n = mesh.ComputeNormals(IsFoliage(material) ? 85f : 35f);
                int offset = m.P.Count;
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    m.P.Add(at + (rot * (mesh.Vertices[i] * item.Scale)));
                    m.N.Add(rot * n[i]);
                }

                foreach (int t in mesh.Triangles) m.T.Add(t + offset);
            }
        }

        foreach (var (material, m) in merged)
        {
            var (color, smooth) = WorldMaterials.All[material];
            meshes.Add(new SceneMesh { Positions = m.P.ToArray(), Normals = m.N.ToArray(), Triangles = m.T.ToArray(), Albedo = color, Smoothness = smooth });
        }

        return meshes;
    }

    public static bool IsFoliage(WorldMaterial m) => m is WorldMaterial.SpruceNeedles or WorldMaterial.SpruceNeedlesDark or WorldMaterial.PineNeedles
        or WorldMaterial.LeavesDark or WorldMaterial.LeavesMid or WorldMaterial.LeavesLight or WorldMaterial.BirchLeaves or WorldMaterial.PoplarLeaves
        or WorldMaterial.Bush or WorldMaterial.BushFlowering or WorldMaterial.Rock;

    private static SceneMesh Ground(Vector3 min, Vector3 max)
    {
        const float cell = 2f;
        int nx = (int)((max.X - min.X) / cell), nz = (int)((max.Z - min.Z) / cell);
        var pos = new List<Vector3>();
        var tri = new List<int>();
        for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++) pos.Add(new Vector3(min.X + (i * cell), 0, min.Z + (j * cell)));
        for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) { int a = (i * (nz + 1)) + j, b = a + 1, c = a + nz + 1, d = c + 1; tri.AddRange(new[] { a, b, d, a, d, c }); }
        return new SceneMesh { Positions = pos.ToArray(), Normals = Enumerable.Repeat(Vector3.UnitY, pos.Count).ToArray(), Triangles = tri.ToArray(), Albedo = new Vector3(0.42f, 0.55f, 0.27f), CastsShadow = false };
    }

    private static double Sample(RoadEdge e, double d)
    {
        int i = Math.Max(1, Array.FindIndex(e.D, x => x >= d));
        return e.E[i];
    }

    private static void Set(byte[] rgb, int size, int x, int y, double r, double g, double b)
    {
        if (x < 0 || y < 0 || x >= size || y >= size) return;
        int o = ((y * size) + x) * 3;
        rgb[o] = (byte)(Math.Clamp(r, 0, 1) * 255);
        rgb[o + 1] = (byte)(Math.Clamp(g, 0, 1) * 255);
        rgb[o + 2] = (byte)(Math.Clamp(b, 0, 1) * 255);
    }

    private static void Dot(byte[] rgb, int size, double cx, double cy, double r, (double R, double G, double B) c)
    {
        for (int y = (int)(cy - r); y <= (int)(cy + r); y++)
        {
            for (int x = (int)(cx - r); x <= (int)(cx + r); x++)
            {
                if (((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) <= r * r) Set(rgb, size, x, y, c.R, c.G, c.B);
            }
        }
    }
}
