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

            var frame = B(AvatarNode.Bike, frameMat);
            var seatDir = Vector3.Normalize(SeatCluster - BottomBracket);

            // Aero frame: elliptical tubes (narrow sideways, deeper in the direction of travel).
            frame.EllipticTube(HeadTop + V(0, -0.025f, -0.005f), SeatCluster + V(0, -0.015f, 0), R(0.016f, 0.02f), R(0.014f, 0.017f), Side); // top tube
            frame.EllipticTube(HeadBottom + V(0, 0.03f, -0.01f), BottomBracket + V(0, 0.02f, 0.03f), R(0.022f, 0.032f), R(0.03f, 0.03f), Side); // down tube
            frame.EllipticTube(BottomBracket + (seatDir * 0.02f), SeatCluster, R(0.018f, 0.024f), R(0.015f, 0.022f), Side); // seat tube
            frame.EllipticTube(HeadBottom - V(0, 0.01f, -0.003f), HeadTop + V(0, 0.015f, -0.004f), R(0.024f, 0.03f), R(0.02f, 0.025f), Side); // head tube
            frame.Tube(BottomBracket + V(-0.04f, 0, 0), BottomBracket + V(0.04f, 0, 0), 0.026f, 0.026f, 14); // BB shell
            var stayTop = BottomBracket + (seatDir * 0.45f);
            for (int s = -1; s <= 1; s += 2)
            {
                frame.EllipticTube(stayTop + V(s * 0.018f, 0, -0.01f), RearAxle + V(s * 0.058f, 0.01f, 0), R(0.008f, 0.012f), R(0.006f, 0.009f), Side); // dropped seat stay
                frame.EllipticTube(BottomBracket + V(s * 0.035f, 0, -0.03f), RearAxle + V(s * 0.062f, 0, 0), R(0.01f, 0.017f), R(0.007f, 0.011f), Side); // chain stay
                frame.EllipticTube(V(s * 0.034f, 0.665f, 0.442f), FrontAxle + V(s * 0.052f, 0.01f, 0), R(0.011f, 0.022f), R(0.007f, 0.012f), Side); // fork blade
                frame.Box(RearAxle + V(s * 0.06f, 0, 0), V(0.012f, 0.05f, 0.04f), Rot.Identity); // dropout
                frame.Box(FrontAxle + V(s * 0.053f, 0, 0), V(0.01f, 0.04f, 0.035f), Rot.Identity);
            }

            frame.Sweep(new[] { V(0, 0.655f, 0.448f), V(0, 0.68f, 0.44f) }, new[] { R(0.045f, 0.028f), R(0.035f, 0.025f) }, Side, 14); // fork crown

            // Decal band on the down tube
            B(AvatarNode.Bike, decalMat).EllipticTube(
                Vector3.Lerp(HeadBottom, BottomBracket, 0.35f), Vector3.Lerp(HeadBottom, BottomBracket, 0.55f), R(0.0245f, 0.0335f), R(0.0255f, 0.0335f), Side);

            // Seatpost, saddle with rails
            var black = B(AvatarNode.Bike, blackMat);
            var postTop = SeatCluster + (seatDir * 0.16f);
            black.EllipticTube(SeatCluster - (seatDir * 0.02f), postTop, R(0.012f, 0.019f), R(0.012f, 0.019f), Side);
            black.Box(postTop + V(0, 0.012f, 0), V(0.04f, 0.02f, 0.06f), Rot.Identity);
            black.Sweep(
                new[] { V(0, 0.982f, -0.06f), V(0, 0.986f, -0.11f), V(0, 0.99f, -0.18f), V(0, 0.993f, -0.25f), V(0, 0.99f, -0.3f) },
                new[] { R(0.015f, 0.01f), R(0.026f, 0.014f), R(0.05f, 0.017f), R(0.07f, 0.019f), R(0.06f, 0.016f) },
                Side, 14);
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
            var downDir = Vector3.Normalize(BottomBracket - HeadBottom);
            var normal = Vector3.Normalize(Vector3.Cross(downDir, Vector3.UnitX));
            if (normal.Y < 0) normal = -normal;
            var b0 = Vector3.Lerp(HeadBottom, BottomBracket, 0.62f) + (normal * 0.068f);
            var b1 = Vector3.Lerp(HeadBottom, BottomBracket, 0.3f) + (normal * 0.068f);
            var bottleDir = Vector3.Normalize(b1 - b0);
            B(AvatarNode.Bike, bottleMat).Sweep(
                new[] { b0, b0 + (bottleDir * 0.05f), b0 + (bottleDir * 0.17f), b0 + (bottleDir * 0.2f), b0 + (bottleDir * 0.215f) },
                new[] { R(0.033f, 0.033f), R(0.036f, 0.036f), R(0.036f, 0.036f), R(0.024f, 0.024f), R(0.012f, 0.012f) },
                Side, 14);
            black.Path(new[] { b0 + (bottleDir * 0.02f) - (normal * 0.03f), b0 + (bottleDir * 0.02f) + (normal * 0.005f) }, 0.004f, 5);
            black.Path(new[] { b0 + (bottleDir * 0.16f) - (normal * 0.03f), b0 + (bottleDir * 0.16f) + (normal * 0.005f) }, 0.004f, 5);

            // Brake calipers (flat mount), thru-axle ends
            black.Box(FrontAxle + V(-0.058f, 0.07f, -0.045f), V(0.022f, 0.045f, 0.06f), Rot.Euler(-35, 0, 0));
            black.Box(RearAxle + V(-0.058f, 0.045f, 0.06f), V(0.022f, 0.045f, 0.06f), Rot.Euler(25, 0, 0));
            metal.Tube(FrontAxle + V(-0.07f, 0, 0), FrontAxle + V(0.07f, 0, 0), 0.008f, 0.008f, 8);
            metal.Tube(RearAxle + V(-0.075f, 0, 0), RearAxle + V(0.075f, 0, 0), 0.008f, 0.008f, 8);

            // Drivetrain: chain, front and rear derailleur
            var chain = B(AvatarNode.Bike, chainMat);
            chain.Tube(BottomBracket + V(0.068f, 0.098f, -0.005f), RearAxle + V(0.068f, 0.048f, 0), 0.0045f, 0.0045f, 5);
            chain.Tube(BottomBracket + V(0.068f, -0.098f, -0.005f), RearAxle + V(0.07f, -0.095f, 0.03f), 0.0045f, 0.0045f, 5);
            var fd = BottomBracket + (seatDir * 0.155f) + V(0.035f, 0, -0.005f);
            black.Box(fd, V(0.025f, 0.03f, 0.06f), Rot.LookRotation(Vector3.Cross(Vector3.UnitX, seatDir), seatDir));
            black.Box(RearAxle + V(0.072f, -0.035f, 0.005f), V(0.016f, 0.05f, 0.03f), Rot.Euler(10, 0, 0));
            black.Box(RearAxle + V(0.078f, -0.075f, 0.015f), V(0.008f, 0.075f, 0.024f), Rot.Euler(-12, 0, 0));
            metal.Torus(RearAxle + V(0.071f, -0.045f, 0.01f), Vector3.UnitX, 0.012f, 0.004f, 0.003f, 12, 5);
            metal.Torus(RearAxle + V(0.071f, -0.1f, 0.025f), Vector3.UnitX, 0.012f, 0.004f, 0.003f, 12, 5);

            BuildWheel(AvatarNode.RearWheel, rear: true);
            BuildWheel(AvatarNode.FrontWheel, rear: false);

            // Crankset (local to the bottom bracket): right arm up at angle 0, left arm opposite.
            var crank = B(AvatarNode.Crankset, crankMat);
            crank.Tube(V(-0.09f, 0, 0), V(0.09f, 0, 0), 0.012f, 0.012f, 10);
            crank.Sweep(new[] { V(0.088f, -0.015f, 0), V(0.09f, CrankLength * 0.5f, 0), V(0.09f, CrankLength + 0.012f, 0) }, new[] { R(0.009f, 0.02f), R(0.008f, 0.015f), R(0.008f, 0.012f) }, Side, 8);
            crank.Sweep(new[] { V(-0.088f, 0.015f, 0), V(-0.09f, -CrankLength * 0.5f, 0), V(-0.09f, -CrankLength - 0.012f, 0) }, new[] { R(0.009f, 0.02f), R(0.008f, 0.015f), R(0.008f, 0.012f) }, Side, 8);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * MathF.PI / 2f) + 0.4f;
                crank.EllipticTube(V(0.075f, 0, 0), V(0.072f, MathF.Cos(a) * 0.075f, MathF.Sin(a) * 0.075f), R(0.004f, 0.012f), R(0.004f, 0.008f), Side, 6);
            }

            var rings = B(AvatarNode.Crankset, chainMat);
            rings.Torus(V(0.07f, 0, 0), Vector3.UnitX, 0.098f, 0.009f, 0.0025f, 48, 6);
            rings.Torus(V(0.064f, 0, 0), Vector3.UnitX, 0.072f, 0.008f, 0.0025f, 40, 6);

            // Pedals (local to the pedal axle)
            for (int side = 0; side < 2; side++)
            {
                var node = side == 0 ? AvatarNode.PedalRight : AvatarNode.PedalLeft;
                float sx = side == 0 ? 1f : -1f;
                B(node, blackMat).Sweep(new[] { V(0, 0, -0.04f), V(0, 0.002f, 0), V(0, 0, 0.045f) }, new[] { R(0.032f, 0.008f), R(0.036f, 0.01f), R(0.03f, 0.007f) }, Side, 8);
                B(node, metalMat).Tube(V(-sx * 0.045f, 0, 0), Vector3.Zero, 0.006f, 0.006f, 6);
            }
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
            B(node, decalMat).Torus(Vector3.Zero, Vector3.UnitX, 0.296f, 0.007f, 0.0112f, 56, 6);
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

            if (rear)
            {
                for (int i = 0; i < 6; i++) metal.Torus(V(0.042f + (i * 0.0045f), 0, 0), Vector3.UnitX, 0.05f - (i * 0.0055f), 0.006f, 0.0015f, 28, 4);
            }
        }
    }
}
