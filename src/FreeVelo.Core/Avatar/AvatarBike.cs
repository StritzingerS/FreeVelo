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
                black.Sweep(
                    new[] { V(s * 0.205f, 0.878f, 0.545f), V(s * 0.205f, 0.893f, 0.565f), V(s * 0.205f, 0.905f, 0.588f), V(s * 0.205f, 0.915f, 0.603f), V(s * 0.205f, 0.912f, 0.615f) },
                    new[] { R(0.016f, 0.016f), R(0.019f, 0.022f), R(0.018f, 0.022f), R(0.013f, 0.016f), R(0.008f, 0.009f) },
                    Side, 12); // hood
                lever.Sweep(
                    new[] { V(s * 0.206f, 0.9f, 0.612f), V(s * 0.207f, 0.87f, 0.627f), V(s * 0.208f, 0.83f, 0.634f), V(s * 0.208f, 0.79f, 0.632f), V(s * 0.207f, 0.755f, 0.622f) },
                    new[] { R(0.008f, 0.011f), R(0.008f, 0.013f), R(0.0075f, 0.012f), R(0.007f, 0.011f), R(0.006f, 0.009f) },
                    Side, 8); // lever blade
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
            metal.Tube(V(-0.055f, 0, 0), V(0.055f, 0, 0), 0.016f, 0.016f, 12);
            for (int s = -1; s <= 1; s += 2) metal.Torus(V(s * 0.032f, 0, 0), Vector3.UnitX, 0.024f, 0.005f, 0.002f, 16, 5); // hub flanges
            var spokes = B(node, spokeMat);
            for (int k = 0; k < 24; k++)
            {
                float a = k * MathF.PI * 2f / 24;
                float side = k % 2 == 0 ? 1f : -1f;
                float lead = side * 0.12f;
                var hub = V(side * 0.032f, MathF.Cos(a + lead) * 0.026f, MathF.Sin(a + lead) * 0.026f);
                var rimPoint = V(side * 0.004f, MathF.Cos(a) * 0.264f, MathF.Sin(a) * 0.264f);
                spokes.Tube(hub, rimPoint, 0.0011f, 0.0011f, 4, false);
            }

            // Disc brake rotor on the left, with spider
            metal.Torus(V(-0.05f, 0, 0), Vector3.UnitX, 0.07f, 0.011f, 0.001f, 40, 4);
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathF.PI / 3f;
                metal.Tube(V(-0.05f, MathF.Cos(a) * 0.018f, MathF.Sin(a) * 0.018f), V(-0.05f, MathF.Cos(a + 0.3f) * 0.062f, MathF.Sin(a + 0.3f) * 0.062f), 0.003f, 0.003f, 4, false);
            }
        }
    }
}
