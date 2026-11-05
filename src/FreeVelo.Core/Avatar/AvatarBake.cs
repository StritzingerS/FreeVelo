using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace FreeVelo.Core.Avatar
{
    /// <summary>
    /// Baked avatar: the sculpted geometry takes seconds to generate, so it is generated once (preview tool,
    /// <c>--bake</c>) and shipped as a small compressed file. Colours of kit and frame are applied when loading,
    /// so one baked file serves every rider style.
    /// </summary>
    public sealed partial class AvatarModel
    {
        private const uint BakeMagic = 0x56415646; // "FVAV"
        private const int BakeVersion = 1;
        private const float PositionScale = 16000f; // int16 → ±2.04 m with 0.06 mm steps

        /// <summary>Serializes all parts (positions, normals, triangles, material ids) into a gzip-compressed blob.</summary>
        public byte[] Serialize()
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            using (var w = new BinaryWriter(gzip, Encoding.UTF8))
            {
                var parts = Parts;
                w.Write(BakeMagic);
                w.Write(BakeVersion);
                w.Write(parts.Count);
                foreach (var part in parts)
                {
                    var mesh = part.Mesh;
                    var normals = mesh.ComputeNormals();
                    w.Write((int)part.Node);
                    w.Write(part.Material.Id);
                    w.Write(part.Material.Color.X);
                    w.Write(part.Material.Color.Y);
                    w.Write(part.Material.Color.Z);
                    w.Write(part.Material.Smoothness);
                    w.Write(part.Material.Metallic);
                    w.Write(mesh.Vertices.Count);
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        var v = mesh.Vertices[i];
                        w.Write(Quantize(v.X));
                        w.Write(Quantize(v.Y));
                        w.Write(Quantize(v.Z));
                        var n = normals[i];
                        w.Write((sbyte)MathF.Round(Math.Clamp(n.X, -1f, 1f) * 127f));
                        w.Write((sbyte)MathF.Round(Math.Clamp(n.Y, -1f, 1f) * 127f));
                        w.Write((sbyte)MathF.Round(Math.Clamp(n.Z, -1f, 1f) * 127f));
                    }

                    bool small = mesh.Vertices.Count <= ushort.MaxValue;
                    w.Write(mesh.Triangles.Count);
                    foreach (int t in mesh.Triangles)
                    {
                        if (small) w.Write((ushort)t);
                        else w.Write(t);
                    }
                }
            }

            return output.ToArray();
        }

        /// <summary>Loads a baked avatar, applying the colours of <paramref name="style"/>.</summary>
        public static AvatarModel Deserialize(byte[] data, AvatarStyle? style = null, bool includeBike = true)
        {
            var model = new AvatarModel(style ?? AvatarStyle.Default);
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var r = new BinaryReader(gzip, Encoding.UTF8);
            if (r.ReadUInt32() != BakeMagic) throw new InvalidDataException("Not a FreeVelo avatar file.");
            int version = r.ReadInt32();
            if (version != BakeVersion) throw new InvalidDataException($"Unsupported avatar file version {version}.");
            int count = r.ReadInt32();
            for (int p = 0; p < count; p++)
            {
                var node = (AvatarNode)r.ReadInt32();
                string id = r.ReadString();
                var color = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                float smoothness = r.ReadSingle();
                float metallic = r.ReadSingle();
                int vertexCount = r.ReadInt32();
                var mesh = new MeshData();
                for (int i = 0; i < vertexCount; i++)
                {
                    mesh.Vertices.Add(new Vector3(r.ReadInt16() / PositionScale, r.ReadInt16() / PositionScale, r.ReadInt16() / PositionScale));
                    var n = new Vector3(r.ReadSByte(), r.ReadSByte(), r.ReadSByte());
                    mesh.Normals.Add(n == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(n));
                }

                bool small = vertexCount <= ushort.MaxValue;
                int indexCount = r.ReadInt32();
                for (int i = 0; i < indexCount; i++) mesh.Triangles.Add(small ? r.ReadUInt16() : r.ReadInt32());

                if (!includeBike && IsBikeNode(node)) continue;
                var material = model.Mat(id, model.StyleColor(id) ?? color, smoothness, metallic);
                model.B(node, material).Append(mesh);
            }

            return model;
        }

        /// <summary>Nodes that belong to the bike (left out when a custom bike model is used).</summary>
        public static bool IsBikeNode(AvatarNode node) =>
            node == AvatarNode.Bike || node == AvatarNode.FrontWheel || node == AvatarNode.RearWheel || node == AvatarNode.Crankset
            || node == AvatarNode.PedalLeft || node == AvatarNode.PedalRight;

        /// <summary>Colour from the style for the materials that a rider can customise.</summary>
        private Vector3? StyleColor(string id)
        {
            switch (id)
            {
                case "jersey": return Style.Jersey;
                case "accent": return Style.JerseyAccent;
                case "jersey2": return Style.JerseySecondary;
                case "shorts": return Style.Shorts;
                case "skin": return Style.Skin;
                case "lips": return LipColor(Style.Skin);
                case "hair": return Style.Hair;
                case "helmet": return Style.Helmet;
                case "shoes": return Style.Shoes;
                case "socks": return Style.Socks;
                case "frame": return Style.Frame;
                default: return null;
            }
        }

        private static short Quantize(float v) => (short)Math.Clamp(MathF.Round(v * PositionScale), short.MinValue, short.MaxValue);
    }
}
