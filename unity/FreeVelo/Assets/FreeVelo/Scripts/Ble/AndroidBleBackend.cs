#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using FreeVelo.Devices.Session;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeVelo.Devices.Logging;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Scripting;

namespace FreeVelo.Ble
{
    /// <summary>Uses the Kotlin BLE library (Assets/Plugins/Android/freevelo-ble.aar) via its UnityBleBridge.</summary>
    public sealed class AndroidBleBackend : IBleBackend
    {
        private const string BridgeClass = "io.github.stritzingers.freevelo.ble.UnityBleBridge";
        private const string ListenerInterface = "io.github.stritzingers.freevelo.ble.UnityBleListener";

        private AndroidJavaObject _bridge;
        private Listener _listener;
        private IBleCallbacks _callbacks;
        private bool _scanWhenGranted;

        public string Name => "Android";

        public string LogFilePath => _bridge != null ? _bridge.Call<string>("getLogFilePath") : string.Empty;

        public void Initialize(IBleCallbacks callbacks)
        {
            _callbacks = callbacks;
            _listener = new Listener(callbacks);
            string logDir = Path.Combine(Application.persistentDataPath, "logs");
            Directory.CreateDirectory(logDir);
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                _bridge = new AndroidJavaObject(BridgeClass, activity, _listener, logDir);
            }

            RequestPermissions();
        }

        public void StartScan()
        {
            if (!HasPermissions())
            {
                _scanWhenGranted = true;
                RequestPermissions();
                return;
            }

            _bridge.Call("startScan");
        }

        public void StopScan() => _bridge?.Call("stopScan");

        public void Connect(string address)
        {
            if (HasPermissions()) _bridge.Call("connect", address);
        }

        public void Disconnect(string address) => _bridge?.Call("disconnect", address);

        public void Write(string address, Guid characteristic, byte[] data) =>
            _bridge?.Call<bool>("write", address, characteristic.ToString(), BleLogEntry.ToHex(data));

        public void ScheduleWrites(IReadOnlyList<ScheduledWrite> writes)
        {
            if (_bridge == null || writes.Count == 0) return;
            string spec = string.Join(";", writes.Select(w => $"{w.OffsetMs}|{w.Address}|{w.Characteristic}|{BleLogEntry.ToHex(w.Data)}"));
            _bridge.Call<int>("scheduleWrites", spec);
        }

        public void CancelScheduledWrites() => _bridge?.Call("cancelScheduledWrites");

        private TickProxy _tickProxy;

        public void StartBackgroundTicks(Action<float> tick, int intervalMs)
        {
            if (_bridge == null) return;
            _tickProxy = new TickProxy(tick);
            _bridge.Call("startBackgroundTicks", _tickProxy, (long)intervalMs);
        }

        public void StopBackgroundTicks()
        {
            _bridge?.Call("stopBackgroundTicks");
            _tickProxy = null;
        }

        /// <summary>Implements the Kotlin UnityBackgroundTick interface (called on the Android main thread).</summary>
        [Preserve]
        private sealed class TickProxy : AndroidJavaProxy
        {
            private readonly Action<float> _tick;

            public TickProxy(Action<float> tick)
                : base("io.github.stritzingers.freevelo.ble.UnityBackgroundTick")
            {
                _tick = tick;
            }

            [Preserve]
            public void onTick(long elapsedMs) => _tick(elapsedMs / 1000f);
        }

        public void Update(float deltaTime)
        {
        }

        public void Dispose()
        {
            _bridge?.Call("close");
            _bridge?.Dispose();
            _bridge = null;
        }

        private static string[] RequiredPermissions()
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                return version.GetStatic<int>("SDK_INT") >= 31
                    ? new[] { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" }
                    : new[] { Permission.FineLocation };
            }
        }

        private static bool HasPermissions() => RequiredPermissions().All(Permission.HasUserAuthorizedPermission);

        private void RequestPermissions()
        {
            if (HasPermissions()) return;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
            {
                if (HasPermissions() && _scanWhenGranted)
                {
                    _scanWhenGranted = false;
                    _bridge.Call("startScan");
                }
            };
            callbacks.PermissionDenied += p => _callbacks.OnError(string.Empty, "Permission denied: " + p);
            Permission.RequestUserPermissions(RequiredPermissions(), callbacks);
        }

        /// <summary>Implements the Kotlin UnityBleListener interface (called on the Android main thread).</summary>
        [Preserve]
        private sealed class Listener : AndroidJavaProxy
        {
            private readonly IBleCallbacks _cb;

            public Listener(IBleCallbacks cb)
                : base(ListenerInterface)
            {
                _cb = cb;
            }

            [Preserve]
            public void onScanResult(string address, string name, int rssi, string servicesCsv) =>
                _cb.OnScanResult(address, name, rssi, servicesCsv);

            [Preserve]
            public void onConnectionState(string address, string state) => _cb.OnConnectionState(address, state);

            [Preserve]
            public void onReady(string address, string servicesCsv) => _cb.OnReady(address, servicesCsv);

            [Preserve]
            public void onData(string address, string characteristic, string hex) => _cb.OnData(address, characteristic, hex);

            [Preserve]
            public void onWriteResult(string address, string characteristic, bool success)
            {
                if (!success) _cb.OnError(address, "Write failed: " + characteristic);
            }

            [Preserve]
            public void onError(string address, string message) => _cb.OnError(address, message);
        }
    }
}
#endif
