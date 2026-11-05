using System;
using System.Collections.Generic;
using System.Numerics;
using static FreeVelo.Core.Avatar.SdfShapes;

namespace FreeVelo.Core.Avatar
{
    /// <summary>
    /// The rider, sculpted from blended signed-distance shapes (muscles, pelvis, ribcage …) and meshed by the
    /// polygonizer. Static upper body on the Rider node; legs are bones moved by <see cref="Pose(float, float)"/>.
    /// </summary>
    public sealed partial class AvatarModel
    {
        // Torso frame: along the spine (pelvis → neck), dorsal direction, lateral = x.
        private static readonly Vector3 SpineDir = Vector3.Normalize(new Vector3(0, 0.3f, 0.45f)); // ~34° torso angle
        private static readonly Vector3 DorsalDir = new Vector3(0, SpineDir.Z, -SpineDir.Y);
        private static readonly Vector3 TorsoOrigin = new Vector3(0, 1.09f, -0.19f);
        private static readonly Rot TorsoRot = Rot.LookRotation(SpineDir, DorsalDir);
        private static readonly Vector3 HeadCenter = new Vector3(0, 1.51f, 0.4f); // forward and low, neck reaching forward
        private static readonly Rot HeadRot = Rot.Euler(12, 0, 0); // looking down at the road ahead

        /// <summary>Point in the torso frame: lateral, dorsal (back) offset, distance along the spine.</summary>
        private static Vector3 T(float lateral, float dorsal, float along) => TorsoOrigin + (Vector3.UnitX * lateral) + (DorsalDir * dorsal) + (SpineDir * along);

        private static float AlongSpine(Vector3 p) => Vector3.Dot(p - TorsoOrigin, SpineDir);

        private static Vector3 HeadToWorld(Vector3 local) => HeadCenter + (HeadRot * local);

        /// <summary>Only torso and head (fast preview for face work).</summary>
        public static AvatarModel BuildHeadStudy(bool glasses = true, bool helmet = true)
        {
            var model = new AvatarModel(AvatarStyle.Default);
            model.BuildTorso();
            model.BuildHead();
            if (helmet) model.BuildHelmet();
            if (glasses) model.BuildGlasses();
            return model;
        }

        private void BuildRider()
        {
            BuildTorso();
            for (int s = -1; s <= 1; s += 2)
            {
                BuildArm(s);
                BuildHand(s);
            }

            BuildHead();
            BuildHelmet();
            BuildGlasses();
            BuildLegs();
        }

        private static void ArmJoints(int s, out Vector3 shoulder, out Vector3 elbow, out Vector3 wrist)
        {
            shoulder = V(s * Shoulder.X, Shoulder.Y, Shoulder.Z);
            var hand = V(s * Hand.X, Hand.Y, Hand.Z);
            elbow = SolveJoint(shoulder, hand, 0.3f, 0.31f, V(s * 0.3f, -1f, 0f)); // elbows only slightly out
            wrist = elbow + ((hand - elbow) * 0.84f);
        }

        // ------------------------------------------------------------------ torso, pelvis, sleeves

