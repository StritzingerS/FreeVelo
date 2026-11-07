using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Devices.Ble;
using FreeVelo.Devices.CyclingPower;
using FreeVelo.Devices.Ftms;
using FreeVelo.Devices.HeartRate;
using FreeVelo.Devices.Tacx;
using FreeVelo.Devices.Zwift;

namespace FreeVelo.Devices.Session
{
    /// <summary>Writes a payload to a characteristic of a connected device (implemented by the BLE layer).</summary>
    public interface IBleWriter
    {
        bool Write(string address, Guid characteristic, byte[] data);
    }

    /// <summary>A write to send <see cref="OffsetMs"/> after the schedule starts.</summary>
    public readonly struct ScheduledWrite
    {
        public ScheduledWrite(long offsetMs, string address, Guid characteristic, byte[] data)
        {
            OffsetMs = offsetMs;
            Address = address;
            Characteristic = characteristic;
            Data = data;
        }

        public long OffsetMs { get; }

        public string Address { get; }

        public Guid Characteristic { get; }

        public byte[] Data { get; }
    }

    public enum TrainerProtocol
    {
        None,
        Ftms,
        TacxFec,
    }

    /// <summary>Which device delivers the power value.</summary>
    public enum PowerSource
    {
        /// <summary>Trainer power (falls back to the power meter if no trainer is connected).</summary>
        Trainer,

        /// <summary>External power meter, e.g. pedals (falls back to the trainer).</summary>
        PowerMeter,
    }

    /// <summary>Combined live values of all connected devices.</summary>
    public sealed class LiveData
    {
        public int? PowerW { get; internal set; }

        public double? CadenceRpm { get; internal set; }

        /// <summary>Speed reported by the trainer (not used for the virtual speed).</summary>
        public double? TrainerSpeedKmh { get; internal set; }

        public int? HeartRateBpm { get; internal set; }

        public int? TrainerPowerW { get; internal set; }

        public int? PowerMeterPowerW { get; internal set; }

        public IReadOnlyList<double> LatestRrIntervalsMs { get; internal set; } = Array.Empty<double>();
    }

    /// <summary>
    /// Device-independent session logic: assigns roles to connected devices, merges their data
    /// and controls the trainer (ERG / SIM) via FTMS or Tacx FE-C.
    /// Call <see cref="Tick"/> regularly (e.g. 4×/s); it is the only place where commands are sent.
    /// </summary>
    public sealed class DeviceHub
    {
        /// <summary>Values older than this are dropped.</summary>
        public const long StaleAfterMs = 3000;

        /// <summary>Maximum ERG change per second when ramping to a new target.</summary>
        public const double ErgRampWattsPerSecond = 50;

        /// <summary>ERG targets are reduced while the cadence stays below this value.</summary>
        public const double LowCadenceRpm = 45;

        private const long LowCadenceDelayMs = 3000;
        private const double LowCadenceFactor = 0.7;
        private const long SimMinIntervalMs = 1000;
        private const long FecUserConfigMinIntervalMs = 10_000;
        private const long FtmsControlRetryMs = 5000;

        // "Long ago" without overflowing in (now - NeverMs).
        private const long NeverMs = long.MinValue / 4;

        private static readonly Guid FtmsService = GattUuids.FromShort(GattUuids.FitnessMachineService);
        private static readonly Guid CyclingPowerService = GattUuids.FromShort(GattUuids.CyclingPowerService);
        private static readonly Guid HeartRateService = GattUuids.FromShort(GattUuids.HeartRateService);
        private static readonly Guid IndoorBikeDataChar = GattUuids.FromShort(GattUuids.IndoorBikeData);
        private static readonly Guid ControlPointChar = GattUuids.FromShort(GattUuids.FitnessMachineControlPoint);
        private static readonly Guid TrainingStatusChar = GattUuids.FromShort(GattUuids.TrainingStatus);
        private static readonly Guid CyclingPowerChar = GattUuids.FromShort(GattUuids.CyclingPowerMeasurement);
        private static readonly Guid HeartRateChar = GattUuids.FromShort(GattUuids.HeartRateMeasurement);

