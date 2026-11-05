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
        private const float ChainLineX = 0.047f; // big ring; road chainline ≈ 43.5 mm
        private static readonly int[] CassetteTeeth = { 28, 25, 23, 21, 19, 17, 15, 14, 13, 12, 11 };
        private const int ChainCog = 7; // 14T, in line with the big ring
        private static Vector3 UpperJockey => RearAxle + new Vector3(0, -0.058f, 0.012f);
        private static Vector3 LowerJockey => RearAxle + new Vector3(0, -0.126f, -0.012f);

        private static float PitchRadius(int teeth) => ChainPitch / (2f * MathF.Sin(MathF.PI / teeth));

        private static float CogX(int index) => 0.0195f + (index * 0.0039f); // between the 142 mm dropouts

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
            Sculpt(AvatarNode.Crankset, Union(bigRing, smallRing), V(ChainLineX - 0.016f, -0.112f, -0.112f), V(ChainLineX + 0.008f, 0.112f, 0.112f), 0.0011f, 3200, ringMat);

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
            var cogs = new List<Sdf> { RoundCone(V(0.014f, 0, 0), V(0.066f, 0, 0), 0.0165f, 0.0165f) };
            for (int i = 0; i < CassetteTeeth.Length; i++) cogs.Add(Sprocket(V(CogX(i), 0, 0), CassetteTeeth[i], 0.0022f, 0.012f, 0.0028f));
            Sculpt(AvatarNode.RearWheel, Union(cogs.ToArray()), V(0.012f, -0.064f, -0.064f), V(0.068f, 0.064f, 0.064f), 0.001f, 3200, metalMat);

            BuildChain(chainMat);
            BuildDerailleurs(blackMat, metalMat, seatDir);

            // Flat-mount calipers straddling the rotors: front behind the fork leg, rear on top of the chain stay.
            BuildCaliper(FrontAxle, FrontRotorX, -62f, FrontAxle + V(-0.0565f, 0.05f, -0.018f), blackMat, metalMat);
            BuildCaliper(RearAxle, RearRotorX, 72f, RearAxle + V(-0.0735f, 0.004f, 0.07f), blackMat, metalMat);

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
                (new Vector2(UpperJockey.Z, UpperJockey.Y), PitchRadius(11), -1), // chain runs down its front side
                (new Vector2(LowerJockey.Z, LowerJockey.Y), PitchRadius(11), 1), // and around the back and bottom of the tension pulley
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
                float l2 = ba.LengthSquared();
                float h = l2 < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(pa, ba) / l2, 0f, 1f);
                return (pa - (ba * h)).Length() - r;
            }

            // Cage plate: round ends around the pulleys, narrower waist, two lightening holes.
            Sdf Plate(float x, float thickness, float radius) => p =>
            {
                float ends = MathF.Min(Capsule2D(p, UpperJockey, UpperJockey, radius * 0.95f), Capsule2D(p, LowerJockey, LowerJockey, radius));
                float outline = SMin(ends, Capsule2D(p, UpperJockey, LowerJockey, radius * 0.62f), 0.012f);
                var mid = Vector3.Lerp(UpperJockey, LowerJockey, 0.5f);
                float holes = MathF.Min(Capsule2D(p, Vector3.Lerp(UpperJockey, mid, 0.55f), Vector3.Lerp(mid, LowerJockey, 0.2f), 0.0045f), Capsule2D(p, LowerJockey, LowerJockey, 0.006f));
                return MathF.Max(MathF.Max(outline, -holes), MathF.Abs(p.X - x) - (thickness / 2f));
            };
            var rd = Blend(
                0.006f,
                RoundBox(RearAxle + V(0.078f, -0.02f, -0.014f), V(0.009f, 0.018f, 0.013f), 0.005f), // hanger knuckle (B-knuckle behind the axle)
                Transformed(RoundBox(Vector3.Zero, V(0.011f, 0.022f, 0.012f), 0.006f), RearAxle + V(0.084f, -0.04f, -0.002f), Rot.Euler(-25, 0, 0)), // parallelogram body
                RoundBox(UpperJockey + V(0.08f, 0.004f, 0f), V(0.009f, 0.013f, 0.013f), 0.006f)); // cage pivot, coaxial with the upper pulley
            rd = Union(rd, Plate(ChainLineX + 0.0085f, 0.0025f, 0.017f), Plate(ChainLineX - 0.008f, 0.002f, 0.0135f));
            var rdMin = Vector3.Min(UpperJockey, LowerJockey) - V(0, 0.04f, 0.05f);
            var rdMax = RearAxle + V(0, 0.01f, 0.07f);
            Sculpt(AvatarNode.Bike, rd, new Vector3(ChainLineX - 0.016f, rdMin.Y, rdMin.Z), new Vector3(0.097f, rdMax.Y, rdMax.Z), 0.0015f, 1600, blackMat);
            var jockeys = Union(Sprocket(UpperJockey + V(ChainLineX, 0, 0), 11, 0.0035f, 0.004f), Sprocket(LowerJockey + V(ChainLineX, 0, 0), 11, 0.0035f, 0.004f));
            Sculpt(AvatarNode.Bike, jockeys, new Vector3(ChainLineX - 0.008f, rdMin.Y, rdMin.Z), new Vector3(ChainLineX + 0.008f, rdMax.Y, rdMax.Z), 0.0011f, 700, metalMat);

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
            Sculpt(AvatarNode.Bike, fd, V(ChainLineX - 0.02f, 0.33f, -0.12f), V(0.1f, 0.47f, 0.0f), 0.0015f, 700, blackMat);
        }

        /// <summary>Rotor plane (x) – inboard of fork legs and stays, as on a real disc bike.</summary>
        internal static float FrontRotorX => -0.044f;

        internal static float RearRotorX => -0.056f;

        private const float RotorRadius = 0.08f; // 160 mm

        /// <summary>Two-piece rotor: steel braking ring with angled cooling slots on a black spider (local to the wheel).</summary>
        private void BuildRotor(AvatarNode node, float x, AvatarMaterial metalMat)
        {
            var carrierMat = Mat("rotorcarrier", V(0.07f, 0.07f, 0.08f), 0.45f, 0.3f);
            const int slots = 30;
            const int arms = 5;
            Sdf rotor = p =>
            {
                float r = MathF.Sqrt((p.Y * p.Y) + (p.Z * p.Z));
                float a = MathF.Atan2(p.Z, p.Y);

                // Braking ring with slots that lean in the direction of rotation.
                float ring = MathF.Abs(r - 0.0715f) - 0.0085f;
                float step = MathF.PI * 2f / slots;
                float ak = MathF.Round(a / step) * step;
                var local = new Vector2(r, (a - ak) * r);
                var s0 = new Vector2(0.0685f, -0.0016f);
                var s1 = new Vector2(0.0768f, 0.0022f);
                var d = s1 - s0;
                float h = Math.Clamp(Vector2.Dot(local - s0, d) / d.LengthSquared(), 0f, 1f);
                float slot = (local - s0 - (d * h)).Length() - 0.0012f;
                float braking = MathF.Max(ring, -slot);

                // Spider: hub disc and five arms to rivets on the inner edge of the ring.
                float spider = r - 0.026f;
                float armStep = MathF.PI * 2f / arms;
                float aa = MathF.Round(a / armStep) * armStep;
                var arm = new Vector2(r, (a - aa) * r);
                float armWidth = 0.0075f - (0.002f * Math.Clamp((r - 0.026f) / 0.04f, 0f, 1f));
                spider = MathF.Min(spider, MathF.Max(MathF.Abs(arm.Y) - armWidth, r - 0.067f));
                spider = MathF.Min(spider, MathF.Abs(r - 0.0635f) - 0.0035f); // inner band under the ring

                float plane = MathF.Min(braking, spider);
                float thickness = r < 0.062f ? 0.0016f : 0.0009f;
                float slab = MathF.Max(plane, MathF.Abs(p.X - x) - thickness);

                // Rivets and centre-lock ring.
                float rivet = MathF.Max((arm - new Vector2(0.0635f, 0f)).Length() - 0.0026f, MathF.Abs(p.X - x) - 0.0024f);
                float lockring = MathF.Max(MathF.Abs(r - 0.019f) - 0.004f, MathF.Abs(p.X - x + (MathF.Sign(x) * 0.002f)) - 0.003f);
                return MathF.Min(slab, MathF.Min(rivet, lockring));
            };

            Sdf carrier = p =>
            {
                float r = MathF.Sqrt((p.Y * p.Y) + (p.Z * p.Z));
                return r - 0.0612f;
            };

            Sculpt(node, rotor, V(x - 0.008f, -0.085f, -0.085f), V(x + 0.008f, 0.085f, 0.085f), 0.0008f, 3200, metalMat, (carrier, carrierMat));
        }

        /// <summary>
        /// Flat-mount two-piston caliper. <paramref name="angle"/> is the position around the axle in degrees
        /// (0 = above, 90 = forward); the body straddles the rotor and a bracket reaches the frame at <paramref name="frameAttach"/>.
        /// </summary>
        private void BuildCaliper(Vector3 axle, float rotorX, float angle, Vector3 frameAttach, AvatarMaterial blackMat, AvatarMaterial metalMat)
        {
            float rad = angle * MathF.PI / 180f;
            var radial = new Vector3(0, MathF.Cos(rad), MathF.Sin(rad));
            var tangent = new Vector3(0, -MathF.Sin(rad), MathF.Cos(rad));
            var rot = Rot.LookRotation(tangent, radial); // local: x across, y radial outwards, z along the rotor
            var center = axle + new Vector3(rotorX, 0, 0) + (radial * 0.074f);
            var inv = rot.Transposed;
            float outward = MathF.Sign(rotorX); // towards the outside of the bike

            Sdf body = p =>
            {
                var q = inv * (p - center);
                // Two halves around the rotor, joined over its outer edge (bridge).
                float halves = Box(q - new Vector3(0, 0.002f, 0), new Vector3(0.0125f, 0.0115f, 0.024f), 0.005f);
                float slot = MathF.Max(MathF.Abs(q.X) - 0.0024f, q.Y - 0.0085f); // rotor gap, open towards the hub
                float shape = MathF.Max(halves, -slot);

                // Cooling fins on the pads (sticking out radially), outboard piston bulge, banjo/hose stub.
                float fins = float.MaxValue;
                for (int i = -3; i <= 3; i++)
                {
                    fins = MathF.Min(fins, Box(q - new Vector3(0, 0.016f, i * 0.0055f), new Vector3(0.006f, 0.0045f, 0.0011f), 0.0005f));
                }

                float piston = Cyl(q - new Vector3(outward * 0.011f, 0.0f, 0), 0.009f, 0.004f);
                float hose = Cyl2(q - new Vector3(outward * -0.004f, 0.006f, -0.026f), 0.0035f, 0.008f);
                return MathF.Min(MathF.Min(shape, fins), MathF.Min(piston, hose));
            };

            // Mounting bracket from the inboard half to the frame member.
            var bracketEnd = frameAttach;
            var bracketStart = center + (inv.Transposed * new Vector3(-outward * 0.009f, -0.006f, 0));
            Sdf bracket = SdfShapes.Squashed(SdfShapes.RoundCone(bracketStart, bracketEnd, 0.007f, 0.008f), bracketStart, radial, 0.45f);
            var bolt = SdfShapes.RoundCone(center + (inv.Transposed * new Vector3(outward * 0.017f, -0.004f, 0.012f)), center + (inv.Transposed * new Vector3(outward * 0.017f, -0.004f, -0.012f)), 0.0028f, 0.0028f);

            var min = Vector3.Min(center, bracketEnd) - new Vector3(0.04f);
            var max = Vector3.Max(center, bracketEnd) + new Vector3(0.04f);
            Sculpt(AvatarNode.Bike, SdfShapes.Union(SdfShapes.Blend(0.004f, body, bracket), bolt), min, max, 0.0011f, 1400, blackMat, (bolt, metalMat));
        }

        private static float Box(Vector3 q, Vector3 half, float radius)
        {
            var d = Vector3.Abs(q) - (half - new Vector3(radius));
            return Vector3.Max(d, Vector3.Zero).Length() + MathF.Min(MathF.Max(d.X, MathF.Max(d.Y, d.Z)), 0f) - radius;
        }

        /// <summary>Cylinder along x (radius, half length).</summary>
        private static float Cyl(Vector3 q, float radius, float half) =>
            MathF.Max(new Vector2(q.Y, q.Z).Length() - radius, MathF.Abs(q.X) - half);

        /// <summary>Cylinder along z (radius, half length).</summary>
        private static float Cyl2(Vector3 q, float radius, float half) =>
            MathF.Max(new Vector2(q.X, q.Y).Length() - radius, MathF.Abs(q.Z) - half);
    }
}
