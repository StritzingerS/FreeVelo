using System.Diagnostics;
using System.Numerics;
using FreeVelo.Core.Avatar;
using FreeVelo.ModelPreview;

// Renders the procedural rider/bike (exactly the geometry Unity uses) from several cameras into one PNG.
// Usage: dotnet run -c Release -- out.png [crankDegrees] [views: game,side,rear34,front34,face,drive,cockpit,top]
if (args.Length > 1 && args[0] == "--bake")
{
    var baked = AvatarModel.Build().Serialize();
    File.WriteAllBytes(args[1], baked);
    var check = AvatarModel.Deserialize(baked);
    Console.WriteLine($"baked {check.TriangleCount} triangles, {baked.Length / 1024} KiB -> {args[1]}");
    return;
}

string output = args.Length > 0 ? args[0] : "avatar.png";
float crank = args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 110f;
string[] wanted = args.Length > 2 ? args[2].Split(',') : new[] { "game", "side", "rear34", "front34", "face", "drive" };

var sw = Stopwatch.StartNew();
var model = File.Exists(Environment.GetEnvironmentVariable("FREEVELO_BAKED") ?? "")
    ? AvatarModel.Deserialize(File.ReadAllBytes(Environment.GetEnvironmentVariable("FREEVELO_BAKED")!))
    : AvatarModel.Build();
var pose = AvatarModel.Pose(crank, 37f);
var meshes = new List<SceneMesh>();
foreach (var part in model.Parts)
{
    var p = pose[part.Node];
    var normals = part.Mesh.ComputeNormals();
    meshes.Add(new SceneMesh
    {
        Positions = part.Mesh.Vertices.Select(v => p.Apply(v)).ToArray(),
        Normals = normals.Select(n => Vector3.Normalize(p.Rotation * n)).ToArray(),
        Triangles = part.Mesh.Triangles.ToArray(),
        Albedo = part.Material.Color,
        Smoothness = part.Material.Smoothness,
        Metallic = part.Material.Metallic,
    });
}

// Road under the bike (receives shadows) and grass beyond.
meshes.Add(Quad(new Vector3(-3.25f, 0, -8), new Vector3(3.25f, 0, 8), new Vector3(0.24f, 0.24f, 0.26f), 0.1f));
meshes.Add(Quad(new Vector3(-15, -0.02f, -15), new Vector3(15, -0.02f, 25), new Vector3(0.33f, 0.5f, 0.24f), 0f));

var cameras = new Dictionary<string, Camera>
{
    ["game"] = new() { Name = "game", Position = new Vector3(0, 1.9f, -4.6f), Target = new Vector3(0, 1.05f, 0), FieldOfView = 60 },
    ["side"] = new() { Name = "side", Position = new Vector3(-3.6f, 0.95f, 0.08f), Target = new Vector3(0, 0.82f, 0.08f), FieldOfView = 34 },
    ["rear34"] = new() { Name = "rear34", Position = new Vector3(-2.3f, 1.55f, -2.6f), Target = new Vector3(0, 0.95f, 0), FieldOfView = 34 },
    ["front34"] = new() { Name = "front34", Position = new Vector3(2.4f, 1.35f, 2.7f), Target = new Vector3(0, 0.9f, 0.1f), FieldOfView = 34 },
    ["face"] = new() { Name = "face", Position = new Vector3(0.75f, 1.62f, 1.45f), Target = new Vector3(0, 1.38f, 0.32f), FieldOfView = 28 },
    ["drive"] = new() { Name = "drive", Position = new Vector3(1.45f, 0.62f, 0.05f), Target = new Vector3(0, 0.42f, -0.12f), FieldOfView = 38 },
    ["cockpit"] = new() { Name = "cockpit", Position = new Vector3(0.55f, 1.25f, 1.25f), Target = new Vector3(0, 0.86f, 0.5f), FieldOfView = 34 },
    ["top"] = new() { Name = "top", Position = new Vector3(0.01f, 3.4f, 0.1f), Target = new Vector3(0, 0.8f, 0.05f), FieldOfView = 34 },
    ["hipside"] = new() { Name = "hipside", Position = new Vector3(-1.9f, 1.0f, -0.05f), Target = new Vector3(0, 0.98f, -0.05f), FieldOfView = 30 },
    ["hipback"] = new() { Name = "hipback", Position = new Vector3(-0.9f, 1.25f, -1.5f), Target = new Vector3(0, 1.0f, -0.15f), FieldOfView = 30 },
    ["back"] = new() { Name = "back", Position = new Vector3(0, 1.25f, -2.4f), Target = new Vector3(0, 1.0f, 0), FieldOfView = 34 },
};

var renderer = new Renderer(meshes);
const int ss = 2, w = 900, h = 640;
var tiles = new List<byte[]>();
foreach (var name in wanted)
{
    var hdr = renderer.Render(cameras[name], w * ss, h * ss);
    tiles.Add(Renderer.ToImage(hdr, w * ss, h * ss, ss, out _, out _));
}

int cols = Math.Min(3, tiles.Count), rows = (tiles.Count + cols - 1) / cols;
var sheet = new byte[cols * w * rows * h * 3];
Array.Fill(sheet, (byte)255);
for (int t = 0; t < tiles.Count; t++)
{
    int ox = (t % cols) * w, oy = (t / cols) * h;
    for (int y = 0; y < h; y++) Buffer.BlockCopy(tiles[t], y * w * 3, sheet, (((oy + y) * cols * w) + ox) * 3, w * 3);
}

Renderer.WritePng(output, sheet, cols * w, rows * h);
Console.WriteLine($"{model.TriangleCount} triangles, {tiles.Count} views, {sw.ElapsedMilliseconds} ms -> {output}");

static SceneMesh Quad(Vector3 min, Vector3 max, Vector3 color, float smoothness)
{
    // Subdivided so no triangle crosses the camera plane (the rasterizer drops those instead of clipping).
    const float cell = 0.5f;
    int nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / cell)), nz = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z) / cell));
    var pos = new List<Vector3>();
    var tri = new List<int>();
    for (int i = 0; i <= nx; i++)
    {
        for (int j = 0; j <= nz; j++) pos.Add(new Vector3(min.X + ((max.X - min.X) * i / nx), min.Y, min.Z + ((max.Z - min.Z) * j / nz)));
    }

    for (int i = 0; i < nx; i++)
    {
        for (int j = 0; j < nz; j++)
        {
            int a = (i * (nz + 1)) + j, b = a + 1, c = a + nz + 1, d = c + 1;
            tri.AddRange(new[] { a, b, d, a, d, c });
        }
    }

    return new SceneMesh
    {
        Positions = pos.ToArray(),
        Normals = Enumerable.Repeat(Vector3.UnitY, pos.Count).ToArray(),
        Triangles = tri.ToArray(),
        Albedo = color,
        Smoothness = smoothness,
        CastsShadow = false,
    };
}
