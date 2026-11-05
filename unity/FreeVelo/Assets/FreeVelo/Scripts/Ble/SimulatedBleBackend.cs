using System;
using System.Collections.Generic;
using FreeVelo.Devices.Ble;
using FreeVelo.Devices.Logging;

namespace FreeVelo.Ble
{
    /// <summary>
    /// Simulated FTMS trainer and heart rate strap for testing in the Unity editor (no Bluetooth needed).
    /// The trainer follows ERG targets; in SIM mode it reports <see cref="RiderPowerW"/>.
    /// </summary>
    public sealed class SimulatedBleBackend : IBleBackend
    {
        public const string TrainerAddress = "SIM:TRAINER";
        public const string HeartRateAddress = "SIM:HRM";

        private static readonly Guid ControlPoint = GattUuids.FromShort(GattUuids.FitnessMachineControlPoint);
        private static readonly string TrainerServices = string.Join(",", GattUuids.FromShort(GattUuids.FitnessMachineService), GattUuids.FromShort(GattUuids.CyclingPowerService));
        private static readonly string HrmServices = GattUuids.FromShort(GattUuids.HeartRateService).ToString();

        private readonly HashSet<string> _connected = new HashSet<string>();
        private readonly Random _random = new Random(1);
        private IBleCallbacks _cb;
        private float _timer;
        private bool _erg;
        private int _ergTarget;
        private double _power;
        private double _heartRate = 70;

        /// <summary>What the simulated rider pedals in SIM mode (0 = stops pedaling).</summary>
        public static float RiderPowerW { get; set; } = 180f;

        public string Name => "Simulator";

        public string LogFilePath => string.Empty;

        public void Initialize(IBleCallbacks callbacks) => _cb = callbacks;

        public void StartScan()
        {
            _cb.OnScanResult(TrainerAddress, "FreeVelo Sim Trainer", -45, TrainerServices);
            _cb.OnScanResult(HeartRateAddress, "FreeVelo Sim HRM", -60, HrmServices);
        }

        public void StopScan()
        {
        }

        public void Connect(string address)
        {
            if (address != TrainerAddress && address != HeartRateAddress) return;
            _connected.Add(address);
            _cb.OnConnectionState(address, "CONNECTED");
            _cb.OnConnectionState(address, "READY");
            _cb.OnReady(address, address == TrainerAddress ? TrainerServices : HrmServices);
        }

        public void Disconnect(string address)
        {
            if (_connected.Remove(address)) _cb.OnConnectionState(address, "DISCONNECTED");
        }

        public void Write(string address, Guid characteristic, byte[] data)
        {
            if (address != TrainerAddress || characteristic != ControlPoint || data.Length == 0) return;

            switch (data[0])
            {
                case 0x05:
                    _erg = true;
                    _ergTarget = data[1] | (data[2] << 8);
                    break;
                case 0x11:
                    _erg = false;
                    break;
            }

            // Every command succeeds.
            Notify(TrainerAddress, ControlPoint, new byte[] { 0x80, data[0], 0x01 });
        }

        // The editor never runs in the background: nothing to schedule.
        public void ScheduleWrites(System.Collections.Generic.IReadOnlyList<FreeVelo.Devices.Session.ScheduledWrite> writes)
        {
        }

        public void CancelScheduledWrites()
        {
        }

        public void Update(float deltaTime)
        {
            _timer += deltaTime;
            if (_timer < 0.25f) return;
            _timer -= 0.25f;

            bool pedaling = RiderPowerW > 0;
            double goal = !pedaling ? 0 : _erg ? _ergTarget : RiderPowerW;
            _power += ((goal - _power) * 0.35) + (pedaling ? (_random.NextDouble() - 0.5) * 8 : 0);
            _power = Math.Max(0, _power);
            int cadence = pedaling ? 86 + _random.Next(-3, 4) : 0;

            if (_connected.Contains(TrainerAddress))
            {
                int p = (int)Math.Round(_power);
                int c = cadence * 2;
                const int speed = 3000; // 30.00 km/h (not used for the virtual speed)
                Notify(TrainerAddress, GattUuids.FromShort(GattUuids.IndoorBikeData), new byte[]
                {
                    0x44, 0x00, speed & 0xFF, speed >> 8, (byte)(c & 0xFF), (byte)(c >> 8), (byte)(p & 0xFF), (byte)(p >> 8),
                });
            }

            if (_connected.Contains(HeartRateAddress))
            {
                _heartRate += ((70 + (_power * 0.4)) - _heartRate) * 0.02;
                Notify(HeartRateAddress, GattUuids.FromShort(GattUuids.HeartRateMeasurement), new byte[] { 0x00, (byte)Math.Round(_heartRate) });
            }
        }

        public void Dispose()
        {
        }

        private void Notify(string address, Guid characteristic, byte[] data) =>
            _cb.OnData(address, characteristic.ToString(), BleLogEntry.ToHex(data));
    }
}
