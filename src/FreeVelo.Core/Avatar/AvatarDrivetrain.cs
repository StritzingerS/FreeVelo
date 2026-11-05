using System;
using System.Collections.Generic;
using System.Numerics;
using static FreeVelo.Core.Avatar.SdfShapes;

namespace FreeVelo.Core.Avatar
{
    /// <summary>
    /// Drivetrain and small bike parts: toothed chainrings and 11-speed cassette, a chain made of links that
    /// follows the real path (ring → cog → jockey wheels), derailleurs, crank arms, pedals, calipers, computer.
    /// </summary>
    public sealed partial class AvatarModel
    {
        private const float ChainPitch = 0.0127f;
        private const float ChainLineX = 0.07f;
        private static readonly int[] CassetteTeeth = { 28, 25, 23, 21, 19, 17, 15, 14, 13, 12, 11 };
        private const int ChainCog = 7; // 14T, in line with the big ring
        private static Vector3 UpperJockey => RearAxle + new Vector3(0, -0.052f, 0.014f);
        private static Vector3 LowerJockey => RearAxle + new Vector3(0, -0.108f, 0.036f);

        private static float PitchRadius(int teeth) => ChainPitch / (2f * MathF.Sin(MathF.PI / teeth));

        private static float CogX(int index) => 0.042f + (index * 0.0039f);

        /// <summary>Toothed sprocket around an axis parallel to x through <paramref name="center"/>.</summary>
        private static Sdf Sprocket(Vector3 center, int teeth, float thickness, float innerRadius, float toothHeight = 0.0042f)
        {
            float rp = PitchRadius(teeth);
            return p =>
            {
                float dy = p.Y - center.Y, dz = p.Z - center.Z;
                float r = MathF.Sqrt((dy * dy) + (dz * dz));
                float phase = MathF.Atan2(dz, dy) * teeth / (2f * MathF.PI);
                float f = MathF.Abs(phase - MathF.Floor(phase) - 0.5f) * 2f; // 0 at a tooth tip, 1 in the valley
                float outer = rp + toothHeight - (toothHeight * 1.85f * f * f);
                float radial = MathF.Max(r - outer, innerRadius - r);
                return MathF.Max(radial, MathF.Abs(p.X - center.X) - (thickness / 2f));
            };
        }

