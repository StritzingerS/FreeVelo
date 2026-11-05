using System;
using System.Numerics;

namespace FreeVelo.Core.Avatar
{
    public sealed partial class AvatarModel
    {
        private void BuildBike()
        {
            var frameMat = Mat("frame", Style.Frame, 0.75f);
            var decalMat = Mat("decal", V(0.95f, 0.95f, 0.95f), 0.6f);
            var blackMat = Mat("black", V(0.06f, 0.06f, 0.07f), 0.35f);
            var tapeMat = Mat("bartape", V(0.05f, 0.05f, 0.05f), 0.1f);
            var metalMat = Mat("metal", V(0.62f, 0.64f, 0.67f), 0.8f, 0.6f);
            var chainMat = Mat("chain", V(0.42f, 0.43f, 0.45f), 0.7f, 0.6f);
            var crankMat = Mat("crank", V(0.1f, 0.1f, 0.11f), 0.6f);
            var leverMat = Mat("lever", V(0.24f, 0.25f, 0.27f), 0.7f);
            var bottleMat = Mat("bottle", V(0.92f, 0.92f, 0.9f), 0.4f);

            var seatDir = Vector3.Normalize(SeatCluster - BottomBracket);
            var downDir = Vector3.Normalize(BottomBracket - HeadBottom);
            BuildFrame(frameMat, decalMat, seatDir, downDir);

            // Aero seatpost, saddle with rails
            var black = B(AvatarNode.Bike, blackMat);
            var postTop = SeatCluster + (seatDir * 0.16f);
            var saddleMat = Mat("saddle", V(0.05f, 0.05f, 0.055f), 0.45f);
            var post = SdfShapes.Squashed(SdfShapes.RoundCone(SeatCluster - (seatDir * 0.03f), postTop, 0.019f, 0.019f), Vector3.Zero, Vector3.UnitX, 0.6f);
            var clamp = SdfShapes.RoundBox(postTop + V(0, 0.012f, 0), V(0.022f, 0.012f, 0.03f), 0.006f);
            Sculpt(AvatarNode.Bike, SdfShapes.Blend(0.01f, post, clamp), V(-0.04f, 0.74f, -0.24f), V(0.04f, 0.98f, -0.1f), 0.003f, 700, blackMat);
            BuildSaddle(saddleMat, blackMat);
            var metal = B(AvatarNode.Bike, metalMat);
            for (int s = -1; s <= 1; s += 2)
            {
                metal.Path(new[] { V(s * 0.02f, 0.976f, -0.08f), V(s * 0.025f, 0.968f, -0.14f), V(s * 0.03f, 0.968f, -0.25f), V(s * 0.035f, 0.978f, -0.29f) }, 0.0035f, 6);
            }

            // Cockpit: spacers, stem, bars with tape, hoods, levers
            black.Tube(HeadTop + V(0, 0.015f, -0.004f), V(0, 0.875f, 0.384f), 0.018f, 0.018f, 12);
            black.Ellipsoid(V(0, 0.878f, 0.384f), V(0.018f, 0.006f, 0.018f), Rot.Identity, 12, 4);
            black.EllipticTube(V(0, 0.862f, 0.378f), V(0, 0.877f, 0.482f), R(0.017f, 0.02f), R(0.016f, 0.018f), Side, 12);
            var tape = B(AvatarNode.Bike, tapeMat);
            tape.Sweep(new[] { V(-0.2f, 0.877f, 0.48f), V(0, 0.877f, 0.48f), V(0.2f, 0.877f, 0.48f) }, new[] { R(0.013f, 0.011f), R(0.016f, 0.013f), R(0.013f, 0.011f) }, Vector3.UnitZ, 10, false, false);
            var lever = B(AvatarNode.Bike, leverMat);
            for (int s = -1; s <= 1; s += 2)
            {
                tape.Path(new[] { V(s * 0.2f, 0.877f, 0.48f), V(s * 0.204f, 0.882f, 0.545f), V(s * 0.21f, 0.855f, 0.6f), V(s * 0.212f, 0.79f, 0.608f), V(s * 0.212f, 0.735f, 0.575f), V(s * 0.212f, 0.726f, 0.505f) }, 0.0125f, 10);
                // Hood (rubber body with the peak at the front) and lever blade with the shift paddle behind it.
                float x0 = s * 0.205f;
                var hood = SdfShapes.Squashed(
                    SdfShapes.Blend(
                        0.012f,
                        SdfShapes.RoundCone(V(x0, 0.878f, 0.543f), V(x0, 0.895f, 0.576f), 0.0165f, 0.0205f),
                        SdfShapes.RoundCone(V(x0, 0.895f, 0.576f), V(x0, 0.91f, 0.601f), 0.0205f, 0.0135f),
                        SdfShapes.Sphere(V(x0, 0.917f, 0.607f), 0.0095f)),
                    V(x0, 0, 0),
                    Vector3.UnitX,
                    0.88f);
                Sculpt(AvatarNode.Bike, hood, V(x0 - 0.04f, 0.85f, 0.51f), V(x0 + 0.04f, 0.94f, 0.64f), 0.0018f, 700, blackMat);
                var blade = SdfShapes.Squashed(
                    SdfShapes.Chain(
                        new[] { V(x0, 0.902f, 0.611f), V(x0, 0.872f, 0.626f), V(x0, 0.832f, 0.633f), V(x0, 0.792f, 0.631f), V(x0, 0.757f, 0.621f) },
                        new[] { 0.011f, 0.0115f, 0.0105f, 0.0095f, 0.0085f }),
                    V(x0, 0, 0),
                    Vector3.UnitX,
                    0.62f);
                var paddle = SdfShapes.Squashed(
                    SdfShapes.Chain(new[] { V(x0 - (s * 0.003f), 0.884f, 0.612f), V(x0 - (s * 0.006f), 0.845f, 0.618f), V(x0 - (s * 0.008f), 0.81f, 0.614f) }, new[] { 0.0065f, 0.006f, 0.0055f }),
                    V(x0, 0, 0),
                    Vector3.UnitX,
                    0.5f);
                Sculpt(AvatarNode.Bike, SdfShapes.Union(blade, paddle), V(x0 - 0.03f, 0.73f, 0.58f), V(x0 + 0.03f, 0.93f, 0.66f), 0.0016f, 700, leverMat);
            }

            // Bottle cage and bottle on the down tube
            var normal = Vector3.Normalize(Vector3.Cross(downDir, Vector3.UnitX));
            if (normal.Y < 0) normal = -normal;
            var downAxisTop = HeadBottom + V(0, 0.035f, -0.012f); // same axis as the sculpted down tube
            var downAxisBottom = BottomBracket + V(0, 0.025f, 0.035f);
            var b0 = Vector3.Lerp(downAxisTop, downAxisBottom, 0.66f) + (normal * 0.072f);
            var b1 = Vector3.Lerp(downAxisTop, downAxisBottom, 0.3f) + (normal * 0.072f);
            var bottleDir = Vector3.Normalize(b1 - b0);
            Vector3 At(float along, float off = 0f) => b0 + (bottleDir * along) + (normal * off);
            var bottle = SdfShapes.Blend(
                0.012f,
                SdfShapes.RoundCone(At(0.012f), At(0.07f), 0.035f, 0.036f),
                SdfShapes.RoundCone(At(0.07f), At(0.1f), 0.036f, 0.0325f), // grip waist
                SdfShapes.RoundCone(At(0.1f), At(0.165f), 0.0325f, 0.036f),
                SdfShapes.RoundCone(At(0.165f), At(0.205f), 0.036f, 0.015f), // shoulder
                SdfShapes.RoundCone(At(0.2f), At(0.226f), 0.012f, 0.008f)); // cap and nozzle
            Sdf Along(float from) => p => from - Vector3.Dot(p - b0, bottleDir);
            var bmin = Vector3.Min(b0, b1) - new Vector3(0.06f);
            var bmax = Vector3.Max(b0, b1) + new Vector3(0.06f);
            Sculpt(AvatarNode.Bike, bottle, bmin, bmax, 0.0025f, 900, bottleMat, (Along(0.115f), frameMat), (Along(0.15f), bottleMat), (Along(0.198f), blackMat));

            // Cage: two wire rails along the bottle with a bottom hook.
            var side = Vector3.UnitX * 0.028f;
            var cage = SdfShapes.Union(
                SdfShapes.Chain(new[] { At(0.03f, -0.034f) + side, At(0.04f, -0.01f) + side, At(0.15f, -0.012f) + side, At(0.17f, -0.034f) + side }, new[] { 0.0028f, 0.0028f, 0.0028f, 0.0028f }),
                SdfShapes.Chain(new[] { At(0.03f, -0.034f) - side, At(0.04f, -0.01f) - side, At(0.15f, -0.012f) - side, At(0.17f, -0.034f) - side }, new[] { 0.0028f, 0.0028f, 0.0028f, 0.0028f }),
                SdfShapes.Chain(new[] { At(0.03f, -0.034f) + side, At(0.0f, -0.03f), At(0.03f, -0.034f) - side }, new[] { 0.0028f, 0.0028f, 0.0028f }),
                SdfShapes.Chain(new[] { At(0.0f, -0.034f), At(-0.006f, -0.01f), At(0.0f, 0.012f) }, new[] { 0.003f, 0.003f, 0.003f }));
            Sculpt(AvatarNode.Bike, cage, bmin, bmax, 0.0016f, 700, blackMat);

            // Thru-axle ends
            metal.Tube(FrontAxle + V(-0.07f, 0, 0), FrontAxle + V(0.07f, 0, 0), 0.008f, 0.008f, 8);
            metal.Tube(RearAxle + V(-0.075f, 0, 0), RearAxle + V(0.075f, 0, 0), 0.008f, 0.008f, 8);

            BuildDrivetrain(blackMat, metalMat, chainMat, crankMat, seatDir);
            BuildWheel(AvatarNode.RearWheel, rear: true);
            BuildWheel(AvatarNode.FrontWheel, rear: false);
        }

