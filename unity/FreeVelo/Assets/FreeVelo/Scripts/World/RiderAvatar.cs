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
            var model = LoadModel(style, includeBike: custom == null);
            avatar.BuildMeshes(world, model);
            avatar.Animate(0, 0, 0);
            return avatar;
        }

        private static AvatarModel LoadModel(AvatarStyle style, bool includeBike)
        {
            var baked = Resources.Load<TextAsset>("FreeVeloAvatar");
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

            // Fallback: sculpt at runtime (takes a few seconds).
            return AvatarModel.Build(style, includeBike, detail: 0.6f);
        }

        /// <summary>Turns wheels and cranks and moves the legs. Call every frame.</summary>
        public void Animate(float speedMps, float cadenceRpm, float dt)
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

            AvatarModel.Pose(_crankAngle, _wheelAngle, _pose);
            foreach (var pair in _nodes)
            {
                if (!_pose.TryGetValue(pair.Key, out var pose)) continue;
                var q = pose.Rotation.ToQuaternion();
                pair.Value.localPosition = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
                pair.Value.localRotation = new Quaternion(q.X, q.Y, q.Z, q.W);
            }
        }

        private void BuildMeshes(WorldBuilder world, AvatarModel model)
        {
            var materials = new Dictionary<string, Material>();
            foreach (var part in model.Parts)
            {
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

                var go = new GameObject(part.Material.Id);
                go.transform.SetParent(node, false);
                go.AddComponent<MeshFilter>().sharedMesh = ToMesh(part.Mesh, part.Node + "_" + part.Material.Id);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
            }
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
