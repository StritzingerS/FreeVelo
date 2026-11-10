using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FreeVelo.Core.World.Gen;
using UnityEngine;
using UnityEngine.Rendering;

namespace FreeVelo.World
{
    /// <summary>
    /// The mountain (Passo dello Stelvio, Alpe d'Huez): far away as coarse 512 m tiles (32 m grid, ground cover by
    /// height and slope, forest as dark ground); close to the rider each tile is replaced by 16 detailed 128 m pieces
    /// (4 m grid with road beds, trees, rocks), computed in the background and dropped again behind the rider.
    /// The pieces share one grid with the home world's terrain chunks, so everything joins exactly.
    /// </summary>
    public sealed partial class WorldBuilder
    {
        private const float MountainChunk = 128f;
        private const int ChunksPerTile = 4;
        private const float TileSize = MountainChunk * ChunksPerTile;

        /// <summary>Tiles closer than this (to their edge) are shown in detail.</summary>
        private const float DetailTileRadius = 520f;

        /// <summary>Detailed tiles are dropped again beyond this.</summary>
        private const float DropTileRadius = 900f;

        private const int MaxBuildsAtOnce = 2;

        private sealed class MountainTile
        {
            public int Tx;
            public int Tz;
            public Rect Area;
            public LODGroup Far;
            public GameObject Near;
            public Task<NearTileData> Building;
            public readonly List<SceneryChunkData> Scenery = new List<SceneryChunkData>();
        }

        /// <summary>Heights and items of one detailed tile, computed off the main thread.</summary>
        private sealed class NearTileData
        {
            /// <summary>Per piece: heights for the 4 m mesh and for its 8 m version (both kept below the roads).</summary>
            public readonly List<(int Cx, int Cz, float[] Heights, float[] Coarse, int N, List<SceneryItem> Items, GroundKind[] Cover)> Chunks = new List<(int, int, float[], float[], int, List<SceneryItem>, GroundKind[])>();
        }

        private readonly Dictionary<(int, int), MountainTile> _tiles = new Dictionary<(int, int), MountainTile>();
        private Rect _homeGrid;
        private float _originX;
        private float _originZ;
        private Material[] _coverMaterials;
        private Vector3 _lastStreamCheck = new Vector3(float.MaxValue, 0, 0);

        private bool HasMountain => _terrain?.Mountain != null;

        // ------------------------------------------------------------------ setup

        private void BuildMountain(List<FarTileData> far)
        {
            if (!HasMountain) return;
            var m = _terrain.Mountain;
            _homeGrid = Rect.MinMaxRect((float)_terrain.MinX, (float)_terrain.MinZ, (float)_terrain.MaxX, (float)_terrain.MaxZ);
            _originX = (float)_terrain.MinX;
            _originZ = (float)_terrain.MinZ;
            var grass = TextureFactory.Grass();
            _coverMaterials = new[]
            {
                Mat(Color.white, grass, 0f),                                  // Grass
                Mat(new Color(0.16f, 0.27f, 0.15f), null, 0f),                // Forest (seen from afar)
                Mat(new Color(0.86f, 0.9f, 0.62f), grass, 0f),                // Alpine meadow
                Mat(new Color(0.47f, 0.46f, 0.44f), TextureFactory.Gravel(), 0.05f), // Rock
                Mat(new Color(0.95f, 0.97f, 1f), null, 0.35f),                // Snow
                Mat(Color.white, TextureFactory.Rows(Crop(WorldMaterial.Wheat)), 0f),  // Fields: wheat
                Mat(Color.white, TextureFactory.Rows(Crop(WorldMaterial.Crop)), 0f),   // green crop
                Mat(Color.white, TextureFactory.Rows(Crop(WorldMaterial.Plowed)), 0f), // ploughed
            };

            // Far tiles over the whole mountain area (not over the home grid): computed in the background while the
            // world loads (ComputeFarTiles); only the meshes are made here.
            var farTiles = far ?? ComputeFarTiles(_terrain);
            var farRoot = new GameObject("Mountain");
            farRoot.transform.SetParent(_root.transform, false);
            foreach (var data in farTiles)
            {
                var tile = new MountainTile { Tx = data.Tx, Tz = data.Tz, Area = data.Area };
                var go = new GameObject($"Mountain {data.Tx},{data.Tz}");
                go.transform.SetParent(farRoot.transform, false);
                var r0 = AddRenderer(go.transform, "LOD0", data.Lod0.ToMesh("MountainFar"), _coverMaterials, false);
                var r1 = AddRenderer(go.transform, "LOD1", data.Lod1.ToMesh("MountainFar"), _coverMaterials, false);
                tile.Far = go.AddComponent<LODGroup>();
                tile.Far.SetLODs(new[] { new LOD(0.06f, new Renderer[] { r0 }), new LOD(0.0004f, new Renderer[] { r1 }) });
                tile.Far.RecalculateBounds();
                _tiles[(data.Tx, data.Tz)] = tile;
            }

            BuildSummitSigns();
            BuildVillageSigns();
            BuildLanes();
        }