        private void BuildDrivetrain(AvatarMaterial blackMat, AvatarMaterial metalMat, AvatarMaterial chainMat, AvatarMaterial crankMat, Vector3 seatDir)
        {
            var ringMat = Mat("chainring", V(0.14f, 0.14f, 0.15f), 0.7f, 0.5f);

            // Crankset (local to the bottom bracket): right arm up at angle 0, left arm opposite.
            var bigRing = Sprocket(V(ChainLineX, 0, 0), 50, 0.003f, PitchRadius(50) - 0.013f);
            var smallRing = Sprocket(V(ChainLineX - 0.0065f, 0, 0), 34, 0.003f, PitchRadius(34) - 0.011f);
            Sculpt(AvatarNode.Crankset, Union(bigRing, smallRing), V(0.056f, -0.112f, -0.112f), V(0.078f, 0.112f, 0.112f), 0.0011f, 3200, ringMat);

            var crankParts = new List<Sdf>
            {
                RoundCone(V(-0.092f, 0, 0), V(0.092f, 0, 0), 0.0125f, 0.0125f), // spindle
                Squashed(RoundCone(V(0.086f, 0, 0), V(0.089f, CrankLength, 0), 0.02f, 0.0125f), V(0.088f, 0, 0), Vector3.UnitX, 0.5f),
                Squashed(RoundCone(V(-0.086f, 0, 0), V(-0.089f, -CrankLength, 0), 0.02f, 0.0125f), V(-0.088f, 0, 0), Vector3.UnitX, 0.5f),
                RoundCone(V(0.082f, CrankLength, 0), V(0.095f, CrankLength, 0), 0.012f, 0.012f), // pedal eyes
                RoundCone(V(-0.082f, -CrankLength, 0), V(-0.095f, -CrankLength, 0), 0.012f, 0.012f),
                Sphere(V(0.08f, 0, 0), 0.024f), // spider hub
            };
            for (int i = 0; i < 4; i++)
            {
                float a = (i * MathF.PI / 2f) + (MathF.PI / 4f);
                var tip = V(ChainLineX + 0.002f, MathF.Cos(a) * (PitchRadius(50) - 0.012f), MathF.Sin(a) * (PitchRadius(50) - 0.012f));
                crankParts.Add(Squashed(RoundCone(V(0.08f, 0, 0), tip, 0.011f, 0.007f), V(ChainLineX + 0.004f, 0, 0), Vector3.UnitX, 0.45f));
            }

            Sculpt(AvatarNode.Crankset, Blend(0.012f, crankParts.ToArray()), V(-0.11f, -0.2f, -0.11f), V(0.11f, 0.2f, 0.11f), 0.0022f, 1800, crankMat);

            // Cassette (on the rear wheel) and freehub body.
            var cogs = new List<Sdf> { RoundCone(V(0.036f, 0, 0), V(0.088f, 0, 0), 0.0165f, 0.0165f) };
            for (int i = 0; i < CassetteTeeth.Length; i++) cogs.Add(Sprocket(V(CogX(i), 0, 0), CassetteTeeth[i], 0.0022f, 0.012f, 0.0028f));
            Sculpt(AvatarNode.RearWheel, Union(cogs.ToArray()), V(0.034f, -0.064f, -0.064f), V(0.09f, 0.064f, 0.064f), 0.001f, 3200, metalMat);

            BuildChain(chainMat);
            BuildDerailleurs(blackMat, metalMat, seatDir);

            // Brake calipers (flat mount) with a bit of shape
            Sdf Caliper(Vector3 c, float pitch) => Transformed(
                Blend(0.006f, RoundBox(Vector3.Zero, V(0.012f, 0.021f, 0.031f), 0.007f), RoundBox(V(0.004f, 0.008f, 0), V(0.01f, 0.012f, 0.02f), 0.005f)),
                c,
                Rot.Euler(pitch, 0, 0));
            var front = FrontAxle + V(-0.058f, 0.07f, -0.045f);
            var rear = RearAxle + V(-0.058f, 0.045f, 0.06f);
            Sculpt(AvatarNode.Bike, Caliper(front, -35f), front - V(0.04f, 0.05f, 0.05f), front + V(0.04f, 0.05f, 0.05f), 0.002f, 350, blackMat);
            Sculpt(AvatarNode.Bike, Caliper(rear, 25f), rear - V(0.04f, 0.05f, 0.05f), rear + V(0.04f, 0.05f, 0.05f), 0.002f, 350, blackMat);

            // Pedals (local to the pedal axle): road pedal body, steel spindle towards the crank.
            for (int side = 0; side < 2; side++)
            {
                var node = side == 0 ? AvatarNode.PedalRight : AvatarNode.PedalLeft;
                float sx = side == 0 ? 1f : -1f;
                var body = Blend(
                    0.008f,
                    RoundBox(V(0, -0.006f, -0.008f), V(0.033f, 0.009f, 0.036f), 0.006f),
                    Ellipsoid(V(0, -0.006f, 0.03f), V(0.022f, 0.008f, 0.025f)),
                    RoundCone(V(-sx * 0.03f, 0, 0), V(sx * 0.03f, 0, 0), 0.011f, 0.011f));
                Sculpt(node, body, V(-0.05f, -0.025f, -0.06f), V(0.05f, 0.015f, 0.065f), 0.002f, 450, blackMat);
                B(node, metalMat).Tube(V(-sx * 0.045f, 0, 0), V(-sx * 0.02f, 0, 0), 0.0065f, 0.0065f, 8);
            }

            // Bike computer on an out-front mount.
            var computerCenter = V(0, 0.889f, 0.548f);
            var computerRot = Rot.Euler(-12, 0, 0);
            var computer = Blend(
                0.006f,
                Transformed(RoundBox(Vector3.Zero, V(0.026f, 0.008f, 0.04f), 0.006f), computerCenter, computerRot),
                Squashed(RoundCone(V(0, 0.874f, 0.482f), V(0, 0.878f, 0.54f), 0.007f, 0.007f), Vector3.Zero, Vector3.UnitX, 0.8f));
            var inv = computerRot.Transposed;
            Sdf screen = p =>
            {
                var q = inv * (p - computerCenter);
                return MathF.Max(MathF.Max(MathF.Abs(q.X) - 0.02f, MathF.Abs(q.Z + 0.003f) - 0.03f), 0.0055f - q.Y);
            };
            Sculpt(AvatarNode.Bike, computer, V(-0.04f, 0.86f, 0.47f), V(0.04f, 0.92f, 0.6f), 0.0016f, 700, blackMat, (screen, Mat("screen", V(0.03f, 0.05f, 0.06f), 0.92f)));
        }

