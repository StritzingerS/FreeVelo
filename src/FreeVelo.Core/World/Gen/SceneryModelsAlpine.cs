using System;
using System.Numerics;
using FreeVelo.Core.Avatar;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>
    /// Alpine models: Tyrolean houses (white ground floor, dark wooden upper floor, balconies with geraniums, wide shallow
    /// roofs), farmhouses with the barn under the same roof, a church with an onion dome, inns, shops, hay huts, an
    /// alpine hut, a wayside chapel, a maypole, a slanted wooden fence and the village sign; and the trees of the Alps:
    /// larch, silver fir, beech, Swiss stone pine, mountain pine scrub, sycamore maple, linden, alder, willow and apple.
    /// </summary>
    public static partial class SceneryModels
    {
        private static bool BuildAlpine(SceneryKind kind, Parts near, Parts far, int v, Random random)
        {
            switch (kind)
            {
                case SceneryKind.Larch: Larch(near, far, v, random); return true;
                case SceneryKind.Fir: Fir(near, far, v, random); return true;
                case SceneryKind.Beech: Broadleaf(near, far, v, random, WorldMaterial.BeechBark, WorldMaterial.BeechLeaves, trunk: 4.5f, shell: new Vector3(3.6f, 5.0f, 3.6f), clumps: 16); return true;
                case SceneryKind.Maple: Broadleaf(near, far, v, random, WorldMaterial.Bark, WorldMaterial.MapleLeaves, trunk: 3.2f, shell: new Vector3(4.6f, 4.0f, 4.6f), clumps: 16); return true;
                case SceneryKind.Linden: Broadleaf(near, far, v, random, WorldMaterial.Bark, WorldMaterial.LindenLeaves, trunk: 3.0f, shell: new Vector3(5.2f, 6.4f, 5.2f), clumps: 20, thick: 1.5f); return true;
                case SceneryKind.Alder: Alder(near, far, v, random); return true;
                case SceneryKind.Willow: Willow(near, far, v, random); return true;
                case SceneryKind.Apple: Apple(near, far, v, random); return true;
                case SceneryKind.StonePine: StonePine(near, far, v, random); return true;
                case SceneryKind.MountainPine: MountainPine(near, far, v, random); return true;
                case SceneryKind.AlpineHouse: AlpineHouse(near, far, v); return true;
                case SceneryKind.Farmhouse: Farmhouse(near, far, v); return true;
                case SceneryKind.Church: Church(near, far, v); return true;
                case SceneryKind.Inn: Inn(near, far, v); return true;
                case SceneryKind.Shop: Shop(near, far, v); return true;
                case SceneryKind.HayHut: HayHut(near, far, v); return true;
                case SceneryKind.AlpineHut: AlpineHut(near, far, v, random); return true;
                case SceneryKind.Shrine: Shrine(near, far); return true;
                case SceneryKind.Maypole: Maypole(near, far, random); return true;
                case SceneryKind.Fence: Fence(near); return true;
                case SceneryKind.VillageSign: VillageSign(near); return true;
                case SceneryKind.DirectionSign: DirectionSign(near, v); return true;
                case SceneryKind.PathStone: near.Of(WorldMaterial.Stone).Box(new Vector3(0, 0.04f, 0), new Vector3(1.5f, 0.2f, 4.0f), Rot.Identity); return true;
                case SceneryKind.Yard: near.Of(WorldMaterial.Gravel).Box(new Vector3(0, 0.03f, 0), new Vector3(7f, 0.2f, 6f), Rot.Identity); return true;
                case SceneryKind.GardenFence: GardenFence(near); return true;
                case SceneryKind.FlowerBed: FlowerBed(near, random); return true;
                case SceneryKind.Bench: Bench(near); return true;
                case SceneryKind.BeerTable: BeerTable(near, v); return true;
                case SceneryKind.Workshop: Workshop(near, far, v); return true;
                case SceneryKind.Sawmill: Sawmill(near, far, v, random); return true;
                case SceneryKind.FireStation: FireStation(near, far); return true;
                case SceneryKind.LogPile: LogPile(near, far, v, random); return true;
                case SceneryKind.LumberStack: LumberStack(near, far, v); return true;
                case SceneryKind.Car: Car(near, v); return true;
                case SceneryKind.Lane:
                    near.Of(v == 1 ? WorldMaterial.Gravel : WorldMaterial.LaneAsphalt).Box(new Vector3(0, -0.12f, 0), new Vector3(v == 1 ? 3.0f : 3.4f, 0.3f, 5.4f), Rot.Identity);
                    far.Of(v == 1 ? WorldMaterial.Gravel : WorldMaterial.LaneAsphalt).Box(new Vector3(0, 0.0f, 0), new Vector3(v == 1 ? 3.0f : 3.4f, 0.06f, 5.4f), Rot.Identity);
                    return true;
                default: return false;
            }
        }

        private static double AlpineRadius(SceneryKind kind) => kind switch
        {
            SceneryKind.Church => 17,
            SceneryKind.Farmhouse => 16,
            SceneryKind.Inn => 11,
            SceneryKind.AlpineHouse => 9,
            SceneryKind.Shop => 8,
            SceneryKind.AlpineHut => 7,
            SceneryKind.HayHut => 4.5,
            SceneryKind.Linden => 6,
            SceneryKind.Maple => 5,
            SceneryKind.Beech => 4,
            SceneryKind.Willow => 4,
            SceneryKind.Apple => 3.5,
            SceneryKind.Larch or SceneryKind.Fir or SceneryKind.StonePine or SceneryKind.Alder => 3,
            SceneryKind.MountainPine => 2.5,
            SceneryKind.Shrine => 2.5,
            SceneryKind.Maypole => 1,
            SceneryKind.Fence => 0.5,
            SceneryKind.VillageSign => 1,
            SceneryKind.DirectionSign => 1,
            SceneryKind.Yard => 4,
            SceneryKind.BeerTable => 1.5,
            SceneryKind.Sawmill => 19,
            SceneryKind.Workshop => 10,
            SceneryKind.FireStation => 10,
            SceneryKind.LogPile => 4,
            SceneryKind.LumberStack => 3,
            SceneryKind.Car => 2.5,
            SceneryKind.Lane => 1.7,
            SceneryKind.PathStone or SceneryKind.GardenFence or SceneryKind.FlowerBed or SceneryKind.Bench => 0.5,
            _ => 1.2,
        };

        // ------------------------------------------------------------------ trees

        /// <summary>European larch: slim cone of light-green tufts on drooping branches, open crown.</summary>
        private static void Larch(Parts near, Parts far, int v, Random random)
        {
            float h = 17f + (v * 2.5f);
            var bark = near.Of(WorldMaterial.Bark);
            bark.Tube(Vector3.Zero, new Vector3(0, h * 0.95f, 0), 0.3f, 0.05f, 7);
            var needles = near.Of(WorldMaterial.LarchNeedles);
            int tiers = 13;
            for (int i = 0; i < tiers; i++)
            {
                float t = (i + ((float)random.NextDouble() * 0.6f)) / tiers;
                float y = (h * 0.2f) + (t * h * 0.74f);
                float reach = (h * 0.17f) * (1f - (t * 0.85f)) * (0.75f + ((float)random.NextDouble() * 0.5f));
                int branches = 3 + random.Next(3);
                for (int b = 0; b < branches; b++)
                {
                    double a = (b * Math.PI * 2 / branches) + (i * 1.3) + (random.NextDouble() * 0.8);
                    var dir = new Vector3((float)Math.Cos(a), 0, (float)Math.Sin(a));
                    var root = new Vector3(0, y, 0);
                    var tip = root + (dir * reach) - new Vector3(0, reach * 0.45f, 0); // drooping branches
                    bark.Tube(root, tip, 0.05f, 0.015f, 4, false);
                    // Small, loose tufts along the branch: an airy crown you can see through.
                    for (int k = 1; k <= 3; k++)
                    {
                        float f = k / 3f;
                        float r = 0.32f + (0.18f * (float)random.NextDouble()) + (reach * 0.08f);
                        var c = Vector3.Lerp(root, tip, f) + new Vector3((float)(random.NextDouble() - 0.5) * 0.4f, (float)(random.NextDouble() - 0.6) * 0.3f, (float)(random.NextDouble() - 0.5) * 0.4f);
                        Blob(needles, c, new Vector3(r, r * 0.6f, r), 0.35f, random.Next(), 6, 4);
                    }
                }
            }

            Blob(needles, new Vector3(0, h * 0.95f, 0), new Vector3(0.45f, 0.9f, 0.45f), 0.2f, random.Next(), 6, 5);
            far.Of(WorldMaterial.Bark).Tube(Vector3.Zero, new Vector3(0, h * 0.2f, 0), 0.3f, 0.2f, 4, false);
            far.Of(WorldMaterial.LarchNeedles).Tube(new Vector3(0, h * 0.18f, 0), new Vector3(0, h, 0), h * 0.2f, 0.05f, 6);
        }

        /// <summary>Silver fir: dark, dense tiers, wider and flatter than a spruce; old firs get a flat top.</summary>
        private static void Fir(Parts near, Parts far, int v, Random random)
        {
            float h = 19f + (v * 3f);
            near.Of(WorldMaterial.Bark).Tube(Vector3.Zero, new Vector3(0, h * 0.5f, 0), 0.34f, 0.14f, 7);
            var needles = near.Of(WorldMaterial.FirNeedles).Mesh;
            int tiers = 8;
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)(tiers - 1);
                float baseY = (h * 0.14f) + (t * h * 0.7f);
                float radius = (h * 0.23f) * (1f - (t * (v == 2 ? 0.6f : 0.78f)));
                float height = (h * 0.16f) * (1f - (t * 0.3f));
                JaggedCone(needles, new Vector3(0, baseY, 0), radius, height, 16, 0.72f, radius * 0.12f, (float)random.NextDouble() * 6.3f, random);
            }

            if (v == 2)
            {
                // "Stork's nest": the old fir's flattened top.
                JaggedCone(needles, new Vector3(0, h * 0.86f, 0), h * 0.12f, h * 0.06f, 12, 0.75f, 0.1f, 0, random);
            }
            else
            {
                JaggedCone(needles, new Vector3(0, h * 0.84f, 0), h * 0.06f, h * 0.15f, 8, 0.75f, 0.1f, 0, random);
            }

            far.Of(WorldMaterial.Bark).Tube(Vector3.Zero, new Vector3(0, h * 0.2f, 0), 0.3f, 0.2f, 4, false);
            far.Of(WorldMaterial.FirNeedles).Tube(new Vector3(0, h * 0.12f, 0), new Vector3(0, h * 0.6f, 0), h * 0.23f, h * 0.12f, 7);
            far.Of(WorldMaterial.FirNeedles).Tube(new Vector3(0, h * 0.55f, 0), new Vector3(0, h * 0.97f, 0), h * 0.13f, v == 2 ? h * 0.06f : 0.05f, 6);
        }

        /// <summary>A broadleaf tree: trunk, main branches, a crown of clumps on an ellipsoid shell.</summary>
        private static void Broadleaf(Parts near, Parts far, int v, Random random, WorldMaterial barkMaterial, WorldMaterial leaves, float trunk, Vector3 shell, int clumps, float thick = 1f)
        {
            float s = 0.88f + (v * 0.12f);
            var bark = near.Of(barkMaterial);
            var fork = new Vector3(0.1f, trunk * s, 0.05f);
            bark.Tube(Vector3.Zero, fork, 0.42f * s * thick, 0.33f * s * thick, 9);
            bark.Tube(new Vector3(0, -0.3f, 0), new Vector3(0, 0.6f, 0), 0.6f * s * thick, 0.42f * s * thick, 9, false);
            var crown = fork + new Vector3(0, shell.Y * 0.75f * s, 0);
            bark.Tube(fork, crown, 0.33f * s * thick, 0.16f * s, 8, false);
            for (int i = 0; i < 5; i++)
            {
                double a = (i * Math.PI * 2 / 5) + 0.3;
                var tip = crown + new Vector3((float)Math.Cos(a) * shell.X * 0.6f * s, -shell.Y * 0.25f * s, (float)Math.Sin(a) * shell.Z * 0.6f * s);
                bark.Tube(fork + new Vector3(0, 0.3f * s, 0), tip, 0.2f * s, 0.07f * s, 6, false);
            }

            LobedCrown(near.Of(leaves), crown, shell * s, 7 + (clumps / 4), random);

            far.Of(barkMaterial).Tube(Vector3.Zero, crown, 0.45f * s, 0.25f * s, 4, false);
            Blob(far.Of(leaves), crown, shell * 1.12f * s, 0.12f, 7, 8, 5);
        }

        /// <summary>
        /// A natural crown: several big irregular lobes of different sizes, overlapping inside the crown's volume and
        /// bulging out of it unevenly (no core ball with little balls on it).
        /// </summary>
        private static void LobedCrown(MeshBuilder foliage, Vector3 center, Vector3 shell, int lobes, Random random)
        {
            for (int i = 0; i < lobes; i++)
            {
                // Spread round the crown (golden angle), more of them up and to the sides than underneath.
                float y = 0.75f - (1.25f * (i + 0.5f) / lobes);
                float ring = (float)Math.Sqrt(Math.Max(0, 1 - (y * y)));
                double a = (i * 2.39996) + (random.NextDouble() * 0.6);
                float reach = 0.32f + ((float)random.NextDouble() * 0.28f);
                var c = center + new Vector3((float)Math.Cos(a) * ring * shell.X * reach, y * shell.Y * reach, (float)Math.Sin(a) * ring * shell.Z * reach);
                float k = 0.55f + ((float)random.NextDouble() * 0.25f);
                var radii = new Vector3(shell.X * k * (0.85f + ((float)random.NextDouble() * 0.3f)), shell.Y * k * (0.7f + ((float)random.NextDouble() * 0.25f)), shell.Z * k * (0.85f + ((float)random.NextDouble() * 0.3f)));
                Blob(foliage, c, radii, 0.26f, random.Next(), 9, 6);
            }
        }

        /// <summary>Grey alder: grey trunk, narrow oval crown, dark leaves (along rivers and lakes).</summary>
        private static void Alder(Parts near, Parts far, int v, Random random)
        {
            float h = 12f + (v * 1.5f);
            near.Of(WorldMaterial.BeechBark).Tube(Vector3.Zero, new Vector3(0, h * 0.9f, 0), 0.24f, 0.06f, 7);
            var foliage = near.Of(WorldMaterial.AlderLeaves);
            int clumps = 11;
            for (int i = 0; i < clumps; i++)
            {
                float t = i / (float)(clumps - 1);
                double a = (i * 2.39996) + random.NextDouble();
                float r = (1.9f * (float)Math.Sin((0.2f + (t * 0.8f)) * Math.PI)) + 0.4f;
                var c = new Vector3((float)Math.Cos(a) * r * 0.6f, (h * 0.3f) + (t * h * 0.62f), (float)Math.Sin(a) * r * 0.6f);
                Blob(foliage, c, new Vector3(r * 0.75f, 1.3f, r * 0.75f), 0.22f, random.Next(), 8, 6);
            }

            far.Of(WorldMaterial.BeechBark).Tube(Vector3.Zero, new Vector3(0, h * 0.3f, 0), 0.25f, 0.18f, 4, false);
            far.Of(WorldMaterial.AlderLeaves).Ellipsoid(new Vector3(0, h * 0.62f, 0), new Vector3(2.0f, h * 0.38f, 2.0f), Rot.Identity, 7, 5);
        }

        /// <summary>White willow: several leaning stems, a wide, loose, silvery crown.</summary>
        private static void Willow(Parts near, Parts far, int v, Random random)
        {
            float h = 9f + (v * 1.2f);
            var bark = near.Of(WorldMaterial.Bark);
            var foliage = near.Of(WorldMaterial.WillowLeaves);
            int stems = 3 + (v % 2);
            var crown = new Vector3(0, h * 0.62f, 0);
            for (int i = 0; i < stems; i++)
            {
                double a = (i * Math.PI * 2 / stems) + random.NextDouble();
                var lean = new Vector3((float)Math.Cos(a), 0, (float)Math.Sin(a));
                var top = (lean * (1.4f + ((float)random.NextDouble() * 0.8f))) + new Vector3(0, h * (0.5f + ((float)random.NextDouble() * 0.15f)), 0);
                bark.Tube(lean * 0.2f, top, 0.26f, 0.1f, 6, false);
            }

            // A wide, loose crown of lobes reaching down the sides.
            LobedCrown(foliage, crown - new Vector3(0, h * 0.08f, 0), new Vector3(3.8f, 3.6f, 3.8f), 9, random);

            far.Of(WorldMaterial.Bark).Tube(Vector3.Zero, new Vector3(0, h * 0.4f, 0), 0.3f, 0.2f, 4, false);
            Blob(far.Of(WorldMaterial.WillowLeaves), new Vector3(0, h * 0.7f, 0), new Vector3(3.8f, 2.6f, 3.8f), 0.15f, 5, 8, 5);
        }

        /// <summary>Apple tree: short gnarled trunk, low wide crown, a few red apples.</summary>
        private static void Apple(Parts near, Parts far, int v, Random random)
        {
            float s = 0.85f + (v * 0.12f);
            var bark = near.Of(WorldMaterial.Bark);
            var fork = new Vector3(0.25f, 1.7f * s, -0.1f);
            bark.Tube(Vector3.Zero, fork, 0.24f * s, 0.2f * s, 7);
            var crown = fork + new Vector3(0, 1.6f * s, 0);
            var shell = new Vector3(2.8f, 1.8f, 2.8f) * s;
            for (int i = 0; i < 4; i++)
            {
                double a = (i * Math.PI / 2) + 0.6;
                bark.Tube(fork, crown + new Vector3((float)Math.Cos(a) * 1.8f * s, -0.3f, (float)Math.Sin(a) * 1.8f * s), 0.13f * s, 0.05f * s, 5, false);
            }

            LobedCrown(near.Of(WorldMaterial.AppleLeaves), crown, shell, 6, random);

            var apples = near.Of(WorldMaterial.AppleFruit);
            for (int i = 0; i < 14; i++)
            {
                double a = random.NextDouble() * Math.PI * 2;
                float y = (float)(random.NextDouble() - 0.6) * 1.2f;
                var c = crown + new Vector3((float)Math.Cos(a) * shell.X * 1.02f, y * shell.Y, (float)Math.Sin(a) * shell.Z * 1.02f);
                apples.Sphere(c, 0.11f, 5, 4);
            }

            far.Of(WorldMaterial.Bark).Tube(Vector3.Zero, fork, 0.25f * s, 0.2f * s, 4, false);
            Blob(far.Of(WorldMaterial.AppleLeaves), crown, shell * 1.15f, 0.12f, 9, 7, 5);
        }

        /// <summary>Swiss stone pine (Zirbe): dense, broad columnar crown of dark tufts, often two stems.</summary>
        private static void StonePine(Parts near, Parts far, int v, Random random)
        {
            float h = 12f + (v * 2f);
            var bark = near.Of(WorldMaterial.PineBark);
            bark.Tube(Vector3.Zero, new Vector3(0.2f, h * 0.85f, 0), 0.4f, 0.12f, 7);
            if (v == 1) bark.Tube(new Vector3(0.3f, 1.2f, 0), new Vector3(1.4f, h * 0.7f, 0.3f), 0.25f, 0.08f, 6, false);
            var needles = near.Of(WorldMaterial.StonePineNeedles);
            int clumps = 16;
            for (int i = 0; i < clumps; i++)
            {
                float t = i / (float)(clumps - 1);
                double a = (i * 2.39996) + random.NextDouble();
                float r = 2.2f * (1f - (t * 0.55f));
                var c = new Vector3((float)Math.Cos(a) * r * 0.55f, (h * 0.25f) + (t * h * 0.7f), (float)Math.Sin(a) * r * 0.55f);
                Blob(needles, c, new Vector3(r * 0.85f, 1.4f, r * 0.85f), 0.25f, random.Next(), 8, 6);
            }

            far.Of(WorldMaterial.PineBark).Tube(Vector3.Zero, new Vector3(0, h * 0.3f, 0), 0.4f, 0.3f, 4, false);
            far.Of(WorldMaterial.StonePineNeedles).Ellipsoid(new Vector3(0, h * 0.6f, 0), new Vector3(2.3f, h * 0.42f, 2.3f), Rot.Identity, 7, 5);
        }

        /// <summary>Mountain pine scrub (Latsche): crooked stems creeping over the ground, dense dark tufts, 1.5–2.5 m.</summary>
        private static void MountainPine(Parts near, Parts far, int v, Random random)
        {
            var bark = near.Of(WorldMaterial.PineBark);
            var needles = near.Of(WorldMaterial.StonePineNeedles);
            int stems = 4 + v;
            for (int i = 0; i < stems; i++)
            {
                double a = (i * Math.PI * 2 / stems) + random.NextDouble();
                var dir = new Vector3((float)Math.Cos(a), 0, (float)Math.Sin(a));
                var mid = (dir * 1.2f) + new Vector3(0, 0.5f, 0);
                var tip = (dir * 2.2f) + new Vector3(0, 1.3f + ((float)random.NextDouble() * 0.8f), 0);
                bark.Tube(Vector3.Zero, mid, 0.12f, 0.09f, 5, false);
                bark.Tube(mid, tip, 0.09f, 0.04f, 5, false);
                Blob(needles, tip, new Vector3(1.0f, 0.75f, 1.0f), 0.3f, random.Next(), 7, 5);
                Blob(needles, mid + new Vector3(0, 0.4f, 0), new Vector3(0.9f, 0.6f, 0.9f), 0.3f, random.Next(), 7, 5);
            }

            far.Of(WorldMaterial.StonePineNeedles).Ellipsoid(new Vector3(0, 0.8f, 0), new Vector3(2.6f, 1.2f, 2.6f), Rot.Identity, 7, 3, upperHalf: true);
        }

        // ------------------------------------------------------------------ buildings

        /// <summary>
        /// A Tyrolean house body, gable to the front (+Z): stone plinth, plastered floors, wooden upper floor and gable,
        /// a shallow roof with wide overhangs, windows with shutters, balconies with geraniums on the front (and back).
        /// </summary>
        private static void AlpineBody(Parts p, float w, float d, int plasterFloors, float pitch, WorldMaterial plaster, WorldMaterial roof,
            bool detailed, bool backBalcony, bool sideBalcony, float zOffset = 0)
        {
            const float floorH = 2.9f;
            float wallTop = 0.5f + ((plasterFloors + 1) * floorH);
            float tan = (float)Math.Tan(pitch * Math.PI / 180);
            float ridge = wallTop + ((w / 2) * tan);
            var o = new Vector3(0, 0, zOffset);
            p.Of(WorldMaterial.Plinth).Box(o + new Vector3(0, -0.9f, 0), new Vector3(w + 0.3f, 2.8f, d + 0.3f), Rot.Identity);
            float plasterTop = 0.5f + (plasterFloors * floorH);
            p.Of(plaster).Box(o + new Vector3(0, (0.5f + plasterTop) / 2, 0), new Vector3(w, plasterTop - 0.5f, d), Rot.Identity);
            p.Of(WorldMaterial.WoodDark).Box(o + new Vector3(0, (plasterTop + wallTop) / 2, 0), new Vector3(w + 0.12f, wallTop - plasterTop, d + 0.12f), Rot.Identity);

            // Wooden gables front and back
            var gable = p.Of(WorldMaterial.WoodDark).Mesh;
            for (int side = -1; side <= 1; side += 2)
            {
                float z = zOffset + (side * ((d / 2) + 0.06f));
                int start = gable.Vertices.Count;
                Add(gable, new Vector3(-w / 2, wallTop, z));
                Add(gable, new Vector3(w / 2, wallTop, z));
                Add(gable, new Vector3(0, ridge, z));
                TriOut(gable, start, start + 1, start + 2, new Vector3(0, 0, side));
            }

            // Roof: two slabs from the eaves to the ridge, wide overhangs on all sides.
            float overEave = 1.3f, overGable = 1.6f, thick = 0.3f;
            float run = (w / 2) + overEave;
            float slope = run / (float)Math.Cos(pitch * Math.PI / 180);
            float eaveY = wallTop - (overEave * tan);
            for (int side = -1; side <= 1; side += 2)
            {
                var rot = Rot.AboutZ(-side * pitch);
                var center = o + new Vector3(side * run / 2, (eaveY + ridge) / 2, 0) + (rot * new Vector3(0, thick / 2, 0));
                p.Of(roof).Box(center, new Vector3(slope, thick, d + (2 * overGable)), rot);
            }

            if (!detailed) return;

            // Fascia boards under the roof edge at the gables
            for (int side = -1; side <= 1; side += 2)
            {
                float z = zOffset + (side * ((d / 2) + overGable - 0.05f));
                for (int s2 = -1; s2 <= 1; s2 += 2)
                {
                    var rot = Rot.AboutZ(-s2 * pitch);
                    var c = new Vector3(s2 * run / 2, ((eaveY + ridge) / 2) - 0.12f, z);
                    p.Of(WorldMaterial.WoodLight).Box(c, new Vector3(slope, 0.32f, 0.1f), rot);
                }
            }

            // Windows with shutters: front and back on every floor, sides too.
            for (int floor = 0; floor <= plasterFloors; floor++)
            {
                float y = 0.5f + (floor * floorH) + (floorH * 0.55f);
                int perRow = Math.Max(2, (int)(w / 3.2f));
                for (int i = 0; i < perRow; i++)
                {
                    float x = -w / 2 + (w * (i + 0.5f) / perRow);
                    if (!(floor == 0 && Math.Abs(x) < 1.4f)) ShutteredWindow(p, o + new Vector3(x, y, (d / 2) + 0.07f), Rot.Identity);
                    ShutteredWindow(p, o + new Vector3(x, y, -(d / 2) - 0.07f), Rot.AboutY(180));
                }

                int perSide = Math.Max(2, (int)(d / 3.6f));
                for (int i = 0; i < perSide; i++)
                {
                    float z = -d / 2 + (d * (i + 0.5f) / perSide);
                    ShutteredWindow(p, o + new Vector3((w / 2) + 0.07f, y, z), Rot.AboutY(90));
                    ShutteredWindow(p, o + new Vector3(-(w / 2) - 0.07f, y, z), Rot.AboutY(-90));
                }
            }

            // Gable window
            ShutteredWindow(p, o + new Vector3(0, wallTop + 1.0f, (d / 2) + 0.1f), Rot.Identity);

            // Balconies with geraniums: front (and back) on the upper floors, sides optional.
            for (int floor = 1; floor <= plasterFloors; floor++)
            {
                float y = 0.5f + (floor * floorH);
                Balcony(p, o + new Vector3(0, y, (d / 2) + 0.75f), w + 0.4f, Rot.Identity);
                if (backBalcony) Balcony(p, o + new Vector3(0, y, -(d / 2) - 0.75f), w + 0.4f, Rot.AboutY(180));
                if (sideBalcony) Balcony(p, o + new Vector3((w / 2) + 0.75f, y, 0), d * 0.7f, Rot.AboutY(90));
            }

            // Front door with a little roof
            p.Of(WorldMaterial.Door).Box(o + new Vector3(0, 0.5f + 1.1f, (d / 2) + 0.04f), new Vector3(1.15f, 2.2f, 0.08f), Rot.Identity);
            p.Of(WorldMaterial.WoodLight).Box(o + new Vector3(0, 0.5f + 2.3f, (d / 2) + 0.06f), new Vector3(1.45f, 0.16f, 0.12f), Rot.Identity);
            p.Of(WorldMaterial.Plinth).Box(o + new Vector3(0, 0.38f, (d / 2) + 0.5f), new Vector3(1.9f, 0.24f, 1.0f), Rot.Identity);

            // Chimney
            p.Of(plaster).Box(o + new Vector3(w * 0.22f, ridge - 0.2f, -d * 0.15f), new Vector3(0.7f, 2.0f, 0.7f), Rot.Identity);
            p.Of(WorldMaterial.RoofGrey).Box(o + new Vector3(w * 0.22f, ridge + 0.9f, -d * 0.15f), new Vector3(1.0f, 0.12f, 1.0f), Rot.Identity);
        }

        /// <summary>A window with a white frame and two shutters (wooden or green).</summary>
        private static void ShutteredWindow(Parts p, Vector3 center, Rot facing)
        {
            Window(p, center, 0.95f, 1.15f, facing, cross: true);
            var shutter = p.Of(WorldMaterial.Shutter);
            for (int s = -1; s <= 1; s += 2)
            {
                var c = center + (facing * new Vector3(s * 0.85f, 0, 0.02f));
                shutter.Box(c, Abs(facing * new Vector3(0.5f, 1.3f, 0.06f)), Rot.Identity);
            }
        }

        /// <summary>Wooden balcony: floor, posts, top rail and a box of red geraniums along it.</summary>
        private static void Balcony(Parts p, Vector3 floorCenter, float length, Rot facing)
        {
            var wood = p.Of(WorldMaterial.WoodLight);
            wood.Box(floorCenter, Abs(facing * new Vector3(length, 0.16f, 1.4f)), Rot.Identity);
            var front = floorCenter + (facing * new Vector3(0, 0, 0.66f));
            wood.Box(front + new Vector3(0, 1.0f, 0), Abs(facing * new Vector3(length, 0.1f, 0.12f)), Rot.Identity);
            wood.Box(front + new Vector3(0, 0.25f, 0), Abs(facing * new Vector3(length, 0.08f, 0.08f)), Rot.Identity);
            int posts = Math.Max(4, (int)(length / 0.32f));
            for (int i = 0; i <= posts; i++)
            {
                float x = -length / 2 + (length * i / posts);
                wood.Box(front + (facing * new Vector3(x, 0.55f, 0)), Abs(facing * new Vector3(0.09f, 0.9f, 0.05f)), Rot.Identity);
            }

            // Geraniums: a green box with red blossoms on top of the rail
            var flowerBox = front + new Vector3(0, 1.18f, 0) + (facing * new Vector3(0, 0, 0.12f));
            p.Of(WorldMaterial.LeavesMid).Box(flowerBox, Abs(facing * new Vector3(length * 0.96f, 0.28f, 0.34f)), Rot.Identity);
            p.Of(WorldMaterial.Geranium).Box(flowerBox + new Vector3(0, 0.2f, 0) + (facing * new Vector3(0, 0, 0.05f)), Abs(facing * new Vector3(length * 0.94f, 0.18f, 0.32f)), Rot.Identity);
        }

        /// <summary>Simple version of a body for distance: plinth, walls, roof.</summary>
        private static void AlpineBodyFar(Parts p, float w, float d, int plasterFloors, float pitch, WorldMaterial plaster, WorldMaterial roof, float zOffset = 0) =>
            AlpineBody(p, w, d, plasterFloors, pitch, plaster, roof, detailed: false, backBalcony: false, sideBalcony: false, zOffset);

        private static void AlpineHouse(Parts near, Parts far, int v)
        {
            // 0: white, red-brown roof; 1: cream, bigger, two plastered floors; 2: white, small, dark shingle roof
            float w = v == 1 ? 11.5f : v == 2 ? 9f : 10.5f;
            float d = v == 1 ? 13f : v == 2 ? 10f : 12f;
            var plaster = v == 1 ? WorldMaterial.WallCream : WorldMaterial.WallWhite;
            var roof = v == 2 ? WorldMaterial.Shingle : WorldMaterial.RoofBrown;
            int floors = v == 1 ? 2 : 1;
            AlpineBody(near, w, d, floors, 23f, plaster, roof, detailed: true, backBalcony: v == 1, sideBalcony: v == 0);
            AlpineBodyFar(far, w, d, floors, 23f, plaster, roof);
        }

        private static void Inn(Parts near, Parts far, int v)
        {
            // Gasthof: big, three storeys, balconies all round, a hanging sign at the corner.
            float w = 14f, d = 17f;
            var plaster = v == 1 ? WorldMaterial.WallOchre : WorldMaterial.WallCream;
            AlpineBody(near, w, d, 2, 22f, plaster, WorldMaterial.RoofBrown, detailed: true, backBalcony: true, sideBalcony: true);
            AlpineBodyFar(far, w, d, 2, 22f, plaster, WorldMaterial.RoofBrown);
            var corner = new Vector3((w / 2) + 0.1f, 4.2f, (d / 2) - 1.2f);
            near.Of(WorldMaterial.Iron).Box(corner + new Vector3(0.9f, 0.5f, 0), new Vector3(1.9f, 0.08f, 0.08f), Rot.Identity);
            near.Of(WorldMaterial.SignBoard).Box(corner + new Vector3(1.4f, -0.2f, 0), new Vector3(1.1f, 1.1f, 0.08f), Rot.Identity);
            near.Of(WorldMaterial.Gold).Box(corner + new Vector3(1.4f, -0.2f, 0), new Vector3(0.7f, 0.7f, 0.12f), Rot.Identity);
            // Benches and a table in front (the terrace)
            for (int i = -1; i <= 1; i += 2)
            {
                near.Of(WorldMaterial.WoodLight).Box(new Vector3(i * 3.5f, 0.75f, (d / 2) + 2.5f), new Vector3(2.2f, 0.08f, 0.9f), Rot.Identity);
                near.Of(WorldMaterial.WoodLight).Box(new Vector3(i * 3.5f, 0.4f, (d / 2) + 2.5f), new Vector3(0.12f, 0.7f, 0.12f), Rot.Identity);
            }
        }

        private static void Shop(Parts near, Parts far, int v)
        {
            // House with a shop on the ground floor: big display windows, an awning and a sign board.
            float w = 10.5f, d = 12f;
            var plaster = v == 2 ? WorldMaterial.WallOchre : WorldMaterial.WallWhite;
            AlpineBody(near, w, d, 1, 23f, plaster, WorldMaterial.RoofBrown, detailed: true, backBalcony: false, sideBalcony: false);
            AlpineBodyFar(far, w, d, 1, 23f, plaster, WorldMaterial.RoofBrown);
            for (int s = -1; s <= 1; s += 2)
            {
                Window(near, new Vector3(s * 2.9f, 1.75f, (d / 2) + 0.09f), 2.6f, 1.7f, Rot.Identity, cross: false);
            }

            var awning = v == 1 ? WorldMaterial.Shutter : WorldMaterial.Awning;
            near.Of(awning).Box(new Vector3(0, 3.0f, (d / 2) + 0.9f), new Vector3(w - 0.6f, 0.12f, 1.8f), Rot.AboutX(-14));
            near.Of(WorldMaterial.SignBoard).Box(new Vector3(0, 3.45f, (d / 2) + 0.1f), new Vector3(4.2f, 0.55f, 0.08f), Rot.Identity);
        }

        private static void Farmhouse(Parts near, Parts far, int v)
        {
            // Einhof: the living part in front (gable to the road), the barn behind it under one long roof.
            float w = 12.5f, living = 12f, barn = 15f + (v * 2f);
            float total = living + barn;
            float zLiving = (total / 2) - (living / 2), zBarn = -(total / 2) + (barn / 2);
            var plaster = v == 1 ? WorldMaterial.WallCream : WorldMaterial.WallWhite;
            var roof = v == 2 ? WorldMaterial.Shingle : WorldMaterial.RoofBrown;
            foreach (var (p, detailed) in new[] { (near, true), (far, false) })
            {
                // One long roof over both parts: build the whole length as a body, the barn walls drawn over it.
                AlpineBody(p, w, total, 1, 22f, plaster, roof, detailed: false, backBalcony: false, sideBalcony: false);
                if (!detailed) continue;
                AlpineBody(p, w - 0.02f, living - 0.2f, 1, 22f, plaster, roof, detailed: true, backBalcony: false, sideBalcony: true, zOffset: zLiving);
                // Barn: stone below, boards above, a big gate on the side and on the back
                p.Of(WorldMaterial.Stone).Box(new Vector3(0, 1.95f, zBarn), new Vector3(w + 0.16f, 2.9f, barn), Rot.Identity);
                p.Of(WorldMaterial.WoodDark).Box(new Vector3(0, 4.85f, zBarn), new Vector3(w + 0.2f, 2.9f, barn), Rot.Identity);
                p.Of(WorldMaterial.Door).Box(new Vector3((w / 2) + 0.12f, 4.6f, zBarn), new Vector3(0.1f, 3.0f, 3.6f), Rot.Identity);
                p.Of(WorldMaterial.Door).Box(new Vector3(0, 2.0f, -(total / 2) - 0.12f), new Vector3(3.2f, 3.0f, 0.1f), Rot.Identity);
                // Ramp up to the hay floor
                p.Of(WorldMaterial.Stone).Box(new Vector3((w / 2) + 2.6f, 1.8f, zBarn), new Vector3(5.0f, 0.5f, 4.0f), Rot.AboutZ(-25));
                for (int i = 0; i < 4; i++) Window(p, new Vector3((w / 2) + 0.2f, 1.8f, zBarn - (barn / 2) + 2 + (i * 3.4f)), 0.5f, 0.4f, Rot.AboutY(90), cross: false);
                // Woodpile under the eaves
                p.Of(WorldMaterial.WoodLight).Box(new Vector3(-(w / 2) - 0.6f, 1.2f, zLiving - 3), new Vector3(0.9f, 1.4f, 4.0f), Rot.Identity);
            }
        }

        private static void Church(Parts near, Parts far, int v)
        {
            // Nave (white, steep roof), choir with a lower roof at the back, the tower at the front with an onion dome.
            float w = 13f, d = 24f, wallTop = 9.5f, pitch = 44f;
            var roof = v == 1 ? WorldMaterial.RoofGrey : WorldMaterial.Shingle;
            foreach (var (p, detailed) in new[] { (near, true), (far, false) })
            {
                p.Of(WorldMaterial.Plinth).Box(new Vector3(0, -0.9f, 0), new Vector3(w + 0.4f, 2.8f, d + 0.4f), Rot.Identity);
                p.Of(WorldMaterial.WallWhite).Box(new Vector3(0, (0.5f + wallTop) / 2, 0), new Vector3(w, wallTop - 0.5f, d), Rot.Identity);
                float tan = (float)Math.Tan(pitch * Math.PI / 180);
                float ridge = wallTop + ((w / 2) * tan);
                var gable = p.Of(WorldMaterial.WallWhite).Mesh;
                for (int side = -1; side <= 1; side += 2)
                {
                    float z = side * (d / 2);
                    int start = gable.Vertices.Count;
                    Add(gable, new Vector3(-w / 2, wallTop, z));
                    Add(gable, new Vector3(w / 2, wallTop, z));
                    Add(gable, new Vector3(0, ridge, z));
                    TriOut(gable, start, start + 1, start + 2, new Vector3(0, 0, side));
                }

                float run = (w / 2) + 0.5f, slope = run / (float)Math.Cos(pitch * Math.PI / 180), eaveY = wallTop - (0.5f * tan);
                for (int side = -1; side <= 1; side += 2)
                {
                    var rot = Rot.AboutZ(-side * pitch);
                    p.Of(roof).Box(new Vector3(side * run / 2, (eaveY + ridge) / 2, 0) + (rot * new Vector3(0, 0.15f, 0)), new Vector3(slope, 0.3f, d + 0.8f), rot);
                }

                // Choir: narrower and lower, at the back, with a half-round end
                float cw = 8.5f, cd = 6f, cTop = 8f;
                p.Of(WorldMaterial.WallWhite).Box(new Vector3(0, (0.5f + cTop) / 2, -(d / 2) - (cd / 2)), new Vector3(cw, cTop - 0.5f, cd), Rot.Identity);
                p.Of(WorldMaterial.WallWhite).Tube(new Vector3(0, 0.5f, -(d / 2) - cd), new Vector3(0, cTop, -(d / 2) - cd), cw / 2, cw / 2, 12, false);
                // Choir roof: a lower gable roof, and a cone over the half-round end
                float cRidge = cTop + ((cw / 2) * tan), cRun = (cw / 2) + 0.4f, cSlope = cRun / (float)Math.Cos(pitch * Math.PI / 180);
                for (int side = -1; side <= 1; side += 2)
                {
                    var rot = Rot.AboutZ(-side * pitch);
                    p.Of(roof).Box(new Vector3(side * cRun / 2, ((cTop - (0.4f * tan)) + cRidge) / 2, -(d / 2) - (cd / 2)) + (rot * new Vector3(0, 0.15f, 0)), new Vector3(cSlope, 0.3f, cd + 0.2f), rot);
                }

                p.Of(roof).Tube(new Vector3(0, cTop - 0.3f, -(d / 2) - cd), new Vector3(0, cRidge + 0.1f, -(d / 2) - cd), cRun, 0.05f, 12, false);

                // Tower at the front, centred on the gable
                float tw = 5.4f, tz = (d / 2) + (tw / 2) - 0.4f, tTop = 25f;
                p.Of(WorldMaterial.Plinth).Box(new Vector3(0, -0.9f, tz), new Vector3(tw + 0.4f, 2.8f, tw + 0.4f), Rot.Identity);
                p.Of(WorldMaterial.WallWhite).Box(new Vector3(0, (0.5f + tTop) / 2, tz), new Vector3(tw, tTop - 0.5f, tw), Rot.Identity);
                p.Of(WorldMaterial.WallOchre).Box(new Vector3(0, tTop - 0.2f, tz), new Vector3(tw + 0.3f, 0.4f, tw + 0.3f), Rot.Identity);
                OnionDome(p, new Vector3(0, tTop, tz), tw, detailed);

                if (!detailed) continue;

                // Corner pilasters on the tower, belfry openings and clocks on all four sides
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        p.Of(WorldMaterial.WallOchre).Box(new Vector3(sx * ((tw / 2) - 0.2f), tTop / 2, tz + (sz * ((tw / 2) - 0.2f))), new Vector3(0.5f, tTop, 0.5f), Rot.Identity);
                    }
                }

                for (int f = 0; f < 4; f++)
                {
                    var face = Rot.AboutY(f * 90);
                    var c = new Vector3(0, 0, tz) + (face * new Vector3(0, 0, (tw / 2) + 0.03f));
                    p.Of(WorldMaterial.Window).Box(c + new Vector3(0, tTop - 3.2f, 0), Abs(face * new Vector3(1.2f, 2.6f, 0.06f)), Rot.Identity);
                    p.Of(WorldMaterial.Frame).Box(c + new Vector3(0, tTop - 7.0f, 0) + (face * new Vector3(0, 0, 0.03f)), Abs(face * new Vector3(2.2f, 2.2f, 0.06f)), Rot.Identity);
                    p.Of(WorldMaterial.Iron).Box(c + new Vector3(0, tTop - 6.7f, 0) + (face * new Vector3(0, 0, 0.07f)), Abs(face * new Vector3(0.12f, 0.8f, 0.04f)), Rot.Identity);
                    p.Of(WorldMaterial.Iron).Box(c + new Vector3(0.25f, tTop - 7.0f, 0) + (face * new Vector3(0, 0, 0.07f)), Abs(face * new Vector3(0.6f, 0.12f, 0.04f)), Rot.Identity);
                }

                // Tall arched windows along the nave
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float z = -d / 2 + 3.5f + (i * 5.6f);
                        var face = Rot.AboutY(side * 90);
                        var c = new Vector3(side * ((w / 2) + 0.03f), 5.2f, z);
                        p.Of(WorldMaterial.Window).Box(c, Abs(face * new Vector3(1.3f, 3.8f, 0.06f)), Rot.Identity);
                        p.Of(WorldMaterial.Window).Ellipsoid(c + new Vector3(0, 1.9f, 0), Abs(face * new Vector3(0.65f, 0.65f, 0.04f)), Rot.Identity, 8, 4, upperHalf: true);
                    }
                }

                // Door in the tower
                p.Of(WorldMaterial.Door).Box(new Vector3(0, 2.1f, tz + (tw / 2) + 0.05f), new Vector3(1.8f, 3.2f, 0.1f), Rot.Identity);
                p.Of(WorldMaterial.Door).Ellipsoid(new Vector3(0, 3.7f, tz + (tw / 2) + 0.05f), new Vector3(0.9f, 0.9f, 0.05f), Rot.Identity, 8, 4, upperHalf: true);

                // Low graveyard wall round the church
                float gx = (w / 2) + 6, gz0 = -(d / 2) - cd - 5, gz1 = (d / 2) + 2;
                var wall = p.Of(WorldMaterial.WallWhite);
                wall.Box(new Vector3(-gx, 0.6f, (gz0 + gz1) / 2), new Vector3(0.45f, 1.2f, gz1 - gz0), Rot.Identity);
                wall.Box(new Vector3(gx, 0.6f, (gz0 + gz1) / 2), new Vector3(0.45f, 1.2f, gz1 - gz0), Rot.Identity);
                wall.Box(new Vector3(0, 0.6f, gz0), new Vector3((2 * gx) + 0.45f, 1.2f, 0.45f), Rot.Identity);
                for (int s = -1; s <= 1; s += 2) wall.Box(new Vector3(s * ((gx + (tw / 2) + 1.5f) / 2), 0.6f, gz1), new Vector3(gx - (tw / 2) - 1.5f, 1.2f, 0.45f), Rot.Identity);
                for (int i = 0; i < 8; i++)
                {
                    float x = (i % 2 == 0 ? -1 : 1) * ((w / 2) + 2.5f + (i % 3));
                    float z = gz0 + 3 + (i * 3.6f);
                    p.Of(WorldMaterial.Iron).Box(new Vector3(x, 0.8f, z), new Vector3(0.08f, 1.0f, 0.08f), Rot.Identity);
                    p.Of(WorldMaterial.Iron).Box(new Vector3(x, 1.05f, z), new Vector3(0.45f, 0.08f, 0.08f), Rot.Identity);
                }
            }
        }

        /// <summary>Onion dome: square drum roof, the bulb, a lantern, a second small bulb, spire, golden ball and cross.</summary>
        private static void OnionDome(Parts p, Vector3 b, float tw, bool detailed)
        {
            var copper = p.Of(WorldMaterial.RoofCopper);
            var profile = new (float Y, float R)[]
            {
                (0.0f, tw * 0.62f), (0.6f, tw * 0.5f), (1.6f, tw * 0.52f), (2.6f, tw * 0.48f), (3.6f, tw * 0.25f), (4.2f, 0.45f),
                (4.6f, 0.55f), (5.6f, 0.6f), (6.0f, 0.35f), (6.6f, 0.8f), (7.3f, 0.5f), (7.9f, 0.1f), (9.2f, 0.05f),
            };
            var rings = new System.Collections.Generic.List<Ring>();
            foreach (var (y, r) in profile) rings.Add(Ring.E(b + new Vector3(0, y, 0), r, r));
            copper.SmoothSweep(rings, Vector3.UnitX, detailed ? 12 : 8, detailed ? 3 : 1);
            if (!detailed) return;
            var gold = p.Of(WorldMaterial.Gold);
            gold.Sphere(b + new Vector3(0, 8.5f, 0), 0.22f, 8, 6);
            gold.Box(b + new Vector3(0, 9.6f, 0), new Vector3(0.1f, 1.4f, 0.1f), Rot.Identity);
            gold.Box(b + new Vector3(0, 9.8f, 0), new Vector3(0.75f, 0.1f, 0.1f), Rot.Identity);
        }

        private static void HayHut(Parts near, Parts far, int v)
        {
            // Heustadel: dark weathered boards, shallow shingle roof, on stone corners.
            float w = 5f + v, d = 6.5f + v, wallTop = 3.2f, pitch = 26f;
            foreach (var (p, detailed) in new[] { (near, true), (far, false) })
            {
                p.Of(WorldMaterial.Stone).Box(new Vector3(0, -0.4f, 0), new Vector3(w + 0.2f, 1.2f, d + 0.2f), Rot.Identity);
                p.Of(WorldMaterial.WoodDark).Box(new Vector3(0, (0.2f + wallTop) / 2, 0), new Vector3(w, wallTop - 0.2f, d), Rot.Identity);
                float tan = (float)Math.Tan(pitch * Math.PI / 180), ridge = wallTop + ((w / 2) * tan);
                var gable = p.Of(WorldMaterial.WoodDark).Mesh;
                for (int side = -1; side <= 1; side += 2)
                {
                    int start = gable.Vertices.Count;
                    Add(gable, new Vector3(-w / 2, wallTop, side * d / 2));
                    Add(gable, new Vector3(w / 2, wallTop, side * d / 2));
                    Add(gable, new Vector3(0, ridge, side * d / 2));
                    TriOut(gable, start, start + 1, start + 2, new Vector3(0, 0, side));
                }

                float run = (w / 2) + 0.6f, slope = run / (float)Math.Cos(pitch * Math.PI / 180), eaveY = wallTop - (0.6f * tan);
                for (int side = -1; side <= 1; side += 2)
                {
                    var rot = Rot.AboutZ(-side * pitch);
                    p.Of(WorldMaterial.Shingle).Box(new Vector3(side * run / 2, (eaveY + ridge) / 2, 0) + (rot * new Vector3(0, 0.12f, 0)), new Vector3(slope, 0.24f, d + 1.0f), rot);
                }

                if (detailed) p.Of(WorldMaterial.Door).Box(new Vector3(0, 1.3f, (d / 2) + 0.04f), new Vector3(1.8f, 2.0f, 0.08f), Rot.Identity);
            }
        }

        private static void AlpineHut(Parts near, Parts far, int v, Random random)
        {
            // Almhütte: stone ground floor, wooden upper part, shingle roof held down by stones, a bench in front.
            float w = 8f, d = 10f;
            AlpineBody(near, w, d, 0, 24f, WorldMaterial.Stone, WorldMaterial.Shingle, detailed: true, backBalcony: false, sideBalcony: false);
            AlpineBodyFar(far, w, d, 0, 24f, WorldMaterial.Stone, WorldMaterial.Shingle);
            near.Of(WorldMaterial.Stone).Box(new Vector3(0, 1.6f, 0), new Vector3(w + 0.1f, 2.4f, d + 0.1f), Rot.Identity);
            float tan = (float)Math.Tan(24 * Math.PI / 180);
            for (int i = 0; i < 6; i++)
            {
                float x = ((i % 3) - 1) * 2.4f, side = i < 3 ? 1 : -1;
                float y = 0.5f + 2.9f + ((w / 2 - Math.Abs(x)) * tan) + 0.4f;
                Blob(near.Of(WorldMaterial.Rock), new Vector3(side * Math.Abs(x) * 0.9f + (side * 1.2f), y - (1.2f * tan), (i * 1.7f) - 4.2f), new Vector3(0.35f, 0.25f, 0.35f), 0.3f, random.Next(), 6, 4);
            }

            near.Of(WorldMaterial.WoodLight).Box(new Vector3(0, 0.95f, (d / 2) + 0.6f), new Vector3(3.0f, 0.08f, 0.45f), Rot.Identity);
        }

        private static void Shrine(Parts near, Parts far)
        {
            // Wayside chapel: small white body, steep shingle roof, an open front with a dark niche, a cross on top.
            foreach (var (p, detailed) in new[] { (near, true), (far, false) })
            {
                p.Of(WorldMaterial.Plinth).Box(new Vector3(0, -0.3f, 0), new Vector3(2.4f, 1.0f, 2.9f), Rot.Identity);
                p.Of(WorldMaterial.WallWhite).Box(new Vector3(0, 1.6f, 0), new Vector3(2.2f, 2.8f, 2.7f), Rot.Identity);
                for (int side = -1; side <= 1; side += 2)
                {
                    var rot = Rot.AboutZ(-side * 50);
                    p.Of(WorldMaterial.Shingle).Box(new Vector3(side * 0.7f, 3.55f, 0), new Vector3(2.05f, 0.18f, 3.3f), rot);
                }

                if (!detailed) continue;
                p.Of(WorldMaterial.Window).Box(new Vector3(0, 1.6f, 1.36f), new Vector3(1.1f, 1.7f, 0.04f), Rot.Identity);
                p.Of(WorldMaterial.Window).Ellipsoid(new Vector3(0, 2.45f, 1.36f), new Vector3(0.55f, 0.55f, 0.03f), Rot.Identity, 8, 4, upperHalf: true);
                p.Of(WorldMaterial.Gold).Box(new Vector3(0, 4.6f, 1.2f), new Vector3(0.08f, 0.8f, 0.08f), Rot.Identity);
                p.Of(WorldMaterial.Gold).Box(new Vector3(0, 4.75f, 1.2f), new Vector3(0.45f, 0.08f, 0.08f), Rot.Identity);
                p.Of(WorldMaterial.Geranium).Box(new Vector3(0, 0.55f, 1.6f), new Vector3(1.0f, 0.25f, 0.3f), Rot.Identity);
            }
        }

        private static void Maypole(Parts near, Parts far, Random random)
        {
            // Maibaum: a tall debarked trunk with a wreath and the little green top.
            float h = 26f;
            near.Of(WorldMaterial.WoodLight).Tube(Vector3.Zero, new Vector3(0, h, 0), 0.28f, 0.1f, 8);
            var green = near.Of(WorldMaterial.SpruceNeedles);
            green.Torus(new Vector3(0, h * 0.72f, 0), Vector3.UnitY, 1.1f, 0.25f, 0.25f, 18, 6);
            JaggedCone(green.Mesh, new Vector3(0, h - 0.5f, 0), 1.0f, 3.2f, 10, 0.7f, 0.2f, 0, random);
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3;
                var from = new Vector3((float)Math.Cos(a) * 1.1f, h * 0.72f, (float)Math.Sin(a) * 1.1f);
                near.Of(i % 2 == 0 ? WorldMaterial.Geranium : WorldMaterial.WallWhite).Box(from + new Vector3(0, -1.0f, 0), new Vector3(0.08f, 2.0f, 0.02f), Rot.AboutY((float)(a * 180 / Math.PI)));
            }

            far.Of(WorldMaterial.WoodLight).Tube(Vector3.Zero, new Vector3(0, h, 0), 0.3f, 0.12f, 4, false);
        }

        private static void Fence(Parts near)
        {
            // One 4 m piece of a Tyrolean slanted fence (Schrägzaun) along X: posts in pairs, slanted boards between.
            var wood = near.Of(WorldMaterial.WoodDark);
            for (int s = -1; s <= 1; s += 2)
            {
                wood.Box(new Vector3(s * 2f, 0.65f, -0.08f), new Vector3(0.1f, 1.3f, 0.1f), Rot.Identity);
                wood.Box(new Vector3(s * 2f, 0.65f, 0.08f), new Vector3(0.1f, 1.3f, 0.1f), Rot.Identity);
            }

            for (int i = 0; i < 6; i++)
            {
                float x = -1.65f + (i * 0.66f);
                wood.Box(new Vector3(x, 0.62f, 0), new Vector3(0.09f, 1.45f, 0.05f), Rot.AboutZ(-40));
            }
        }

        /// <summary>A 3 m piece of a low wooden picket fence along X.</summary>
        private static void GardenFence(Parts near)
        {
            var wood = near.Of(WorldMaterial.WoodLight);
            wood.Box(new Vector3(0, 0.3f, 0), new Vector3(3.0f, 0.07f, 0.04f), Rot.Identity);
            wood.Box(new Vector3(0, 0.75f, 0), new Vector3(3.0f, 0.07f, 0.04f), Rot.Identity);
            for (int i = 0; i < 12; i++) wood.Box(new Vector3(-1.4f + (i * 0.255f), 0.5f, 0.04f), new Vector3(0.09f, 1.0f, 0.025f), Rot.Identity);
            near.Of(WorldMaterial.WoodDark).Box(new Vector3(-1.5f, 0.55f, 0), new Vector3(0.12f, 1.1f, 0.12f), Rot.Identity);
        }

        /// <summary>A flower bed along a house wall: soil, greenery, red and yellow flowers.</summary>
        private static void FlowerBed(Parts near, Random random)
        {
            near.Of(WorldMaterial.Stone).Box(new Vector3(0, 0.1f, 0), new Vector3(2.6f, 0.3f, 0.9f), Rot.Identity);
            near.Of(WorldMaterial.Soil).Box(new Vector3(0, 0.22f, 0), new Vector3(2.4f, 0.1f, 0.7f), Rot.Identity);
            for (int i = 0; i < 6; i++)
            {
                var c = new Vector3(-1.0f + (i * 0.4f), 0.4f, (float)(random.NextDouble() - 0.5) * 0.3f);
                Blob(near.Of(WorldMaterial.LeavesMid), c, new Vector3(0.22f, 0.2f, 0.22f), 0.3f, random.Next(), 6, 4);
                Blob(near.Of(i % 2 == 0 ? WorldMaterial.Geranium : WorldMaterial.Flowers), c + new Vector3(0, 0.14f, 0), new Vector3(0.14f, 0.08f, 0.14f), 0.3f, random.Next(), 5, 3);
            }
        }

        private static void Bench(Parts near)
        {
            var wood = near.Of(WorldMaterial.WoodLight);
            wood.Box(new Vector3(0, 0.45f, 0), new Vector3(1.8f, 0.07f, 0.45f), Rot.Identity);
            wood.Box(new Vector3(0, 0.75f, -0.22f), new Vector3(1.8f, 0.4f, 0.05f), Rot.Identity);
            for (int s = -1; s <= 1; s += 2) wood.Box(new Vector3(s * 0.75f, 0.22f, 0), new Vector3(0.07f, 0.45f, 0.4f), Rot.Identity);
        }

        /// <summary>
        /// Beer garden set: a slatted wooden table on a dark iron frame with four folding chairs (iron X-frame, wooden
        /// slats); variants 1 and 2 with a parasol.
        /// </summary>
        private static void BeerTable(Parts near, int v)
        {
            var wood = near.Of(WorldMaterial.WoodLight);
            var iron = near.Of(WorldMaterial.Iron);
            for (int i = 0; i < 6; i++) wood.Box(new Vector3(0, 0.75f, -0.35f + (i * 0.14f)), new Vector3(1.3f, 0.035f, 0.11f), Rot.Identity);
            for (int s = -1; s <= 1; s += 2)
            {
                iron.Tube(new Vector3(s * 0.55f, 0, -0.35f), new Vector3(s * 0.55f, 0.73f, 0.35f), 0.018f, 0.018f, 4, false);
                iron.Tube(new Vector3(s * 0.55f, 0, 0.35f), new Vector3(s * 0.55f, 0.73f, -0.35f), 0.018f, 0.018f, 4, false);
            }

            // Two chairs on each long side, facing the table
            foreach (float x in new[] { -0.33f, 0.33f })
            {
                FoldingChair(wood, iron, new Vector3(x, 0, 0.75f), Rot.AboutY(180));
                FoldingChair(wood, iron, new Vector3(x, 0, -0.75f), Rot.Identity);
            }

            if (v == 0) return;
            iron.Tube(new Vector3(0, 0.77f, 0), new Vector3(0, 2.5f, 0), 0.03f, 0.03f, 5, false);
            near.Of(v == 1 ? WorldMaterial.WallWhite : WorldMaterial.Awning).Tube(new Vector3(0, 2.15f, 0), new Vector3(0, 2.65f, 0), 1.6f, 0.05f, 8);
        }

        /// <summary>A folding bistro chair: crossed iron legs, wooden seat slats, a slatted backrest (front = +Z).</summary>
        private static void FoldingChair(MeshBuilder wood, MeshBuilder iron, Vector3 at, Rot facing)
        {
            Vector3 P(float x, float y, float z) => at + (facing * new Vector3(x, y, z));
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 0.2f;
                // Back legs run on up into the backrest frame; front legs cross them under the seat.
                iron.Tube(P(x, 0, -0.22f), P(x, 0.46f, -0.17f), 0.012f, 0.012f, 4, false);
                iron.Tube(P(x, 0.46f, -0.17f), P(x, 0.9f, -0.24f), 0.012f, 0.012f, 4, false);
                iron.Tube(P(x, 0, 0.24f), P(x, 0.45f, -0.16f), 0.012f, 0.012f, 4, false);
                iron.Tube(P(x, 0.44f, 0.2f), P(x, 0.44f, -0.17f), 0.01f, 0.01f, 4, false);
            }

            iron.Tube(P(-0.2f, 0.08f, 0.2f), P(0.2f, 0.08f, 0.2f), 0.01f, 0.01f, 4, false);
            for (int i = 0; i < 5; i++) wood.Box(P(0, 0.465f, -0.15f + (i * 0.085f)), Abs(facing * new Vector3(0.44f, 0.022f, 0.07f)), Rot.Identity);
            for (int i = 0; i < 4; i++) wood.Box(P(0, 0.58f + (i * 0.09f), -0.2f - (i * 0.012f)), Abs(facing * new Vector3(0.44f, 0.065f, 0.02f)), Rot.Identity);
        }

        /// <summary>A simple gable roof over a w × d box (ridge along Z), for sheds and workshops.</summary>
        private static void GableRoof(Parts p, float w, float d, float wallTop, float pitch, float over, WorldMaterial roof, WorldMaterial gableMaterial, float x0 = 0, float z0 = 0)
        {
            float tan = (float)Math.Tan(pitch * Math.PI / 180), ridge = wallTop + ((w / 2) * tan);
            var gable = p.Of(gableMaterial).Mesh;
            for (int side = -1; side <= 1; side += 2)
            {
                int start = gable.Vertices.Count;
                Add(gable, new Vector3(x0 - (w / 2), wallTop, z0 + (side * d / 2)));
                Add(gable, new Vector3(x0 + (w / 2), wallTop, z0 + (side * d / 2)));
                Add(gable, new Vector3(x0, ridge, z0 + (side * d / 2)));
                TriOut(gable, start, start + 1, start + 2, new Vector3(0, 0, side));
            }

            float run = (w / 2) + over, slope = run / (float)Math.Cos(pitch * Math.PI / 180), eaveY = wallTop - (over * tan);
            for (int side = -1; side <= 1; side += 2)
            {
                var rot = Rot.AboutZ(-side * pitch);
                p.Of(roof).Box(new Vector3(x0 + (side * run / 2), (eaveY + ridge) / 2, z0) + (rot * new Vector3(0, 0.12f, 0)), new Vector3(slope, 0.24f, d + (2 * over)), rot);
            }
        }

        /// <summary>Car workshop: a low hall with two big roller doors to the road, a side office and the sign.</summary>
        private static void Workshop(Parts near, Parts far, int v)
        {
            float w = 16f, d = 13f, wallTop = 5.2f;
            var wall = v == 1 ? WorldMaterial.WallCream : WorldMaterial.WallWhite;
            foreach (var (p, detailed) in new[] { (near, true), (far, false) })
            {
                p.Of(WorldMaterial.Plinth).Box(new Vector3(0, -0.9f, 0), new Vector3(w + 0.3f, 2.8f, d + 0.3f), Rot.Identity);
                p.Of(wall).Box(new Vector3(0, (0.5f + wallTop) / 2, 0), new Vector3(w, wallTop - 0.5f, d), Rot.Identity);
                GableRoof(p, w, d, wallTop, 16f, 0.8f, WorldMaterial.RoofGrey, wall);
                if (!detailed) continue;
                for (int s = -1; s <= 1; s += 2)
                {
                    p.Of(WorldMaterial.RollerDoor).Box(new Vector3(s * 3.6f, 2.3f, (d / 2) + 0.05f), new Vector3(4.2f, 3.6f, 0.1f), Rot.Identity);
                    for (int k = 0; k < 8; k++) p.Of(WorldMaterial.Plinth).Box(new Vector3(s * 3.6f, 0.9f + (k * 0.42f), (d / 2) + 0.11f), new Vector3(4.2f, 0.04f, 0.04f), Rot.Identity);
                }

                p.Of(WorldMaterial.SignBlue).Box(new Vector3(0, 4.55f, (d / 2) + 0.08f), new Vector3(8.5f, 0.9f, 0.08f), Rot.Identity);
                p.Of(WorldMaterial.WallWhite).Box(new Vector3(-1.5f, 4.55f, (d / 2) + 0.13f), new Vector3(4.2f, 0.25f, 0.03f), Rot.Identity);
                p.Of(WorldMaterial.FireRed).Box(new Vector3(2.4f, 4.55f, (d / 2) + 0.13f), new Vector3(1.0f, 0.5f, 0.03f), Rot.Identity);
                // Office on the side with a window and door
                p.Of(wall).Box(new Vector3((w / 2) + 2.5f, 1.8f, (d / 2) - 3), new Vector3(5f, 3.2f, 6f), Rot.Identity);
                p.Of(WorldMaterial.RoofGrey).Box(new Vector3((w / 2) + 2.5f, 3.5f, (d / 2) - 3), new Vector3(5.6f, 0.2f, 6.6f), Rot.Identity);
                Window(p, new Vector3((w / 2) + 2.5f, 1.9f, (d / 2) + 0.03f), 2.2f, 1.3f, Rot.Identity, cross: false);
                p.Of(WorldMaterial.Door).Box(new Vector3((w / 2) + 4.6f, 1.4f, (d / 2) + 0.03f), new Vector3(0.9f, 2.0f, 0.06f), Rot.Identity);
                // Tyres stacked by the wall
                for (int k = 0; k < 4; k++) p.Of(WorldMaterial.Tyre).Torus(new Vector3(-(w / 2) - 0.8f, 0.15f + (k * 0.22f), 2), Vector3.UnitY, 0.32f, 0.12f, 0.11f, 12, 5);
            }
        }

        /// <summary>
        /// Sawmill: a long open shed on posts (the saw line under it), a closed end with the office, logs and timber
        /// round it and sawdust on the ground.
        /// </summary>
        private static void Sawmill(Parts near, Parts far, int v, Random random)
        {
            float w = 12f, d = 30f, top = 5.5f;
            foreach (var (p, detailed) in new[] { (near, true), (far, false) })
            {
                p.Of(WorldMaterial.Concrete).Box(new Vector3(0, 0.1f, 0), new Vector3(w + 1, 0.4f, d + 1), Rot.Identity);
                GableRoof(p, w, d, top, 20f, 1.0f, v == 1 ? WorldMaterial.Shingle : WorldMaterial.RoofGrey, WorldMaterial.WoodDark);
                // Closed back third: boards
                p.Of(WorldMaterial.WoodDark).Box(new Vector3(0, (0.3f + top) / 2, -(d / 2) + 5), new Vector3(w, top - 0.3f, 10f), Rot.Identity);
                if (!detailed) continue;
                var post = p.Of(WorldMaterial.WoodDark);
                for (float z = -(d / 2) + 10; z <= (d / 2) + 0.01f; z += 5)
                {
                    for (int s = -1; s <= 1; s += 2) post.Box(new Vector3(s * ((w / 2) - 0.15f), (0.3f + top) / 2, z), new Vector3(0.3f, top - 0.3f, 0.3f), Rot.Identity);
                }

                // The saw line: a long carriage track with a log on it and the saw frame
                p.Of(WorldMaterial.Iron).Box(new Vector3(0, 0.55f, 4), new Vector3(1.6f, 0.5f, 18f), Rot.Identity);
                p.Of(WorldMaterial.Bark).Tube(new Vector3(0, 1.2f, -2), new Vector3(0, 1.2f, 8), 0.45f, 0.4f, 9);
                p.Of(WorldMaterial.FireRed).Box(new Vector3(0, 2.2f, 10), new Vector3(2.6f, 3.4f, 0.8f), Rot.Identity);
                p.Of(WorldMaterial.Sawdust).Ellipsoid(new Vector3(2.5f, 0.3f, 11), new Vector3(1.8f, 0.8f, 1.6f), Rot.Identity, 8, 4, upperHalf: true);
                // Office window and door in the closed part
                Window(p, new Vector3((w / 2) + 0.05f, 2.2f, -(d / 2) + 4), 1.2f, 1.0f, Rot.AboutY(90), cross: true);
                p.Of(WorldMaterial.Door).Box(new Vector3((w / 2) + 0.05f, 1.4f, -(d / 2) + 7.5f), new Vector3(0.06f, 2.1f, 1.0f), Rot.Identity);
            }
        }

        /// <summary>Feuerwehrhaus: white hall with red doors, the hose-drying tower with its little roof.</summary>
        private static void FireStation(Parts near, Parts far)
        {
            float w = 14f, d = 13f, wallTop = 5.5f;
            foreach (var (p, detailed) in new[] { (near, true), (far, false) })
            {
                p.Of(WorldMaterial.Plinth).Box(new Vector3(0, -0.9f, 0), new Vector3(w + 0.3f, 2.8f, d + 0.3f), Rot.Identity);
                p.Of(WorldMaterial.WallWhite).Box(new Vector3(0, (0.5f + wallTop) / 2, 0), new Vector3(w, wallTop - 0.5f, d), Rot.Identity);
                GableRoof(p, w, d, wallTop, 22f, 1.0f, WorldMaterial.RoofBrown, WorldMaterial.WoodDark);
                // Hose tower at the back corner
                float tx = -(w / 2) + 2, tz = -(d / 2) + 2, th = 16f;
                p.Of(WorldMaterial.WallWhite).Box(new Vector3(tx, th / 2, tz), new Vector3(3.6f, th, 3.6f), Rot.Identity);
                HipRoof(p.Of(WorldMaterial.RoofBrown).Mesh, new Vector3(tx, th, tz), 2.4f, 2.6f, 0.3f);
                p.Of(WorldMaterial.WoodDark).Box(new Vector3(tx, th - 0.15f, tz), new Vector3(3.9f, 0.3f, 3.9f), Rot.Identity); // eaves board
                if (detailed) p.Of(WorldMaterial.Iron).Box(new Vector3(tx, th + 3.0f, tz), new Vector3(0.06f, 0.9f, 0.06f), Rot.Identity); // finial
                if (!detailed) continue;
                for (int s = -1; s <= 1; s += 2)
                {
                    p.Of(WorldMaterial.FireRed).Box(new Vector3(s * 3.4f, 2.4f, (d / 2) + 0.05f), new Vector3(4.0f, 3.8f, 0.1f), Rot.Identity);
                    for (int k = 0; k < 3; k++) Window(p, new Vector3(s * (2.3f + (k * 1.1f)) * 0.95f, 3.7f, (d / 2) + 0.12f), 0.6f, 0.4f, Rot.Identity, cross: false);
                }

                p.Of(WorldMaterial.FireRed).Box(new Vector3(0, 4.85f, (d / 2) + 0.08f), new Vector3(6.0f, 0.55f, 0.06f), Rot.Identity);
                for (int f = 0; f < 4; f++)
                {
                    var face = Rot.AboutY(f * 90);
                    Window(p, new Vector3(tx, th - 2, tz) + (face * new Vector3(0, 0, 1.83f)), 0.7f, 1.2f, face, cross: false);
                }
            }
        }

        /// <summary>A pile of logs (end grain towards the viewer), on two squared timbers.</summary>
        private static void LogPile(Parts near, Parts far, int v, Random random)
        {
            float length = 6f + v;
            var bark = near.Of(WorldMaterial.Bark);
            var ends = near.Of(WorldMaterial.Sawdust);
            for (int s = -1; s <= 1; s += 2) near.Of(WorldMaterial.WoodDark).Box(new Vector3(s * 2f, 0.12f, 0), new Vector3(0.25f, 0.25f, 2.8f), Rot.Identity);
            int[] perRow = { 5, 4, 3 };
            for (int row = 0; row < perRow.Length; row++)
            {
                for (int i = 0; i < perRow[row]; i++)
                {
                    float r = 0.24f + ((float)random.NextDouble() * 0.06f);
                    float z = (i - ((perRow[row] - 1) / 2f)) * 0.56f;
                    float y = 0.25f + r + (row * 0.48f);
                    float shift = (float)(random.NextDouble() - 0.5) * 0.4f;
                    bark.Tube(new Vector3(-(length / 2) + shift, y, z), new Vector3((length / 2) + shift, y, z), r, r * 0.92f, 8, false);
                    ends.Disc(new Vector3((length / 2) + shift, y, z), Vector3.UnitX, 0, r * 0.95f, 0.02f, 8);
                    ends.Disc(new Vector3(-(length / 2) + shift, y, z), Vector3.UnitX, 0, r * 0.95f, 0.02f, 8);
                }
            }

            far.Of(WorldMaterial.Bark).Box(new Vector3(0, 0.75f, 0), new Vector3(length, 1.4f, 2.6f), Rot.Identity);
        }

        /// <summary>Sawn timber: boards stacked in layers with spacer laths between.</summary>
        private static void LumberStack(Parts near, Parts far, int v)
        {
            float length = 4.5f, width = 1.4f;
            int layers = 6 + (v * 2);
            for (int k = 0; k < layers; k++)
            {
                float y = 0.2f + (k * 0.12f);
                near.Of(WorldMaterial.WoodLight).Box(new Vector3(0, y, 0), new Vector3(length, 0.07f, width), Rot.Identity);
                for (int s = -1; s <= 1; s++) near.Of(WorldMaterial.WoodDark).Box(new Vector3(s * 1.8f, y + 0.055f, 0), new Vector3(0.06f, 0.04f, width), Rot.Identity);
            }

            near.Of(WorldMaterial.Concrete).Box(new Vector3(0, 0.08f, 0), new Vector3(length, 0.16f, width * 0.6f), Rot.Identity);
            far.Of(WorldMaterial.WoodLight).Box(new Vector3(0, 0.2f + (layers * 0.06f), 0), new Vector3(length, layers * 0.12f, width), Rot.Identity);
        }

        /// <summary>
        /// A square hipped roof (pyramid) sitting square on a tower: <paramref name="half"/> = half the eave width
        /// (tower plus overhang), <paramref name="height"/> = to the tip; with a thickness so it reads from below too.
        /// </summary>
        private static void HipRoof(MeshData m, Vector3 baseCenter, float half, float height, float thickness)
        {
            var tip = baseCenter + new Vector3(0, height, 0);
            var corners = new[] { new Vector3(-half, 0, -half), new Vector3(half, 0, -half), new Vector3(half, 0, half), new Vector3(-half, 0, half) };
            for (int k = 0; k < 4; k++)
            {
                var a = baseCenter + corners[k];
                var b = baseCenter + corners[(k + 1) % 4];
                var mid = (a + b) / 2;
                int start = m.Vertices.Count;
                Add(m, a);
                Add(m, b);
                Add(m, tip);
                TriOut(m, start, start + 1, start + 2, new Vector3(mid.X - baseCenter.X, 0.6f, mid.Z - baseCenter.Z));
                // Underside and the edge of the eaves
                int under = m.Vertices.Count;
                Add(m, a - new Vector3(0, thickness, 0));
                Add(m, b - new Vector3(0, thickness, 0));
                Add(m, baseCenter + new Vector3(0, height * 0.55f, 0));
                TriOut(m, under, under + 1, under + 2, new Vector3(0, -1, 0));
                int edge = m.Vertices.Count;
                Add(m, a);
                Add(m, b);
                Add(m, b - new Vector3(0, thickness, 0));
                Add(m, a - new Vector3(0, thickness, 0));
                var outward = new Vector3(mid.X - baseCenter.X, 0, mid.Z - baseCenter.Z);
                TriOut(m, edge, edge + 1, edge + 2, outward);
                TriOut(m, edge, edge + 2, edge + 3, outward);
            }
        }

        /// <summary>
        /// A compact five-door hatchback in the style of the late-90s Golf (no badges): front = +Z, 4.15 × 1.73 ×
        /// 1.44 m, wheelbase 2.51 m. Rounded body with a short bonnet, raked windscreen, long roof, a thick C-pillar
        /// sloping forward to the slightly slanted tailgate; twin round headlights in clear housings with chrome grille
        /// bars between, body-coloured bumpers with dark intakes, rubbing strips, door seams and handles, 7-spoke
        /// alloys, upright tail lights.
        /// </summary>
        private static void Car(Parts near, int v)
        {
            var paint = v == 0 ? WorldMaterial.CarSilver : v == 1 ? WorldMaterial.CarBlue : WorldMaterial.CarRed;
            var body = near.Of(paint);
            var glass = near.Of(WorldMaterial.Glass);
            var dark = near.Of(WorldMaterial.Iron);
            var chrome = near.Of(WorldMaterial.CarSilver);
            const float belt = 0.97f, half = 0.865f, axle = 1.255f, r = 0.315f;

            // Real Golf IV (5-door) layout: 4.15 m long (z = ±2.075), front overhang 0.84 m, wheelbase 2.51 m; the
            // windscreen from the scuttle (z 0.72) up to the roof (z −0.08), the roof to z −1.52, the tailgate down to
            // the tail. Lower body: rounded corners; the bonnet falls from the scuttle to a low nose, the tail is upright.
            void Quad(MeshData m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                int q = m.Vertices.Count;
                Add(m, a);
                Add(m, b);
                Add(m, c);
                Add(m, d);
                TriOut(m, q, q + 1, q + 2, outward);
                TriOut(m, q, q + 2, q + 3, outward);
            }

            // Upper body: from the belt down to just above the wheels (y 0.69); the bonnet falls to the nose.
            body.Sweep(new[]
            {
                new Ring(new Vector3(0, 0.83f, -2.075f), half - 0.04f, half - 0.04f, belt - 0.83f, 0.14f, 6f),
                new Ring(new Vector3(0, 0.83f, -2.04f), half - 0.01f, half - 0.01f, belt - 0.83f, 0.14f, 5f),
                new Ring(new Vector3(0, 0.83f, -1.98f), half, half, belt - 0.83f, 0.14f, 6f),
                new Ring(new Vector3(0, 0.83f, 0.72f), half, half, belt - 0.83f, 0.14f, 6f),
                new Ring(new Vector3(0, 0.815f, 1.2f), half, half, 0.125f, 0.125f, 6f),
                new Ring(new Vector3(0, 0.8f, 1.6f), half - 0.01f, half - 0.01f, 0.1f, 0.11f, 5.5f),
                new Ring(new Vector3(0, 0.785f, 1.85f), half - 0.02f, half - 0.02f, 0.08f, 0.095f, 5.5f),
                new Ring(new Vector3(0, 0.775f, 2.0f), half - 0.04f, half - 0.04f, 0.065f, 0.085f, 5.5f),
                new Ring(new Vector3(0, 0.765f, 2.075f), half - 0.09f, half - 0.09f, 0.05f, 0.075f, 5f),
            }, Vector3.UnitX, 22);

            // Sides below it: panels with round wheel arches cut out (dark inside), the sill between the wheels.
            const float archR = 0.37f, sill = 0.3f, panelTop = 0.8f, depth = 0.08f;
            float Bottom(float z)
            {
                foreach (float a in new[] { -axle, axle })
                {
                    float dz = z - a;
                    if (Math.Abs(dz) < archR) return Math.Min(panelTop - 0.01f, r + (float)Math.Sqrt((archR * archR) - (dz * dz)));
                }

                return sill;
            }

            var lining = near.Of(WorldMaterial.Tyre).Mesh;
            for (int side = -1; side <= 1; side += 2)
            {
                float xo = side * half, xi = side * (half - depth);
                for (float z = -1.99f; z < 1.99f - 0.001f; z += 0.04f)
                {
                    float z2 = Math.Min(1.99f, z + 0.04f);
                    float b1 = Bottom(z), b2 = Bottom(z2);
                    Quad(body.Mesh, new Vector3(xo, b1, z), new Vector3(xo, b2, z2), new Vector3(xo, panelTop, z2), new Vector3(xo, panelTop, z), new Vector3(side, 0, 0));
                    // The edge underneath: dark in the arches, body colour along the sill
                    Quad(b1 > sill + 0.01f ? lining : body.Mesh, new Vector3(xi, b1, z), new Vector3(xi, b2, z2), new Vector3(xo, b2, z2), new Vector3(xo, b1, z), Vector3.UnitY * -1);
                }

                // Arch liners: dark curved walls behind the wheels, so the arches read as openings.
                foreach (float a in new[] { -axle, axle })
                {
                    for (int k = 0; k < 16; k++)
                    {
                        double t0 = Math.PI * k / 16, t1 = Math.PI * (k + 1) / 16;
                        var p0 = new Vector3(0, r + (archR * (float)Math.Sin(t0)), a + (archR * (float)Math.Cos(t0)));
                        var p1 = new Vector3(0, r + (archR * (float)Math.Sin(t1)), a + (archR * (float)Math.Cos(t1)));
                        Quad(lining, new Vector3(side * 0.6f, p0.Y, p0.Z), new Vector3(side * 0.6f, p1.Y, p1.Z), new Vector3(xi, p1.Y, p1.Z), new Vector3(xi, p0.Y, p0.Z), new Vector3(0, r - p0.Y, a - p0.Z));
                    }
                }
            }

            // Bumpers: rounded bars across the front and the back, wrapping round the corners into the sides.
            Ring Bar(float x, float scale, float y, float zc, float toFront, float toBack, float halfH) =>
                new Ring(new Vector3(x, y, zc), toFront * scale, toBack * scale, halfH * scale, halfH * scale, 6f);
            body.Sweep(new[]
            {
                Bar(-half - 0.005f, 0.55f, 0.5f, 1.97f, 0.11f, 0.14f, 0.19f),
                Bar(-half + 0.03f, 0.95f, 0.5f, 1.97f, 0.11f, 0.14f, 0.19f),
                Bar(-half + 0.12f, 1f, 0.5f, 1.97f, 0.11f, 0.14f, 0.19f),
                Bar(half - 0.12f, 1f, 0.5f, 1.97f, 0.11f, 0.14f, 0.19f),
                Bar(half - 0.03f, 0.95f, 0.5f, 1.97f, 0.11f, 0.14f, 0.19f),
                Bar(half + 0.005f, 0.55f, 0.5f, 1.97f, 0.11f, 0.14f, 0.19f),
            }, Vector3.UnitZ, 14);
            body.Sweep(new[]
            {
                Bar(-half - 0.005f, 0.55f, 0.5f, -1.98f, 0.14f, 0.11f, 0.185f),
                Bar(-half + 0.03f, 0.95f, 0.5f, -1.98f, 0.14f, 0.11f, 0.185f),
                Bar(-half + 0.12f, 1f, 0.5f, -1.98f, 0.14f, 0.11f, 0.185f),
                Bar(half - 0.12f, 1f, 0.5f, -1.98f, 0.14f, 0.11f, 0.185f),
                Bar(half - 0.03f, 0.95f, 0.5f, -1.98f, 0.14f, 0.11f, 0.185f),
                Bar(half + 0.005f, 0.55f, 0.5f, -1.98f, 0.14f, 0.11f, 0.185f),
            }, Vector3.UnitZ, 14);
            const float tail = -2.075f;

            // Cabin: one closed shape (windscreen, roof, tailgate, two sides with tumblehome), painted; the windows
            // sit on it, so the pillars are simply the paint between the glass.
            var bf = new Vector3(0.8f, belt, 0.72f);     // bottom front (x > 0 side): the scuttle
            var br = new Vector3(0.8f, belt, -2.02f);    // bottom rear: the tailgate runs down to the tail
            var tf = new Vector3(0.7f, 1.43f, -0.08f);   // top front: windscreen top
            var tr = new Vector3(0.71f, 1.43f, -1.52f);  // top rear: roof end
            Vector3 M(Vector3 v, int side) => new Vector3(v.X * side, v.Y, v.Z);
            var shell = body.Mesh;
            Quad(shell, M(bf, -1), M(bf, 1), M(tf, 1), M(tf, -1), new Vector3(0, 0.5f, 1));      // windscreen
            Quad(shell, M(tf, -1), M(tf, 1), M(tr, 1), M(tr, -1), Vector3.UnitY);               // roof
            Quad(shell, M(tr, -1), M(tr, 1), M(br, 1), M(br, -1), new Vector3(0, 0.5f, -1));     // tailgate
            for (int side = -1; side <= 1; side += 2) Quad(shell, M(bf, side), M(br, side), M(tr, side), M(tf, side), new Vector3(side, 0, 0));

            // Windscreen and rear window: glass over nearly the whole face (thin frame)
            Vector3 On(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u, float v) => Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), v);
            void Pane(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0, float u1, float v0, float v1, Vector3 outward)
            {
                var o = Vector3.Normalize(outward) * 0.012f;
                Quad(glass.Mesh, On(a, b, c, d, u0, v0) + o, On(a, b, c, d, u1, v0) + o, On(a, b, c, d, u1, v1) + o, On(a, b, c, d, u0, v1) + o, outward);
            }

            var screenOut = Vector3.Cross(M(bf, 1) - M(bf, -1), M(tf, -1) - M(bf, -1));
            Pane(M(bf, -1), M(bf, 1), M(tf, 1), M(tf, -1), 0.03f, 0.97f, 0.04f, 0.96f, screenOut.Y > 0 ? screenOut : -screenOut);
            var hatchOut = Vector3.Cross(M(br, -1) - M(br, 1), M(tr, 1) - M(br, 1));
            Pane(M(br, 1), M(br, -1), M(tr, -1), M(tr, 1), 0.07f, 0.93f, 0.1f, 0.94f, hatchOut.Y > 0 ? hatchOut : -hatchOut);

            // Side windows, drawn as the real shapes on the side face (x follows the tumblehome):
            // front door right behind a thin A-pillar, a short dark B-pillar, the rear door window with its back edge
            // sloping along the broad C-pillar.
            const float yb = belt + 0.035f, yt = 1.395f;
            float SideX(float y) => 0.8f - ((y - belt) / (1.43f - belt) * 0.095f) + 0.012f;
            void SideWindow(int side, (float Z, float Y) p0, (float Z, float Y) p1, (float Z, float Y) p2, (float Z, float Y) p3, MeshBuilder material)
            {
                Vector3 P((float Z, float Y) q) => new Vector3(side * SideX(q.Y), q.Y, q.Z);
                Quad(material.Mesh, P(p0), P(p1), P(p2), P(p3), new Vector3(side, 0.08f, 0));
            }

            for (int side = -1; side <= 1; side += 2)
            {
                SideWindow(side, (0.615f, yb), (-0.27f, yb), (-0.27f, yt), (-0.11f, yt), glass);
                SideWindow(side, (-0.27f, yb), (-0.35f, yb), (-0.35f, yt), (-0.27f, yt), dark); // B-pillar
                SideWindow(side, (-0.35f, yb), (-1.27f, yb), (-1.08f, yt), (-0.35f, yt), glass);
            }

            // Small roof spoiler over the tailgate
            body.Box(new Vector3(0, 1.425f, tr.Z + 0.02f), new Vector3(1.32f, 0.05f, 0.2f), Rot.AboutX(-8));
            // Roof antenna, leaning back
            dark.Tube(new Vector3(0, 1.44f, tr.Z + 0.25f), new Vector3(0, 1.98f, tr.Z - 0.05f), 0.01f, 0.006f, 4, false);

            for (int s = -1; s <= 1; s += 2)
            {
                // Mirror on the door
                body.Box(new Vector3(s * 0.92f, 1.04f, 0.58f), new Vector3(0.1f, 0.13f, 0.19f), Rot.AboutY(s * 8));
                body.Box(new Vector3(s * 0.85f, 1.01f, 0.6f), new Vector3(0.1f, 0.05f, 0.07f), Rot.Identity);
                // Rubbing strip, door seams and handles
                dark.Box(new Vector3(s * (half + 0.005f), 0.6f, -0.1f), new Vector3(0.02f, 0.05f, 2.4f), Rot.Identity);
                foreach (float z in new[] { 0.72f, -0.31f, -1.3f }) dark.Box(new Vector3(s * (half + 0.004f), 0.68f, z), new Vector3(0.01f, 0.55f, 0.015f), Rot.Identity);
                foreach (float z in new[] { -0.12f, -1.12f }) chrome.Box(new Vector3(s * (half + 0.01f), 0.88f, z), new Vector3(0.025f, 0.035f, 0.14f), Rot.Identity);
                // Tail lights high on the rear corners, just under the rear window, wrapping round the side
                near.Of(WorldMaterial.Awning).Box(new Vector3(s * 0.63f, 0.9f, tail - 0.012f), new Vector3(0.36f, 0.15f, 0.03f), Rot.Identity);
                near.Of(WorldMaterial.FireRed).Box(new Vector3(s * 0.63f, 0.81f, tail - 0.012f), new Vector3(0.36f, 0.05f, 0.03f), Rot.Identity);
                near.Of(WorldMaterial.Awning).Box(new Vector3(s * (half - 0.035f), 0.88f, tail + 0.1f), new Vector3(0.04f, 0.19f, 0.2f), Rot.Identity);
                // Tailgate seams
                dark.Box(new Vector3(s * 0.43f, 0.84f, tail - 0.004f), new Vector3(0.012f, 0.26f, 0.01f), Rot.Identity);
                // Headlights: a reflector housing with two round lamps
                var hl = new Vector3(s * 0.52f, 0.755f, 2.07f);
                chrome.Box(hl, new Vector3(0.36f, 0.13f, 0.05f), Rot.Identity);
                near.Of(WorldMaterial.Frame).Disc(hl + new Vector3(s * -0.08f, 0, 0.035f), Vector3.UnitZ, 0, 0.06f, 0.02f, 10);
                near.Of(WorldMaterial.Frame).Disc(hl + new Vector3(s * 0.08f, 0, 0.025f), Vector3.UnitZ, 0, 0.05f, 0.02f, 10);
            }

            // Grille: chrome bars between the headlights; bumpers in body colour with dark intakes and the plates
            for (int k = 0; k < 4; k++) chrome.Box(new Vector3(0, 0.71f + (k * 0.03f), 2.075f), new Vector3(0.6f, 0.014f, 0.03f), Rot.Identity);
            // Tailgate: lower seam and the handle
            dark.Box(new Vector3(0, 0.71f, tail - 0.004f), new Vector3(0.88f, 0.012f, 0.01f), Rot.Identity);
            chrome.Box(new Vector3(0, 0.79f, tail - 0.012f), new Vector3(0.18f, 0.04f, 0.025f), Rot.Identity);
            // Rear bumper: big, body colour, a grey strip on top, the plate in it, dark below with twin exhausts.
            // On the bumpers: the grey strip along the rear one, the plates, dark intakes in front, the exhausts
            near.Of(WorldMaterial.Plinth).Box(new Vector3(0, 0.655f, -2.07f), new Vector3(1.7f, 0.03f, 0.2f), Rot.Identity);
            dark.Box(new Vector3(0, 0.5f, -2.137f), new Vector3(0.58f, 0.15f, 0.01f), Rot.Identity);
            near.Of(WorldMaterial.WallWhite).Box(new Vector3(0, 0.5f, -2.145f), new Vector3(0.52f, 0.11f, 0.01f), Rot.Identity);
            dark.Box(new Vector3(0, 0.37f, -2.08f), new Vector3(1.5f, 0.06f, 0.12f), Rot.Identity);
            foreach (float x in new[] { -0.52f, -0.42f }) chrome.Tube(new Vector3(x, 0.3f, -1.95f), new Vector3(x, 0.3f, -2.16f), 0.035f, 0.035f, 8, false);
            dark.Box(new Vector3(0, 0.4f, 2.118f), new Vector3(0.62f, 0.08f, 0.01f), Rot.Identity);
            for (int s = -1; s <= 1; s += 2) dark.Box(new Vector3(s * 0.55f, 0.4f, 2.115f), new Vector3(0.26f, 0.07f, 0.01f), Rot.Identity);
            near.Of(WorldMaterial.WallWhite).Box(new Vector3(0, 0.53f, 2.123f), new Vector3(0.52f, 0.11f, 0.01f), Rot.Identity);
            dark.Box(new Vector3(0, 0.32f, 0), new Vector3(1.6f, 0.06f, 3.2f), Rot.Identity); // underbody between the wheels

            // Wheels: tyres, 7-spoke alloys with a hub
            var tyre = near.Of(WorldMaterial.Tyre);
            foreach (float z in new[] { -axle, axle })
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    tyre.Tube(new Vector3(s * 0.64f, r, z), new Vector3(s * 0.85f, r, z), r, r, 18);
                    var c = new Vector3(s * 0.855f, r, z);
                    chrome.Disc(c, Vector3.UnitX, 0.17f, 0.2f, 0.03f, 16);
                    chrome.Sphere(c, 0.05f, 8, 5);
                    for (int k = 0; k < 7; k++)
                    {
                        double a = k * Math.PI * 2 / 7;
                        var dir = new Vector3(0, (float)Math.Sin(a), (float)Math.Cos(a));
                        chrome.Tube(c + (dir * 0.04f), c + (dir * 0.18f), 0.022f, 0.016f, 4, false);
                    }
                }
            }
        }

        /// <summary>Junction signpost: a grey pole with 1–3 white boards with a blue border (variant + 1 boards).</summary>
        private static void DirectionSign(Parts near, int v)
        {
            near.Of(WorldMaterial.CarSilver).Tube(Vector3.Zero, new Vector3(0, 2.75f, 0), 0.04f, 0.04f, 8);
            for (int i = 0; i <= Math.Min(2, v); i++)
            {
                float y = 2.4f - (i * 0.42f);
                near.Of(WorldMaterial.WallWhite).Box(new Vector3(0.75f, y, 0.06f), new Vector3(1.55f, 0.32f, 0.03f), Rot.Identity);
                near.Of(WorldMaterial.SignBlue).Box(new Vector3(0.75f, y, 0.045f), new Vector3(1.65f, 0.38f, 0.03f), Rot.Identity);
                near.Of(WorldMaterial.CarSilver).Box(new Vector3(0.75f, y, 0.03f), new Vector3(1.7f, 0.42f, 0.02f), Rot.Identity);
            }
        }

        private static void VillageSign(Parts near)
        {
            // Two posts and the board; the app writes the name on it.
            var post = near.Of(WorldMaterial.Iron);
            post.Box(new Vector3(-0.9f, 1.05f, 0), new Vector3(0.07f, 2.1f, 0.07f), Rot.Identity);
            post.Box(new Vector3(0.9f, 1.05f, 0), new Vector3(0.07f, 2.1f, 0.07f), Rot.Identity);
            // Austrian style: white board, blue border (the app writes the name in dark letters)
            near.Of(WorldMaterial.WallWhite).Box(new Vector3(0, 1.75f, 0.07f), new Vector3(2.2f, 0.62f, 0.04f), Rot.Identity);
            near.Of(WorldMaterial.SignBlue).Box(new Vector3(0, 1.75f, 0.05f), new Vector3(2.34f, 0.76f, 0.04f), Rot.Identity);
            near.Of(WorldMaterial.CarSilver).Box(new Vector3(0, 1.75f, 0.03f), new Vector3(2.42f, 0.84f, 0.03f), Rot.Identity);
        }
    }
}
