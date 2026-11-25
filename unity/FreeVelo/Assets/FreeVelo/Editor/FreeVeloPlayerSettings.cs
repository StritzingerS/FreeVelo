using UnityEditor;

namespace FreeVelo.EditorTools
{
    /// <summary>
    /// Player settings the project relies on, set when the editor loads (so they cannot get lost and need no clicking):
    /// Android "Optimized Frame Pacing" (Swappy) – frames are shown at even intervals instead of now quick, now late,
    /// which is what the p95/p99 frame times in the phone logs showed (docs/GRAPHICS_OVERHAUL.md section 6).
    /// </summary>
    [InitializeOnLoad]
    internal static class FreeVeloPlayerSettings
    {
        static FreeVeloPlayerSettings()
        {
            if (!PlayerSettings.Android.optimizedFramePacing)
            {
                PlayerSettings.Android.optimizedFramePacing = true;
                AssetDatabase.SaveAssets();
            }
        }
    }
}
