using System.Diagnostics;
using System.Numerics;
using FreeVelo.Core.Avatar;
using FreeVelo.ModelPreview;

// Renders the procedural rider/bike (exactly the geometry Unity uses) from several cameras into one PNG.
// Usage: dotnet run -c Release -- out.png [crankDegrees] [views: game,side,rear34,front34,face,drive,cockpit,top]
if (args.Length > 1 && args[0] == "--scenery")
{
    WorldPreview.Gallery(args[1], args.Length > 2 ? int.Parse(args[2]) : 0, args.Length > 3 ? args[3] : null);
    return;
}

if (args.Length > 1 && args[0] == "--world")
{
    var at = args.Skip(2).Select(a => double.Parse(a, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    WorldPreview.Scene(args[1], at.Length > 0 ? at : new[] { 300.0, 1840, 4200, -5000 });
    return;
}

if (args.Length > 1 && args[0] == "--top")
{
    WorldPreview.Top(args[1], args.Length > 2 ? int.Parse(args[2]) : 1600);
    return;
}

if (args.Length > 2 && args[0] == "--places")
{
    WorldPreview.Places(args[1], args[2]);
    return;
}

if (args.Length > 1 && args[0] == "--export")
{
    WorldPreview.Export(args[1]);
    return;
}

if (args.Length > 1 && args[0] == "--map")
{
    WorldPreview.Map(args[1], args.Length > 2 ? int.Parse(args[2]) : FreeVelo.Core.World.Gen.WorldMap.DefaultSeed);
    return;
}

if (args.Length > 1 && args[0] == "--icons")
{
    // Icon sheet for checking the procedural UI icons: dark background, white icons at 96 px and 32 px.
    var icons = Enum.GetValues<FreeVelo.Core.Ui.Icon>();
    const int big = 96, small = 32, cell = 140;
    int iconCols = 8, rowsN = (icons.Length + iconCols - 1) / iconCols;
    int sw2 = iconCols * cell, sh2 = rowsN * cell;
    var rgb2 = new byte[sw2 * sh2 * 3];
    for (int i = 0; i < rgb2.Length; i += 3) { rgb2[i] = 22; rgb2[i + 1] = 27; rgb2[i + 2] = 34; }
    for (int i = 0; i < icons.Length; i++)
    {
        int ox = (i % iconCols) * cell, oy = (i / iconCols) * cell;
        void Blit(float[] a, int size, int x0, int y0)
        {
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float v = a[(y * size) + x];
                int o = (((y0 + y) * sw2) + x0 + x) * 3;
                for (int c = 0; c < 3; c++) rgb2[o + c] = (byte)((rgb2[o + c] * (1 - v)) + (240 * v));
            }
        }

        Blit(FreeVelo.Core.Ui.IconPainter.Render(icons[i], big), big, ox + 8, oy + 8);
        Blit(FreeVelo.Core.Ui.IconPainter.Render(icons[i], small), small, ox + 106, oy + 8);
    }

    Renderer.WritePng(args[1], rgb2, sw2, sh2);
    Console.WriteLine($"{icons.Length} icons -> {args[1]}");
    return;
}

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
    : Environment.GetEnvironmentVariable("FREEVELO_HEAD") is { } headMode
        ? AvatarModel.BuildHeadStudy(glasses: !headMode.Contains("noglasses"), helmet: !headMode.Contains("nohelmet"))
        : AvatarModel.Build(detail: previewDetail);
var pose = AvatarModel.Pose(crank, 37f, Stopped());
var meshes = new List<SceneMesh>();
bool noRider = Environment.GetEnvironmentVariable("FREEVELO_NORIDER") == "1";
// Optional lean (degrees, positive = into a right-hand bend), as the game applies it.
var lean = Rot.AboutZ(-(float.Parse(Environment.GetEnvironmentVariable("FREEVELO_LEAN") ?? "0", System.Globalization.CultureInfo.InvariantCulture) + AvatarModel.StopLeanDeg(Stopped())));
foreach (var part in model.Parts)
{
    if (noRider && !AvatarModel.IsBikeNode(part.Node)) continue;
    var p = pose[part.Node];
    var normals = part.Mesh.ComputeNormals();
    meshes.Add(new SceneMesh
    {
        Positions = part.Mesh.Vertices.Select(v => lean * p.Apply(v)).ToArray(),
        Normals = normals.Select(n => Vector3.Normalize(lean * (p.Rotation * n))).ToArray(),
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
    ["zw"] = new() { Name = "zw", Position = new Vector3(0.2f, 1.44f, 0.86f), Target = new Vector3(0, 1.47f, 0.42f), FieldOfView = 24 },
    ["zwfront"] = new() { Name = "zwfront", Position = new Vector3(0, 1.43f, 0.9f), Target = new Vector3(0, 1.47f, 0.42f), FieldOfView = 24 },
    ["zwside"] = new() { Name = "zwside", Position = new Vector3(0.55f, 1.47f, 0.5f), Target = new Vector3(0, 1.47f, 0.42f), FieldOfView = 26 },
    ["fc1"] = new() { Name = "fc1", Position = new Vector3(0.16f, 1.44f, 0.72f), Target = new Vector3(0, 1.465f, 0.44f), FieldOfView = 26 },
    ["fc2"] = new() { Name = "fc2", Position = new Vector3(-0.2f, 1.42f, 0.7f), Target = new Vector3(0, 1.465f, 0.44f), FieldOfView = 26 },
    ["fc3"] = new() { Name = "fc3", Position = new Vector3(0.34f, 1.46f, 0.47f), Target = new Vector3(0, 1.465f, 0.43f), FieldOfView = 30 },
    ["brakefront"] = new() { Name = "brakefront", Position = new Vector3(-0.42f, 0.55f, 0.42f), Target = new Vector3(-0.045f, 0.36f, 0.56f), FieldOfView = 34 },
    ["brakerear"] = new() { Name = "brakerear", Position = new Vector3(-0.42f, 0.55f, -0.18f), Target = new Vector3(-0.06f, 0.36f, -0.38f), FieldOfView = 34 },
    ["braketop"] = new() { Name = "braketop", Position = new Vector3(-0.12f, 0.75f, -0.3f), Target = new Vector3(-0.06f, 0.34f, -0.4f), FieldOfView = 34 },
    ["forktop"] = new() { Name = "forktop", Position = new Vector3(-0.12f, 0.8f, 0.68f), Target = new Vector3(-0.045f, 0.34f, 0.56f), FieldOfView = 34 },
    ["calf"] = new() { Name = "calf", Position = new Vector3(-0.26f, 0.45f, 0.42f), Target = new Vector3(-0.05f, 0.39f, 0.51f), FieldOfView = 30 },
    ["calr"] = new() { Name = "calr", Position = new Vector3(-0.27f, 0.47f, -0.24f), Target = new Vector3(-0.065f, 0.39f, -0.355f), FieldOfView = 30 },
    ["fbtop"] = new() { Name = "fbtop", Position = new Vector3(-0.06f, 0.75f, 0.53f), Target = new Vector3(-0.05f, 0.38f, 0.54f), FieldOfView = 16 },
    ["fbback"] = new() { Name = "fbback", Position = new Vector3(-0.07f, 0.42f, 0.2f), Target = new Vector3(-0.05f, 0.38f, 0.55f), FieldOfView = 20 },
    ["fbin"] = new() { Name = "fbin", Position = new Vector3(0.2f, 0.42f, 0.52f), Target = new Vector3(-0.05f, 0.38f, 0.56f), FieldOfView = 30 },
    ["forktip"] = new() { Name = "forktip", Position = new Vector3(-0.3f, 0.42f, 0.66f), Target = new Vector3(-0.055f, 0.35f, 0.6f), FieldOfView = 26 },
    ["bar1"] = new() { Name = "bar1", Position = new Vector3(-0.45f, 1.0f, 0.82f), Target = new Vector3(0.03f, 0.84f, 0.5f), FieldOfView = 34 },
    ["bar2"] = new() { Name = "bar2", Position = new Vector3(0.35f, 0.95f, 0.95f), Target = new Vector3(0.05f, 0.84f, 0.53f), FieldOfView = 34 },
    ["bar3"] = new() { Name = "bar3", Position = new Vector3(-0.55f, 0.85f, 0.55f), Target = new Vector3(0, 0.83f, 0.5f), FieldOfView = 30 },
    ["ph1"] = new() { Name = "ph1", Position = new Vector3(-0.32f, 0.98f, 0.72f), Target = new Vector3(0.06f, 0.85f, 0.5f), FieldOfView = 40 },
    ["ph2"] = new() { Name = "ph2", Position = new Vector3(-0.12f, 0.93f, 1.0f), Target = new Vector3(0.02f, 0.84f, 0.55f), FieldOfView = 34 },
    ["ph3"] = new() { Name = "ph3", Position = new Vector3(-0.42f, 0.86f, 0.56f), Target = new Vector3(-0.2f, 0.83f, 0.56f), FieldOfView = 40 },
    ["shside"] = new() { Name = "shside", Position = new Vector3(-1.2f, 0.83f, 0.58f), Target = new Vector3(-0.21f, 0.83f, 0.58f), FieldOfView = 12 },
    ["shfront"] = new() { Name = "shfront", Position = new Vector3(-0.21f, 0.86f, 1.6f), Target = new Vector3(-0.21f, 0.83f, 0.58f), FieldOfView = 12 },
    ["sh34"] = new() { Name = "sh34", Position = new Vector3(-0.75f, 0.98f, 1.2f), Target = new Vector3(-0.21f, 0.83f, 0.58f), FieldOfView = 14 },
    ["stem1"] = new() { Name = "stem1", Position = new Vector3(-0.3f, 0.92f, 0.62f), Target = new Vector3(0, 0.85f, 0.42f), FieldOfView = 26 },
    ["stem2"] = new() { Name = "stem2", Position = new Vector3(-0.35f, 0.86f, 0.4f), Target = new Vector3(0, 0.85f, 0.41f), FieldOfView = 24 },
    ["stem3"] = new() { Name = "stem3", Position = new Vector3(0.28f, 0.95f, 0.75f), Target = new Vector3(0, 0.85f, 0.43f), FieldOfView = 26 },
    ["stem4"] = new() { Name = "stem4", Position = new Vector3(-0.17f, 1.0f, 0.2f), Target = new Vector3(0, 0.85f, 0.42f), FieldOfView = 28 },
    ["stem5"] = new() { Name = "stem5", Position = new Vector3(-0.1f, 0.95f, 0.72f), Target = new Vector3(0, 0.85f, 0.4f), FieldOfView = 26 },
    ["handside"] = new() { Name = "handside", Position = new Vector3(-0.9f, 0.9f, 0.58f), Target = new Vector3(-0.21f, 0.89f, 0.58f), FieldOfView = 16 },
    ["hand34"] = new() { Name = "hand34", Position = new Vector3(-0.55f, 1.05f, 0.95f), Target = new Vector3(-0.21f, 0.89f, 0.58f), FieldOfView = 20 },
    ["handfront"] = new() { Name = "handfront", Position = new Vector3(-0.15f, 0.95f, 1.3f), Target = new Vector3(-0.2f, 0.89f, 0.58f), FieldOfView = 16 },
    ["droprear"] = new() { Name = "droprear", Position = new Vector3(-0.32f, 0.42f, -0.3f), Target = new Vector3(-0.07f, 0.34f, -0.4f), FieldOfView = 26 },
    ["droprear2"] = new() { Name = "droprear2", Position = new Vector3(0.32f, 0.42f, -0.3f), Target = new Vector3(0.07f, 0.34f, -0.4f), FieldOfView = 26 },
    ["shoeside"] = new() { Name = "shoeside", Position = new Vector3(0.75f, 0.3f, 0.1f), Target = new Vector3(0.13f, 0.25f, 0.05f), FieldOfView = 22 },
    ["shoe34"] = new() { Name = "shoe34", Position = new Vector3(0.6f, 0.55f, 0.55f), Target = new Vector3(0.13f, 0.25f, 0.05f), FieldOfView = 24 },
    ["shoeback"] = new() { Name = "shoeback", Position = new Vector3(0.3f, 0.5f, -0.6f), Target = new Vector3(0.13f, 0.27f, 0.0f), FieldOfView = 24 },
    ["shoeflat"] = new() { Name = "shoeflat", Position = new Vector3(1.4f, 0.17f, 0.08f), Target = new Vector3(0.12f, 0.17f, 0.08f), FieldOfView = 12 },
    ["feetfront"] = new() { Name = "feetfront", Position = new Vector3(0.02f, 0.3f, 1.4f), Target = new Vector3(0, 0.27f, 0f), FieldOfView = 22 },
    ["feetback"] = new() { Name = "feetback", Position = new Vector3(0.02f, 0.45f, -1.2f), Target = new Vector3(0, 0.27f, 0f), FieldOfView = 22 },
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

// 0 = riding, 1 = standing with the left foot down (FREEVELO_STOP).
static float Stopped() => float.Parse(Environment.GetEnvironmentVariable("FREEVELO_STOP") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
