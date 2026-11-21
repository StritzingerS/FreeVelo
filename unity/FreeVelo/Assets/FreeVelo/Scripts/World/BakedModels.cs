using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FreeVelo.Core.World.Gen;
using UnityEngine;

namespace FreeVelo.World
{
    /// <summary>
    /// Models built in Blender by script (tools/blender/*.py) for the close-up level of detail, replacing the coded
    /// model of the same kind and variant within <see cref="WorldBuilder"/>'s close range; farther away the coded
    /// models are still used. Resources/Models/{Kind}_{Variant}.bytes in the format "FVM1": per material its
    /// WorldMaterial name, positions and normals (Unity space, metres, origin on the floor, front +Z) and triangles
    /// (clockwise front faces). Texture coordinates are made from position and normal (SurfaceTextures.UV).
    /// </summary>
    public static class BakedModels
    {
        public sealed class Part
        {
            public WorldMaterial Material;
            public Vector3[] Positions;
            public Vector3[] Normals;
            public int[] Triangles;

            /// <summary>Texture coordinates (format FVM2); null = made from position and normal.</summary>
            public Vector2[] Uvs;

            /// <summary>
            /// For material "Textured:&lt;stem&gt;": the model keeps its own texture, Resources/Surfaces/&lt;stem&gt;_diff
            /// and _nor (instead of a WorldMaterial).
            /// </summary>
            public string TextureStem;
        }

        private static readonly Dictionary<(SceneryKind, int, int), Part[]> Cache = new Dictionary<(SceneryKind, int, int), Part[]>();

        /// <summary>
        /// Play mode starts without a domain reload ("Enter Play Mode Options"): without this, a model file replaced
        /// in the editor would keep showing its old version until Unity restarts.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();

        /// <summary>
        /// The baked model, or null when this kind and variant has none. <paramref name="level"/> 0 = close up
        /// ({Kind}_{Variant}.bytes), 1 = the simplified version for the middle distance ({Kind}_{Variant}_lod1.bytes).
        /// </summary>
        public static Part[] Get(SceneryKind kind, int variant, int level = 0)
        {
            if (Cache.TryGetValue((kind, variant, level), out var parts)) return parts;
            parts = null;
            // Variants above the coded three carry a colour (the plain house: variant = size + 3 × colour) and have
            // their own baked file; without one they fall back to the size variant.
            string Name(int v) => level == 0 ? $"Models/{kind}_{v}" : $"Models/{kind}_{v}_lod{level}";
            int size = ((variant % SceneryModels.Variants) + SceneryModels.Variants) % SceneryModels.Variants;
            var asset = (variant >= SceneryModels.Variants ? Resources.Load<TextAsset>(Name(variant)) : null) ?? Resources.Load<TextAsset>(Name(size));
            if (asset != null)
            {
                try
                {
                    parts = Read(asset.bytes);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Baked model {kind} {variant}: {e.Message}");
                }

                Resources.UnloadAsset(asset);
            }

            Cache[(kind, variant, level)] = parts;
            return parts;
        }

        /// <summary>Triangles of a baked model (0 if there is none).</summary>
        public static int Triangles(Part[] parts)
        {
            if (parts == null) return 0;
            int n = 0;
            foreach (var p in parts) n += p.Triangles.Length / 3;
            return n;
        }

        public static Part[] Read(byte[] bytes)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (magic != "FVM1" && magic != "FVM2") throw new InvalidDataException("not an FVM1/FVM2 model");
            bool withUvs = magic == "FVM2";
            int count = reader.ReadInt32();
            var parts = new List<Part>(count);
            for (int m = 0; m < count; m++)
            {
                string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                int n = reader.ReadInt32();
                var positions = new Vector3[n];
                var normals = new Vector3[n];
                for (int i = 0; i < n; i++) positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                for (int i = 0; i < n; i++) normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Vector2[] uvs = null;
                if (withUvs)
                {
                    uvs = new Vector2[n];
                    for (int i = 0; i < n; i++) uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }

                var triangles = new int[reader.ReadInt32()];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = reader.ReadInt32();
                string stem = name.StartsWith("Textured:") ? name.Substring("Textured:".Length) : null;
                var material = WorldMaterial.Plinth;
                if (stem == null && !Enum.TryParse(name, out material))
                {
                    Debug.LogWarning($"Baked model: unknown material '{name}', drawn as Plinth");
                    material = WorldMaterial.Plinth;
                }

                parts.Add(new Part { Material = material, Positions = positions, Normals = normals, Triangles = triangles, Uvs = uvs, TextureStem = stem });
            }

            return parts.ToArray();
        }
    }
}
