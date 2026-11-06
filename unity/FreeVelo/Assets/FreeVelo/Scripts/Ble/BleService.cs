using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FreeVelo.App;
using FreeVelo.Devices.Ble;
using FreeVelo.Devices.Logging;
using FreeVelo.Devices.Session;
using FreeVelo.Devices.Tacx;
using UnityEngine;

namespace FreeVelo.Ble
{
    /// <summary>A device seen while scanning or connected.</summary>
    public sealed class DeviceInfo
    {
        public string Address;
        public string Name;
        public int Rssi;
        public HashSet<Guid> Services = new HashSet<Guid>();

        /// <summary>Connection state, null if not connected.</summary>
        public string State;

        public bool IsConnected => State != null && State != "DISCONNECTED";

        public string Tags
        {
            get
            {
                var tags = new List<string>();
                if (Services.Contains(GattUuids.FromShort(GattUuids.FitnessMachineService))) tags.Add("FTMS");
                if (Services.Contains(TacxFec.Service)) tags.Add("FE-C");
                if (Services.Contains(GattUuids.FromShort(GattUuids.CyclingPowerService))) tags.Add(L.Get("power"));
                if (Services.Contains(GattUuids.FromShort(GattUuids.HeartRateService))) tags.Add("HR");
                return string.Join(" · ", tags);
            }
        }
    }

    /// <summary>
    /// Owns the BLE backend and the <see cref="DeviceHub"/>. Backend callbacks are queued and handled on the Unity thread.
    /// </summary>
    public sealed class BleService : MonoBehaviour, IBleCallbacks, IBleWriter
    {
        private const float TickInterval = 0.25f;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private readonly Dictionary<string, DeviceInfo> _devices = new Dictionary<string, DeviceInfo>();
        private readonly List<string> _log = new List<string>();
        private IBleBackend _backend;
        private float _tickTimer;
        private float _scanUntil;

        public DeviceHub Hub { get; private set; }

        public IEnumerable<DeviceInfo> Devices =>
            _devices.Values.OrderByDescending(d => d.IsConnected).ThenByDescending(d => d.Services.Count).ThenByDescending(d => d.Rssi);

        public bool IsScanning => Time.time < _scanUntil;

        public IReadOnlyList<string> Log => _log;

        public string BackendName => _backend?.Name ?? "-";

        public string LogFilePath => _backend?.LogFilePath ?? string.Empty;

        private void Awake()
        {
            Hub = new DeviceHub(this, () => 1000 + Clock.ElapsedMilliseconds);
            Hub.Log += AddLog;
            ApplySettings();

#if UNITY_ANDROID && !UNITY_EDITOR
            _backend = new AndroidBleBackend();
#else
            _backend = new SimulatedBleBackend();
#endif
            try
            {
                _backend.Initialize(this);
            }
            catch (Exception e)
            {
                AddLog("BLE init failed: " + e.Message);
                UnityEngine.Debug.LogException(e);
            }

            // Reconnect remembered devices.
            if (AppSettings.RememberedDevices.Count > 0) StartScan();
        }

        /// <summary>Applies settings that the hub uses (weight, difficulty, power source).</summary>
        public void ApplySettings()
        {
            Hub.RiderWeightKg = AppSettings.RiderWeightKg;
            Hub.TrainerDifficulty = AppSettings.TrainerDifficulty;
            Hub.PowerSource = AppSettings.PowerSource;
        }

        /// <summary>
        /// Guards the device hub and the ride: normally used on the Unity thread, while the app is in the
        /// background by the Android plugin's ticks (Unity's frame loop is paused then).
        /// </summary>
        public static readonly object Sync = new object();

        private void Update()
        {
            lock (Sync)
            {
                ProcessQueue();
                UpdateOnMainThread();
            }
        }

        private void ProcessQueue()
        {
            while (_queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }
        }

        /// <summary>One background step (caller holds <see cref="Sync"/>): Bluetooth events and trainer commands.</summary>
        public void BackgroundTick()
        {
            ProcessQueue();
            Hub.Tick();
        }

        /// <summary>Lets the platform call <paramref name="tick"/> regularly while Unity is paused.</summary>
        public void StartBackgroundTicks(Action<float> tick, int intervalMs) => _backend?.StartBackgroundTicks(tick, intervalMs);

        public void StopBackgroundTicks() => _backend?.StopBackgroundTicks();

