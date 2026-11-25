using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FreeVelo.World
{
    /// <summary>
    /// The living world, all cheap: wind for the foliage (globals read by the FreeVelo/LitWind shader), ripples
    /// drifting over the water (the water material's normal map is moved), and cloud shadows drifting over the land
    /// (a cookie on the sun). Added by <see cref="WorldBuilder"/> when it builds the world.
    /// </summary>
    public sealed class WorldAnimation : MonoBehaviour
    {
        /// <summary>The water material (normal-mapped, world-scaled UVs); null if the map has no water.</summary>
        public Material Water;

        /// <summary>Wind from the west-south-west, as on most fair days in the Alps' north side.</summary>
        private static readonly Vector2 WindFrom = new Vector2(-0.93f, -0.37f);

        private static readonly int WindDirId = Shader.PropertyToID("_FvWindDir");
        private static readonly int WindStrengthId = Shader.PropertyToID("_FvWindStrength");
        private static Texture2D _waterNormal, _clouds;

        private Light _sun;
        private UniversalAdditionalLightData _sunData;
        private Vector2 _cloudOffset, _waterOffset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _waterNormal = null;
            _clouds = null;
        }

        private void Update()
        {
            float t = Time.time;
            var dir = -WindFrom.normalized; // where the wind blows to
            // A breeze that comes and goes over a minute or two.
            float strength = 0.65f + (0.2f * Mathf.Sin(t * 0.05f)) + (0.1f * Mathf.Sin(t * 0.13f + 1.3f));
            // Low quality: no wind (the shader then skips its work) and no cloud shadows.
            bool lite = FreeVelo.App.AppSettings.Lite;
            if (lite) strength = 0f;
            Shader.SetGlobalVector(WindDirId, new Vector4(dir.x, 0f, dir.y, 0f));
            Shader.SetGlobalFloat(WindStrengthId, strength);

            if (Water != null)
            {
                // Ripples drift downwind at about 0.3 m/s (UVs: 35 m a repeat).
                _waterOffset += dir * (0.009f * Time.deltaTime);
                _waterOffset.x = Mathf.Repeat(_waterOffset.x, 1f);
                _waterOffset.y = Mathf.Repeat(_waterOffset.y, 1f);
                Water.SetTextureOffset(BaseMapId, _waterOffset);
            }

            UpdateClouds(dir, lite);
        }

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        /// <summary>Cloud shadows: a tiling cloud cookie on the sun (about 900 m a repeat), moving with the wind at about 6 m/s.</summary>
        private void UpdateClouds(Vector2 dir, bool lite)
        {
            if (_sun == null)
            {
                _sun = RenderSettings.sun != null ? RenderSettings.sun : FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
                if (_sun == null) return;
                _sunData = _sun.GetUniversalAdditionalLightData();
                if (_sunData != null) _sunData.lightCookieSize = new Vector2(CloudTileM, CloudTileM);
            }

            // on and off with the graphics quality (it can change during a ride)
            var cookie = lite ? null : CloudCookie();
            if (_sun.cookie != cookie) _sun.cookie = cookie;
            if (_sunData == null || lite) return;
            _cloudOffset += dir * (6f * Time.deltaTime);
            _cloudOffset.x = Mathf.Repeat(_cloudOffset.x, CloudTileM);
            _cloudOffset.y = Mathf.Repeat(_cloudOffset.y, CloudTileM);
            _sunData.lightCookieOffset = _cloudOffset;
        }

        private const float CloudTileM = 900f;

        private void OnDestroy()
        {
            if (_sun != null) _sun.cookie = null;
        }

        /// <summary>Tiling wave normal map for the water (sum of waves with whole periods per tile).</summary>
        public static Texture2D WaterNormalMap()
        {
            if (_waterNormal != null) return _waterNormal;
            const int size = 256;
            var heights = new float[size * size];
            // (cycles across, cycles down, amplitude, phase)
            // Few, crossing waves of different lengths (the finer ones made a visible grid at a distance).
            var waves = new[] { (2, 1, 0.5f, 0.0f), (-1, 3, 0.45f, 1.7f), (4, -3, 0.25f, 2.9f), (-5, -2, 0.15f, 0.6f), (3, 7, 0.08f, 4.1f) };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size, h = 0f;
                    foreach (var (fx, fy, a, p) in waves) h += a * Mathf.Sin((2f * Mathf.PI * ((fx * u) + (fy * v))) + p);
                    heights[(y * size) + x] = h;
                }
            }

            _waterNormal = SurfaceTextures.NormalMap("WaterRipples", heights, size, 10f);
            return _waterNormal;
        }

        /// <summary>Tiling cloud cookie: 1 = sunlit, about 0.55 under a cloud, with soft edges.</summary>
        private static Texture2D CloudCookie()
        {
            if (_clouds != null) return _clouds;
            const int size = 256;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = 0f, amplitude = 0.5f, total = 0f;
                    for (int octave = 0, period = 4; octave < 5; octave++, period *= 2)
                    {
                        n += amplitude * TilingNoise((float)x / size * period, (float)y / size * period, period);
                        total += amplitude;
                        amplitude *= 0.5f;
                    }

                    float cloud = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.68f, n / total));
                    byte value = (byte)Mathf.RoundToInt((1f - (0.5f * cloud)) * 255f);
                    pixels[(y * size) + x] = new Color32(value, value, value, 255);
                }
            }

            _clouds = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "CloudShadows", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            _clouds.SetPixels32(pixels);
            _clouds.Apply(true, true);
            return _clouds;
        }

        /// <summary>Smooth value noise in 0–1 that repeats every <paramref name="period"/> cells.</summary>
        private static float TilingNoise(float x, float y, int period)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - (2f * fx));
            fy = fy * fy * (3f - (2f * fy));
            float a = Hash(x0, y0, period), b = Hash(x0 + 1, y0, period), c = Hash(x0, y0 + 1, period), d = Hash(x0 + 1, y0 + 1, period);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Hash(int x, int y, int period)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            uint h = (uint)((x * 374761393) + (y * 668265263) + (period * 2147483647));
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }
}
