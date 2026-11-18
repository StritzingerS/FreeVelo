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
        }

        private static readonly Dictionary<(SceneryKind, int), Part[]> Cache = new Dictionary<(SceneryKind, int), Part[]>();

        /// <summary>The baked model, or null when this kind and variant has none.</summary>
        public static Part[] Get(SceneryKind kind, int variant)
        {
            variant = ((variant % SceneryModels.Variants) + SceneryModels.Variants) % SceneryModels.Variants;
            if (Cache.TryGetValue((kind, variant), out var parts)) return parts;
            parts = null;
            var asset = Resources.Load<TextAsset>($"Models/{kind}_{variant}");
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

            Cache[(kind, variant)] = parts;
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
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "FVM1") throw new InvalidDataException("not an FVM1 model");
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
                var triangles = new int[reader.ReadInt32()];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = reader.ReadInt32();
                if (!Enum.TryParse(name, out WorldMaterial material))
                {
                    Debug.LogWarning($"Baked model: unknown material '{name}', drawn as Plinth");
                    material = WorldMaterial.Plinth;
                }

                parts.Add(new Part { Material = material, Positions = positions, Normals = normals, Triangles = triangles });
            }

            return parts.ToArray();
        }
    }
}