        /// <summary>Number of ground covers (submeshes of the mountain pieces).</summary>
        private static readonly int CoverKinds = System.Enum.GetValues(typeof(GroundKind)).Length;

        private static Color Crop(WorldMaterial crop)
        {
            var (c, _) = WorldMaterials.All[crop];
            return new Color(c.X, c.Y, c.Z);
        }

        /// <summary>
        /// The side lanes as one strip each, following the ground every 2 m and rising to the road surface where they
        /// leave the road (rigid pieces stuck out of the meadow wherever it sloped).
        /// </summary>
        private void BuildLanes()
        {
            var landscape = _terrain.Landscape;
            if (landscape == null || landscape.Lanes.Count == 0) return;
            var paved = new Strips();
            var gravel = new Strips();
            foreach (var (ax, az, bx, bz, isGravel) in landscape.Lanes)
            {
                float length = Mathf.Sqrt((float)(((bx - ax) * (bx - ax)) + ((bz - az) * (bz - az))));
                if (length < 1) continue;
                var along = new Vector3((float)(bx - ax), 0, (float)(bz - az)) / length;
                var right = new Vector3(along.z, 0, -along.x);
                float half = isGravel ? 1.5f : 1.7f;
                var strip = isGravel ? gravel : paved;
                strip.Begin();
                int steps = Mathf.CeilToInt(length / 2f);
                for (int k = 0; k <= steps; k++)
                {
                    float t = length * k / steps;
                    double x = ax + (along.x * t), z = az + (along.z * t);
                    float y = (float)_terrain.Height(x, z) + 0.08f;
                    double d = _terrain.DistanceToRoad(x, z, 12, out var road, out double roadHeight, out _);
                    if (road != null)
                    {
                        // Up onto the road where the lane joins it, then down to the meadow over a couple of metres.
                        float over = (float)(d - (road.Width / 2));
                        float blend = Mathf.Clamp01(1 - (over / 3.5f));
                        y = Mathf.Max(y, Mathf.Lerp(y, (float)roadHeight + 0.03f, blend));
                    }

                    var c = new Vector3((float)x, y, (float)z);
                    strip.Add(c - (right * half), c + (right * half), t / (half * 2));
                }
            }

            CreateMesh("Lanes", paved.ToMesh("Lanes"), Mat(Color.white, TextureFactory.Asphalt(), 0.12f), false);
            CreateMesh("GravelLanes", gravel.ToMesh("GravelLanes"), Mat(Color.white, TextureFactory.GravelRoad(), 0.05f), false);
        }

        /// <summary>Black letters, as on the white Austrian place-name and direction signs.</summary>
        private static readonly Color SignInk = Color.black;

        /// <summary>
        /// The writing on the signs (the boards themselves are scenery): the village names at the village entrances
        /// and, on the signposts before each junction, an arrow and where each road leads.
        /// </summary>
        private void BuildVillageSigns()
        {
            var landscape = _terrain.Landscape;
            if (landscape == null) return;
            var font = Resources.Load<Font>("Fonts/Inter-Bold");
            foreach (var place in landscape.Signs)
            {
                var sign = SignRoot($"Sign {place.Name}", place.Item);
                SignText(sign.transform, place.Name, new Vector3(0, 1.75f, 0.047f), 0.045f, font, front: true);
                SignText(sign.transform, place.Name, new Vector3(0, 1.75f, -0.047f), 0.045f, font, front: false); // the back (end of the village)
            }

            foreach (var (item, boards) in landscape.Signposts)
            {
                var post = SignRoot("Signpost", item);
                for (int i = 0; i < boards.Length; i++)
                {
                    var (arrow, name) = boards[i];
                    string line = arrow == "→" ? name + " →" : arrow + " " + name;
                    float size = 0.026f * Mathf.Min(1, 15f / Mathf.Max(1, line.Length));
                    SignText(post.transform, line, new Vector3(0.75f, 2.4f - (i * 0.42f), 0.08f), size, font, front: true);
                }
            }
        }

