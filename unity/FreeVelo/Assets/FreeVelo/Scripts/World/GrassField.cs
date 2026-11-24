using System.Collections.Generic;
using FreeVelo.Core.World.Gen;
using UnityEngine;
using UnityEngine.Rendering;
using Terrain = FreeVelo.Core.World.Gen.Terrain;

namespace FreeVelo.World
{
    /// <summary>
    /// Grass tufts round the camera: tall and dense along the road verges, in patches over the meadows, short above
    /// the treeline. Never on the road or its shoulder, in water, on anything built, on fields, rock or snow, or on
    /// steep slopes; each tuft stands on the ground exactly as it is drawn (<see cref="Terrain.SurfaceHeight"/>), a
    /// little sunk in. Places are deterministic per 1.4 m cell, computed per 16 m piece when the camera comes near
    /// (within a time budget per frame) and drawn instanced without shadows; they shrink away towards the edge of the
    /// radius instead of popping. They sway with the wind (FreeVelo/LitWind with _FV_GRASS).
    /// </summary>
    public sealed class GrassField : MonoBehaviour
    {
        private const float Piece = 16f;
        private const float Cell = 1.2f;
        private const float ShoulderWidth = 0.9f; // as WorldBuilder

        private struct Tuft
        {
            public Vector3 Position;
            public float Yaw, Scale, Rank;
            public int Kind; // 0 short, 1 meadow, 2 verge
            public int Colour; // 0 lush, 1 mid, 2 dry tips
        }

        private Terrain _terrain;
        private readonly Dictionary<long, Tuft[]> _pieces = new Dictionary<long, Tuft[]>();
        private readonly Queue<long> _todo = new Queue<long>();
        private readonly HashSet<long> _queued = new HashSet<long>();
        private Mesh[] _meshes;
        private Material[] _materials;
        private readonly List<Matrix4x4>[] _lists = new List<Matrix4x4>[18]; // detail (0 near, 1 far) × kind × colour
        private readonly Matrix4x4[] _batch = new Matrix4x4[1023];
        private readonly Plane[] _planes = new Plane[6];

        /// <summary>Tufts drawn in the last frame and held in the computed pieces (for the render-shot breakdown).</summary>
        public static int Drawn, DrawnTriangles, Computed;
        /// <summary>Debug: cells turned down, by rule (outside, road, chance, built, water, slope, ground).</summary>
        public static readonly int[] Reject = new int[7];

        /// <summary>Pieces near the camera still to be computed (render shots wait for them).</summary>
        public bool Busy => _todo.Count > 0;

        public void Init(Terrain terrain)
        {
            _terrain = terrain;
            _pieces.Clear();
            _todo.Clear();
            _queued.Clear();
        }

        private static bool LowQuality => FreeVelo.App.AppSettings.Quality == FreeVelo.App.GraphicsQuality.Low;

        /// <summary>How far the tufts reach: less on low graphics quality.</summary>
        private static float Radius => LowQuality ? 45f : 90f;

        /// <summary>Beyond this the tufts use the simple version (a third of the blades, a little wider).</summary>
        private static float DetailRadius => LowQuality ? 18f : 30f;

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (_terrain == null || cam == null) return;
            if (_meshes == null) CreateAssets();
            var eye = cam.transform.position;
            float radius = Radius;

            // Queue the pieces in reach, nearest first; compute some within 1.5 ms.
            int c0 = Mathf.FloorToInt((eye.x - radius) / Piece), c1 = Mathf.FloorToInt((eye.x + radius) / Piece);
            int r0 = Mathf.FloorToInt((eye.z - radius) / Piece), r1 = Mathf.FloorToInt((eye.z + radius) / Piece);
            var wanted = new List<(float D, long Key, int X, int Z)>();
            for (int pz = r0; pz <= r1; pz++)
            {
                for (int px = c0; px <= c1; px++)
                {
                    long key = Key(px, pz);
                    if (_pieces.ContainsKey(key) || _queued.Contains(key)) continue;
                    float dx = ((px + 0.5f) * Piece) - eye.x, dz = ((pz + 0.5f) * Piece) - eye.z;
                    float d = Mathf.Sqrt((dx * dx) + (dz * dz));
                    if (d < radius + (Piece * 0.71f)) wanted.Add((d, key, px, pz));
                }
            }

