using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeVelo.App;
using FreeVelo.Ble;
using FreeVelo.Core.History;
using FreeVelo.Core.Physics;
using FreeVelo.Core.Recording;
using FreeVelo.Core.Training;
using FreeVelo.Core.Workouts;
using FreeVelo.Core.World;
using FreeVelo.Core.World.Gen;
using FreeVelo.Devices.Zwift;
using FreeVelo.Net;
using FreeVelo.Shared.Api;
using FreeVelo.UI;
using UnityEngine;

namespace FreeVelo.Ride
{
    public enum RideState
    {
        Idle,
        Riding,
        Finished,
    }

    /// <summary>Result of a finished ride.</summary>
    public sealed class RideSummary
    {
        public string Title;
        public int DurationS;
        public double DistanceM;
        public double AvgPowerW;
        public double NormalizedPowerW;
        public double IntensityFactor;
        public double Tss;
        public double? AvgHeartRate;
        public int? MaxPowerW;
        public double AscentM;

        /// <summary>Energy burned in kcal: about the mechanical work in kJ (body efficiency ≈ 24 % cancels 4.184 kJ/kcal).</summary>
        public double Kcal;
        public int? EstimatedFtpW;
        public IReadOnlyDictionary<int, double> PowerCurve;

        /// <summary>Saved FIT file (null if nothing was recorded or saving failed).</summary>
        public string FitPath;

        /// <summary>The ride's entry in the history (null without FIT file).</summary>
        public RideRecord Record;

        /// <summary>The training plan session this ride completed (set by the app).</summary>
        public FreeVelo.Core.Plans.PlannedSession PlanSession;

        /// <summary>Famous climbs ridden to the top in this ride.</summary>
        public List<ClimbEffort> Climbs = new List<ClimbEffort>();
    }

    /// <summary>
    /// Runs a ride: virtual speed from power (physics on the route), workout engine,
    /// trainer control (ERG / SIM) and 1-second recording.
    /// </summary>
    public sealed class RideController : MonoBehaviour
    {
        private const float AutoPauseAfterS = 3f;

        private readonly RiderPhysics _physics = new RiderPhysics();
        private RideRecording _recording;
        private int _lastSegmentIndex = -1;
        private BleService _ble;
        private float _sampleTimer;
        private float _idleTime;
        private FtpTestKind? _ftpTest;
        private double _intensitySum;
        private int _intensitySeconds;
        private int _pendingShift;
        private bool _wasGroupRide;
        private bool _fixedRoute;
        private double _lastClimbM = double.NaN;
        private readonly List<ClimbEffort> _climbEfforts = new List<ClimbEffort>();

        /// <summary>The famous climb the rider is on (foot passed, summit not yet reached), or null.</summary>
        public ClimbSegment ActiveClimb { get; private set; }

        /// <summary>Ride time when <see cref="ActiveClimb"/> started.</summary>
        public double ActiveClimbStartS { get; private set; }

        /// <summary>Metres ridden on <see cref="ActiveClimb"/>.</summary>
        public double ActiveClimbProgressM { get; private set; }

        /// <summary>The current ride follows a fixed route (a climb started from the menu).</summary>
        public bool IsClimbRide => _fixedRoute;

        /// <summary>Virtual gears (used when <see cref="AppSettings.VirtualGears"/> is on).</summary>
        public VirtualGears Gears { get; private set; } = new VirtualGears();

        /// <summary>Virtual gears act now: a free / group ride or a free part of a workout (in ERG blocks, shifting changes the intensity).</summary>
        public bool GearsActive => State == RideState.Riding && AppSettings.VirtualGears && !InErgBlock;

        public bool InErgBlock => WorkoutState != null && WorkoutState.TargetW.HasValue && !WorkoutState.IsFinished;

        public RideState State { get; private set; } = RideState.Idle;

        public Route Route { get; private set; }

        public WorkoutEngine Engine { get; private set; }

        public WorkoutState WorkoutState { get; private set; }

        public RideSummary Summary { get; private set; }

        /// <summary>A ride ended and its FIT file was written (e.g. for the Strava upload).</summary>
        public event Action<RideSummary> RideSaved;

        /// <summary>Position on the route (keeps going across rides; loops wrap).</summary>
        public double DistanceM { get; private set; }

        /// <summary>Distance ridden in the current ride (starts at 0 for every ride).</summary>
        public double RideDistanceM => DistanceM - _rideStartDistanceM;