        private readonly IBleWriter _writer;
        private readonly Func<long> _clockMs;
        private readonly Dictionary<string, HashSet<Guid>> _services = new Dictionary<string, HashSet<Guid>>();
        private readonly Dictionary<string, CrankCadenceCalculator> _cadence = new Dictionary<string, CrankCadenceCalculator>();

        private (long Ms, int Value)? _trainerPowerValue;
        private (long Ms, int Value)? _meterPowerValue;
        private (long Ms, double Value)? _trainerCadence;
        private readonly CadenceGlitchFilter _trainerCadenceFilter = new CadenceGlitchFilter();
        private (long Ms, double Value)? _meterCadence;
        private (long Ms, double Value)? _trainerSpeed;
        private (long Ms, int Value)? _heartRate;
        private (long Ms, int Value)? _trainerHeartRate;

        // Trainer control state
        private bool _ergMode;
        private int _ergTarget;
        private double _ergSent = double.NaN;
        private double _ergCurrent = double.NaN;
        private long _lastTickMs;
        private double _simGrade;
        private double _simCrr = 0.004;
        private double _simCw = 0.51;
        private double _simSentGrade = double.NaN;
        private long _simSentMs = NeverMs;
        private bool _resendNeeded;
        private bool _ftmsControlGranted;
        private bool _ftmsControlRequested;
        private long _ftmsControlRequestedMs;
        private long _lowCadenceSinceMs = -1;
        private bool _ergFromGears;
        private bool _simScale = true;

        // Virtual shifting in the trainer (Zwift protocol)
        private double? _gearFactor;
        private double _gearSent = double.NaN;
        private bool _shiftHandshakeSent;
        private bool _shiftGradeSent;
        private bool _shiftWasUsed;

        private sealed class ControllerLink
        {
            public bool ClickV2;
            public long HandshakeMs = NeverMs;
            public ZwiftControllerState State = ZwiftControllerState.Connecting;
            public HashSet<ZwiftButton> Held = new HashSet<ZwiftButton>();
            public int? Battery;
        }

        private readonly Dictionary<string, ControllerLink> _controllers = new Dictionary<string, ControllerLink>();
        private byte? _ftmsTrainingStatus;
        private long _ergOverSinceMs = -1;
        private long _ergTooFastUntilMs = NeverMs;
        private bool _fecUserConfigRequested;
        private long _fecUserConfigSentMs = NeverMs;

        public DeviceHub(IBleWriter writer, Func<long> clockMs)
        {
            _writer = writer;
            _clockMs = clockMs;
        }

        public string? TrainerAddress { get; private set; }

        public TrainerProtocol TrainerProtocol { get; private set; }

        public string? PowerMeterAddress { get; private set; }

        public string? HeartRateAddress { get; private set; }

        public PowerSource PowerSource { get; set; } = PowerSource.Trainer;

        /// <summary>Rider weight sent to Tacx FE-C trainers (SIM physics).</summary>
        public double RiderWeightKg { get; set; } = 75;

        /// <summary>Scales the grade sent to the trainer (0–1), e.g. 0.5 = climbs feel half as steep.</summary>
        public double TrainerDifficulty { get; set; } = 1.0;

        /// <summary>Reduce ERG targets at low cadence (turn off for ramp tests, where failing is the goal).</summary>
        public bool LowCadenceProtectionEnabled { get; set; } = true;

        /// <summary>True while the ERG target is reduced because of low cadence.</summary>
        public bool LowCadenceProtectionActive { get; private set; }

        public bool IsErgMode => _ergMode;

        /// <summary>
        /// FTMS training status "speed outside of control region – high": the wheel turns too fast for the trainer
        /// to get down to the ERG target even at minimum resistance (typical for rollers like the Elite Nero).
        /// </summary>
        public const byte FtmsStatusSpeedTooHigh = 0x0A;

        /// <summary>
        /// True while the trainer cannot hold the ERG target because the rider is too fast (gear too big): the
        /// trainer reports it, or the power stays clearly above the target.
        /// </summary>
        public bool ErgSpeedTooHigh { get; private set; }

        /// <summary>Estimated speed (km/h) at which the trainer could hold the current ERG target.</summary>
        public double? ErgMaxSpeedKmh { get; private set; }

        /// <summary>The ERG target currently sent to the trainer (after ramping/protection).</summary>
        public int? SentErgTargetW => double.IsNaN(_ergSent) ? (int?)null : (int)Math.Round(_ergSent);