        private void UpdateOnMainThread()
        {
            _backend?.Update(Time.unscaledDeltaTime);

            if (_scanUntil > 0 && Time.time >= _scanUntil)
            {
                _scanUntil = 0;
                _backend?.StopScan();
            }

            _tickTimer += Time.unscaledDeltaTime;
            if (_tickTimer >= TickInterval)
            {
                _tickTimer = 0;
                Hub.Tick();
            }
        }

        private void OnDestroy() => _backend?.Dispose();

        // ------------------------------------------------------------------ user actions

        /// <summary>Lets the platform send these writes on its own (app in the background).</summary>
        public void ScheduleBackgroundWrites(IReadOnlyList<ScheduledWrite> writes)
        {
            _backend?.ScheduleWrites(writes);
            if (writes.Count > 0) AddLog($"Background: {writes.Count} trainer commands scheduled");
        }

        public void CancelBackgroundWrites() => _backend?.CancelScheduledWrites();

        public void StartScan(float seconds = 20f)
        {
            _scanUntil = Time.time + seconds;
            _backend?.StartScan();
        }

        public void StopScan()
        {
            _scanUntil = 0;
            _backend?.StopScan();
        }

        public void Connect(string address)
        {
            AppSettings.RememberDevice(address, true);
            if (_devices.TryGetValue(address, out var d)) d.State = "CONNECTING";
            _backend?.Connect(address);
        }

        public void Disconnect(string address)
        {
            AppSettings.RememberDevice(address, false);
            _backend?.Disconnect(address);
            Hub.Forget(address);
            if (_devices.TryGetValue(address, out var d)) d.State = null;
        }

        public string NameOf(string address) =>
            address != null && _devices.TryGetValue(address, out var d) && !string.IsNullOrEmpty(d.Name) ? d.Name : address ?? L.Get("none");

        public void AddLog(string message)
        {
            _log.Insert(0, DateTime.Now.ToString("HH:mm:ss ") + message);
            if (_log.Count > 40) _log.RemoveAt(_log.Count - 1);
        }

        // ------------------------------------------------------------------ IBleWriter (DeviceHub → backend)

        bool IBleWriter.Write(string address, Guid characteristic, byte[] data)
        {
            if (_backend == null) return false;
            _backend.Write(address, characteristic, data);
            return true;
        }

        // ------------------------------------------------------------------ IBleCallbacks (any thread → queue)

        void IBleCallbacks.OnScanResult(string address, string name, int rssi, string servicesCsv) => _queue.Enqueue(() =>
        {
            var d = GetOrAdd(address);
            if (!string.IsNullOrEmpty(name)) d.Name = name;
            d.Rssi = rssi;
            foreach (var s in ParseGuids(servicesCsv)) d.Services.Add(s);
            if (!d.IsConnected && AppSettings.RememberedDevices.Contains(address))
            {
                d.State = "CONNECTING";
                _backend.Connect(address);
            }
        });

        void IBleCallbacks.OnConnectionState(string address, string state) => _queue.Enqueue(() =>
        {
            var d = GetOrAdd(address);
            d.State = state == "DISCONNECTED" ? null : state;
            if (state == "DISCONNECTED" || state == "RECONNECTING") Hub.OnDisconnected(address);
            AddLog($"{NameOf(address)}: {state}");
        });

        void IBleCallbacks.OnReady(string address, string servicesCsv) => _queue.Enqueue(() =>
        {
            var d = GetOrAdd(address);
            var services = ParseGuids(servicesCsv).ToList();
            foreach (var s in services) d.Services.Add(s);
            Hub.OnReady(address, services);
        });

        void IBleCallbacks.OnData(string address, string characteristic, string hex) => _queue.Enqueue(() =>
        {
            if (Guid.TryParse(characteristic, out var uuid)) Hub.OnData(address, uuid, BleLogEntry.FromHex(hex));
        });

        void IBleCallbacks.OnError(string address, string message) => _queue.Enqueue(() =>
            AddLog($"⚠ {(string.IsNullOrEmpty(address) ? string.Empty : NameOf(address) + ": ")}{message}"));

        private DeviceInfo GetOrAdd(string address)
        {
            if (!_devices.TryGetValue(address, out var d))
            {
                d = new DeviceInfo { Address = address, Name = string.Empty };
                _devices[address] = d;
            }

            return d;
        }

        private static IEnumerable<Guid> ParseGuids(string csv)
        {
            foreach (var part in (csv ?? string.Empty).Split(','))
            {
                if (Guid.TryParse(part.Trim(), out var g)) yield return g;
            }
        }
    }
}
