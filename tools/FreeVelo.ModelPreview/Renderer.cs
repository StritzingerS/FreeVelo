using System.IO.Compression;
using System.Numerics;
using FreeVelo.Core.Avatar;

namespace FreeVelo.ModelPreview;

/// <summary>World-space triangle soup with per-vertex normals and a material.</summary>
public sealed class SceneMesh
{
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required int[] Triangles { get; init; }
    public required Vector3 Albedo { get; init; }
    public float Smoothness { get; init; }
    public float Metallic { get; init; }
    public bool CastsShadow { get; init; } = true;
}

public sealed class Camera
{
    public required string Name { get; init; }
    public required Vector3 Position { get; init; }
    public required Vector3 Target { get; init; }
    public float FieldOfView { get; init; } = 60f;
}

/// <summary>
/// Small software rasterizer approximating Unity URP Lit: back-face culling (clockwise front faces),
/// smooth normals, sun with shadow map, trilight ambient, Blinn-Phong specular, ACES-like tonemapping.
/// </summary>
public sealed class Renderer
{
    private static readonly Vector3 SunDir = Rot.Euler(36f, 125f, 0f) * Vector3.UnitZ; // direction light travels
    private static readonly Vector3 SunColor = new Vector3(1f, 0.95f, 0.86f) * 1.3f;
    private static readonly Vector3 SkyAmbient = SrgbToLinear(new Vector3(0.62f, 0.7f, 0.82f));
    private static readonly Vector3 EquatorAmbient = SrgbToLinear(new Vector3(0.55f, 0.6f, 0.58f));
    private static readonly Vector3 GroundAmbient = SrgbToLinear(new Vector3(0.3f, 0.32f, 0.25f));

    private readonly List<SceneMesh> _meshes;
    private float[] _shadow = Array.Empty<float>();
    private int _shadowSize;
    private Rot _lightRot;
    private Vector2 _shadowMin;
    private float _shadowScale;

    private readonly Vector3? _shadowCenter;

    /// <summary>Distance fog like the game (off for top-down maps).</summary>
    public bool Fog { get; set; } = true;
    private readonly float _shadowRadius;

    public Renderer(List<SceneMesh> meshes, Vector3? shadowCenter = null, float shadowRadius = 0)
    {
        _meshes = meshes;
        _shadowCenter = shadowCenter;
        _shadowRadius = shadowRadius;
        BuildShadowMap(4096);
    }

    /// <summary>Shadow casters: everything, or (large scenes) only what is near the camera.</summary>
    private bool InShadowRegion(Vector3 p) => _shadowCenter == null || Vector3.DistanceSquared(new Vector3(p.X, 0, p.Z), new Vector3(_shadowCenter.Value.X, 0, _shadowCenter.Value.Z)) < _shadowRadius * _shadowRadius;

    // ------------------------------------------------------------------ shadow map (orthographic along the sun)

    private void BuildShadowMap(int size)
    {
        _shadowSize = size;
        _lightRot = Rot.LookRotation(SunDir, Vector3.UnitY);
        var inv = _lightRot.Transposed;
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var m in _meshes.Where(m => m.CastsShadow))
        {
            foreach (var p in m.Positions)
            {
                if (!InShadowRegion(p)) continue;
                var l = inv * p;
                min = Vector2.Min(min, new Vector2(l.X, l.Y));
                max = Vector2.Max(max, new Vector2(l.X, l.Y));
            }
        }

        min -= new Vector2(0.05f);
        max += new Vector2(0.05f);
        _shadowMin = min;
        _shadowScale = (size - 1) / MathF.Max(max.X - min.X, max.Y - min.Y);
        _shadow = new float[size * size];
        Array.Fill(_shadow, float.MaxValue);