        private void BuildChain(AvatarMaterial chainMat)
        {
            // Circles in the (z, y) plane with the direction the chain wraps them (+1 counter-clockwise seen with z to the right).
            var circles = new (Vector2 C, float R, int W)[]
            {
                (new Vector2(BottomBracket.Z, BottomBracket.Y), PitchRadius(50), 1),
                (new Vector2(RearAxle.Z, RearAxle.Y), PitchRadius(CassetteTeeth[ChainCog]), 1),
                (new Vector2(UpperJockey.Z, UpperJockey.Y), PitchRadius(11), -1),
                (new Vector2(LowerJockey.Z, LowerJockey.Y), PitchRadius(11), -1),
            };

            static Vector2 Rot90(Vector2 v) => new Vector2(-v.Y, v.X);

            // Tangent segments between consecutive circles.
            int n = circles.Length;
            var leave = new Vector2[n];
            var arrive = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var a = circles[i];
                var b = circles[(i + 1) % n];
                var d = b.C - a.C;
                float k = (b.W * b.R) - (a.W * a.R);
                float l2 = d.LengthSquared();
                float t = MathF.Sqrt(MathF.Max(0f, l2 - (k * k)));
                var dir = ((d * t) - (Rot90(d) * k)) / l2;
                leave[i] = a.C - (Rot90(dir) * (a.W * a.R));
                arrive[(i + 1) % n] = b.C - (Rot90(dir) * (b.W * b.R));
            }