        private void BuildTorso()
        {
            var jersey = Mat("jersey", Style.Jersey, 0.3f);
            var accent = Mat("accent", Style.JerseyAccent, 0.3f);
            var shorts = Mat("shorts", Style.Shorts, 0.35f);
            var jersey2 = Mat("jersey2", Style.JerseySecondary, 0.3f);

            var parts = new List<Sdf>
            {
                Ellipsoid(V(0, 1.08f, -0.195f), V(0.138f, 0.072f, 0.1f), Rot.Euler(-30, 0, 0)), // pelvis
                Ellipsoid(T(0, -0.005f, 0.17f), V(0.14f, 0.1f, 0.15f), TorsoRot), // abdomen / lower back
                Ellipsoid(T(0, 0.02f, 0.05f), V(0.145f, 0.092f, 0.11f), TorsoRot), // lumbar: fills the waist above the pelvis
                Ellipsoid(T(0, 0.016f, 0.34f), V(0.145f, 0.104f, 0.19f), TorsoRot), // ribcage
                Ellipsoid(T(0, 0.04f, 0.4f), V(0.12f, 0.08f, 0.13f), TorsoRot), // rounded thoracic back (riders are never flat)
                Ellipsoid(T(0, 0.01f, 0.5f), V(0.155f, 0.066f, 0.072f), TorsoRot), // shoulder girdle
            };
            for (int s = -1; s <= 1; s += 2)
            {
                parts.Add(Ellipsoid(V(s * 0.056f, 1.055f, -0.235f), V(0.058f, 0.046f, 0.054f), Rot.Euler(-30, 0, 0))); // gluteus (seated: flattened on the saddle)
                parts.Add(Ellipsoid(T(s * 0.095f, 0.025f, 0.37f), V(0.055f, 0.066f, 0.13f), TorsoRot)); // latissimus
                parts.Add(Ellipsoid(T(s * 0.06f, -0.065f, 0.45f), V(0.058f, 0.034f, 0.055f), TorsoRot)); // pectoral
            }

            var core = Blend(0.06f, parts.ToArray());

            var upper = new List<Sdf> { core };
            var sleeves = new List<Sdf>();
            for (int s = -1; s <= 1; s += 2)
            {
                ArmJoints(s, out var shoulder, out var elbow, out _);
                var arm = elbow - shoulder;
                var dir = Vector3.Normalize(arm);
                upper.Add(RoundCone(T(s * 0.12f, 0.018f, 0.525f), T(0, 0.015f, 0.615f), 0.022f, 0.036f)); // trapezius (lean)
                upper.Add(Ellipsoid(shoulder + (dir * 0.035f) + V(s * 0.008f, 0.006f, 0), V(0.045f, 0.068f, 0.046f), Rot.FromTo(Vector3.UnitY, dir))); // deltoid
                var hem = shoulder + (arm * 0.55f);
                sleeves.Add(SmoothIntersect(RoundCone(shoulder, shoulder + (arm * 0.62f), 0.049f, 0.045f), Plane(hem, dir), 0.008f));
            }

            Sdf body = Blend(0.035f, upper.ToArray());
            foreach (var sleeve in sleeves) body = Blend(0.03f, body, sleeve);

            // Jersey worn over the shorts: a lip at the hem, collar.
            float hemAlong = 0.015f;
            var jerseyShell = SmoothIntersect(Grow(body, 0.0025f), Plane(T(0, 0, hemAlong), -SpineDir), 0.006f);
            var collar = Torus(T(0, 0.0f, 0.62f), SpineDir, 0.049f, 0.005f); // low collar in jersey colour
            var shape = Union(body, jerseyShell);

            var regions = new List<(Sdf, AvatarMaterial)>
            {
                (Plane(T(0, 0, hemAlong), -SpineDir), jersey),
                (Plane(T(0, 0, 0.302f), -SpineDir), accent), // chest band (accent above its lower edge …
                (Plane(T(0, 0, 0.334f), -SpineDir), jersey), // … jersey again above its upper edge)
                (SidePanel(hemAlong), jersey2), // contrast side panels from the hem to the armpits
            };
            for (int s = -1; s <= 1; s += 2)
            {
                ArmJoints(s, out var shoulder, out var elbow, out _);
                var arm = elbow - shoulder;
                var dir = Vector3.Normalize(arm);
                float len = arm.Length();
                var sh = shoulder;
                regions.Add((p =>
                {
                    float t = Vector3.Dot(p - sh, dir);
                    return MathF.Max((len * 0.49f) - t, Vector3.Distance(p, sh + (dir * t)) - 0.085f);
                }, accent)); // sleeve cuff
            }

            Sculpt(AvatarNode.Rider, shape, V(-0.29f, 0.97f, -0.37f), V(0.29f, 1.53f, 0.42f), 0.007f, 9000, shorts, regions.ToArray());
        }

        /// <summary>Jersey side panel: torso sides below the armpits, not on the sleeves.</summary>
        private static Sdf SidePanel(float hemAlong)
        {
            var arms = new List<(Vector3 A, Vector3 B)>();
            for (int s = -1; s <= 1; s += 2)
            {
                ArmJoints(s, out var shoulder, out var elbow, out _);
                arms.Add((shoulder, elbow));
            }

            return p =>
            {
                float along = AlongSpine(p);
                float side = 0.108f - MathF.Abs(p.X);
                float armDistance = float.MaxValue;
                foreach (var (a, b) in arms)
                {
                    var ab = b - a;
                    float t = Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
                    armDistance = MathF.Min(armDistance, Vector3.Distance(p, a + (ab * t)));
                }

                return MathF.Max(MathF.Max(side, along - 0.44f), MathF.Max(hemAlong - along, 0.075f - armDistance));
            };
        }

        // ------------------------------------------------------------------ arms and hands

