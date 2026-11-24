using System.Collections.Generic;
using FreeVelo.Core.World.Gen;
using UnityEngine;

namespace FreeVelo.World
{
    /// <summary>
    /// Thin smoke from some of the chimneys (about two houses in five), drifting with the wind. The chimneys are found
    /// in the models themselves (<see cref="FindChimneys"/>): a model without a clear chimney gets no smoke, so nothing
    /// smokes out of a roof. One particle system for all, fed by hand within 350 m of the camera.
    /// </summary>
    public sealed class ChimneySmoke : MonoBehaviour
    {
        private const float Reach = 350f;
        private const float PuffsPerSecond = 6f;

        private static readonly SceneryKind[] Kinds = { SceneryKind.House, SceneryKind.AlpineHouse, SceneryKind.Farmhouse, SceneryKind.Inn, SceneryKind.AlpineHut };

        private WorldBuilder _world;
        private readonly List<(SceneryItem Item, float Phase)> _houses = new List<(SceneryItem, float)>();
        private readonly Dictionary<(SceneryKind, int), Vector3[]> _chimneys = new Dictionary<(SceneryKind, int), Vector3[]>();
        private readonly List<(Vector3 Top, float Phase)> _sources = new List<(Vector3, float)>();
        private Vector3 _lastUpdate = new Vector3(float.MaxValue, 0, 0);
        private ParticleSystem _particles;
        private float _clock;

        /// <summary>Smoke puffs alive (for the render-shot breakdown).</summary>
        public static int Alive, Sources;

        public void Init(WorldBuilder world, List<SceneryItem> items)
        {
            _world = world;
            _houses.Clear();
            _sources.Clear();
            _lastUpdate = new Vector3(float.MaxValue, 0, 0);
            foreach (var item in items)
            {
                if (System.Array.IndexOf(Kinds, item.Kind) < 0) continue;
                // About two in five houses have the stove on, the same ones every day.
                float h = Hash(item.X, item.Z);
                if (h > 0.42f) continue;
                _houses.Add((item, h * 40f));
            }
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (_world == null || cam == null || _houses.Count == 0) return;
            if (_particles == null) CreateParticles();
            var eye = cam.transform.position;

            // Which chimneys are near (rebuilt every 25 m of camera travel).
            if ((eye - _lastUpdate).sqrMagnitude > 25f * 25f)
            {
                _lastUpdate = eye;
                _sources.Clear();
                foreach (var (item, phase) in _houses)
                {
                    float dx = item.X - eye.x, dz = item.Z - eye.z;
                    if ((dx * dx) + (dz * dz) > (Reach + 50f) * (Reach + 50f)) continue;
                    var tops = Chimneys(item.Kind, item.Variant);
                    if (tops.Length == 0) continue;
                    var m = WorldBuilder.PlacementMatrix(item);
                    _sources.Add((m.MultiplyPoint3x4(tops[0]), phase));
                }
            }

            // Wind: the same as the foliage's (WorldAnimation's globals).
            var wind = Shader.GetGlobalVector("_FvWindDir");
            float strength = Shader.GetGlobalFloat("_FvWindStrength");
            var drift = new Vector3(wind.x, 0f, wind.z) * (0.4f + strength);
            var force = _particles.forceOverLifetime;
            force.x = drift.x * 0.35f;
            force.z = drift.z * 0.35f;

            float dt = Time.deltaTime;
            float before = _clock;
            _clock += dt;
            var emit = new ParticleSystem.EmitParams();
            foreach (var (top, phase) in _sources)
            {
                float d = Vector3.Distance(top, eye);
                if (d > Reach) continue;
                // Whole puffs at this chimney's own rhythm.
                int n = Mathf.FloorToInt((_clock + phase) * PuffsPerSecond) - Mathf.FloorToInt((before + phase) * PuffsPerSecond);
                for (int k = 0; k < n; k++)
                {
                    emit.position = top + new Vector3(Random.Range(-0.08f, 0.08f), 0.1f, Random.Range(-0.08f, 0.08f));
                    emit.velocity = (Vector3.up * Random.Range(0.6f, 0.9f)) + (drift * 0.3f);
                    emit.startSize = Random.Range(0.35f, 0.55f);
                    emit.startLifetime = Random.Range(6f, 9f);
                    emit.rotation = Random.Range(0f, 360f);
                    _particles.Emit(emit, 1);
                }
            }

            Alive = _particles.particleCount;
            Sources = _sources.Count;
        }

        /// <summary>Chimney tops of a model (local space); empty if it has none that can be told apart from the roof.</summary>
        private Vector3[] Chimneys(SceneryKind kind, int variant)
        {
            if (_chimneys.TryGetValue((kind, variant), out var tops)) return tops;
            var mesh = _world.SceneryMesh(kind, variant);
            tops = mesh != null ? FindChimneys(mesh.vertices) : new Vector3[0];
            _chimneys[(kind, variant)] = tops;            return tops;
        }

