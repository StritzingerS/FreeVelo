using System.Linq;
using FreeVelo.Ble;
using FreeVelo.Net;
using FreeVelo.Core.World;
using FreeVelo.Core.World.Gen;
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
        private GroupRideService _group;
        private WorldBuilder _world;
        private System.Threading.Tasks.Task<(FreeVelo.Core.World.Gen.Terrain Terrain, SceneryLayout Layout)> _worldTask;
        private RiderAvatar _avatar;
        private Camera _camera;
        private float _lean;
        private float _orbit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<FreeVeloApp>() == null) new GameObject("FreeVeloApp").AddComponent<FreeVeloApp>();
        }

        /// <summary>
        /// Landscape only (both directions): the interface is laid out for a wide screen. Runs before the splash
        /// screen, so the app does not start in portrait; first forced to landscape, then free to turn by 180°.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void LandscapeOnly()
        {
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            if (Screen.orientation == ScreenOrientation.Portrait || Screen.orientation == ScreenOrientation.PortraitUpsideDown)
            {
                Screen.orientation = ScreenOrientation.LandscapeLeft;
            }

            Screen.orientation = ScreenOrientation.AutoRotation;
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;

            LandscapeOnly();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;

            _ble = gameObject.AddComponent<BleService>();
            var world = gameObject.AddComponent<WorldBuilder>();
            _world = world;
            _ride = gameObject.AddComponent<RideController>();
            var hud = gameObject.AddComponent<Hud>();
            _group = gameObject.AddComponent<GroupRideService>();
            var groupAvatars = gameObject.AddComponent<GroupAvatars>();

            // The generated world: the road network is ready at once (also used by the server); terrain and
            // scenery take a few seconds and are generated in the background, then built into the scene.
            var map = WorldMap.Default;
            var path = NetworkPath.OnMainLoop(map, 0, 0);
            _worldTask = System.Threading.Tasks.Task.Run(() =>
            {
                var terrain = FreeVelo.Core.World.Gen.Terrain.Build(map);
                return (terrain, SceneryLayout.Generate(terrain));
            });
            _ride.Initialize(_ble, path.Route);
            _ride.Map = map;
            _ride.Path = path;
            hud.Initialize(_ble, _ride);
            _ride.Group = _group;
            groupAvatars.Initialize(world, _group, map.MainLoop);
            hud.InitializeGroup(_group, groupAvatars);
            hud.InitializeWorld(world);
            var strava = gameObject.AddComponent<StravaService>();
            strava.Initialize(_ride);
            hud.InitializeStrava(strava);
            _group.RestoreLobby(); // back into the group ride after an app restart
            _avatar = RiderAvatar.Create(world, GroupAvatars.OwnJersey);

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

            // Phase 3: history (old FIT files are read once in the background), training plan, Zwift controllers.
            HistoryStore.ImportMissingRides();
            if (HistoryStore.Data.ftpHistory.Count == 0) HistoryStore.RecordFtp(AppSettings.FtpW, "manual");
            PlanStore.UpdateForToday();
            _ride.RideSaved += OnRideSaved;
            _ble.Hub.ControllerButton += _ride.OnControllerButton;
        }

        /// <summary>A ride ended: into the history, and into the training plan if it was today's workout.</summary>
        private void OnRideSaved(FreeVelo.Ride.RideSummary summary)
        {
            if (summary.Record == null) return;
            HistoryStore.Add(summary.Record);
            summary.PlanSession = PlanStore.RecordRide(summary.Record);
            if (summary.PlanSession != null) FreeVelo.UI.Ui.Toast(L.Get("plan_session_done"));
        }

        /// <summary>Back from the file picker (or any other app): import waiting workout files.</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) return;
            WorkoutStore.ProcessImports();
            PlanStore.UpdateForToday(); // a new day may have started while the app was open
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

        private void Update()
        {
            HistoryStore.Poll();
            if (_worldTask == null || !_worldTask.IsCompleted) return;
            var task = _worldTask;
            _worldTask = null;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                return;
            }

            _world.Build(_ride.Map, task.Result.Terrain, task.Result.Layout);
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
            // Negative roll tilts to the right; the stop lean tips the bike towards the foot on the ground.
            var rotation = Quaternion.Euler(pitch, heading, -(_lean + _avatar.StopLeanDeg));
            var right = Quaternion.Euler(0, heading, 0) * Vector3.right;

            // Ride on the right half of the road (in a group ride in the own lane, so riders do not overlap).
            float lateral = _ride.IsGroupRide ? GroupAvatars.Lateral(_group.State.MyHomeLaneM) : GroupAvatars.BaseLateralM;
            var position = new Vector3((float)sample.X, (float)sample.Y + 0.05f, (float)sample.Z) + (right * lateral);
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