        private void BuildArm(int s)
        {
            var skin = Mat("skin", Style.Skin, 0.35f);
            ArmJoints(s, out var shoulder, out var elbow, out var wrist);
            var arm = elbow - shoulder;
            var fore = wrist - elbow;
            var armDir = Vector3.Normalize(arm);
            var foreDir = Vector3.Normalize(fore);

            // Inside of the elbow bend (biceps side) and the outside (triceps, olecranon).
            var inside = Vector3.Normalize(foreDir - (Vector3.Dot(foreDir, armDir) * armDir));
            var armRot = Rot.FromTo(Vector3.UnitY, armDir);
            var foreRot = Rot.FromTo(Vector3.UnitY, foreDir);
            var lateralUp = Vector3.Normalize(Vector3.Cross(foreDir, inside) * s);

            var shape = Blend(
                0.025f,
                RoundCone(shoulder + (arm * 0.3f), elbow, 0.039f, 0.03f),
                Ellipsoid(shoulder + (arm * 0.64f) + (inside * 0.01f), V(0.029f, 0.07f, 0.029f), armRot), // biceps
                Ellipsoid(shoulder + (arm * 0.62f) - (inside * 0.01f), V(0.03f, 0.08f, 0.03f), armRot), // triceps
                Sphere(elbow - (inside * 0.012f), 0.022f), // olecranon
                RoundCone(elbow, wrist, 0.03f, 0.024f),
                Ellipsoid(elbow + (fore * 0.27f) + (lateralUp * 0.008f), V(0.037f, 0.08f, 0.033f), foreRot)); // forearm muscles

            var min = Vector3.Min(Vector3.Min(shoulder, elbow), wrist) - new Vector3(0.08f);
            var max = Vector3.Max(Vector3.Max(shoulder, elbow), wrist) + new Vector3(0.08f);
            Sculpt(AvatarNode.Rider, shape, min, max, 0.005f, 1400, skin);
        }

        private void BuildHand(int s)
        {
            var glove = Mat("glove", V(0.08f, 0.08f, 0.09f), 0.25f);
            var skin = Mat("skin", Style.Skin, 0.35f);
            ArmJoints(s, out _, out _, out var wrist);
            float x0 = s * 0.205f;

            var parts = new List<Sdf>
            {
                RoundBox(V(x0, 0.94f, 0.572f), V(0.04f, 0.016f, 0.045f), 0.014f, Rot.Euler(12, 0, 0)), // palm
                Ellipsoid(V(x0, 0.952f, 0.566f), V(0.036f, 0.012f, 0.04f)), // back of the hand
                RoundCone(wrist, V(x0, 0.946f, 0.536f), 0.026f, 0.029f), // wrist
            };

            // Fingers wrap around the hood and over the lever blade; index finger on the inside (−s).
            float[] offsets = { -0.027f, -0.009f, 0.009f, 0.026f };
            float[] lengths = { 0.95f, 1f, 0.96f, 0.82f };
            float[] radii = { 0.0095f, 0.0098f, 0.0092f, 0.0082f };
            for (int k = 0; k < 4; k++)
            {
                float o = s * offsets[k];
                float l = lengths[k];
                var knuckle = V(x0 + o, 0.94f, 0.606f);
                var p1 = knuckle + (V(o * 0.08f, -0.008f, 0.03f) * l);
                var p2 = p1 + (V(0, -0.026f, 0.014f) * l);
                var tip = p2 + (V(-o * 0.08f, -0.026f, -0.006f) * l);
                parts.Add(Chain(new[] { knuckle, p1, p2, tip }, new[] { radii[k] * 1.08f, radii[k], radii[k] * 0.93f, radii[k] * 0.85f }));
            }

            var thumbTip = V(x0 - (s * 0.03f), 0.905f, 0.602f);
            parts.Add(Chain(new[] { V(x0 - (s * 0.028f), 0.936f, 0.548f), V(x0 - (s * 0.037f), 0.917f, 0.578f), thumbTip }, new[] { 0.0125f, 0.0105f, 0.0088f }));

            var shape = Blend(0.01f, parts.ToArray());
            Sculpt(
                AvatarNode.Rider,
                shape,
                V(x0 - 0.075f, 0.84f, Math.Min(wrist.Z, 0.5f) - 0.04f),
                V(x0 + 0.075f, 1.0f, 0.68f),
                0.0026f,
                1800,
                glove,
                (p => 0.632f - p.Z, skin), // fingerless gloves
                (Sphere(thumbTip, 0.013f), skin));
        }

        // ------------------------------------------------------------------ head

