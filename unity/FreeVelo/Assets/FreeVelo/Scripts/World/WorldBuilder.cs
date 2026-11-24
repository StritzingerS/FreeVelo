using System.Collections.Generic;
using System.Linq;
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
    public sealed partial class WorldBuilder : MonoBehaviour
    {
        private const float ShoulderWidth = 0.9f;
        private const int TerrainChunkCells = 32;
        private const float SceneryChunk = 100f;
        /// <summary>Trees, houses and bushes are drawn in full detail up to this distance (instanced).</summary>
        private const float DetailRadius = 300f;
        /// <summary>Chunks closer than this (centre) are drawn item by item; must exceed DetailRadius + half a chunk diagonal.</summary>
        private const float NearChunkRadius = 380f;

        private readonly Dictionary<(Color, Texture, Texture, bool), Material> _materials = new Dictionary<(Color, Texture, Texture, bool), Material>();
        private Material _templateNormal, _templateWind, _templateWindNormal;
        private readonly Dictionary<(SceneryKind, int, int), (Mesh Mesh, Material[] Materials)> _models = new Dictionary<(SceneryKind, int, int), (Mesh, Material[])>();
        private readonly List<SceneryChunkData> _chunks = new List<SceneryChunkData>();
        private readonly Dictionary<(SceneryKind Kind, int Variant, int Lod, int Shadows), List<Matrix4x4>> _instances = new Dictionary<(SceneryKind, int, int, int), List<Matrix4x4>>();
        private readonly Matrix4x4[] _batch = new Matrix4x4[1023];
        private GameObject _root;
        private Material _template;
        private Terrain _terrain;
        private Vector3 _lastInstanceUpdate = new Vector3(float.MaxValue, 0, 0);

        public WorldMap Map { get; private set; }

        /// <summary>True once the world has been built (terrain and scenery are generated in the background first).</summary>
        public bool IsBuilt => _root != null;

        /// <summary>The world being shown (for tooling such as the render shots).</summary>
        public static WorldBuilder Current { get; private set; }

        /// <summary>Detailed ground tiles are still being computed around the camera.</summary>
        public bool IsStreaming => _tiles.Values.Any(t => t.Building != null) || (_grass != null && _grass.Busy);

        private GrassField _grass;

        /// <summary>Set when generating the world failed (the app goes on without it instead of loading forever).</summary>
        public bool Failed { get; set; }

        /// <summary>Loading is over: built or failed.</summary>
        public bool IsLoaded => IsBuilt || Failed;

        /// <summary>
        /// Builds the scene. <paramref name="far"/>: the mountain's far tiles computed in the background
        /// (<see cref="ComputeFarTiles"/>); null computes them here.
        /// </summary>
        public void Build(WorldMap map, Terrain terrain, SceneryLayout layout, List<FarTileData> far = null)
        {
            Current = this;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var times = new System.Text.StringBuilder("World build (ms):");
            void Step(string name)
            {
                times.Append($" {name} {clock.ElapsedMilliseconds}");
                clock.Restart();
            }

            if (_root != null) Destroy(_root);
            _root = new GameObject("World");
            Map = map;
            _terrain = terrain;
            _rails = layout.GuardRails;

            BuildTerrain();
            Step("terrain");
            BuildRoads();
            BuildGuardRails();
            BuildKerbs(layout.Kerbs);
            BuildBridges();
            BuildWater();
            Step("roads+water");
            BuildFields(layout);
            BuildScenery(layout);
            _grass = GetComponent<GrassField>();
            if (_grass == null) _grass = gameObject.AddComponent<GrassField>();
            _grass.Init(terrain);
            var smoke = GetComponent<ChimneySmoke>();
            if (smoke == null) smoke = gameObject.AddComponent<ChimneySmoke>();
            smoke.Init(this, layout.Items);
            var birds = GetComponent<Birds>();
            if (birds == null) birds = gameObject.AddComponent<Birds>();
            birds.Init(this);
            Step("scenery");
            BuildMountains();
            BuildMountain(far);
            Step("mountain");
            times.Append($" (surface textures {SurfaceTextures.GenerateMs})");
            BuildTimes = times.ToString();
            Debug.Log(BuildTimes);
        }

        /// <summary>Time of each step of the last <see cref="Build"/> (for the log on the devices page).</summary>
        public string BuildTimes { get; private set; } = "";

        /// <summary>Ground height (same grid as the terrain mesh).</summary>
        public float GroundHeight(float x, float z) => _terrain != null ? (float)_terrain.Height(x, z) : 0f;

        // ------------------------------------------------------------------ materials

        /// <summary>Material based on the URP Lit material in Resources (keeps the shader in builds).</summary>
        public Material Mat(Color color) => Mat(color, null, 0.15f);

        public Material Mat(Color color, Texture texture, float smoothness) => Mat(color, texture, smoothness, null);

        /// <summary>
        /// With <paramref name="normal"/>: a normal-mapped material (the mesh needs UVs and tangents). Its template in
        /// Resources carries the _NORMALMAP keyword so the shader variant is kept in builds.
        /// </summary>
        public Material Mat(Color color, Texture texture, float smoothness, Texture normal, float bumpScale = 1f) => Mat(color, texture, smoothness, normal, bumpScale, false);

        /// <summary>
        /// With <paramref name="wind"/>: the FreeVelo/LitWind shader (foliage of scenery models sways; the vertex height
        /// above the model's foot sets how much, so only for models standing on their own origin).
        /// </summary>
        private Material Mat(Color color, Texture texture, float smoothness, Texture normal, float bumpScale, bool wind)
        {
            var key = (color, texture, normal, wind);
            if (_materials.TryGetValue(key, out var m)) return m;
            if (_template == null)
            {
                _template = Resources.Load<Material>("FreeVeloLit");
                if (_template == null) _template = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                _templateNormal = Resources.Load<Material>("FreeVeloLitNormal") ?? _template;
                _templateWind = Resources.Load<Material>("FreeVeloLitWind") ?? _template;
                _templateWindNormal = Resources.Load<Material>("FreeVeloLitWindNormal") ?? _templateNormal;
            }

            var template = wind ? (normal != null ? _templateWindNormal : _templateWind) : (normal != null ? _templateNormal : _template);
            m = new Material(template) { color = color, enableInstancing = true };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (texture != null)
            {
                m.mainTexture = texture;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            }

            if (normal != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", bumpScale);
                m.EnableKeyword("_NORMALMAP");
            }

            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            _materials[key] = m;
            return m;
        }

        /// <summary>
        /// A model that keeps its own (generated) texture: Resources/Surfaces/&lt;stem&gt;_diff and _nor, untinted. Falls
        /// back to plaster white if the texture is missing.
        /// </summary>
        private Material OwnTextureMat(string stem)
        {
            var albedo = Resources.Load<Texture2D>("Surfaces/" + stem + "_diff");
            var normal = Resources.Load<Texture2D>("Surfaces/" + stem + "_nor");
            if (albedo == null) return Mat(WorldMaterial.WallWhite);
            return Mat(Color.white, albedo, 0.08f, normal);
        }

        /// <summary>A model surface: its colour, tinted detail texture and normal map (<see cref="SurfaceTextures"/>).</summary>
        private Material Mat(WorldMaterial material, bool wind = false)
        {
            var (c, smoothness) = WorldMaterials.All[material];
            var set = SurfaceTextures.Get(SurfaceTextures.Of(material));
            var color = new Color(c.X, c.Y, c.Z);
            return set == null
                ? Mat(color, null, smoothness, null, 1f, wind)
                : Mat(set.Tint(color), set.Albedo, smoothness, set.Normal, set.BumpScale, wind);
        }

        /// <summary>Leaves and needles of scenery models: they sway in the wind.</summary>
        private static bool IsFoliage(WorldMaterial m) => SurfaceTextures.IsFoliage(m);

        // ------------------------------------------------------------------ terrain

        /// <summary>Terrain chunks of 128 m: full 4 m grid close by, every 4th point far away; skirts hide LOD cracks.</summary>
        private void BuildTerrain()
        {
            var grassTexture = TextureFactory.Grass();
            var grass = Mat(Color.white, grassTexture, 0f, TextureFactory.NormalOf(grassTexture, 6f));
            int cx =(_terrain.Nx - 1 + TerrainChunkCells - 1) / TerrainChunkCells;
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
            mesh.RecalculateTangents();
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
            var asphaltTexture = TextureFactory.Asphalt();
            var gravelRoadTexture = TextureFactory.GravelRoad();
            var gravelTexture = TextureFactory.Gravel();
            var asphalt = Mat(Color.white, asphaltTexture, 0.12f, TextureFactory.NormalOf(asphaltTexture, 5f));
            var gravelRoad = Mat(Color.white, gravelRoadTexture, 0.05f, TextureFactory.NormalOf(gravelRoadTexture, 6f));
            var gravel = Mat(Color.white, gravelTexture, 0.05f, TextureFactory.NormalOf(gravelTexture, 6f));
            var paint = Mat(new Color(0.92f, 0.92f, 0.88f), null, 0.3f);
            var road = new Strips();
            var dirt = new Strips();
            var shoulders = new Strips();
            var lines = new Strips();
            var posts = new MeshBuilder();
            var postBands = new MeshBuilder();
            BuildJunctionCorners(road, dirt);

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
                    // Where another road meets or splits off, this road's edge lies on the other road's asphalt:
                    // no gravel shoulder, edge line or posts there, so the two surfaces run into each other.
                    bool nearEnd = e.D[i] < MergeReach || e.D[i] > e.Length - MergeReach;
                    bool leftMerged = nearEnd && (InCorner(e, i, -1) || OnOtherRoad(e, c - (right * (half + 0.4f)), 0.6f));
                    bool rightMerged = nearEnd && (InCorner(e, i, 1) || OnOtherRoad(e, c + (right * (half + 0.4f)), 0.6f));
                    var edgeLeft = c - (right * half) + (Vector3.up * -0.01f);
                    var edgeRight = c + (right * half) + (Vector3.up * -0.01f);
                    shoulders.Add(leftMerged ? edgeLeft : c - (right * (half + ShoulderWidth)) + drop, edgeLeft, sv);
                    shoulders.AddSecond(edgeRight, rightMerged ? edgeRight : c + (right * (half + ShoulderWidth)) + drop, sv);

                    if (!paved) continue;
                    bool atJunction = e.D[i] < 9 || e.D[i] > e.Length - 9;
                    bool centreOnOther = nearEnd && OnOtherRoad(e, c, -0.5f);
                    var up = Vector3.up * 0.015f;
                    if (atJunction || leftMerged) leftLine.Break();
                    else leftLine.Add(c - (right * (half - 0.2f)) + up, c - (right * (half - 0.32f)) + up);
                    if (atJunction || rightMerged) rightLine.Break();
                    else rightLine.Add(c + (right * (half - 0.32f)) + up, c + (right * (half - 0.2f)) + up);
                    if (!atJunction && !centreOnOther)
                    {
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
                            if (HasRail(e.Id, s, e.D[i])) continue; // the guard rail stands there instead
                            if (nearEnd &&(InCorner(e, i, s, 6) || OnOtherRoad(e, c + (right * (s * (half + ShoulderWidth + 0.4f))), 1.5f))) continue;
                            var foot = c + (right * (s * (half + ShoulderWidth + 0.4f))) + new Vector3(0, -0.35f, 0);
                            posts.EllipticTube(foot, foot + (Vector3.up * 1.35f), new Vector2(0.04f, 0.055f), new Vector2(0.035f, 0.048f), right, 6);
                            postBands.EllipticTube(foot + (Vector3.up * 1.08f), foot + (Vector3.up * 1.24f), new Vector2(0.038f, 0.052f), new Vector2(0.037f, 0.05f), right, 6);
                        }
                    }
                }
            }

            // Junctions: a pad between the road ends (their convex hull), so the edges run straight on round the
            // outside instead of bulging into the meadow.
            foreach (var node in Map.Nodes)
            {
                var c = new Vector3((float)node.X, (float)node.Elevation + 0.06f, (float)node.Z);
                var corners = new List<Vector3>();
                foreach (int id in node.Edges)
                {
                    var e = Map.Edges[id];
                    bool start = e.From == node.Id;
                    foreach (float s in new[] { 0f, 2f })
                    {
                        foreach (int side in new[] { -1, 1 }) corners.Add(EdgeSide(e, start, s, side));
                    }
                }

                var hull = ConvexHull(corners);
                for (int k = 0; k < hull.Length; k++) hull[k].y = c.y;
                if (hull.Length >= 3) road.Fan(c, hull);
            }

            CreateMesh("Roads", road.ToMesh("Roads"), asphalt, false);
            CreateMesh("GravelRoads", dirt.ToMesh("GravelRoads"), gravelRoad, false);
            CreateMesh("Shoulders", shoulders.ToMesh("Shoulders"), gravel, false);
            CreateMesh("Markings", lines.ToMesh("Markings"), paint, false);
            CreateMesh("Posts", posts.ToMesh("Posts"), Mat(new Color(0.93f, 0.93f, 0.9f), null, 0.4f), true);
            CreateMesh("PostBands", postBands.ToMesh("PostBands"), Mat(new Color(0.05f, 0.05f, 0.05f), null, 0.3f), false);
        }

        /// <summary>Per road end and side (−1 left, +1 right): how far from the junction its rounded corner reaches.</summary>
        private readonly Dictionary<(int Edge, bool Start, int Side), float> _cornerReach = new Dictionary<(int, bool, int), float>();

        private bool InCorner(RoadEdge e, int i, int side, float extra = 1f)
        {
            float fromStart = (float)e.D[i], fromEnd = (float)(e.Length - e.D[i]);
            return (_cornerReach.TryGetValue((e.Id, true, side), out float a) && fromStart < a + extra)
                || (_cornerReach.TryGetValue((e.Id, false, side), out float b) && fromEnd < b + extra);
        }

        /// <summary>
        /// Rounded asphalt corners between neighbouring roads at each junction, as real junctions are built: from where
        /// the two road edges cross, a curve joins them, wider the shallower the split. Lines, shoulders and posts keep
        /// off the corners (see <see cref="InCorner"/>).
        /// </summary>
        private void BuildJunctionCorners(Strips paved, Strips gravel)
        {
            // The shapes come from the core (the terrain is lowered under them there too).
            foreach (var corner in _terrain.Corners)
            {
                var rim = new Vector3[corner.X.Length];
                for (int j = 0; j < rim.Length; j++)
                {
                    float x = (float)corner.X[j], z = (float)corner.Z[j];
                    float y = Mathf.Max((float)corner.Y[j] + 0.05f, (float)_terrain.Height(x, z) + 0.06f) + 0.004f;
                    rim[j] = new Vector3(x, y, z);
                }

                var centre = new Vector3((float)corner.CX, (float)corner.CY + 0.054f, (float)corner.CZ);
                (corner.Asphalt ? paved : gravel).Fan(centre, rim);
                Remember(Map.Edges[corner.EdgeA], corner.StartA, corner.SideA, (float)corner.ReachA);
                Remember(Map.Edges[corner.EdgeB], corner.StartB, corner.SideB, (float)corner.ReachB);
            }
        }

        private void Remember(RoadEdge e, bool start, int side, float reach)
        {
            var key = (e.Id, start, side);
            _cornerReach[key] = _cornerReach.TryGetValue(key, out float old) ? Mathf.Max(old, reach) : reach;
        }

        /// <summary>Convex hull (x/z) of a few points, counter-clockwise (monotone chain).</summary>
        private static Vector3[] ConvexHull(List<Vector3> points)
        {
            var p = points.OrderBy(v => v.x).ThenBy(v => v.z).ToList();
            if (p.Count < 3) return p.ToArray();
            float Cross(Vector3 o, Vector3 a, Vector3 b) => ((a.x - o.x) * (b.z - o.z)) - ((a.z - o.z) * (b.x - o.x));
            var hull = new List<Vector3>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                foreach (var v in p)
                {
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], v) <= 0) hull.RemoveAt(hull.Count - 1);
                    hull.Add(v);
                }

                hull.RemoveAt(hull.Count - 1);
                p.Reverse();
            }

            return hull.ToArray();
        }

        /// <summary>A point on one edge of a road, <paramref name="s"/> metres from the junction end.</summary>
        private static Vector3 EdgeSide(RoadEdge e, bool start, float s, int side, float inset = 0)
        {
            var (c, forward) = OnEdge(e, start ? s : (float)e.Length - s);
            var right = new Vector3(forward.z, 0, -forward.x);
            return c + (right * (side * (((float)e.Width / 2) - inset)));
        }

        /// <summary>How far from a junction roads can still overlap (shallow splits run side by side for a while).</summary>
        private const float MergeReach = 150f;

        /// <summary>
        /// Whether <paramref name="p"/> lies on another road that shares a junction with <paramref name="e"/>
        /// (within that road's half width plus <paramref name="margin"/>; negative = well inside).
        /// </summary>
        private bool OnOtherRoad(RoadEdge e, Vector3 p, float margin)
        {
            foreach (int node in new[] { e.From, e.To })
            {
                foreach (int id in Map.Nodes[node].Edges)
                {
                    if (id == e.Id) continue;
                    var o = Map.Edges[id];
                    float reach = ((float)o.Width / 2) + margin;
                    bool atStart = o.From == node;
                    for (int k = 0; k < o.X.Length - 1; k++)
                    {
                        double along = atStart ? o.D[k] : o.Length - o.D[k + 1];
                        if (along > MergeReach + 20) continue;
                        float ax = (float)o.X[k], az = (float)o.Z[k], bx = (float)o.X[k + 1], bz = (float)o.Z[k + 1];
                        float abx = bx - ax, abz = bz - az, len2 = (abx * abx) + (abz * abz);
                        float f = len2 > 0 ? Mathf.Clamp01((((p.x - ax) * abx) + ((p.z - az) * abz)) / len2) : 0;
                        float dx = ax + (abx * f) - p.x, dz = az + (abz * f) - p.z;
                        if ((dx * dx) + (dz * dz) < reach * reach) return true;
                    }
                }
            }

            return false;
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
                        railing.Tube(edge + (Vector3.up * 0.95f), nextEdge + (Vector3.up * 0.95f), 0.025f, 0.025f, 5, false);
                        railing.Tube(edge + (Vector3.up * 0.62f), nextEdge + (Vector3.up * 0.62f), 0.025f, 0.025f, 5, false);
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
            CreateMesh("Railings", railing.ToMesh("Railings"), Galvanised(), true);
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
                    int a = Mathf.Max(0, i - 1), b = Mathf.Min(river.X.Length - 1, i + 1);
                    var forward = new Vector3((float)(river.X[b] - river.X[a]), 0, (float)(river.Z[b] - river.Z[a])).normalized;
                    var right = new Vector3(forward.z, 0, -forward.x);
                    var c = new Vector3(x, (float)river.Level[i], z);
                    water.Add(c - (right * half), c + (right * half), i * 0.1f);
                }
            }

            // Ripples: a wave normal map in world-scaled UVs (35 m a repeat), drifted by WorldAnimation.
            var animation = GetComponent<WorldAnimation>();
            if (animation == null) animation = gameObject.AddComponent<WorldAnimation>();
            var mesh = water.ToMesh("Water");
            if (mesh == null) return;
            var vertices = mesh.vertices;
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) uvs[i] = new Vector2(vertices[i].x / 35f, vertices[i].z / 35f);
            mesh.uv = uvs;
            mesh.RecalculateTangents();
            var (waterColor, smoothness) = WorldMaterials.All[WorldMaterial.Water];
            var material = Mat(new Color(waterColor.X, waterColor.Y, waterColor.Z), null, smoothness, WorldAnimation.WaterNormalMap(), 0.12f);
            CreateMesh("Water", mesh, material, false);
            animation.Water = material;
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

            /// <summary>The 400 m block this chunk's far mesh is merged into (home grid only); null for streamed chunks.</summary>
            public SceneryBlock Block;
            public readonly List<SceneryItem> Items = new List<SceneryItem>();

            // Filled on first use: every item's matrix (computed once, not at every update) and the chunk's extent.
            public Matrix4x4[] Matrices;
            public Bounds Bounds;

            public void Prepare()
            {
                if (Matrices != null) return;
                Matrices = new Matrix4x4[Items.Count];
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                for (int i = 0; i < Items.Count; i++)
                {
                    var it = Items[i];
                    Matrices[i] = ItemMatrix(it);
                    x0 = Mathf.Min(x0, it.X); x1 = Mathf.Max(x1, it.X);
                    y0 = Mathf.Min(y0, it.Y); y1 = Mathf.Max(y1, it.Y);
                    z0 = Mathf.Min(z0, it.Z); z1 = Mathf.Max(z1, it.Z);
                }

                // Items reach out from their position (a church tower 33 m, trees up to 25 m, a house 8 m to the side).
                Bounds = new Bounds(new Vector3((x0 + x1) / 2, (y0 + y1) / 2 + 15, (z0 + z1) / 2), new Vector3(x1 - x0 + 30, y1 - y0 + 50, z1 - z0 + 30));
            }
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
                chunk.FarRenderer = BuildSceneryChunkMesh(_root.transform, chunk.Items);
                if (chunk.FarRenderer != null) _chunks.Add(chunk);
            }

            BuildSceneryBlocks();
        }

        /// <summary>
        /// A far-away block of the home grid's scenery (villages, farms, trees): the far meshes of up to 4 × 4 chunks
        /// (400 m) merged into one, drawn instead of them once the whole block is beyond <see cref="BlockRadius"/> –
        /// one draw call instead of up to 16 (hierarchical LOD; the render breakdown showed about 170 chunk draws).
        /// </summary>
        private sealed class SceneryBlock
        {
            public Vector3 Center;
            public MeshRenderer Renderer;
            public bool Active;
        }

        private const float SceneryBlockSize = 400f;

        /// <summary>A block is drawn merged when its centre is this far away (its nearest chunk then beyond about 700 m).</summary>
        private const float BlockRadius = 1000f;

        private readonly List<SceneryBlock> _blocks = new List<SceneryBlock>();

        private void BuildSceneryBlocks()
        {
            _blocks.Clear();
            var groups = new Dictionary<(int, int), List<SceneryChunkData>>();
            foreach (var chunk in _chunks)
            {
                if (chunk.FarRenderer == null) continue;
                var key = (Mathf.FloorToInt(chunk.Center.x / SceneryBlockSize), Mathf.FloorToInt(chunk.Center.z / SceneryBlockSize));
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<SceneryChunkData>();
                list.Add(chunk);
            }

            foreach (var pair in groups)
            {
                if (pair.Value.Count < 2) continue; // nothing to merge
                var combine = new CombineInstance[pair.Value.Count];
                for (int i = 0; i < combine.Length; i++)
                {
                    combine[i] = new CombineInstance { mesh = pair.Value[i].FarRenderer.GetComponent<MeshFilter>().sharedMesh, transform = pair.Value[i].FarRenderer.transform.localToWorldMatrix };
                }

                var mesh = new Mesh { name = "SceneryBlock", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(combine, mergeSubMeshes: true, useMatrices: true);
                var go = new GameObject("SceneryBlock") { layer = FarSceneryLayer };
                go.transform.SetParent(_root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = SceneryPaletteMaterial();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.enabled = false;
                var block = new SceneryBlock { Center = new Vector3((pair.Key.Item1 + 0.5f) * SceneryBlockSize, 0f, (pair.Key.Item2 + 0.5f) * SceneryBlockSize), Renderer = renderer };
                _blocks.Add(block);
                foreach (var chunk in pair.Value) chunk.Block = block;
            }
        }

        /// <summary>Far blocks on or off for this camera position (with the instance rebuild, every 5 m or 6°).</summary>
        private void UpdateSceneryBlocks(Vector3 eye)
        {
            foreach (var block in _blocks)
            {
                float dx = block.Center.x - eye.x, dz = block.Center.z - eye.z;
                block.Active = (dx * dx) + (dz * dz) > BlockRadius * BlockRadius;
                block.Renderer.enabled = block.Active;
            }
        }

        /// <summary>Scenery blocks drawn merged right now (for the render-shot breakdown).</summary>
        public int ActiveSceneryBlocks
        {
            get
            {
                int n = 0;
                foreach (var b in _blocks) if (b.Active) n++;
                return n;
            }
        }

        /// <summary>Layer of the far scenery chunks (the camera culls it beyond 4 km; an unnamed layer is enough).</summary>
        public const int FarSceneryLayer = 30;

        private static readonly int PaletteSize = System.Enum.GetValues(typeof(WorldMaterial)).Length;
        private Material _sceneryPalette;

        /// <summary>One material for the far scenery: every WorldMaterial's average colour in a palette texture.</summary>
        private Material SceneryPaletteMaterial()
        {
            if (_sceneryPalette != null) return _sceneryPalette;
            var palette = new Texture2D(PaletteSize, 1, TextureFormat.RGBA32, false) { name = "SceneryPalette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            foreach (WorldMaterial material in System.Enum.GetValues(typeof(WorldMaterial)))
            {
                var c = Color.grey;
                if (WorldMaterials.All.TryGetValue(material, out var entry)) c = new Color(entry.Item1.X, entry.Item1.Y, entry.Item1.Z, 1f);
                palette.SetPixel((int)material, 0, c);
            }

            palette.Apply(false, true);
            _sceneryPalette = Mat(Color.white, palette, 0f);
            return _sceneryPalette;
        }

        /// <summary>Low-detail scenery of one chunk merged into one mesh and one material (a colour palette); null if empty.</summary>
        private MeshRenderer BuildSceneryChunkMesh(Transform parent, List<SceneryItem> items)
        {
            // One mesh per chunk, one submesh per material.
            var parts = new Dictionary<WorldMaterial, (List<Vector3> V, List<Vector3> N, List<Vector2> UV, List<int> T)>();
            foreach (var item in items)
            {
                var model = SceneryModels.Get(item.Kind, item.Variant);
                var m = ItemMatrix(item);
                foreach (var pair in model.Lod1)
                {
                    if (!parts.TryGetValue(pair.Key, out var p)) parts[pair.Key] = p = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>());
                    int offset = p.V.Count;
                    var normals = Normals(pair.Key, pair.Value);
                    float tile = SurfaceTextures.TileM(pair.Key);
                    for (int i = 0; i < pair.Value.Vertices.Count; i++)
                    {
                        var v = pair.Value.Vertices[i];
                        var local = new Vector3(v.X, v.Y, v.Z);
                        var normal = new Vector3(normals[i].X, normals[i].Y, normals[i].Z);
                        p.V.Add(m.MultiplyPoint3x4(local));
                        p.N.Add(m.MultiplyVector(normal).normalized);
                        p.UV.Add(SurfaceTextures.UV(local, normal, tile)); // in model space, as in the near models
                    }

                    foreach (int t in pair.Value.Triangles) p.T.Add(t + offset);
                }
            }

            if (parts.Count == 0) return null;
            // These chunks are only seen beyond NearChunkRadius (380 m): one submesh, each material as its average
            // colour from a palette (one draw call per chunk instead of one per material; the phone drew ~800 here).
            var mesh = new Mesh { name = "Scenery", indexFormat = IndexFormat.UInt32 };
            var vertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var allUvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (var pair in parts)
            {
                int offset = vertices.Count;
                vertices.AddRange(pair.Value.V);
                allNormals.AddRange(pair.Value.N);
                var texel = new Vector2(((int)pair.Key + 0.5f) / PaletteSize, 0.5f);
                for (int i = 0; i < pair.Value.V.Count; i++) allUvs.Add(texel);
                foreach (int t in pair.Value.T) tris.Add(t + offset);
            }

            var materials = new List<Material> { SceneryPaletteMaterial() };
            mesh.SetVertices(vertices);
            mesh.SetNormals(allNormals);
            mesh.SetUVs(0, allUvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("Scenery") { layer = FarSceneryLayer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();
            // Only seen beyond 380 m, well past the shadow distance: no shadow.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
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
            StreamMountain(eye);

            // The instance lists are rebuilt when the camera has moved 5 m or turned 6°: only chunks in view (a frustum
            // a little wider than the camera's, so turning does not show gaps before the next rebuild) are drawn, plus,
            // shadow only, those within the shadow distance behind or beside the view. Everything else is skipped
            // (before: every chunk within 380 m all round, and every model cast shadows to 300 m).
            var forward = cam.transform.forward;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            if ((eye - _lastInstanceUpdate).sqrMagnitude > 25f || Mathf.Abs(Mathf.DeltaAngle(yaw, _lastInstanceYaw)) > 6f)
            {
                _lastInstanceUpdate = eye;
                _lastInstanceYaw = yaw;
                RebuildInstances(cam, eye);
            }

            int drawn = 0, calls = 0;
            long triangles = 0;
            foreach (var pair in _instances)
            {
                if (pair.Value.Count == 0) continue;
                var (mesh, materials) = Model(pair.Key.Kind, pair.Key.Variant, pair.Key.Lod);
                if (mesh == null) continue;
                var shadows = (ShadowCastingMode)pair.Key.Shadows;
                for (int start = 0; start < pair.Value.Count; start += _batch.Length)
                {
                    int count = Mathf.Min(_batch.Length, pair.Value.Count - start);
                    pair.Value.CopyTo(start, _batch, 0, count);
                    for (int s = 0; s < materials.Length; s++) Graphics.DrawMeshInstanced(mesh, s, materials[s], _batch, count, null, shadows, shadows != ShadowCastingMode.ShadowsOnly);
                    calls += materials.Length;
                }

                drawn += pair.Value.Count;
                if (pair.Key.Shadows != (int)ShadowCastingMode.ShadowsOnly) triangles += pair.Value.Count * TriangleCount(mesh);
            }

            InstancesDrawn = drawn;
            InstanceDrawCalls = calls;
            InstanceTriangles = triangles;
        }

        /// <summary>Scenery instances drawn and their draw calls in the last frame (for the performance log).</summary>
        public static int InstancesDrawn, InstanceDrawCalls;

        /// <summary>Triangles of the visible scenery instances in the last frame (shadow passes not counted).</summary>
        public static long InstanceTriangles;

        private readonly Dictionary<Mesh, long> _triangleCounts = new Dictionary<Mesh, long>();

        private long TriangleCount(Mesh mesh)
        {
            if (_triangleCounts.TryGetValue(mesh, out var n)) return n;
            for (int s = 0; s < mesh.subMeshCount; s++) n += mesh.GetIndexCount(s) / 3;
            _triangleCounts[mesh] = n;
            return n;
        }

        private float _lastInstanceYaw;
        private readonly Plane[] _frustum = new Plane[6];
        private readonly Dictionary<(SceneryKind, int), (bool Baked, float Range)> _bakedInfo = new Dictionary<(SceneryKind, int), (bool, float)>();

        private void RebuildInstances(Camera cam, Vector3 eye)
        {
            foreach (var list in _instances.Values) list.Clear();
            var projection = Matrix4x4.Perspective(Mathf.Min(170f, cam.fieldOfView + 25f), cam.aspect, cam.nearClipPlane, NearChunkRadius + 100f);
            GeometryUtility.CalculateFrustumPlanes(projection * cam.worldToCameraMatrix, _frustum);
            float shadowRange = ShadowDistance() + 15f, shadow2 = shadowRange * shadowRange;
            const float halfDiagonal = SceneryChunk * 0.75f;
            UpdateSceneryBlocks(eye);
            foreach (var chunk in _chunks)
            {
                var flat = new Vector2(chunk.Center.x - eye.x, chunk.Center.z - eye.z);
                bool near = flat.magnitude < NearChunkRadius;
                // far: its own mesh, unless its block is drawn merged
                if (chunk.FarRenderer != null) chunk.FarRenderer.enabled = !near && (chunk.Block == null || !chunk.Block.Active);
                if (!near) continue;
                chunk.Prepare();
                bool inView = GeometryUtility.TestPlanesAABB(_frustum, chunk.Bounds);
                if (!inView && flat.magnitude > shadowRange + halfDiagonal) continue;
                for (int i = 0; i < chunk.Items.Count; i++)
                {
                    var item = chunk.Items[i];
                    float dx = item.X - eye.x, dz = item.Z - eye.z, d2 = (dx * dx) + (dz * dz);
                    bool shadow = d2 < shadow2;
                    if (!inView && !shadow) continue;
                    // Close: the Blender model where there is one. With a baked middle-distance version (trees) the
                    // switch is at TreeDetailRadius; small props are shown all the way to DetailRadius; big ones like
                    // houses switch to the coded model at BakedRadius. Beyond DetailRadius, low detail.
                    var (baked, bakedRange) = BakedInfo(item.Kind, item.Variant);
                    int lod = d2 >= DetailRadius * DetailRadius ? 1
                        : baked && d2 < bakedRange * bakedRange ? 0
                        : 2;
                    var mode = !inView ? ShadowCastingMode.ShadowsOnly : shadow && lod != 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    var key = (item.Kind, item.Variant, lod, (int)mode);
                    if (!_instances.TryGetValue(key, out var list)) _instances[key] = list = new List<Matrix4x4>();
                    list.Add(chunk.Matrices[i]);
                }
            }
        }

        private (bool Baked, float Range) BakedInfo(SceneryKind kind, int variant)
        {
            if (_bakedInfo.TryGetValue((kind, variant), out var info)) return info;
            var baked = BakedModels.Get(kind, variant);
            bool bakedMiddle = BakedModels.Get(kind, variant, 1) != null;
            float range = bakedMiddle ? TreeDetailRadius : BakedModels.Triangles(baked) <= SmallBakedTriangles ? DetailRadius : BakedRadius;
            info = (baked != null, range);
            _bakedInfo[(kind, variant)] = info;
            return info;
        }

        /// <summary>The render pipeline's shadow distance (scenery farther away casts no shadow).</summary>
        private static float ShadowDistance() =>
            GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp ? urp.shadowDistance : QualitySettings.shadowDistance;

        /// <summary>The close-up mesh of a scenery model, in its local space (for <see cref="ChimneySmoke"/>).</summary>
        internal Mesh SceneryMesh(SceneryKind kind, int variant) => Model(kind, variant, 0).Mesh;

        /// <summary>Where an item stands (local to world).</summary>
        internal static Matrix4x4 PlacementMatrix(SceneryItem item) => ItemMatrix(item);

        private static Matrix4x4 ItemMatrix(SceneryItem item)
        {
            var m = Matrix4x4.TRS(new Vector3(item.X, item.Y, item.Z), Quaternion.Euler(0, item.RotationDeg, 0), Vector3.one * item.Scale);
            if (item.Slope == 0) return m;

            // Pieces that follow the ground (fences): sheared along their length, y += slope * x, posts stay upright.
            var shear = Matrix4x4.identity;
            shear.m10 = item.Slope;
            return m * shear;
        }

        /// <summary>Close range of the Blender-built models (<see cref="BakedModels"/>); the coded model takes over beyond.</summary>
        private const float BakedRadius = 70f;

        /// <summary>
        /// Models with a baked middle-distance version (trees) stay in full detail up to here (owner's request, 18 November
        /// 2026: the switch at 70 m was too visible). Measured: the larches at the worst forest point cost about
        /// 220,000 triangles all round at 130 m (135,000 at 70 m), before culling what is out of view.
        /// </summary>
        private const float TreeDetailRadius = 130f;

        /// <summary>
        /// Baked models up to this size are cheap enough to be shown all the way to <see cref="DetailRadius"/> (no jump to
        /// a differently shaped coded model at <see cref="BakedRadius"/>).
        /// </summary>
        private const int SmallBakedTriangles = 4000;

        /// <summary>
        /// The instanced mesh of a model: <paramref name="lod"/> 0 = the Blender-built model, 2 = the middle distance
        /// (baked "_lod1" version where there is one, else the coded near model), 1 = the coded far model.
        /// </summary>
        private (Mesh Mesh, Material[] Materials) Model(SceneryKind kind, int variant, int lod)
        {
            var key = (kind, variant, lod);
            if (_models.TryGetValue(key, out var cached)) return cached;

            // Collect (material, positions, normals, triangles) from the baked or the coded model.
            var source = new List<(WorldMaterial Material, Vector3[] Positions, Vector3[] Normals, int[] Triangles, Vector2[] Uvs, string Texture)>();
            // lod 0: the close-up baked model; lod 2: a baked middle-distance version if there is one, else coded.
            var baked = lod == 0 ? BakedModels.Get(kind, variant) : lod == 2 ? BakedModels.Get(kind, variant, 1) : null;
            if (baked != null)
            {
                foreach (var part in baked) source.Add((part.Material, part.Positions, part.Normals, part.Triangles, part.Uvs, part.TextureStem));
            }
            else
            {
                var model = SceneryModels.Get(kind, variant);
                foreach (var pair in lod == 1 ? model.Lod1 : model.Lod0)
                {
                    var n = Normals(pair.Key, pair.Value);
                    var positions = new Vector3[pair.Value.Vertices.Count];
                    var normalsOut = new Vector3[positions.Length];
                    for (int i = 0; i < positions.Length; i++)
                    {
                        var v = pair.Value.Vertices[i];
                        positions[i] = new Vector3(v.X, v.Y, v.Z);
                        normalsOut[i] = new Vector3(n[i].X, n[i].Y, n[i].Z);
                    }

                    source.Add((pair.Key, positions, normalsOut, pair.Value.Triangles.ToArray(), null, null));
                }
            }

            if (source.Count == 0)
            {
                _models[key] = (null, null);
                return (null, null);
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var materials = new List<Material>();
            var submeshes = new List<List<int>>();
            foreach (var part in source)
            {
                int offset = vertices.Count;
                float tile = SurfaceTextures.TileM(part.Material);
                for (int i = 0; i < part.Positions.Length; i++)
                {
                    vertices.Add(part.Positions[i]);
                    normals.Add(part.Normals[i]);
                    uvs.Add(part.Uvs != null ? part.Uvs[i] : SurfaceTextures.UV(part.Positions[i], part.Normals[i], tile));
                }

                var tris = new List<int>(part.Triangles.Length);
                foreach (int t in part.Triangles) tris.Add(t + offset);
                submeshes.Add(tris);
                materials.Add(part.Texture != null ? OwnTextureMat(part.Texture) : Mat(part.Material, IsFoliage(part.Material)));
            }

            var mesh = new Mesh { name = $"{kind} {variant} LOD{lod}", indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s);
            mesh.RecalculateTangents();
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

            // No ring where the real mountain stands (its own terrain is there)
            var mountain = _terrain.Mountain;
            bool Hidden(int i)
            {
                if (mountain == null) return false;
                float a = i * Mathf.PI * 2f / segments;
                for (float r = inner; r <= outer; r += 150f)
                {
                    float x = center.x + (Mathf.Cos(a) * r), z = center.y + (Mathf.Sin(a) * r);
                    if (mountain.Contains(x, z) && mountain.RoadDistance(x, z) < 2400) return true;
                }

                return false;
            }

            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float ridge = 90f + (Mathf.PerlinNoise(i * 0.18f, 3.3f) * 260f) + (Mathf.PerlinNoise(i * 0.7f, 9.1f) * 60f);
                var c = new Vector3(center.x, baseY, center.y);
                bool connect = i < segments && !Hidden(i) && !Hidden(i + 1);
                mesh.Ring(c + (dir * inner), c + (dir * (inner + 450f)) + (Vector3.up * ridge), c + (dir * outer) + (Vector3.up * (ridge * 0.4f)), connect);
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
                    // Up-facing whatever the rim direction: the same winding as the road strips (front face when
                    // Cross(v1 − v0, v2 − v0) points up). The other way round, pads and lakes were culled as back faces.
                    var n = Vector3.Cross(_v[a] - _v[c], _v[b] - _v[c]);
                    if (n.y > 0) _t.AddRange(new[] { c, a, b });
                    else _t.AddRange(new[] { c, b, a });
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
                mesh.RecalculateTangents();
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
