using System.Collections.Generic;
using FreeVelo.Core.Physics;
using FreeVelo.Core.World.Gen;
using UnityEngine;
using UnityEngine.Rendering;
using CoreMesh = FreeVelo.Core.Avatar.MeshData;
using Num = System.Numerics;
using Terrain = FreeVelo.Core.World.Gen.Terrain;

namespace FreeVelo.World
{
    /// <summary>
    /// Builds the 3D world from the generated <see cref="WorldMap"/> (FreeVelo.Core): terrain in chunks with two levels
    /// of detail, the road network (asphalt and gravel, junctions, bridges with railings), lakes and the river, fields
    /// and the scenery. Far scenery is merged into static low-detail chunks; near the camera every tree, house and bush
    /// is drawn in full detail with GPU instancing.
    /// </summary>
    public sealed class WorldBuilder : MonoBehaviour
    {
        private const float ShoulderWidth = 0.9f;
        private const int TerrainChunkCells = 32;
        private const float SceneryChunk = 100f;
        private const float DetailRadius = 75f;
        private const float NearChunkRadius = 150f;

        private readonly Dictionary<(Color, Texture), Material> _materials = new Dictionary<(Color, Texture), Material>();
        private readonly Dictionary<(SceneryKind, int, int), (Mesh Mesh, Material[] Materials)> _models = new Dictionary<(SceneryKind, int, int), (Mesh, Material[])>();
        private readonly List<SceneryChunkData> _chunks = new List<SceneryChunkData>();
        private readonly Dictionary<(SceneryKind, int, int), List<Matrix4x4>> _instances = new Dictionary<(SceneryKind, int, int), List<Matrix4x4>>();
        private readonly Matrix4x4[] _batch = new Matrix4x4[1023];
        private GameObject _root;
        private Material _template;
        private Terrain _terrain;
        private Vector3 _lastInstanceUpdate = new Vector3(float.MaxValue, 0, 0);

        public WorldMap Map { get; private set; }

        /// <summary>True once the world has been built (terrain and scenery are generated in the background first).</summary>
        public bool IsBuilt => _root != null;

        public void Build(WorldMap map, Terrain terrain, SceneryLayout layout)
        {
            if (_root != null) Destroy(_root);
            _root = new GameObject("World");
            Map = map;
            _terrain = terrain;

            BuildTerrain();
            BuildRoads();
            BuildBridges();
            BuildWater();
            BuildFields(layout);
            BuildScenery(layout);
            BuildMountains();
        }

        /// <summary>Ground height (same grid as the terrain mesh).</summary>
        public float GroundHeight(float x, float z) => _terrain != null ? (float)_terrain.Height(x, z) : 0f;

        // ------------------------------------------------------------------ materials

        /// <summary>Material based on the URP Lit material in Resources (keeps the shader in builds).</summary>
        public Material Mat(Color color) => Mat(color, null, 0.15f);

        public Material Mat(Color color, Texture texture, float smoothness)
        {
            var key = (color, texture);
            if (_materials.TryGetValue(key, out var m)) return m;
            if (_template == null)
            {
                _template = Resources.Load<Material>("FreeVeloLit");
                if (_template == null) _template = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            }

            m = new Material(_template) { color = color, enableInstancing = true };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (texture != null)
            {
                m.mainTexture = texture;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            }

            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            _materials[key] = m;
            return m;
        }

        private Material Mat(WorldMaterial material)
        {
            var (c, smoothness) = WorldMaterials.All[material];
            return Mat(new Color(c.X, c.Y, c.Z), null, smoothness);
        }

        // ------------------------------------------------------------------ terrain