        private void BuildHead()
        {
            var skin = Mat("skin", Style.Skin, 0.35f);
            var hair = Mat("hair", Style.Hair, 0.15f);

            // Proportions of an adult male head (local: y up, z forward, origin mid-cranium): breadth 15 cm,
            // length 19.5 cm, eyes at y ≈ 0.01, nose base −0.04, mouth −0.066, chin −0.12.
            // Face as one smooth loft: per height a cross-section (half width, front and back depth) taken from
            // adult head proportions, blended into the cranium. Small features are added gently on top.
            var head = Blend(0.02f, Ellipsoid(V(0, 0.02f, -0.01f), V(0.074f, 0.09f, 0.098f)), FaceLoft());

            // Brow ridge and cheekbones (zygomatic) as soft ridges.
            head = Blend(
                0.012f,
                head,
                Capsule(V(-0.026f, 0.026f, 0.086f), V(0.026f, 0.026f, 0.086f), 0.009f));

            // Eyes: socket, eyeball set back, upper lid fold.
            for (int s = -1; s <= 1; s += 2)
            {
                var eye = V(s * 0.032f, 0.006f, 0.07f);
                head = SmoothSubtract(head, Ellipsoid(eye + V(0.002f * s, 0.002f, 0.017f), V(0.0145f, 0.009f, 0.007f)), 0.009f);
                head = Blend(0.003f, head, Sphere(eye, 0.0118f)); // eyeball, cornea at z ≈ 0.082
                head = Blend(0.004f, head, Intersect(Shell(Sphere(eye, 0.0128f), 0.0018f), Plane(eye + V(0, 0.003f, 0), -Vector3.UnitY))); // upper lid
            }

            // Nose: bridge from the brow to a defined tip, wide base, nostril wings.
            var nose = Blend(
                0.006f,
                RoundCone(V(0, 0.012f, 0.088f), V(0, -0.024f, 0.109f), 0.0058f, 0.0072f), // dorsum
                Ellipsoid(V(0, -0.02f, 0.094f), V(0.014f, 0.019f, 0.012f)), // base on the maxilla
                Sphere(V(0, -0.028f, 0.109f), 0.0078f), // tip
                Sphere(V(0.0115f, -0.033f, 0.098f), 0.0068f), // alae
                Sphere(V(-0.0115f, -0.033f, 0.098f), 0.0068f));
            head = Blend(0.007f, head, nose);
            for (int s = -1; s <= 1; s += 2) head = SmoothSubtract(head, Ellipsoid(V(s * 0.0062f, -0.0385f, 0.103f), V(0.0042f, 0.0022f, 0.0055f)), 0.0015f); // nostrils

            // Philtrum, lips with a cupid's bow, closed mouth.
            head = Blend(
                0.005f,
                head,
                Ellipsoid(V(0, -0.045f, 0.094f), V(0.008f, 0.007f, 0.004f)), // philtrum
                Ellipsoid(V(0.008f, -0.0545f, 0.0925f), V(0.011f, 0.0048f, 0.0048f), Rot.Euler(0, 0, -8)), // upper lip halves
                Ellipsoid(V(-0.008f, -0.0545f, 0.0925f), V(0.011f, 0.0048f, 0.0048f), Rot.Euler(0, 0, 8)),
                Ellipsoid(V(0, -0.0635f, 0.0915f), V(0.0135f, 0.0058f, 0.0052f))); // lower lip
            head = SmoothSubtract(head, Ellipsoid(V(0, -0.075f, 0.093f), V(0.013f, 0.0032f, 0.005f)), 0.006f); // mentolabial fold

            // Ears: helix, concha.
            for (int s = -1; s <= 1; s += 2)
            {
                var ear = SmoothSubtract(
                    Ellipsoid(V(s * 0.074f, -0.008f, -0.014f), V(0.0105f, 0.03f, 0.018f), Rot.Euler(-14, s * 18f, 0)),
                    Ellipsoid(V(s * 0.083f, -0.012f, -0.01f), V(0.006f, 0.015f, 0.0095f)),
                    0.004f);
                head = Blend(0.006f, head, ear);
            }

            // Neck from behind the jaw down into the collar.
            var neckBase = HeadRot.Transposed * (T(0, -0.005f, 0.5f) - HeadCenter);
            head = Blend(0.028f, head, RoundCone(V(0, -0.06f, -0.04f), neckBase, 0.044f, 0.05f));

            // Short hair: back and sides, under the helmet.
            Sdf hairRegion = p =>
            {
                var q = HeadRot.Transposed * (p - HeadCenter);

                // Hairline: high on the forehead, down over the temples to short sideburns, low at the nape.
                float t = Math.Clamp((0.075f - q.Z) / 0.17f, 0f, 1f);
                float line = 0.066f - (0.12f * t) + (MathF.Abs(q.X) > 0.06f && q.Z > -0.03f ? -0.02f * Math.Clamp((q.Z + 0.03f) / 0.04f, 0f, 1f) : 0f);
                float hair = line - q.Y;
                float notEars = -(Ellipsoid(V(MathF.Sign(q.X) * 0.076f, -0.008f, -0.014f), V(0.018f, 0.036f, 0.026f))(q));
                float face = MathF.Max(0.06f - q.Z, q.Y - 0.055f); // negative on the face below the forehead line
                return MathF.Max(MathF.Max(hair, notEars), -face);
            };

            Sdf browRegion = p =>
            {
                var q = HeadRot.Transposed * (p - HeadCenter);
                float ax = MathF.Abs(q.X);
                float arch = 0.029f + (0.004f * MathF.Sin(Math.Clamp((ax - 0.012f) / 0.042f, 0f, 1f) * MathF.PI)) - (ax > 0.045f ? (ax - 0.045f) * 0.3f : 0f);
                return MathF.Max(MathF.Max(MathF.Abs(q.Y - arch) - 0.0032f, MathF.Max(0.011f - ax, ax - 0.054f)), 0.07f - q.Z);
            };

            Sdf lipsRegion = p =>
            {
                var q = HeadRot.Transposed * (p - HeadCenter);
                return MathF.Max(Ellipsoid(V(0, -0.0588f, 0.095f), V(0.0185f, 0.0092f, 0.014f))(q), 0.0898f - q.Z);
            };

            _sculptError = 0.0006f; // faces show every lump
            Sculpt(AvatarNode.Rider, Transformed(head, HeadCenter, HeadRot), V(-0.11f, 1.27f, 0.15f), V(0.11f, 1.65f, 0.56f), 0.0024f, 12000, skin, (hairRegion, hair), (browRegion, hair), (lipsRegion, Mat("lips", LipColor(Style.Skin), 0.45f)), (MouthLine(), Mat("mouthline", LipColor(Style.Skin) * 0.45f, 0.3f)));
            _sculptError = 0.0025f;
        }

