using System;
using System.Collections.Generic;
using System.Numerics;
using FreeVelo.Core.Avatar;

namespace FreeVelo.Core.World.Gen
{
    public enum SceneryKind
    {
        Spruce,
        Pine,
        Oak,
        Birch,
        Poplar,
        Bush,
        Rock,
        Reed,
        House,
        Barn,
    }

    /// <summary>A model in two levels of detail, one mesh per material, in local space (origin on the ground, front +Z).</summary>
    public sealed class SceneryModel
    {
        internal SceneryModel(Dictionary<WorldMaterial, MeshData> lod0, Dictionary<WorldMaterial, MeshData> lod1)
        {
            Lod0 = lod0;
            Lod1 = lod1;
        }

        /// <summary>Close up.</summary>
        public IReadOnlyDictionary<WorldMaterial, MeshData> Lod0 { get; }

        /// <summary>Far away (a handful of triangles; empty for small things that simply disappear).</summary>
        public IReadOnlyDictionary<WorldMaterial, MeshData> Lod1 { get; }

        public int Triangles(int lod)
        {
            int n = 0;
            foreach (var m in (lod == 0 ? Lod0 : Lod1).Values) n += m.TriangleCount;
            return n;
        }
    }

    /// <summary>
    /// Procedural models of the world: spruce, pine, oak, birch and poplar trees, bushes, rocks, reeds, houses and
    /// barns – a few variants each, low-poly but with organic, irregular shapes. Deterministic and cached.
    /// </summary>
    public static class SceneryModels
    {
        public const int Variants = 3;

        private static readonly Dictionary<(SceneryKind, int), SceneryModel> Cache = new Dictionary<(SceneryKind, int), SceneryModel>();

        public static SceneryModel Get(SceneryKind kind, int variant)
        {
            variant = ((variant % Variants) + Variants) % Variants;
            lock (Cache)
            {
                if (!Cache.TryGetValue((kind, variant), out var model))
                {
                    model = Build(kind, variant);
                    Cache[(kind, variant)] = model;
                }

                return model;
            }
        }

        /// <summary>Footprint radius (keeps other things away).</summary>
        public static double Radius(SceneryKind kind) => kind switch
        {
            SceneryKind.House => 9,
            SceneryKind.Barn => 11,
            SceneryKind.Oak => 4,
            SceneryKind.Spruce => 3,
            SceneryKind.Pine => 3,
            SceneryKind.Birch => 2.5,
            SceneryKind.Poplar => 2.2,
            SceneryKind.Bush => 1.5,
            SceneryKind.Rock => 1.6,
            _ => 1.2,
        };

        private static SceneryModel Build(SceneryKind kind, int v)
        {
            var near = new Parts();
            var far = new Parts();
            var random = new Random(((int)kind * 97) + v + 11);
            switch (kind)
            {
                case SceneryKind.Spruce: Spruce(near, far, v, random); break;
                case SceneryKind.Pine: Pine(near, far, v, random); break;
                case SceneryKind.Oak: Oak(near, far, v, random); break;
                case SceneryKind.Birch: Birch(near, far, v, random); break;
                case SceneryKind.Poplar: Poplar(near, far, v, random); break;
                case SceneryKind.Bush: Bush(near, far, v, random); break;
                case SceneryKind.Rock: Rock(near, far, v, random); break;
                case SceneryKind.Reed: Reed(near, v, random); break;
                case SceneryKind.House: House(near, far, v); break;
                case SceneryKind.Barn: Barn(near, far, v); break;
            }

            return new SceneryModel(near.Done(), far.Done());
        }

        // ------------------------------------------------------------------ trees

        private static void Spruce(Parts near, Parts far, int v, Random random)
        {
            float h = 14f + (v * 3f);
            var needles = v == 1 ? WorldMaterial.SpruceNeedlesDark : WorldMaterial.SpruceNeedles;
            near.Of(WorldMaterial.Bark).Tube(Vector3.Zero, new Vector3(0, h * 0.45f, 0), 0.28f, 0.12f, 7);
            int tiers = 7;
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)(tiers - 1);
                float baseY = (h * 0.12f) + (t * h * 0.72f);
                float radius = (h * 0.24f) * (1f - (t * 0.82f));
                float height = (h * 0.24f) * (1f - (t * 0.35f));
                JaggedCone(near.Of(needles).Mesh, new Vector3(0, baseY, 0), radius, height, 14, 0.7f, radius * 0.22f, (float)random.NextDouble() * 6.3f, random);
            }

