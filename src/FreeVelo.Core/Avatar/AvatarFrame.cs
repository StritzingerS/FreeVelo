using System;
using System.Collections.Generic;
using System.Numerics;
using static FreeVelo.Core.Avatar.SdfShapes;

namespace FreeVelo.Core.Avatar
{
    /// <summary>Sculpted carbon frame, fork and saddle (smooth fillets at the tube junctions, aero tube profiles).</summary>
    public sealed partial class AvatarModel
    {
        /// <summary>Round cone whose cross-section is narrowed sideways (aero profile) around x = <paramref name="centerX"/>.</summary>
        private static Sdf Aero(Vector3 a, Vector3 b, float ra, float rb, float width, float centerX = 0f) =>
            Squashed(RoundCone(a, b, ra, rb), new Vector3(centerX, 0, 0), Vector3.UnitX, width);

        private void BuildFrame(AvatarMaterial frameMat, AvatarMaterial decalMat, Vector3 seatDir, Vector3 downDir)
        {
            var downStart = HeadBottom + V(0, 0.035f, -0.012f);
            var downEnd = BottomBracket + V(0, 0.025f, 0.035f);
            var main = Blend(
                0.028f,
                Aero(HeadBottom + V(0, -0.012f, 0.004f), HeadTop + V(0, 0.012f, -0.004f), 0.031f, 0.025f, 0.72f), // head tube
                Aero(HeadTop + V(0, -0.03f, -0.01f), SeatCluster + V(0, -0.012f, 0.005f), 0.021f, 0.016f, 0.72f), // top tube
                Aero(downStart, downEnd, 0.03f, 0.033f, 0.7f), // down tube
                Aero(BottomBracket + (seatDir * 0.03f), SeatCluster + (seatDir * 0.012f), 0.025f, 0.019f, 0.68f), // seat tube
                RoundCone(BottomBracket + V(-0.043f, 0, 0), BottomBracket + V(0.043f, 0, 0), 0.029f, 0.029f), // BB shell
                Ellipsoid(V(0, 0.664f, 0.447f), V(0.05f, 0.022f, 0.036f))); // fork crown

            var stays = new List<Sdf> { main };
            var stayTop = BottomBracket + (seatDir * 0.47f);
            for (int s = -1; s <= 1; s += 2)
            {
                // Stays and fork blades run outside the rotor plane within the rotor radius (≈ 9 cm around the axle).
                var csBend = RearAxle + V(s * 0.0735f, 0.004f, 0.13f);
                stays.Add(Blend(0.01f, Aero(BottomBracket + V(s * 0.035f, -0.005f, -0.035f), csBend, 0.017f, 0.011f, 0.7f, s * 0.055f), Aero(csBend, RearAxle + V(s * 0.0735f, -0.004f, 0.012f), 0.011f, 0.0095f, 0.7f, s * 0.0735f))); // chain stay
                var ssBend = RearAxle + V(s * 0.072f, 0.115f, 0.06f);
                stays.Add(Blend(0.01f, Aero(stayTop + V(s * 0.016f, 0, -0.012f), ssBend, 0.012f, 0.009f, 0.75f, s * 0.045f), Aero(ssBend, RearAxle + V(s * 0.0725f, 0.018f, 0.004f), 0.009f, 0.008f, 0.75f, s * 0.0725f))); // dropped seat stay
                stays.Add(RoundBox(RearAxle + V(s * 0.0745f, 0.006f, 0.002f), V(0.0045f, 0.024f, 0.022f), 0.004f)); // rear dropout (142 mm spacing)
                var forkBend = FrontAxle + V(s * 0.0565f, 0.12f, -0.024f);
                stays.Add(Blend(0.012f, Aero(V(s * 0.036f, 0.655f, 0.45f), forkBend, 0.018f, 0.012f, 0.6f, s * 0.047f), Aero(forkBend, FrontAxle + V(s * 0.0565f, 0.012f, -0.002f), 0.012f, 0.009f, 0.6f, s * 0.0565f))); // fork blade
                stays.Add(RoundBox(FrontAxle + V(s * 0.056f, 0.004f, 0f), V(0.0055f, 0.02f, 0.018f), 0.005f)); // fork dropout (100 mm)
            }

            var frame = Blend(0.015f, stays.ToArray());

            float downLen = Vector3.Distance(downStart, downEnd);
            Sdf AlongDown(float from) => p =>
            {
                float t = Vector3.Dot(p - downStart, downDir);
                return MathF.Max((downLen * from) - t, Vector3.Distance(p, downStart + (downDir * t)) - 0.05f);
            };

            var seatStart = BottomBracket + (seatDir * 0.03f);
            Sdf AlongSeat(float from) => p =>
            {
                float t = Vector3.Dot(p - seatStart, seatDir);
                return MathF.Max(from - t, Vector3.Distance(p, seatStart + (seatDir * t)) - 0.04f);
            };

            Sculpt(
                AvatarNode.Bike,
                frame,
                V(-0.1f, 0.22f, -0.46f),
                V(0.1f, 0.88f, 0.63f),
                0.004f,
                7000,
                frameMat,
                (AlongDown(0.3f), decalMat), // logo band on the down tube
                (AlongDown(0.56f), frameMat),
                (AlongSeat(0.38f), decalMat), // stripe on the seat tube
                (AlongSeat(0.405f), frameMat),
                (Sphere(Vector3.Lerp(HeadBottom, HeadTop, 0.55f) + V(0, 0, 0.03f), 0.014f), decalMat)); // head badge
        }

        private void BuildSaddle(AvatarMaterial saddleMat, AvatarMaterial accentMat)
        {
            var saddle = Blend(
                0.03f,
                Ellipsoid(V(0, 0.99f, -0.252f), V(0.069f, 0.018f, 0.058f)),
                Ellipsoid(V(0, 0.988f, -0.165f), V(0.042f, 0.016f, 0.09f)),
                Ellipsoid(V(0, 0.985f, -0.088f), V(0.021f, 0.013f, 0.05f)));
            saddle = SmoothIntersect(saddle, Plane(V(0, 0.976f, 0), -Vector3.UnitY), 0.004f);
            saddle = SmoothSubtract(saddle, RoundBox(V(0, 1.004f, -0.2f), V(0.009f, 0.02f, 0.052f), 0.008f), 0.004f); // relief channel
            Sculpt(
                AvatarNode.Bike,
                saddle,
                V(-0.085f, 0.955f, -0.33f),
                V(0.085f, 1.025f, -0.02f),
                0.0025f,
                1100,
                saddleMat,
                (p => p.Y - 0.982f, accentMat)); // dark base shell
        }
    }
}