        /// <summary>Raised for diagnostics (commands, roles, trainer responses).</summary>
        public event Action<string>? Log;

        /// <summary>A Zwift controller button went down (once per press).</summary>
        public event Action<ZwiftButton>? ControllerButton;

        /// <summary>Connected Zwift controllers and their state (Locked = Click V2 left unit not unlocked by Zwift).</summary>
        public IReadOnlyList<(string Address, ZwiftControllerState State, int? Battery)> Controllers =>
            _controllers.Select(c => (c.Key, c.Value.State, c.Value.Battery)).ToList();

        public bool IsController(string address) => _controllers.ContainsKey(address);

        // ------------------------------------------------------------------ connection events

        /// <summary>A device finished connecting; <paramref name="services"/> are its GATT services.</summary>
        public void OnReady(string address, IEnumerable<Guid> services)
        {
            var set = new HashSet<Guid>(services);
            _services[address] = set;

            if (ZwiftController.IsController(set) && !ZwiftTrainer.SupportsShifting(set, FtmsService, TacxFec.Service))
            {
                // Zwift Click / Click V2 / Play: buttons only, never trainer or power meter.
                _controllers[address] = new ControllerLink { ClickV2 = set.Contains(ZwiftController.RideService) };
                Log?.Invoke($"{address}: Zwift controller");
                return;
            }

            if (set.Contains(FtmsService) && TrainerProtocol != TrainerProtocol.Ftms)
            {
                SetTrainer(address, TrainerProtocol.Ftms);
            }
            else if (set.Contains(TacxFec.Service) && TrainerProtocol == TrainerProtocol.None)
            {
                SetTrainer(address, TrainerProtocol.TacxFec);
            }
            else if (set.Contains(CyclingPowerService) && address != TrainerAddress && PowerMeterAddress == null)
            {
                PowerMeterAddress = address;
                Log?.Invoke($"{address}: power meter");
            }

            if (set.Contains(HeartRateService) && HeartRateAddress == null)
            {
                HeartRateAddress = address;
                Log?.Invoke($"{address}: heart rate");
            }

            if (address == TrainerAddress)
            {
                // (Re)connected: the trainer may have lost its target.
                _resendNeeded = true;
                _ftmsControlGranted = false;
                _ftmsControlRequested = false;
                _shiftHandshakeSent = false;
                _shiftGradeSent = false;
                _gearSent = double.NaN;
                if (TrainerSupportsShifting) Log?.Invoke($"{address}: trainer supports virtual shifting");
            }
        }

        /// <summary>The trainer has virtual shifting built in (Zwift protocol, see <see cref="ZwiftTrainer"/>).</summary>
        public bool TrainerSupportsShifting =>
            TrainerAddress != null && _services.TryGetValue(TrainerAddress, out var s) && ZwiftTrainer.SupportsShifting(s, FtmsService, TacxFec.Service);

        /// <summary>Virtual gears are being done by the trainer itself right now (SIM mode with a gear set).</summary>
        public bool UsesTrainerShifting => TrainerSupportsShifting && _gearFactor.HasValue && !_ergMode;

        /// <summary>
        /// Virtual gear for trainers with built-in shifting: virtual ratio / ratio of the gear the bike is in (null =
        /// no virtual gears). With such a trainer, SIM mode then sends the plain road and this gear, and the trainer
        /// works out the resistance; other trainers ignore it (the app adjusts the grade instead).
        /// </summary>
        public void SetVirtualGear(double? gearFactor) => _gearFactor = gearFactor;

        /// <summary>A device disconnected (it may reconnect automatically and call <see cref="OnReady"/> again).</summary>
        public void OnDisconnected(string address)
        {
            _cadence.Remove(address);
            if (_controllers.TryGetValue(address, out var controller))
            {
                controller.State = ZwiftControllerState.Connecting;
                controller.HandshakeMs = NeverMs;
                controller.Held.Clear();
            }

            if (address == TrainerAddress)
            {
                _ftmsControlGranted = false;
                _ftmsControlRequested = false;
                _ftmsTrainingStatus = null;
                _trainerCadenceFilter.Reset();
            }
        }