        private Sdf HelmetOuter()
        {
            var outer = Blend(
                0.04f,
                Ellipsoid(V(0, 0.034f, -0.008f), V(0.102f, 0.1f, 0.135f)),
                Ellipsoid(V(0, 0.016f, -0.085f), V(0.083f, 0.072f, 0.085f)));

            // Lower edge: high over the forehead, low at the back.
            var rimPoint = V(0, 0.056f, 0.12f);
            var rimNormal = Vector3.Normalize(V(0, 0.95f, -0.31f));
            return SmoothIntersect(outer, Plane(rimPoint, -rimNormal), 0.01f);
        }

        private void BuildHelmet()
        {
            var shell = Mat("helmet", Style.Helmet, 0.75f);
            var inner = Mat("helmetinner", V(0.13f, 0.13f, 0.14f), 0.2f);
            var accent = Mat("accent", Style.JerseyAccent, 0.3f);
            var black = Mat("black", V(0.06f, 0.06f, 0.07f), 0.35f);

            var outer = HelmetOuter();
            var helmet = SmoothSubtract(outer, Ellipsoid(V(0, 0.02f, -0.01f), V(0.084f, 0.098f, 0.108f)), 0.004f);

            // Vents: long slots from front to back, splayed outwards; three brow vents.
            for (int i = -2; i <= 2; i++)
            {
                float xc = i * 0.026f;
                float splay = i * 0.12f;
                float halfLen = i == 0 ? 0.035f : 0.045f;
                float zc = i == 0 ? 0.0f : -0.01f;
                helmet = SmoothSubtract(helmet, p =>
                {
                    float x = p.X - xc - (splay * (p.Z - zc) * 0.6f);
                    float z = MathF.Max(MathF.Abs(p.Z - zc) - halfLen, 0f);
                    return MathF.Sqrt((x * x) + (z * z)) - 0.0075f;
                }, 0.004f);
            }

            for (int i = -1; i <= 1; i++)
            {
                float xc = i * 0.034f;
                helmet = SmoothSubtract(helmet, p =>
                {
                    float x = MathF.Max(MathF.Abs(p.X - xc) - 0.011f, 0f);
                    float y = p.Y - 0.065f;
                    return MathF.Sqrt((x * x) + (y * y)) - 0.009f + (p.Z < 0.06f ? 1f : 0f);
                }, 0.004f);
            }

            // Rear vents: slots across the back of the shell.
            for (int i = -1; i <= 1; i++)
            {
                float xc = i * 0.03f;
                helmet = SmoothSubtract(helmet, p =>
                {
                    float x = MathF.Max(MathF.Abs(p.X - xc) - 0.008f, 0f);
                    float y = p.Y - 0.035f + (MathF.Abs(i) * 0.008f);
                    return MathF.Sqrt((x * x) + (y * y)) - 0.0065f + (p.Z > -0.09f ? 1f : 0f);
                }, 0.003f);
            }

            Sdf innerRegion = p => outer(HeadRot.Transposed * (p - HeadCenter)) + 0.0035f;
            Sdf stripe = p =>
            {
                var q = HeadRot.Transposed * (p - HeadCenter);
                return MathF.Abs(MathF.Abs(q.X) - 0.083f + (q.Y * 0.25f)) - 0.007f;
            };

            Sculpt(AvatarNode.Rider, Transformed(helmet, HeadCenter, HeadRot), V(-0.13f, 1.4f, 0.2f), V(0.13f, 1.68f, 0.6f), 0.003f, 4500, shell, (stripe, accent), (innerRegion, inner));

            // Straps and retention cradle.
            var straps = new List<Sdf>();
            for (int s = -1; s <= 1; s += 2)
            {
                var below = V(s * 0.07f, -0.062f, 0.004f);
                straps.Add(Chain(new[] { V(s * 0.08f, 0.0f, 0.04f), below }, new[] { 0.0032f, 0.0032f }));
                straps.Add(Chain(new[] { V(s * 0.082f, 0.0f, -0.045f), below }, new[] { 0.0032f, 0.0032f }));
                straps.Add(Chain(new[] { below, V(s * 0.047f, -0.094f, 0.022f), V(s * 0.016f, -0.111f, 0.044f), V(0, -0.112f, 0.047f) }, new[] { 0.0033f, 0.0033f, 0.0033f, 0.0033f })); // along the jaw to the buckle under the chin
                straps.Add(Chain(new[] { V(s * 0.07f, -0.02f, -0.07f), V(s * 0.04f, -0.045f, -0.098f), V(0, -0.05f, -0.105f) }, new[] { 0.004f, 0.004f, 0.004f }));
            }

            // Fit dial at the back of the retention cradle.
            var dial = V(0, -0.05f, -0.112f);
            straps.Add(q => SmoothMax(new Vector2(q.X - dial.X, q.Y - dial.Y).Length() - 0.012f, MathF.Abs(q.Z - dial.Z) - 0.007f, 0.003f));
            Sculpt(AvatarNode.Rider, Transformed(Union(straps.ToArray()), HeadCenter, HeadRot), V(-0.12f, 1.33f, 0.22f), V(0.12f, 1.6f, 0.56f), 0.0022f, 700, black);
        }

