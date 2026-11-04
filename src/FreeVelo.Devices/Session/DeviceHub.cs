using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Devices.Ble;
using FreeVelo.Devices.CyclingPower;
using FreeVelo.Devices.Ftms;
using FreeVelo.Devices.HeartRate;
using FreeVelo.Devices.Tacx;

namespace FreeVelo.Devices.Session
{
    /// <summary>Writes a payload to a characteristic of a connected device (implemented by the BLE layer).</summary>
    public interface IBleWriter
    {
        bool Write(string address, Guid characteristic, byte[] data);
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
        private static readonly Guid CyclingPowerChar = GattUuids.FromShort(GattUuids.CyclingPowerMeasurement);
        private static readonly Guid HeartRateChar = GattUuids.FromShort(GattUuids.HeartRateMeasurement);

        private readonly IBleWriter _writer;
        private readonly Func<long> _clockMs;
        private readonly Dictionary<string, HashSet<Guid>> _services = new Dictionary<string, HashSet<Guid>>();
        private readonly Dictionary<string, CrankCadenceCalculator> _cadence = new Dictionary<string, CrankCadenceCalculator>();

        private (long Ms, int Value)? _trainerPowerValue;
        private (long Ms, int Value)? _meterPowerValue;
        private (long Ms, double Value)? _trainerCadence;
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

        /// <summary>The ERG target currently sent to the trainer (after ramping/protection).</summary>
        public int? SentErgTargetW => double.IsNaN(_ergSent) ? (int?)null : (int)Math.Round(_ergSent);

        /// <summary>Raised for diagnostics (commands, roles, trainer responses).</summary>
        public event Action<string>? Log;

        // ------------------------------------------------------------------ connection events

        /// <summary>A device finished connecting; <paramref name="services"/> are its GATT services.</summary>
        public void OnReady(string address, IEnumerable<Guid> services)
        {
            var set = new HashSet<Guid>(services);
            _services[address] = set;

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
            }
        }

        /// <summary>A device disconnected (it may reconnect automatically and call <see cref="OnReady"/> again).</summary>
        public void OnDisconnected(string address)
        {
            _cadence.Remove(address);
            if (address == TrainerAddress)
            {
                _ftmsControlGranted = false;
                _ftmsControlRequested = false;
            }
        }

        /// <summary>The user removed a device: forget its role.</summary>
        public void Forget(string address)
        {
            OnDisconnected(address);
            _services.Remove(address);
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

            if (characteristic == IndoorBikeDataChar && IndoorBikeData.TryParse(data, out var ibd) && ibd != null)
            {
                if (ibd.PowerW.HasValue) _trainerPowerValue = (now, ibd.PowerW.Value);
                if (ibd.CadenceRpm.HasValue) _trainerCadence = (now, ibd.CadenceRpm.Value);
                if (ibd.SpeedKmh.HasValue) _trainerSpeed = (now, ibd.SpeedKmh.Value);
                if (ibd.HeartRateBpm.HasValue && ibd.HeartRateBpm > 0) _trainerHeartRate = (now, ibd.HeartRateBpm.Value);
            }
            else if (characteristic == TacxFec.RxCharacteristic && FecData.TryParse(data, out var fec) && fec != null)
            {
                if (fec.PowerW.HasValue) _trainerPowerValue = (now, fec.PowerW.Value);
                if (fec.CadenceRpm.HasValue) _trainerCadence = (now, fec.CadenceRpm.Value);
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
        public void SetErgTarget(int watts)
        {
            if (!_ergMode)
            {
                _ergMode = true;
                _ergCurrent = double.NaN; // start immediately at the first target
            }

            _ergTarget = Math.Max(0, watts);
        }

        /// <summary>SIM mode: the trainer simulates a road with this grade (before <see cref="TrainerDifficulty"/>).</summary>
        public void SetSimulation(double gradePercent, double rollingResistance = 0.004, double windResistanceKgPerM = 0.51)
        {
            if (_ergMode)
            {
                _ergMode = false;
                _simSentGrade = double.NaN;
            }

            _simGrade = gradePercent;
            _simCrr = rollingResistance;
            _simCw = windResistanceKgPerM;
        }

        /// <summary>Sends pending commands. Call regularly (e.g. every 250 ms).</summary>
        public void Tick()
        {
            long now = _clockMs();
            double dt = _lastTickMs == 0 ? 0 : Math.Min(2.0, (now - _lastTickMs) / 1000.0);
            _lastTickMs = now;
            UpdateLive(now);

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

            _resendNeeded = false;
        }

        private void TickErg(string trainer, long now, double dt)
        {
            // Low-cadence protection against the "ERG death spiral".
            double? cadence = Live.CadenceRpm;
            if (LowCadenceProtectionEnabled && cadence.HasValue && cadence.Value > 0 && cadence.Value < LowCadenceRpm)
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

        private void TickSim(string trainer, long now)
        {
            double grade = Math.Round(_simGrade * Math.Max(0, Math.Min(1, TrainerDifficulty)), 1);
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

        private void SendFecUserConfig(string trainer, long now)
        {
            if (Send(trainer, TacxFec.TxCharacteristic, TacxFec.SetUserConfiguration(RiderWeightKg), null))
            {
                _fecUserConfigSentMs = now;
                _fecUserConfigRequested = false;
            }
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
