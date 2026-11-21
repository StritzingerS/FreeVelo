using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FreeVelo.App
{
    /// <summary>
    /// The graphics quality (Settings → App): high or low (owner's decision, 21 November 2026: two tiers, and the app
    /// drops from high to low by itself when high stutters, see <see cref="AutoQuality"/>). Measured on a OnePlus Nord 2:
    /// high 34 fps (GPU 29 ms a frame), low 52–54 fps before its fixed 30. The render pipeline asset is copied first,
    /// so changing it at run time never touches the asset file in the project (docs/GRAPHICS_OVERHAUL.md section 6).
    /// </summary>
    public static class GraphicsQuality
    {
        public const int Low = 0;
        public const int High = 2;

        private static UniversalRenderPipelineAsset _copy;

        /// <summary>The choices in the settings, in this order: low, high.</summary>
        public static string[] Names => new[] { L.Get("quality_low"), L.Get("quality_high") };

        /// <summary>Applies the tier (<see cref="Low"/> or <see cref="High"/>) to the render pipeline and the sun.</summary>
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

            if (tier == Low)
            {
                // 70 % resolution, no edge smoothing, short hard shadows
                Set(0.7f, 1, false, 50f, 1, 1024);
            }
            else
            {
                // full resolution, 4× edge smoothing, HDR, soft shadows to 110 m (the project's own settings)
                Set(1f, 4, true, 110f, 2, 2048);
            }

            // Low holds a steady 30 frames a second: the Nord 2 averaged 52–54 fps there but jumped between 60 and 30
            // all the time (95th percentile at 33.7 ms); a fixed 30 is even, cooler and uses less battery.
            Application.targetFrameRate = tier == Low ? 30 : 60;

            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun != null) sun.shadows = tier == High ? LightShadows.Soft : LightShadows.Hard;
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

    /// <summary>
    /// Watches the frame rate on high: when the phone cannot keep up (below 45 fps on average over 30 s, measured
    /// after the first 20 s of a running world), it switches to low, says so, and remembers it. If the rider then
    /// chooses high again by hand, it stays high.
    /// </summary>
    public sealed class AutoQuality : MonoBehaviour
    {
        private const float Warmup = 20f, Window = 30f, MinFps = 45f;

        private System.Func<bool> _active;
        private System.Action<string> _log;
        private float _running, _window, _frames;

        public void Begin(System.Func<bool> active, System.Action<string> log)
        {
            _active = active;
            _log = log;
        }

        /// <summary>Starts the measuring over (after the quality was changed, or the world rebuilt).</summary>
        public void Restart()
        {
            _running = 0;
            _window = 0;
            _frames = 0;
        }

        private void Update()
        {
            if (_active == null || !_active() || AppSettings.Quality != GraphicsQuality.High || AppSettings.QualityChosenByHand) return;
            float dt = Time.unscaledDeltaTime;
            _running += dt;
            if (_running < Warmup || dt > 1f) return; // loading hitches and the app coming back from the background
            _window += dt;
            _frames++;
            if (_window < Window) return;
            float fps = _frames / _window;
            _window = 0;
            _frames = 0;
            if (fps >= MinFps) return;

            AppSettings.Quality = GraphicsQuality.Low;
            AppSettings.QualityAutoLowered = true;
            GraphicsQuality.Apply(GraphicsQuality.Low);
            _log?.Invoke($"Graphics: high ran at {fps:0} fps over {Window:0} s, switched to low");
            FreeVelo.UI.Ui.Toast(L.Get("quality_auto_low"));
        }
    }
}
