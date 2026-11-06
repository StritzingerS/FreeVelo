using System.Diagnostics;
using System.Numerics;
using FreeVelo.Core.Avatar;
using FreeVelo.Core.Physics;
using FreeVelo.Core.World.Gen;

namespace FreeVelo.ModelPreview;

/// <summary>Top-down map of the generated world: height shading, roads, river, lakes, junctions, bridges.</summary>
public static class WorldPreview
{
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

        foreach (var l in map.Lakes) Console.WriteLine($"  lake at ({l.X:0},{l.Z:0}) r {l.Radius:0} level {l.Level:0.0}");
        foreach (var b in map.Bridges) Console.WriteLine($"  bridge edge {b.Edge} at {b.DistanceM:0} m, water {b.WaterLevel:0.0}, road {Sample(map.Edges[b.Edge], b.DistanceM):0.0}");
        var loop = map.MainLoop;
        Console.WriteLine($"  main loop {loop.LengthM:0} m, ascent {loop.TotalAscentM:0} m");

        var (minX, minZ, maxX, maxZ) = map.Bounds;
        double margin = 500;
        minX -= margin; minZ -= margin; maxX += margin; maxZ += margin;
        const int size = 1000;
        double scale = size / Math.Max(maxX - minX, maxZ - minZ);
        var rgb = new byte[size * size * 3];
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                double x = minX + (px / scale), z = maxZ - (py / scale);
                double h = map.BaseHeight(x, z);
                double hx = map.BaseHeight(x + 5, z) - h;
                double shade = Math.Clamp(0.55 + ((h - 100) / 70) - (hx * 0.6), 0.15, 1);
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
            float spacing = kind is SceneryKind.House or SceneryKind.Barn ? 22 : kind is SceneryKind.Bush or SceneryKind.Rock or SceneryKind.Reed ? 4 : 11;
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