        private void BuildGlasses()
        {
            var lens = Mat("lens", V(0.17f, 0.18f, 0.21f), 0.92f, 0.3f); // grey tinted, reflective
            var black = Mat("black", V(0.06f, 0.06f, 0.07f), 0.35f);

            var shield = Ellipsoid(V(0, 0.012f, 0.0f), V(0.088f, 0.1f, 0.113f));
            var outline = Ellipsoid(V(0, 0.003f, 0.06f), V(0.092f, 0.021f, 0.12f)); // sits on the nose, brows visible above
            var lensShape = Intersect(Intersect(Shell(shield, 0.003f), outline), Plane(V(0, 0, 0.03f), -Vector3.UnitZ));
            lensShape = SmoothSubtract(lensShape, Ellipsoid(V(0, -0.021f, 0.11f), V(0.0115f, 0.019f, 0.04f)), 0.004f); // nose cut-out
            var frame = Intersect(Intersect(Shell(shield, 0.0048f), Grow(outline, 0.0012f)), Intersect(Plane(V(0, 0.0165f, 0), -Vector3.UnitY), Plane(V(0, 0, 0.03f), -Vector3.UnitZ))); // top rim along the lens edge
            var parts = new List<Sdf> { lensShape, frame };
            for (int s = -1; s <= 1; s += 2) parts.Add(Chain(new[] { V(s * 0.083f, 0.021f, 0.035f), V(s * 0.082f, 0.014f, -0.02f), V(s * 0.077f, 0.0f, -0.045f) }, new[] { 0.0026f, 0.0024f, 0.0022f }));

            Sdf frameRegion = p =>
            {
                var q = HeadRot.Transposed * (p - HeadCenter);
                return MathF.Min(0.0185f - q.Y, 0.033f - q.Z);
            };

            Sculpt(AvatarNode.Rider, Transformed(Union(parts.ToArray()), HeadCenter, HeadRot), V(-0.11f, 1.45f, 0.33f), V(0.11f, 1.6f, 0.56f), 0.0015f, 1800, lens, (frameRegion, black));
        }

        // ------------------------------------------------------------------ legs and shoes

