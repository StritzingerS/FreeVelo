using System.Linq;
using FreeVelo.Ble;
using FreeVelo.Core.World;
using FreeVelo.Ride;
using FreeVelo.UI;
using FreeVelo.World;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>
    /// Entry point: creates all FreeVelo components when any scene is loaded – no scene setup needed.
    /// </summary>
    public sealed class FreeVeloApp : MonoBehaviour
    {
        private BleService _ble;
        private RideController _ride;
        private RiderAvatar _avatar;
        private Camera _camera;
        private float _lean;
        private float _orbit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<FreeVeloApp>() == null) new GameObject("FreeVeloApp").AddComponent<FreeVeloApp>();
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;

            _ble = gameObject.AddComponent<BleService>();
            var world = gameObject.AddComponent<WorldBuilder>();
            _ride = gameObject.AddComponent<RideController>();
            var hud = gameObject.AddComponent<Hud>();

            var route = ProceduralRoutes.Generate(seed: 1, lengthM: 10_000, hilliness: 0.6);
            world.Build(route);
            _ride.Initialize(_ble, route);
            hud.Initialize(_ble, _ride);
            _avatar = RiderAvatar.Create(world, new Color(0.1f, 0.45f, 0.85f));

            _camera = Camera.main;
            if (_camera == null)
            {
                _camera = new GameObject("Main Camera").AddComponent<Camera>();
                _camera.tag = "MainCamera";
            }

            _camera.nearClipPlane = 0.4f;
            _camera.farClipPlane = 6000f;
            _camera.fieldOfView = 60f;

            SetUpAtmosphere();
            WorkoutStore.ProcessImports();
        }

        /// <summary>Back from the file picker (or any other app): import waiting workout files.</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) WorkoutStore.ProcessImports();
        }

        /// <summary>Sun, ambient light and distance fog (hazy horizon, depth).</summary>
        private static void SetUpAtmosphere()
        {
            var sun = FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            sun.transform.rotation = Quaternion.Euler(36f, 125f, 0f);
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.7f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.6f, 0.58f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.32f, 0.25f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.73f, 0.8f, 0.87f);
            RenderSettings.fogStartDistance = 180f;
            RenderSettings.fogEndDistance = 3600f;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            var sample = _ride.Position;
            float heading = (float)sample.HeadingRad * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan((float)sample.GradePercent / 100f) * Mathf.Rad2Deg;
            // Lean into bends like a real rider: tan(lean) = v²·κ / g, eased in and out over ~0.4 s.
            float targetLean = _ride.State == RideState.Riding
                ? (float)Route.LeanAngleDeg(_ride.SpeedMps, _ride.Route.CurvatureAt(_ride.DistanceM))
                : 0f;
            _lean = Mathf.Lerp(_lean, targetLean, 1f - Mathf.Exp(-dt / 0.4f));
            var rotation = Quaternion.Euler(pitch, heading, -_lean); // negative roll tilts the rider to the right
            var right = Quaternion.Euler(0, heading, 0) * Vector3.right;

            // Ride on the right half of the road.
            var position = new Vector3((float)sample.X, (float)sample.Y + 0.05f, (float)sample.Z) + (right * 1.4f);
            _avatar.transform.SetPositionAndRotation(position, rotation);

            bool riding = _ride.State == RideState.Riding;
            float cadence = riding ? (float)(_ble.Hub.Live.CadenceRpm ?? 0) : 0;
            float speed = riding ? (float)_ride.SpeedMps : 0f; // wheels stand still outside a ride (menus, summary)
            _avatar.Animate(speed, cadence, dt);

            Vector3 target;
            if (riding)
            {
                var flatForward = Quaternion.Euler(0, heading, 0) * Vector3.forward;
                target = position - (flatForward * 4.6f) + (Vector3.up * 1.9f);
            }
            else
            {
                // Menus: slow orbit close to the rider so bike and rider are visible.
                _orbit += dt * 8f;
                var offset = Quaternion.Euler(0, _orbit, 0) * new Vector3(0, 0, -3.6f);
                target = position + offset + (Vector3.up * 1.5f);
            }

            _camera.transform.position = Vector3.Lerp(_camera.transform.position, target, 1f - Mathf.Exp(-4f * dt));
            _camera.transform.LookAt(position + (Vector3.up * 1.05f));
        }
    }
}
