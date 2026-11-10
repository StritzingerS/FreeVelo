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
        var land = Landscape.Generate(Terrain.Build(map), new DateTime(2026, 7, 1));
        var built = land.Items.Where(i => i.Kind is SceneryKind.AlpineHouse or SceneryKind.Inn or SceneryKind.Shop or SceneryKind.Church or SceneryKind.Farmhouse
            or SceneryKind.Workshop or SceneryKind.Sawmill or SceneryKind.FireStation or SceneryKind.HayHut or SceneryKind.AlpineHut or SceneryKind.Shrine or SceneryKind.Lane);
        sb.Append(",\"buildings\":[" + string.Join(",", built.Select(i => $"{{\"k\":\"{i.Kind}\",\"x\":{F(i.X)},\"z\":{F(i.Z)},\"r\":{F(i.RotationDeg)}}}")) + "]");
        string Patch(FieldPatch f) => "[" + string.Join(",", new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }.Select(c => { var (x, z) = f.Point(c.Item1 * f.HalfLength, c.Item2 * f.HalfWidth); return $"[{F(x)},{F(z)}]"; })) + "]";
        sb.Append(",\"fields\":[" + string.Join(",", land.Fields.Select(f => $"{{\"c\":\"{f.Crop}\",\"p\":{Patch(f)}}}")) + "]");
        sb.Append(",\"pastures\":[" + string.Join(",", land.Pastures.Select(Patch)) + "]");
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
                    // FRONT=1: turned round, so the cameras (south of the models) see their fronts.
                    var turn = Environment.GetEnvironmentVariable("ROTY") is string ry ? Rot.AboutY(float.Parse(ry, System.Globalization.CultureInfo.InvariantCulture))
                        : Environment.GetEnvironmentVariable("FRONT") == "1" ? Rot.AboutY(180) : Rot.Identity;
                    normals = normals.Select(n => turn * n).ToArray();
                    meshes.Add(new SceneMesh { Positions = mesh.Vertices.Select(p => (turn * p) + offset).ToArray(), Normals = normals, Triangles = mesh.Triangles.ToArray(), Albedo = color, Smoothness = smooth });
                }

                x += spacing;
            }
        }

        meshes.Add(Ground(new Vector3(-30, 0, -40), new Vector3(x + 30, 0, 40)));
        var renderer = new Renderer(meshes);
        float cx = (x - 11) / 2;
        float dist = Environment.GetEnvironmentVariable("CAMCLOSE") == "1" ? Math.Max(7, x * 0.45f) : Math.Max(14, x * 0.75f); // CAMCLOSE=1: closer
        var cameras = new[]
        {
            new Camera { Name = "front", Position = new Vector3(cx, 6, -dist), Target = new Vector3(cx, 5, 0), FieldOfView = 50 },
            Environment.GetEnvironmentVariable("CAMCLOSE") == "1"
                ? new Camera { Name = "angle", Position = new Vector3(cx + (dist * 0.35f), dist * 0.18f, -dist * 0.55f), Target = new Vector3(cx, 0.8f, 0), FieldOfView = 40 }
                : new Camera { Name = "angle", Position = new Vector3(cx + (dist * 0.5f), dist * 0.35f, -dist * 0.7f), Target = new Vector3(cx, 3, 0), FieldOfView = 50 },
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

    /// <summary>
    /// Views of the villages: riding in (from 420 m out, rider's eye) and from above; names separated by commas
    /// ("all" = every village).
    /// </summary>
    public static void Places(string output, string names)
    {
        var sw = Stopwatch.StartNew();
        var map = WorldMap.Default;
        var terrain = Terrain.Build(map);
        var layout = SceneryLayout.Generate(terrain, new DateTime(2026, 5, 10));
        var landscape = terrain.Landscape!;
        Console.WriteLine($"world + landscape in {sw.ElapsedMilliseconds} ms: {landscape.Villages.Count} villages, {landscape.Items.Count} items, {landscape.Fields.Count} fields, {landscape.Pastures.Count} pastures");
        foreach (var g in landscape.Items.GroupBy(i => i.Kind)) Console.Write($" {g.Key} {g.Count()}");
        Console.WriteLine();
        foreach (var t in landscape.Items.Where(i => i.Kind is SceneryKind.Workshop or SceneryKind.Sawmill or SceneryKind.FireStation))
        {
            var v0 = landscape.Villages.OrderBy(v => Math.Pow(v.X - t.X, 2) + Math.Pow(v.Z - t.Z, 2)).First();
            Console.WriteLine($"  {t.Kind} near {v0.Name} at ({t.X:0},{t.Z:0})");
        }

        var wanted = names == "all" ? landscape.Villages.Select(v => v.Name).ToList() : names.Split(',').ToList();
        // "trade:Kind" = a view of the first such building, from the road side, close up.
        if (wanted[0].StartsWith("trade:"))
        {
            var kinds = wanted.Select(w => Enum.Parse<SceneryKind>(w.Substring(6))).ToList();
            const int sw2 = 2, ww = 960, hh = 540;
            var shots = new List<byte[]>();
            foreach (var kind in kinds)
            {
                foreach (var t in landscape.Items.Where(i => i.Kind == kind && i.Y >= double.Parse(Environment.GetEnvironmentVariable("MINH") ?? "0") && (Environment.GetEnvironmentVariable("MINH") == null || map.Nodes.All(n => Math.Abs(n.X - i.X) + Math.Abs(n.Z - i.Z) > 350))).Take(4))
                {
                    double r = t.RotationDeg * Math.PI / 180;
                    var front = new Vector3((float)Math.Sin(r), 0, (float)Math.Cos(r));
                    var side = new Vector3(front.Z, 0, -front.X);
                    var at = new Vector3(t.X, t.Y, t.Z);
                    bool small = kind is SceneryKind.DirectionSign or SceneryKind.VillageSign;
                    var cam = small
                        ? new Camera { Name = kind.ToString(), Position = at + (front * 7) + (side * 2.5f) + new Vector3(0, 2.2f, 0), Target = at + new Vector3(0, 2.1f, 0), FieldOfView = 50 }
                        : new Camera { Name = kind.ToString(), Position = at + (front * 34) + (side * 18) + new Vector3(0, 14, 0), Target = at + new Vector3(0, 3, 0), FieldOfView = 55 };
                    var meshes = SceneMeshes(map, terrain, layout, cam.Position, 600);
                    shots.Add(Renderer.ToImage(new Renderer(meshes, shadowCenter: at, shadowRadius: 120).Render(cam, ww * sw2, hh * sw2), ww * sw2, hh * sw2, sw2, out _, out _));
                }
            }

            int rows2 = (shots.Count + 1) / 2;
            var sheet2 = new byte[2 * ww * rows2 * hh * 3];
            for (int t = 0; t < shots.Count; t++)
            {
                int ox = (t % 2) * ww, oy = (t / 2) * hh;
                for (int y = 0; y < hh; y++) Buffer.BlockCopy(shots[t], y * ww * 3, sheet2, (((oy + y) * 2 * ww) + ox) * 3, ww * 3);
            }

            Renderer.WritePng(output, sheet2, 2 * ww, rows2 * hh);
            Console.WriteLine($"-> {output}");
            return;
        }

        const int ss = 2, w = 960, h = 540;
        var tiles = new List<byte[]>();
        foreach (var name in wanted)
        {
            var v = landscape.Villages.First(x => x.Name == name);
            var node = map.Nodes[v.Node];
            var e = map.Edges[node.Edges[0]];
            bool outward = e.From == node.Id;
            double s = Math.Min(e.Length - 5, 420), d = outward ? s : e.Length - s;
            int i = Array.FindIndex(e.D, x => x >= d);
            var p = new Vector3((float)e.X[i], (float)e.E[i], (float)e.Z[i]);
            var toward = Vector3.Normalize(new Vector3((float)(node.X - p.X), 0, (float)(node.Z - p.Z)));
            Vector3 Pos(RoadEdge edge, double at)
            {
                int k = Math.Max(0, Array.FindIndex(edge.D, x => x >= at));
                return new Vector3((float)edge.X[k], (float)edge.E[k], (float)edge.Z[k]);
            }

            var right = new Vector3(toward.Z, 0, -toward.X);
            var cams = new[]
            {
                new Camera { Name = name + " in", Position = p + (right * 1.4f) + new Vector3(0, 1.9f, 0), Target = p + (toward * 120) + new Vector3(0, 6f, 0), FieldOfView = 60 },
                new Camera { Name = name + " street", Position = Pos(e, outward ? 140 : e.Length - 140) + new Vector3(0, 1.9f, 0), Target = Pos(e, outward ? 60 : e.Length - 60) + new Vector3(0, 2.5f, 0), FieldOfView = 60 },
                new Camera { Name = name + " close", Position = Pos(e, outward ? 110 : e.Length - 110) + (right * 30) + new Vector3(0, 28, 0), Target = Pos(e, outward ? 70 : e.Length - 70), FieldOfView = 55 },
                new Camera { Name = name + " above", Position = new Vector3((float)node.X, (float)node.Elevation, (float)node.Z) - (toward * 330) + new Vector3(0, 170, 0), Target = new Vector3((float)node.X, (float)node.Elevation, (float)node.Z) + (toward * 60), FieldOfView = 55 },
            };
            foreach (var camera in cams)
            {
                var meshes = SceneMeshes(map, terrain, layout, camera.Position, 1100);
                var renderer = new Renderer(meshes, shadowCenter: camera.Position, shadowRadius: 400);
                tiles.Add(Renderer.ToImage(renderer.Render(camera, w * ss, h * ss), w * ss, h * ss, ss, out _, out _));
                Console.WriteLine($"  view {camera.Name}: {meshes.Sum(m => m.Triangles.Length / 3) / 1000}k triangles");
            }
        }

        int cols = 2, rows = (tiles.Count + 1) / 2;
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

    private static List<(double X, double Z)> Hull(List<(double X, double Z)> points)
    {
        var p = points.OrderBy(v => v.X).ThenBy(v => v.Z).ToList();
        var hull = new List<(double X, double Z)>();
        if (p.Count < 3) return p;
        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            foreach (var v in p)
            {
                while (hull.Count >= start + 2 && (((hull[^1].X - hull[^2].X) * (v.Z - hull[^2].Z)) - ((hull[^1].Z - hull[^2].Z) * (v.X - hull[^2].X))) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(v);
            }

            hull.RemoveAt(hull.Count - 1);
            p.Reverse();
        }

        return hull;
    }

    /// <summary>Eight views orbiting the rider at the start of the default route, like the app's home screen.</summary>
    public static void Start(string output)
    {
        var map = WorldMap.Default;
        var terrain = Terrain.Build(map);
        var layout = SceneryLayout.Generate(terrain, new DateTime(2026, 11, 10));
        var e = map.Edges[map.MainLoopEdges[0]];
        int node = e.From;
        var n = map.Nodes[node];
        double fx = e.X[2] - e.X[0], fz = e.Z[2] - e.Z[0], fl = Math.Sqrt((fx * fx) + (fz * fz));
        var forward = new Vector3((float)(fx / fl), 0, (float)(fz / fl));
        var right = new Vector3(forward.Z, 0, -forward.X);
        var rider = new Vector3((float)e.X[0], (float)e.E[0] + 0.05f, (float)e.Z[0]) + (right * 1.5f);
        const int ss = 2, w = 640, h = 360;
        var tiles = new List<byte[]>();
        var meshes = SceneMeshes(map, terrain, layout, rider, 600);
        var renderer = new Renderer(meshes, shadowCenter: rider, shadowRadius: 120);
        for (int k = 0; k < 8; k++)
        {
            double a = k * Math.PI / 4;
            var offset = new Vector3((float)(Math.Sin(a) * -3.6), 1.5f, (float)(Math.Cos(a) * -3.6));
            var cam = new Camera { Name = $"orbit {k * 45}", Position = rider + offset, Target = rider + new Vector3(0, 1.05f, 0), FieldOfView = 60 };
            tiles.Add(Renderer.ToImage(renderer.Render(cam, w * ss, h * ss), w * ss, h * ss, ss, out _, out _));
        }

        int cols = 2, rows = 4;
        var sheet = new byte[cols * w * rows * h * 3];
        for (int t = 0; t < tiles.Count; t++)
        {
            int ox = (t % cols) * w, oy = (t / cols) * h;
            for (int y = 0; y < h; y++) Buffer.BlockCopy(tiles[t], y * w * 3, sheet, (((oy + y) * cols * w) + ox) * 3, w * 3);
        }

        Renderer.WritePng(output, sheet, cols * w, rows * h);
        Console.WriteLine($"start at node {n.Name} ({rider.X:0},{rider.Z:0}) -> {output}");
    }

    private static List<SceneMesh> SceneMeshes(WorldMap map, Terrain terrain, SceneryLayout layout, Vector3 eye, double radius, double detailRadius = 110)
    {
        var meshes = new List<SceneMesh>();

        // Terrain anywhere (home grid or streamed ground), 4 m close by, 12 m further away, coloured by ground cover.
        var mt = terrain.Mountain!;
        int natureSeed = SceneryLayout.NatureSeed(map);
        var coverColor = new Dictionary<GroundKind, Vector3>
        {
            [GroundKind.Grass] = new Vector3(0.36f, 0.5f, 0.22f),
            [GroundKind.Forest] = new Vector3(0.16f, 0.27f, 0.15f),
            [GroundKind.Meadow] = new Vector3(0.52f, 0.58f, 0.3f),
            [GroundKind.Rock] = new Vector3(0.47f, 0.46f, 0.44f),
            [GroundKind.Snow] = new Vector3(0.95f, 0.97f, 1f),
            [GroundKind.Wheat] = WorldMaterials.All[WorldMaterial.Wheat].Color,
            [GroundKind.Crop] = WorldMaterials.All[WorldMaterial.Crop].Color,
            [GroundKind.Plowed] = WorldMaterials.All[WorldMaterial.Plowed].Color,
        };
        foreach (var (inner, outer, cell) in detailRadius > 0 ? new[] { (0.0, 260.0, 4.0), (240.0, radius, 12.0) } : new[] { (0.0, radius, 16.0) })
        {
            int n = (int)(2 * outer / cell) + 1;
            double x0 = Math.Floor((eye.X - outer) / cell) * cell, z0 = Math.Floor((eye.Z - outer) / cell) * cell;
            var heights = new float[n * n];
            Parallel.For(0, n, j2 =>
            {
                for (int i2 = 0; i2 < n; i2++)
                {
                    double x = x0 + (i2 * cell), z = z0 + (j2 * cell);
                    heights[(j2 * n) + i2] = (float)Math.Min(terrain.Height(x, z), terrain.RoadCap(x, z, cell)) - (cell > 4 ? 0.4f : 0);
                }
            });

            var byCover = new Dictionary<GroundKind, (List<Vector3> P, List<Vector3> N, List<int> T)>();
            for (int j2 = 0; j2 < n - 1; j2++)
            {
                for (int i2 = 0; i2 < n - 1; i2++)
                {
                    double x = x0 + ((i2 + 0.5) * cell), z = z0 + ((j2 + 0.5) * cell);
                    double r = Math.Sqrt(((x - eye.X) * (x - eye.X)) + ((z - eye.Z) * (z - eye.Z)));
                    if (r < inner || r > outer) continue;
                    float h00 = heights[(j2 * n) + i2], h10 = heights[(j2 * n) + i2 + 1], h01 = heights[((j2 + 1) * n) + i2], h11 = heights[((j2 + 1) * n) + i2 + 1];
                    double slope = Math.Sqrt(((h10 - h00) * (h10 - h00)) + ((h01 - h00) * (h01 - h00))) / cell;
                    var kind = MountainLook.Ground(mt, x, z, (h00 + h11) / 2, slope, natureSeed, distant: cell > 8, terrain.Landscape);
                    if (!byCover.TryGetValue(kind, out var m)) byCover[kind] = m = (new List<Vector3>(), new List<Vector3>(), new List<int>());
                    var nrm = Vector3.Normalize(new Vector3(-(h10 - h00 + h11 - h01) / 2, (float)cell, -(h01 - h00 + h11 - h10) / 2));
                    int k = m.P.Count;
                    m.P.Add(new Vector3((float)(x - (cell / 2)), h00, (float)(z - (cell / 2))));
                    m.P.Add(new Vector3((float)(x + (cell / 2)), h10, (float)(z - (cell / 2))));
                    m.P.Add(new Vector3((float)(x - (cell / 2)), h01, (float)(z + (cell / 2))));
                    m.P.Add(new Vector3((float)(x + (cell / 2)), h11, (float)(z + (cell / 2))));
                    for (int q = 0; q < 4; q++) m.N.Add(nrm);
                    m.T.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
                }
            }

            foreach (var (kind, m) in byCover) meshes.Add(new SceneMesh { Positions = m.P.ToArray(), Normals = m.N.ToArray(), Triangles = m.T.ToArray(), Albedo = coverColor[kind], CastsShadow = false });
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

        // Junctions as the app paves them: rounded corners and the pad between the road ends.
        {
            var pos = new List<Vector3>();
            var tri = new List<int>();
            void Fan(Vector3 c, IList<Vector3> rim)
            {
                int k = pos.Count;
                pos.Add(c);
                pos.AddRange(rim);
                for (int i = 0; i < rim.Count; i++)
                {
                    int a2 = k + 1 + i, b2 = k + 1 + ((i + 1) % rim.Count);
                    var n = Vector3.Cross(pos[a2] - c, pos[b2] - c);
                    if (n.Y > 0) tri.AddRange(new[] { k, a2, b2 }); else tri.AddRange(new[] { k, b2, a2 });
                }
            }

            foreach (var corner in terrain.Corners)
            {
                var rim = new List<Vector3>();
                for (int j = 0; j < corner.X.Length; j++)
                {
                    float y = Math.Max((float)corner.Y[j] + 0.05f, (float)terrain.Height(corner.X[j], corner.Z[j]) + 0.06f) + 0.004f;
                    rim.Add(new Vector3((float)corner.X[j], y, (float)corner.Z[j]));
                }

                Fan(new Vector3((float)corner.CX, (float)corner.CY + 0.054f, (float)corner.CZ), rim);
            }

            foreach (var node in map.Nodes)
            {
                var pts = new List<(double X, double Z)>();
                foreach (int id in node.Edges)
                {
                    var e = map.Edges[id];
                    bool start = e.From == node.Id;
                    foreach (double sd in new[] { 0.0, 2.0 })
                    {
                        foreach (int side in new[] { -1, 1 }) { var q = JunctionCorners.Side(e, start, sd, side, 0); pts.Add((q.X, q.Z)); }
                    }
                }

                var hull = Hull(pts);
                if (hull.Count >= 3) Fan(new Vector3((float)node.X, (float)node.Elevation + 0.06f, (float)node.Z), hull.Select(h => new Vector3((float)h.X, (float)node.Elevation + 0.06f, (float)h.Z)).ToList());
            }

            var color = Environment.GetEnvironmentVariable("MARKJ") == "1" ? new Vector3(0.75f, 0.15f, 0.15f) : new Vector3(0.3f, 0.3f, 0.31f);
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

        // Scenery, merged per material
        var merged = new Dictionary<WorldMaterial, (List<Vector3> P, List<Vector3> N, List<int> T)>();
        var normalCache = new Dictionary<MeshData, Vector3[]>();
        var sceneItems = new List<SceneryItem>();
        int cr = (int)Math.Ceiling(radius / MountainLook.ChunkSize);
        int ecx = (int)Math.Floor(eye.X / MountainLook.ChunkSize), ecz = (int)Math.Floor(eye.Z / MountainLook.ChunkSize);
        for (int cx = ecx - cr; cx <= ecx + cr; cx++)
        {
            for (int cz = ecz - cr; cz <= ecz + cr; cz++) sceneItems.AddRange(MountainLook.Items(terrain, cx, cz, natureSeed));
        }

        foreach (var item in sceneItems)
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
        or WorldMaterial.Bush or WorldMaterial.BushFlowering or WorldMaterial.Rock or WorldMaterial.LarchNeedles or WorldMaterial.FirNeedles
        or WorldMaterial.StonePineNeedles or WorldMaterial.BeechLeaves or WorldMaterial.MapleLeaves or WorldMaterial.LindenLeaves or WorldMaterial.AlderLeaves
        or WorldMaterial.WillowLeaves or WorldMaterial.AppleLeaves;

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
