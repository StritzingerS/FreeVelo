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

            _camera.farClipPlane = 2500f;
            _camera.fieldOfView = 60f;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            var sample = _ride.Position;
            float heading = (float)sample.HeadingRad * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan((float)sample.GradePercent / 100f) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(pitch, heading, 0);
            var right = Quaternion.Euler(0, heading, 0) * Vector3.right;

            // Ride on the right half of the road.
            var position = new Vector3((float)sample.X, (float)sample.Y + 0.05f, (float)sample.Z) + (right * 1.4f);
            _avatar.transform.SetPositionAndRotation(position, rotation);

            bool riding = _ride.State == RideState.Riding;
            float cadence = riding ? (float)(_ble.Hub.Live.CadenceRpm ?? 0) : 0;
            _avatar.Animate((float)_ride.SpeedMps, cadence, dt);

            Vector3 target;
            if (riding)
            {
                var flatForward = Quaternion.Euler(0, heading, 0) * Vector3.forward;
                target = position - (flatForward * 6.5f) + (Vector3.up * 2.6f);
            }
            else
            {
                _orbit += dt * 8f;
                var offset = Quaternion.Euler(0, _orbit, 0) * new Vector3(0, 0, -7.5f);
                target = position + offset + (Vector3.up * 2.8f);
            }

            _camera.transform.position = Vector3.Lerp(_camera.transform.position, target, 1f - Mathf.Exp(-4f * dt));
            _camera.transform.LookAt(position + (Vector3.up * 1.1f));
        }
    }
}
