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

        private float _lastStreamTime;
        private bool _streamWorkLeft = true;

        private sealed class MountainTile
        {
            public int Tx;
            public int Tz;
            public Rect Area;
            public LODGroup Far;
            public GameObject Near;
            public Task<NearTileData> Building;
            public readonly List<SceneryChunkData> Scenery = new List<SceneryChunkData>();

            // The far tile's finest level (32 m, textured) is only made near the camera (FineTileRadius); until then the
            // 64 m level stands in for it.
            public bool HasFine;
            public Task<MeshArrays> FineBuilding;
            public Renderer R1, R2;

            /// <summary>The merged blocks this tile belongs to (null if none); while one is on, the tile itself is off.</summary>
            public FarBlock Block, Mid;
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
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var farTiles = far ?? ComputeFarTiles(_terrain);
            var farRoot = new GameObject("Mountain");
            farRoot.transform.SetParent(_root.transform, false);
            foreach (var data in farTiles)
            {
                var tile = new MountainTile { Tx = data.Tx, Tz = data.Tz, Area = data.Area };
                var go = new GameObject($"Mountain {data.Tx},{data.Tz}");
                go.transform.SetParent(farRoot.transform, false);
                // Textured, one piece per cover, only up to about 2 km (relative height 0.25 of a 512 m tile); beyond,
                // one piece with the covers' average colours: one draw call instead of eight (the phone drew up to
                // 4,800 for these tiles alone: at 0.06 the textured level reached about 7 km).
                var r1 = AddRenderer(go.transform, "LOD1", data.Lod1.ToMesh("MountainFar"), new[] { FarCoverMaterial() }, false);
                var r2 = AddRenderer(go.transform, "LOD2", data.Lod2.ToMesh("MountainFar"), new[] { FarCoverMaterial() }, false);
                tile.Far = go.AddComponent<LODGroup>();
                tile.R1 = r1;
                tile.R2 = r2;
                if (data.Lod0 != null) AddFineLevel(tile, data.Lod0);
                else tile.Far.SetLODs(new[] { new LOD(0.07f, new Renderer[] { r1 }), new LOD(0.0004f, new Renderer[] { r2 }) });
                tile.Far.RecalculateBounds();
                _tiles[(data.Tx, data.Tz)] = tile;
            }

            BuildFarBlocks(farRoot.transform);
            long tilesMs = clock.ElapsedMilliseconds;
            clock.Restart();
            BuildSummitSigns();
            BuildVillageSigns();
            long signsMs = clock.ElapsedMilliseconds;
            clock.Restart();
            BuildLanes();
            long lanesMs = clock.ElapsedMilliseconds;
            clock.Restart();
            BuildSurfaces();
            MountainTimes = $"tiles {tilesMs}, signs {signsMs}, lanes {lanesMs}, surfaces {clock.ElapsedMilliseconds}";
        }

        /// <summary>Where BuildMountain's time went (for the start-up line in the log).</summary>
        public string MountainTimes { get; private set; } = "";

        /// <summary>
        /// Far blocks: the coarsest level (128 m) of 4 × 4 far tiles (about 2 km) merged into one mesh, drawn instead of
        /// the tiles while all of them are beyond <see cref="FarBlockRadius"/> – where each tile would show that level
        /// anyway, so nothing changes on screen. The render breakdown showed about 450 tile draws beyond 4 km.
        /// </summary>
        private sealed class FarBlock
        {
            public Rect Area;
            public MeshRenderer Renderer;
            public readonly List<MountainTile> Tiles = new List<MountainTile>();
            public bool Active;
        }

        private const int TilesPerBlock = 4;

        /// <summary>
        /// Beyond this (to the block's nearest edge) every tile shows its 128 m level: a 512 m tile switches to it below a
        /// relative height of 0.07, about 6.3 km at a 60° view (68° at speed), so 7.5 km leaves room.
        /// </summary>
        private const float FarBlockRadius = 7500f;

        private readonly List<FarBlock> _farBlocks = new List<FarBlock>();

        /// <summary>
        /// Middle blocks: the 64 m level of 2 × 2 far tiles merged, drawn instead of the tiles while all of them are past
        /// the distance where a tile leaves its textured 32 m level (relative height 0.25 of a 512 m tile: about 1.8 km
        /// at a 60° view, times QualitySettings.lodBias, plus a margin). Between there and the far blocks each tile
        /// would show that level or the coarser 128 m one, so nothing is lost; one draw instead of four (the render
        /// breakdown at the start showed 169 single tile draws beyond 4 km, OnePlus benchmark 25 Nov 2026).
        /// </summary>
        private readonly List<FarBlock> _midBlocks = new List<FarBlock>();

        private const int TilesPerMidBlock = 2;
        private const float MidBlockRadiusAtBias1 = 1950f;

        private void BuildFarBlocks(Transform parent)
        {
            _farBlocks.Clear();
            _midBlocks.Clear();
            BuildBlocks(parent, TilesPerBlock, t => t.R2, "Mountain block", _farBlocks, (t, b) => t.Block = b);
            BuildBlocks(parent, TilesPerMidBlock, t => t.R1, "Mountain mid block", _midBlocks, (t, b) => t.Mid = b);
        }

        private void BuildBlocks(Transform parent, int tilesPerBlock, System.Func<MountainTile, Renderer> level, string name, List<FarBlock> into, System.Action<MountainTile, FarBlock> link)
        {
            var groups = new Dictionary<(int, int), List<MountainTile>>();
            foreach (var tile in _tiles.Values)
            {
                if (level(tile) == null) continue;
                var key = (Mathf.FloorToInt(tile.Tx / (float)tilesPerBlock), Mathf.FloorToInt(tile.Tz / (float)tilesPerBlock));
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<MountainTile>();
                list.Add(tile);
            }

            foreach (var list in groups.Values)
            {
                if (list.Count < 2) continue;
                var combine = new CombineInstance[list.Count];
                var area = list[0].Area;
                for (int i = 0; i < list.Count; i++)
                {
                    var r = level(list[i]);
                    combine[i] = new CombineInstance { mesh = r.GetComponent<MeshFilter>().sharedMesh, transform = r.transform.localToWorldMatrix };
                    area = Rect.MinMaxRect(Mathf.Min(area.xMin, list[i].Area.xMin), Mathf.Min(area.yMin, list[i].Area.yMin), Mathf.Max(area.xMax, list[i].Area.xMax), Mathf.Max(area.yMax, list[i].Area.yMax));
                }

                var mesh = new Mesh { name = "MountainBlock", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(combine, mergeSubMeshes: true, useMatrices: true);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = FarCoverMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.enabled = false;
                var block = new FarBlock { Area = area, Renderer = renderer };
                block.Tiles.AddRange(list);
                foreach (var tile in list) link(tile, block);
                into.Add(block);
            }
        }

        /// <summary>Far and middle blocks on where the camera is far enough, their tiles off (and back).</summary>
        private void UpdateFarBlocks(Vector2 p)
        {
            foreach (var block in _farBlocks) block.Active = Distance(block.Area, p) > FarBlockRadius;
            float midRadius = MidBlockRadiusAtBias1 * Mathf.Max(1f, QualitySettings.lodBias);
            foreach (var mid in _midBlocks)
            {
                bool farOn = false;
                foreach (var tile in mid.Tiles) farOn |= tile.Block != null && tile.Block.Active;
                mid.Active = !farOn && Distance(mid.Area, p) > midRadius;
            }

            foreach (var block in _farBlocks)
            {
                if (block.Renderer.enabled != block.Active) block.Renderer.enabled = block.Active;
            }

            foreach (var mid in _midBlocks)
            {
                if (mid.Renderer.enabled != mid.Active) mid.Renderer.enabled = mid.Active;
            }

            foreach (var tile in _tiles.Values) ShowFarTile(tile);
        }

        /// <summary>A far tile is drawn unless its detailed tile or one of its merged blocks stands in for it.</summary>
        private static void ShowFarTile(MountainTile tile)
        {
            if (tile.Far == null) return;
            bool show = tile.Near == null && !(tile.Block != null && tile.Block.Active) && !(tile.Mid != null && tile.Mid.Active);
            if (tile.Far.gameObject.activeSelf != show) tile.Far.gameObject.SetActive(show);
        }

        /// <summary>The far tile's textured 32 m level (one piece per cover), shown up to about 2 km.</summary>
        private void AddFineLevel(MountainTile tile, MeshArrays lod0)
        {
            var r0 = AddRenderer(tile.Far.transform, "LOD0", lod0.ToMesh("MountainFar"), _coverMaterials, false);
            tile.Far.SetLODs(new[] { new LOD(0.25f, new Renderer[] { r0 }), new LOD(0.07f, new Renderer[] { tile.R1 }), new LOD(0.0004f, new Renderer[] { tile.R2 }) });
            tile.HasFine = true;
        }

        /// <summary>
        /// Far tiles get their textured 32 m level only within this distance (it shows up to about 2 km). Computing it for
        /// every tile at start was most of the far tiles' work (about 2 s of the start on a phone); the rest follow in the
        /// background as the rider comes near.
        /// </summary>
        public const float FineTileRadius = 2600f;

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
            var lanes = landscape.Lanes;
            var terrain = _terrain;
            // The heights every 2 m along each lane, on all cores (a road search per point).
            var heights = new float[lanes.Count][];
            System.Threading.Tasks.Parallel.For(0, lanes.Count, n =>
            {
                var (ax, az, bx, bz, _) = lanes[n];
                float length = Mathf.Sqrt((float)(((bx - ax) * (bx - ax)) + ((bz - az) * (bz - az))));
                if (length < 1) return;
                double fx = (bx - ax) / length, fz = (bz - az) / length;
                int steps = Mathf.CeilToInt(length / 2f);
                var h = new float[steps + 1];
                for (int k = 0; k <= steps; k++)
                {
                    float t = length * k / steps;
                    double x = ax + (fx * t), z = az + (fz * t);
                    float y = (float)terrain.SurfaceHeight(x, z) + 0.08f;
                    double d = terrain.DistanceToRoad(x, z, 12, out var road, out double roadHeight, out _);
                    if (road != null)
                    {
                        // Up onto the road where the lane joins it, then down to the meadow over a couple of metres.
                        float over = (float)(d - (road.Width / 2));
                        float blend = Mathf.Clamp01(1 - (over / 3.5f));
                        y = Mathf.Max(y, Mathf.Lerp(y, (float)roadHeight + 0.03f, blend));
                    }

                    h[k] = y;
                }

                heights[n] = h;
            });

            for (int n = 0; n < lanes.Count; n++)
            {
                var (ax, az, bx, bz, isGravel) = lanes[n];
                if (heights[n] == null) continue;
                float length = Mathf.Sqrt((float)(((bx - ax) * (bx - ax)) + ((bz - az) * (bz - az))));
                var along = new Vector3((float)(bx - ax), 0, (float)(bz - az)) / length;
                var right = new Vector3(along.z, 0, -along.x);
                float half = isGravel ? 1.5f : 1.7f;
                var strip = isGravel ? gravel : paved;
                strip.Begin();
                int steps = heights[n].Length - 1;
                for (int k = 0; k <= steps; k++)
                {
                    float t = length * k / steps;
                    double x = ax + (along.x * t), z = az + (along.z * t);
                    var c = new Vector3((float)x, heights[n][k], (float)z);
                    strip.Add(c - (right * half), c + (right * half), t / (half * 2));
                }
            }

            CreateMesh("Lanes", paved.ToMesh("Lanes"), Mat(Color.white, TextureFactory.Asphalt(), 0.12f), false);
            CreateMesh("GravelLanes", gravel.ToMesh("GravelLanes"), Mat(Color.white, TextureFactory.GravelRoad(), 0.05f), false);
        }

        /// <summary>
        /// Gravel yards, forecourts, beer-garden floors and stone paths (Landscape.Surfaces): each rectangle draped over the
        /// ground exactly as drawn (Terrain.SurfaceHeight) on a 1 m grid, 4 cm above it, rising onto the road where it
        /// meets one (as the lanes do). One mesh per material for the whole world.
        /// </summary>
        private void BuildSurfaces()
        {
            var landscape = _terrain.Landscape;
            if (landscape == null || landscape.Surfaces.Count == 0) return;
            var byMaterial = new Dictionary<WorldMaterial, (List<Vector3> V, List<Vector2> UV, List<int> T)>();

            // The heights of every 1 m grid point (ground as drawn, raised onto a road next to it): on all cores, they
            // were 0.6 s of the start on a PC (a road search per point), several times that on a phone.
            var surfaces = landscape.Surfaces;
            var heights = new float[surfaces.Count][];
            var terrain = _terrain;
            System.Threading.Tasks.Parallel.For(0, surfaces.Count, k =>
            {
                var s = surfaces[k];
                int nu = Mathf.Max(1, Mathf.CeilToInt((float)(s.HalfLength * 2))), nv = Mathf.Max(1, Mathf.CeilToInt((float)(s.HalfWidth * 2)));
                var h = new float[(nu + 1) * (nv + 1)];
                for (int j = 0; j <= nv; j++)
                {
                    for (int i = 0; i <= nu; i++)
                    {
                        double u = -s.HalfLength + (s.HalfLength * 2 * i / nu), v = -s.HalfWidth + (s.HalfWidth * 2 * j / nv);
                        var (x, z) = s.Point(u, v);
                        float y = (float)terrain.SurfaceHeight(x, z) + 0.04f;
                        double d = terrain.DistanceToRoad(x, z, 8, out var road, out double roadHeight, out _);
                        if (road != null)
                        {
                            float over = (float)(d - (road.Width / 2));
                            float blend = Mathf.Clamp01(1 - (over / 2.5f));
                            y = Mathf.Max(y, Mathf.Lerp(y, (float)roadHeight + 0.02f, blend));
                        }

                        h[(j * (nu + 1)) + i] = y;
                    }
                }

                heights[k] = h;
            });

            for (int k = 0; k < surfaces.Count; k++)
            {
                var s = surfaces[k];
                if (!byMaterial.TryGetValue(s.Crop, out var m)) byMaterial[s.Crop] = m = (new List<Vector3>(), new List<Vector2>(), new List<int>());
                float tile = SurfaceTextures.TileM(s.Crop);
                int nu = Mathf.Max(1, Mathf.CeilToInt((float)(s.HalfLength * 2))), nv = Mathf.Max(1, Mathf.CeilToInt((float)(s.HalfWidth * 2)));
                int start = m.V.Count;
                for (int j = 0; j <= nv; j++)
                {
                    for (int i = 0; i <= nu; i++)
                    {
                        double u = -s.HalfLength + (s.HalfLength * 2 * i / nu), v = -s.HalfWidth + (s.HalfWidth * 2 * j / nv);
                        var (x, z) = s.Point(u, v);
                        m.V.Add(new Vector3((float)x, heights[k][(j * (nu + 1)) + i], (float)z));
                        m.UV.Add(SurfaceUV(s.Crop, (float)x, (float)z, tile));
                    }
                }

                for (int j = 0; j < nv; j++)
                {
                    for (int i = 0; i < nu; i++)
                    {
                        int a = start + (j * (nu + 1)) + i, b = a + nu + 1;
                        // Up-facing whatever the patch's orientation (as the road fans do).
                        var n = Vector3.Cross(m.V[b] - m.V[a], m.V[a + 1] - m.V[a]);
                        if (n.y > 0) m.T.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                        else m.T.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                    }
                }
            }

            foreach (var pair in byMaterial)
            {
                var mesh = new Mesh { name = "Surfaces " + pair.Key, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(pair.Value.V);
                mesh.SetUVs(0, pair.Value.UV);
                mesh.SetTriangles(pair.Value.T, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                CreateMesh("Surfaces " + pair.Key, mesh, Mat(pair.Key), false);
            }
        }

        /// <summary>
        /// Texture coordinates of a ground surface: world metres / repeat. On the earth yard they are bent a little by
        /// low-frequency noise, so the repeats do not line up in straight rows (the scan's large blotches, which showed
        /// the grid most, are filtered out of the file itself: docs/THIRD_PARTY_ASSETS.md). Turning world-sized UVs
        /// stretched the scan into streaks; this only shifts them, by up to about a third of a repeat.
        /// </summary>
        private static Vector2 SurfaceUV(WorldMaterial material, float x, float z, float tile)
        {
            var uv = new Vector2(x, z) / tile;
            if (material != WorldMaterial.YardEarth) return uv;
            float n1 = Mathf.PerlinNoise((x * 0.11f) + 17.3f, (z * 0.11f) + 4.1f) - 0.5f;
            float n2 = Mathf.PerlinNoise((x * 0.11f) + 51.7f, (z * 0.11f) + 33.9f) - 0.5f;
            return uv + new Vector2(0.6f * n1, 0.6f * n2);
        }

        /// <summary>Black letters, as on the white Austrian place-name and direction signs.</summary>
        private static readonly Color SignInk = Color.black;

        /// <summary>
        /// The writing on the signs (the boards themselves are scenery): the village names at the village entrances.
        /// (The junction signposts were taken out at the owner's request: they did not look good.)
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

            /// <summary>Ready arrays with one submesh per cover (any thread).</summary>
            public MeshArrays Covered() => new MeshArrays(Vertices.ToArray(), Normals.ToArray(), Uvs.ToArray(), Cover.Select(c => c.ToArray()).ToArray());

            /// <summary>
            /// Ready arrays with one submesh for all covers: every triangle gets its own corners whose texture coordinate
            /// points at its cover's texel in the palette of <see cref="FarCoverMaterial"/> (any thread).
            /// </summary>
            public MeshArrays Palette()
            {
                int count = Cover.Sum(c => c.Count);
                var v = new Vector3[count];
                var n = new Vector3[count];
                var uv = new Vector2[count];
                var t = new int[count];
                int at = 0;
                for (int k = 0; k < Cover.Length; k++)
                {
                    var texel = new Vector2((k + 0.5f) / CoverKinds, 0.5f);
                    foreach (int i in Cover[k])
                    {
                        t[at] = at;
                        v[at] = Vertices[i];
                        n[at] = Normals[i];
                        uv[at] = texel;
                        at++;
                    }
                }

                return new MeshArrays(v, n, uv, new[] { t });
            }
        }

        /// <summary>
        /// Mesh data prepared off the main thread (with its bounds), so the main thread only hands it to the GPU: building
        /// the far tiles' meshes on the main thread took 2.8 s of the start on a phone.
        /// </summary>
        public sealed class MeshArrays
        {
            private readonly Vector3[] _v, _n;
            private readonly Vector2[] _uv;
            private readonly int[][] _sub;
            private readonly Bounds _bounds;

            public MeshArrays(Vector3[] v, Vector3[] n, Vector2[] uv, int[][] sub)
            {
                _v = v;
                _n = n;
                _uv = uv;
                _sub = sub;
                if (v.Length == 0) return;
                Vector3 lo = v[0], hi = v[0];
                foreach (var p in v)
                {
                    lo = Vector3.Min(lo, p);
                    hi = Vector3.Max(hi, p);
                }

                _bounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            }

            /// <summary>The Unity mesh (main thread only).</summary>
            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.subMeshCount = _sub.Length;
                for (int k = 0; k < _sub.Length; k++) mesh.SetIndices(_sub[k], MeshTopology.Triangles, k, false);
                mesh.bounds = _bounds;
                return mesh;
            }
        }

        private Material _farCover;

        /// <summary>One material for the coarse ground: a palette of the covers' average colours (texture × tint).</summary>
        private Material FarCoverMaterial()
        {
            if (_farCover != null) return _farCover;
            var palette = new Texture2D(CoverKinds, 1, TextureFormat.RGBA32, false) { name = "CoverPalette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int k = 0; k < CoverKinds; k++)
            {
                var m = _coverMaterials[Mathf.Min(k, _coverMaterials.Length - 1)];
                var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                if (m.mainTexture is Texture2D tex && TextureFactory.Means.TryGetValue(tex, out var mean)) c *= mean;
                c.a = 1f;
                palette.SetPixel(k, 0, c);
            }

            palette.Apply(false, true);
            _farCover = Mat(Color.white, palette, 0f);
            return _farCover;
        }

        /// <summary>One far tile of the mountain: 32 m and 128 m level of detail.</summary>
        public sealed class FarTileData
        {
            public int Tx, Tz;
            public Rect Area;
            public MeshArrays Lod0, Lod1, Lod2;
        }

        /// <summary>
        /// All far tiles of the mountain (pure maths, in parallel): run in the background while the world loads, so
        /// the main thread only creates the meshes.
        /// </summary>
        public static List<FarTileData> ComputeFarTiles(FreeVelo.Core.World.Gen.Terrain terrain) => ComputeFarTiles(terrain, null);

        /// <param name="fineAround">With a start point: the textured 32 m level only for tiles within <see cref="FineTileRadius"/> of it.</param>
        public static List<FarTileData> ComputeFarTiles(FreeVelo.Core.World.Gen.Terrain terrain, Vector2? fineAround)
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
                // The mountain's height and slope sampled once on the finest grid; the coarser levels take every 2nd and
                // 4th point of it (they were sampled anew for every level: the far tiles took 4.8 s at start on a phone).
                var samples = new TileSamples(terrain.Mountain, t.Area, 32f);
                bool fine = !fineAround.HasValue || Distance(t.Area, fineAround.Value) < FineTileRadius;
                if (fine) t.Lod0 = FarTileParts(terrain, homeGrid, seed, t.Area, 32f, samples)?.Covered();
                t.Lod1 = FarTileParts(terrain, homeGrid, seed, t.Area, 64f, samples)?.Palette();
                t.Lod2 = FarTileParts(terrain, homeGrid, seed, t.Area, 128f, samples)?.Palette();
            });
            foreach (var t in tiles)
            {
                if (t.Lod1 != null && t.Lod2 != null) result.Add(t);
            }

            return result;
        }

        /// <summary>The mountain's coarse height and its slope (±8 m) on a tile's finest grid, computed once.</summary>
        private sealed class TileSamples
        {
            private readonly float[] _h, _hx, _hz;
            private readonly int _n;
            private readonly float _x0, _z0, _step;

            public TileSamples(FreeVelo.Core.World.Gen.MountainTerrain m, Rect area, float step)
            {
                _x0 = area.xMin;
                _z0 = area.yMin;
                _step = step;
                _n = Mathf.RoundToInt(area.width / step) + 1;
                _h = new float[_n * _n];
                _hx = new float[_n * _n];
                _hz = new float[_n * _n];
                for (int j = 0; j < _n; j++)
                {
                    for (int i = 0; i < _n; i++)
                    {
                        float x = _x0 + (i * step), z = _z0 + (j * step);
                        int k = (j * _n) + i;
                        _h[k] = (float)m.Coarse(x, z);
                        _hx[k] = (float)(m.Coarse(x + 8, z) - m.Coarse(x - 8, z));
                        _hz[k] = (float)(m.Coarse(x, z + 8) - m.Coarse(x, z - 8));
                    }
                }
            }

            /// <summary>Height and slope differences at a grid point of this tile (any multiple of the step).</summary>
            public (float H, float Hx, float Hz) At(float x, float z)
            {
                int i = Mathf.RoundToInt((x - _x0) / _step), j = Mathf.RoundToInt((z - _z0) / _step);
                int k = (j * _n) + i;
                return (_h[k], _hx[k], _hz[k]);
            }
        }

        /// <summary>Coarse ground of a tile, one submesh per ground cover; chunks inside the home grid are left out.</summary>
        private static MeshParts FarTileParts(FreeVelo.Core.World.Gen.Terrain terrain, Rect homeGrid, int seed, Rect area, float step, TileSamples samples)
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
                            var (coarse, hx, hz) = samples.At(x, z);
                            float h = (float)System.Math.Min(coarse, terrain.RoadCap(x, z, step) + 1.5); // (−1.5 below)
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

            // Finished fine far levels → meshes (one per frame)
            foreach (var tile in _tiles.Values)
            {
                if (tile.FineBuilding == null || !tile.FineBuilding.IsCompleted) continue;
                var task = tile.FineBuilding;
                tile.FineBuilding = null;
                if (task.IsFaulted) Debug.LogException(task.Exception);
                else if (task.Result != null && tile.Far != null) AddFineLevel(tile, task.Result);
                else tile.HasFine = true; // nothing to show at that level
                break;
            }

            // Every 20 m of camera travel, and every half second while work is queued (after a jump many tiles want it).
            bool moved = (eye - _lastStreamCheck).sqrMagnitude >= 400f;
            if (!moved && (Time.realtimeSinceStartup - _lastStreamTime < 0.5f || !_streamWorkLeft)) return;
            _lastStreamCheck = eye;
            _lastStreamTime = Time.realtimeSinceStartup;
            var p = new Vector2(eye.x, eye.z);
            UpdateFarBlocks(p);
            int running = _tiles.Values.Count(t => t.Building != null || t.FineBuilding != null);
            _streamWorkLeft = _tiles.Values.Any(t => (!t.HasFine && t.Far != null && Distance(t.Area, p) < FineTileRadius) || (t.Near == null && Distance(t.Area, p) < DetailTileRadius));
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

                if (d < FineTileRadius && !tile.HasFine && tile.FineBuilding == null && tile.Far != null && running < MaxBuildsAtOnce)
                {
                    var t = tile;
                    var terrain = _terrain;
                    var homeGrid = _homeGrid;
                    int seed = Map.Seed + 7;
                    t.FineBuilding = Task.Run(() => FarTileParts(terrain, homeGrid, seed, t.Area, 32f, new TileSamples(terrain.Mountain, t.Area, 32f))?.Covered());
                    running++;
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
            ShowFarTile(tile);
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
            ShowFarTile(tile);
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
