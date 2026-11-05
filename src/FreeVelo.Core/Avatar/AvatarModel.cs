using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FreeVelo.Core.Avatar
{
    /// <summary>Independently moving parts of the avatar.</summary>
    public enum AvatarNode
    {
        Bike,
        Rider,
        FrontWheel,
        RearWheel,
        Crankset,
        PedalRight,
        PedalLeft,
        ThighRight,
        ThighLeft,
        ShinRight,
        ShinLeft,
        FootRight,
        FootLeft,
        UpperArmRight,
        UpperArmLeft,
        ForearmRight,
        ForearmLeft,
        HandRight,
        HandLeft,
    }

    /// <summary>Surface definition (sRGB colour, smoothness/metallic as in Unity's URP Lit).</summary>
    public sealed class AvatarMaterial
    {
        public AvatarMaterial(string id, Vector3 color, float smoothness, float metallic = 0f)
        {
            Id = id;
            Color = color;
            Smoothness = smoothness;
            Metallic = metallic;
        }

        public string Id { get; }

        public Vector3 Color { get; }

        public float Smoothness { get; }

        public float Metallic { get; }
    }

    /// <summary>Mesh of one material on one node (in the node's local space).</summary>
    public sealed class AvatarPart
    {
        public AvatarPart(AvatarNode node, AvatarMaterial material, MeshData mesh)
        {
            Node = node;
            Material = material;
            Mesh = mesh;
        }

        public AvatarNode Node { get; }

        public AvatarMaterial Material { get; }

        public MeshData Mesh { get; }
    }

    /// <summary>Local position and rotation of a node relative to the avatar root.</summary>
    public readonly struct NodePose
    {
        public NodePose(Vector3 position, Rot rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Position { get; }

        public Rot Rotation { get; }

        public Vector3 Apply(Vector3 local) => (Rotation * local) + Position;
    }

    /// <summary>Colours of kit and bike.</summary>
    public sealed class AvatarStyle
    {
        public Vector3 Jersey { get; set; } = new Vector3(0.1f, 0.45f, 0.85f);

        public Vector3 JerseyAccent { get; set; } = new Vector3(0.95f, 0.95f, 0.95f);

        /// <summary>Contrast colour of the jersey side panels.</summary>
        public Vector3 JerseySecondary { get; set; } = new Vector3(0.04f, 0.1f, 0.26f);

        public Vector3 Shorts { get; set; } = new Vector3(0.05f, 0.05f, 0.06f);

        public Vector3 Skin { get; set; } = new Vector3(0.87f, 0.66f, 0.54f);

        public Vector3 Hair { get; set; } = new Vector3(0.25f, 0.18f, 0.12f);

        public Vector3 Helmet { get; set; } = new Vector3(0.97f, 0.97f, 0.97f);

        public Vector3 Shoes { get; set; } = new Vector3(0.96f, 0.96f, 0.96f);

        public Vector3 Socks { get; set; } = new Vector3(0.95f, 0.95f, 0.95f);

        public Vector3 Frame { get; set; } = new Vector3(0.72f, 0.06f, 0.08f);

        public static AvatarStyle Default => new AvatarStyle();
    }

    /// <summary>
    /// Procedural rider on a road bike, independent of any engine: Unity converts the parts into meshes and
    /// applies <see cref="Pose(float, float, float)"/> every frame; the preview tool renders the same data.
    /// Coordinates: x = right, y = up, z = forward (Unity); origin on the ground below the bottom bracket.
    /// </summary>
    public sealed partial class AvatarModel
    {
        // Bike geometry (metres) – roughly a size 56 road bike with 700×25c tyres.
        public const float CrankLength = 0.17f;
        public const float WheelRadius = 0.336f;
        public const float PedalOffsetX = 0.135f; // pedal centre from the bike centre (Q-factor + spindle)
        public static readonly Vector3 BottomBracket = new Vector3(0, 0.27f, 0);
        public static readonly Vector3 RearAxle = new Vector3(0, WheelRadius, -0.405f);
        public static readonly Vector3 FrontAxle = new Vector3(0, WheelRadius, 0.6f); // ~2 cm tyre clearance to the down tube
        public static readonly Vector3 SeatCluster = new Vector3(0, 0.80f, -0.16f);
        public static readonly Vector3 HeadTop = new Vector3(0, 0.83f, 0.39f);
        public static readonly Vector3 HeadBottom = new Vector3(0, 0.68f, 0.435f);

        // Rider (≈ 1.78 m): hip joint ~5 cm above the saddle; knee 70–143° over a revolution.
        public const float ThighLength = 0.46f;
        public const float ShinLength = 0.45f;
        public static readonly Vector3 Hip = new Vector3(0.085f, 1.06f, -0.185f);
        public static readonly Vector3 Shoulder = new Vector3(0.15f, 1.37f, 0.262f);
        public static readonly Vector3 Hand = new Vector3(0.205f, 0.91f, 0.555f);

        /// <summary>Ankle relative to the cleat (pedal axle) in the foot frame.</summary>
        public static readonly Vector3 AnkleInFoot = new Vector3(0, 0.08f, -0.1f);

        internal static readonly Vector3 Side = Vector3.UnitX;

        private readonly Dictionary<(AvatarNode, string), (AvatarMaterial Material, MeshBuilder Builder)> _parts =
            new Dictionary<(AvatarNode, string), (AvatarMaterial, MeshBuilder)>();

        private readonly Dictionary<string, AvatarMaterial> _materials = new Dictionary<string, AvatarMaterial>();

        private AvatarModel(AvatarStyle style)
        {
            Style = style;
        }

        public AvatarStyle Style { get; }

        /// <summary>All meshes, grouped by node and material.</summary>
        public IReadOnlyList<AvatarPart> Parts =>
            _parts.Where(p => p.Value.Builder.VertexCount > 0)
                .Select(p => new AvatarPart(p.Key.Item1, p.Value.Material, p.Value.Builder.Mesh))
                .ToList();

        public IReadOnlyCollection<AvatarMaterial> Materials => _materials.Values;

        public int TriangleCount => _parts.Values.Sum(p => p.Builder.Mesh.TriangleCount);

        /// <summary>Builds rider and bike. <paramref name="includeBike"/> = false for a custom bike model.</summary>
        public static AvatarModel Build(AvatarStyle? style = null, bool includeBike = true, float detail = 1f)
        {
            var model = new AvatarModel(style ?? AvatarStyle.Default) { Detail = Math.Clamp(detail, 0.25f, 2f) };
            if (includeBike) model.BuildBike();
            model.BuildRider();
            return model;
        }

        internal AvatarMaterial Mat(string id, Vector3 color, float smoothness, float metallic = 0f)
        {
            if (!_materials.TryGetValue(id, out var m))
            {
                m = new AvatarMaterial(id, color, smoothness, metallic);
                _materials[id] = m;
            }

            return m;
        }

        /// <summary>Builder for a node and material (created on first use).</summary>
        internal MeshBuilder B(AvatarNode node, AvatarMaterial material)
        {
            var key = (node, material.Id);
            if (!_parts.TryGetValue(key, out var part))
            {
                part = (material, new MeshBuilder());
                _parts[key] = part;
            }

            return part.Builder;
        }

        /// <summary>
        /// Meshes a sculpted shape and adds it to <paramref name="node"/>: the base material plus regions (each a
        /// signed function, negative inside) that later entries override. The triangle budget scales with <see cref="Detail"/>.
        /// </summary>
        internal void Sculpt(AvatarNode node, Sdf shape, Vector3 min, Vector3 max, float cell, int triangles, AvatarMaterial baseMaterial, params (Sdf Region, AvatarMaterial Material)[] regions)
        {
            var mesh = Polygonizer.Mesh(shape, min, max, cell);
            mesh = Decimator.Simplify(mesh, (int)(triangles * Detail), _sculptError, shape);
            var parts = Polygonizer.Split(mesh, regions.Select(r => r.Region).ToList());
            B(node, baseMaterial).Append(parts[0]);
            for (int i = 0; i < regions.Length; i++) B(node, regions[i].Material).Append(parts[i + 1]);
        }

        /// <summary>Allowed deviation of simplified sculpts (metres); tighter for faces.</summary>
        private float _sculptError = 0.0025f;

        /// <summary>Mesh resolution factor (1 = default; lower for distant riders).</summary>
        public float Detail { get; private set; } = 1f;

        // ------------------------------------------------------------------ pose

        /// <summary>
        /// Node poses for a crank angle (right crank: 0° = up, 90° = forward) and wheel rotation (degrees).
        /// </summary>
        public static Dictionary<AvatarNode, NodePose> Pose(float crankDegrees, float wheelDegrees, float stopped = 0f) =>
            Pose(crankDegrees, wheelDegrees, new Dictionary<AvatarNode, NodePose>(), stopped);

        /// <summary>Crank angle while standing: right pedal at "2 o'clock", ready to push off.</summary>
        public const float ParkedCrankDegrees = 60f;

        /// <summary>Lean of the whole avatar (degrees, positive = right) while standing with the left foot down.</summary>
        public static float StopLeanDeg(float stopped) => -8f * Smooth(stopped);

        /// <summary>
        /// Same as <see cref="Pose(float, float, float)"/>, filling an existing dictionary (no allocation per frame).
        /// <paramref name="stopped"/> blends from riding (0) to standing (1): the rider slides forward off the saddle,
        /// sits up a little, unclips the left foot and puts it on the ground; the bike leans by
        /// <see cref="StopLeanDeg"/> (applied by the caller around the tyre contact line).
        /// </summary>
        public static Dictionary<AvatarNode, NodePose> Pose(float crankDegrees, float wheelDegrees, Dictionary<AvatarNode, NodePose> pose, float stopped = 0f)
        {
            float t = Smooth(Math.Clamp(stopped, 0f, 1f));
            float crank = LerpAngle(crankDegrees, ParkedCrankDegrees, t);

            pose[AvatarNode.Bike] = new NodePose(Vector3.Zero, Rot.Identity);
            pose[AvatarNode.FrontWheel] = new NodePose(FrontAxle, Rot.AngleAxis(wheelDegrees, Vector3.UnitX));
            pose[AvatarNode.RearWheel] = new NodePose(RearAxle, Rot.AngleAxis(wheelDegrees, Vector3.UnitX));
            pose[AvatarNode.Crankset] = new NodePose(BottomBracket, Rot.AngleAxis(crank, Vector3.UnitX));

            // Upper body: slides forward and down off the saddle, sits up a little (rotation about the hip centre).
            var hipCentre = new Vector3(0, Hip.Y, Hip.Z);
            var body = Rot.AboutX(-14f * t);
            var hipNow = hipCentre + (StandingHipOffset * t);
            var rider = new NodePose(hipNow - (body * hipCentre), body);
            pose[AvatarNode.Rider] = rider;

            // Ground under the left foot, expressed in the avatar frame (which the caller leans by StopLeanDeg).
            var lean = Rot.AboutZ(-StopLeanDeg(stopped));
            var leanInverse = lean.Transposed;

            for (int side = 0; side < 2; side++)
            {
                bool right = side == 0;
                float sign = right ? 1f : -1f;
                float angle = (crank + (right ? 0f : 180f)) * MathF.PI / 180f;
                var pedal = BottomBracket + new Vector3(sign * PedalOffsetX, CrankLength * MathF.Cos(angle), CrankLength * MathF.Sin(angle));

                // Ankling: toes drop on the back of the stroke, foot nearly flat at the front.
                var foot = Rot.Euler(10f - (8f * MathF.Sin(angle)), 0, 0);
                var cleat = pedal + new Vector3(sign * 0.003f, 0.012f, 0); // shoe centred on the pedal, clear of the crank arm
                var pedalRot = foot;

                if (!right && t > 0f)
                {
                    // Left foot: unclips, swings out and down, stands flat on the road beside the bike.
                    var groundWorld = new Vector3(-0.3f, 0f, 0.03f);
                    var groundFoot = Rot.Euler(0, -8f, 0); // toes slightly out
                    var groundCleat = leanInverse * (groundWorld + new Vector3(0, 0.012f, 0));
                    var groundRot = leanInverse * groundFoot;
                    var lift = new Vector3(-0.04f, 0.07f, 0) * MathF.Sin(t * MathF.PI); // arc: lift and move out first
                    cleat = Vector3.Lerp(cleat, groundCleat, t) + lift;
                    foot = SlerpRot(foot, groundRot, t);
                    pedalRot = SlerpRot(pedalRot, Rot.Identity, t);
                }

                var ankle = cleat + (foot * AnkleInFoot);
                var hip = rider.Apply(new Vector3(sign * Hip.X, Hip.Y, Hip.Z));
                var knee = SolveJoint(hip, ankle, ThighLength, ShinLength, new Vector3(sign * (0.1f + (0.25f * t)), 0, 1f));

                pose[right ? AvatarNode.PedalRight : AvatarNode.PedalLeft] = new NodePose(pedal, pedalRot);
                pose[right ? AvatarNode.ThighRight : AvatarNode.ThighLeft] = Bone(hip, knee);
                pose[right ? AvatarNode.ShinRight : AvatarNode.ShinLeft] = Bone(knee, ankle);
                pose[right ? AvatarNode.FootRight : AvatarNode.FootLeft] = new NodePose(cleat, foot);

                // Arms: shoulders move with the upper body, hands stay on the hoods; the elbows follow (IK).
                var restShoulder = new Vector3(sign * Shoulder.X, Shoulder.Y, Shoulder.Z);
                var hand = new Vector3(sign * Hand.X, Hand.Y, Hand.Z);
                var restElbow = SolveJoint(restShoulder, hand, UpperArmLength, ForearmLength, ElbowPole(sign));
                var shoulder = rider.Apply(restShoulder);
                var elbow = SolveJoint(shoulder, hand, UpperArmLength, ForearmLength, ElbowPole(sign));
                pose[right ? AvatarNode.UpperArmRight : AvatarNode.UpperArmLeft] = Retarget(restShoulder, restElbow, shoulder, elbow);
                pose[right ? AvatarNode.ForearmRight : AvatarNode.ForearmLeft] = Retarget(restElbow, hand, elbow, hand);
                pose[right ? AvatarNode.HandRight : AvatarNode.HandLeft] = new NodePose(Vector3.Zero, Rot.Identity);
            }

            return pose;
        }

        /// <summary>Hip movement from the saddle to standing over the top tube.</summary>
        private static readonly Vector3 StandingHipOffset = new Vector3(0, -0.085f, 0.2f);

        public const float UpperArmLength = 0.3f;
        public const float ForearmLength = 0.31f;

        internal static Vector3 ElbowPole(float sign) => new Vector3(sign * 0.3f, -1f, 0f);

        /// <summary>
        /// Pose for a mesh modelled in the avatar's rest pose along the bone <paramref name="restFrom"/>→<paramref name="restTo"/>,
        /// moved onto the bone <paramref name="from"/>→<paramref name="to"/>.
        /// </summary>
        private static NodePose Retarget(Vector3 restFrom, Vector3 restTo, Vector3 from, Vector3 to)
        {
            var rest = Bone(restFrom, restTo).Rotation;
            var now = Bone(from, to).Rotation;
            var r = now * rest.Transposed;
            return new NodePose(from - (r * restFrom), r);
        }

        private static float Smooth(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - (2f * t));
        }

        private static float LerpAngle(float a, float b, float t)
        {
            float d = ((b - a) % 360f + 540f) % 360f - 180f;
            return a + (d * t);
        }

        /// <summary>Rotation interpolation via quaternions.</summary>
        private static Rot SlerpRot(Rot a, Rot b, float t)
        {
            var qa = a.ToQuaternion();
            var qb = b.ToQuaternion();
            var q = Quaternion.Slerp(new Quaternion(qa.X, qa.Y, qa.Z, qa.W), new Quaternion(qb.X, qb.Y, qb.Z, qb.W), t);
            var m = Matrix4x4.CreateFromQuaternion(q);

            // System.Numerics matrices are row-vector based: rows are the images of the basis vectors.
            return new Rot(new Vector3(m.M11, m.M12, m.M13), new Vector3(m.M21, m.M22, m.M23), new Vector3(m.M31, m.M32, m.M33));
        }

        /// <summary>Bone from <paramref name="from"/> to <paramref name="to"/>: local +Y along the bone, +Z to the front.</summary>
        public static NodePose Bone(Vector3 from, Vector3 to)
        {
            var dir = Vector3.Normalize(to - from);
            var front = Vector3.Cross(dir, Vector3.UnitX);
            return new NodePose(from, Rot.LookRotation(front.LengthSquared() > 1e-6f ? front : Vector3.UnitZ, dir));
        }

        /// <summary>Two-bone IK: middle joint (knee/elbow) bending towards <paramref name="pole"/>.</summary>
        public static Vector3 SolveJoint(Vector3 root, Vector3 target, float upper, float lower, Vector3 pole)
        {
            var d = target - root;
            float dist = Math.Clamp(d.Length(), 0.01f, upper + lower - 0.001f);
            var dir = Vector3.Normalize(d);
            float a = ((upper * upper) - (lower * lower) + (dist * dist)) / (2f * dist);
            float h = MathF.Sqrt(MathF.Max(0f, (upper * upper) - (a * a)));
            var bend = Vector3.Normalize(pole - (Vector3.Dot(pole, dir) * dir));
            return root + (dir * a) + (bend * h);
        }

        internal static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        internal static Vector2 R(float side, float other) => new Vector2(side, other);
    }
}