        /// <summary>The user removed a device: forget its role.</summary>
        public void Forget(string address)
        {
            OnDisconnected(address);
            _services.Remove(address);
            _controllers.Remove(address);
            if (TrainerAddress == address)
            {
                TrainerAddress = null;
                TrainerProtocol = TrainerProtocol.None;
            }

            if (PowerMeterAddress == address) PowerMeterAddress = null;
            if (HeartRateAddress == address) HeartRateAddress = null;
        }

        private void SetTrainer(string address, TrainerProtocol protocol)
        {
            if (PowerMeterAddress == address) PowerMeterAddress = null;
            TrainerAddress = address;
            TrainerProtocol = protocol;
            Log?.Invoke($"{address}: trainer ({protocol})");
        }

        // ------------------------------------------------------------------ data

        /// <summary>Notification or read value from a device.</summary>
        public void OnData(string address, Guid characteristic, byte[] data)
        {
            long now = _clockMs();
            bool isTrainer = address == TrainerAddress;

            if (_controllers.TryGetValue(address, out var controller))
            {
                OnControllerData(address, controller, characteristic, data, now);
                return;
            }

            if (characteristic == TrainingStatusChar && data.Length >= 2)
            {
                if (_ftmsTrainingStatus != data[1]) Log?.Invoke($"FTMS training status 0x{data[1]:X2}");
                _ftmsTrainingStatus = data[1];
            }
            else if (characteristic == IndoorBikeDataChar && IndoorBikeData.TryParse(data, out var ibd) && ibd != null)
            {
                if (ibd.PowerW.HasValue) _trainerPowerValue = (now, ibd.PowerW.Value);
                if (ibd.CadenceRpm.HasValue) _trainerCadence = (now, _trainerCadenceFilter.Next(now, ibd.CadenceRpm.Value, ibd.PowerW));
                if (ibd.SpeedKmh.HasValue) _trainerSpeed = (now, ibd.SpeedKmh.Value);
                if (ibd.HeartRateBpm.HasValue && ibd.HeartRateBpm > 0) _trainerHeartRate = (now, ibd.HeartRateBpm.Value);
            }
            else if (characteristic == TacxFec.RxCharacteristic && FecData.TryParse(data, out var fec) && fec != null)
            {
                if (fec.PowerW.HasValue) _trainerPowerValue = (now, fec.PowerW.Value);
                if (fec.CadenceRpm.HasValue) _trainerCadence = (now, _trainerCadenceFilter.Next(now, fec.CadenceRpm.Value, Value(_trainerPowerValue, now)));
                if (fec.SpeedKmh.HasValue) _trainerSpeed = (now, fec.SpeedKmh.Value);
                if (fec.HeartRateBpm.HasValue) _trainerHeartRate = (now, fec.HeartRateBpm.Value);
                if (fec.Page == 0x19 && fec.TrainerStatus.HasFlag(FecTrainerStatus.UserConfigurationRequired))
                {
                    _fecUserConfigRequested = true;
                }

                if (fec.CommandResult.HasValue && fec.CommandResult != FecCommandResult.Pass && fec.CommandResult != FecCommandResult.Pending)
                {
                    Log?.Invoke($"FE-C command 0x{fec.LastCommandPage:X2}: {fec.CommandResult}");
                }
            }
            else if (characteristic == ControlPointChar && FtmsControlPointResponse.TryParse(data, out var response) && response != null)
            {
                if (response.RequestOpCode == FtmsOpCode.RequestControl)
                {
                    _ftmsControlGranted = response.IsSuccess;
                }
                else if (response.Result == FtmsResultCode.ControlNotPermitted)
                {
                    _ftmsControlGranted = false;
                    _ftmsControlRequested = false;
                    _resendNeeded = true;
                }

                if (!response.IsSuccess) Log?.Invoke($"FTMS {response}");
            }
            else if (characteristic == CyclingPowerChar && CyclingPowerMeasurement.TryParse(data, out var cps) && cps != null)
            {
                if (!_cadence.TryGetValue(address, out var calc))
                {
                    calc = new CrankCadenceCalculator();
                    _cadence[address] = calc;
                }

                double? cadence = cps.CumulativeCrankRevolutions.HasValue && cps.LastCrankEventTime.HasValue
                    ? calc.Update(cps.CumulativeCrankRevolutions.Value, cps.LastCrankEventTime.Value, now)
                    : calc.CadenceRpm;

                if (isTrainer)
                {
                    // Trainers with a dedicated protocol report power there; CPS is only a fallback.
                    if (TrainerProtocol == TrainerProtocol.None || !Fresh(_trainerPowerValue, now)) _trainerPowerValue = (now, cps.PowerW);
                    if (cadence.HasValue && !Fresh(_trainerCadence, now)) _trainerCadence = (now, cadence.Value);
                }
                else
                {
                    _meterPowerValue = (now, cps.PowerW);
                    if (cadence.HasValue) _meterCadence = (now, cadence.Value);
                }
            }
            else if (characteristic == HeartRateChar && HeartRateMeasurement.TryParse(data, out var hr) && hr != null)
            {
                _heartRate = (now, hr.Bpm);
                Live.LatestRrIntervalsMs = hr.RrIntervalsMs;
            }

            UpdateLive(now);
        }

