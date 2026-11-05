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
    /// applies <see cref="Pose(float, float)"/> every frame; the preview tool renders the same data.
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
        public static Dictionary<AvatarNode, NodePose> Pose(float crankDegrees, float wheelDegrees) =>
            Pose(crankDegrees, wheelDegrees, new Dictionary<AvatarNode, NodePose>());

        /// <summary>Same as <see cref="Pose(float, float)"/>, filling an existing dictionary (no allocation per frame).</summary>
        public static Dictionary<AvatarNode, NodePose> Pose(float crankDegrees, float wheelDegrees, Dictionary<AvatarNode, NodePose> pose)
        {
            pose[AvatarNode.Bike] = new NodePose(Vector3.Zero, Rot.Identity);
            pose[AvatarNode.Rider] = new NodePose(Vector3.Zero, Rot.Identity);
            pose[AvatarNode.FrontWheel] = new NodePose(FrontAxle, Rot.AngleAxis(wheelDegrees, Vector3.UnitX));
            pose[AvatarNode.RearWheel] = new NodePose(RearAxle, Rot.AngleAxis(wheelDegrees, Vector3.UnitX));
            pose[AvatarNode.Crankset] = new NodePose(BottomBracket, Rot.AngleAxis(crankDegrees, Vector3.UnitX));

            for (int side = 0; side < 2; side++)
            {
                bool right = side == 0;
                float sign = right ? 1f : -1f;
                float angle = (crankDegrees + (right ? 0f : 180f)) * MathF.PI / 180f;
                var pedal = BottomBracket + new Vector3(sign * PedalOffsetX, CrankLength * MathF.Cos(angle), CrankLength * MathF.Sin(angle));

                // Ankling: toes drop on the back of the stroke, foot nearly flat at the front.
                var foot = Rot.Euler(10f - (8f * MathF.Sin(angle)), 0, 0);
                var cleat = pedal + new Vector3(sign * 0.003f, 0.012f, 0); // shoe centred on the pedal, clear of the crank arm
                var ankle = cleat + (foot * AnkleInFoot);
                var hip = new Vector3(sign * Hip.X, Hip.Y, Hip.Z);
                var knee = SolveJoint(hip, ankle, ThighLength, ShinLength, new Vector3(sign * 0.1f, 0, 1f));

                pose[right ? AvatarNode.PedalRight : AvatarNode.PedalLeft] = new NodePose(pedal, foot); // clipped in: pedal follows the shoe
                pose[right ? AvatarNode.ThighRight : AvatarNode.ThighLeft] = Bone(hip, knee);
                pose[right ? AvatarNode.ShinRight : AvatarNode.ShinLeft] = Bone(knee, ankle);
                pose[right ? AvatarNode.FootRight : AvatarNode.FootLeft] = new NodePose(cleat, foot);
            }

            return pose;
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