        private void BuildWheel(AvatarNode node, bool rear)
        {
            var tyreMat = Mat("tyre", V(0.045f, 0.045f, 0.045f), 0.15f);
            var rimMat = Mat("rim", V(0.09f, 0.09f, 0.1f), 0.65f);
            var decalMat = Mat("decal", V(0.95f, 0.95f, 0.95f), 0.6f);
            var metalMat = Mat("metal", V(0.62f, 0.64f, 0.67f), 0.8f, 0.6f);
            var spokeMat = Mat("spokes", V(0.12f, 0.12f, 0.13f), 0.5f);

            B(node, tyreMat).Torus(Vector3.Zero, Vector3.UnitX, WheelRadius - 0.014f, 0.014f, 0.0125f, 56, 10);
            B(node, rimMat).Torus(Vector3.Zero, Vector3.UnitX, 0.29f, 0.028f, 0.0105f, 56, 10);
            // Rim logos: two arcs on each sidewall (like the brand lettering on deep carbon rims).
            var decal = B(node, decalMat);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k < 2; k++)
                {
                    float start = (k * MathF.PI) + 0.35f;
                    var centers = new Vector3[13];
                    var radii = new Vector2[13];
                    for (int i = 0; i < centers.Length; i++)
                    {
                        float a = start + (0.75f * i / (centers.Length - 1));
                        centers[i] = V(side * 0.0104f, MathF.Cos(a) * 0.291f, MathF.Sin(a) * 0.291f);
                        radii[i] = R(0.0007f, i == 0 || i == centers.Length - 1 ? 0.003f : 0.0065f);
                    }

                    decal.Sweep(centers, radii, Vector3.UnitX, 6);
                }
            }
            var metal = B(node, metalMat);
            float hubHalf = rear ? 0.07f : 0.05f;
            metal.Tube(V(-hubHalf, 0, 0), V(hubHalf, 0, 0), 0.016f, 0.016f, 12);
            // Hub flanges: on the rear the drive-side flange sits inboard of the cassette.
            float FlangeX(float side) => rear ? (side > 0 ? 0.016f : -0.036f) : side * 0.034f;
            for (int s = -1; s <= 1; s += 2) metal.Torus(V(FlangeX(s), 0, 0), Vector3.UnitX, 0.024f, 0.005f, 0.002f, 16, 5);
            var spokes = B(node, spokeMat);
            for (int k = 0; k < 24; k++)
            {
                float a = k * MathF.PI * 2f / 24;
                float side = k % 2 == 0 ? 1f : -1f;
                float lead = side * 0.12f;
                var hub = V(FlangeX(side), MathF.Cos(a + lead) * 0.026f, MathF.Sin(a + lead) * 0.026f);
                var rimPoint = V(side * 0.004f, MathF.Cos(a) * 0.264f, MathF.Sin(a) * 0.264f);
                spokes.Tube(hub, rimPoint, 0.0011f, 0.0011f, 4, false);
            }

