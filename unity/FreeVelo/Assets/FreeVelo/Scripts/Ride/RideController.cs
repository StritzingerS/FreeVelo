using System.Collections.Generic;
using System.Linq;
using FreeVelo.App;
using FreeVelo.Ble;
using FreeVelo.Core.Physics;
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
    }

    /// <summary>
    /// Runs a ride: virtual speed from power (physics on the route), workout engine,
    /// trainer control (ERG / SIM) and 1-second recording.
    /// </summary>
    public sealed class RideController : MonoBehaviour
    {
        private const float AutoPauseAfterS = 3f;

        private readonly RiderPhysics _physics = new RiderPhysics();
        private readonly List<double> _power = new List<double>();
        private readonly List<double> _heartRate = new List<double>();
        private readonly List<double> _cadence = new List<double>();
        private BleService _ble;
        private float _sampleTimer;
        private float _idleTime;
        private FtpTestKind? _ftpTest;

        public RideState State { get; private set; } = RideState.Idle;

        public Route Route { get; private set; }

        public WorkoutEngine Engine { get; private set; }

        public WorkoutState WorkoutState { get; private set; }

        public RideSummary Summary { get; private set; }

        public double DistanceM { get; private set; }

        public double ElapsedS { get; private set; }

        public double SpeedMps => _physics.SpeedMps;

        public RouteSample Position => Route.Sample(DistanceM);

        /// <summary>True while the rider does not pedal (time and workout stop).</summary>
        public bool IsAutoPaused { get; private set; }

        /// <summary>Paused by the user.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>Latest workout cue text and until when it is shown (Time.time).</summary>
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
        }

        /// <summary>Starts a ride; <paramref name="workout"/> null = free ride.</summary>
        public void StartRide(Workout workout)
        {
            _ble.ApplySettings();
            _power.Clear();
            _heartRate.Clear();
            _cadence.Clear();
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
        }

        public void TogglePause() => IsPaused = !IsPaused;

        public void SkipBlock() => Engine?.SkipSegment();

        public void AdjustBlockIntensity(double delta) => Engine?.AdjustCurrentSegmentIntensity(delta);

        public void EndRide()
        {
            if (State != RideState.Riding) return;
            State = RideState.Finished;
            _ble.Hub.SetSimulation(0); // release ERG resistance
            _ble.Hub.LowCadenceProtectionEnabled = true;

            double np = PowerMetrics.NormalizedPower(_power);
            int ftp = AppSettings.FtpW;
            Summary = new RideSummary
            {
                Title = Engine != null ? Engine.Workout.Name.Get(AppSettings.Language) : L.Get("free_ride"),
                DurationS = (int)ElapsedS,
                DistanceM = DistanceM,
                AvgPowerW = PowerMetrics.Average(_power),
                NormalizedPowerW = np,
                IntensityFactor = PowerMetrics.IntensityFactor(np, ftp),
                Tss = PowerMetrics.TrainingStressScore(_power.Count, np, ftp),
                AvgHeartRate = _heartRate.Count > 0 ? _heartRate.Average() : (double?)null,
                PowerCurve = PowerMetrics.PowerCurve(_power),
                EstimatedFtpW = _ftpTest.HasValue && Engine != null ? FtpTests.EstimateFtp(_ftpTest.Value, _power, Engine.Workout) : null,
            };
        }

        public void Close()
        {
            State = RideState.Idle;
            Engine = null;
            WorkoutState = null;
        }

        private void Update()
        {
            if (State != RideState.Riding || Route == null) return;

            float dt = Mathf.Min(Time.deltaTime, 0.25f);
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
                }

                _sampleTimer += dt;
                while (_sampleTimer >= 1f)
                {
                    _sampleTimer -= 1f;
                    _power.Add(power);
                    _cadence.Add(cadence);
                    if (live.HeartRateBpm.HasValue) _heartRate.Add(live.HeartRateBpm.Value);
                }
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
                ShowCue(L.Get("finished"));
                EndRide();
            }
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
            _powerWindow.Enqueue((Time.time, power));
            while (_powerWindow.Count > 0 && Time.time - _powerWindow.Peek().T > 3f) _powerWindow.Dequeue();
            Power3s = _powerWindow.Average(x => x.P);
        }

        private void ShowCue(string text)
        {
            CueText = text;
            CueUntil = Time.time + 5f;
        }
    }
}
