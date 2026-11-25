using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Avatar;
using UnityEngine;
using Num = System.Numerics;

namespace FreeVelo.World
{
    /// <summary>
    /// Rider on a road bike. The geometry comes from FreeVelo.Core (<see cref="AvatarModel"/>, sculpted and baked
    /// into Resources/FreeVeloAvatar.bytes by tools/FreeVelo.ModelPreview --bake); this class only turns it into
    /// Unity meshes and applies <see cref="AvatarModel.Pose"/> every frame (cranks, wheels, legs).
    /// A locally installed bicycle model (Resources/FreeVeloBicycle) replaces the generated bike if present.
    /// Coordinates: x = right, y = up, z = forward; origin on the ground below the bottom bracket.
    /// </summary>
    public sealed class RiderAvatar : MonoBehaviour
    {
        private readonly Dictionary<AvatarNode, Transform> _nodes = new Dictionary<AvatarNode, Transform>();
        private readonly Dictionary<AvatarNode, NodePose> _pose = new Dictionary<AvatarNode, NodePose>();
        private float _crankAngle;
        private float _wheelAngle;
        private float _stopped = 1f; // start standing (menus)

        // Custom bicycle model (local Asset Store model)
        private readonly List<Renderer> _customWheels = new List<Renderer>();
        private Renderer _customCrank;
        private float _customWheelDiameter = AvatarModel.WheelRadius * 2;

        public static RiderAvatar Create(WorldBuilder world, Color jersey)
        {
            var root = new GameObject("Rider");
            var avatar = root.AddComponent<RiderAvatar>();
            var custom = Resources.Load<GameObject>("FreeVeloBicycle");
            if (custom != null) avatar.UseCustomBike(custom);

            var style = new AvatarStyle { Jersey = new Num.Vector3(jersey.r, jersey.g, jersey.b) };
            var materials = new Dictionary<string, Material>();
            var high = LoadModel("FreeVeloAvatar", style, includeBike: custom == null) ?? AvatarModel.Build(style, custom == null, detail: 0.6f);

            // The skinned rider (if baked) replaces the generated one; the generated bike stays.
            avatar._skin = RiderSkin.Create(root.transform, jersey, world);
            bool bikeOnly = avatar._skin != null;
            var highRenderers = avatar.BuildMeshes(world, high, materials, "LOD0", bikeOnly);
            if (bikeOnly) highRenderers.AddRange(avatar._skin.Lod0);

            // Low-detail version for riders further away (if baked).
            var low = LoadModel("FreeVeloAvatarLow", style, includeBike: custom == null);
            if (low != null)
            {
                var lowRenderers = avatar.BuildMeshes(world, low, materials, "LOD1", bikeOnly);
                if (bikeOnly) lowRenderers.AddRange(avatar._skin.Lod1.Count > 0 ? avatar._skin.Lod1 : avatar._skin.Lod0);
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(0.3f, highRenderers.ToArray()),
                    new LOD(0.004f, lowRenderers.ToArray()),
                });
                group.RecalculateBounds();
            }

