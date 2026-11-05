using System.Diagnostics;
using System.Numerics;
using FreeVelo.Core.Avatar;
using FreeVelo.ModelPreview;

// Renders the procedural rider/bike (exactly the geometry Unity uses) from several cameras into one PNG.
// Usage: dotnet run -c Release -- out.png [crankDegrees] [views: game,side,rear34,front34,face,drive,cockpit,top]
if (args.Length > 1 && args[0] == "--bake")
{
    // args[1] = Resources folder: high detail and a low-detail LOD for distant riders.
    foreach (var (file, detail) in new[] { ("FreeVeloAvatar.bytes", 1f), ("FreeVeloAvatarLow.bytes", 0.3f) })
    {
        var baked = AvatarModel.Build(detail: detail).Serialize();
        var path = Path.Combine(args[1], file);
        File.WriteAllBytes(path, baked);
        var check = AvatarModel.Deserialize(baked);
        Console.WriteLine($"baked {check.TriangleCount} triangles, {baked.Length / 1024} KiB -> {path}");
    }

    return;
}

string output = args.Length > 0 ? args[0] : "avatar.png";
float crank = args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 110f;
string[] wanted = args.Length > 2 ? args[2].Split(',') : new[] { "game", "side", "rear34", "front34", "face", "drive" };

var sw = Stopwatch.StartNew();
float previewDetail = float.Parse(Environment.GetEnvironmentVariable("FREEVELO_DETAIL") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
var model = File.Exists(Environment.GetEnvironmentVariable("FREEVELO_BAKED") ?? "")
    ? AvatarModel.Deserialize(File.ReadAllBytes(Environment.GetEnvironmentVariable("FREEVELO_BAKED")!))
    : AvatarModel.Build(detail: previewDetail);
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
    ["chain"] = new() { Name = "chain", Position = new Vector3(0.62f, 0.45f, -0.12f), Target = new Vector3(0.06f, 0.3f, -0.2f), FieldOfView = 45 },
    ["rd"] = new() { Name = "rd", Position = new Vector3(0.38f, 0.3f, -0.3f), Target = new Vector3(0.07f, 0.28f, -0.39f), FieldOfView = 40 },
    ["bars"] = new() { Name = "bars", Position = new Vector3(-0.55f, 1.0f, 0.95f), Target = new Vector3(0, 0.84f, 0.52f), FieldOfView = 40 },
    ["wheel"] = new() { Name = "wheel", Position = new Vector3(-1.1f, 0.5f, 0.75f), Target = new Vector3(0, 0.34f, 0.56f), FieldOfView = 40 },
    ["downtube"] = new() { Name = "downtube", Position = new Vector3(-0.45f, 0.6f, 0.45f), Target = new Vector3(0, 0.52f, 0.28f), FieldOfView = 40 },
    ["teleback"] = new() { Name = "teleback", Position = new Vector3(0.3f, 1.3f, -9f), Target = new Vector3(0, 0.85f, 0), FieldOfView = 11 },
    ["telefront"] = new() { Name = "telefront", Position = new Vector3(-0.6f, 1.6f, 9f), Target = new Vector3(0, 0.8f, 0), FieldOfView = 11 },
    ["chase"] = new() { Name = "chase", Position = new Vector3(0.9f, 2.0f, -3.9f), Target = new Vector3(0.05f, 0.95f, 0.1f), FieldOfView = 32 },
    ["chaseleft"] = new() { Name = "chaseleft", Position = new Vector3(-1.6f, 1.7f, -3.2f), Target = new Vector3(0, 0.85f, 0.1f), FieldOfView = 32 },
    ["reartop"] = new() { Name = "reartop", Position = new Vector3(0.25f, 1.0f, -0.75f), Target = new Vector3(0.03f, 0.33f, -0.4f), FieldOfView = 34 },
    ["portrait"] = new() { Name = "portrait", Position = new Vector3(0.28f, 1.5f, 0.95f), Target = new Vector3(0, 1.49f, 0.44f), FieldOfView = 22 },
    ["profile"] = new() { Name = "profile", Position = new Vector3(-0.75f, 1.5f, 0.45f), Target = new Vector3(0, 1.48f, 0.42f), FieldOfView = 22 },
    ["nose"] = new() { Name = "nose", Position = new Vector3(-0.32f, 1.47f, 0.72f), Target = new Vector3(0, 1.47f, 0.5f), FieldOfView = 18 },
    ["nose2"] = new() { Name = "nose2", Position = new Vector3(-0.38f, 1.38f, 0.62f), Target = new Vector3(0, 1.47f, 0.5f), FieldOfView = 18 },
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
