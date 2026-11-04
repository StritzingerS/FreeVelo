package io.github.stritzingers.freevelo.ble

import android.content.Context
import android.os.Handler
import android.os.Looper
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.UUID

/**
 * String-only callbacks for Unity (implemented in C# with AndroidJavaProxy).
 * Only strings, ints and booleans are used so that no array marshalling is needed.
 * Callbacks arrive on the Android main thread, not on the Unity thread.
 */
interface UnityBleListener {
    /** [servicesCsv]: comma separated service UUIDs. */
    fun onScanResult(address: String, name: String, rssi: Int, servicesCsv: String)

    /** [state]: name of [ConnectionState]. */
    fun onConnectionState(address: String, state: String)

    fun onReady(address: String, servicesCsv: String)

    /** Notification, indication or read value as lowercase hex. */
    fun onData(address: String, characteristic: String, hex: String)

    fun onWriteResult(address: String, characteristic: String, success: Boolean)

    fun onError(address: String, message: String)
}

/**
 * Facade over [BleManager] for Unity. Unity calls arrive on the Unity thread and are
 * forwarded to the Android main thread, where all BLE state lives.
 */
class UnityBleBridge(context: Context, private val unity: UnityBleListener, logDirectory: String?) {
    private val main = Handler(Looper.getMainLooper())

    private val byteLog: ByteLog? = logDirectory?.let {
        val stamp = SimpleDateFormat("yyyyMMdd-HHmmss", Locale.ROOT).format(Date())
        ByteLog(File(it, "ble-$stamp.csv"))
    }

    private val manager = BleManager(context, object : BleListener {
        override fun onScanResult(device: ScannedDevice) =
            unity.onScanResult(device.address, device.name ?: "", device.rssi, device.services.joinToString(","))

        override fun onConnectionStateChanged(address: String, state: ConnectionState) =
            unity.onConnectionState(address, state.name)

        override fun onReady(address: String, services: Set<UUID>) =
            unity.onReady(address, services.joinToString(","))

        override fun onCharacteristicData(address: String, characteristic: UUID, data: ByteArray) =
            unity.onData(address, characteristic.toString(), ByteLog.toHex(data))

        override fun onWriteResult(address: String, characteristic: UUID, success: Boolean) =
            unity.onWriteResult(address, characteristic.toString(), success)

        override fun onError(address: String?, message: String) =
            unity.onError(address ?: "", message)
    }, byteLog)

    /** Path of the byte log of this session ("" if logging is off). */
    val logFilePath: String get() = byteLog?.file?.absolutePath ?: ""

    val isBluetoothEnabled: Boolean get() = manager.isBluetoothEnabled

    fun startScan() = run { manager.startScan() }

    fun stopScan() = run { manager.stopScan() }

    fun connect(address: String) = run { manager.connect(address) }

    fun disconnect(address: String) = run { manager.disconnect(address) }

    fun disconnectAll() = run { manager.disconnectAll() }

    /** Queues a write; failures are reported via [UnityBleListener.onWriteResult] / onError. */
    fun write(address: String, characteristic: String, hex: String): Boolean {
        val uuid = UUID.fromString(characteristic)
        val data = fromHex(hex)
        run {
            if (!manager.write(address, uuid, data)) unity.onWriteResult(address, characteristic, false)
        }
        return true
    }

    fun close() = run {
        manager.stopScan()
        manager.disconnectAll()
        byteLog?.close()
    }

    private fun run(action: () -> Unit) {
        if (Looper.myLooper() == Looper.getMainLooper()) action() else main.post { action() }
    }

    companion object {
        fun fromHex(hex: String): ByteArray = ByteArray(hex.length / 2) {
            ((Character.digit(hex[it * 2], 16) shl 4) or Character.digit(hex[it * 2 + 1], 16)).toByte()
        }
    }
}
