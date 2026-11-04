using System;

namespace FreeVelo.Ble
{
    /// <summary>Callbacks from a BLE backend. May be called from any thread.</summary>
    public interface IBleCallbacks
    {
        void OnScanResult(string address, string name, int rssi, string servicesCsv);

        /// <summary>DISCONNECTED, CONNECTING, CONNECTED, READY or RECONNECTING.</summary>
        void OnConnectionState(string address, string state);

        void OnReady(string address, string servicesCsv);

        void OnData(string address, string characteristic, string hex);

        void OnError(string address, string message);
    }

    /// <summary>Platform BLE implementation: Android plugin on the phone, simulator in the editor.</summary>
    public interface IBleBackend : IDisposable
    {
        string Name { get; }

        /// <summary>Path of the byte log of this session, if any.</summary>
        string LogFilePath { get; }

        void Initialize(IBleCallbacks callbacks);

        void StartScan();

        void StopScan();

        void Connect(string address);

        void Disconnect(string address);

        void Write(string address, Guid characteristic, byte[] data);

        /// <summary>Called every frame on the Unity thread.</summary>
        void Update(float deltaTime);
    }
}