        /// <summary>Terrain chunks of 128 m: full 4 m grid close by, every 4th point far away; skirts hide LOD cracks.</summary>
        private void BuildTerrain()
        {
            var grass = Mat(Color.white, TextureFactory.Grass(), 0f);
            int cx = (_terrain.Nx - 1 + TerrainChunkCells - 1) / TerrainChunkCells;
            int cz = (_terrain.Nz - 1 + TerrainChunkCells - 1) / TerrainChunkCells;
            for (int j = 0; j < cz; j++)
            {
                for (int i = 0; i < cx; i++)
                {
                    int x0 = i * TerrainChunkCells, z0 = j * TerrainChunkCells;
                    int x1 = Mathf.Min(_terrain.Nx - 1, x0 + TerrainChunkCells), z1 = Mathf.Min(_terrain.Nz - 1, z0 + TerrainChunkCells);
                    var go = new GameObject($"Ground {i},{j}");
                    go.transform.SetParent(_root.transform, false);
                    var near = AddRenderer(go.transform, "LOD0", GroundMesh(x0, z0, x1, z1, 1), grass, false);
                    var far = AddRenderer(go.transform, "LOD1", GroundMesh(x0, z0, x1, z1, 4), grass, false);
                    var group = go.AddComponent<LODGroup>();
                    group.SetLODs(new[] { new LOD(0.18f, new Renderer[] { near }), new LOD(0.0005f, new Renderer[] { far }) });
                    group.RecalculateBounds();
                }
            }
        }