            // Disc brake rotor (160 mm) on the left: braking track with cooling holes, six-arm spider.
            var rotorX = rear ? -0.058f : -0.046f;
            Sdf rotor = p =>
            {
                float r = MathF.Sqrt((p.Y * p.Y) + (p.Z * p.Z));
                float a = MathF.Atan2(p.Z, p.Y);
                float track = MathF.Abs(r - 0.07f) - 0.01f;
                float hole = float.MaxValue;
                for (int ring = 0; ring < 2; ring++)
                {
                    float hr = ring == 0 ? 0.066f : 0.074f;
                    float step = MathF.PI * 2f / 24f;
                    float offset = ring * step / 2f;
                    float ha = (MathF.Round((a - offset) / step) * step) + offset;
                    var hc = new Vector2(MathF.Cos(ha) * hr, MathF.Sin(ha) * hr);
                    hole = MathF.Min(hole, Vector2.Distance(new Vector2(p.Y, p.Z), hc) - 0.0022f);
                }

                float arms = float.MaxValue;
                for (int i = 0; i < 6; i++)
                {
                    float aa = i * MathF.PI / 3f;
                    var from = new Vector2(MathF.Cos(aa) * 0.018f, MathF.Sin(aa) * 0.018f);
                    var to = new Vector2(MathF.Cos(aa + 0.35f) * 0.064f, MathF.Sin(aa + 0.35f) * 0.064f);
                    var q = new Vector2(p.Y, p.Z) - from;
                    var d = to - from;
                    float h = Math.Clamp(Vector2.Dot(q, d) / d.LengthSquared(), 0f, 1f);
                    arms = MathF.Min(arms, (q - (d * h)).Length() - 0.0045f);
                }

                float hub = r - 0.022f;
                float plane = MathF.Min(MathF.Max(track, -hole), MathF.Min(arms, hub));
                return MathF.Max(plane, MathF.Abs(p.X - rotorX) - 0.0009f);
            };
            Sculpt(node, rotor, V(rotorX - 0.004f, -0.085f, -0.085f), V(rotorX + 0.004f, 0.085f, 0.085f), 0.0009f, 1600, metalMat);
        }
    }
}
