using System.Collections.Generic;
using System.Numerics;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>Surfaces of the world's models; the app makes one material per entry.</summary>
    public enum WorldMaterial
    {
        Bark,
        PineBark,
        BirchBark,
        BirchMarks,
        SpruceNeedles,
        SpruceNeedlesDark,
        PineNeedles,
        LeavesDark,
        LeavesMid,
        LeavesLight,
        BirchLeaves,
        PoplarLeaves,
        Bush,
        BushFlowering,
        Rock,
        Reed,
        WallWhite,
        WallCream,
        WallOchre,
        WallBrick,
        WallWood,
        Plinth,
        RoofRed,
        RoofBrown,
        RoofGrey,
        Window,
        Frame,
        Door,
        Wheat,
        Crop,
        Plowed,
        Meadow,
        Water,
        Concrete,
        Railing,
    }

    public static class WorldMaterials
    {
        /// <summary>Base colour (sRGB 0–1) and smoothness of each material.</summary>
        public static readonly IReadOnlyDictionary<WorldMaterial, (Vector3 Color, float Smoothness)> All = new Dictionary<WorldMaterial, (Vector3, float)>
        {
            [WorldMaterial.Bark] = (new Vector3(0.30f, 0.23f, 0.17f), 0f),
            [WorldMaterial.PineBark] = (new Vector3(0.52f, 0.33f, 0.22f), 0f),
            [WorldMaterial.BirchBark] = (new Vector3(0.88f, 0.87f, 0.82f), 0.1f),
            [WorldMaterial.BirchMarks] = (new Vector3(0.12f, 0.11f, 0.10f), 0f),
            [WorldMaterial.SpruceNeedles] = (new Vector3(0.17f, 0.33f, 0.21f), 0f),
            [WorldMaterial.SpruceNeedlesDark] = (new Vector3(0.13f, 0.27f, 0.18f), 0f),
            [WorldMaterial.PineNeedles] = (new Vector3(0.26f, 0.40f, 0.21f), 0f),
            [WorldMaterial.LeavesDark] = (new Vector3(0.19f, 0.36f, 0.14f), 0f),
            [WorldMaterial.LeavesMid] = (new Vector3(0.27f, 0.44f, 0.17f), 0f),
            [WorldMaterial.LeavesLight] = (new Vector3(0.38f, 0.53f, 0.20f), 0f),
            [WorldMaterial.BirchLeaves] = (new Vector3(0.45f, 0.60f, 0.24f), 0f),
            [WorldMaterial.PoplarLeaves] = (new Vector3(0.26f, 0.43f, 0.18f), 0f),
            [WorldMaterial.Bush] = (new Vector3(0.21f, 0.37f, 0.15f), 0f),
            [WorldMaterial.BushFlowering] = (new Vector3(0.86f, 0.80f, 0.86f), 0f),
            [WorldMaterial.Rock] = (new Vector3(0.53f, 0.52f, 0.49f), 0.1f),
            [WorldMaterial.Reed] = (new Vector3(0.55f, 0.58f, 0.30f), 0f),
            [WorldMaterial.WallWhite] = (new Vector3(0.90f, 0.89f, 0.85f), 0.05f),
            [WorldMaterial.WallCream] = (new Vector3(0.93f, 0.86f, 0.70f), 0.05f),
            [WorldMaterial.WallOchre] = (new Vector3(0.85f, 0.68f, 0.42f), 0.05f),
            [WorldMaterial.WallBrick] = (new Vector3(0.62f, 0.32f, 0.24f), 0.05f),
            [WorldMaterial.WallWood] = (new Vector3(0.52f, 0.34f, 0.21f), 0f),
            [WorldMaterial.Plinth] = (new Vector3(0.45f, 0.44f, 0.42f), 0.05f),
            [WorldMaterial.RoofRed] = (new Vector3(0.62f, 0.24f, 0.17f), 0.15f),
            [WorldMaterial.RoofBrown] = (new Vector3(0.38f, 0.22f, 0.16f), 0.15f),
            [WorldMaterial.RoofGrey] = (new Vector3(0.27f, 0.28f, 0.30f), 0.2f),
            [WorldMaterial.Window] = (new Vector3(0.13f, 0.17f, 0.21f), 0.85f),
            [WorldMaterial.Frame] = (new Vector3(0.95f, 0.95f, 0.93f), 0.2f),
            [WorldMaterial.Door] = (new Vector3(0.36f, 0.22f, 0.13f), 0.2f),
            [WorldMaterial.Wheat] = (new Vector3(0.82f, 0.70f, 0.38f), 0f),
            [WorldMaterial.Crop] = (new Vector3(0.40f, 0.58f, 0.22f), 0f),
            [WorldMaterial.Plowed] = (new Vector3(0.40f, 0.30f, 0.21f), 0f),
            [WorldMaterial.Meadow] = (new Vector3(0.52f, 0.62f, 0.28f), 0f),
            [WorldMaterial.Water] = (new Vector3(0.20f, 0.33f, 0.38f), 0.93f),
            [WorldMaterial.Concrete] = (new Vector3(0.66f, 0.65f, 0.62f), 0.1f),
            [WorldMaterial.Railing] = (new Vector3(0.35f, 0.40f, 0.36f), 0.4f),
        };
    }
}