        private Mesh GroundMesh(int x0, int z0, int x1, int z1, int step)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            int nx = ((x1 - x0) / step) + 1, nz = ((z1 - z0) / step) + 1;
            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++) AddGroundVertex(x0 + (x * step), z0 + (z * step), 0, vertices, normals, uvs);
            }

            for (int z = 0; z < nz - 1; z++)
            {
                for (int x = 0; x < nx - 1; x++)
                {
                    int a = (z * nx) + x;
                    triangles.AddRange(new[] { a, a + nx, a + 1, a + 1, a + nx, a + nx + 1 });
                }
            }

            // Skirt: the border bent 4 m down, so neighbours at another level of detail show no gaps.
            var border = new List<int>();
            for (int x = 0; x < nx; x++) border.Add(x);
            for (int z = 1; z < nz; z++) border.Add((z * nx) + nx - 1);
            for (int x = nx - 2; x >= 0; x--) border.Add(((nz - 1) * nx) + x);
            for (int z = nz - 2; z > 0; z--) border.Add(z * nx);
            border.Add(0);
            int skirt = vertices.Count;
            foreach (int b in border)
            {
                vertices.Add(vertices[b] + (Vector3.down * 4f));
                normals.Add(normals[b]);
                uvs.Add(uvs[b]);
            }

            for (int k = 0; k < border.Count - 1; k++)
            {
                int a = border[k], b = border[k + 1], c = skirt + k, d = skirt + k + 1;
                triangles.AddRange(new[] { a, b, c, b, d, c, a, c, b, b, c, d });
            }

            var mesh = new Mesh { name = "Ground", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddGroundVertex(int gx, int gz, float drop, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs)
        {
            float x = (float)(_terrain.MinX + (gx * _terrain.Cell)), z = (float)(_terrain.MinZ + (gz * _terrain.Cell));
            vertices.Add(new Vector3(x, _terrain.At(gx, gz) - drop, z));
            float dx = _terrain.At(gx + 1, gz) - _terrain.At(gx - 1, gz), dz = _terrain.At(gx, gz + 1) - _terrain.At(gx, gz - 1);
            normals.Add(new Vector3(-dx, 2f * (float)_terrain.Cell, -dz).normalized);
            uvs.Add(new Vector2(x / 18f, z / 18f));
        }

        // ------------------------------------------------------------------ roads

        private void BuildRoads()
        {
            var asphalt = Mat(Color.white, TextureFactory.Asphalt(), 0.12f);
            var gravelRoad = Mat(Color.white, TextureFactory.GravelRoad(), 0.05f);
            var gravel = Mat(Color.white, TextureFactory.Gravel(), 0.05f);
            var paint = Mat(new Color(0.92f, 0.92f, 0.88f), null, 0.3f);
            var road = new Strips();
            var dirt = new Strips();
            var shoulders = new Strips();
            var lines = new Strips();
            var posts = new MeshBuilder();
            var postBands = new MeshBuilder();

            foreach (var e in Map.Edges)
            {
                bool paved = e.Surface == RoadSurface.Asphalt;
                float half = (float)e.Width / 2;
                var surface = paved ? road : dirt;
                surface.Begin();
                shoulders.Begin();
                var leftLine = new Strips.Run(lines);
                var rightLine = new Strips.Run(lines);
                int n = e.X.Length;
                for (int i = 0; i < n; i++)
                {
                    var c = new Vector3((float)e.X[i], (float)e.E[i] + 0.05f, (float)e.Z[i]);
                    var right = Right(e, i);
                    float v = (float)e.D[i] / (float)e.Width;
                    surface.Add(c - (right * half), c + (right * half), v);

                    var drop = new Vector3(0, -0.32f, 0);
                    float sv = (float)e.D[i] / 2f;
                    shoulders.Add(c - (right * (half + ShoulderWidth)) + drop, c - (right * half) + (Vector3.up * -0.01f), sv);
                    shoulders.AddSecond(c + (right * half) + (Vector3.up * -0.01f), c + (right * (half + ShoulderWidth)) + drop, sv);

                    if (!paved) continue;
                    bool atJunction = e.D[i] < 9 || e.D[i] > e.Length - 9;
                    var up = Vector3.up * 0.015f;
                    if (atJunction)
                    {
                        leftLine.Break();
                        rightLine.Break();
                    }
                    else
                    {
                        leftLine.Add(c - (right * (half - 0.2f)) + up, c - (right * (half - 0.32f)) + up);
                        rightLine.Add(c + (right * (half - 0.32f)) + up, c + (right * (half - 0.2f)) + up);
                        if (i % 2 == 0 && i < n - 1 && e.D[i + 1] < e.Length - 9)
                        {
                            var cn = new Vector3((float)e.X[i + 1], (float)e.E[i + 1] + 0.065f, (float)e.Z[i + 1]);
                            var r = right * 0.07f;
                            lines.Quad(c + (Vector3.up * 0.015f) - r, c + (Vector3.up * 0.015f) + r, cn - r, cn + r);
                        }
                    }

                    // Delineator posts every 50 m, not at junctions or on bridges.
                    if (i % 10 == 0 && e.D[i] > 25 && e.D[i] < e.Length - 25 && !OnBridge(e, e.D[i]))
                    {
                        for (int s = -1; s <= 1; s += 2)
                        {
                            var foot = c + (right * (s * (half + ShoulderWidth + 0.4f))) + new Vector3(0, -0.35f, 0);
                            posts.EllipticTube(foot, foot + (Vector3.up * 1.35f), new Vector2(0.04f, 0.055f), new Vector2(0.035f, 0.048f), right, 6);
                            postBands.EllipticTube(foot + (Vector3.up * 1.08f), foot + (Vector3.up * 1.24f), new Vector2(0.038f, 0.052f), new Vector2(0.037f, 0.05f), right, 6);
                        }
                    }
                }
            }

            // Junctions: an asphalt pad covers where the roads overlap.
            foreach (var node in Map.Nodes)
            {
                var c = new Vector3((float)node.X, (float)node.Elevation + 0.06f, (float)node.Z);
                road.Disc(c, 6.2f, 24);
            }

            CreateMesh("Roads", road.ToMesh("Roads"), asphalt, false);
            CreateMesh("GravelRoads", dirt.ToMesh("GravelRoads"), gravelRoad, false);
            CreateMesh("Shoulders", shoulders.ToMesh("Shoulders"), gravel, false);
            CreateMesh("Markings", lines.ToMesh("Markings"), paint, false);
            CreateMesh("Posts", posts.ToMesh("Posts"), Mat(new Color(0.93f, 0.93f, 0.9f), null, 0.4f), true);
            CreateMesh("PostBands", postBands.ToMesh("PostBands"), Mat(new Color(0.05f, 0.05f, 0.05f), null, 0.3f), false);
        }

        private static Vector3 Right(RoadEdge e, int i)
        {
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(e.X.Length - 1, i + 1);
            var forward = new Vector3((float)(e.X[b] - e.X[a]), 0, (float)(e.Z[b] - e.Z[a])).normalized;
            return new Vector3(forward.z, 0, -forward.x);
        }

        private bool OnBridge(RoadEdge e, double d)
        {
            float span = (float)Map.River.HalfWidth + 16;
            foreach (var b in Map.Bridges)
            {
                if (b.Edge == e.Id && System.Math.Abs(b.DistanceM - d) < span) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ bridges

        /// <summary>Concrete deck with side beams, piers and steel railings where roads cross the river.</summary>
        private void BuildBridges()
        {
            var concrete = new MeshBuilder();
            var railing = new MeshBuilder();
            float span = (float)Map.River.HalfWidth + 14;
            foreach (var b in Map.Bridges)
            {
                var e = Map.Edges[b.Edge];
                float half = (float)e.Width / 2;
                for (float d = (float)b.DistanceM - span; d <= (float)b.DistanceM + span; d += 2f)
                {
                    var (c, forward) = OnEdge(e, d);
                    var right = new Vector3(forward.z, 0, -forward.x);
                    var rot = Quaternion.LookRotation(forward, Vector3.up);
                    concrete.Box(c + (Vector3.down * 0.55f), new Vector3((half * 2) + 1.6f, 0.9f, 2.1f), rot);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var edge = c + (right * (s * (half + 0.55f)));
                        concrete.Box(edge + (Vector3.up * 0.12f), new Vector3(0.5f, 0.5f, 2.1f), rot);
                        railing.Box(edge + (Vector3.up * 0.85f), new Vector3(0.07f, 1.0f, 0.07f), rot);
                        var (cn, _) = OnEdge(e, d + 2f);
                        var nextEdge = cn + (right * (s * (half + 0.55f)));
                        railing.Tube(edge + (Vector3.up * 1.32f), nextEdge + (Vector3.up * 1.32f), 0.045f, 0.045f, 6, false);
                        railing.Tube(edge + (Vector3.up * 0.85f), nextEdge + (Vector3.up * 0.85f), 0.025f, 0.025f, 5, false);
                    }
                }

                // Two piers in the river.
                var (mid, dir) = OnEdge(e, (float)b.DistanceM);
                var side = new Vector3(dir.z, 0, -dir.x);
                for (int s = -1; s <= 1; s += 2)
                {
                    var top = mid + (side * (s * half * 0.6f)) + (Vector3.down * 0.9f);
                    concrete.Tube(top, new Vector3(top.x, (float)b.WaterLevel - 2.5f, top.z), 0.55f, 0.65f, 10);
                }
            }

            CreateMesh("Bridges", concrete.ToMesh("Bridges"), Mat(WorldMaterial.Concrete), true);
            CreateMesh("Railings", railing.ToMesh("Railings"), Mat(WorldMaterial.Railing), true);
        }

        private static (Vector3 Position, Vector3 Forward) OnEdge(RoadEdge e, float d)
        {
            int i = 1;
            while (i < e.D.Length - 1 && e.D[i] < d) i++;
            float f = Mathf.Clamp01((float)((d - e.D[i - 1]) / (e.D[i] - e.D[i - 1])));
            var a = new Vector3((float)e.X[i - 1], (float)e.E[i - 1] + 0.05f, (float)e.Z[i - 1]);
            var b = new Vector3((float)e.X[i], (float)e.E[i] + 0.05f, (float)e.Z[i]);
            var forward = b - a;
            forward.y = 0;
            return (Vector3.Lerp(a, b, f), forward.normalized);
        }

        // ------------------------------------------------------------------ water

        private void BuildWater()
        {
            var water = new Strips();
            foreach (var lake in Map.Lakes)
            {
                const int n = 64;
                var center = new Vector3((float)lake.X, (float)lake.Level, (float)lake.Z);
                var rim = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2 / n;
                    float r = (float)lake.RadiusAt(a) * 1.06f;
                    rim[i] = center + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                }

                water.Fan(center, rim);
            }

            var river = Map.River;
            if (river != null)
            {
                water.Begin();
                float half = (float)river.HalfWidth + 2.5f;
                for (int i = 0; i < river.X.Length; i++)
                {
                    float x = (float)river.X[i], z = (float)river.Z[i];
                    if (x < _terrain.MinX || z < _terrain.MinZ || x > _terrain.MaxX || z > _terrain.MaxZ)
                    {
                        water.Begin();
                        continue;
                    }

                    int a = Mathf.Max(0, i - 1), b = Mathf.Min(river.X.Length - 1, i + 1);
                    var forward = new Vector3((float)(river.X[b] - river.X[a]), 0, (float)(river.Z[b] - river.Z[a])).normalized;
                    var right = new Vector3(forward.z, 0, -forward.x);
                    var c = new Vector3(x, (float)river.Level[i], z);
                    water.Add(c - (right * half), c + (right * half), i * 0.1f);
                }
            }

            CreateMesh("Water", water.ToMesh("Water"), Mat(WorldMaterial.Water), false);
        }

        // ------------------------------------------------------------------ fields

        private void BuildFields(SceneryLayout layout)
        {
            var textures = new Dictionary<WorldMaterial, Material>();
            foreach (var field in layout.Fields)
            {
                if (!textures.TryGetValue(field.Crop, out var material))
                {
                    var (c, _) = WorldMaterials.All[field.Crop];
                    material = Mat(Color.white, TextureFactory.Rows(new Color(c.X, c.Y, c.Z)), 0f);
                    textures[field.Crop] = material;
                }

                var vertices = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                const float cell = 6f;
                int nu = Mathf.CeilToInt((float)(field.HalfLength * 2) / cell), nv = Mathf.CeilToInt((float)(field.HalfWidth * 2) / cell);
                for (int j = 0; j <= nv; j++)
                {
                    for (int i = 0; i <= nu; i++)
                    {
                        double u = -field.HalfLength + (field.HalfLength * 2 * i / nu), v = -field.HalfWidth + (field.HalfWidth * 2 * j / nv);
                        var (x, z) = field.Point(u, v);
                        vertices.Add(new Vector3((float)x, (float)_terrain.Height(x, z) + 0.07f, (float)z));
                        uvs.Add(new Vector2((float)u / 40f, (float)v / 6f));
                    }
                }

                for (int j = 0; j < nv; j++)
                {
                    for (int i = 0; i < nu; i++)
                    {
                        int a = (j * (nu + 1)) + i, b = a + nu + 1;
                        // Winding chosen so the field faces up (checked below via the normal).
                        triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    }
                }

                var mesh = new Mesh { name = "Field", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                if (mesh.normals.Length > 0 && mesh.normals[0].y < 0)
                {
                    for (int t = 0; t < triangles.Count; t += 3) (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
                    mesh.SetTriangles(triangles, 0);
                    mesh.RecalculateNormals();
                }

                mesh.RecalculateBounds();
                AddRenderer(_root.transform, "Field", mesh, material, false);
            }
        }

        // ------------------------------------------------------------------ scenery

        private sealed class SceneryChunkData
        {
            public Vector3 Center;
            public MeshRenderer FarRenderer;
            public readonly List<SceneryItem> Items = new List<SceneryItem>();
        }

        /// <summary>Static low-detail chunks for everything (shown from afar); near chunks are drawn by <see cref="LateUpdate"/>.</summary>
        private void BuildScenery(SceneryLayout layout)
        {
            var byChunk = new Dictionary<(int, int), SceneryChunkData>();
            foreach (var item in layout.Items)
            {
                var key = (Mathf.FloorToInt(item.X / SceneryChunk), Mathf.FloorToInt(item.Z / SceneryChunk));
                if (!byChunk.TryGetValue(key, out var chunk))
                {
                    chunk = new SceneryChunkData { Center = new Vector3((key.Item1 + 0.5f) * SceneryChunk, item.Y, (key.Item2 + 0.5f) * SceneryChunk) };
                    byChunk[key] = chunk;
                }

                chunk.Items.Add(item);
            }

            foreach (var chunk in byChunk.Values)
            {
                // One mesh per chunk, one submesh per material.
                var parts = new Dictionary<WorldMaterial, (List<Vector3> V, List<Vector3> N, List<int> T)>();
                foreach (var item in chunk.Items)
                {
                    var model = SceneryModels.Get(item.Kind, item.Variant);
                    var m = ItemMatrix(item);
                    foreach (var pair in model.Lod1)
                    {
                        if (!parts.TryGetValue(pair.Key, out var p)) parts[pair.Key] = p = (new List<Vector3>(), new List<Vector3>(), new List<int>());
                        int offset = p.V.Count;
                        var normals = Normals(pair.Key, pair.Value);
                        for (int i = 0; i < pair.Value.Vertices.Count; i++)
                        {
                            var v = pair.Value.Vertices[i];
                            p.V.Add(m.MultiplyPoint3x4(new Vector3(v.X, v.Y, v.Z)));
                            p.N.Add(m.MultiplyVector(new Vector3(normals[i].X, normals[i].Y, normals[i].Z)).normalized);
                        }

                        foreach (int t in pair.Value.Triangles) p.T.Add(t + offset);
                    }
                }

                if (parts.Count == 0) continue;
                var mesh = new Mesh { name = "Scenery", indexFormat = IndexFormat.UInt32 };
                var vertices = new List<Vector3>();
                var allNormals = new List<Vector3>();
                var materials = new List<Material>();
                var submeshes = new List<List<int>>();
                foreach (var pair in parts)
                {
                    int offset = vertices.Count;
                    vertices.AddRange(pair.Value.V);
                    allNormals.AddRange(pair.Value.N);
                    var tris = new List<int>(pair.Value.T.Count);
                    foreach (int t in pair.Value.T) tris.Add(t + offset);
                    submeshes.Add(tris);
                    materials.Add(Mat(pair.Key));
                }

                mesh.SetVertices(vertices);
                mesh.SetNormals(allNormals);
                mesh.subMeshCount = submeshes.Count;
                for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s);
                mesh.RecalculateBounds();

                var go = new GameObject("Scenery");
                go.transform.SetParent(_root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials.ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.On;
                chunk.FarRenderer = renderer;
                _chunks.Add(chunk);
            }
        }

        /// <summary>
        /// Close to the camera, the static chunks are hidden and their items drawn one by one: full detail within
        /// <see cref="DetailRadius"/>, low detail beyond (GPU instancing, a few draw calls per model).
        /// </summary>
        private void LateUpdate()
        {
            if (_root == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            var eye = cam.transform.position;

            if ((eye - _lastInstanceUpdate).sqrMagnitude > 25f)
            {
                _lastInstanceUpdate = eye;
                foreach (var list in _instances.Values) list.Clear();
                foreach (var chunk in _chunks)
                {
                    var flat = new Vector2(chunk.Center.x - eye.x, chunk.Center.z - eye.z);
                    bool near = flat.magnitude < NearChunkRadius;
                    chunk.FarRenderer.enabled = !near;
                    if (!near) continue;
                    foreach (var item in chunk.Items)
                    {
                        float dx = item.X - eye.x, dz = item.Z - eye.z;
                        int lod = (dx * dx) + (dz * dz) < DetailRadius * DetailRadius ? 0 : 1;
                        var key = (item.Kind, item.Variant, lod);
                        if (!_instances.TryGetValue(key, out var list)) _instances[key] = list = new List<Matrix4x4>();
                        list.Add(ItemMatrix(item));
                    }
                }
            }

            foreach (var pair in _instances)
            {
                if (pair.Value.Count == 0) continue;
                var (mesh, materials) = Model(pair.Key.Item1, pair.Key.Item2, pair.Key.Item3);
                if (mesh == null) continue;
                var shadows = pair.Key.Item3 == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                for (int start = 0; start < pair.Value.Count; start += _batch.Length)
                {
                    int count = Mathf.Min(_batch.Length, pair.Value.Count - start);
                    pair.Value.CopyTo(start, _batch, 0, count);
                    for (int s = 0; s < materials.Length; s++) Graphics.DrawMeshInstanced(mesh, s, materials[s], _batch, count, null, shadows, true);
                }
            }
        }

        private static Matrix4x4 ItemMatrix(SceneryItem item) =>
            Matrix4x4.TRS(new Vector3(item.X, item.Y, item.Z), Quaternion.Euler(0, item.RotationDeg, 0), Vector3.one * item.Scale);

        private (Mesh Mesh, Material[] Materials) Model(SceneryKind kind, int variant, int lod)
        {
            var key = (kind, variant, lod);
            if (_models.TryGetValue(key, out var cached)) return cached;
            var model = SceneryModels.Get(kind, variant);
            var parts = lod == 0 ? model.Lod0 : model.Lod1;
            if (parts.Count == 0)
            {
                _models[key] = (null, null);
                return (null, null);
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var materials = new List<Material>();
            var submeshes = new List<List<int>>();
            foreach (var pair in parts)
            {
                int offset = vertices.Count;
                var n = Normals(pair.Key, pair.Value);
                for (int i = 0; i < pair.Value.Vertices.Count; i++)
                {
                    var v = pair.Value.Vertices[i];
                    vertices.Add(new Vector3(v.X, v.Y, v.Z));
                    normals.Add(new Vector3(n[i].X, n[i].Y, n[i].Z));
                }

                var tris = new List<int>();
                foreach (int t in pair.Value.Triangles) tris.Add(t + offset);
                submeshes.Add(tris);
                materials.Add(Mat(pair.Key));
            }

            var mesh = new Mesh { name = $"{kind} {variant} LOD{lod}", indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s);
            mesh.RecalculateBounds();
            var result = (mesh, materials.ToArray());
            _models[key] = result;
            return result;
        }

        private readonly Dictionary<CoreMesh, Num.Vector3[]> _normals = new Dictionary<CoreMesh, Num.Vector3[]>();

        /// <summary>Soft normals for foliage and stones, crisper ones for trunks and buildings (cached per mesh).</summary>
        private Num.Vector3[] Normals(WorldMaterial material, CoreMesh mesh)
        {
            if (_normals.TryGetValue(mesh, out var n)) return n;
            bool soft = material is WorldMaterial.SpruceNeedles or WorldMaterial.SpruceNeedlesDark or WorldMaterial.PineNeedles
                or WorldMaterial.LeavesDark or WorldMaterial.LeavesMid or WorldMaterial.LeavesLight or WorldMaterial.BirchLeaves
                or WorldMaterial.PoplarLeaves or WorldMaterial.Bush or WorldMaterial.BushFlowering or WorldMaterial.Rock;
            n = mesh.ComputeNormals(soft ? 85f : 35f);
            _normals[mesh] = n;
            return n;
        }

        // ------------------------------------------------------------------ horizon

        private void BuildMountains()
        {
            const int segments = 120;
            var center = new Vector2((float)(_terrain.MinX + _terrain.MaxX) / 2, (float)(_terrain.MinZ + _terrain.MaxZ) / 2);
            float inner = (float)System.Math.Max(_terrain.MaxX - _terrain.MinX, _terrain.MaxZ - _terrain.MinZ) * 0.72f;
            float outer = inner + 900f;
            float baseY = (float)_terrain.Height(center.x, center.y) - 25f;
            var mesh = new Strips();
            mesh.Begin();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float ridge = 90f + (Mathf.PerlinNoise(i * 0.18f, 3.3f) * 260f) + (Mathf.PerlinNoise(i * 0.7f, 9.1f) * 60f);
                var c = new Vector3(center.x, baseY, center.y);
                mesh.Ring(c + (dir * inner), c + (dir * (inner + 450f)) + (Vector3.up * ridge), c + (dir * outer) + (Vector3.up * (ridge * 0.4f)), i < segments);
            }

            CreateMesh("Mountains", mesh.ToMesh("Mountains"), Mat(new Color(0.42f, 0.5f, 0.45f), null, 0f), false);
        }

        // ------------------------------------------------------------------ helpers

        public static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private void CreateMesh(string name, Mesh mesh, Material material, bool shadows)
        {
            if (mesh == null || mesh.vertexCount == 0) return;
            AddRenderer(_root.transform, name, mesh, material, shadows);
        }

        private static MeshRenderer AddRenderer(Transform parent, string name, Mesh mesh, Material material, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return renderer;
        }

        /// <summary>Strip meshes (roads, shoulders, water, markings), fans and the mountain ring.</summary>
        private sealed class Strips
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int> _t = new List<int>();
            private int _last = -1;
            private int _lastSecond = -1;

            /// <summary>Starts a new strip (no connection to the previous vertices).</summary>
            public void Begin()
            {
                _last = -1;
                _lastSecond = -1;
            }

            /// <summary>Adds a left/right pair, connected to the previous pair of this strip.</summary>
            public void Add(Vector3 left, Vector3 right, float v) => _last = Pair(left, right, v, _last);

            /// <summary>A second, parallel strip (e.g. the right shoulder) in the same pass.</summary>
            public void AddSecond(Vector3 left, Vector3 right, float v) => _lastSecond = Pair(left, right, v, _lastSecond);

            private int Pair(Vector3 left, Vector3 right, float v, int previous)
            {
                int a = _v.Count;
                _v.Add(left);
                _v.Add(right);
                _uv.Add(new Vector2(0, v));
                _uv.Add(new Vector2(1, v));
                if (previous >= 0) QuadIndices(previous, previous + 1, a, a + 1);
                return a;
            }

            public void Quad(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
            {
                int a = _v.Count;
                _v.Add(a0);
                _v.Add(a1);
                _v.Add(b0);
                _v.Add(b1);
                for (int i = 0; i < 4; i++) _uv.Add(Vector2.zero);
                QuadIndices(a, a + 1, a + 2, a + 3);
            }

            /// <summary>Flat polygon around a centre (lakes, junction pads); faces up.</summary>
            public void Fan(Vector3 center, Vector3[] rim)
            {
                int c = _v.Count;
                _v.Add(center);
                _uv.Add(new Vector2(center.x / 6.5f, center.z / 6.5f));
                foreach (var p in rim)
                {
                    _v.Add(p);
                    _uv.Add(new Vector2(p.x / 6.5f, p.z / 6.5f));
                }

                for (int i = 0; i < rim.Length; i++)
                {
                    int a = c + 1 + i, b = c + 1 + ((i + 1) % rim.Length);
                    // Up-facing whatever the rim direction.
                    var n = Vector3.Cross(_v[a] - _v[c], _v[b] - _v[c]);
                    if (n.y > 0) _t.AddRange(new[] { c, b, a });
                    else _t.AddRange(new[] { c, a, b });
                }
            }

            public void Disc(Vector3 center, float radius, int segments)
            {
                var rim = new Vector3[segments];
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments;
                    rim[i] = center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                }

                Fan(center, rim);
            }

            /// <summary>Mountain ring: foot, ridge and back per angle, facing the inside.</summary>
            public void Ring(Vector3 foot, Vector3 ridge, Vector3 back, bool connect)
            {
                int a = _v.Count;
                _v.Add(foot);
                _v.Add(ridge);
                _v.Add(back);
                for (int i = 0; i < 3; i++) _uv.Add(Vector2.zero);
                if (!connect) return;
                for (int k = 0; k < 2; k++)
                {
                    int p0 = a + k, p1 = a + k + 1, q0 = a + 3 + k, q1 = a + 4 + k;
                    _t.AddRange(new[] { p0, q0, p1, q0, q1, p1 });
                }
            }

            public Mesh ToMesh(string name)
            {
                if (_v.Count == 0) return null;
                var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(_v);
                mesh.SetUVs(0, _uv);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            // Pair (left a, right b) followed by pair (left c, right d): clockwise from above.
            private void QuadIndices(int a, int b, int c, int d)
            {
                _t.Add(a);
                _t.Add(c);
                _t.Add(b);
                _t.Add(b);
                _t.Add(c);
                _t.Add(d);
            }

            /// <summary>A strip that can be interrupted (edge lines stop at junctions).</summary>
            public sealed class Run
            {
                private readonly Strips _owner;
                private int _last = -1;

                public Run(Strips owner) => _owner = owner;

                public void Add(Vector3 left, Vector3 right) => _last = _owner.Pair(left, right, 0, _last);

                public void Break() => _last = -1;
            }
        }
    }
}
