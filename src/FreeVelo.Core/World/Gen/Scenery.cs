using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Physics;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>One placed model.</summary>
    public struct SceneryItem
    {
        public SceneryKind Kind;
        public int Variant;
        public float X;
        public float Y;
        public float Z;

        /// <summary>Turn about the vertical axis in degrees (the model's front +Z then points to (sin, cos)).</summary>
        public float RotationDeg;

        public float Scale;
    }

    /// <summary>A field: a rotated rectangle draped over the ground.</summary>
    public sealed class FieldPatch
    {
        public FieldPatch(double cx, double cz, double halfLength, double halfWidth, double angle, WorldMaterial crop)
        {
            Cx = cx;
            Cz = cz;
            HalfLength = halfLength;
            HalfWidth = halfWidth;
            Angle = angle;
            Crop = crop;
        }

        public double Cx { get; }

        public double Cz { get; }

        /// <summary>Half size along the field's direction (the rows).</summary>
        public double HalfLength { get; }

        public double HalfWidth { get; }

        /// <summary>Direction of the rows (radians, 0 = +Z, π/2 = +X).</summary>
        public double Angle { get; }

        public WorldMaterial Crop { get; }

        /// <summary>Position in field coordinates: along the rows and across them.</summary>
        public (double U, double V) Local(double x, double z)
        {
            double dx = x - Cx, dz = z - Cz;
            double s = Math.Sin(Angle), c = Math.Cos(Angle);
            return ((dx * s) + (dz * c), (dx * c) - (dz * s));
        }

        public bool Contains(double x, double z, double margin = 0)
        {
            var (u, v) = Local(x, z);
            return Math.Abs(u) <= HalfLength + margin && Math.Abs(v) <= HalfWidth + margin;
        }

        public (double X, double Z) Point(double u, double v)
        {
            double s = Math.Sin(Angle), c = Math.Cos(Angle);
            return (Cx + (u * s) + (v * c), Cz + (u * c) - (v * s));
        }
    }

    /// <summary>
    /// The scenery of the detailed home grid (around the start village), built once at start-up: the same as everywhere
    /// else – villages, farms and huts from the <see cref="Landscape"/> and nature by the altitude zones
    /// (<see cref="MountainLook.Items"/>) – just not streamed. Generating it also lays out the landscape for the rest
    /// of the world (<see cref="Terrain.Landscape"/>).
    /// </summary>
    public sealed class SceneryLayout
    {
        private SceneryLayout()
        {
        }

        public List<SceneryItem> Items { get; } = new List<SceneryItem>();

        /// <summary>Fields within the home grid (draped meshes); outside it, fields are a ground cover.</summary>
        public List<FieldPatch> Fields { get; } = new List<FieldPatch>();

        /// <summary>The nature seed the streamed pieces use too (so the home grid joins them seamlessly).</summary>
        public static int NatureSeed(WorldMap map) => map.Seed + 7;

        public static SceneryLayout Generate(Terrain terrain) => Generate(terrain, DateTime.Now);

        /// <summary><paramref name="today"/>: the maypoles only stand in May.</summary>
        public static SceneryLayout Generate(Terrain terrain, DateTime today)
        {
            var layout = new SceneryLayout();
            terrain.Landscape ??= Landscape.Generate(terrain, today);
            int seed = NatureSeed(terrain.Map);
            int c0x = (int)Math.Floor(terrain.MinX / MountainLook.ChunkSize), c1x = (int)Math.Floor(terrain.MaxX / MountainLook.ChunkSize);
            int c0z = (int)Math.Floor(terrain.MinZ / MountainLook.ChunkSize), c1z = (int)Math.Floor(terrain.MaxZ / MountainLook.ChunkSize);
            for (int cx = c0x; cx <= c1x; cx++)
            {
                for (int cz = c0z; cz <= c1z; cz++)
                {
                    foreach (var item in MountainLook.Items(terrain, cx, cz, seed))
                    {
                        if (terrain.InGrid(item.X, item.Z)) layout.Items.Add(item);
                    }
                }
            }

            layout.Fields.AddRange(terrain.Landscape.Fields.Where(f => terrain.InGrid(f.Cx, f.Cz)));
            return layout;
        }
    }
}