        private double _rideStartDistanceM;

        public double ElapsedS { get; private set; }

        /// <summary>Elevation gained in this ride (metres climbed, descents not subtracted).</summary>
        public double AscentM { get; private set; }

        private double? _lastElevation;

        /// <summary>Adds the climb since the last step; jumps (joining a group, a new route) are not counted.</summary>
        private void TrackAscent()
        {
            double y = Route.Sample(DistanceM).Y;
            if (_lastElevation.HasValue)
            {
                double rise = y - _lastElevation.Value;
                if (rise > 0 && rise < 5) AscentM += rise;
            }

            _lastElevation = y;
        }

        public double SpeedMps => _physics.SpeedMps;

        /// <summary>True while the rider brakes for a bend ahead (descents).</summary>
        public bool IsBraking { get; private set; }

        private double _brakeHoldS;

        public RouteSample Position => Route.Sample(DistanceM);

        /// <summary>True while the rider does not pedal (time and workout stop).</summary>
        public bool IsAutoPaused { get; private set; }

        /// <summary>Paused by the user.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>Latest workout cue text and until when it is shown (<see cref="ClockS"/>).</summary>
        public string CueText { get; private set; }

        public float CueUntil { get; private set; }

        /// <summary>Power averaged over the last 3 seconds (for display).</summary>
        public double Power3s { get; private set; }

        /// <summary>Power for the display: 1 s average (reacts quickly, still steady).</summary>
        public double PowerShown { get; private set; }

        private readonly Queue<(float T, double P)> _powerWindow = new Queue<(float, double)>();

        /// <summary>Group rides (set by the app; null = solo only).</summary>
        public GroupRideService Group { get; set; }

        /// <summary>The current ride is a group ride (live with the others).</summary>
        public bool IsGroupRide { get; private set; }

        /// <summary>The generated world (road network).</summary>
        public WorldMap Map { get; set; }

        /// <summary>Solo rides: the way through the road network (the rider picks left / right at junctions).</summary>
        public NetworkPath Path { get; set; }

        /// <summary>The next junction ahead (solo rides; null in group rides, which follow the main loop).</summary>
        public UpcomingJunction NextJunction()
        {
            if (IsGroupRide || Path == null) return null;
            lock (BleService.Sync) return Path.Next();
        }

        /// <summary>Picks the way on at the next junction (index into its options).</summary>
        public void ChooseTurn(int option)
        {
            lock (BleService.Sync) Path?.Choose(option);
        }

        public void Initialize(BleService ble, Route route)
        {
            _ble = ble;
            Route = route;
        }

        public void SetRoute(Route route)
        {
            Route = route;
            DistanceM = 0;
            _rideStartDistanceM = 0;
        }

        /// <summary>
        /// Starts a ride at the foot of a famous climb (from the menu): round the mountain – up, over the top, down the
        /// other side and back – optionally with a workout.
        /// </summary>
        public void StartClimb(string climbId, Workout workout = null)
        {
            var route = BuiltInRoutes.Get(climbId == ClimbData.Stelvio ? BuiltInRoutes.StelvioId : BuiltInRoutes.AlpeId);
            lock (BleService.Sync)
            {
                Route = route;
                Path = null;
                DistanceM = 0;
                _rideStartDistanceM = 0;
                _fixedRoute = true;
            }

            StartRide(workout);
        }