            avatar.Animate(0, 0, 0);
            return avatar;
        }

        /// <summary>Loads a baked avatar from Resources (null if missing or unreadable).</summary>
        private static AvatarModel LoadModel(string resource, AvatarStyle style, bool includeBike)
        {
            var baked = Resources.Load<TextAsset>(resource);
            if (baked != null)
            {
                try
                {
                    return AvatarModel.Deserialize(baked.bytes, style, includeBike);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"FreeVelo: baked avatar unreadable ({e.Message}), generating it.");
                }
            }

            // Caller falls back to sculpting at runtime (takes a few seconds).
            return null;
        }

        /// <summary>Extra lean (degrees, positive = right) of the whole avatar while standing with a foot down.</summary>
        public float StopLeanDeg => AvatarModel.StopLeanDeg(_stopped);

        /// <summary>Turns wheels and cranks and moves the legs. Call every frame.</summary>
        /// <param name="curvature">Of the road just ahead (1/m, positive = right-hand bend): the head looks into it.</param>
        /// <param name="gradePercent">Of the road here: steep climbs make the rider stand.</param>
        /// <param name="intensity">Power relative to FTP (1 = threshold): at about twice FTP the rider sprints.</param>
        public void Animate(float speedMps, float cadenceRpm, float dt, float curvature = 0f, float gradePercent = 0f, float intensity = 0f)
        {
            using (FreeVelo.App.PerfTimes.Measure("riders")) AnimateNow(speedMps, cadenceRpm, dt, curvature, gradePercent, intensity);
        }

        private void AnimateNow(float speedMps, float cadenceRpm, float dt, float curvature, float gradePercent, float intensity)
        {
            float wheelDegrees = speedMps / (2f * Mathf.PI * AvatarModel.WheelRadius) * 360f * dt;
            _wheelAngle = Mathf.Repeat(_wheelAngle + wheelDegrees, 360f);
            _crankAngle = Mathf.Repeat(_crankAngle + (cadenceRpm / 60f * 360f * dt), 360f);

            if (_customWheels.Count > 0)
            {
                float degrees = speedMps / (Mathf.PI * _customWheelDiameter) * 360f * dt;
                foreach (var wheel in _customWheels) wheel.transform.RotateAround(wheel.bounds.center, transform.right, degrees);
                if (_customCrank != null) _customCrank.transform.RotateAround(_customCrank.bounds.center, transform.right, cadenceRpm / 60f * 360f * dt);
            }

            // Standing: below ~1 km/h the rider unclips the left foot and puts it down (about 1.2 s), and clips back
            // in as soon as the bike rolls again.
            float stopTarget = speedMps < 0.3f ? 1f : 0f;
            _stopped = Mathf.MoveTowards(_stopped, stopTarget, dt / 1.2f);
            if (_stopped >= 1f) _crankAngle = AvatarModel.ParkedCrankDegrees; // push off from "2 o'clock"
            if (ShotCrank.HasValue)
            {
                _stopped = 0f;
                _crankAngle = ShotCrank.Value;
            }

            AvatarModel.Pose(_crankAngle, _wheelAngle, _pose, _stopped);

            // Out of the saddle (only with the skinned rider): the bike rocks under the rider with the pedal stroke,
            // away from the pushing leg, about the line where the tyres touch the road.
            UpdateStance(cadenceRpm, gradePercent, intensity, dt);
            float rockDeg = _skin == null ? 0f : _stand * Mathf.Lerp(6f, 9f, _sprint) * Mathf.Sin(_crankAngle * Mathf.Deg2Rad);
            var rock = Quaternion.AngleAxis(rockDeg, Vector3.forward);
            foreach (var pair in _nodes)
            {
                if (!_pose.TryGetValue(pair.Key, out var pose)) continue;
                var q = pose.Rotation.ToQuaternion();
                pair.Value.localPosition = rock * new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
                pair.Value.localRotation = rock * new Quaternion(q.X, q.Y, q.Z, q.W);
            }

            if (_skin != null)
            {
                // Body motion fades in with pedalling (about 0.3 s) and out when coasting or stopping.
                float pedalling = cadenceRpm > 5f || ShotCrank.HasValue ? 1f : 0f;
                _pedalling = dt > 0 ? Mathf.Lerp(_pedalling, pedalling, 1f - Mathf.Exp(-dt / 0.3f)) : pedalling;
                // Gaze to a point about 25 m ahead on the bend: half the heading change over that distance.
                float look = Mathf.Clamp(curvature * 12.5f * Mathf.Rad2Deg, -30f, 30f);
                _look = dt > 0 ? Mathf.Lerp(_look, look, 1f - Mathf.Exp(-dt / 0.5f)) : look;
                if (ShotLook.HasValue) _look = ShotLook.Value;
                _skin.Apply(_pose, _stopped, _crankAngle, _pedalling, _look, _stand, _sprint, rock);
            }
        }

        private RiderSkin _skin;
        private float _pedalling, _look;

        // Out of the saddle: what the rider wants (0 seated, 1 climbing, 2 sprinting), since when, and the blends.
        private int _stance;
        private float _stanceTime, _stand, _sprint;

        /// <summary>
        /// Seated, standing on a climb, or sprinting, from the road and the effort, with hysteresis so the rider
        /// does not bob up and down: climbing out of the saddle from 7 % at 30–75 rpm and at least 80 % of FTP
        /// (back down below 5.5 %, above 82 rpm or below 60 % of FTP); sprinting from twice FTP at 60 rpm or more
        /// (until under 1.5 × FTP). A new stance holds for at least 1.5 s; the blend takes about 0.4 s.
        /// </summary>
        private void UpdateStance(float cadenceRpm, float gradePercent, float intensity, float dt)
        {
            int want;
            bool pedalling = cadenceRpm > 5f;
            if (!pedalling || _stopped > 0f) want = 0;
            else if ((intensity >= 2f && cadenceRpm >= 60f) || (_stance == 2 && intensity >= 1.5f)) want = 2;
            else if (_stance == 1)
                want = gradePercent >= 5.5f && cadenceRpm <= 82f && intensity >= 0.6f ? 1 : 0;
            else
                want = gradePercent >= 7f && cadenceRpm >= 30f && cadenceRpm <= 75f && intensity >= 0.8f ? 1 : 0;

            _stanceTime += dt;
            if (want != _stance && (_stanceTime >= 1.5f || !pedalling || _stopped > 0f))
            {
                _stance = want;
                _stanceTime = 0f;
            }

            int stance = ShotStance ?? _stance;
            float k = dt > 0 ? 1f - Mathf.Exp(-dt / 0.4f) : 1f;
            _stand = ShotStance.HasValue ? (stance > 0 ? 1f : 0f) : Mathf.Lerp(_stand, stance > 0 ? 1f : 0f, k);
            _sprint = ShotStance.HasValue ? (stance == 2 ? 1f : 0f) : Mathf.Lerp(_sprint, stance == 2 ? 1f : 0f, k);
        }

        /// <summary>Render-shot tooling: 0 seated, 1 standing, 2 sprinting for every rider.</summary>
        public static int? ShotStance;

        /// <summary>Render-shot tooling: head turn (degrees, positive = right) for every rider.</summary>
        public static float? ShotLook;

        /// <summary>Render-shot tooling: hold every rider riding at this crank angle.</summary>
        public static float? ShotCrank;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ShotCrank = null;
            ShotLook = null;
            ShotStance = null;
            _paletteMaterial = null;
            _paletteAlbedo = _paletteMetal = null;
            _paletteTried = false;
            PaletteSlots.Clear();
        }

        /// <summary>Nodes of the bike (kept when the skinned rider replaces the generated one).</summary>
        private static bool IsBikeNode(AvatarNode node) =>
            node == AvatarNode.Bike || node == AvatarNode.FrontWheel || node == AvatarNode.RearWheel ||
            node == AvatarNode.Crankset || node == AvatarNode.PedalRight || node == AvatarNode.PedalLeft;

        /// <summary>
        /// One mesh per moving part (frame, each wheel, cranks, each pedal) instead of one per part and material: every
        /// material's colour, metallic and smoothness go into one texel of a shared palette (the bike drew 32
        /// renderers, each also in the shadow pass; now 6). Same look: the parts have no textures, only those values,
        /// and the palette is sampled at texel centres without filtering. Falls back to one renderer per part and
        /// material if the palette template is missing or full.
        /// </summary>
        private List<Renderer> BuildMeshes(WorldBuilder world, AvatarModel model, Dictionary<string, Material> materials, string lodName, bool bikeOnly)
        {
            var merged = BuildMergedMeshes(model, lodName, bikeOnly);
            return merged ?? BuildPartMeshes(world, model, materials, lodName, bikeOnly);
        }

        private List<Renderer> BuildMergedMeshes(AvatarModel model, string lodName, bool bikeOnly)
        {
            var material = PaletteMaterial();
            if (material == null) return null;
            var byNode = new Dictionary<AvatarNode, (List<Vector3> V, List<Vector3> N, List<Vector2> Uv, List<int> T)>();
            foreach (var part in model.Parts)
            {
                if (bikeOnly && !IsBikeNode(part.Node)) continue;
                int slot = PaletteSlot(part.Material);
                if (slot < 0) return null;
                if (!byNode.TryGetValue(part.Node, out var b)) byNode[part.Node] = b = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>());
                var normals = part.Mesh.ComputeNormals();
                int offset = b.V.Count;
                var uv = new Vector2((slot + 0.5f) / PaletteSize, 0.5f);
                for (int i = 0; i < part.Mesh.Vertices.Count; i++)
                {
                    var v = part.Mesh.Vertices[i];
                    var n = normals[i];
                    b.V.Add(new Vector3(v.X, v.Y, v.Z));
                    b.N.Add(new Vector3(n.X, n.Y, n.Z));
                    b.Uv.Add(uv);
                }

                foreach (int t in part.Mesh.Triangles) b.T.Add(t + offset);
            }

            var renderers = new List<Renderer>();
            foreach (var pair in byNode)
            {
                if (!_nodes.TryGetValue(pair.Key, out var node))
                {
                    node = new GameObject(pair.Key.ToString()).transform;
                    node.SetParent(transform, false);
                    _nodes[pair.Key] = node;
                }

                var b = pair.Value;
                var mesh = new Mesh { name = pair.Key + "_" + lodName };
                if (b.V.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(b.V);
                mesh.SetNormals(b.N);
                mesh.SetUVs(0, b.Uv);
                mesh.SetTriangles(b.T, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(lodName);
                go.transform.SetParent(node, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderers.Add(renderer);
            }

            return renderers;
        }

        private const int PaletteSize = 128;
        private static Texture2D _paletteAlbedo, _paletteMetal;
        private static Material _paletteMaterial;
        private static bool _paletteTried;
        private static readonly Dictionary<(float, float, float, float, float), int> PaletteSlots = new Dictionary<(float, float, float, float, float), int>();

        /// <summary>
        /// Lit with the palette as albedo (sRGB, like a material colour) and as metallic (R) / smoothness (A) map;
        /// template Resources/FreeVeloLitPalette.mat keeps the _METALLICSPECGLOSSMAP variant in builds.
        /// </summary>
        private static Material PaletteMaterial()
        {
            if (_paletteMaterial != null || _paletteTried) return _paletteMaterial;
            _paletteTried = true;
            var template = Resources.Load<Material>("FreeVeloLitPalette");
            if (template == null) return null;
            _paletteAlbedo = new Texture2D(PaletteSize, 1, TextureFormat.RGBA32, false, false) { name = "BikePalette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _paletteMetal = new Texture2D(PaletteSize, 1, TextureFormat.RGBA32, false, true) { name = "BikePaletteMetal", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _paletteMaterial = new Material(template) { name = "BikePalette", enableInstancing = true };
            _paletteMaterial.SetTexture("_BaseMap", _paletteAlbedo);
            _paletteMaterial.SetColor("_BaseColor", Color.white);
            _paletteMaterial.SetTexture("_MetallicGlossMap", _paletteMetal);
            _paletteMaterial.SetFloat("_Smoothness", 1f); // scales the map's alpha
            _paletteMaterial.EnableKeyword("_METALLICSPECGLOSSMAP");
            return _paletteMaterial;
        }

        private static int PaletteSlot(AvatarMaterial m)
        {
            var key = (m.Color.X, m.Color.Y, m.Color.Z, m.Smoothness, m.Metallic);
            if (PaletteSlots.TryGetValue(key, out int slot)) return slot;
            if (PaletteSlots.Count >= PaletteSize) return -1;
            slot = PaletteSlots.Count;
            PaletteSlots[key] = slot;
            _paletteAlbedo.SetPixel(slot, 0, new Color(m.Color.X, m.Color.Y, m.Color.Z, 1f));
            _paletteMetal.SetPixel(slot, 0, new Color(m.Metallic, 0f, 0f, m.Smoothness));
            _paletteAlbedo.Apply(false);
            _paletteMetal.Apply(false);
            return slot;
        }

        private List<Renderer> BuildPartMeshes(WorldBuilder world, AvatarModel model, Dictionary<string, Material> materials, string lodName, bool bikeOnly)
        {
            var renderers = new List<Renderer>();
            foreach (var part in model.Parts)
            {
                if (bikeOnly && !IsBikeNode(part.Node)) continue;
                if (!_nodes.TryGetValue(part.Node, out var node))
                {
                    node = new GameObject(part.Node.ToString()).transform;
                    node.SetParent(transform, false);
                    _nodes[part.Node] = node;
                }

                if (!materials.TryGetValue(part.Material.Id, out var material))
                {
                    var c = part.Material.Color;
                    material = world.Mat(new Color(c.X, c.Y, c.Z), null, part.Material.Smoothness);
                    if (part.Material.Metallic > 0 && material.HasProperty("_Metallic"))
                    {
                        material = new Material(material);
                        material.SetFloat("_Metallic", part.Material.Metallic);
                    }

                    materials[part.Material.Id] = material;
                }

                var go = new GameObject(lodName + "_" + part.Material.Id);
                go.transform.SetParent(node, false);
                go.AddComponent<MeshFilter>().sharedMesh = ToMesh(part.Mesh, part.Node + "_" + part.Material.Id);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderers.Add(renderer);
            }

            return renderers;
        }

        private static Mesh ToMesh(MeshData data, string name)
        {
            var normals = data.ComputeNormals();
            var vertices = new Vector3[data.Vertices.Count];
            var unityNormals = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = data.Vertices[i];
                var n = normals[i];
                vertices[i] = new Vector3(v.X, v.Y, v.Z);
                unityNormals[i] = new Vector3(n.X, n.Y, n.Z);
            }

            var mesh = new Mesh { name = name };
            if (vertices.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = unityNormals;
            mesh.triangles = data.Triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void UseCustomBike(GameObject prefab)
        {
            var bike = Instantiate(prefab, transform, false);
            foreach (var c in bike.GetComponentsInChildren<Collider>()) Destroy(c);
            var renderers = bike.GetComponentsInChildren<Renderer>();
            _customWheels.AddRange(renderers.Where(r => r.name.ToLowerInvariant().Contains("wheel")).Take(2));
            _customCrank = renderers.FirstOrDefault(r => r.name.ToLowerInvariant().Contains("crank"));
            if (_customWheels.Count > 0) _customWheelDiameter = Mathf.Max(0.3f, _customWheels[0].bounds.size.y);
        }
    }
}
