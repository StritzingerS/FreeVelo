package io.github.stritzingers.freevelo.ble

import java.util.UUID

/** A device found while scanning. */
data class ScannedDevice(
    val address: String,
    val name: String?,
    val rssi: Int,
    val services: Set<UUID>,
) {
    val isKnownDevice: Boolean get() = services.any { it in GattUuids.KNOWN_SERVICES }
}

enum class ConnectionState {
    DISCONNECTED,
    CONNECTING,

    /** Connected, services are being discovered and subscribed. */
    CONNECTED,

    /** Subscriptions done, the device can be used. */
    READY,

    /** Connection lost, retrying automatically. */
    RECONNECTING,
}

/**
 * Events from [BleManager]. All callbacks run on the main thread.
 * The library only moves bytes; protocol parsing happens in the caller
 * (FreeVelo.Devices in C# for the Unity app).
 */
interface BleListener {
    fun onScanResult(device: ScannedDevice) {}

    fun onConnectionStateChanged(address: String, state: ConnectionState) {}

    /** Called once per connection when all subscriptions are active. */
    fun onReady(address: String, services: Set<UUID>) {}

    /** Notification, indication or read result. */
    fun onCharacteristicData(address: String, characteristic: UUID, data: ByteArray) {}

    fun onWriteResult(address: String, characteristic: UUID, success: Boolean) {}

    fun onError(address: String?, message: String) {}
}