        private void BuildLegs()
        {
            var shorts = Mat("shorts", Style.Shorts, 0.35f);
            var jersey = Mat("jersey", Style.Jersey, 0.3f);
            var skin = Mat("skin", Style.Skin, 0.35f);
            var socks = Mat("socks", Style.Socks, 0.15f);
            var shoes = Mat("shoes", Style.Shoes, 0.55f);
            var black = Mat("black", V(0.06f, 0.06f, 0.07f), 0.35f);
            var shoeAccent = Mat("shoeaccent", V(0.2f, 0.21f, 0.23f), 0.5f);

            foreach (var (thighNode, shinNode, footNode, s) in new[]
            {
                (AvatarNode.ThighRight, AvatarNode.ShinRight, AvatarNode.FootRight, 1f),
                (AvatarNode.ThighLeft, AvatarNode.ShinLeft, AvatarNode.FootLeft, -1f),
            })
            {
                // Thigh (local: +y hip → knee, +z front, +x right).
                float l = ThighLength;
                var muscles = Blend(
                    0.04f,
                    RoundCone(V(0, 0, 0), V(0, l, 0), 0.063f, 0.046f),
                    Sphere(V(0, 0.01f, 0.008f), 0.06f), // hip
                    Ellipsoid(V(0, 0.55f * l, 0.026f), V(0.058f, 0.2f, 0.045f)), // rectus femoris
                    Ellipsoid(V(s * 0.032f, 0.5f * l, 0f), V(0.045f, 0.17f, 0.048f)), // vastus lateralis
                    Ellipsoid(V(0, 0.5f * l, -0.028f), V(0.052f, 0.16f, 0.04f)), // hamstrings
                    Ellipsoid(V(-s * 0.03f, 0.25f * l, 0f), V(0.045f, 0.12f, 0.05f))); // adductors
                var thigh = Blend(
                    0.02f,
                    muscles,
                    Ellipsoid(V(-s * 0.028f, 0.8f * l, 0.02f), V(0.035f, 0.065f, 0.035f)), // vastus medialis
                    Sphere(V(0, l, 0.003f), 0.046f), // knee
                    Ellipsoid(V(0, l - 0.008f, 0.04f), V(0.026f, 0.03f, 0.014f))); // patella
                float legEnd = 0.72f * l;
                var shortsLeg = SmoothIntersect(Grow(thigh, 0.0025f), Plane(V(0, legEnd, 0), Vector3.UnitY), 0.005f);
                Sculpt(
                    thighNode,
                    Union(thigh, shortsLeg),
                    V(-0.11f, -0.1f, -0.11f),
                    V(0.11f, l + 0.06f, 0.11f),
                    0.006f,
                    2600,
                    shorts,
                    (Plane(V(0, legEnd - 0.03f, 0), -Vector3.UnitY), jersey), // leg gripper in jersey colour
                    (Plane(V(0, legEnd - 0.0004f, 0), -Vector3.UnitY), skin));

                // Shin (local: +y knee → ankle, +z front).
                float sl = ShinLength;
                var shin = Blend(
                    0.03f,
                    Sphere(V(0, 0, 0.003f), 0.045f),
                    RoundCone(V(0, 0.02f, 0.005f), V(0, sl, -0.005f), 0.043f, 0.029f),
                    Ellipsoid(V(-s * 0.017f, 0.3f * sl, -0.03f), V(0.034f, 0.1f, 0.04f)), // gastrocnemius medial
                    Ellipsoid(V(s * 0.018f, 0.26f * sl, -0.028f), V(0.031f, 0.085f, 0.036f)), // lateral
                    Capsule(V(0, 0.08f * sl, 0.03f), V(0, 0.85f * sl, 0.017f), 0.013f), // tibia
                    RoundCone(V(0, 0.65f * sl, -0.03f), V(0, sl, -0.032f), 0.015f, 0.012f)); // achilles
                shin = Blend(0.012f, shin, Sphere(V(s * 0.026f, sl - 0.005f, 0), 0.014f), Sphere(V(-s * 0.024f, sl - 0.012f, 0.002f), 0.013f), Sphere(V(0, sl, -0.005f), 0.031f));
                float sockTop = 0.7f * sl;
                var cuff = SmoothIntersect(Grow(shin, 0.002f), p => MathF.Abs(p.Y - (sockTop + 0.012f)) - 0.012f, 0.004f);
                Sculpt(shinNode, Union(shin, cuff), V(-0.08f, -0.06f, -0.09f), V(0.08f, sl + 0.05f, 0.08f), 0.005f, 2200, skin, (Plane(V(0, sockTop, 0), -Vector3.UnitY), socks));

                // Shoe (local: origin at the cleat, +z toes, +y up).
                var upperBlend = Blend(
                    0.03f,
                    Ellipsoid(V(0, 0.048f, -0.122f), V(0.033f, 0.05f, 0.042f)), // heel
                    Ellipsoid(V(0, 0.036f, -0.045f), V(0.04f, 0.044f, 0.08f)), // midfoot
                    Ellipsoid(V(s * 0.004f, 0.024f, 0.035f), V(0.043f, 0.032f, 0.06f)), // forefoot
                    Ellipsoid(V(s * 0.002f, 0.019f, 0.07f), V(0.034f, 0.025f, 0.031f))); // toe box
                var upper = Intersect(upperBlend, Plane(V(0, 0.014f, 0), -Vector3.UnitY));
                upper = SmoothSubtract(upper, Ellipsoid(V(0, 0.1f, -0.108f), V(0.028f, 0.034f, 0.04f)), 0.01f); // collar opening
                var footprint = Grow(upperBlend, 0.003f);
                Sdf sole = p => SmoothMax(footprint(new Vector3(p.X, 0.014f, p.Z)), MathF.Abs(p.Y - 0.007f) - 0.0075f, 0.004f);
                var dialCenter = V(s * 0.012f, 0.077f, -0.042f);
                var dialRot = Rot.Euler(-28, 0, s * 12f);
                var dial = Transformed(p => SmoothMax(new Vector2(p.X, p.Z).Length() - 0.015f, MathF.Abs(p.Y) - 0.006f, 0.003f), dialCenter, dialRot);
                var strap = Intersect(Shell(Grow(upper, 0.0015f), 0.003f), p => MathF.Max(MathF.Abs(p.Z - 0.02f) - 0.011f, 0.02f - p.Y));
                var cleat = RoundBox(V(0, -0.002f, 0.002f), V(0.03f, 0.007f, 0.04f), 0.004f);
                var shoe = Union(Blend(0.004f, upper, sole), dial, strap, cleat);
                Sdf stripe = p => MathF.Max(MathF.Abs(p.Z + (p.Y * 0.9f) + 0.03f) - 0.007f, -(p.X * s) + 0.025f);
                Sculpt(
                    footNode,
                    shoe,
                    V(-0.075f, -0.02f, -0.175f),
                    V(0.075f, 0.13f, 0.175f),
                    0.0035f,
                    2600,
                    shoes,
                    (stripe, shoeAccent),
                    (p => MathF.Min(p.Y - 0.0142f, Vector3.Distance(p, dialCenter) - 0.019f), black),
                    (p => MathF.Max(MathF.Abs(p.Z - 0.02f) - 0.011f, MathF.Max(0.0145f - p.Y, -(Grow(upper, 0.0006f)(p)))), black));
            }
        }