        /// <summary>Combined live values (updated by <see cref="OnData"/> and <see cref="Tick"/>).</summary>
        public LiveData Live { get; } = new LiveData();

        private void UpdateLive(long now)
        {
            int? trainer = Value(_trainerPowerValue, now);
            int? meter = Value(_meterPowerValue, now);
            Live.TrainerPowerW = trainer;
            Live.PowerMeterPowerW = meter;
            Live.PowerW = PowerSource == PowerSource.PowerMeter ? meter ?? trainer : trainer ?? meter;

            double? trainerCad = Value(_trainerCadence, now);
            double? meterCad = Value(_meterCadence, now);
            Live.CadenceRpm = PowerSource == PowerSource.PowerMeter ? meterCad ?? trainerCad : trainerCad ?? meterCad;

            Live.TrainerSpeedKmh = Value(_trainerSpeed, now);
            Live.HeartRateBpm = Value(_heartRate, now) ?? Value(_trainerHeartRate, now);
        }

        private static bool Fresh<T>((long Ms, T Value)? sample, long now) where T : struct =>
            sample.HasValue && now - sample.Value.Ms <= StaleAfterMs;

        private static T? Value<T>((long Ms, T Value)? sample, long now) where T : struct =>
            Fresh(sample, now) ? sample!.Value.Value : (T?)null;

        // ------------------------------------------------------------------ trainer control

        /// <summary>ERG mode: the trainer holds <paramref name="watts"/>. Large jumps are ramped.</summary>
        public void SetErgTarget(int watts) => SetErgTarget(watts, virtualGears: false);

        /// <summary>
        /// ERG target; <paramref name="virtualGears"/>: it comes from virtual gears ("SIM via ERG") – no low-cadence
        /// protection and no "too fast" hint, since the target already follows the cadence.
        /// </summary>
        public void SetErgTarget(int watts, bool virtualGears)
        {
            if (!_ergMode)
            {
                _ergMode = true;
                _ergCurrent = double.NaN; // start immediately at the first target
            }

            _ergFromGears = virtualGears;
            _ergTarget = Math.Max(0, watts);
        }

        /// <summary>SIM mode: the trainer simulates a road with this grade (before <see cref="TrainerDifficulty"/>).</summary>
        public void SetSimulation(double gradePercent, double rollingResistance = 0.004, double windResistanceKgPerM = 0.51) =>
            SetSimulation(gradePercent, rollingResistance, windResistanceKgPerM, scaleByDifficulty: true);

        /// <summary>
        /// SIM mode; <paramref name="scaleByDifficulty"/> = false when the grade was already prepared (virtual gears are
        /// computed from the grade after <see cref="TrainerDifficulty"/>).
        /// </summary>
        public void SetSimulation(double gradePercent, double rollingResistance, double windResistanceKgPerM, bool scaleByDifficulty)
        {
            _simScale = scaleByDifficulty;
            if (_ergMode)
            {
                _ergMode = false;
                _simSentGrade = double.NaN;
            }

            _simGrade = gradePercent;
            _simCrr = rollingResistance;
            _simCw = windResistanceKgPerM;
        }