        foreach (var m in _meshes.Where(m => m.CastsShadow))
        {
            var lp = m.Positions.Select(p => inv * p).ToArray();
            for (int t = 0; t < m.Triangles.Length; t += 3)
            {
                if (!InShadowRegion(m.Positions[m.Triangles[t]])) continue;
                var a = lp[m.Triangles[t]];
                var b = lp[m.Triangles[t + 1]];
                var c = lp[m.Triangles[t + 2]];
                RasterDepth(ToShadow(a), ToShadow(b), ToShadow(c));
            }
        }
    }

    private Vector3 ToShadow(Vector3 l) => new Vector3((l.X - _shadowMin.X) * _shadowScale, (l.Y - _shadowMin.Y) * _shadowScale, l.Z);

    private void RasterDepth(Vector3 a, Vector3 b, Vector3 c)
    {
        int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int x1 = Math.Min(_shadowSize - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int y1 = Math.Min(_shadowSize - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        float area = Edge(a, b, c.X, c.Y);
        if (MathF.Abs(area) < 1e-9f) return;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = Edge(b, c, px, py) / area, w1 = Edge(c, a, px, py) / area, w2 = Edge(a, b, px, py) / area;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float z = (w0 * a.Z) + (w1 * b.Z) + (w2 * c.Z);
                int i = (y * _shadowSize) + x;
                if (z < _shadow[i]) _shadow[i] = z;
            }
        }
    }

    private float ShadowFactor(Vector3 world, Vector3 normal)
    {
        if (!InShadowRegion(world)) return 1f;
        var l = _lightRot.Transposed * (world + (normal * (_shadowCenter == null ? 0.004f : 0.15f)));
        var s = ToShadow(l);
        float bias = _shadowCenter == null ? 0.004f : 0.25f;
        int lit = 0, total = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = (int)s.X + dx, y = (int)s.Y + dy;
                total++;
                if (x < 0 || y < 0 || x >= _shadowSize || y >= _shadowSize) { lit++; continue; }
                if (s.Z - bias <= _shadow[(y * _shadowSize) + x]) lit++;
            }
        }

        return lit / (float)total;
    }

    private static float Edge(Vector3 a, Vector3 b, float x, float y) => ((b.X - a.X) * (y - a.Y)) - ((b.Y - a.Y) * (x - a.X));

    // ------------------------------------------------------------------ camera rendering

    public Vector3[] Render(Camera camera, int width, int height)
    {
        var color = new Vector3[width * height];
        var depth = new float[width * height];
        Array.Fill(depth, float.MaxValue);

        for (int y = 0; y < height; y++)
        {
            float t = y / (float)height;
            var sky = Vector3.Lerp(SrgbToLinear(new Vector3(0.45f, 0.62f, 0.86f)), SrgbToLinear(new Vector3(0.78f, 0.85f, 0.92f)), t);
            for (int x = 0; x < width; x++) color[(y * width) + x] = sky;
        }

        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var camRot = Rot.LookRotation(forward, Vector3.UnitY);
        var inv = camRot.Transposed;
        float focal = (height / 2f) / MathF.Tan(camera.FieldOfView * MathF.PI / 360f);

        foreach (var m in _meshes)
        {
            var view = m.Positions.Select(p => inv * (p - camera.Position)).ToArray();
            var albedo = SrgbToLinear(m.Albedo);
            for (int t = 0; t < m.Triangles.Length; t += 3)
            {
                int ia = m.Triangles[t], ib = m.Triangles[t + 1], ic = m.Triangles[t + 2];
                var wa = m.Positions[ia];
                var faceNormal = Vector3.Cross(m.Positions[ib] - wa, m.Positions[ic] - wa);
                if (Vector3.Dot(faceNormal, camera.Position - wa) <= 0) continue; // back face (as Unity culls)
                var va = view[ia];
                var vb = view[ib];
                var vc = view[ic];
                if (va.Z < 0.05f || vb.Z < 0.05f || vc.Z < 0.05f) continue;
                var sa = new Vector3((width / 2f) + (va.X / va.Z * focal), (height / 2f) - (va.Y / va.Z * focal), va.Z);
                var sb = new Vector3((width / 2f) + (vb.X / vb.Z * focal), (height / 2f) - (vb.Y / vb.Z * focal), vb.Z);
                var sc = new Vector3((width / 2f) + (vc.X / vc.Z * focal), (height / 2f) - (vc.Y / vc.Z * focal), vc.Z);
                RasterShade(sa, sb, sc, m, ia, ib, ic, albedo, camera.Position, color, depth, width, height);
            }
        }

        return color;
    }

    private void RasterShade(Vector3 a, Vector3 b, Vector3 c, SceneMesh m, int ia, int ib, int ic, Vector3 albedo, Vector3 eye, Vector3[] color, float[] depth, int width, int height)
    {
        int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int x1 = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int y1 = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        float area = Edge(a, b, c.X, c.Y);
        if (MathF.Abs(area) < 1e-9f) return;
        float iza = 1f / a.Z, izb = 1f / b.Z, izc = 1f / c.Z;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = Edge(b, c, px, py) / area, w1 = Edge(c, a, px, py) / area, w2 = Edge(a, b, px, py) / area;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float iz = (w0 * iza) + (w1 * izb) + (w2 * izc);
                float z = 1f / iz;
                int i = (y * width) + x;
                if (z >= depth[i]) continue;
                depth[i] = z;

                // Perspective-correct interpolation
                float pa = w0 * iza * z, pb = w1 * izb * z, pc = w2 * izc * z;
                var pos = (m.Positions[ia] * pa) + (m.Positions[ib] * pb) + (m.Positions[ic] * pc);
                var n = Vector3.Normalize((m.Normals[ia] * pa) + (m.Normals[ib] * pb) + (m.Normals[ic] * pc));
                var shaded = Shade(pos, n, albedo, m.Smoothness, m.Metallic, eye);
                if (_shadowCenter != null && Fog)
                {
                    float fog = Math.Clamp((Vector3.Distance(pos, eye) - 180f) / (3600f - 180f), 0f, 1f);
                    shaded = Vector3.Lerp(shaded, SrgbToLinear(new Vector3(0.73f, 0.8f, 0.87f)), fog);
                }

                color[i] = shaded;
            }
        }
    }

    private Vector3 Shade(Vector3 pos, Vector3 n, Vector3 albedo, float smoothness, float metallic, Vector3 eye)
    {
        var v = Vector3.Normalize(eye - pos);
        if (Vector3.Dot(n, v) < 0) n = -n; // thin parts seen from inside

        float up = n.Y;
        var ambient = up >= 0 ? Vector3.Lerp(EquatorAmbient, SkyAmbient, up) : Vector3.Lerp(EquatorAmbient, GroundAmbient, -up);
        var l = -SunDir;
        float ndl = MathF.Max(0f, Vector3.Dot(n, l));
        float shadow = ndl > 0 ? ShadowFactor(pos, n) : 0f;

        var diffuseColor = albedo * (1f - metallic);
        var specColor = Vector3.Lerp(new Vector3(0.04f), albedo, metallic);
        var h = Vector3.Normalize(l + v);
        float exponent = MathF.Pow(2f, (smoothness * 10f) + 1f);
        float spec = MathF.Pow(MathF.Max(0f, Vector3.Dot(n, h)), exponent) * ((exponent + 8f) / 25f) * ndl;
        float fresnel = MathF.Pow(1f - MathF.Max(0f, Vector3.Dot(n, v)), 5f);
        var env = Vector3.Lerp(EquatorAmbient, SkyAmbient, MathF.Max(0f, Vector3.Reflect(-v, n).Y)) * (specColor + (new Vector3(1f) - specColor) * fresnel) * smoothness * 0.6f;

        var lit = (diffuseColor * ((ambient * 0.9f) + (SunColor * ndl * shadow))) + (specColor * SunColor * spec * shadow) + env;
        return lit;
    }

    // ------------------------------------------------------------------ output

    public static byte[] ToImage(Vector3[] hdr, int width, int height, int supersample, out int outWidth, out int outHeight)
    {
        outWidth = width / supersample;
        outHeight = height / supersample;
        var rgb = new byte[outWidth * outHeight * 3];
        for (int y = 0; y < outHeight; y++)
        {
            for (int x = 0; x < outWidth; x++)
            {
                var sum = Vector3.Zero;
                for (int sy = 0; sy < supersample; sy++)
                {
                    for (int sx = 0; sx < supersample; sx++) sum += Tonemap(hdr[(((y * supersample) + sy) * width) + (x * supersample) + sx]);
                }

                var c = sum / (supersample * supersample);
                int o = ((y * outWidth) + x) * 3;
                rgb[o] = (byte)Math.Clamp(c.X * 255f + 0.5f, 0, 255);
                rgb[o + 1] = (byte)Math.Clamp(c.Y * 255f + 0.5f, 0, 255);
                rgb[o + 2] = (byte)Math.Clamp(c.Z * 255f + 0.5f, 0, 255);
            }
        }

        return rgb;
    }

    private static Vector3 Tonemap(Vector3 c)
    {
        // ACES approximation (Narkowicz) + sRGB gamma
        Vector3 Aces(Vector3 x) => Vector3.Clamp((x * ((x * 2.51f) + new Vector3(0.03f))) / ((x * ((x * 2.43f) + new Vector3(0.59f))) + new Vector3(0.14f)), Vector3.Zero, Vector3.One);
        var t = Aces(c * 1.1f);
        return new Vector3(LinearToSrgb(t.X), LinearToSrgb(t.Y), LinearToSrgb(t.Z));
    }

    public static Vector3 SrgbToLinear(Vector3 c) => new Vector3(ToLin(c.X), ToLin(c.Y), ToLin(c.Z));

    private static float ToLin(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float LinearToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : (1.055f * MathF.Pow(c, 1f / 2.4f)) - 0.055f;

    public static void WritePng(string path, byte[] rgb, int width, int height)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width);
        WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // RGB
        Chunk(file, "IHDR", ihdr);
        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (int y = 0; y < height; y++)
                {
                    z.WriteByte(0);
                    z.Write(rgb, y * width * 3, width * 3);
                }
            }

            Chunk(file, "IDAT", raw.ToArray());
        }

        Chunk(file, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes, data);
        var c = new byte[4];
        WriteBE(c, 0, crc);
        s.Write(c);
    }

    private static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static uint[]? _crcTable;

    private static uint Crc32(byte[] a, byte[] b)
    {
        _crcTable ??= Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();
        uint crc = 0xFFFFFFFFu;
        foreach (var arr in new[] { a, b })
        {
            foreach (byte x in arr) crc = _crcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