            wanted.Sort((a, b) => a.D.CompareTo(b.D));
            foreach (var w in wanted)
            {
                _todo.Enqueue(w.Key);
                _queued.Add(w.Key);
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (_todo.Count > 0 && clock.Elapsed.TotalMilliseconds < 1.5)
            {
                long key = _todo.Dequeue();
                _queued.Remove(key);
                _pieces[key] = Compute((int)(key >> 32), (int)(key & 0xFFFFFFFF));
            }

            // Forget pieces far behind (memory), then draw what is in view.
            if (_pieces.Count > 400)
            {
                var drop = new List<long>();
                foreach (var key in _pieces.Keys)
                {
                    float dx = (((int)(key >> 32) + 0.5f) * Piece) - eye.x, dz = (((int)(key & 0xFFFFFFFF) + 0.5f) * Piece) - eye.z;
                    if ((dx * dx) + (dz * dz) > 4f * radius * radius) drop.Add(key);
                }

                foreach (var key in drop) _pieces.Remove(key);
            }

            Draw(cam, eye, radius);
        }

        private void Draw(Camera cam, Vector3 eye, float radius)
        {
            foreach (var list in _lists) list.Clear();
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            float fadeStart = radius * 0.7f;
            float detail = DetailRadius;
            foreach (var pair in _pieces)
            {
                var tufts = pair.Value;
                if (tufts.Length == 0) continue;
                int px = (int)(pair.Key >> 32), pz = (int)(pair.Key & 0xFFFFFFFF);
                var centre = new Vector3((px + 0.5f) * Piece, tufts[0].Position.y, (pz + 0.5f) * Piece);
                if (((centre.x - eye.x) * (centre.x - eye.x)) + ((centre.z - eye.z) * (centre.z - eye.z)) > (radius + 12f) * (radius + 12f)) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, new Bounds(centre, new Vector3(Piece + 2f, 60f, Piece + 2f)))) continue;
                foreach (var t in tufts)
                {
                    float dx = t.Position.x - eye.x, dz = t.Position.z - eye.z;
                    float d = Mathf.Sqrt((dx * dx) + (dz * dz));
                    // Thin out and shrink towards the edge (the far ones go first, in an order fixed per tuft).
                    float limit = Mathf.Lerp(fadeStart, radius, t.Rank);
                    if (d > limit) continue;
                    float fade = 1f - Mathf.SmoothStep(0f, 1f, (d - (limit - 6f)) / 6f);
                    float s = t.Scale * Mathf.Max(0.05f, fade);
                    _lists[(d > detail ? 9 : 0) + (t.Kind * 3) + t.Colour].Add(Matrix4x4.TRS(t.Position, Quaternion.Euler(0f, t.Yaw, 0f), new Vector3(s, s, s)));
                }
            }