        /// <summary>Starts a ride; <paramref name="workout"/> null = free ride; <paramref name="intensity"/> from the training plan.</summary>
        public void StartRide(Workout workout, double intensity = 1.0)
        {
            _ble.ApplySettings();
            IsGroupRide = Group != null && Group.Lobby != null;
            // Group rides follow the lobby's route (main loop or round the mountain; the server's Keep Together
            // simulation uses the same route).
            if (IsGroupRide && Map != null)
            {
                Route = BuiltInRoutes.Get(Group.Lobby.routeId);
                _fixedRoute = false;
            }
            else if (!_fixedRoute && Path != null) Route = Path.Route;
            string title = workout != null ? workout.Name.Get(AppSettings.Language) : IsGroupRide ? Group.Lobby.name : L.Get("free_ride");
            _recording = new RideRecording(DateTime.UtcNow, title);
            _lastSegmentIndex = -1;
            _workoutComplete = false;
            _sampleTimer = 0;
            _rideStartDistanceM = DistanceM; // continue where the rider is, but count this ride from 0
            _physics.SpeedMps = 0;
            IsBraking = false;
            _physics.RiderMassKg = AppSettings.RiderWeightKg;
            ElapsedS = 0;
            AscentM = 0;
            _lastElevation = null;
            Summary = null;
            IsPaused = false;
            IsAutoPaused = false;
            CueText = null;

            Engine = workout != null ? new WorkoutEngine(workout, AppSettings.FtpW) : null;
            if (Engine != null) Engine.GlobalIntensity = intensity;
            WorkoutState = Engine?.Current;
            _intensitySum = 0;
            _intensitySeconds = 0;
            _climbEfforts.Clear();
            ActiveClimb = null;
            _lastClimbM = double.NaN;
            if (Gears.Count != AppSettings.GearCount) Gears = new VirtualGears(AppSettings.GearCount);
            Gears.Set(Gears.NearestGear(Gears.ReferenceRatio));
            _ftpTest = FtpTestOf(workout);

            // Failing is the goal of a ramp test: no ERG reduction at low cadence.
            _ble.Hub.LowCadenceProtectionEnabled = _ftpTest != FtpTestKind.Ramp;
            State = RideState.Riding;
            if (IsGroupRide) Group.StartLive();
            RideKeepAlive.Start(_recording.Name, L.Get("ride_running"));
            _notificationTimer = 0;
        }

        public void TogglePause() => IsPaused = !IsPaused;

        public void SkipBlock() => Engine?.SkipSegment();

        public void AdjustBlockIntensity(double delta) => Engine?.AdjustCurrentSegmentIntensity(delta);

        /// <summary>
        /// One shift (+1 harder, −1 easier) from the on-screen buttons or a Zwift controller: in an ERG block the block's
        /// intensity changes by 1 %, otherwise the virtual gear.
        /// </summary>
        public void Shift(int steps)
        {
            if (State != RideState.Riding) return;
            if (InErgBlock) AdjustBlockIntensity(0.01 * AppSettings.ErgStepPercent * steps);
            else if (AppSettings.VirtualGears) Gears.Shift(steps);
        }

        /// <summary>Zwift controller buttons (may come from the Bluetooth thread); what they do is set in Settings.</summary>
        public void OnControllerButton(ZwiftButton button)
        {
            int step = AppSettings.ButtonAction(button); // as assigned in Settings (cached, thread-safe)
            if (step != 0) System.Threading.Interlocked.Add(ref _pendingShift, step);
        }

        public void EndRide()
        {
            if (State != RideState.Riding) return;
            State = RideState.Finished;
            RideKeepAlive.Stop();
            if (IsGroupRide) Group.StopLive(); // still in the group ride (waiting room) until the rider leaves it
            if ((IsGroupRide || _fixedRoute) && Map != null)
            {
                // Back on the road network: from the place on the main loop, or (after a ride round the mountain) at
                // the start of the main loop.
                bool onLoop = Route == Map.MainLoop;
                lock (BleService.Sync)
                {
                    Path = NetworkPath.OnMainLoop(Map, onLoop ? DistanceM : 0, DistanceM);
                    Route = Path.Route;
                }
            }

            _fixedRoute = false;
            ActiveClimb = null;

            _wasGroupRide = IsGroupRide;
            IsGroupRide = false;
            IsBraking = false;
            _physics.SpeedMps = 0; // the rider stops: no more wheel/world movement after the ride
            _ble.Hub.SetSimulation(0); // release ERG resistance
            _ble.Hub.LowCadenceProtectionEnabled = true;

            var power = _recording.PowerSeries();
            var stats = _recording.Stats();
            double np = PowerMetrics.NormalizedPower(power);
            int ftp = AppSettings.FtpW;
            Summary = new RideSummary
            {
                Title = _recording.Name,
                DurationS = stats.Seconds,
                DistanceM = RideDistanceM,
                AvgPowerW = stats.AvgPowerW ?? 0,
                NormalizedPowerW = np,
                IntensityFactor = PowerMetrics.IntensityFactor(np, ftp),
                Tss = PowerMetrics.TrainingStressScore(power.Count, np, ftp),
                AvgHeartRate = stats.AvgHeartRate,
                MaxPowerW = stats.MaxPowerW,
                AscentM = stats.AscentM,
                Kcal = stats.WorkKj,
                PowerCurve = PowerMetrics.PowerCurve(power),
                Climbs = new List<ClimbEffort>(_climbEfforts),
                EstimatedFtpW = _ftpTest.HasValue && Engine != null ? FtpTests.EstimateFtp(_ftpTest.Value, power, Engine.Workout) : null,
                FitPath = SaveFit(ftp),
            };
            if (Summary.FitPath != null)
            {
                Summary.Record = BuildRecord(Summary, ftp);
                RideSaved?.Invoke(Summary);
            }
        }