        /// <summary>
        /// Trainer commands for a list of ERG targets (offset in seconds, watts), e.g. from
        /// WorkoutEngine.TargetsAhead: what the platform sends on its own while the app is in the background.
        /// Free-ride entries (null) keep the last target. Empty if no trainer is in ERG mode and ready.
        /// </summary>
        public IReadOnlyList<ScheduledWrite> BuildErgSchedule(IEnumerable<(double OffsetS, int? TargetW)> targets)
        {
            var result = new List<ScheduledWrite>();
            string? trainer = TrainerAddress;
            if (trainer == null || !_ergMode) return result;
            if (TrainerProtocol == TrainerProtocol.Ftms && !_ftmsControlGranted) return result;
            foreach (var (offset, watts) in targets)
            {
                if (!watts.HasValue) continue;
                byte[] command = TrainerProtocol == TrainerProtocol.Ftms
                    ? FtmsControlPoint.SetTargetPower(watts.Value)
                    : TacxFec.SetTargetPower(watts.Value);
                result.Add(new ScheduledWrite((long)Math.Round(offset * 1000), trainer, CommandCharacteristic, command));
            }

            return result;
        }

        /// <summary>Sends the current target again on the next tick (after the platform controlled the trainer).</summary>
        public void ResendOnNextTick() => _resendNeeded = true;

        /// <summary>Sends pending commands. Call regularly (e.g. every 250 ms).</summary>
        public void Tick()
        {
            long now = _clockMs();
            double dt = _lastTickMs == 0 ? 0 : Math.Min(2.0, (now - _lastTickMs) / 1000.0);
            _lastTickMs = now;
            UpdateLive(now);
            TickControllers(now);

            string? trainer = TrainerAddress;
            if (trainer == null) return;

            if (TrainerProtocol == TrainerProtocol.Ftms && !_ftmsControlGranted)
            {
                if (!_ftmsControlRequested || now - _ftmsControlRequestedMs >= FtmsControlRetryMs)
                {
                    _ftmsControlRequested = Send(trainer, ControlPointChar, FtmsControlPoint.RequestControl(), "FTMS request control")
                        && Send(trainer, ControlPointChar, FtmsControlPoint.StartOrResume(), "FTMS start");
                    _ftmsControlRequestedMs = now;
                }

                return; // wait for the response
            }

            if (TrainerProtocol == TrainerProtocol.TacxFec && (_resendNeeded || _fecUserConfigRequested)
                && now - _fecUserConfigSentMs >= FecUserConfigMinIntervalMs)
            {
                SendFecUserConfig(trainer, now);
            }

            if (_ergMode) TickErg(trainer, now, dt);
            else TickSim(trainer, now);
            UpdateErgRange(now);

            _resendNeeded = false;
        }

        private void TickErg(string trainer, long now, double dt)
        {
            // Low-cadence protection against the "ERG death spiral".
            double? cadence = Live.CadenceRpm;
            if (LowCadenceProtectionEnabled && !_ergFromGears && cadence.HasValue && cadence.Value > 0 && cadence.Value < LowCadenceRpm)
            {
                if (_lowCadenceSinceMs < 0) _lowCadenceSinceMs = now;
            }
            else
            {
                _lowCadenceSinceMs = -1;
            }

            bool protect = _lowCadenceSinceMs >= 0 && now - _lowCadenceSinceMs >= LowCadenceDelayMs;
            if (protect != LowCadenceProtectionActive)
            {
                LowCadenceProtectionActive = protect;
                Log?.Invoke(protect ? "Low cadence – ERG target reduced" : "Cadence ok – ERG target restored");
            }

            double goal = protect ? _ergTarget * LowCadenceFactor : _ergTarget;

            if (double.IsNaN(_ergCurrent))
            {
                _ergCurrent = goal;
            }
            else
            {
                double maxStep = ErgRampWattsPerSecond * dt;
                _ergCurrent += Math.Max(-maxStep, Math.Min(maxStep, goal - _ergCurrent));
            }

            int rounded = (int)Math.Round(_ergCurrent);
            if (_resendNeeded || double.IsNaN(_ergSent) || rounded != (int)Math.Round(_ergSent))
            {
                byte[] command = TrainerProtocol == TrainerProtocol.Ftms
                    ? FtmsControlPoint.SetTargetPower(rounded)
                    : TacxFec.SetTargetPower(rounded);
                if (Send(trainer, CommandCharacteristic, command, null)) _ergSent = rounded;
            }
        }