        private GameObject SignRoot(string name, SceneryItem item)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.SetPositionAndRotation(new Vector3(item.X, item.Y, item.Z), Quaternion.Euler(0, item.RotationDeg, 0));
            return go;
        }

        /// <summary>The font's material, but hidden behind the board like any other surface (the default draws on top).</summary>
        private static Material _signTextMaterial;

        private static void SignText(Transform parent, string value, Vector3 at, float characterSize, Font font, bool front)
        {
            var text = new GameObject("Text");
            text.transform.SetParent(parent, false);
            text.transform.localPosition = at;
            text.transform.localRotation = Quaternion.Euler(0, front ? 180 : 0, 0); // a TextMesh reads from its −Z side
            var mesh = text.AddComponent<TextMesh>();
            mesh.text = value;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = characterSize;
            mesh.fontSize = 64;
            mesh.color = SignInk;
            if (font != null)
            {
                mesh.font = font;
                if (_signTextMaterial == null)
                {
                    // The font's own shader (GUI/Text) draws on top of everything, so the name showed through the
                    // board from behind, mirrored. Sprites/Default tests depth like any surface: letters behind the
                    // board stay hidden. Its colour comes from the vertices (black), the shapes from the atlas' alpha.
                    var depthTested = Shader.Find("Sprites/Default");
                    _signTextMaterial = depthTested != null ? new Material(depthTested) { mainTexture = font.material.mainTexture } : new Material(font.material);
                    _signTextMaterial.renderQueue = 3000;
                    Font.textureRebuilt += rebuilt =>
                    {
                        if (rebuilt == font && _signTextMaterial != null) _signTextMaterial.mainTexture = font.material.mainTexture;
                    };
                }

                text.GetComponent<MeshRenderer>().sharedMaterial = _signTextMaterial;
            }
        }

        private static MeshRenderer AddRenderer(Transform parent, string name, Mesh mesh, Material[] materials, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.Take(mesh.subMeshCount).ToArray();
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return renderer;
        }

        // ------------------------------------------------------------------ far tiles

        /// <summary>Vertex data of a mesh with one submesh per ground cover (can be made off the main thread).</summary>
        public sealed class MeshParts
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int>[] Cover = Enumerable.Range(0, CoverKinds).Select(_ => new List<int>()).ToArray();