        private static float SmoothMax(float a, float b, float k) => -SMin(-a, -b, k);

        /// <summary>Thin dark line between the lips (colour, not geometry: too fine to sculpt).</summary>
        private static Sdf MouthLine() => p =>
        {
            var q = HeadRot.Transposed * (p - HeadCenter);
            float y = -0.0588f - (q.X * q.X * 5f);
            return MathF.Max(MathF.Max(MathF.Abs(q.Y - y) - 0.0007f, MathF.Abs(q.X) - 0.0195f), 0.088f - q.Z);
        };

        /// <summary>Smooth face/jaw loft (head-local: y up, z forward, origin mid-cranium).</summary>
        private static Sdf FaceLoft()
        {
            // y from forehead to under the chin; half width; front depth (face surface without nose/lips); back depth.
            float[] ys = { 0.07f, 0.04f, 0.012f, -0.012f, -0.035f, -0.054f, -0.072f, -0.089f, -0.101f, -0.11f };
            float[] w = { 0.068f, 0.07f, 0.07f, 0.068f, 0.064f, 0.058f, 0.05f, 0.039f, 0.027f, 0.012f }; // gradual taper to the jaw
            float[] front = { 0.084f, 0.088f, 0.08f, 0.084f, 0.09f, 0.092f, 0.089f, 0.088f, 0.083f, 0.066f }; // muzzle under the lips, chin projection
            float[] back = { 0.08f, 0.075f, 0.065f, 0.05f, 0.035f, 0.02f, 0.008f, -0.002f, -0.01f, -0.016f };

            float Sample(float[] values, float y)
            {
                if (y >= ys[0]) return values[0];
                int n = ys.Length;
                if (y <= ys[n - 1]) return values[n - 1];
                int i = 0;
                while (ys[i + 1] > y) i++;
                float t = (ys[i] - y) / (ys[i] - ys[i + 1]);
                float p0 = values[Math.Max(0, i - 1)], p1 = values[i], p2 = values[i + 1], p3 = values[Math.Min(n - 1, i + 2)];
                return 0.5f * ((2f * p1) + ((p2 - p0) * t) + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t * t) + (((3f * p1) - p0 - (3f * p2) + p3) * t * t * t));
            }

            return p =>
            {
                float y = p.Y;
                float hw = Sample(w, y), zf = Sample(front, y), zb = Sample(back, y);
                float zc = (zf - zb) / 2f; // section centre and half depth
                float hd = (zf + zb) / 2f;
                float qx = p.X / hw, qz = (p.Z - zc) / hd;
                float k0 = MathF.Sqrt((qx * qx) + (qz * qz));
                float k1 = MathF.Sqrt((qx * qx / (hw * hw)) + (qz * qz / (hd * hd)));
                float section = k1 < 1e-9f ? -MathF.Min(hw, hd) : k0 * (k0 - 1f) / k1;
                float below = (ys[ys.Length - 1] + 0.004f) - y; // closed under the chin
                float above = y - ys[0];
                return SmoothMax(section * 0.9f, MathF.Max(below, above), 0.008f);
            };
        }

        internal static Vector3 LipColor(Vector3 skin) => skin * new Vector3(0.95f, 0.83f, 0.87f);
    }
}