        /// <summary>Over the target by this much (W, or fraction of the target if larger) counts as "too fast".</summary>
        private const int ErgOverWatts = 15;
        private const double ErgOverFraction = 0.12;
        private const long ErgOverDelayMs = 8000;
        private const long ErgTooFastHoldMs = 3000;

        private void UpdateErgRange(long now)
        {
            int target = double.IsNaN(_ergSent) ? 0 : (int)Math.Round(_ergSent);
            int? power = Live.TrainerPowerW ?? Live.PowerW;
            double? speed = Live.TrainerSpeedKmh;
            // Only at a normal cadence and a settled target (not while ramping or reduced for low cadence)
            bool pedaling = _ergMode && !_ergFromGears && target > 0 && power.HasValue && Live.CadenceRpm >= LowCadenceRpm
                && Math.Abs(_ergCurrent - _ergTarget) < 1;

            bool over = pedaling && power!.Value > target + Math.Max(ErgOverWatts, target * ErgOverFraction);
            if (over) { if (_ergOverSinceMs < 0) _ergOverSinceMs = now; }
            else _ergOverSinceMs = -1;

            bool reported = _ftmsTrainingStatus == FtmsStatusSpeedTooHigh && pedaling && power!.Value > target + 5;
            bool tooFast = reported || (_ergOverSinceMs >= 0 && now - _ergOverSinceMs >= ErgOverDelayMs);
            if (tooFast) _ergTooFastUntilMs = now + ErgTooFastHoldMs;

            bool show = _ergMode && (tooFast || now < _ergTooFastUntilMs);
            if (show && tooFast && speed > 0 && power > 0)
            {
                // Rollers and most wheel-speed trainers: power at minimum resistance grows roughly with speed.
                ErgMaxSpeedKmh = Math.Floor(speed!.Value * target / power!.Value);
            }

            if (!show) ErgMaxSpeedKmh = null;
            if (show != ErgSpeedTooHigh)
            {
                ErgSpeedTooHigh = show;
                Log?.Invoke(show ? $"ERG: too fast for {target} W – trainer at minimum resistance" : "ERG: back in control range");
            }
        }

        private void TickSim(string trainer, long now)
        {
            if (UsesTrainerShifting)
            {
                TickTrainerShifting(trainer, now);
                return;
            }

            if (_shiftWasUsed && TrainerSupportsShifting && _shiftHandshakeSent)
            {
                // Virtual gears switched off: back to the neutral ratio, so the trainer's own gear doesn't linger.
                if (Send(trainer, ZwiftController.SyncRxChar, ZwiftTrainer.Gear(1.0), "Zwift gear neutral"))
                {
                    _shiftWasUsed = false;
                    _gearSent = double.NaN;
                }
            }

            double grade = Math.Round(_simScale ? _simGrade * Math.Max(0, Math.Min(1, TrainerDifficulty)) : _simGrade, 1);
            bool changed = double.IsNaN(_simSentGrade) || Math.Abs(grade - _simSentGrade) >= 0.1;
            if (!_resendNeeded && (!changed || now - _simSentMs < SimMinIntervalMs)) return;

            byte[] command = TrainerProtocol == TrainerProtocol.Ftms
                ? FtmsControlPoint.SetIndoorBikeSimulation(grade, 0, _simCrr, _simCw)
                : TacxFec.SetTrackResistance(grade, _simCrr);
            if (Send(trainer, CommandCharacteristic, command, null))
            {
                _simSentGrade = grade;
                _simSentMs = now;
                _ergSent = double.NaN;

                // The Neo 2 asks for the user configuration again after grade changes: send it right away.
                if (TrainerProtocol == TrainerProtocol.TacxFec) SendFecUserConfig(trainer, now);
            }
        }

