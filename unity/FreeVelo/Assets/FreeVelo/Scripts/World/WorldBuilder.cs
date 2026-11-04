using System.Collections.Generic;
using FreeVelo.Core.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace FreeVelo.World
{
    /// <summary>Builds a simple 3D world from a route: road, ground, trees (placeholder art).</summary>
    public sealed class WorldBuilder : MonoBehaviour
    {
        private const float RoadWidth = 6f;
        private const float GroundCell = 15f;
        private const float GroundMargin = 300f;

        private GameObject _root;
        private Material _template;
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        private Vector3[] _coarse;

        public void Build(Route route)
        {
            if (_root != null) Destroy(_root);
            _root = new GameObject("World");

            _coarse = new Vector3[(route.Points.Count / 4) + 1];
            for (int i = 0, j = 0; i < route.Points.Count && j < _coarse.Length; i += 4, j++)
            {
                var p = route.Points[i];
                _coarse[j] = new Vector3((float)p.X, (float)p.ElevationM, (float)p.Z);
            }

            BuildRoad(route);
            BuildGround(route);
            BuildTrees(route, 350);
        }

        /// <summary>Material based on the URP Lit material in Resources (keeps the shader in builds).</summary>
        public Material Mat(Color color)
        {
            if (_materials.TryGetValue(color, out var m)) return m;
            if (_template == null)
            {
                _template = Resources.Load<Material>("FreeVeloLit");
                if (_template == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    _template = new Material(shader);
                }
            }

            m = new Material(_template) { color = color, enableInstancing = true };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            _materials[color] = m;
            return m;
        }

        private void BuildRoad(Route route)
        {
            int n = route.Points.Count;
            var vertices = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var triangles = new int[(n - 1) * 6];
            var line = new List<Vector3>();
            var lineTris = new List<int>();

            for (int i = 0; i < n; i++)
            {
                var p = route.Points[i];
                var prev = route.Points[Mathf.Max(0, i - 1)];
                var next = route.Points[Mathf.Min(n - 1, i + 1)];
                var forward = new Vector3((float)(next.X - prev.X), 0, (float)(next.Z - prev.Z)).normalized;
                var right = new Vector3(forward.z, 0, -forward.x);
                var center = new Vector3((float)p.X, (float)p.ElevationM + 0.05f, (float)p.Z);

                vertices[i * 2] = center - (right * (RoadWidth / 2));
                vertices[(i * 2) + 1] = center + (right * (RoadWidth / 2));
                float v = (float)p.DistanceM / RoadWidth;
                uvs[i * 2] = new Vector2(0, v);
                uvs[(i * 2) + 1] = new Vector2(1, v);

                if (i < n - 1)
                {
                    int t = i * 6;
                    int a = i * 2;
                    triangles[t] = a;
                    triangles[t + 1] = a + 2;
                    triangles[t + 2] = a + 1;
                    triangles[t + 3] = a + 1;
                    triangles[t + 4] = a + 2;
                    triangles[t + 5] = a + 3;
                }

                // Dashed centre line: every second 5 m segment.
                if (i < n - 1 && i % 2 == 0)
                {
                    var q = route.Points[i + 1];
                    var c2 = new Vector3((float)q.X, (float)q.ElevationM + 0.07f, (float)q.Z);
                    var c1 = center + new Vector3(0, 0.02f, 0);
                    int b = line.Count;
                    line.Add(c1 - (right * 0.08f));
                    line.Add(c1 + (right * 0.08f));
                    line.Add(c2 - (right * 0.08f));
                    line.Add(c2 + (right * 0.08f));
                    lineTris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }

            CreateMesh("Road", vertices, uvs, triangles, new Color(0.22f, 0.22f, 0.24f));
            CreateMesh("CentreLine", line.ToArray(), null, lineTris.ToArray(), new Color(0.95f, 0.95f, 0.9f));
        }

        private void BuildGround(Route route)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, avgY = 0;
            foreach (var p in _coarse)
            {
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z);
                maxZ = Mathf.Max(maxZ, p.z);
                avgY += p.y;
            }

            avgY /= _coarse.Length;
            minX -= GroundMargin;
            minZ -= GroundMargin;
            int nx = Mathf.CeilToInt((maxX + GroundMargin - minX) / GroundCell) + 1;
            int nz = Mathf.CeilToInt((maxZ + GroundMargin - minZ) / GroundCell) + 1;

            var vertices = new Vector3[nx * nz];
            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++)
                {
                    float wx = minX + (x * GroundCell);
                    float wz = minZ + (z * GroundCell);
                    vertices[(z * nx) + x] = new Vector3(wx, GroundHeight(wx, wz, avgY), wz);
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

            CreateMesh("Ground", vertices, null, triangles, new Color(0.36f, 0.55f, 0.27f));
        }

        /// <summary>Follows the road near it (slightly below), flattens out further away.</summary>
        private float GroundHeight(float x, float z, float baseY)
        {
            float best = float.MaxValue;
            float roadY = baseY;
            foreach (var p in _coarse)
            {
                float dx = p.x - x, dz = p.z - z;
                float d2 = (dx * dx) + (dz * dz);
                if (d2 < best)
                {
                    best = d2;
                    roadY = p.y;
                }
            }

            float blend = Mathf.SmoothStep(0, 1, (Mathf.Sqrt(best) - 12f) / 150f);
            return Mathf.Lerp(roadY - 0.6f, baseY - 1f, blend);
        }

        private void BuildTrees(Route route, int count)
        {
            var random = new System.Random(route.Name.GetHashCode());
            var trunk = Mat(new Color(0.4f, 0.27f, 0.15f));
            var leaves = new[] { Mat(new Color(0.18f, 0.42f, 0.18f)), Mat(new Color(0.24f, 0.5f, 0.2f)), Mat(new Color(0.15f, 0.35f, 0.2f)) };
            float avgY = 0;
            foreach (var p in _coarse) avgY += p.y;
            avgY /= _coarse.Length;

            int placed = 0;
            for (int attempt = 0; attempt < count * 10 && placed < count; attempt++)
            {
                var anchor = _coarse[random.Next(_coarse.Length)];
                float angle = (float)(random.NextDouble() * Mathf.PI * 2);
                float dist = 12f + (float)(random.NextDouble() * 120);
                float x = anchor.x + (Mathf.Cos(angle) * dist);
                float z = anchor.z + (Mathf.Sin(angle) * dist);
                if (DistanceToRoad(x, z) < 10f) continue;

                float y = GroundHeight(x, z, avgY);
                float size = 0.8f + (float)random.NextDouble() * 0.8f;
                var tree = new GameObject("Tree");
                tree.transform.SetParent(_root.transform);
                tree.transform.position = new Vector3(x, y, z);
                Primitive(PrimitiveType.Cylinder, tree.transform, new Vector3(0, 1.5f * size, 0), new Vector3(0.4f, 1.5f, 0.4f) * size, trunk);
                Primitive(PrimitiveType.Sphere, tree.transform, new Vector3(0, 4f * size, 0), new Vector3(3.5f, 4f, 3.5f) * size, leaves[random.Next(leaves.Length)]);
                placed++;
            }
        }

        private float DistanceToRoad(float x, float z)
        {
            float best = float.MaxValue;
            foreach (var p in _coarse)
            {
                float dx = p.x - x, dz = p.z - z;
                best = Mathf.Min(best, (dx * dx) + (dz * dz));
            }

            return Mathf.Sqrt(best);
        }

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

        private void CreateMesh(string name, Vector3[] vertices, Vector2[] uvs, int[] triangles, Color color)
        {
            var mesh = new Mesh { name = name, indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices;
            if (uvs != null) mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Mat(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