        /// <summary>The ride's history entry: summary from the recorded seconds, plus what was ridden.</summary>
        private RideRecord BuildRecord(RideSummary summary, int ftp)
        {
            var samples = _recording.Samples
                .Select(s => new FitRecord(s.TimeUtc, s.PowerW, s.HeartRateBpm, s.CadenceRpm, s.DistanceM, s.SpeedMps, s.AltitudeM))
                .ToList();
            var record = RideAnalysis.Summarize(System.IO.Path.GetFileName(summary.FitPath), summary.Title, _recording.StartUtc, samples, ftp);
            record.distanceM = Math.Round(summary.DistanceM, 1);
            record.groupRide = _wasGroupRide;
            record.climbs = _climbEfforts.ToArray();
            record.estimatedFtpW = summary.EstimatedFtpW ?? 0;
            if (Engine != null)
            {
                record.workoutId = Engine.Workout.Id ?? "";
                record.workoutCompleted = _workoutComplete ? 1 : Math.Min(1, Engine.ElapsedS / Math.Max(1, Engine.TotalDurationS));
                record.workoutIntensity = _intensitySeconds > 0 ? Math.Round(_intensitySum / _intensitySeconds, 3) : Engine.GlobalIntensity;
            }

            return record;
        }

        /// <summary>Writes the ride as FIT file to persistentDataPath/rides; returns the path.</summary>
        private string SaveFit(int ftp)
        {
            if (_recording == null || _recording.Samples.Count < 10) return null;
            try
            {
                string folder = System.IO.Path.Combine(Application.persistentDataPath, "rides");
                Directory.CreateDirectory(folder);
                string safeName = new string(_recording.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
                string path = System.IO.Path.Combine(folder, $"{_recording.StartUtc.ToLocalTime():yyyy-MM-dd_HH-mm}_{safeName}.fit");
                File.WriteAllBytes(path, FitActivityWriter.Write(_recording, FitActivityWriter.SessionInfo(_recording, ftp)));
                Debug.Log("FreeVelo: ride saved to " + path);
                return path;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        public void Close()
        {
            State = RideState.Idle;
            RideKeepAlive.Stop();
            Engine = null;
            WorkoutState = null;
        }

        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Ride clock in seconds (usable from any thread, unlike Time.time).</summary>
        public float ClockS => (float)Clock.Elapsed.TotalSeconds;

        private bool _workoutComplete;
        private DateTime _lastStepUtc;

        /// <summary>The workout is done; the ride goes on as a free ride until the rider ends it.</summary>
        public bool WorkoutComplete => _workoutComplete;

        private void Update()
        {
            lock (BleService.Sync)
            {
                Step(Mathf.Min(Time.deltaTime, 0.25f), background: false);
            }
        }

        /// <summary>
        /// Once, when the last block ends: the ride and its recording go on in free-ride mode (trainer simulates
        /// the road, own lap), so a 50 min workout can be ridden on to a full hour.
        /// </summary>
        private void CompleteWorkoutIfDone(bool background)
        {
            if (_workoutComplete || Engine == null || !Engine.IsFinished) return;
            _workoutComplete = true;
            _recording.StartLap();
            ShowCue(L.Get("finished"));
            if (background) RideKeepAlive.Alert(L.Get("finished"), L.Get("finished_background"));
        }

        /// <summary>
        /// One step of the ride. Runs every frame, and while the app is in the background from the Android
        /// plugin's ticks (no Unity main-thread APIs in here).
        /// </summary>
        private void Step(float dt, bool background)
        {
            if (State != RideState.Riding || Route == null) return;
            _lastStepUtc = DateTime.UtcNow;
            var live = _ble.Hub.Live;
            double power = live.PowerW ?? 0;
            double cadence = live.CadenceRpm ?? 0;

            UpdatePower3s(power);
            int shifts = System.Threading.Interlocked.Exchange(ref _pendingShift, 0);
            if (shifts != 0) Shift(shifts);

            // Auto pause when the rider stops pedaling (keeps workout and recording aligned).
            _idleTime = power <= 0 && cadence <= 0 ? _idleTime + dt : 0;
            IsAutoPaused = _idleTime >= AutoPauseAfterS;
            bool running = !IsPaused && !IsAutoPaused;

            var position = Route.Sample(DistanceM);
            double crr = RoadSurfaces.RollingResistance(position.Surface);
            _physics.Step(IsPaused ? 0 : power, position.GradePercent, crr, dt);
            // Brake before tight bends (descents): only the virtual speed, the trainer keeps simulating the road.
            double limit = Cornering.StepLimit(Route, DistanceM, _physics.SpeedMps, dt);
            // Shown for at least half a second, so the brake chip and the freewheeling legs do not flicker.
            _brakeHoldS = Cornering.Brake(_physics, limit, dt) ? 0.5 : Math.Max(0, _brakeHoldS - dt);
            IsBraking = _brakeHoldS > 0;
            DistanceM += _physics.SpeedMps * dt;
            if (IsGroupRide)
            {
                ApplyGroup(cadence, dt);
                IsBraking = _physics.SpeedMps > 3 && _physics.SpeedMps >= limit - 0.3; // the server brakes for the group
            }
            else if (Path != null && Path.Update(DistanceM)) Route = Path.Route; // next junction reached: chosen road added
            UpdateClimb();
            TrackAscent();

            if (running)
            {
                ElapsedS += dt;
                if (Engine != null)
                {
                    WorkoutState = Engine.Update(dt);
                    foreach (var cue in WorkoutState.NewCues) ShowCue(cue.Text.Get(AppSettings.Language));

                    // One FIT lap per workout block.
                    if (Engine.CurrentIndex != _lastSegmentIndex && !Engine.IsFinished)
                    {
                        if (_lastSegmentIndex >= 0) _recording.StartLap();
                        _lastSegmentIndex = Engine.CurrentIndex;
                    }
                }

                _sampleTimer += dt;
                while (_sampleTimer >= 1f)
                {
                    _sampleTimer -= 1f;
                    if (InErgBlock)
                    {
                        _intensitySum += WorkoutState.SegmentIntensity;
                        _intensitySeconds++;
                    }

                    // Whole seconds, strictly increasing (FIT records are 1 s apart).
                    var now = DateTime.UtcNow;
                    var samples = _recording.Samples;
                    if (samples.Count > 0 && now < samples[samples.Count - 1].TimeUtc.AddSeconds(1)) now = samples[samples.Count - 1].TimeUtc.AddSeconds(1);
                    _recording.Add(new RideSample(
                        now,
                        live.PowerW,
                        live.HeartRateBpm,
                        live.CadenceRpm.HasValue ? (int?)Mathf.RoundToInt((float)live.CadenceRpm.Value) : null,
                        _physics.SpeedMps,
                        RideDistanceM,
                        position.Y,
                        WorkoutState?.TargetW));
                }
            }

            // Notification text (distance, 3 s power) every 5 s.
            _notificationTimer -= dt;
            if (_notificationTimer <= 0)
            {
                _notificationTimer = 5f;
                RideKeepAlive.Update($"{RideDistanceM / 1000:0.0} km · {Power3s:0} W");
            }

            // Trainer: ERG from the workout, otherwise simulate the road.
            if (WorkoutState != null && WorkoutState.TargetW.HasValue && !WorkoutState.IsFinished)
            {
                _ble.Hub.SetErgTarget(WorkoutState.TargetW.Value);
            }
            else if (!AppSettings.VirtualGears)
            {
                _ble.Hub.SetVirtualGear(null);
                _ble.Hub.SetSimulation(position.GradePercent, crr, _physics.WindResistanceCoefficient);
            }
            else if (_ble.Hub.TrainerSupportsShifting)
            {
                // Virtual gears built into the trainer: it gets the plain road and the gear, and works out the rest.
                _ble.Hub.SetVirtualGear(Gears.Factor);
                _ble.Hub.SetSimulation(position.GradePercent, crr, _physics.WindResistanceCoefficient);
            }
            else
            {
                // Other trainers: the bike stays in one gear, the trainer gets the road as felt in the virtual gear.
                _ble.Hub.SetVirtualGear(null);
                double grade = position.GradePercent * _ble.Hub.TrainerDifficulty; // (not PlayerPrefs: may run in the background)
                double mass = _physics.TotalMassKg;
                double cw = _physics.WindResistanceCoefficient;
                double? trainerKmh = live.TrainerSpeedKmh;
                double trainerSpeed = trainerKmh.HasValue ? trainerKmh.Value / 3.6 : _physics.SpeedMps / Gears.Factor;
                _ble.Hub.SetSimulation(Gears.EffectiveGrade(grade, trainerSpeed, mass, crr, cw), crr, cw, scaleByDifficulty: false);
            }

            CompleteWorkoutIfDone(background);
        }

        /// <summary>
        /// Climb timer: starts when the rider passes the foot of a famous climb (on the route, uphill) and stops at the
        /// summit; the time is the ride time in between (pauses don't count).
        /// </summary>
        private void UpdateClimb()
        {
            var route = Route;
            double d = route.IsLoop ? ((DistanceM % route.LengthM) + route.LengthM) % route.LengthM : DistanceM;
            if (ActiveClimb == null)
            {
                foreach (var c in route.Climbs)
                {
                    bool crossed = double.IsNaN(_lastClimbM) || _lastClimbM <= c.StartM + 0.001;
                    if (crossed && d >= c.StartM && d < c.StartM + 50)
                    {
                        ActiveClimb = c;
                        ActiveClimbStartS = ElapsedS;
                        break;
                    }
                }
            }
            else
            {
                var c = ActiveClimb;
                ActiveClimbProgressM = Math.Max(0, d - c.StartM);
                if (d >= c.EndM - 0.5)
                {
                    _climbEfforts.Add(new ClimbEffort { id = c.Id, name = c.Name, timeS = Math.Round(ElapsedS - ActiveClimbStartS, 1) });
                    ShowCue(string.Format(L.Get("summit_reached"), c.Name, Ui.Clock(ElapsedS - ActiveClimbStartS)));
                    ActiveClimb = null;
                }
                else if (d < c.StartM - 20 || d > c.EndM + 200)
                {
                    ActiveClimb = null; // left the climb (route changed)
                }
            }

            _lastClimbM = d;
        }

        /// <summary>
        /// Group ride, every step (also in the background): Keep Together places the rider where the server's group
        /// simulation says (the trainer still gets the grade of that spot); Free mode starts next to the group once.
        /// Jumps into the group do not count as ridden distance. Then the own state goes to the server.
        /// </summary>
        private void ApplyGroup(double cadence, float dt)
        {
            if (Group.Mode == LobbyMode.KeepTogether)
            {
                if (Group.TryGetOwnPosition(out double groupDistance, out float groupSpeed, out bool welcomed))
                {
                    double error = groupDistance - DistanceM;
                    if (welcomed || Math.Abs(error) > 30)
                    {
                        // (Re)connected or far off: jump into the group (not counted as ridden distance).
                        if (welcomed) _rideStartDistanceM += error;
                        DistanceM = groupDistance;
                    }
                    else
                    {
                        // The rider already moved on with the group speed this step; close the remaining gap to the
                        // server's position gently (~0.5 s), so small corrections never show as jerks.
                        DistanceM += error * Math.Min(1.0, dt * 2.0);
                    }

                    _physics.SpeedMps = groupSpeed;
                }
            }
            else if (Group.TryTakeStartDistance(out double start))
            {
                _rideStartDistanceM += start - DistanceM;
                DistanceM = start;
            }

            // Mass set at the start of the ride (PlayerPrefs cannot be read from the background thread).
            Group.Publish(DistanceM, _physics.SpeedMps, Power3s / Math.Max(1.0, _physics.RiderMassKg), cadence);
        }

        /// <summary>Background tick from the Android plugin (Unity paused): Bluetooth, trainer and ride go on.</summary>
        private void BackgroundTick(float dt)
        {
            lock (BleService.Sync)
            {
                _ble.BackgroundTick();
                Step(Mathf.Min(dt, 1f), background: true);
            }
        }

        private float _notificationTimer;
        private DateTime? _backgroundSinceUtc;

        /// <summary>
        /// Unity stops updating while the app is in the background (screen off, other app). The foreground service
        /// keeps Bluetooth and the trainer's ERG target alive meanwhile; on return the ride is caught up: workout
        /// time, distance and 1 s samples (last known power/cadence/heart rate) for the gap.
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                if (State == RideState.Riding)
                {
                    _backgroundSinceUtc = DateTime.UtcNow;
                    _lastStepUtc = DateTime.UtcNow;

                    // The plugin keeps calling the ride logic (same code as every frame) four times a second.
                    _ble.StartBackgroundTicks(BackgroundTick, 250);

                    // Fallback, used by the plugin only if those ticks stop: the coming ERG targets on time.
                    if (Engine != null && !Engine.IsFinished && !IsPaused)
                    {
                        _ble.ScheduleBackgroundWrites(_ble.Hub.BuildErgSchedule(Engine.TargetsAhead(6 * 3600)));
                    }
                }

                return;
            }

            _ble.StopBackgroundTicks();
            _ble.CancelBackgroundWrites();
            RideKeepAlive.ClearAlert();

            // Catch up only what the background ticks did not cover (normally nothing).
            if (_backgroundSinceUtc.HasValue && State == RideState.Riding)
            {
                lock (BleService.Sync)
                {
                    double gap = (DateTime.UtcNow - _lastStepUtc).TotalSeconds;
                    if (gap > 1.5) CatchUp(_lastStepUtc, Math.Min(gap, 6 * 3600));
                }
            }

            _backgroundSinceUtc = null;
            _ble.Hub.ResendOnNextTick(); // the trainer may hold a scheduled target: send ours again
        }

        private void CatchUp(DateTime fromUtc, double seconds)
        {
            if (IsPaused || Route == null) return;
            var live = _ble.Hub.Live;
            int? heartRate = live.HeartRateBpm;
            int? cadence = live.CadenceRpm.HasValue ? (int?)Mathf.RoundToInt((float)live.CadenceRpm.Value) : null;
            double lastPower = Power3s;
            if (lastPower <= 0 && (cadence ?? 0) <= 0) return; // the rider had stopped: auto pause covers the gap

            var samples = _recording.Samples;
            var time = fromUtc;
            for (int i = 0; i < (int)seconds; i++)
            {
                // In ERG the trainer holds the target, so it is the best estimate of the power ridden.
                double power = WorkoutState != null && WorkoutState.TargetW.HasValue && !WorkoutState.IsFinished ? WorkoutState.TargetW.Value : lastPower;
                var position = Route.Sample(DistanceM);
                _physics.Step(power, position.GradePercent, RoadSurfaces.RollingResistance(position.Surface), 1f);
                Cornering.Brake(_physics, Cornering.StepLimit(Route, DistanceM, _physics.SpeedMps, 1f), 1f);
                DistanceM += _physics.SpeedMps;
                ElapsedS += 1;
                TrackAscent();

                if (Engine != null && !Engine.IsFinished)
                {
                    WorkoutState = Engine.Update(1);
                    if (Engine.CurrentIndex != _lastSegmentIndex && !Engine.IsFinished)
                    {
                        if (_lastSegmentIndex >= 0) _recording.StartLap();
                        _lastSegmentIndex = Engine.CurrentIndex;
                    }

                    CompleteWorkoutIfDone(background: false);
                }

                time = time.AddSeconds(1);
                if (samples.Count > 0 && time <= samples[samples.Count - 1].TimeUtc) time = samples[samples.Count - 1].TimeUtc.AddSeconds(1);
                _recording.Add(new RideSample(time, (int)Math.Round(power), heartRate, cadence, _physics.SpeedMps, RideDistanceM, position.Y, WorkoutState?.TargetW));
            }

            Debug.Log($"FreeVelo: caught up {seconds:0} s after the app was in the background");
        }

        private static FtpTestKind? FtpTestOf(Workout workout)
        {
            switch (workout?.Id)
            {
                case "ftp-ramp": return FtpTestKind.Ramp;
                case "ftp-20min": return FtpTestKind.TwentyMinute;
                case "ftp-2x8": return FtpTestKind.TwoByEight;
                default: return null;
            }
        }

        private void UpdatePower3s(double power)
        {
            float now = ClockS;
            _powerWindow.Enqueue((now, power));
            while (_powerWindow.Count > 0 && now - _powerWindow.Peek().T > 3f) _powerWindow.Dequeue();
            Power3s = _powerWindow.Average(x => x.P);
            PowerShown = _powerWindow.Where(x => now - x.T <= 1f).Select(x => x.P).DefaultIfEmpty(power).Average();
        }

        private void ShowCue(string text)
        {
            CueText = text;
            CueUntil = ClockS + 5f;
        }
    }
}