            Drawn = 0;
            DrawnTriangles = 0;
            Computed = 0;
            foreach (var p in _pieces.Values) Computed += p.Length;
            for (int i = 0; i < _lists.Length; i++)
            {
                var list = _lists[i];
                Drawn += list.Count;
                for (int start = 0; start < list.Count; start += _batch.Length)
                {
                    int count = Mathf.Min(_batch.Length, list.Count - start);
                    list.CopyTo(start, _batch, 0, count);
                    Graphics.DrawMeshInstanced(_meshes[i / 3], 0, _materials[i % 3], _batch, count, null, ShadowCastingMode.Off, true);
                    DrawnTriangles += count * (int)(_meshes[i / 3].GetIndexCount(0) / 3);
                }
            }
        }

        private static long Key(int x, int z) => ((long)x << 32) | (uint)z;

        /// <summary>The tufts of one 16 m piece.</summary>
        private Tuft[] Compute(int px, int pz)
        {
            var tufts = new List<Tuft>();
            var m = _terrain.Mountain;
            var landscape = _terrain.Landscape;
            int seed = _terrain.Map.Seed + 7;
            int cells = Mathf.RoundToInt(Piece / Cell);
            float cell = Piece / cells;
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    int gx = (px * cells) + i, gz = (pz * cells) + j;
                    float x = (px * Piece) + ((i + Hash(gx, gz, 1)) * cell);
                    float z = (pz * Piece) + ((j + Hash(gx, gz, 2)) * cell);
                    if (!_terrain.InGrid(x, z) && (m == null || !m.Contains(x, z))) { Reject[0]++; continue; }

                    double edge = _terrain.DistanceToRoad(x, z, 12, out var road, out _, out _);
                    edge = road != null ? edge - (road.Width / 2) : 99;
                    if (edge < ShoulderWidth + 0.3) { Reject[1]++; continue; } // road and shoulder
                    bool verge = edge < 4.5;
                    float chance = verge ? 0.95f : 0.25f + (0.6f * Mathf.PerlinNoise((x / 11f) + 300f, (z / 11f) + 700f));
                    if (Hash(gx, gz, 3) > chance) { Reject[2]++; continue; }

                    if (landscape != null && landscape.BuiltOn(x, z)) { Reject[3]++; continue; }
                    if (OnJunctionCorner(x, z)) { Reject[1]++; continue; }
                    if (_terrain.IsWater(x, z, 0.5)) { Reject[4]++; continue; }
                    double slope = _terrain.Slope(x, z);
                    if (slope > 0.75) { Reject[5]++; continue; }
                    double h = _terrain.SurfaceHeight(x, z);

                    int kind = verge ? 2 : 1;
                    if (m != null && !_terrain.InGrid(x, z))
                    {
                        var ground = MountainLook.Ground(m, x, z, h, slope, seed, distant: false, landscape);
                        if (ground == GroundKind.Meadow) kind = 0;
                        else if (ground != GroundKind.Grass) { Reject[6]++; continue; } // rock, snow, fields
                    }

                    // Half the meadow tufts are the short kind, for a mixed sward.
                    if (kind == 1 && Hash(gx, gz, 4) < 0.45f) kind = 0;
                    float dry = Mathf.PerlinNoise((x / 23f) + 50f, (z / 23f) + 90f);
                    int colour = dry > 0.7f ? 2 : Hash(gx, gz, 5) < 0.5f ? 0 : 1;
                    tufts.Add(new Tuft
                    {
                        Position = new Vector3(x, (float)h - 0.05f, z),
                        Yaw = Hash(gx, gz, 6) * 360f,
                        Scale = 0.75f + (0.5f * Hash(gx, gz, 7)),
                        Rank = Hash(gx, gz, 8),
                        Kind = kind,
                        Colour = colour,
                    });

                    // Clumps: the verge grows thick (three more tufts round this one), the lush meadow patches a bit.
                    int extra = verge ? 3 : chance > 0.65f ? 1 : 0;
                    for (int k = 0; k < extra; k++)
                    {
                        float ex = x + ((Hash(gx, gz, 20 + k) - 0.5f) * cell * 1.1f);
                        float ez = z + ((Hash(gx, gz, 30 + k) - 0.5f) * cell * 1.1f);
                        double eEdge = _terrain.DistanceToRoad(ex, ez, 12, out var eRoad, out _, out _);
                        if (eRoad != null && eEdge - (eRoad.Width / 2) < ShoulderWidth + 0.3) continue;
                        if (landscape != null && landscape.BuiltOn(ex, ez)) continue;
                        if (OnJunctionCorner(ex, ez) || _terrain.IsWater(ex, ez, 0.5)) continue;
                        tufts.Add(new Tuft
                        {
                            Position = new Vector3(ex, (float)_terrain.SurfaceHeight(ex, ez) - 0.05f, ez),
                            Yaw = Hash(gx, gz, 40 + k) * 360f,
                            Scale = 0.65f + (0.5f * Hash(gx, gz, 50 + k)),
                            Rank = Hash(gx, gz, 60 + k),
                            Kind = verge && k == 2 ? 1 : kind,
                            Colour = colour == 2 || Hash(gx, gz, 70 + k) < 0.5f ? colour : 1 - Mathf.Min(colour, 1),
                        });
                    }
                }
            }

            return tufts.ToArray();
        }

        /// <summary>On (or within a shoulder of) the paved corner of a junction, which reaches beyond the road edges.</summary>
        private bool OnJunctionCorner(double x, double z)
        {
            const double margin = ShoulderWidth + 0.3;
            foreach (var c in _terrain.Corners)
            {
                if (x < c.MinX - margin || x > c.MaxX + margin || z < c.MinZ - margin || z > c.MaxZ + margin) continue;
                if (c.Distance(x, z) < margin) return true;
            }

            return false;
        }

        private static float Hash(int x, int z, int salt)
        {
            uint h = (uint)((x * 73856093) ^ (z * 19349663) ^ (salt * 83492791));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }

        // ------------------------------------------------------------------ assets

        private void CreateAssets()
        {
            for (int i = 0; i < _lists.Length; i++) _lists[i] = new List<Matrix4x4>();
            _meshes = new[]
            {
                TuftMesh("GrassTuftShort", blades: 14, height: 0.3f, seed: 1),
                TuftMesh("GrassTuftMeadow", blades: 18, height: 0.48f, seed: 2),
                TuftMesh("GrassTuftVerge", blades: 24, height: 0.75f, seed: 3),
                // Far: a third of the blades, wider, so the tuft keeps its bulk at a distance.
                TuftMesh("GrassTuftShortFar", blades: 5, height: 0.3f, seed: 1, widthScale: 2f),
                TuftMesh("GrassTuftMeadowFar", blades: 6, height: 0.48f, seed: 2, widthScale: 2f),
                TuftMesh("GrassTuftVergeFar", blades: 8, height: 0.75f, seed: 3, widthScale: 2f),
            };

            var template = Resources.Load<Material>("FreeVeloLitGrass");
            // Base like the darker ground grass, tips lighter; the third with dry, straw-coloured tips.
            var bases = new[] { new Color(0.17f, 0.31f, 0.1f), new Color(0.2f, 0.33f, 0.12f), new Color(0.22f, 0.33f, 0.12f) };
            var tips = new[] { new Color(0.4f, 0.6f, 0.2f), new Color(0.47f, 0.62f, 0.24f), new Color(0.62f, 0.62f, 0.32f) };
            _materials = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                var gradient = new Texture2D(2, 32, TextureFormat.RGBA32, false) { name = "GrassGradient" + i, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 32; y++)
                {
                    float v = y / 31f;
                    var c = Color.Lerp(bases[i], tips[i], Mathf.Pow(v, 0.8f));
                    gradient.SetPixel(0, y, c);
                    gradient.SetPixel(1, y, c);
                }

                gradient.Apply(false, true);
                var material = template != null ? new Material(template) : new Material(Shader.Find("FreeVelo/LitWind"));
                material.name = "GrassTuft" + i;
                material.enableInstancing = true;
                material.EnableKeyword("_FV_GRASS");
                material.SetTexture("_BaseMap", gradient);
                material.mainTexture = gradient;
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0.05f);
                material.SetFloat("_Cull", 0f); // blades seen from both sides
                _materials[i] = material;
            }
        }

        /// <summary>
        /// A tuft: blades from a small root circle, leaning outwards and curving over, tapering to a point. Normals
        /// lean up (soft, meadow-like light from both sides).
        /// </summary>
        private static Mesh TuftMesh(string name, int blades, float height, int seed, float widthScale = 1f)
        {
            var random = new System.Random(seed);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const int segments = 3;
            for (int b = 0; b < blades; b++)
            {
                float angle = ((b + (float)random.NextDouble() * 0.6f) / blades) * Mathf.PI * 2f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var side = new Vector3(-outward.z, 0f, outward.x);
                var root = outward * (0.02f + (0.09f * (float)random.NextDouble()));
                float h = height * (0.55f + (0.55f * (float)random.NextDouble()));
                float lean = 0.1f + (0.4f * (float)random.NextDouble()); // outward reach at the tip, relative to height
                float width = (0.018f + (0.016f * (float)random.NextDouble())) * widthScale;
                // the blade's face turns a little, so the tuft is not a perfect star
                side = Quaternion.AngleAxis(((float)random.NextDouble() - 0.5f) * 50f, Vector3.up) * side;
                var normal = (Vector3.up * 0.75f + outward * 0.45f).normalized;
                int first = vertices.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments;
                    // curving over: reach grows with t²
                    var p = root + (Vector3.up * (h * t * (1f - (0.25f * lean * t)))) + (outward * (h * lean * t * t));
                    float w = width * (1f - t);
                    if (s < segments)
                    {
                        vertices.Add(p - (side * w));
                        vertices.Add(p + (side * w));
                        normals.Add(normal);
                        normals.Add(normal);
                        uvs.Add(new Vector2(0f, t));
                        uvs.Add(new Vector2(1f, t));
                    }
                    else
                    {
                        vertices.Add(p);
                        normals.Add(normal);
                        uvs.Add(new Vector2(0.5f, 1f));
                    }
                }

                for (int s = 0; s < segments - 1; s++)
                {
                    int a = first + (s * 2);
                    tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }

                int last = first + ((segments - 1) * 2);
                tris.AddRange(new[] { last, last + 2, last + 1 });
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