            /// <summary>The Unity mesh (main thread only).</summary>
            public Mesh ToMesh(string name) => CoverMesh(name, Vertices, Normals, Uvs, Cover);
        }

        /// <summary>One far tile of the mountain: 32 m and 128 m level of detail.</summary>
        public sealed class FarTileData
        {
            public int Tx, Tz;
            public Rect Area;
            public MeshParts Lod0, Lod1;
        }

        /// <summary>
        /// All far tiles of the mountain (pure maths, in parallel): run in the background while the world loads, so
        /// the main thread only creates the meshes.
        /// </summary>
        public static List<FarTileData> ComputeFarTiles(FreeVelo.Core.World.Gen.Terrain terrain)
        {
            var result = new List<FarTileData>();
            var m = terrain.Mountain;
            if (m == null) return result;
            var homeGrid = Rect.MinMaxRect((float)terrain.MinX, (float)terrain.MinZ, (float)terrain.MaxX, (float)terrain.MaxZ);
            float originX = (float)terrain.MinX, originZ = (float)terrain.MinZ;
            int seed = terrain.Map.Seed + 7;
            int tx0 = Mathf.FloorToInt(((float)m.MinX - originX) / TileSize), tx1 = Mathf.CeilToInt(((float)m.MaxX - originX) / TileSize);
            int tz0 = Mathf.FloorToInt(((float)m.MinZ - originZ) / TileSize), tz1 = Mathf.CeilToInt(((float)m.MaxZ - originZ) / TileSize);
            var tiles = new List<FarTileData>();
            for (int tz = tz0; tz < tz1; tz++)
            {
                for (int tx = tx0; tx < tx1; tx++)
                {
                    var area = new Rect(originX + (tx * TileSize), originZ + (tz * TileSize), TileSize, TileSize);
                    if (homeGrid.Contains(area.min) && homeGrid.Contains(area.max)) continue;
                    tiles.Add(new FarTileData { Tx = tx, Tz = tz, Area = area });
                }
            }

            Parallel.ForEach(tiles, t =>
            {
                t.Lod0 = FarTileParts(terrain, homeGrid, seed, t.Area, 32f);
                t.Lod1 = t.Lod0 != null ? FarTileParts(terrain, homeGrid, seed, t.Area, 128f) : null;
            });
            foreach (var t in tiles)
            {
                if (t.Lod0 != null && t.Lod1 != null) result.Add(t);
            }

            return result;
        }

        /// <summary>Coarse ground of a tile, one submesh per ground cover; chunks inside the home grid are left out.</summary>
        private static MeshParts FarTileParts(FreeVelo.Core.World.Gen.Terrain terrain, Rect homeGrid, int seed, Rect area, float step)
        {
            var m = terrain.Mountain;
            var parts = new MeshParts();
            var vertices = parts.Vertices;
            var normals = parts.Normals;
            var uvs = parts.Uvs;
            var cover = parts.Cover;
            int per = Mathf.RoundToInt(MountainChunk / step);
            for (int cz = 0; cz < ChunksPerTile; cz++)
            {
                for (int cx = 0; cx < ChunksPerTile; cx++)
                {
                    float x0 = area.xMin + (cx * MountainChunk), z0 = area.yMin + (cz * MountainChunk);
                    var chunk = new Rect(x0, z0, MountainChunk, MountainChunk);
                    if (homeGrid.Overlaps(chunk) && homeGrid.Contains(chunk.center)) continue;
                    if (!m.Contains(chunk.center.x, chunk.center.y)) continue;
                    int start = vertices.Count;
                    for (int j = 0; j <= per; j++)
                    {
                        for (int i = 0; i <= per; i++)
                        {
                            float x = x0 + (i * step), z = z0 + (j * step);
                            // Natural ground, kept below the roads next to it (big triangles would cut through them).
                            float h = (float)System.Math.Min(m.Coarse(x, z), terrain.RoadCap(x, z, step) + 1.5); // (−1.5 below)
                            float hx = (float)(m.Coarse(x + 8, z) - m.Coarse(x - 8, z)), hz = (float)(m.Coarse(x, z + 8) - m.Coarse(x, z - 8));
                            vertices.Add(new Vector3(x, h - 1.5f, z));
                            normals.Add(new Vector3(-hx, 16f, -hz).normalized);
                            uvs.Add(new Vector2(x / 18f, z / 18f));
                        }
                    }

                    for (int j = 0; j < per; j++)
                    {
                        for (int i = 0; i < per; i++)
                        {
                            int a = start + (j * (per + 1)) + i, b = a + per + 1;
                            var center = (vertices[a] + vertices[b + 1]) * 0.5f;
                            float slope = Mathf.Sqrt(1f / (normals[a].y * normals[a].y) - 1f);
                            var kind = MountainLook.Ground(m, center.x, center.z, center.y, slope, seed, distant: true, terrain.Landscape);
                            cover[(int)kind].AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                        }
                    }

                    AddSkirt(vertices, normals, uvs, cover[0], start, per + 1, 25f);
                }
            }

            return vertices.Count == 0 ? null : parts;
        }

        /// <summary>Border of a (n × n) vertex block bent down, so neighbours at another level of detail show no gaps.</summary>
        private static void AddSkirt(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles, int start, int n, float drop)
        {
            var border = new List<int>();
            for (int x = 0; x < n; x++) border.Add(start + x);
            for (int z = 1; z < n; z++) border.Add(start + (z * n) + n - 1);
            for (int x = n - 2; x >= 0; x--) border.Add(start + ((n - 1) * n) + x);
            for (int z = n - 2; z > 0; z--) border.Add(start + (z * n));
            border.Add(start);
            int skirt = vertices.Count;
            foreach (int b in border)
            {
                vertices.Add(vertices[b] + (Vector3.down * drop));
                normals.Add(normals[b]);
                uvs.Add(uvs[b]);
            }

            for (int k = 0; k < border.Count - 1; k++)
            {
                int a = border[k], b = border[k + 1], c = skirt + k, d = skirt + k + 1;
                triangles.AddRange(new[] { a, b, c, b, d, c, a, c, b, b, c, d });
            }
        }

        private static Mesh CoverMesh(string name, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int>[] cover)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            // Submesh index = ground cover (empty ones stay, so the materials line up)
            mesh.subMeshCount = cover.Length;
            for (int k = 0; k < cover.Length; k++) mesh.SetTriangles(cover[k], k);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ streaming

        /// <summary>Called every frame: swaps tiles near the camera for detailed pieces and back.</summary>
        private void StreamMountain(Vector3 eye)
        {
            if (!HasMountain) return;

            // Finished background work → meshes (one tile per frame keeps frames smooth)
            foreach (var tile in _tiles.Values)
            {
                if (tile.Building == null || !tile.Building.IsCompleted) continue;
                var task = tile.Building;
                tile.Building = null;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    continue;
                }

                CreateNearTile(tile, task.Result);
                break;
            }

            if ((eye - _lastStreamCheck).sqrMagnitude < 400f) return;
            _lastStreamCheck = eye;
            var p = new Vector2(eye.x, eye.z);
            int running = _tiles.Values.Count(t => t.Building != null);
            foreach (var tile in _tiles.Values.OrderBy(t => Distance(t.Area, p)))
            {
                float d = Distance(tile.Area, p);
                if (d < DetailTileRadius && tile.Near == null && tile.Building == null && running < MaxBuildsAtOnce)
                {
                    var t = tile;
                    var terrain = _terrain;
                    int seed = Map.Seed + 7;
                    t.Building = Task.Run(() => ComputeNearTile(terrain, t.Area, seed));
                    running++;
                }
                else if (d > DropTileRadius && tile.Near != null)
                {
                    DropNearTile(tile);
                }
            }
        }

        private static float Distance(Rect r, Vector2 p)
        {
            float dx = Mathf.Max(0, Mathf.Max(r.xMin - p.x, p.x - r.xMax)), dz = Mathf.Max(0, Mathf.Max(r.yMin - p.y, p.y - r.yMax));
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>Background thread: heights (4 m, with a border for normals), ground cover and scenery of the tile's pieces.</summary>
        private static NearTileData ComputeNearTile(FreeVelo.Core.World.Gen.Terrain terrain, Rect area, int seed)
        {
            var m = terrain.Mountain;
            var data = new NearTileData();
            const int cells = 32;
            const float cell = 4f;
            int n = cells + 3; // one extra row/column on each side for the normals
            for (int cz = 0; cz < ChunksPerTile; cz++)
            {
                for (int cx = 0; cx < ChunksPerTile; cx++)
                {
                    float x0 = area.xMin + (cx * MountainChunk), z0 = area.yMin + (cz * MountainChunk);
                    if (terrain.InGrid(x0 + (MountainChunk / 2), z0 + (MountainChunk / 2))) continue;
                    if (!m.Contains(x0 + (MountainChunk / 2), z0 + (MountainChunk / 2))) continue;
                    var heights = new float[n * n];
                    for (int j = 0; j < n; j++)
                    {
                        for (int i = 0; i < n; i++) heights[(j * n) + i] = (float)terrain.Height(x0 + ((i - 1) * cell), z0 + ((j - 1) * cell));
                    }

                    // Keep the triangles below the roads: a corner next to a road may not be higher than the road
                    // (the 8 m version has bigger triangles, so it reaches further).
                    var coarse = (float[])heights.Clone();
                    for (int j = 0; j < n; j++)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            double x = x0 + ((i - 1) * cell), z = z0 + ((j - 1) * cell);
                            int k = (j * n) + i;
                            heights[k] = (float)System.Math.Min(heights[k], terrain.RoadCap(x, z, cell));
                            coarse[k] = (float)System.Math.Min(coarse[k], terrain.RoadCap(x, z, 2 * cell));
                        }
                    }

                    var cover = new GroundKind[cells * cells];
                    for (int j = 0; j < cells; j++)
                    {
                        for (int i = 0; i < cells; i++)
                        {
                            float h00 = heights[((j + 1) * n) + i + 1], h11 = heights[((j + 2) * n) + i + 2];
                            float h10 = heights[((j + 1) * n) + i + 2], h01 = heights[((j + 2) * n) + i + 1];
                            float slope = Mathf.Sqrt(((h10 - h00) * (h10 - h00)) + ((h01 - h00) * (h01 - h00))) / cell;
                            cover[(j * cells) + i] = MountainLook.Ground(m, x0 + ((i + 0.5f) * cell), z0 + ((j + 0.5f) * cell), (h00 + h11) / 2, slope, seed, distant: false, terrain.Landscape);
                        }
                    }

                    int chunkX = Mathf.RoundToInt(x0 / MountainChunk), chunkZ = Mathf.RoundToInt(z0 / MountainChunk);
                    var items = MountainLook.Items(terrain, Mathf.FloorToInt(x0 / (float)MountainLook.ChunkSize), Mathf.FloorToInt(z0 / (float)MountainLook.ChunkSize), seed);
                    data.Chunks.Add((Mathf.RoundToInt((x0 - area.xMin) / MountainChunk), Mathf.RoundToInt((z0 - area.yMin) / MountainChunk), heights, coarse, n, items, cover));
                }
            }

            return data;
        }

        private void CreateNearTile(MountainTile tile, NearTileData data)
        {
            if (tile.Near != null) return;
            var root = new GameObject($"Mountain detail {tile.Tx},{tile.Tz}");
            root.transform.SetParent(_root.transform, false);
            foreach (var (cx, cz, heights, coarse, n, items, cover) in data.Chunks)
            {
                float x0 = tile.Area.xMin + (cx * MountainChunk), z0 = tile.Area.yMin + (cz * MountainChunk);
                var go = new GameObject($"Ground {cx},{cz}");
                go.transform.SetParent(root.transform, false);
                var near = AddRenderer(go.transform, "LOD0", NearChunkMesh(x0, z0, heights, n, cover, 1), _coverMaterials, false);
                var far = AddRenderer(go.transform, "LOD1", NearChunkMesh(x0, z0, coarse, n, cover, 2), _coverMaterials, false);
                var group = go.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0.18f, new Renderer[] { near }), new LOD(0.0005f, new Renderer[] { far }) });
                group.RecalculateBounds();

                if (items.Count > 0)
                {
                    var chunk = new SceneryChunkData { Center = new Vector3(x0 + (MountainChunk / 2), items[0].Y, z0 + (MountainChunk / 2)) };
                    chunk.Items.AddRange(items);
                    chunk.FarRenderer = BuildSceneryChunkMesh(root.transform, chunk.Items);
                    tile.Scenery.Add(chunk);
                    _chunks.Add(chunk);
                }
            }

            tile.Near = root;
            if (tile.Far != null) tile.Far.gameObject.SetActive(false);
            _lastInstanceUpdate = new Vector3(float.MaxValue, 0, 0); // let the instancing pick up the new trees
        }

        private void DropNearTile(MountainTile tile)
        {
            foreach (var chunk in tile.Scenery)
            {
                _chunks.Remove(chunk);
                if (chunk.FarRenderer != null) Destroy(chunk.FarRenderer.GetComponent<MeshFilter>().sharedMesh);
            }

            tile.Scenery.Clear();
            foreach (var filter in tile.Near.GetComponentsInChildren<MeshFilter>()) Destroy(filter.sharedMesh);
            Destroy(tile.Near);
            tile.Near = null;
            if (tile.Far != null) tile.Far.gameObject.SetActive(true);
            _lastInstanceUpdate = new Vector3(float.MaxValue, 0, 0);
        }

        /// <summary>One 128 m piece at 4 m (step 1) or 8 m (step 2), submeshes by ground cover, with a skirt.</summary>
        private static Mesh NearChunkMesh(float x0, float z0, float[] heights, int n, GroundKind[] cover, int step)
        {
            const int cells = 32;
            const float cell = 4f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>[CoverKinds];
            for (int k = 0; k < tris.Length; k++) tris[k] = new List<int>();
            int per = cells / step;
            for (int j = 0; j <= per; j++)
            {
                for (int i = 0; i <= per; i++)
                {
                    int gi = (i * step) + 1, gj = (j * step) + 1;
                    float h = heights[(gj * n) + gi];
                    float dx = heights[(gj * n) + gi + 1] - heights[(gj * n) + gi - 1], dz = heights[((gj + 1) * n) + gi] - heights[((gj - 1) * n) + gi];
                    float x = x0 + (i * step * cell), z = z0 + (j * step * cell);
                    vertices.Add(new Vector3(x, h, z));
                    normals.Add(new Vector3(-dx, 2f * cell, -dz).normalized);
                    uvs.Add(new Vector2(x / 18f, z / 18f));
                }
            }

            for (int j = 0; j < per; j++)
            {
                for (int i = 0; i < per; i++)
                {
                    int a = (j * (per + 1)) + i, b = a + per + 1;
                    var kind = cover[(j * step * cells) + (i * step)];
                    tris[(int)kind].AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            }

            AddSkirt(vertices, normals, uvs, tris[(int)GroundKind.Grass], 0, per + 1, 4f);
            return CoverMesh("MountainNear", vertices, normals, uvs, tris);
        }

        // ------------------------------------------------------------------ summit signs

        /// <summary>A name sign at the top of each climb, beside the road (real name and summit altitude).</summary>
        private void BuildSummitSigns()
        {
            var mountain = Map.Mountain;
            if (mountain == null) return;
            var font = Resources.Load<Font>("Fonts/Inter-Bold");
            var wood = Mat(new Color(0.36f, 0.25f, 0.16f), null, 0.1f);
            var board = Mat(new Color(0.12f, 0.3f, 0.62f), null, 0.3f);
            foreach (var climb in mountain.Climbs)
            {
                var e = Map.Edges[climb.Edge];
                int last = e.X.Length - 1;
                var top = new Vector3((float)e.X[last], (float)e.E[last], (float)e.Z[last]);
                var right = Right(e, last - 2);
                var forward = new Vector3((float)(e.X[last] - e.X[last - 6]), 0, (float)(e.Z[last] - e.Z[last - 6])).normalized;
                var spot = top - (forward * 14f) + (right * ((float)e.Width / 2 + 3.2f));
                spot.y = GroundHeight(spot.x, spot.z);
                var face = Quaternion.LookRotation(-forward, Vector3.up); // readable when arriving

                var sign = new GameObject($"Summit {climb.Data.Id}");
                sign.transform.SetParent(_root.transform, false);
                sign.transform.SetPositionAndRotation(spot, face);
                for (int s = -1; s <= 1; s += 2)
                {
                    var post = Primitive(PrimitiveType.Cube, sign.transform, new Vector3(s * 1.5f, 1.4f, 0), new Vector3(0.14f, 2.8f, 0.14f), wood);
                    post.name = "Post";
                }

                Primitive(PrimitiveType.Cube, sign.transform, new Vector3(0, 2.35f, 0), new Vector3(3.6f, 1.3f, 0.08f), board).name = "Board";
                string name = climb.Data.Id == ClimbData.Stelvio ? "PASSO DELLO STELVIO" : climb.Data.Id == ClimbData.AlpeDHuez ? "ALPE D'HUEZ" : climb.Data.Name.ToUpperInvariant();
                int altitude = Mathf.RoundToInt((float)climb.Data.E[climb.Data.E.Length - 1]);
                // Both faces: towards the arriving rider (+Z of the sign) and towards the other climb
                foreach (float side in new[] { -0.05f, 0.05f })
                {
                    var text = new GameObject("Text");
                    text.transform.SetParent(sign.transform, false);
                    text.transform.localPosition = new Vector3(0, 2.38f, side);
                    // A TextMesh reads from its −Z side: the face at +Z is turned round.
                    text.transform.localRotation = side > 0 ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
                    var mesh = text.AddComponent<TextMesh>();
                    mesh.text = $"{name}\n{altitude} m";
                    mesh.anchor = TextAnchor.MiddleCenter;
                    mesh.alignment = TextAlignment.Center;
                    mesh.characterSize = 0.05f;
                    mesh.fontSize = 64;
                    mesh.color = Color.white;
                    if (font != null)
                    {
                        mesh.font = font;
                        text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                    }

                }
            }
        }
    }
}
