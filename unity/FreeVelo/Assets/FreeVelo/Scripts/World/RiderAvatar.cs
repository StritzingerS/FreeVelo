using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FreeVelo.World
{
    /// <summary>
    /// Rider on a bike. Uses a locally installed bicycle model (Resources/FreeVeloBicycle, see menu
    /// FreeVelo → Use selected prefab as bicycle) or a placeholder built from primitives.
    /// Wheels and cranks turn with speed and cadence.
    /// </summary>
    public sealed class RiderAvatar : MonoBehaviour
    {
        private const float WheelDiameter = 0.68f;
        private const string CustomBicycle = "FreeVeloBicycle";

        private Transform _frontWheel;
        private Transform _rearWheel;
        private Transform _crank;

        // Parts of a custom model, rotated around their own centre.
        private readonly List<Renderer> _customWheels = new List<Renderer>();
        private Renderer _customCrank;
        private float _customWheelDiameter = WheelDiameter;

        public static RiderAvatar Create(WorldBuilder world, Color jersey)
        {
            var root = new GameObject("Rider");
            var avatar = root.AddComponent<RiderAvatar>();
            var black = world.Mat(new Color(0.08f, 0.08f, 0.08f));
            var shirt = world.Mat(jersey);
            var skin = world.Mat(new Color(0.95f, 0.78f, 0.64f));

            var custom = Resources.Load<GameObject>(CustomBicycle);
            if (custom != null)
            {
                var bike = Instantiate(custom, root.transform, false);
                foreach (var c in bike.GetComponentsInChildren<Collider>()) Destroy(c);
                var renderers = bike.GetComponentsInChildren<Renderer>();
                avatar._customWheels.AddRange(renderers.Where(r => r.name.ToLowerInvariant().Contains("wheel")).Take(2));
                avatar._customCrank = renderers.FirstOrDefault(r => r.name.ToLowerInvariant().Contains("crank"));
                if (avatar._customWheels.Count > 0) avatar._customWheelDiameter = Mathf.Max(0.3f, avatar._customWheels[0].bounds.size.y);

                float height = renderers.Length > 0 ? renderers.Max(r => r.bounds.max.y) - root.transform.position.y : 1f;
                AddRider(root.transform, Mathf.Clamp(height, 0.8f, 1.3f), shirt, skin, black);
                return avatar;
            }

            var frame = world.Mat(new Color(0.85f, 0.1f, 0.1f));

            avatar._rearWheel = Wheel(world, root.transform, new Vector3(0, WheelDiameter / 2, -0.5f), black);
            avatar._frontWheel = Wheel(world, root.transform, new Vector3(0, WheelDiameter / 2, 0.52f), black);

            WorldBuilder.Primitive(PrimitiveType.Cube, root.transform, new Vector3(0, 0.62f, 0), new Vector3(0.05f, 0.05f, 1.0f), frame);
            var down = WorldBuilder.Primitive(PrimitiveType.Cube, root.transform, new Vector3(0, 0.5f, 0.15f), new Vector3(0.05f, 0.05f, 0.7f), frame);
            down.transform.localRotation = Quaternion.Euler(35, 0, 0);
            WorldBuilder.Primitive(PrimitiveType.Cube, root.transform, new Vector3(0, 0.95f, 0.5f), new Vector3(0.45f, 0.04f, 0.04f), black); // handlebar
            WorldBuilder.Primitive(PrimitiveType.Cube, root.transform, new Vector3(0, 0.98f, -0.25f), new Vector3(0.12f, 0.04f, 0.25f), black); // saddle

            var crank = new GameObject("Crank").transform;
            crank.SetParent(root.transform, false);
            crank.localPosition = new Vector3(0, 0.35f, 0.02f);
            WorldBuilder.Primitive(PrimitiveType.Cube, crank, new Vector3(0.12f, 0, 0), new Vector3(0.03f, 0.34f, 0.03f), black);
            WorldBuilder.Primitive(PrimitiveType.Cube, crank, new Vector3(-0.12f, 0, 0), new Vector3(0.03f, 0.34f, 0.03f), black).transform.localRotation = Quaternion.Euler(180, 0, 0);
            avatar._crank = crank;

            AddRider(root.transform, 1f, shirt, skin, black);
            return avatar;
        }

        /// <summary>Simple rider figure; <paramref name="bikeHeight"/> scales it to the bike (placeholder bike = 1).</summary>
        private static void AddRider(Transform root, float bikeHeight, Material shirt, Material skin, Material black)
        {
            float k = bikeHeight;
            var body = WorldBuilder.Primitive(PrimitiveType.Capsule, root, new Vector3(0, 1.22f * k, 0.05f), new Vector3(0.38f, 0.38f, 0.32f), shirt);
            body.transform.localRotation = Quaternion.Euler(60, 0, 0);
            WorldBuilder.Primitive(PrimitiveType.Sphere, root, new Vector3(0, 1.48f * k, 0.36f), Vector3.one * 0.24f, skin);
            WorldBuilder.Primitive(PrimitiveType.Capsule, root, new Vector3(0.14f, 0.72f * k, -0.08f), new Vector3(0.14f, 0.32f * k, 0.14f), black);
            WorldBuilder.Primitive(PrimitiveType.Capsule, root, new Vector3(-0.14f, 0.72f * k, -0.08f), new Vector3(0.14f, 0.32f * k, 0.14f), black);
        }

        public void Animate(float speedMps, float cadenceRpm, float dt)
        {
            float crankDegrees = cadenceRpm / 60f * 360f * dt;
            if (_frontWheel == null)
            {
                // Custom model: rotate parts around their own centre and the bike's sideways axis.
                float degrees = speedMps / (Mathf.PI * _customWheelDiameter) * 360f * dt;
                foreach (var wheel in _customWheels) wheel.transform.RotateAround(wheel.bounds.center, transform.right, degrees);
                if (_customCrank != null) _customCrank.transform.RotateAround(_customCrank.bounds.center, transform.right, crankDegrees);
                return;
            }

            float wheelDegrees = speedMps / (Mathf.PI * WheelDiameter) * 360f * dt;
            _frontWheel.Rotate(Vector3.right, wheelDegrees, Space.Self);
            _rearWheel.Rotate(Vector3.right, wheelDegrees, Space.Self);
            _crank.Rotate(Vector3.right, crankDegrees, Space.Self);
        }

        private static Transform Wheel(WorldBuilder world, Transform parent, Vector3 position, Material material)
        {
            var pivot = new GameObject("Wheel").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = position;
            var tyre = WorldBuilder.Primitive(PrimitiveType.Cylinder, pivot, Vector3.zero, new Vector3(WheelDiameter, 0.015f, WheelDiameter), material);
            tyre.transform.localRotation = Quaternion.Euler(0, 0, 90);
            WorldBuilder.Primitive(PrimitiveType.Cube, pivot, Vector3.zero, new Vector3(0.02f, WheelDiameter * 0.95f, 0.02f), material);
            return pivot;
        }
    }
}
