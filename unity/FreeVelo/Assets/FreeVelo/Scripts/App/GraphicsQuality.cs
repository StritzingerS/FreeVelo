using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FreeVelo.App
{
    /// <summary>
    /// The graphics quality tiers (Settings → App): resolution, edge smoothing, HDR and shadows. Measured on a OnePlus
    /// Nord 2 at the old fixed settings (full resolution, 4× MSAA, HDR, soft 2048 px shadows to 110 m): 34 fps, the GPU
    /// at 29 ms a frame. The render pipeline asset is copied first, so changing it at run time never touches the asset
    /// file in the project (docs/GRAPHICS_OVERHAUL.md section 6).
    /// </summary>
    public static class GraphicsQuality
    {
        private static UniversalRenderPipelineAsset _copy;

        public static string[] Names => new[] { L.Get("quality_low"), L.Get("quality_medium"), L.Get("quality_high") };

        /// <summary>Applies the tier (0 low, 1 medium, 2 high) to the render pipeline and the sun.</summary>
        public static void Apply(int tier)
        {
#if UNITY_EDITOR
            // In the editor the pipeline assignment would be written into the project's settings (and point at an
            // object that is gone after play mode): the editor keeps the asset's own settings.
            return;
#else
            ApplyInPlayer(tier);
#endif
        }

        private static void ApplyInPlayer(int tier)
        {
            if (_copy == null)
            {
                var source = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
                if (source == null) return;
                _copy = Object.Instantiate(source);
                _copy.name = source.name + " (runtime)";
                GraphicsSettings.defaultRenderPipeline = _copy;
                QualitySettings.renderPipeline = _copy;
            }

            switch (tier)
            {
                case 0: // low: 70 % resolution, no edge smoothing, short hard shadows, 30 fps
                    Set(0.7f, 1, false, 50f, 1, 1024);
                    break;
                case 2: // high: as before
                    Set(1f, 4, true, 110f, 2, 2048);
                    break;
                default: // medium: 80 % resolution, 2× edge smoothing, hard shadows to 70 m
                    Set(0.8f, 2, false, 70f, 2, 1024);
                    break;
            }

            // Low holds a steady 30 frames a second: the Nord 2 averaged 52–54 fps there but jumped between 60 and 30
            // all the time (95th percentile at 33.7 ms); a fixed 30 is even, cooler and uses less battery.
            Application.targetFrameRate = tier == 0 ? 30 : 60;

            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun != null) sun.shadows = tier == 2 ? LightShadows.Soft : LightShadows.Hard;
        }

        private static void Set(float scale, int msaa, bool hdr, float shadowDistance, int cascades, int shadowResolution)
        {
            _copy.renderScale = scale;
            _copy.msaaSampleCount = msaa;
            _copy.supportsHDR = hdr;
            _copy.shadowDistance = shadowDistance;
            _copy.shadowCascadeCount = cascades;
            _copy.mainLightShadowmapResolution = shadowResolution;
        }
    }
}