        /// <summary>
        /// Chimneys in a building's vertices: on a 0.4 m grid seen from above, a spot that stands at least 0.5 m above
        /// everything 1.2–2.4 m round it and in the upper part of the building. A ridge does not count (it continues at
        /// the same height), nor a dormer (the roof rises above it towards the ridge). The top of each such spot.
        /// </summary>
        public static Vector3[] FindChimneys(Vector3[] vertices)
        {
            if (vertices.Length == 0) return new Vector3[0];
            const float cell = 0.4f;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue, top = float.MinValue;
            foreach (var v in vertices)
            {
                minX = Mathf.Min(minX, v.x);
                maxX = Mathf.Max(maxX, v.x);
                minZ = Mathf.Min(minZ, v.z);
                maxZ = Mathf.Max(maxZ, v.z);
                top = Mathf.Max(top, v.y);
            }

            int nx = Mathf.CeilToInt((maxX - minX) / cell) + 1, nz = Mathf.CeilToInt((maxZ - minZ) / cell) + 1;
            if (nx * nz > 400000) return new Vector3[0];
            var height = new float[nx * nz];
            for (int k = 0; k < height.Length; k++) height[k] = float.MinValue;
            foreach (var v in vertices)
            {
                int k = (Mathf.FloorToInt((v.z - minZ) / cell) * nx) + Mathf.FloorToInt((v.x - minX) / cell);
                height[k] = Mathf.Max(height[k], v.y);
            }

            var found = new List<Vector3>();
            int inner = Mathf.CeilToInt(1.2f / cell), outer = Mathf.CeilToInt(2.4f / cell);
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    float h = height[(j * nx) + i];
                    if (h < top * 0.6f) continue;
                    bool chimney = true, any = false;
                    for (int dj = -outer; dj <= outer && chimney; dj++)
                    {
                        for (int di = -outer; di <= outer; di++)
                        {
                            int r2 = (di * di) + (dj * dj);
                            if (r2 < inner * inner || r2 > outer * outer) continue;
                            int ii = i + di, jj = j + dj;
                            if (ii < 0 || jj < 0 || ii >= nx || jj >= nz) continue;
                            float around = height[(jj * nx) + ii];
                            if (around == float.MinValue) continue;
                            any = true;
                            if (around > h - 0.5f)
                            {
                                chimney = false;
                                break;
                            }
                        }
                    }

                    if (!chimney || !any) continue;
                    var p = new Vector3(minX + ((i + 0.5f) * cell), h, minZ + ((j + 0.5f) * cell));
                    // One per chimney: keep the highest spot within 1 m.
                    int near = found.FindIndex(f => (new Vector2(f.x - p.x, f.z - p.z)).sqrMagnitude < 1f);
                    if (near < 0) found.Add(p);
                    else if (p.y > found[near].y) found[near] = p;
                }
            }

            return found.ToArray();
        }

        private void CreateParticles()
        {
            var go = new GameObject("ChimneySmoke");
            go.transform.SetParent(transform, false);
            _particles = go.AddComponent<ParticleSystem>();
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _particles.main;
            main.loop = true; // keeps simulating; nothing is emitted by itself (emission off)
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;
            main.startColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            var emission = _particles.emission;
            emission.enabled = false;
            var shape = _particles.shape;
            shape.enabled = false;

            // Grows as it rises and thins out: faint at the chimney, most visible after a second, gone at the end.
            var size = _particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(0.3f, 3f), new Keyframe(1f, 7f)));
            var colour = _particles.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.75f, 0.75f, 0.76f), 0f), new GradientColorKey(new Color(0.92f, 0.92f, 0.93f), 1f) },
                new[] { new GradientAlphaKey(0.2f, 0f), new GradientAlphaKey(0.3f, 0.06f), new GradientAlphaKey(0.12f, 0.55f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;
            var force = _particles.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            var limit = _particles.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 2.5f;
            limit.dampen = 0.08f;
            var noise = _particles.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.35f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var rotation = _particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

            var renderer = _particles.GetComponent<ParticleSystemRenderer>();
            var template = Resources.Load<Material>("FreeVeloSmoke");
            var material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var puff = PuffTexture();
            material.SetTexture("_BaseMap", puff);
            material.mainTexture = puff;
            material.SetColor("_BaseColor", Color.white);
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            _particles.Play();
        }

        /// <summary>A soft, slightly lumpy round puff (64 × 64, white with alpha).</summary>
        private static Texture2D PuffTexture()
        {
            const int size = 64;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "SmokePuff", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = ((x + 0.5f) / size * 2f) - 1f, v = ((y + 0.5f) / size * 2f) - 1f;
                    float r = Mathf.Sqrt((u * u) + (v * v));
                    float lump = 0.75f + (0.25f * Mathf.PerlinNoise(x * 0.12f, y * 0.12f));
                    float a = Mathf.Clamp01(1f - (r / lump));
                    a = a * a * (3f - (2f * a));
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            t.SetPixels32(pixels);
            t.Apply(true, true);
            return t;
        }

        private static float Hash(float x, float z)
        {
            uint h = (uint)((Mathf.RoundToInt(x * 10f) * 73856093) ^ (Mathf.RoundToInt(z * 10f) * 19349663));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }
}
