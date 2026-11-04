using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FreeVelo.World
{
    /// <summary>
    /// Rider on a road bike, generated in code (no assets): frame with road geometry, deep-section wheels,
    /// drop bars, drivetrain; rider whose feet stay on the pedals (two-bone leg IK).
    /// A locally installed bicycle model (Resources/FreeVeloBicycle) replaces the generated bike if present.
    /// Coordinates: x = right, y = up, z = forward; origin on the ground below the bottom bracket.
    /// </summary>
    public sealed class RiderAvatar : MonoBehaviour
    {
        // Bike geometry (metres) – roughly a size 56 road bike with 700×25c tyres.
        private const float CrankLength = 0.17f;
        private const float WheelRadius = 0.336f;
        private static readonly Vector3 BottomBracket = new Vector3(0, 0.27f, 0);
        private static readonly Vector3 RearAxle = new Vector3(0, WheelRadius, -0.405f);
        private static readonly Vector3 FrontAxle = new Vector3(0, WheelRadius, 0.565f);
        private static readonly Vector3 SeatCluster = new Vector3(0, 0.80f, -0.16f);
        private static readonly Vector3 HeadTop = new Vector3(0, 0.83f, 0.39f);
        private static readonly Vector3 HeadBottom = new Vector3(0, 0.68f, 0.435f);
        private const float PedalOffsetX = 0.125f;

        // Rider (≈ 1.78 m)
        private const float ThighLength = 0.46f;
        private const float ShinLength = 0.45f;
        private static readonly Vector3 Hip = new Vector3(0.085f, 1.02f, -0.17f);
        private static readonly Vector3 Shoulder = new Vector3(0.175f, 1.40f, 0.25f);
        private static readonly Vector3 Hand = new Vector3(0.205f, 0.91f, 0.555f);
        private static readonly Vector3 AnkleAbovePedal = new Vector3(0, 0.07f, -0.045f);
        private static readonly Vector3 ShoeAbovePedal = new Vector3(0, 0.035f, 0.03f);

        private Transform _frontWheel;
        private Transform _rearWheel;
        private Transform _crank;
        private float _crankAngle;
        private readonly Transform[] _pedals = new Transform[2];
        private readonly Transform[] _thighs = new Transform[2];
        private readonly Transform[] _shins = new Transform[2];
        private readonly Transform[] _shoes = new Transform[2];

        // Custom bicycle model (local Asset Store model)
        private readonly List<Renderer> _customWheels = new List<Renderer>();
        private Renderer _customCrank;
        private float _customWheelDiameter = WheelRadius * 2;

        public static RiderAvatar Create(WorldBuilder world, Color jersey)
        {
            var root = new GameObject("Rider");
            var avatar = root.AddComponent<RiderAvatar>();
            var palette = new Palette(world, jersey);

            var custom = Resources.Load<GameObject>("FreeVeloBicycle");
            if (custom != null)
            {
                avatar.UseCustomBike(custom);
            }
            else
            {
                avatar.BuildBike(palette);
            }

            avatar.BuildRider(palette);
            avatar.Animate(0, 0, 0);
            return avatar;
        }

        /// <summary>Turns wheels and cranks and moves the legs. Call every frame.</summary>
        public void Animate(float speedMps, float cadenceRpm, float dt)
        {
            float wheelDegrees = speedMps / (2f * Mathf.PI * WheelRadius) * 360f * dt;
            _crankAngle = Mathf.Repeat(_crankAngle + (cadenceRpm / 60f * 360f * dt), 360f);

            if (_frontWheel != null)
            {
                _frontWheel.Rotate(Vector3.right, wheelDegrees, Space.Self);
                _rearWheel.Rotate(Vector3.right, wheelDegrees, Space.Self);
            }
            else
            {
                float degrees = speedMps / (Mathf.PI * _customWheelDiameter) * 360f * dt;
                foreach (var wheel in _customWheels) wheel.transform.RotateAround(wheel.bounds.center, transform.right, degrees);
                if (_customCrank != null) _customCrank.transform.RotateAround(_customCrank.bounds.center, transform.right, cadenceRpm / 60f * 360f * dt);
            }

            if (_crank != null) _crank.localRotation = Quaternion.AngleAxis(_crankAngle, Vector3.right);

            for (int side = 0; side < 2; side++)
            {
                // Right crank at the crank angle, left crank opposite (180°).
                float angle = (_crankAngle + (side == 0 ? 0f : 180f)) * Mathf.Deg2Rad;
                float sign = side == 0 ? 1f : -1f;
                var pedal = BottomBracket + new Vector3(sign * PedalOffsetX, CrankLength * Mathf.Cos(angle), CrankLength * Mathf.Sin(angle));
                if (_pedals[side] != null) _pedals[side].localPosition = pedal;

                var hip = new Vector3(sign * Hip.x, Hip.y, Hip.z);
                var ankle = pedal + new Vector3(-sign * 0.02f, AnkleAbovePedal.y, AnkleAbovePedal.z);
                var knee = SolveJoint(hip, ankle, ThighLength, ShinLength, new Vector3(sign * 0.1f, 0, 1f));
                Place(_thighs[side], hip, knee);
                Place(_shins[side], knee, ankle);
                _shoes[side].localPosition = pedal + new Vector3(-sign * 0.02f, ShoeAbovePedal.y, ShoeAbovePedal.z);
            }
        }

        // ------------------------------------------------------------------ bike

        private void BuildBike(Palette p)
        {
            // Frame
            var frame = p.Builder(p.Frame);
            frame.Tube(HeadTop + new Vector3(0, -0.02f, -0.005f), SeatCluster + new Vector3(0, -0.01f, 0), 0.016f, 0.014f);   // top tube
            frame.Tube(HeadBottom + new Vector3(0, 0.02f, -0.005f), BottomBracket + new Vector3(0, 0.02f, 0.02f), 0.024f, 0.022f); // down tube
            frame.Tube(BottomBracket, SeatCluster, 0.017f, 0.015f);                                                            // seat tube
            frame.Tube(HeadBottom, HeadTop + new Vector3(0, 0.02f, -0.005f), 0.022f, 0.02f);                                  // head tube
            for (int s = -1; s <= 1; s += 2)
            {
                frame.Tube(new Vector3(s * 0.025f, 0.78f, -0.165f), new Vector3(s * 0.06f, RearAxle.y, RearAxle.z), 0.009f, 0.007f); // seat stay
                frame.Tube(new Vector3(s * 0.03f, BottomBracket.y, -0.03f), new Vector3(s * 0.065f, RearAxle.y, RearAxle.z), 0.012f, 0.008f); // chain stay
                frame.Tube(new Vector3(s * 0.03f, 0.67f, 0.44f), new Vector3(s * 0.05f, FrontAxle.y, FrontAxle.z), 0.014f, 0.008f); // fork blade
            }

            frame.Box(new Vector3(0, 0.672f, 0.44f), new Vector3(0.09f, 0.03f, 0.05f), Quaternion.Euler(-17, 0, 0)); // fork crown
            var bbShell = p.Builder(p.Frame);
            bbShell.Tube(BottomBracket + new Vector3(-0.035f, 0, 0), BottomBracket + new Vector3(0.035f, 0, 0), 0.022f, 0.022f, 12);

            // Seatpost, saddle, stem, bars, hoods
            var black = p.Builder(p.Black);
            black.Tube(SeatCluster, new Vector3(0, 0.953f, -0.206f), 0.0135f, 0.0135f);
            black.Ellipsoid(new Vector3(0, 0.975f, -0.19f), new Vector3(0.06f, 0.02f, 0.135f), Quaternion.identity, 12, 6);
            black.Ellipsoid(new Vector3(0, 0.972f, -0.255f), new Vector3(0.07f, 0.02f, 0.06f), Quaternion.identity, 12, 6);
            black.Tube(HeadTop + new Vector3(0, 0.02f, -0.005f), new Vector3(0, 0.87f, 0.385f), 0.017f, 0.017f); // steerer spacers
            black.Tube(new Vector3(0, 0.86f, 0.38f), new Vector3(0, 0.875f, 0.48f), 0.016f, 0.015f);           // stem
            black.Tube(new Vector3(-0.2f, 0.875f, 0.48f), new Vector3(0.2f, 0.875f, 0.48f), 0.012f, 0.012f);  // bar tops
            for (int s = -1; s <= 1; s += 2)
            {
                black.Path(new[]
                {
                    new Vector3(s * 0.2f, 0.875f, 0.48f),
                    new Vector3(s * 0.205f, 0.88f, 0.545f),
                    new Vector3(s * 0.21f, 0.85f, 0.60f),
                    new Vector3(s * 0.21f, 0.78f, 0.605f),
                    new Vector3(s * 0.21f, 0.73f, 0.57f),
                    new Vector3(s * 0.21f, 0.725f, 0.50f),
                }, 0.012f);
                black.Ellipsoid(new Vector3(s * 0.205f, 0.895f, 0.565f), new Vector3(0.018f, 0.032f, 0.042f), Quaternion.Euler(20, 0, 0), 8, 6);
                black.Tube(new Vector3(s * 0.205f, 0.885f, 0.59f), new Vector3(s * 0.2f, 0.79f, 0.615f), 0.006f, 0.005f, 6); // brake lever
            }

            // Bottle on the down tube
            var downDir = (BottomBracket - HeadBottom).normalized;
            var bottleNormal = Vector3.Cross(downDir, Vector3.right).normalized;
            if (bottleNormal.y < 0) bottleNormal = -bottleNormal;
            var b0 = Vector3.Lerp(HeadBottom, BottomBracket, 0.3f) + (bottleNormal * 0.05f);
            var b1 = Vector3.Lerp(HeadBottom, BottomBracket, 0.62f) + (bottleNormal * 0.05f);
            p.Builder(p.Bottle).Tube(b0, b1, 0.034f, 0.034f, 12);
            p.Builder(p.Black).Tube(b0, b0 + ((b0 - b1).normalized * 0.025f), 0.015f, 0.012f, 8);

            // Chain and rear derailleur
            var chain = p.Builder(p.Metal);
            chain.Tube(BottomBracket + new Vector3(0.07f, 0.095f, 0), RearAxle + new Vector3(0.07f, 0.045f, 0), 0.004f, 0.004f, 5);
            chain.Tube(BottomBracket + new Vector3(0.07f, -0.095f, 0), RearAxle + new Vector3(0.07f, -0.06f, 0.02f), 0.004f, 0.004f, 5);
            chain.Box(RearAxle + new Vector3(0.075f, -0.07f, 0.01f), new Vector3(0.015f, 0.08f, 0.03f), Quaternion.identity);

            p.Flush(transform, "Bike");

            _rearWheel = BuildWheel(p, RearAxle, cassette: true);
            _frontWheel = BuildWheel(p, FrontAxle, cassette: false);

            // Cranks: right arm points up at angle 0, left arm opposite.
            _crank = new GameObject("Crankset").transform;
            _crank.SetParent(transform, false);
            _crank.localPosition = BottomBracket;
            var crank = p.Builder(p.Metal);
            crank.Tube(new Vector3(-0.09f, 0, 0), new Vector3(0.09f, 0, 0), 0.012f, 0.012f);
            crank.Box(new Vector3(0.088f, CrankLength / 2, 0), new Vector3(0.016f, CrankLength + 0.03f, 0.026f), Quaternion.identity);
            crank.Box(new Vector3(-0.088f, -CrankLength / 2, 0), new Vector3(0.016f, CrankLength + 0.03f, 0.026f), Quaternion.identity);
            crank.Torus(new Vector3(0.07f, 0, 0), Vector3.right, 0.1f, 0.008f, 0.003f, 40, 6);  // big ring
            crank.Torus(new Vector3(0.064f, 0, 0), Vector3.right, 0.075f, 0.007f, 0.003f, 32, 6); // small ring
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f;
                crank.Tube(new Vector3(0.068f, 0, 0), new Vector3(0.068f, Mathf.Cos(a) * 0.095f, Mathf.Sin(a) * 0.095f), 0.009f, 0.006f, 6); // spider
            }

            p.Flush(_crank, "Crankset");

            for (int side = 0; side < 2; side++)
            {
                var pedal = new GameObject(side == 0 ? "PedalRight" : "PedalLeft").transform;
                pedal.SetParent(transform, false);
                var pb = p.Builder(p.Black);
                pb.Box(Vector3.zero, new Vector3(0.075f, 0.016f, 0.085f), Quaternion.identity);
                pb.Tube(new Vector3(side == 0 ? -0.04f : 0.04f, 0, 0), Vector3.zero, 0.006f, 0.006f, 6);
                p.Flush(pedal, "Pedal");
                _pedals[side] = pedal;
            }
        }

        private Transform BuildWheel(Palette p, Vector3 axle, bool cassette)
        {
            var wheel = new GameObject(cassette ? "RearWheel" : "FrontWheel").transform;
            wheel.SetParent(transform, false);
            wheel.localPosition = axle;

            p.Builder(p.Black).Torus(Vector3.zero, Vector3.right, WheelRadius - 0.014f, 0.014f, 0.013f, 40, 8); // tyre
            p.Builder(p.Rim).Torus(Vector3.zero, Vector3.right, 0.293f, 0.025f, 0.011f, 40, 8);                  // deep rim
            var metal = p.Builder(p.Metal);
            metal.Tube(new Vector3(-0.05f, 0, 0), new Vector3(0.05f, 0, 0), 0.018f, 0.018f, 10);                 // hub
            for (int k = 0; k < 20; k++)
            {
                float a = k * Mathf.PI * 2f / 20;
                float side = k % 2 == 0 ? 1f : -1f;
                var hub = new Vector3(side * 0.03f, Mathf.Cos(a) * 0.025f, Mathf.Sin(a) * 0.025f);
                var rim = new Vector3(side * 0.006f, Mathf.Cos(a) * 0.27f, Mathf.Sin(a) * 0.27f);
                metal.Tube(hub, rim, 0.0012f, 0.0012f, 4, false);
            }

            if (cassette)
            {
                for (int i = 0; i < 4; i++) metal.Torus(new Vector3(0.045f + (i * 0.006f), 0, 0), Vector3.right, 0.028f + (i * 0.008f), 0.006f, 0.002f, 24, 5);
            }

            p.Flush(wheel, "Wheel");
            return wheel;
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

        // ------------------------------------------------------------------ rider

        private void BuildRider(Palette p)
        {
            var body = p.Builder(p.Jersey);
            var pelvis = new Vector3(0, 1.05f, -0.13f);
            var neck = new Vector3(0, 1.43f, 0.26f);
            var torsoDir = neck - pelvis;
            body.Ellipsoid((pelvis + neck) / 2f, new Vector3(0.165f, (torsoDir.magnitude / 2f) + 0.05f, 0.11f), Quaternion.FromToRotation(Vector3.up, torsoDir), 14, 10);

            var shorts = p.Builder(p.Shorts);
            shorts.Ellipsoid(new Vector3(0, 1.03f, -0.16f), new Vector3(0.155f, 0.1f, 0.13f), Quaternion.identity, 14, 8);

            var skin = p.Builder(p.Skin);
            skin.Tube(neck, new Vector3(0, 1.5f, 0.33f), 0.05f, 0.045f, 10);
            skin.Ellipsoid(new Vector3(0, 1.55f, 0.37f), new Vector3(0.085f, 0.105f, 0.1f), Quaternion.Euler(20, 0, 0), 14, 10);

            p.Builder(p.Helmet).Ellipsoid(new Vector3(0, 1.575f, 0.355f), new Vector3(0.105f, 0.1f, 0.14f), Quaternion.Euler(-12, 0, 0), 14, 6, upperHalf: true);
            p.Builder(p.Black).Box(new Vector3(0, 1.565f, 0.455f), new Vector3(0.15f, 0.035f, 0.02f), Quaternion.Euler(-10, 0, 0)); // sunglasses

            for (int s = -1; s <= 1; s += 2)
            {
                var shoulder = new Vector3(s * Shoulder.x, Shoulder.y, Shoulder.z);
                var hand = new Vector3(s * Hand.x, Hand.y, Hand.z);
                var elbow = SolveJoint(shoulder, hand, 0.30f, 0.31f, new Vector3(s * 0.6f, -1f, 0f));
                body.Limb(shoulder, elbow, 0.055f, 0.045f);   // sleeve
                skin.Limb(elbow, hand, 0.04f, 0.03f);          // forearm
                p.Builder(p.Black).Ellipsoid(hand, new Vector3(0.035f, 0.04f, 0.05f), Quaternion.identity, 8, 6); // glove
            }

            p.Flush(transform, "Rider");

            for (int side = 0; side < 2; side++)
            {
                _thighs[side] = LimbObject(p, "Thigh", ThighLength, 0.075f, 0.055f, p.Shorts);
                _shins[side] = LimbObject(p, "Shin", ShinLength, 0.05f, 0.035f, p.Skin);
                var shoe = new GameObject("Shoe").transform;
                shoe.SetParent(transform, false);
                p.Builder(p.Shoes).Ellipsoid(Vector3.zero, new Vector3(0.045f, 0.04f, 0.13f), Quaternion.identity, 12, 8);
                p.Flush(shoe, "Shoe");
                _shoes[side] = shoe;
            }
        }

        /// <summary>Limb mesh along local +Y from 0 to <paramref name="length"/>.</summary>
        private Transform LimbObject(Palette p, string name, float length, float r0, float r1, Material material)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            p.Builder(material).Limb(Vector3.zero, new Vector3(0, length, 0), r0, r1);
            p.Flush(t, name);
            return t;
        }

        private static void Place(Transform limb, Vector3 from, Vector3 to)
        {
            limb.localPosition = from;
            limb.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
        }

        /// <summary>Two-bone IK: middle joint (knee/elbow) bending towards <paramref name="pole"/>.</summary>
        private static Vector3 SolveJoint(Vector3 root, Vector3 target, float upper, float lower, Vector3 pole)
        {
            Vector3 d = target - root;
            float dist = Mathf.Clamp(d.magnitude, 0.01f, upper + lower - 0.001f);
            Vector3 dir = d.normalized;
            float a = ((upper * upper) - (lower * lower) + (dist * dist)) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, (upper * upper) - (a * a)));
            Vector3 bend = (pole - (Vector3.Dot(pole, dir) * dir)).normalized;
            return root + (dir * a) + (bend * h);
        }

        /// <summary>Materials and per-material mesh builders (one draw call per material and part).</summary>
        private sealed class Palette
        {
            private readonly Dictionary<Material, MeshBuilder> _builders = new Dictionary<Material, MeshBuilder>();

            public Palette(WorldBuilder world, Color jersey)
            {
                Frame = world.Mat(new Color(0.75f, 0.08f, 0.1f));
                Black = world.Mat(new Color(0.07f, 0.07f, 0.08f));
                Rim = world.Mat(new Color(0.15f, 0.15f, 0.17f));
                Metal = world.Mat(new Color(0.55f, 0.57f, 0.6f));
                Bottle = world.Mat(new Color(0.92f, 0.92f, 0.9f));
                Jersey = world.Mat(jersey);
                Shorts = world.Mat(new Color(0.06f, 0.06f, 0.07f));
                Skin = world.Mat(new Color(0.93f, 0.76f, 0.62f));
                Helmet = world.Mat(new Color(0.95f, 0.95f, 0.95f));
                Shoes = world.Mat(new Color(0.95f, 0.95f, 0.95f));
            }

            public Material Frame { get; }

            public Material Black { get; }

            public Material Rim { get; }

            public Material Metal { get; }

            public Material Bottle { get; }

            public Material Jersey { get; }

            public Material Shorts { get; }

            public Material Skin { get; }

            public Material Helmet { get; }

            public Material Shoes { get; }

            public MeshBuilder Builder(Material material)
            {
                if (!_builders.TryGetValue(material, out var b))
                {
                    b = new MeshBuilder();
                    _builders[material] = b;
                }

                return b;
            }

            /// <summary>Creates one child object per material under <paramref name="parent"/> and resets the builders.</summary>
            public void Flush(Transform parent, string name)
            {
                foreach (var pair in _builders)
                {
                    if (pair.Value.VertexCount == 0) continue;
                    var go = new GameObject(name);
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = pair.Value.ToMesh(name);
                    go.AddComponent<MeshRenderer>().sharedMaterial = pair.Key;
                }

                _builders.Clear();
            }
        }
    }
}
