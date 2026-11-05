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
            var tapeMat = Mat("bartape", V(0.045f, 0.045f, 0.047f), 0.18f);
            var metalMat = Mat("metal", V(0.62f, 0.64f, 0.67f), 0.8f, 0.6f);
            var chainMat = Mat("chain", V(0.42f, 0.43f, 0.45f), 0.7f, 0.6f);
            var crankMat = Mat("crank", V(0.1f, 0.1f, 0.11f), 0.6f);
            var leverMat = Mat("lever", V(0.1f, 0.1f, 0.11f), 0.8f);
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
            BuildCockpit(blackMat, tapeMat, metalMat);
            var lever = B(AvatarNode.Bike, leverMat);
            for (int s = -1; s <= 1; s += 2)
            {
                BuildShifter(s, blackMat, leverMat);
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
            metal.Tube(FrontAxle + V(-0.062f, 0, 0), FrontAxle + V(0.062f, 0, 0), 0.0065f, 0.0065f, 10);
            var capMat = Mat("axlecap", V(0.05f, 0.05f, 0.055f), 0.55f);
            foreach (var (axle, half) in new[] { (FrontAxle, 0.0625f), (RearAxle, 0.0795f) })
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    // Round thru-axle end cap with a hex socket (dark) instead of a bolt head.
                    var c = axle + V(s * half, 0, 0);
                    Sculpt(
                        AvatarNode.Bike,
                        p => MathF.Max(MathF.Max(new Vector2(p.Y - c.Y, p.Z - c.Z).Length() - 0.0105f, MathF.Abs(p.X - c.X) - 0.0028f), -MathF.Max(HexDistance(p.Y - c.Y, p.Z - c.Z) - 0.0035f, (s * (c.X - p.X)) + 0.0015f)),
                        c - V(0.006f, 0.014f, 0.014f),
                        c + V(0.006f, 0.014f, 0.014f),
                        0.0006f,
                        260,
                        capMat);
                }
            }
            metal.Tube(RearAxle + V(-0.078f, 0, 0), RearAxle + V(0.078f, 0, 0), 0.0065f, 0.0065f, 10);

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

            BuildRotor(node, rear ? RearRotorX : FrontRotorX, metalMat);
        }

        /// <summary>Distance-like value of a regular hexagon (across-flats = 2 × result + 2r).</summary>
        private static float HexDistance(float x, float y)
        {
            x = MathF.Abs(x);
            y = MathF.Abs(y);
            return MathF.Max((x * 0.866025f) + (y * 0.5f), y);
        }
    }
}