        /// <summary>SIM with the trainer's built-in virtual shifting: the plain road and the gear, over the Zwift service.</summary>
        private void TickTrainerShifting(string trainer, long now)
        {
            _shiftWasUsed = true;
            if (!_shiftHandshakeSent)
            {
                _shiftHandshakeSent = Send(trainer, ZwiftController.SyncRxChar, ZwiftTrainer.Handshake, "Zwift trainer handshake")
                    && Send(trainer, ZwiftController.SyncRxChar, ZwiftTrainer.Physical((uint)Math.Round(10000 * ZwiftTrainer.ReferenceRatio), 8, RiderWeightKg), null);
                if (!_shiftHandshakeSent) return;
                _resendNeeded = true;
            }

            double grade = Math.Round(_simScale ? _simGrade * Math.Max(0, Math.Min(1, TrainerDifficulty)) : _simGrade, 1);
            bool changed = double.IsNaN(_simSentGrade) || Math.Abs(grade - _simSentGrade) >= 0.1;
            if (_resendNeeded || (changed && now - _simSentMs >= SimMinIntervalMs))
            {
                if (Send(trainer, ZwiftController.SyncRxChar, ZwiftTrainer.Simulation(grade, _simCrr, _simCw), null))
                {
                    _simSentGrade = grade;
                    _simSentMs = now;
                    _ergSent = double.NaN;
                    _shiftGradeSent = true;
                }
            }

            // Gears only take effect once a grade has been sent; a shift goes out right away (no rate limit).
            double factor = Math.Round(_gearFactor!.Value, 4);
            if (_shiftGradeSent && (_resendNeeded || double.IsNaN(_gearSent) || Math.Abs(factor - _gearSent) > 1e-4))
            {
                if (Send(trainer, ZwiftController.SyncRxChar, ZwiftTrainer.Gear(factor), null)
                    && Send(trainer, ZwiftController.SyncRxChar, ZwiftTrainer.ConfirmGear, null))
                {
                    _gearSent = factor;
                }
            }
        }

        private void SendFecUserConfig(string trainer, long now)
        {
            if (Send(trainer, TacxFec.TxCharacteristic, TacxFec.SetUserConfiguration(RiderWeightKg), null))
            {
                _fecUserConfigSentMs = now;
                _fecUserConfigRequested = false;
            }
        }

        private void TickControllers(long now)
        {
            foreach (var entry in _controllers)
            {
                var c = entry.Value;
                if (c.State == ZwiftControllerState.Locked) continue; // a locked unit only answers with challenges
                // Handshake, repeated as keep-alive: the Click V2 right unit sleeps after about a minute otherwise.
                if (now - c.HandshakeMs >= ZwiftController.KeepAliveMs)
                {
                    c.HandshakeMs = now;
                    Send(entry.Key, ZwiftController.SyncRxChar, ZwiftController.StartCommand(c.ClickV2), null);
                }
            }
        }

        private void OnControllerData(string address, ControllerLink c, Guid characteristic, byte[] data, long now)
        {
            if (characteristic == ZwiftController.SyncTxChar)
            {
                var answer = ZwiftController.HandshakeAnswer(data);
                if (answer.HasValue && answer.Value != c.State)
                {
                    c.State = answer.Value;
                    Log?.Invoke(c.State == ZwiftControllerState.Locked
                        ? $"{address}: Zwift controller locked (unlock it in the Zwift app, or use the right unit)"
                        : $"{address}: Zwift controller ready");
                }

                return;
            }

            if (characteristic != ZwiftController.AsyncChar) return;
            if (ZwiftController.Battery(data) is int battery) c.Battery = battery;
            var pressed = ZwiftController.PressedButtons(data);
            if (pressed == null) return;
            if (c.State != ZwiftControllerState.Ready)
            {
                c.State = ZwiftControllerState.Ready; // button data means it works, whatever the handshake said
            }

            foreach (var button in pressed)
            {
                if (!c.Held.Contains(button)) ControllerButton?.Invoke(button);
            }

            c.Held = pressed;
        }

        private Guid CommandCharacteristic => TrainerProtocol == TrainerProtocol.Ftms ? ControlPointChar : TacxFec.TxCharacteristic;

        private bool Send(string address, Guid characteristic, byte[] data, string? label)
        {
            bool ok = _writer.Write(address, characteristic, data);
            if (label != null) Log?.Invoke(ok ? $"→ {label}" : $"✗ {label}");
            return ok;
        }

        /// <summary>Known services of a connected device (empty if unknown).</summary>
        public IReadOnlyCollection<Guid> ServicesOf(string address) =>
            _services.TryGetValue(address, out var s) ? (IReadOnlyCollection<Guid>)s : Array.Empty<Guid>();

        /// <summary>Addresses of all devices that reported their services.</summary>
        public IReadOnlyCollection<string> KnownDevices => _services.Keys.ToList();
    }
}