            JaggedCone(near.Of(needles).Mesh, new Vector3(0, h * 0.84f, 0), h * 0.06f, h * 0.17f, 8, 0.75f, 0.1f, 0, random);

            far.Of(WorldMaterial.Bark).Tube(Vector3.Zero, new Vector3(0, h * 0.2f, 0), 0.3f, 0.2f, 4, false);
            far.Of(needles).Tube(new Vector3(0, h * 0.1f, 0), new Vector3(0, h * 0.62f, 0), h * 0.24f, h * 0.1f, 7);
            far.Of(needles).Tube(new Vector3(0, h * 0.55f, 0), new Vector3(0, h, 0), h * 0.13f, 0.05f, 6);
        }

        private static void Pine(Parts near, Parts far, int v, Random random)
        {
            float h = 15f + (v * 2.5f);
            float lean = 0.3f + ((float)random.NextDouble() * 0.5f);
            var trunk = new[] { Vector3.Zero, new Vector3(lean * 0.3f, h * 0.35f, 0.1f), new Vector3(lean, h * 0.68f, -0.2f), new Vector3(lean * 1.2f, h * 0.93f, 0) };
            var bark = near.Of(WorldMaterial.PineBark);
            for (int i = 0; i < trunk.Length - 1; i++) bark.Tube(trunk[i], trunk[i + 1], 0.3f - (i * 0.07f), 0.23f - (i * 0.07f), 7, false);

            // Scots pine: a loose crown over the top third, flat tufts on short crooked branches.
            var needles = near.Of(WorldMaterial.PineNeedles);
            int clumps = 8 + (v * 2);
            for (int i = 0; i < clumps; i++)
            {
                float t = i / (float)(clumps - 1);
                var on = Vector3.Lerp(trunk[2], trunk[3], t * 0.9f) + new Vector3(0, -h * 0.05f, 0);
                double a = (i * 2.39996) + random.NextDouble() * 0.5;
                float r = (2.8f - (t * 1.6f)) * (0.8f + ((float)random.NextDouble() * 0.4f));
                var c = on + new Vector3((float)Math.Cos(a) * r, 0.6f + ((float)random.NextDouble() * 0.6f), (float)Math.Sin(a) * r);
                bark.Tube(on, c - new Vector3(0, 0.3f, 0), 0.08f, 0.05f, 4, false);
                Blob(needles, c, new Vector3(1.8f, 0.85f, 1.7f) * (1.05f - (t * 0.3f)), 0.2f, random.Next(), 8, 5);
            }

            Blob(needles, trunk[3] + new Vector3(0, 0.5f, 0), new Vector3(1.6f, 0.9f, 1.6f), 0.2f, random.Next(), 10, 6);

            far.Of(WorldMaterial.PineBark).Tube(Vector3.Zero, trunk[2], 0.27f, 0.15f, 4, false);
            far.Of(WorldMaterial.PineNeedles).Ellipsoid(Vector3.Lerp(trunk[2], trunk[3], 0.5f) + new Vector3(0, 0.4f, 0), new Vector3(3.2f, h * 0.15f, 3.0f), Rot.Identity, 7, 4);
        }

        private static void Oak(Parts near, Parts far, int v, Random random)
        {
            var leaves = v == 0 ? WorldMaterial.LeavesDark : v == 1 ? WorldMaterial.LeavesMid : WorldMaterial.LeavesLight;
            float s = 0.9f + (v * 0.12f);
            var bark = near.Of(WorldMaterial.Bark);
            var fork = new Vector3(0.15f, 2.5f * s, 0.05f);
            bark.Tube(Vector3.Zero, fork, 0.45f * s, 0.36f * s, 9);
            bark.Tube(new Vector3(0, -0.3f, 0), new Vector3(0, 0.6f, 0), 0.62f * s, 0.45f * s, 9, false); // root flare

            // A broad, round crown of many smaller clumps on an ellipsoid shell. The trunk continues into the
            // crown and strong main branches fan out from the fork, so there is no bare gap below the leaves.
            var crown = fork + new Vector3(0, 2.4f * s, 0);
            var shell = new Vector3(3.6f, 2.5f, 3.6f) * s;
            bark.Tube(fork, crown, 0.36f * s, 0.2f * s, 8, false);
            for (int i = 0; i < 4; i++)
            {
                double a = (i * Math.PI / 2) + 0.4;
                var tip = crown + new Vector3((float)Math.Cos(a) * 2.4f * s, -0.6f * s, (float)Math.Sin(a) * 2.4f * s);
                bark.Tube(fork + new Vector3(0, 0.3f * s, 0), tip, 0.2f * s, 0.08f * s, 6, false);
            }

            var foliage = near.Of(leaves);
            Blob(foliage, crown, shell * 0.8f, 0.12f, random.Next(), 12, 8);
            int clumps = 14;
            for (int i = 0; i < clumps; i++)
            {
                // Spread evenly over the upper three quarters of the shell (golden-angle spiral).
                float y = 1f - (1.5f * (i + 0.5f) / clumps);
                float ring = (float)Math.Sqrt(Math.Max(0, 1 - (y * y)));
                double a = i * 2.39996;
                var dir = new Vector3((float)Math.Cos(a) * ring, y, (float)Math.Sin(a) * ring);
                var c = crown + (dir * shell * (0.85f + ((float)random.NextDouble() * 0.15f)));
                float r = (1.5f + ((float)random.NextDouble() * 0.6f)) * s;
                Blob(foliage, c, new Vector3(r, r * 0.82f, r), 0.18f, random.Next(), 8, 6);
            }

            far.Of(WorldMaterial.Bark).Tube(Vector3.Zero, crown, 0.45f * s, 0.25f * s, 4, false);
            Blob(far.Of(leaves), crown, shell * 1.15f, 0.12f, 7, 8, 5);
        }

        private static void Birch(Parts near, Parts far, int v, Random random)
        {
            float h = 12f + (v * 1.5f);
            float bend = 0.3f + ((float)random.NextDouble() * 0.5f);
            var trunk = new[] { Vector3.Zero, new Vector3(bend * 0.4f, h * 0.4f, 0), new Vector3(bend, h * 0.75f, 0.1f), new Vector3(bend * 1.1f, h * 0.95f, 0.1f) };
            var bark = near.Of(WorldMaterial.BirchBark);
            for (int i = 0; i < trunk.Length - 1; i++) bark.Tube(trunk[i], trunk[i + 1], 0.2f - (i * 0.05f), 0.15f - (i * 0.05f), 7, false);

            // Black marks: short dark bands on the white bark.
            var marks = near.Of(WorldMaterial.BirchMarks);
            for (float y = 0.4f; y < h * 0.65f; y += 0.55f + ((float)random.NextDouble() * 0.6f))
            {
                float f = y / (h * 0.4f);
                var c = f < 1 ? Vector3.Lerp(trunk[0], trunk[1], f) : Vector3.Lerp(trunk[1], trunk[2], (y - (h * 0.4f)) / (h * 0.35f));
                float r = (f < 1 ? 0.2f - (0.05f * f) : 0.15f - (0.05f * Math.Min(1, f - 1))) + 0.012f;
                marks.Tube(c, c + new Vector3(0, 0.06f + ((float)random.NextDouble() * 0.1f), 0), r, r, 7, false);
            }

            // Airy, narrow crown of many small clumps hanging off thin branches.
            var foliage = near.Of(WorldMaterial.BirchLeaves);
            int clumps = 13;
            for (int i = 0; i < clumps; i++)
            {
                float t = i / (float)(clumps - 1);
                var on = Vector3.Lerp(trunk[1], trunk[3], 0.15f + (t * 0.85f));
                double a = (i * 2.39996) + random.NextDouble();
                float r = (1.7f - (t * 0.9f)) * (0.7f + ((float)random.NextDouble() * 0.5f));
                var c = on + new Vector3((float)Math.Cos(a) * r, -0.4f, (float)Math.Sin(a) * r);
                bark.Tube(on, c + new Vector3(0, 0.3f, 0), 0.05f, 0.025f, 4, false);
                Blob(foliage, c, new Vector3(1.0f, 1.35f, 1.0f) * (0.8f + ((float)random.NextDouble() * 0.35f)), 0.2f, random.Next(), 7, 5);
            }

            Blob(foliage, trunk[3] + new Vector3(0, 0.2f, 0), new Vector3(0.9f, 1.4f, 0.9f), 0.2f, random.Next(), 8, 6);

            far.Of(WorldMaterial.BirchBark).Tube(Vector3.Zero, trunk[2], 0.2f, 0.1f, 4, false);
            far.Of(WorldMaterial.BirchLeaves).Ellipsoid(Vector3.Lerp(trunk[1], trunk[3], 0.6f), new Vector3(2.2f, 3.8f, 2.2f), Rot.Identity, 7, 4);
        }

        private static void Poplar(Parts near, Parts far, int v, Random random)
        {
            float h = 18f + (v * 2.5f);
            near.Of(WorldMaterial.Bark).Tube(Vector3.Zero, new Vector3(0, h * 0.5f, 0), 0.32f, 0.15f, 7);
            var foliage = near.Of(WorldMaterial.PoplarLeaves);
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                float w = 1.9f * (float)Math.Sin((0.15f + (t * 0.85f)) * Math.PI) + 0.5f;
                var c = new Vector3((float)(random.NextDouble() - 0.5) * 0.5f, (h * 0.18f) + (t * h * 0.72f), (float)(random.NextDouble() - 0.5) * 0.5f);
                Blob(foliage, c, new Vector3(w, h * 0.13f, w), 0.25f, random.Next(), 10, 7);
            }

            far.Of(WorldMaterial.Bark).Tube(Vector3.Zero, new Vector3(0, h * 0.2f, 0), 0.3f, 0.2f, 4, false);
            far.Of(WorldMaterial.PoplarLeaves).Ellipsoid(new Vector3(0, h * 0.55f, 0), new Vector3(2.0f, h * 0.45f, 2.0f), Rot.Identity, 7, 5);
        }

        private static void Bush(Parts near, Parts far, int v, Random random)
        {
            var leaves = near.Of(v == 2 ? WorldMaterial.LeavesLight : WorldMaterial.Bush);
            int blobs = 3 + v;
            for (int i = 0; i < blobs; i++)
            {
                double a = random.NextDouble() * Math.PI * 2;
                float r = (float)random.NextDouble() * 0.8f;
                float s = 0.7f + ((float)random.NextDouble() * 0.5f);
                Blob(leaves, new Vector3((float)Math.Cos(a) * r, 0.45f * s, (float)Math.Sin(a) * r), new Vector3(1.0f, 0.75f, 0.9f) * s, 0.3f, random.Next(), 9, 6);
            }

            if (v == 1)
            {
                // Flowering (elder / hawthorn): small white blossoms on the outside.
                var blossoms = near.Of(WorldMaterial.BushFlowering);
                for (int i = 0; i < 14; i++)
                {
                    double a = random.NextDouble() * Math.PI * 2;
                    var c = new Vector3((float)Math.Cos(a) * 1.0f, 0.6f + ((float)random.NextDouble() * 0.6f), (float)Math.Sin(a) * 0.95f);
                    blossoms.Ellipsoid(c, new Vector3(0.16f, 0.1f, 0.16f), Rot.Identity, 5, 3);
                }
            }

            far.Of(WorldMaterial.Bush).Ellipsoid(new Vector3(0, 0.5f, 0), new Vector3(1.4f, 0.9f, 1.3f), Rot.Identity, 6, 3, upperHalf: true);
        }

        private static void Rock(Parts near, Parts far, int v, Random random)
        {
            var size = new Vector3(1.1f + (v * 0.4f), 0.7f + (v * 0.25f), 0.9f + (v * 0.3f));
            Blob(near.Of(WorldMaterial.Rock), new Vector3(0, size.Y * 0.35f, 0), size, 0.35f, random.Next(), 10, 7);
            Blob(far.Of(WorldMaterial.Rock), new Vector3(0, size.Y * 0.35f, 0), size, 0.2f, 3, 6, 4);
        }

        private static void Reed(Parts near, int v, Random random)
        {
            var mesh = near.Of(WorldMaterial.Reed).Mesh;
            int blades = 16 + (v * 6);
            for (int i = 0; i < blades; i++)
            {
                double a = random.NextDouble() * Math.PI * 2;
                float r = (float)random.NextDouble() * 0.9f;
                var foot = new Vector3((float)Math.Cos(a) * r, -0.1f, (float)Math.Sin(a) * r);
                float height = 1.3f + ((float)random.NextDouble() * 0.9f);
                var lean = new Vector3((float)Math.Cos(a), 0, (float)Math.Sin(a)) * (0.2f + ((float)random.NextDouble() * 0.4f));
                Blade(mesh, foot, foot + new Vector3(0, height * 0.6f, 0) + (lean * 0.4f), foot + new Vector3(0, height, 0) + lean, 0.035f);
            }

            var heads = near.Of(WorldMaterial.Bark);
            for (int i = 0; i < 4 + v; i++)
            {
                double a = random.NextDouble() * Math.PI * 2;
                var c = new Vector3((float)Math.Cos(a) * 0.5f, 1.5f + ((float)random.NextDouble() * 0.4f), (float)Math.Sin(a) * 0.5f);
                heads.Tube(c, c + new Vector3(0, 0.22f, 0), 0.05f, 0.05f, 5);
            }
        }

        // ------------------------------------------------------------------ buildings

        private static void House(Parts near, Parts far, int v)
        {
            // Variants: 0 white with red roof, 1 cream two storeys with grey roof, 2 brick bungalow with brown roof.
            float w = v == 1 ? 11f : v == 2 ? 9.5f : 10f;
            float d = v == 1 ? 9f : 8f;
            float wallTop = v == 1 ? 6.2f : v == 2 ? 3.4f : 4.4f;
            float pitch = v == 2 ? 30f : 42f;
            var wall = v == 0 ? WorldMaterial.WallWhite : v == 1 ? WorldMaterial.WallCream : WorldMaterial.WallBrick;
            var roof = v == 0 ? WorldMaterial.RoofRed : v == 1 ? WorldMaterial.RoofGrey : WorldMaterial.RoofBrown;
            Building(near, w, d, wallTop, pitch, wall, roof, detailed: true, floors: v == 1 ? 2 : 1, gableWindows: v == 0);
            Building(far, w, d, wallTop, pitch, wall, roof, detailed: false, floors: 0, gableWindows: false);
            if (v != 2)
            {
                // Chimney
                float ridge = wallTop + ((d / 2) * (float)Math.Tan(pitch * Math.PI / 180));
                near.Of(WorldMaterial.WallBrick).Box(new Vector3(w * 0.25f, ridge - 0.3f, -d * 0.12f), new Vector3(0.6f, 2.4f, 0.6f), Rot.Identity);
            }
        }

        private static void Barn(Parts near, Parts far, int v)
        {
            float w = 16f, d = 10f, wallTop = 5f, pitch = 35f;
            var roof = v == 0 ? WorldMaterial.RoofGrey : v == 1 ? WorldMaterial.RoofRed : WorldMaterial.RoofBrown;
            Building(near, w, d, wallTop, pitch, WorldMaterial.WallWood, roof, detailed: false, floors: 0, gableWindows: false);
            Building(far, w, d, wallTop, pitch, WorldMaterial.WallWood, roof, detailed: false, floors: 0, gableWindows: false);

            // Big gate on the front, vertical boards.
            var gate = near.Of(WorldMaterial.Door);
            gate.Box(new Vector3(0, 0.5f + 1.9f, (d / 2) + 0.05f), new Vector3(4.2f, 3.8f, 0.12f), Rot.Identity);
            var frame = near.Of(WorldMaterial.Frame);
            frame.Box(new Vector3(0, 0.5f + 3.85f, (d / 2) + 0.08f), new Vector3(4.6f, 0.18f, 0.14f), Rot.Identity);
            frame.Box(new Vector3(-2.2f, 0.5f + 1.9f, (d / 2) + 0.08f), new Vector3(0.18f, 3.8f, 0.14f), Rot.Identity);
            frame.Box(new Vector3(2.2f, 0.5f + 1.9f, (d / 2) + 0.08f), new Vector3(0.18f, 3.8f, 0.14f), Rot.Identity);
            frame.Box(new Vector3(0, 0.5f + 1.9f, (d / 2) + 0.09f), new Vector3(0.12f, 3.8f, 0.14f), Rot.Identity);
            for (int i = -1; i <= 1; i += 2) Window(near, new Vector3(i * 5.5f, 3.2f, (d / 2) + 0.02f), 0.9f, 0.7f, Rot.Identity, cross: false);
        }

        /// <summary>
        /// A house body: stone plinth (reaching into slopes), walls, gables, a pitched roof with overhangs; with
        /// <paramref name="detailed"/> also windows with white frames and a front door.
        /// </summary>
        private static void Building(Parts p, float w, float d, float wallTop, float pitch, WorldMaterial wall, WorldMaterial roof, bool detailed, int floors, bool gableWindows)
        {
            float tan = (float)Math.Tan(pitch * Math.PI / 180);
            float ridge = wallTop + ((d / 2) * tan);
            p.Of(WorldMaterial.Plinth).Box(new Vector3(0, -0.9f, 0), new Vector3(w + 0.3f, 2.8f, d + 0.3f), Rot.Identity);
            p.Of(wall).Box(new Vector3(0, (0.5f + wallTop) / 2, 0), new Vector3(w, wallTop - 0.5f, d), Rot.Identity);

            // Gables: triangles on both short ends (x = ±w/2), as thin prisms.
            var gables = p.Of(wall).Mesh;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * w / 2;
                Prism(gables, new Vector3(x, wallTop, -d / 2), new Vector3(x, wallTop, d / 2), new Vector3(x, ridge, 0), new Vector3(side * 0.001f, 0, 0) * 0, side);
            }

            // Roof: two slabs from the eaves (overhang) up to the ridge.
            float overEave = 0.55f, overGable = 0.4f, thick = 0.22f;
            float run = (d / 2) + overEave;
            float slope = run / (float)Math.Cos(pitch * Math.PI / 180);
            float eaveY = wallTop - (overEave * tan);
            for (int side = -1; side <= 1; side += 2)
            {
                var rot = Rot.AboutX(side * pitch);
                var center = new Vector3(0, (eaveY + ridge) / 2, side * run / 2) + (rot * new Vector3(0, thick / 2, 0));
                p.Of(roof).Box(center, new Vector3(w + (2 * overGable), thick, slope), rot);
            }

            if (!detailed) return;

            // Windows on both long sides, per floor.
            float floorH = floors == 2 ? (wallTop - 0.5f) / 2 : wallTop - 0.5f;
            int perRow = Math.Max(2, (int)(w / 2.7f));
            for (int floor = 0; floor < Math.Max(1, floors); floor++)
            {
                float y = 0.5f + (floor * floorH) + (floorH * 0.55f);
                for (int i = 0; i < perRow; i++)
                {
                    float x = -w / 2 + (w * (i + 0.5f) / perRow);
                    // Front: leave the middle free for the door on the ground floor.
                    if (!(floor == 0 && Math.Abs(x) < 1.3f)) Window(p, new Vector3(x, y, (d / 2) + 0.02f), 1.1f, 1.25f, Rot.Identity, cross: true);
                    Window(p, new Vector3(x, y, -(d / 2) - 0.02f), 1.1f, 1.25f, Rot.AboutY(180), cross: true);
                }
            }

            if (gableWindows)
            {
                for (int side = -1; side <= 1; side += 2) Window(p, new Vector3(side * ((w / 2) + 0.02f), wallTop + 0.9f, 0), 0.9f, 1.0f, Rot.AboutY(side * 90), cross: true);
            }

            // Front door with frame and a step.
            p.Of(WorldMaterial.Door).Box(new Vector3(0, 0.5f + 1.05f, (d / 2) + 0.03f), new Vector3(1.05f, 2.1f, 0.08f), Rot.Identity);
            var frame = p.Of(WorldMaterial.Frame);
            frame.Box(new Vector3(0, 0.5f + 2.15f, (d / 2) + 0.05f), new Vector3(1.35f, 0.14f, 0.1f), Rot.Identity);
            frame.Box(new Vector3(-0.6f, 0.5f + 1.05f, (d / 2) + 0.05f), new Vector3(0.14f, 2.2f, 0.1f), Rot.Identity);
            frame.Box(new Vector3(0.6f, 0.5f + 1.05f, (d / 2) + 0.05f), new Vector3(0.14f, 2.2f, 0.1f), Rot.Identity);
            p.Of(WorldMaterial.Plinth).Box(new Vector3(0, 0.38f, (d / 2) + 0.45f), new Vector3(1.8f, 0.24f, 0.9f), Rot.Identity);
        }

        private static void Window(Parts p, Vector3 center, float width, float height, Rot facing, bool cross)
        {
            p.Of(WorldMaterial.Window).Box(center, facing * new Vector3(width, height, 0.06f), Rot.Identity);
            var frame = p.Of(WorldMaterial.Frame);
            float t = 0.09f;
            var out1 = facing * new Vector3(0, 0, 0.03f);
            frame.Box(center + out1 + (facing * new Vector3(0, (height / 2) + (t / 2), 0)), Abs(facing * new Vector3(width + (2 * t), t, 0.09f)), Rot.Identity);
            frame.Box(center + out1 - (facing * new Vector3(0, (height / 2) + (t / 2), 0)), Abs(facing * new Vector3(width + (2 * t) + 0.1f, t * 1.4f, 0.14f)), Rot.Identity);
            frame.Box(center + out1 + (facing * new Vector3((width / 2) + (t / 2), 0, 0)), Abs(facing * new Vector3(t, height, 0.09f)), Rot.Identity);
            frame.Box(center + out1 - (facing * new Vector3((width / 2) + (t / 2), 0, 0)), Abs(facing * new Vector3(t, height, 0.09f)), Rot.Identity);
            if (!cross) return;
            frame.Box(center + out1, Abs(facing * new Vector3(0.06f, height, 0.07f)), Rot.Identity);
            frame.Box(center + out1 + (facing * new Vector3(0, height * 0.12f, 0)), Abs(facing * new Vector3(width, 0.06f, 0.07f)), Rot.Identity);
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));

        // ------------------------------------------------------------------ shapes

        /// <summary>An organic lump: ellipsoid with its surface pushed in and out by noise (same at seams).</summary>
        private static void Blob(MeshBuilder b, Vector3 center, Vector3 radii, float roughness, int seed, int slices, int stacks)
        {
            int start = b.VertexCount;
            b.Ellipsoid(center, radii, Rot.Identity, slices, stacks);
            var verts = b.Mesh.Vertices;
            for (int i = start; i < verts.Count; i++)
            {
                var dir = (verts[i] - center) / radii;
                double n = Noise.Value((dir.X * 1.7) + (dir.Y * 0.6), (dir.Z * 1.7) - (dir.Y * 0.9), seed)
                         + (0.5 * Noise.Value((dir.X * 3.9) - dir.Z, (dir.Y * 3.9) + dir.X, seed + 3));
                float f = 1f + (roughness * (float)n);
                // Flatter underside, as foliage and stones are.
                if (dir.Y < -0.3f) f *= 0.92f;
                verts[i] = center + ((verts[i] - center) * f);
            }
        }

        /// <summary>
        /// One tier of a spruce: a cone with a star-shaped, drooping rim (needled branch tips) and a hollow underside.
        /// </summary>
        private static void JaggedCone(MeshData m, Vector3 baseCenter, float radius, float height, int points, float inner, float droop, float phase, Random random)
        {
            int apex = m.Vertices.Count;
            Add(m, baseCenter + new Vector3(0, height, 0));
            int under = m.Vertices.Count;
            Add(m, baseCenter + new Vector3(0, height * 0.28f, 0));
            int rim = m.Vertices.Count;
            int count = points * 2;
            for (int i = 0; i < count; i++)
            {
                double a = phase + (i * Math.PI * 2 / count);
                bool tip = i % 2 == 0;
                float r = radius * (tip ? 0.9f + (float)(random.NextDouble() * 0.2) : inner);
                float y = tip ? -droop * (0.8f + (float)random.NextDouble() * 0.4f) : droop * 0.25f;
                Add(m, baseCenter + new Vector3((float)Math.Cos(a) * r, y, (float)Math.Sin(a) * r));
            }

            for (int i = 0; i < count; i++)
            {
                int a = rim + i, b = rim + ((i + 1) % count);
                var mid = (m.Vertices[a] + m.Vertices[b]) / 2;
                TriOut(m, apex, a, b, new Vector3(mid.X - baseCenter.X, 0.4f, mid.Z - baseCenter.Z));
                TriOut(m, under, b, a, new Vector3(0, -1, 0));
            }
        }

        /// <summary>A reed blade: thin three-sided strip through three points.</summary>
        private static void Blade(MeshData m, Vector3 a, Vector3 b, Vector3 c, float width)
        {
            var side = Vector3.Normalize(Vector3.Cross(b - a, Vector3.UnitY == Vector3.Normalize(b - a) ? Vector3.UnitX : Vector3.UnitY)) * width;
            int start = m.Vertices.Count;
            Add(m, a - side);
            Add(m, a + side);
            Add(m, b - (side * 0.7f));
            Add(m, b + (side * 0.7f));
            Add(m, c);
            // Both faces, so the blade is visible from every side.
            int[] tris = { 0, 2, 1, 1, 2, 3, 2, 4, 3, 0, 1, 2, 1, 3, 2, 2, 3, 4 };
            foreach (int t in tris) m.Triangles.Add(start + t);
        }

        /// <summary>Gable triangle as a closed thin prism (visible from both sides).</summary>
        private static void Prism(MeshData m, Vector3 a, Vector3 b, Vector3 top, Vector3 unused, int side)
        {
            var o = new Vector3(side * 0.001f, 0, 0);
            int start = m.Vertices.Count;
            Add(m, a + o);
            Add(m, b + o);
            Add(m, top + o);
            TriOut(m, start, start + 1, start + 2, new Vector3(side, 0, 0));
        }

        private static void Add(MeshData m, Vector3 v)
        {
            m.Vertices.Add(v);
            m.Normals.Add(Vector3.Zero);
        }

        /// <summary>Adds a triangle wound so that its front faces <paramref name="outward"/>.</summary>
        private static void TriOut(MeshData m, int a, int b, int c, Vector3 outward)
        {
            var n = Vector3.Cross(m.Vertices[b] - m.Vertices[a], m.Vertices[c] - m.Vertices[a]);
            if (Vector3.Dot(n, outward) < 0) (b, c) = (c, b);
            m.Triangles.Add(a);
            m.Triangles.Add(b);
            m.Triangles.Add(c);
        }

        /// <summary>One mesh builder per material.</summary>
        private sealed class Parts
        {
            private readonly Dictionary<WorldMaterial, MeshBuilder> _builders = new Dictionary<WorldMaterial, MeshBuilder>();

            public MeshBuilder Of(WorldMaterial material)
            {
                if (!_builders.TryGetValue(material, out var b)) _builders[material] = b = new MeshBuilder();
                return b;
            }

            public Dictionary<WorldMaterial, MeshData> Done()
            {
                var result = new Dictionary<WorldMaterial, MeshData>();
                foreach (var pair in _builders)
                {
                    if (pair.Value.VertexCount > 0) result[pair.Key] = pair.Value.Mesh;
                }

                return result;
            }
        }
    }
}