            // Dense closed path: arc on each circle from arrival to departure, then the straight run.
            var path = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                var c = circles[i];
                float a0 = MathF.Atan2(arrive[i].Y - c.C.Y, arrive[i].X - c.C.X);
                float a1 = MathF.Atan2(leave[i].Y - c.C.Y, leave[i].X - c.C.X);
                float sweep = a1 - a0;
                if (c.W > 0) { while (sweep < 0) sweep += 2f * MathF.PI; }
                else { while (sweep > 0) sweep -= 2f * MathF.PI; }
                int steps = Math.Max(2, (int)(MathF.Abs(sweep) * c.R / 0.002f));
                for (int s = 0; s <= steps; s++)
                {
                    float a = a0 + (sweep * s / steps);
                    path.Add(c.C + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * c.R));
                }
            }

            path.Add(path[0]);

            // Links every pitch along the path: alternating inner/outer plate pairs and rollers.
            var chain = B(AvatarNode.Bike, chainMat);
            var pins = new List<Vector2>();
            float carry = 0f;
            pins.Add(path[0]);
            for (int i = 1; i < path.Count; i++)
            {
                float seg = Vector2.Distance(path[i - 1], path[i]);
                while (carry + seg >= ChainPitch)
                {
                    float t = (ChainPitch - carry) / seg;
                    var p = Vector2.Lerp(path[i - 1], path[i], t);
                    pins.Add(p);
                    seg -= ChainPitch - carry;
                    path[i - 1] = p;
                    carry = 0f;
                }

                carry += seg;
            }

            if (Detail < 0.5f)
            {
                // Low detail: the chain as a thin tube along the same path.
                var points = new List<Vector3>();
                for (int i = 0; i < pins.Count; i += 2) points.Add(new Vector3(ChainLineX, pins[i].Y, pins[i].X));
                points.Add(points[0]);
                chain.Path(points, 0.0045f, 5);
                return;
            }

            for (int i = 0; i < pins.Count - 1; i++)
            {
                var a = new Vector3(ChainLineX, pins[i].Y, pins[i].X);
                var b = new Vector3(ChainLineX, pins[i + 1].Y, pins[i + 1].X);
                var dir = Vector3.Normalize(b - a);
                var rot = Rot.LookRotation(dir, Vector3.Cross(dir, Vector3.UnitX));
                float offset = i % 2 == 0 ? 0.0047f : 0.0036f;
                var mid = (a + b) / 2f;
                chain.Box(mid + V(offset, 0, 0), V(0.0009f, 0.0085f, ChainPitch + 0.004f), rot);
                chain.Box(mid - V(offset, 0, 0), V(0.0009f, 0.0085f, ChainPitch + 0.004f), rot);
                chain.Tube(a - V(0.0035f, 0, 0), a + V(0.0035f, 0, 0), 0.0036f, 0.0036f, 6, false);
            }
        }

        private void BuildDerailleurs(AvatarMaterial blackMat, AvatarMaterial metalMat, Vector3 seatDir)
        {
            // Rear derailleur: knuckle, parallelogram, cage plates around two toothed jockey wheels.
            static float Capsule2D(Vector3 p, Vector3 a, Vector3 b, float r)
            {
                var pa = new Vector2(p.Z - a.Z, p.Y - a.Y);
                var ba = new Vector2(b.Z - a.Z, b.Y - a.Y);
                float h = Math.Clamp(Vector2.Dot(pa, ba) / ba.LengthSquared(), 0f, 1f);
                return (pa - (ba * h)).Length() - r;
            }

            Sdf Plate(float x, float thickness, float radius) => p => MathF.Max(Capsule2D(p, UpperJockey, LowerJockey, radius), MathF.Abs(p.X - x) - (thickness / 2f));
            var rd = Blend(
                0.006f,
                RoundBox(RearAxle + V(0.076f, -0.018f, -0.006f), V(0.008f, 0.02f, 0.012f), 0.005f), // hanger knuckle
                Transformed(RoundBox(Vector3.Zero, V(0.011f, 0.024f, 0.009f), 0.005f), RearAxle + V(0.082f, -0.038f, 0.014f), Rot.Euler(-35, 0, 0)), // parallelogram
                RoundBox(UpperJockey + V(0.08f, 0.006f, -0.004f), V(0.008f, 0.012f, 0.012f), 0.005f)); // cage pivot
            rd = Union(rd, Plate(ChainLineX + 0.0085f, 0.0025f, 0.017f), Plate(ChainLineX - 0.008f, 0.002f, 0.0135f));
            var rdMin = Vector3.Min(UpperJockey, LowerJockey) - V(0, 0.04f, 0.05f);
            var rdMax = RearAxle + V(0, 0.01f, 0.07f);
            Sculpt(AvatarNode.Bike, rd, new Vector3(0.055f, rdMin.Y, rdMin.Z), new Vector3(0.097f, rdMax.Y, rdMax.Z), 0.0015f, 1600, blackMat);
            var jockeys = Union(Sprocket(UpperJockey + V(ChainLineX, 0, 0), 11, 0.0035f, 0.004f), Sprocket(LowerJockey + V(ChainLineX, 0, 0), 11, 0.0035f, 0.004f));
            Sculpt(AvatarNode.Bike, jockeys, new Vector3(0.062f, rdMin.Y, rdMin.Z), new Vector3(0.078f, rdMax.Y, rdMax.Z), 0.0011f, 700, metalMat);

            // Front derailleur: curved cage plates above the big ring, clamped to the seat tube.
            float ring = PitchRadius(50);
            Sdf Cage(float x, float thickness, float from, float to) => p =>
            {
                float dy = p.Y - BottomBracket.Y, dz = p.Z - BottomBracket.Z;
                float r = MathF.Sqrt((dy * dy) + (dz * dz));
                float a = MathF.Atan2(dy, dz); // 0 = forward, π/2 = up
                float radial = MathF.Abs(r - (ring + 0.016f)) - 0.008f;
                float angular = MathF.Max(from - a, a - to) * r;
                return MathF.Max(MathF.Max(radial, angular), MathF.Abs(p.X - x) - (thickness / 2f));
            };
            var mount = BottomBracket + (seatDir * 0.155f);
            var fd = Blend(
                0.006f,
                Cage(ChainLineX + 0.012f, 0.0025f, 1.65f, 2.2f),
                Cage(ChainLineX - 0.01f, 0.002f, 1.75f, 2.15f),
                Transformed(RoundBox(Vector3.Zero, V(0.016f, 0.012f, 0.014f), 0.005f), mount + V(0.03f, 0.0f, -0.012f), Rot.LookRotation(Vector3.Cross(Vector3.UnitX, seatDir), seatDir)));
            Sculpt(AvatarNode.Bike, fd, V(0.05f, 0.33f, -0.12f), V(0.1f, 0.47f, 0.0f), 0.0015f, 700, blackMat);
        }
    }
}
