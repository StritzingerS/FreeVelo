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
                // full resolution (lowered to 80 % at most by DynamicResolution when the GPU cannot keep up), 2× edge
                // smoothing (4× until 25 Nov 2026: hardly visible on a phone's dense display, costly on Mali GPUs),
                // HDR, soft shadows to 110 m
                Set(1f, 2, true, 110f, 2, 2048);
            }

            // Low holds a steady 30 frames a second: the Nord 2 averaged 52–54 fps there but jumped between 60 and 30
            // all the time (95th percentile at 33.7 ms); a fixed 30 is even, cooler and uses less battery.
            Application.targetFrameRate = tier == Low ? 30 : 60;

            // Low: LODGroups switch to their simpler level sooner (the instanced scenery does the same, see
            // WorldBuilder.LiteDistance).
            if (_baseLodBias < 0) _baseLodBias = QualitySettings.lodBias; // the quality level's own (PC 2, phone 1)
            QualitySettings.lodBias = tier == Low ? _baseLodBias * FreeVelo.World.WorldBuilder.LiteDistance : _baseLodBias;

            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun != null) sun.shadows = tier == High ? LightShadows.Soft : LightShadows.Hard;
        }

        private static float _baseLodBias = -1f;

        /// <summary>The render scale in use (1 in the editor, where the pipeline asset is left alone).</summary>
        public static float RenderScale => _copy != null ? _copy.renderScale : 1f;

        /// <summary>For <see cref="DynamicResolution"/>: the render scale on high (0.8–1).</summary>
        public static void SetRenderScale(float scale)
        {
            if (_copy != null) _copy.renderScale = Mathf.Clamp(scale, 0.8f, 1f);
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

    /// <summary>
    /// On high, lowers the render resolution step by step (to 80 % at most) while the GPU needs more than 15 ms a frame
    /// on average over a second, and raises it again below 11.5 ms (owner, 25 Nov 2026: little visible on a phone's
    /// dense display, much GPU time saved). Phones that keep up stay at 100 %. Needs the GPU frame time
    /// (FrameTimingManager, captured by <see cref="PerfMonitor"/>); without it nothing changes. Player only.
    /// </summary>
    public sealed class DynamicResolution : MonoBehaviour
    {
        private const float TooSlowMs = 15f, FastMs = 11.5f, Step = 0.05f, Interval = 1f;

        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private double _gpu;
        private int _samples;
        private float _since;
        private bool _wasHigh;

        private void Update()
        {
            bool high = AppSettings.Quality == GraphicsQuality.High;
            if (!high)
            {
                _wasHigh = false;
                return;
            }

            if (!_wasHigh)
            {
                // just switched to high (or started): from full resolution
                _wasHigh = true;
                GraphicsQuality.SetRenderScale(1f);
                _gpu = 0;
                _samples = 0;
                _since = 0;
            }

            if (FrameTimingManager.GetLatestTimings(1, _timing) > 0 && _timing[0].gpuFrameTime > 0)
            {
                _gpu += _timing[0].gpuFrameTime;
                _samples++;
            }

            _since += Time.unscaledDeltaTime;
            if (_since < Interval) return;
            _since = 0;
            if (_samples < 10) return;
            double gpu = _gpu / _samples;
            _gpu = 0;
            _samples = 0;
            float scale = GraphicsQuality.RenderScale;
            if (gpu > TooSlowMs && scale > 0.8f + 0.001f) GraphicsQuality.SetRenderScale(scale - Step);
            else if (gpu < FastMs && scale < 1f - 0.001f) GraphicsQuality.SetRenderScale(scale + Step);
        }
    }
}
