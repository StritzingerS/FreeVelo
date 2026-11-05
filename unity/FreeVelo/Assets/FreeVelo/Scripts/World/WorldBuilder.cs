using System.Collections.Generic;
using FreeVelo.Core.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace FreeVelo.World
{
    /// <summary>
    /// Builds the 3D world from a route, entirely in code: textured road with markings and shoulders,
    /// rolling textured ground, distant mountains, trees, bushes. Vegetation is merged into a few meshes.
    /// </summary>
    public sealed class WorldBuilder : MonoBehaviour
    {
        private const float RoadWidth = 6.5f;
        private const float ShoulderWidth = 0.9f;
        private const float GroundCell = 12f;
        private const float GroundMargin = 450f;

        private readonly Dictionary<(Color, Texture), Material> _materials = new Dictionary<(Color, Texture), Material>();
        private GameObject _root;
        private Material _template;
        private Vector3[] _coarse;
        private float _baseY;
        private Vector2 _center;
        private float _radius;

        // Spatial index: route segment indices per 80 m cell.
        private const float IndexCell = 80f;
        private const float MaxRoadQuery = 330f;
        private readonly Dictionary<(int, int), List<int>> _segmentIndex = new Dictionary<(int, int), List<int>>();

        public void Build(Route route)
        {
            if (_root != null) Destroy(_root);
            _root = new GameObject("World");

            // Route points every 20 m for distance/height queries.
            _coarse = new Vector3[(route.Points.Count / 4) + 1];
            for (int i = 0, j = 0; j < _coarse.Length; i = Mathf.Min(i + 4, route.Points.Count - 1), j++)
            {
                var p = route.Points[i];
                _coarse[j] = new Vector3((float)p.X, (float)p.ElevationM, (float)p.Z);
            }

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            _baseY = 0;
            foreach (var p in _coarse)
            {
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z);
                maxZ = Mathf.Max(maxZ, p.z);
                _baseY += p.y;
            }

            _baseY /= _coarse.Length;
            BuildSegmentIndex();
            _center = new Vector2((minX + maxX) / 2, (minZ + maxZ) / 2);
            _radius = Mathf.Max(maxX - minX, maxZ - minZ) / 2;

            BuildRoad(route);
            BuildGround(minX, maxX, minZ, maxZ);
            BuildMountains();
            BuildVegetation(route);
        }

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

        // ------------------------------------------------------------------ road

        private void BuildRoad(Route route)
        {
            int n = route.Points.Count;
            var road = new MeshData();
            var lines = new MeshData();
            var shoulders = new MeshData();
            float half = RoadWidth / 2;

            for (int i = 0; i < n; i++)
            {
                var p = route.Points[i];
                var prev = route.Points[Mathf.Max(0, i - 1)];
                var next = route.Points[Mathf.Min(n - 1, i + 1)];
                var forward = new Vector3((float)(next.X - prev.X), 0, (float)(next.Z - prev.Z)).normalized;
                var right = new Vector3(forward.z, 0, -forward.x);
                var c = new Vector3((float)p.X, (float)p.ElevationM + 0.05f, (float)p.Z);
                float v = (float)p.DistanceM / RoadWidth;

                road.Strip(c - (right * half), c + (right * half), v, i < n - 1);

                // Shoulders: gravel, sloping slightly down to the grass.
                var drop = new Vector3(0, -0.32f, 0); // meets the ground (road height − 0.3 m)
                float sv = (float)p.DistanceM / 2f;
                shoulders.Strip(c - (right * (half + ShoulderWidth)) + drop, c - (right * half) + (Vector3.up * -0.01f), sv, i < n - 1);
                shoulders.Strip(c + (right * half) + (Vector3.up * -0.01f), c + (right * (half + ShoulderWidth)) + drop, sv, i < n - 1);

                // Solid edge lines and dashed centre line (marking slightly above the asphalt).
                var up = Vector3.up * 0.015f;
                lines.Strip(c - (right * (half - 0.2f)) + up, c - (right * (half - 0.32f)) + up, 0, i < n - 1);
                lines.Strip(c + (right * (half - 0.32f)) + up, c + (right * (half - 0.2f)) + up, 0, i < n - 1);
            }

            for (int i = 0; i < n - 1; i += 2)
            {
                var a = route.Points[i];
                var b = route.Points[i + 1];
                var dir = new Vector3((float)(b.X - a.X), 0, (float)(b.Z - a.Z)).normalized;
                var right = new Vector3(dir.z, 0, -dir.x) * 0.07f;
                var ca = new Vector3((float)a.X, (float)a.ElevationM + 0.065f, (float)a.Z);
                var cb = new Vector3((float)b.X, (float)b.ElevationM + 0.065f, (float)b.Z);
                lines.Quad(ca - right, ca + right, cb - right, cb + right);
            }

            CreateMesh("Road", road, Mat(Color.white, TextureFactory.Asphalt(), 0.12f), shadows: false);
            CreateMesh("Shoulders", shoulders, Mat(Color.white, TextureFactory.Gravel(), 0.05f), shadows: false);
            CreateMesh("Markings", lines, Mat(new Color(0.92f, 0.92f, 0.88f), null, 0.3f), shadows: false);
        }

        // ------------------------------------------------------------------ ground

        private void BuildGround(float minX, float maxX, float minZ, float maxZ)
        {
            minX -= GroundMargin;
            minZ -= GroundMargin;
            int nx = Mathf.CeilToInt((maxX + GroundMargin - minX) / GroundCell) + 1;
            int nz = Mathf.CeilToInt((maxZ + GroundMargin - minZ) / GroundCell) + 1;

            var vertices = new Vector3[nx * nz];
            var uvs = new Vector2[nx * nz];
            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++)
                {
                    float wx = minX + (x * GroundCell);
                    float wz = minZ + (z * GroundCell);
                    vertices[(z * nx) + x] = new Vector3(wx, GroundHeight(wx, wz), wz);
                    uvs[(z * nx) + x] = new Vector2(wx / 18f, wz / 18f);
                }
            }

            var triangles = new int[(nx - 1) * (nz - 1) * 6];
            int t = 0;
            for (int z = 0; z < nz - 1; z++)
            {
                for (int x = 0; x < nx - 1; x++)
                {
                    int a = (z * nx) + x;
                    triangles[t++] = a;
                    triangles[t++] = a + nx;
                    triangles[t++] = a + 1;
                    triangles[t++] = a + 1;
                    triangles[t++] = a + nx;
                    triangles[t++] = a + nx + 1;
                }
            }

            var mesh = new Mesh { name = "Ground", indexFormat = IndexFormat.UInt32, vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AddRenderer("Ground", mesh, Mat(Color.white, TextureFactory.Grass(), 0f), shadows: false);
        }

        /// <summary>Ground height: follows the road (slightly below) nearby, rolling hills further away.</summary>
        public float GroundHeight(float x, float z)
        {
            float distance = DistanceToRoad(x, z, out float roadY);
            float hills = (Mathf.PerlinNoise((x * 0.0025f) + 100f, (z * 0.0025f) + 100f) - 0.45f) * 45f
                        + ((Mathf.PerlinNoise((x * 0.012f) + 7f, (z * 0.012f) + 3f) - 0.5f) * 6f);
            float far = Mathf.SmoothStep(0f, 1f, (distance - 15f) / 300f);
            float near = roadY - 0.3f;
            return Mathf.Lerp(near, _baseY + hills, far) + (Mathf.SmoothStep(0f, 1f, (distance - 6f) / 30f) * 0.6f * (1 - far));
        }

        private void BuildSegmentIndex()
        {
            _segmentIndex.Clear();
            for (int i = 0; i < _coarse.Length - 1; i++)
            {
                int x0 = Mathf.FloorToInt(Mathf.Min(_coarse[i].x, _coarse[i + 1].x) / IndexCell);
                int x1 = Mathf.FloorToInt(Mathf.Max(_coarse[i].x, _coarse[i + 1].x) / IndexCell);
                int z0 = Mathf.FloorToInt(Mathf.Min(_coarse[i].z, _coarse[i + 1].z) / IndexCell);
                int z1 = Mathf.FloorToInt(Mathf.Max(_coarse[i].z, _coarse[i + 1].z) / IndexCell);
                for (int cx = x0; cx <= x1; cx++)
                {
                    for (int cz = z0; cz <= z1; cz++)
                    {
                        if (!_segmentIndex.TryGetValue((cx, cz), out var list))
                        {
                            list = new List<int>();
                            _segmentIndex[(cx, cz)] = list;
                        }

                        list.Add(i);
                    }
                }
            }
        }

        /// <summary>
        /// Distance to the road centre line (capped at ~330 m) and the road height at the closest point.
        /// </summary>
        private float DistanceToRoad(float x, float z, out float roadY)
        {
            float best = MaxRoadQuery * MaxRoadQuery;
            roadY = _baseY;
            var p = new Vector2(x, z);
            int range = Mathf.CeilToInt(MaxRoadQuery / IndexCell);
            int cx0 = Mathf.FloorToInt(x / IndexCell), cz0 = Mathf.FloorToInt(z / IndexCell);
            for (int cx = cx0 - range; cx <= cx0 + range; cx++)
            {
                for (int cz = cz0 - range; cz <= cz0 + range; cz++)
                {
                    if (!_segmentIndex.TryGetValue((cx, cz), out var list)) continue;
                    foreach (int i in list) Closest(i, p, ref best, ref roadY);
                }
            }

            return Mathf.Sqrt(best);
        }

        private void Closest(int i, Vector2 p, ref float best, ref float roadY)
        {
            {
                var a = new Vector2(_coarse[i].x, _coarse[i].z);
                var b = new Vector2(_coarse[i + 1].x, _coarse[i + 1].z);
                var ab = b - a;
                float len2 = ab.sqrMagnitude;
                float f = len2 > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0;
                float d2 = (a + (ab * f) - p).sqrMagnitude;
                if (d2 < best)
                {
                    best = d2;
                    roadY = Mathf.Lerp(_coarse[i].y, _coarse[i + 1].y, f);
                }
            }
        }

        // ------------------------------------------------------------------ horizon

        private void BuildMountains()
        {
            const int segments = 120;
            float inner = _radius + GroundMargin + 200f;
            float outer = inner + 900f;
            var mesh = new MeshData();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float ridge = 90f + (Mathf.PerlinNoise(i * 0.18f, 3.3f) * 260f) + (Mathf.PerlinNoise(i * 0.7f, 9.1f) * 60f);
                var c = new Vector3(_center.x, _baseY - 15f, _center.y);
                mesh.AddRing(c + (dir * inner), c + (dir * (inner + 450f)) + (Vector3.up * ridge), c + (dir * outer) + (Vector3.up * (ridge * 0.4f)), i < segments);
            }

            CreateMesh("Mountains", mesh, Mat(new Color(0.42f, 0.5f, 0.45f), null, 0f), shadows: false);
        }

        // ------------------------------------------------------------------ vegetation

        private void BuildVegetation(Route route)
        {
            var random = new System.Random(route.Name.GetHashCode());
            var trunk = new MeshBuilder();
            var conifer = new MeshBuilder();
            var leavesA = new MeshBuilder();
            var leavesB = new MeshBuilder();
            var bushes = new MeshBuilder();

            // Forests: dense clusters near the road, plus scattered single trees.
            int forests = 14;
            for (int f = 0; f < forests; f++)
            {
                var anchor = _coarse[random.Next(_coarse.Length)];
                float angle = (float)(random.NextDouble() * Mathf.PI * 2);
                float dist = 40f + (float)(random.NextDouble() * 160);
                var center = new Vector2(anchor.x + (Mathf.Cos(angle) * dist), anchor.z + (Mathf.Sin(angle) * dist));
                bool coniferous = random.NextDouble() < 0.6;
                int trees = 30 + random.Next(30);
                for (int t = 0; t < trees; t++)
                {
                    var pos = center + (RandomInCircle(random) * 70f);
                    PlaceTree(random, pos, coniferous || random.NextDouble() < 0.15, trunk, conifer, leavesA, leavesB);
                }
            }

            for (int t = 0; t < 260; t++)
            {
                var anchor = _coarse[random.Next(_coarse.Length)];
                float angle = (float)(random.NextDouble() * Mathf.PI * 2);
                float dist = 11f + (float)(random.NextDouble() * 220);
                var pos = new Vector2(anchor.x + (Mathf.Cos(angle) * dist), anchor.z + (Mathf.Sin(angle) * dist));
                PlaceTree(random, pos, random.NextDouble() < 0.3, trunk, conifer, leavesA, leavesB);
            }

            for (int b = 0; b < 350; b++)
            {
                var anchor = _coarse[random.Next(_coarse.Length)];
                float angle = (float)(random.NextDouble() * Mathf.PI * 2);
                float dist = 7f + (float)(random.NextDouble() * 60);
                float x = anchor.x + (Mathf.Cos(angle) * dist), z = anchor.z + (Mathf.Sin(angle) * dist);
                if (DistanceToRoad(x, z, out _) < 6.5f) continue;
                float s = 0.6f + (float)random.NextDouble();
                bushes.Ellipsoid(new Vector3(x, GroundHeight(x, z) + (0.35f * s), z), new Vector3(1.1f * s, 0.6f * s, 0.9f * s), Quaternion.Euler(0, random.Next(360), 0), 7, 4);
            }

            CreateMesh("Trunks", trunk, Mat(new Color(0.33f, 0.24f, 0.16f), null, 0f), shadows: true);
            CreateMesh("Conifers", conifer, Mat(new Color(0.12f, 0.27f, 0.15f), null, 0f), shadows: true);
            CreateMesh("LeavesA", leavesA, Mat(new Color(0.22f, 0.42f, 0.16f), null, 0f), shadows: true);
            CreateMesh("LeavesB", leavesB, Mat(new Color(0.3f, 0.47f, 0.18f), null, 0f), shadows: true);
            CreateMesh("Bushes", bushes, Mat(new Color(0.2f, 0.36f, 0.14f), null, 0f), shadows: true);
        }

        private void PlaceTree(System.Random random, Vector2 pos, bool coniferous, MeshBuilder trunk, MeshBuilder conifer, MeshBuilder leavesA, MeshBuilder leavesB)
        {
            if (DistanceToRoad(pos.x, pos.y, out _) < 9f) return;
            float s = 0.75f + ((float)random.NextDouble() * 0.6f);
            var b = new Vector3(pos.x, GroundHeight(pos.x, pos.y) - 0.2f, pos.y);

            if (coniferous)
            {
                float h = 14f * s;
                trunk.Tube(b, b + (Vector3.up * h * 0.35f), 0.28f * s, 0.18f * s, 6, false);
                for (int layer = 0; layer < 3; layer++)
                {
                    float y0 = h * (0.18f + (layer * 0.24f));
                    float r = 3.2f * s * (1f - (layer * 0.26f));
                    conifer.Tube(b + (Vector3.up * y0), b + (Vector3.up * (y0 + (h * 0.42f))), r, 0.05f, 8);
                }
            }
            else
            {
                float h = 10f * s;
                trunk.Tube(b, b + (Vector3.up * h * 0.55f), 0.32f * s, 0.2f * s, 6, false);
                var leaves = random.NextDouble() < 0.5 ? leavesA : leavesB;
                int blobs = 3 + random.Next(3);
                for (int k = 0; k < blobs; k++)
                {
                    var offset = new Vector3(((float)random.NextDouble() - 0.5f) * 3f * s, h * (0.55f + ((float)random.NextDouble() * 0.3f)), ((float)random.NextDouble() - 0.5f) * 3f * s);
                    float r = (2.2f + ((float)random.NextDouble() * 1.4f)) * s;
                    leaves.Ellipsoid(b + offset, new Vector3(r, r * 0.85f, r), Quaternion.Euler(0, random.Next(360), 0), 8, 5);
                }
            }
        }

        private static Vector2 RandomInCircle(System.Random random)
        {
            float a = (float)(random.NextDouble() * Mathf.PI * 2);
            float r = Mathf.Sqrt((float)random.NextDouble());
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
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

        private void CreateMesh(string name, MeshBuilder builder, Material material, bool shadows)
        {
            if (builder.VertexCount == 0) return;
            AddRenderer(name, builder.ToMesh(name), material, shadows);
        }

        private void CreateMesh(string name, MeshData data, Material material, bool shadows) =>
            AddRenderer(name, data.ToMesh(name), material, shadows);

        private void AddRenderer(string name, Mesh mesh, Material material, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        /// <summary>Strips along the route (road, shoulders, lines) and the mountain ring.</summary>
        private sealed class MeshData
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int> _t = new List<int>();

            /// <summary>Adds a left/right vertex pair; connects it to the next pair when <paramref name="connect"/>.</summary>
            public void Strip(Vector3 left, Vector3 right, float v, bool connect)
            {
                int a = _v.Count;
                _v.Add(left);
                _v.Add(right);
                _uv.Add(new Vector2(0, v));
                _uv.Add(new Vector2(1, v));
                if (connect) AddQuadIndices(a, a + 1, a + 2, a + 3);
            }

            /// <summary>Separate quad (a0,a1 at the start, b0,b1 at the end of a segment).</summary>
            public void Quad(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
            {
                int a = _v.Count;
                _v.Add(a0);
                _v.Add(a1);
                _v.Add(b0);
                _v.Add(b1);
                for (int i = 0; i < 4; i++) _uv.Add(Vector2.zero);
                AddQuadIndices(a, a + 1, a + 2, a + 3);
            }

            /// <summary>Mountain ring: three vertices per angle (foot, ridge, back), visible from inside and outside.</summary>
            public void AddRing(Vector3 foot, Vector3 ridge, Vector3 back, bool connect)
            {
                int a = _v.Count;
                _v.Add(foot);
                _v.Add(ridge);
                _v.Add(back);
                _uv.Add(Vector2.zero);
                _uv.Add(Vector2.zero);
                _uv.Add(Vector2.zero);
                if (!connect) return;
                for (int k = 0; k < 2; k++)
                {
                    int p0 = a + k, p1 = a + k + 1, q0 = a + 3 + k, q1 = a + 4 + k;
                    _t.AddRange(new[] { p0, q0, p1, q0, q1, p1 }); // faces the route (inside the ring)
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(_v);
                mesh.SetUVs(0, _uv);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            // Pair (left a, right b) followed by pair (left c, right d): clockwise from above.
            private void AddQuadIndices(int a, int b, int c, int d)
            {
                _t.Add(a);
                _t.Add(c);
                _t.Add(b);
                _t.Add(b);
                _t.Add(c);
                _t.Add(d);
            }
        }
    }
}
