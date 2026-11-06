using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeVelo.App;
using FreeVelo.Ble;
using FreeVelo.Core.Physics;
using FreeVelo.Core.Recording;
using FreeVelo.Core.Training;
using FreeVelo.Core.Workouts;
using FreeVelo.Core.World;
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
        public int? EstimatedFtpW;
        public IReadOnlyDictionary<int, double> PowerCurve;

        /// <summary>Saved FIT file (null if nothing was recorded or saving failed).</summary>
        public string FitPath;
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

        public RideState State { get; private set; } = RideState.Idle;

        public Route Route { get; private set; }

        public WorkoutEngine Engine { get; private set; }

        public WorkoutState WorkoutState { get; private set; }

        public RideSummary Summary { get; private set; }

        /// <summary>Position on the route (keeps going across rides; loops wrap).</summary>
        public double DistanceM { get; private set; }

        /// <summary>Distance ridden in the current ride (starts at 0 for every ride).</summary>
        public double RideDistanceM => DistanceM - _rideStartDistanceM;

        private double _rideStartDistanceM;

        public double ElapsedS { get; private set; }

        public double SpeedMps => _physics.SpeedMps;

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

        private readonly Queue<(float T, double P)> _powerWindow = new Queue<(float, double)>();

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

        /// <summary>Starts a ride; <paramref name="workout"/> null = free ride.</summary>
        public void StartRide(Workout workout)
        {
            _ble.ApplySettings();
            _recording = new RideRecording(DateTime.UtcNow, workout != null ? workout.Name.Get(AppSettings.Language) : L.Get("free_ride"));
            _lastSegmentIndex = -1;
            _sampleTimer = 0;
            _rideStartDistanceM = DistanceM; // continue where the rider is, but count this ride from 0
            _physics.SpeedMps = 0;
            _physics.RiderMassKg = AppSettings.RiderWeightKg;
            ElapsedS = 0;
            Summary = null;
            IsPaused = false;
            IsAutoPaused = false;
            CueText = null;

            Engine = workout != null ? new WorkoutEngine(workout, AppSettings.FtpW) : null;
            WorkoutState = Engine?.Current;
            _ftpTest = FtpTestOf(workout);

            // Failing is the goal of a ramp test: no ERG reduction at low cadence.
            _ble.Hub.LowCadenceProtectionEnabled = _ftpTest != FtpTestKind.Ramp;
            State = RideState.Riding;
            RideKeepAlive.Start(_recording.Name, L.Get("ride_running"));
            _notificationTimer = 0;
        }

        public void TogglePause() => IsPaused = !IsPaused;

        public void SkipBlock() => Engine?.SkipSegment();

        public void AdjustBlockIntensity(double delta) => Engine?.AdjustCurrentSegmentIntensity(delta);

        public void EndRide()
        {
            if (State != RideState.Riding) return;
            State = RideState.Finished;
            RideKeepAlive.Stop();
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
                PowerCurve = PowerMetrics.PowerCurve(power),
                EstimatedFtpW = _ftpTest.HasValue && Engine != null ? FtpTests.EstimateFtp(_ftpTest.Value, power, Engine.Workout) : null,
                FitPath = SaveFit(ftp),
            };
        }

        /// <summary>Writes the ride as FIT file to persistentDataPath/rides; returns the path.</summary>
        private string SaveFit(int ftp)
        {
            if (_recording == null || _recording.Samples.Count < 10) return null;
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "rides");
                Directory.CreateDirectory(folder);
                string safeName = new string(_recording.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
                string path = Path.Combine(folder, $"{_recording.StartUtc.ToLocalTime():yyyy-MM-dd_HH-mm}_{safeName}.fit");
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

        private bool _finishPending;
        private DateTime _lastStepUtc;

        private void Update()
        {
            lock (BleService.Sync)
            {
                if (_finishPending)
                {
                    _finishPending = false;
                    ShowCue(L.Get("finished"));
                    EndRide();
                    return;
                }

                Step(Mathf.Min(Time.deltaTime, 0.25f), background: false);
            }
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

            // Auto pause when the rider stops pedaling (keeps workout and recording aligned).
            _idleTime = power <= 0 && cadence <= 0 ? _idleTime + dt : 0;
            IsAutoPaused = _idleTime >= AutoPauseAfterS;
            bool running = !IsPaused && !IsAutoPaused;

            var position = Route.Sample(DistanceM);
            double crr = RoadSurfaces.RollingResistance(position.Surface);
            _physics.Step(IsPaused ? 0 : power, position.GradePercent, crr, dt);
            DistanceM += _physics.SpeedMps * dt;

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
            else
            {
                _ble.Hub.SetSimulation(position.GradePercent, crr, _physics.WindResistanceCoefficient);
            }

            if (Engine != null && Engine.IsFinished)
            {
                // Saving the FIT file needs the main thread: in the background, finish when the app returns.
                if (background)
                {
                    // The recording ends here (no more samples, no catch-up); release the trainer and tell the
                    // rider, who cannot see the screen.
                    _finishPending = true;
                    _physics.SpeedMps = 0;
                    _ble.Hub.SetSimulation(0);
                    RideKeepAlive.Update(L.Get("finished"));
                    RideKeepAlive.Alert(L.Get("finished"), L.Get("finished_background"));
                }
                else
                {
                    ShowCue(L.Get("finished"));
                    EndRide();
                }
            }
        }

        /// <summary>Background tick from the Android plugin (Unity paused): Bluetooth, trainer and ride go on.</summary>
        private void BackgroundTick(float dt)
        {
            lock (BleService.Sync)
            {
                _ble.BackgroundTick();
                if (!_finishPending) Step(Mathf.Min(dt, 1f), background: true);
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
            if (_backgroundSinceUtc.HasValue && State == RideState.Riding && !_finishPending)
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
                DistanceM += _physics.SpeedMps;
                ElapsedS += 1;

                if (Engine != null && !Engine.IsFinished)
                {
                    WorkoutState = Engine.Update(1);
                    if (Engine.CurrentIndex != _lastSegmentIndex && !Engine.IsFinished)
                    {
                        if (_lastSegmentIndex >= 0) _recording.StartLap();
                        _lastSegmentIndex = Engine.CurrentIndex;
                    }
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
        }

        private void ShowCue(string text)
        {
            CueText = text;
            CueUntil = ClockS + 5f;
        }
    }
}
