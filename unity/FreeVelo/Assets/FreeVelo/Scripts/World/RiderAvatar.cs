using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FreeVelo.World
{
    /// <summary>
    /// Rider on a modern disc-brake road bike, generated in code (no assets).
    /// Shapes are swept elliptical cross-sections (aero tubes, muscles, torso); legs follow the pedals with
    /// two-bone IK and the feet "ankle" with the crank angle.
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
        // Hip joint ~5 cm above the saddle top (sit bones on the saddle); knee 70–143° over a revolution.
        private const float ThighLength = 0.46f;
        private const float ShinLength = 0.45f;
        private static readonly Vector3 Hip = new Vector3(0.085f, 1.06f, -0.185f);
        private static readonly Vector3 Shoulder = new Vector3(0.175f, 1.40f, 0.25f);
        private static readonly Vector3 Hand = new Vector3(0.205f, 0.91f, 0.555f);

        /// <summary>Ankle relative to the cleat (pedal axle) in the foot frame.</summary>
        private static readonly Vector3 AnkleInFoot = new Vector3(0, 0.085f, -0.075f);

        private static readonly Vector3 Side = Vector3.right;

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
            if (custom != null) avatar.UseCustomBike(custom);
            else avatar.BuildBike(palette);

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

                // Ankling: toes drop on the back of the stroke, foot nearly flat at the front.
                var foot = Quaternion.Euler(10f - (8f * Mathf.Sin(angle)), 0, 0);
                var cleat = pedal + new Vector3(-sign * 0.02f, 0.012f, 0);
                var ankle = cleat + (foot * AnkleInFoot);
                var hip = new Vector3(sign * Hip.x, Hip.y, Hip.z);
                var knee = SolveJoint(hip, ankle, ThighLength, ShinLength, new Vector3(sign * 0.1f, 0, 1f));
                PlaceBone(_thighs[side], hip, knee);
                PlaceBone(_shins[side], knee, ankle);
                _shoes[side].localPosition = cleat;
                _shoes[side].localRotation = foot;
            }
        }

        // ------------------------------------------------------------------ bike

        private void BuildBike(Palette p)
        {
            var frame = p.Builder(p.Frame);
            var seatDir = (SeatCluster - BottomBracket).normalized;

            // Aero frame: elliptical tubes (narrow sideways, deeper in the direction of travel).
            frame.EllipticTube(HeadTop + new Vector3(0, -0.025f, -0.005f), SeatCluster + new Vector3(0, -0.015f, 0), new Vector2(0.016f, 0.02f), new Vector2(0.014f, 0.017f), Side); // top tube
            frame.EllipticTube(HeadBottom + new Vector3(0, 0.03f, -0.01f), BottomBracket + new Vector3(0, 0.02f, 0.03f), new Vector2(0.022f, 0.032f), new Vector2(0.03f, 0.03f), Side); // down tube
            frame.EllipticTube(BottomBracket + (seatDir * 0.02f), SeatCluster, new Vector2(0.018f, 0.024f), new Vector2(0.015f, 0.022f), Side); // seat tube
            frame.EllipticTube(HeadBottom - new Vector3(0, 0.01f, -0.003f), HeadTop + new Vector3(0, 0.015f, -0.004f), new Vector2(0.024f, 0.03f), new Vector2(0.02f, 0.025f), Side); // head tube
            frame.Tube(BottomBracket + new Vector3(-0.04f, 0, 0), BottomBracket + new Vector3(0.04f, 0, 0), 0.026f, 0.026f, 14); // BB shell
            var stayTop = BottomBracket + (seatDir * 0.45f);
            for (int s = -1; s <= 1; s += 2)
            {
                frame.EllipticTube(stayTop + new Vector3(s * 0.018f, 0, -0.01f), RearAxle + new Vector3(s * 0.058f, 0.01f, 0), new Vector2(0.008f, 0.012f), new Vector2(0.006f, 0.009f), Side); // dropped seat stay
                frame.EllipticTube(BottomBracket + new Vector3(s * 0.035f, 0, -0.03f), RearAxle + new Vector3(s * 0.062f, 0, 0), new Vector2(0.01f, 0.017f), new Vector2(0.007f, 0.011f), Side); // chain stay
                frame.EllipticTube(new Vector3(s * 0.034f, 0.665f, 0.442f), FrontAxle + new Vector3(s * 0.052f, 0.01f, 0), new Vector2(0.011f, 0.022f), new Vector2(0.007f, 0.012f), Side); // fork blade
                frame.Box(RearAxle + new Vector3(s * 0.06f, 0, 0), new Vector3(0.012f, 0.05f, 0.04f), Quaternion.identity); // dropout
                frame.Box(FrontAxle + new Vector3(s * 0.053f, 0, 0), new Vector3(0.01f, 0.04f, 0.035f), Quaternion.identity);
            }

            frame.Sweep(new[] { new Vector3(0, 0.655f, 0.448f), new Vector3(0, 0.68f, 0.44f) }, new[] { new Vector2(0.045f, 0.028f), new Vector2(0.035f, 0.025f) }, Side, 14); // fork crown

            // Decal band on the down tube
            var dtA = Vector3.Lerp(HeadBottom, BottomBracket, 0.35f);
            var dtB = Vector3.Lerp(HeadBottom, BottomBracket, 0.55f);
            p.Builder(p.Decal).EllipticTube(dtA, dtB, new Vector2(0.0245f, 0.0335f), new Vector2(0.0255f, 0.0335f), Side);

            // Seatpost, saddle with rails
            var black = p.Builder(p.Black);
            var postTop = SeatCluster + (seatDir * 0.16f);
            black.EllipticTube(SeatCluster - (seatDir * 0.02f), postTop, new Vector2(0.012f, 0.019f), new Vector2(0.012f, 0.019f), Side);
            black.Box(postTop + new Vector3(0, 0.012f, 0), new Vector3(0.04f, 0.02f, 0.06f), Quaternion.identity);
            black.Sweep(
                new[] { new Vector3(0, 0.982f, -0.06f), new Vector3(0, 0.986f, -0.11f), new Vector3(0, 0.99f, -0.18f), new Vector3(0, 0.993f, -0.25f), new Vector3(0, 0.99f, -0.3f) },
                new[] { new Vector2(0.015f, 0.01f), new Vector2(0.026f, 0.014f), new Vector2(0.05f, 0.017f), new Vector2(0.07f, 0.019f), new Vector2(0.06f, 0.016f) },
                Side, 14);
            var metal = p.Builder(p.Metal);
            for (int s = -1; s <= 1; s += 2)
            {
                metal.Path(new[] { new Vector3(s * 0.02f, 0.976f, -0.08f), new Vector3(s * 0.025f, 0.968f, -0.14f), new Vector3(s * 0.03f, 0.968f, -0.25f), new Vector3(s * 0.035f, 0.978f, -0.29f) }, 0.0035f, 6);
            }

            // Cockpit: spacers, stem, bars with tape, hoods, levers
            black.Tube(HeadTop + new Vector3(0, 0.015f, -0.004f), new Vector3(0, 0.875f, 0.384f), 0.018f, 0.018f, 12);
            black.Ellipsoid(new Vector3(0, 0.878f, 0.384f), new Vector3(0.018f, 0.006f, 0.018f), Quaternion.identity, 12, 4);
            black.EllipticTube(new Vector3(0, 0.862f, 0.378f), new Vector3(0, 0.877f, 0.482f), new Vector2(0.017f, 0.02f), new Vector2(0.016f, 0.018f), Side, 12);
            var tape = p.Builder(p.BarTape);
            tape.Sweep(new[] { new Vector3(-0.2f, 0.877f, 0.48f), new Vector3(0, 0.877f, 0.48f), new Vector3(0.2f, 0.877f, 0.48f) }, new[] { new Vector2(0.013f, 0.011f), new Vector2(0.016f, 0.013f), new Vector2(0.013f, 0.011f) }, Vector3.forward, 10, false, false);
            for (int s = -1; s <= 1; s += 2)
            {
                tape.Path(new[]
                {
                    new Vector3(s * 0.2f, 0.877f, 0.48f),
                    new Vector3(s * 0.204f, 0.882f, 0.545f),
                    new Vector3(s * 0.21f, 0.855f, 0.6f),
                    new Vector3(s * 0.212f, 0.79f, 0.608f),
                    new Vector3(s * 0.212f, 0.735f, 0.575f),
                    new Vector3(s * 0.212f, 0.726f, 0.505f),
                }, 0.0125f, 10);
                black.Sweep(
                    new[] { new Vector3(s * 0.205f, 0.882f, 0.55f), new Vector3(s * 0.205f, 0.905f, 0.575f), new Vector3(s * 0.205f, 0.91f, 0.598f) },
                    new[] { new Vector2(0.017f, 0.02f), new Vector2(0.019f, 0.024f), new Vector2(0.012f, 0.015f) },
                    Side, 10); // hood
                black.Path(new[] { new Vector3(s * 0.205f, 0.895f, 0.6f), new Vector3(s * 0.207f, 0.84f, 0.625f), new Vector3(s * 0.205f, 0.78f, 0.618f) }, 0.006f, 6); // lever
            }

            // Bottle cage and bottle on the down tube
            var downDir = (BottomBracket - HeadBottom).normalized;
            var normal = Vector3.Cross(downDir, Vector3.right).normalized;
            if (normal.y < 0) normal = -normal;
            var b0 = Vector3.Lerp(HeadBottom, BottomBracket, 0.62f) + (normal * 0.068f);
            var b1 = Vector3.Lerp(HeadBottom, BottomBracket, 0.3f) + (normal * 0.068f);
            var bottleDir = (b1 - b0).normalized;
            p.Builder(p.Bottle).Sweep(
                new[] { b0, b0 + (bottleDir * 0.05f), b0 + (bottleDir * 0.17f), b0 + (bottleDir * 0.2f), b0 + (bottleDir * 0.215f) },
                new[] { new Vector2(0.033f, 0.033f), new Vector2(0.036f, 0.036f), new Vector2(0.036f, 0.036f), new Vector2(0.024f, 0.024f), new Vector2(0.012f, 0.012f) },
                Side, 14);
            black.Path(new[] { b0 + (bottleDir * 0.02f) - (normal * 0.03f), b0 + (bottleDir * 0.02f) + (normal * 0.005f) }, 0.004f, 5);
            black.Path(new[] { b0 + (bottleDir * 0.16f) - (normal * 0.03f), b0 + (bottleDir * 0.16f) + (normal * 0.005f) }, 0.004f, 5);

            // Brake calipers (flat mount), thru-axle ends
            black.Box(FrontAxle + new Vector3(-0.058f, 0.07f, -0.045f), new Vector3(0.022f, 0.045f, 0.06f), Quaternion.Euler(-35, 0, 0));
            black.Box(RearAxle + new Vector3(-0.058f, 0.045f, 0.06f), new Vector3(0.022f, 0.045f, 0.06f), Quaternion.Euler(25, 0, 0));
            metal.Tube(FrontAxle + new Vector3(-0.07f, 0, 0), FrontAxle + new Vector3(0.07f, 0, 0), 0.008f, 0.008f, 8);
            metal.Tube(RearAxle + new Vector3(-0.075f, 0, 0), RearAxle + new Vector3(0.075f, 0, 0), 0.008f, 0.008f, 8);

            // Drivetrain: chain, front and rear derailleur
            var chain = p.Builder(p.Chain);
            chain.Tube(BottomBracket + new Vector3(0.068f, 0.098f, -0.005f), RearAxle + new Vector3(0.068f, 0.048f, 0), 0.0045f, 0.0045f, 5);
            chain.Tube(BottomBracket + new Vector3(0.068f, -0.098f, -0.005f), RearAxle + new Vector3(0.07f, -0.095f, 0.03f), 0.0045f, 0.0045f, 5);
            var fd = BottomBracket + (seatDir * 0.155f) + new Vector3(0.035f, 0, -0.005f);
            black.Box(fd, new Vector3(0.025f, 0.03f, 0.06f), Quaternion.LookRotation(Vector3.Cross(Vector3.right, seatDir), seatDir));
            black.Box(RearAxle + new Vector3(0.072f, -0.035f, 0.005f), new Vector3(0.016f, 0.05f, 0.03f), Quaternion.Euler(10, 0, 0));
            black.Box(RearAxle + new Vector3(0.078f, -0.075f, 0.015f), new Vector3(0.008f, 0.075f, 0.024f), Quaternion.Euler(-12, 0, 0));
            metal.Torus(RearAxle + new Vector3(0.071f, -0.045f, 0.01f), Vector3.right, 0.012f, 0.004f, 0.003f, 12, 5);
            metal.Torus(RearAxle + new Vector3(0.071f, -0.1f, 0.025f), Vector3.right, 0.012f, 0.004f, 0.003f, 12, 5);

            p.Flush(transform, "Bike");

            _rearWheel = BuildWheel(p, RearAxle, rear: true);
            _frontWheel = BuildWheel(p, FrontAxle, rear: false);

            // Crankset: tapered arms (right up at angle 0, left opposite), spider, two chainrings.
            _crank = new GameObject("Crankset").transform;
            _crank.SetParent(transform, false);
            _crank.localPosition = BottomBracket;
            var crank = p.Builder(p.Crank);
            crank.Tube(new Vector3(-0.09f, 0, 0), new Vector3(0.09f, 0, 0), 0.012f, 0.012f, 10);
            crank.Sweep(new[] { new Vector3(0.088f, -0.015f, 0), new Vector3(0.09f, CrankLength * 0.5f, 0), new Vector3(0.09f, CrankLength + 0.012f, 0) }, new[] { new Vector2(0.009f, 0.02f), new Vector2(0.008f, 0.015f), new Vector2(0.008f, 0.012f) }, Side, 8);
            crank.Sweep(new[] { new Vector3(-0.088f, 0.015f, 0), new Vector3(-0.09f, -CrankLength * 0.5f, 0), new Vector3(-0.09f, -CrankLength - 0.012f, 0) }, new[] { new Vector2(0.009f, 0.02f), new Vector2(0.008f, 0.015f), new Vector2(0.008f, 0.012f) }, Side, 8);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * Mathf.PI / 2f) + 0.4f;
                crank.EllipticTube(new Vector3(0.075f, 0, 0), new Vector3(0.072f, Mathf.Cos(a) * 0.075f, Mathf.Sin(a) * 0.075f), new Vector2(0.004f, 0.012f), new Vector2(0.004f, 0.008f), Side, 6);
            }

            var rings = p.Builder(p.Chain);
            rings.Torus(new Vector3(0.07f, 0, 0), Vector3.right, 0.098f, 0.009f, 0.0025f, 48, 6);
            rings.Torus(new Vector3(0.064f, 0, 0), Vector3.right, 0.072f, 0.008f, 0.0025f, 40, 6);
            p.Flush(_crank, "Crankset");

            for (int side = 0; side < 2; side++)
            {
                var pedal = new GameObject(side == 0 ? "PedalRight" : "PedalLeft").transform;
                pedal.SetParent(transform, false);
                var pb = p.Builder(p.Black);
                float sx = side == 0 ? 1f : -1f;
                pb.Sweep(new[] { new Vector3(0, 0, -0.04f), new Vector3(0, 0.002f, 0), new Vector3(0, 0, 0.045f) }, new[] { new Vector2(0.032f, 0.008f), new Vector2(0.036f, 0.01f), new Vector2(0.03f, 0.007f) }, Side, 8);
                p.Builder(p.Metal).Tube(new Vector3(-sx * 0.045f, 0, 0), Vector3.zero, 0.006f, 0.006f, 6);
                p.Flush(pedal, "Pedal");
                _pedals[side] = pedal;
            }
        }

        private Transform BuildWheel(Palette p, Vector3 axle, bool rear)
        {
            var wheel = new GameObject(rear ? "RearWheel" : "FrontWheel").transform;
            wheel.SetParent(transform, false);
            wheel.localPosition = axle;

            p.Builder(p.Tyre).Torus(Vector3.zero, Vector3.right, WheelRadius - 0.014f, 0.014f, 0.0125f, 56, 10);
            p.Builder(p.Rim).Torus(Vector3.zero, Vector3.right, 0.29f, 0.028f, 0.0105f, 56, 10);
            var decal = p.Builder(p.Decal);
            decal.Torus(Vector3.zero, Vector3.right, 0.296f, 0.007f, 0.0112f, 56, 6);
            var metal = p.Builder(p.Metal);
            metal.Tube(new Vector3(-0.055f, 0, 0), new Vector3(0.055f, 0, 0), 0.016f, 0.016f, 12);
            for (int s = -1; s <= 1; s += 2) metal.Torus(new Vector3(s * 0.032f, 0, 0), Vector3.right, 0.024f, 0.005f, 0.002f, 16, 5); // hub flanges
            var spokes = p.Builder(p.Spokes);
            for (int k = 0; k < 24; k++)
            {
                float a = k * Mathf.PI * 2f / 24;
                float side = k % 2 == 0 ? 1f : -1f;
                float lead = side * 0.12f; // slight tangential lacing
                var hub = new Vector3(side * 0.032f, Mathf.Cos(a + lead) * 0.026f, Mathf.Sin(a + lead) * 0.026f);
                var rim = new Vector3(side * 0.004f, Mathf.Cos(a) * 0.264f, Mathf.Sin(a) * 0.264f);
                spokes.Tube(hub, rim, 0.0011f, 0.0011f, 4, false);
            }

            // Disc brake rotor on the left, with spider
            metal.Torus(new Vector3(-0.05f, 0, 0), Vector3.right, 0.07f, 0.011f, 0.001f, 40, 4);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                metal.Tube(new Vector3(-0.05f, Mathf.Cos(a) * 0.018f, Mathf.Sin(a) * 0.018f), new Vector3(-0.05f, Mathf.Cos(a + 0.3f) * 0.062f, Mathf.Sin(a + 0.3f) * 0.062f), 0.003f, 0.003f, 4, false);
            }

            if (rear)
            {
                for (int i = 0; i < 6; i++) metal.Torus(new Vector3(0.042f + (i * 0.0045f), 0, 0), Vector3.right, 0.05f - (i * 0.0055f), 0.006f, 0.0015f, 28, 4);
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
            // Torso along a rounded back: bib shorts (pelvis → waist) and jersey (waist → shoulders).
            var spine = new[]
            {
                new Vector3(0, 1.085f, -0.19f),
                new Vector3(0, 1.145f, -0.125f),
                new Vector3(0, 1.225f, -0.03f),
                new Vector3(0, 1.31f, 0.09f),
                new Vector3(0, 1.385f, 0.21f),
                new Vector3(0, 1.43f, 0.27f),
            };
            var torsoRadii = new[]
            {
                new Vector2(0.148f, 0.11f),
                new Vector2(0.138f, 0.105f),
                new Vector2(0.148f, 0.115f),
                new Vector2(0.168f, 0.12f),
                new Vector2(0.178f, 0.105f),
                new Vector2(0.085f, 0.07f),
            };
            p.Builder(p.Shorts).Sweep(new[] { spine[0], spine[1] }, new[] { torsoRadii[0], torsoRadii[1] }, Side, 16, true, false);
            // Pelvis/seat as one smooth shape swept from back to front (bib shorts): wide, flat,
            // resting on the saddle; the lower torso and the thigh tops start inside it (no seams).
            p.Builder(p.Shorts).Sweep(
                new[]
                {
                    new Vector3(0, 1.075f, -0.315f),
                    new Vector3(0, 1.07f, -0.29f),
                    new Vector3(0, 1.07f, -0.245f),
                    new Vector3(0, 1.075f, -0.19f),
                    new Vector3(0, 1.08f, -0.13f),
                    new Vector3(0, 1.08f, -0.085f),
                },
                new[]
                {
                    new Vector2(0.07f, 0.035f),
                    new Vector2(0.115f, 0.06f),
                    new Vector2(0.145f, 0.078f),
                    new Vector2(0.152f, 0.082f),
                    new Vector2(0.14f, 0.075f),
                    new Vector2(0.1f, 0.05f),
                },
                Side, 18);
            p.Builder(p.Jersey).Sweep(spine.Skip(1).ToList(), torsoRadii.Skip(1).ToList(), Side, 16, false, true);
            p.Builder(p.Accent).Sweep(
                new[] { Vector3.Lerp(spine[2], spine[3], 0.45f), Vector3.Lerp(spine[2], spine[3], 0.7f) },
                new[] { Vector2.Lerp(torsoRadii[2], torsoRadii[3], 0.45f) * 1.025f, Vector2.Lerp(torsoRadii[2], torsoRadii[3], 0.7f) * 1.025f },
                Side, 16, false, false); // chest band
            for (int s = -1; s <= 1; s += 2)
            {
                p.Builder(p.Jersey).Ellipsoid(new Vector3(s * 0.16f, 1.385f, 0.235f), new Vector3(0.065f, 0.06f, 0.07f), Quaternion.identity, 12, 8); // shoulder
            }

            // Neck and head
            var skin = p.Builder(p.Skin);
            skin.Sweep(new[] { new Vector3(0, 1.4f, 0.25f), new Vector3(0, 1.46f, 0.31f), new Vector3(0, 1.505f, 0.345f) }, new[] { new Vector2(0.055f, 0.05f), new Vector2(0.047f, 0.045f), new Vector2(0.045f, 0.042f) }, Side, 12, false, false);
            var headCenter = new Vector3(0, 1.555f, 0.375f);
            skin.Ellipsoid(headCenter, new Vector3(0.08f, 0.098f, 0.104f), Quaternion.Euler(15, 0, 0), 16, 10);
            skin.Ellipsoid(headCenter + new Vector3(0, -0.05f, 0.045f), new Vector3(0.058f, 0.045f, 0.058f), Quaternion.Euler(10, 0, 0), 12, 8); // jaw
            skin.Ellipsoid(headCenter + new Vector3(0, -0.01f, 0.103f), new Vector3(0.012f, 0.02f, 0.016f), Quaternion.Euler(-20, 0, 0), 8, 6); // nose
            for (int s = -1; s <= 1; s += 2) skin.Ellipsoid(headCenter + new Vector3(s * 0.079f, -0.005f, -0.005f), new Vector3(0.012f, 0.026f, 0.018f), Quaternion.identity, 8, 6); // ears

            // Aero road helmet with vents and straps
            var helmetRot = Quaternion.Euler(-12, 0, 0);
            var helmetCenter = headCenter + new Vector3(0, 0.018f, -0.012f);
            p.Builder(p.Helmet).Ellipsoid(helmetCenter, new Vector3(0.105f, 0.105f, 0.148f), helmetRot, 18, 8, upperHalf: true);
            var dark = p.Builder(p.Black);
            for (int v = -1; v <= 1; v++)
            {
                // On the helmet surface (ellipsoid), half sunk in: dark vent stripes.
                float x = v * 0.045f;
                float y = 0.105f * Mathf.Sqrt(1f - ((x / 0.105f) * (x / 0.105f)));
                var top = helmetCenter + (helmetRot * new Vector3(x, y, 0.01f));
                dark.Box(top, new Vector3(0.012f, 0.012f, 0.12f), helmetRot * Quaternion.Euler(0, 0, -v * 25f));
            }

            for (int s = -1; s <= 1; s += 2)
            {
                dark.Path(new[] { headCenter + new Vector3(s * 0.088f, 0.0f, 0.02f), headCenter + new Vector3(s * 0.065f, -0.075f, 0.04f), headCenter + new Vector3(0, -0.098f, 0.05f) }, 0.004f, 5);
            }

            // Wraparound sunglasses
            var lens = new List<Vector3>();
            var lensRadii = new List<Vector2>();
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.Lerp(-1.25f, 1.25f, i / 8f);
                lens.Add(headCenter + new Vector3(Mathf.Sin(a) * 0.088f, 0.012f, (Mathf.Cos(a) * 0.108f) - 0.002f));
                lensRadii.Add(new Vector2(Mathf.Abs(a) < 0.9f ? 0.022f : 0.012f, 0.004f));
            }

            p.Builder(p.Lens).Sweep(lens, lensRadii, Vector3.up, 6);

            // Arms: sleeve, forearm, gloved hand on the hood
            for (int s = -1; s <= 1; s += 2)
            {
                var shoulder = new Vector3(s * Shoulder.x, Shoulder.y, Shoulder.z);
                var hand = new Vector3(s * Hand.x, Hand.y, Hand.z);
                var elbow = SolveJoint(shoulder, hand, 0.3f, 0.31f, new Vector3(s * 0.6f, -1f, 0f));
                var upper = new[] { shoulder, Vector3.Lerp(shoulder, elbow, 0.3f), Vector3.Lerp(shoulder, elbow, 0.55f) };
                p.Builder(p.Jersey).Sweep(upper, new[] { new Vector2(0.055f, 0.055f), new Vector2(0.05f, 0.052f), new Vector2(0.047f, 0.048f) }, Side, 12);
                skin.Sweep(new[] { Vector3.Lerp(shoulder, elbow, 0.5f), elbow }, new[] { new Vector2(0.043f, 0.043f), new Vector2(0.036f, 0.038f) }, Side, 12, false, false);
                skin.Ellipsoid(elbow, Vector3.one * 0.037f, Quaternion.identity, 10, 6);
                skin.Sweep(
                    new[] { elbow, Vector3.Lerp(elbow, hand, 0.3f), Vector3.Lerp(elbow, hand, 0.85f) },
                    new[] { new Vector2(0.036f, 0.038f), new Vector2(0.04f, 0.042f), new Vector2(0.026f, 0.022f) },
                    Side, 12, false, false);
                var glove = p.Builder(p.Glove);
                glove.Ellipsoid(hand + new Vector3(0, 0.012f, -0.01f), new Vector3(0.03f, 0.04f, 0.052f), Quaternion.Euler(30, 0, 0), 10, 8);
                glove.Ellipsoid(hand + new Vector3(0, -0.012f, 0.03f), new Vector3(0.026f, 0.035f, 0.03f), Quaternion.Euler(60, 0, 0), 10, 6); // fingers around the hood
            }

            p.Flush(transform, "Rider");

            // Legs: separate bones moved by the IK in Animate()
            for (int side = 0; side < 2; side++)
            {
                _thighs[side] = BuildThigh(p);
                _shins[side] = BuildShin(p);
                _shoes[side] = BuildShoe(p);
            }
        }

        /// <summary>Thigh along local +Y (hip → knee); local +Z is the front (quadriceps).</summary>
        private Transform BuildThigh(Palette p)
        {
            var bone = NewBone("Thigh");
            float l = ThighLength;
            p.Builder(p.Shorts).Sweep(
                new[] { new Vector3(0, -0.06f, -0.015f), new Vector3(0, -0.01f, -0.005f), new Vector3(0, l * 0.15f, 0.005f), new Vector3(0, l * 0.5f, 0.01f), new Vector3(0, l * 0.78f, 0.005f) },
                new[] { new Vector2(0.06f, 0.065f), new Vector2(0.08f, 0.086f), new Vector2(0.076f, 0.083f), new Vector2(0.066f, 0.072f), new Vector2(0.057f, 0.06f) },
                Side, 14);
            p.Builder(p.Skin).Sweep(
                new[] { new Vector3(0, l * 0.77f, 0.005f), new Vector3(0, l * 0.92f, 0f), new Vector3(0, l + 0.01f, 0f) },
                new[] { new Vector2(0.056f, 0.058f), new Vector2(0.05f, 0.052f), new Vector2(0.048f, 0.05f) },
                Side, 14, false, true);
            p.Builder(p.Skin).Ellipsoid(new Vector3(0, l, 0.028f), new Vector3(0.033f, 0.036f, 0.025f), Quaternion.identity, 10, 6); // kneecap
            p.Flush(bone, "Thigh");
            return bone;
        }

        /// <summary>Shin along local +Y (knee → ankle); calf bulges to the back (−Z).</summary>
        private Transform BuildShin(Palette p)
        {
            var bone = NewBone("Shin");
            float l = ShinLength;
            p.Builder(p.Skin).Sweep(
                new[] { new Vector3(0, -0.01f, 0), new Vector3(0, l * 0.22f, -0.014f), new Vector3(0, l * 0.45f, -0.008f), new Vector3(0, l * 0.75f, 0) },
                new[] { new Vector2(0.048f, 0.05f), new Vector2(0.05f, 0.062f), new Vector2(0.042f, 0.05f), new Vector2(0.031f, 0.034f) },
                Side, 14, true, false);
            p.Builder(p.Sock).Sweep(
                new[] { new Vector3(0, l * 0.74f, 0), new Vector3(0, l + 0.01f, 0.005f) },
                new[] { new Vector2(0.032f, 0.035f), new Vector2(0.03f, 0.033f) },
                Side, 14, false, true);
            p.Flush(bone, "Shin");
            return bone;
        }

        /// <summary>Cycling shoe; origin at the cleat, +Z towards the toes.</summary>
        private Transform BuildShoe(Palette p)
        {
            var bone = NewBone("Shoe");
            p.Builder(p.Shoes).Sweep(
                new[] { new Vector3(0, 0.055f, -0.125f), new Vector3(0, 0.05f, -0.09f), new Vector3(0, 0.045f, -0.02f), new Vector3(0, 0.035f, 0.06f), new Vector3(0, 0.026f, 0.115f), new Vector3(0, 0.022f, 0.135f) },
                new[] { new Vector2(0.032f, 0.04f), new Vector2(0.038f, 0.042f), new Vector2(0.045f, 0.036f), new Vector2(0.043f, 0.026f), new Vector2(0.032f, 0.018f), new Vector2(0.012f, 0.008f) },
                Side, 14);
            p.Builder(p.Black).Sweep(
                new[] { new Vector3(0, 0.012f, -0.13f), new Vector3(0, 0.01f, 0f), new Vector3(0, 0.01f, 0.13f) },
                new[] { new Vector2(0.032f, 0.008f), new Vector2(0.042f, 0.008f), new Vector2(0.03f, 0.007f) },
                Side, 10); // sole
            p.Builder(p.Black).Box(new Vector3(0, 0.003f, 0.005f), new Vector3(0.045f, 0.008f, 0.06f), Quaternion.identity); // cleat
            p.Builder(p.Black).Sweep(new[] { new Vector3(-0.04f, 0.06f, -0.03f), new Vector3(0, 0.083f, -0.035f), new Vector3(0.04f, 0.06f, -0.03f) }, new[] { new Vector2(0.012f, 0.003f), new Vector2(0.014f, 0.003f), new Vector2(0.012f, 0.003f) }, Vector3.forward, 4); // strap
            p.Flush(bone, "Shoe");
            return bone;
        }

        private Transform NewBone(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        /// <summary>Places a bone from <paramref name="from"/> to <paramref name="to"/> with local +Z facing forward/up.</summary>
        private static void PlaceBone(Transform bone, Vector3 from, Vector3 to)
        {
            Vector3 dir = (to - from).normalized;
            Vector3 front = Vector3.Cross(dir, Vector3.right);
            bone.localPosition = from;
            bone.localRotation = Quaternion.LookRotation(front.sqrMagnitude > 1e-6f ? front : Vector3.forward, dir);
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
                Frame = world.Mat(new Color(0.72f, 0.06f, 0.08f), null, 0.75f);
                Decal = world.Mat(new Color(0.95f, 0.95f, 0.95f), null, 0.6f);
                Black = world.Mat(new Color(0.06f, 0.06f, 0.07f), null, 0.35f);
                BarTape = world.Mat(new Color(0.05f, 0.05f, 0.05f), null, 0.1f);
                Tyre = world.Mat(new Color(0.045f, 0.045f, 0.045f), null, 0.15f);
                Rim = world.Mat(new Color(0.09f, 0.09f, 0.1f), null, 0.65f);
                Metal = world.Mat(new Color(0.62f, 0.64f, 0.67f), null, 0.8f);
                Spokes = world.Mat(new Color(0.12f, 0.12f, 0.13f), null, 0.5f);
                Chain = world.Mat(new Color(0.42f, 0.43f, 0.45f), null, 0.7f);
                Crank = world.Mat(new Color(0.1f, 0.1f, 0.11f), null, 0.6f);
                Bottle = world.Mat(new Color(0.92f, 0.92f, 0.9f), null, 0.4f);
                Jersey = world.Mat(jersey, null, 0.25f);
                Accent = world.Mat(new Color(0.95f, 0.95f, 0.95f), null, 0.25f);
                Shorts = world.Mat(new Color(0.05f, 0.05f, 0.06f), null, 0.3f);
                Skin = world.Mat(new Color(0.9f, 0.72f, 0.58f), null, 0.3f);
                Sock = world.Mat(new Color(0.95f, 0.95f, 0.95f), null, 0.1f);
                Shoes = world.Mat(new Color(0.96f, 0.96f, 0.96f), null, 0.55f);
                Glove = world.Mat(new Color(0.08f, 0.08f, 0.09f), null, 0.2f);
                Helmet = world.Mat(new Color(0.97f, 0.97f, 0.97f), null, 0.7f);
                Lens = world.Mat(new Color(0.05f, 0.08f, 0.15f), null, 0.95f);
            }

            public Material Frame { get; }

            public Material Decal { get; }

            public Material Black { get; }

            public Material BarTape { get; }

            public Material Tyre { get; }

            public Material Rim { get; }

            public Material Metal { get; }

            public Material Spokes { get; }

            public Material Chain { get; }

            public Material Crank { get; }

            public Material Bottle { get; }

            public Material Jersey { get; }

            public Material Accent { get; }

            public Material Shorts { get; }

            public Material Skin { get; }

            public Material Sock { get; }

            public Material Shoes { get; }

            public Material Glove { get; }

            public Material Helmet { get; }

            public Material Lens { get; }

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
