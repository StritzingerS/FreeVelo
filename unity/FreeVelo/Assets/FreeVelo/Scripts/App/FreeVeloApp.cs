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
        private System.Threading.Tasks.Task<(FreeVelo.Core.World.Gen.Terrain Terrain, SceneryLayout Layout, System.Collections.Generic.List<WorldBuilder.FarTileData> Far)> _worldTask;
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

            AppSettings.Preload(); // settings the ride logic reads in the background
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
            // Prepared at build time (Resources/FreeVeloWorld.bytes) or saved on the phone after the first start:
            // loading it takes a fraction of generating it. Only used if it belongs to exactly this map.
            var prepared = Resources.Load<TextAsset>("FreeVeloWorld")?.bytes;
            string cache = System.IO.Path.Combine(Application.persistentDataPath, "world-cache.bin");
            var startClock = System.Diagnostics.Stopwatch.StartNew();
            _startClock = startClock;
            _worldTask = System.Threading.Tasks.Task.Run(() =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                LoadingStep = "loading_world_load";
                var terrain = LoadPreparedWorld(map, prepared, cache);
                bool loaded = terrain != null;
                if (terrain == null)
                {
                    LoadingStep = "loading_world_generate"; // no prepared world: generated once, then kept
                    terrain = FreeVelo.Core.World.Gen.Terrain.Build(map);
                }

                long terrainMs = clock.ElapsedMilliseconds;
                LoadingStep = "loading_world_scenery";
                if (prepared == null) SavePreparedWorld(terrain, cache);
                // Scenery and the mountain's far tiles in parallel, both off the main thread.
                clock.Restart();
                var farClock = System.Diagnostics.Stopwatch.StartNew();
                long farMs = 0;
                var far = System.Threading.Tasks.Task.Run(() =>
                {
                    var tiles = WorldBuilder.ComputeFarTiles(terrain);
                    farMs = farClock.ElapsedMilliseconds;
                    return tiles;
                });
                var layout = SceneryLayout.Generate(terrain);
                long layoutMs = clock.ElapsedMilliseconds;
                var farTiles = far.Result;
                BackgroundTimes = $"world {(loaded ? "loaded" : "generated")} {terrainMs}, scenery layout {layoutMs}, far tiles {farMs} (parallel)";
                return (terrain, layout, farTiles);
            });
            _ride.Initialize(_ble, path.Route);
            _ride.Map = map;
            _ride.Path = path;
            hud.Initialize(_ble, _ride);
            _ride.Group = _group;
            groupAvatars.Initialize(world, _group, () => _group.Lobby != null ? FreeVelo.Core.World.BuiltInRoutes.Get(_group.Lobby.routeId) : map.MainLoop);
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
            _camera.farClipPlane = 16000f; // the mountain is seen from far away
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
            RenderSettings.fogStartDistance = 250f;
            RenderSettings.fogEndDistance = 9000f; // hazy, but the mountain stays visible from the home world
        }

        /// <summary>The world prepared at build time, else the one saved on the phone; null if neither fits the map.</summary>
        private static FreeVelo.Core.World.Gen.Terrain LoadPreparedWorld(WorldMap map, byte[] prepared, string cache)
        {
            if (prepared != null)
            {
                var t = FreeVelo.Core.World.Gen.Terrain.TryLoad(map, new System.IO.MemoryStream(prepared));
                if (t != null) return t;
            }

            try
            {
                if (!System.IO.File.Exists(cache)) return null;
                using var file = System.IO.File.OpenRead(cache);
                return FreeVelo.Core.World.Gen.Terrain.TryLoad(map, file);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>Keeps the generated world on the phone for the next start (unless it is already saved).</summary>
        private static void SavePreparedWorld(FreeVelo.Core.World.Gen.Terrain terrain, string cache)
        {
            try
            {
                if (System.IO.File.Exists(cache))
                {
                    using var existing = System.IO.File.OpenRead(cache);
                    if (FreeVelo.Core.World.Gen.Terrain.IsSavedFor(terrain.Map, existing)) return;
                }

                string tmp = cache + ".tmp";
                using (var file = System.IO.File.Create(tmp)) terrain.Save(file);
                if (System.IO.File.Exists(cache)) System.IO.File.Delete(cache);
                System.IO.File.Move(tmp, cache);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("World cache not saved: " + e.Message);
            }
        }

        /// <summary>What the start-up is doing (L key), shown on the loading screen.</summary>
        public static volatile string LoadingStep = "loading_world_load";

        private void Update()
        {
            HistoryStore.Poll();
            if (_worldTask == null || !_worldTask.IsCompleted) return;
            var task = _worldTask;
            _worldTask = null;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                _world.Failed = true;
                FreeVelo.UI.Ui.Toast(L.Get("world_failed"));
                return;
            }

            long waitedMs = _startClock?.ElapsedMilliseconds ?? 0;
            _world.Build(_ride.Map, task.Result.Terrain, task.Result.Layout, task.Result.Far);
            // Where the start-up time goes, in the log on the devices page ("Show log").
            _ble.AddLog($"Start-up (ms): background {waitedMs} [{BackgroundTimes}]");
            _ble.AddLog("Scene " + _world.BuildTimes.Replace("World build (ms):", "(ms):") + $" · total {_startClock?.ElapsedMilliseconds ?? 0}");
        }

        /// <summary>Set by the render-shot tooling: where the game camera stands and looks instead of following the rider.</summary>
        public static (Vector3 Position, Vector3 Target)? CameraOverride;

        private System.Diagnostics.Stopwatch _startClock;
        private static volatile string BackgroundTimes = "";

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
            // Braking for a bend: the rider stops pedalling and freewheels.
            float cadence = riding && !_ride.IsBraking ? (float)(_ble.Hub.Live.CadenceRpm ?? 0) : 0;
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

            // Follow smoothly; after a jump (a climb started at its foot, joining a group elsewhere) cut straight to
            // the rider instead of gliding through the mountain. Never below the ground.
            var cam = (_camera.transform.position - target).sqrMagnitude > 60f * 60f
                ? target
                : Vector3.Lerp(_camera.transform.position, target, 1f - Mathf.Exp(-4f * dt));
            if (_world.IsBuilt) cam.y = Mathf.Max(cam.y, _world.GroundHeight(cam.x, cam.z) + 0.6f);
            _camera.transform.position = cam;
            _camera.transform.LookAt(position + (Vector3.up * 1.05f));

            // Render shots (editor tooling): the camera stands where the shot wants it.
            if (CameraOverride.HasValue)
            {
                _camera.transform.position = CameraOverride.Value.Position;
                _camera.transform.LookAt(CameraOverride.Value.Target);
            }
        }
    }
}
