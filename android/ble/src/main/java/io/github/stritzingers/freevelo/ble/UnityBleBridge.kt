package io.github.stritzingers.freevelo.ble

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
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
/** Implemented in Unity (C#): one step of the ride logic, called while the Unity player is paused. */
interface UnityBackgroundTick {
    fun onTick(elapsedMs: Long)
}

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

    private val scheduleToken = Any()
    private val tickToken = Any()

    @Volatile
    private var lastTickOkMs = 0L

    /**
     * Calls Unity's ride logic every [intervalMs] while the app is in the background, so it keeps running with
     * the same code as in the foreground. Failed calls are retried; the scheduled writes take over if no call
     * succeeds for [TICK_TIMEOUT_MS].
     */
    fun startBackgroundTicks(tick: UnityBackgroundTick, intervalMs: Long) {
        main.post {
            main.removeCallbacksAndMessages(tickToken)
            var last = SystemClock.uptimeMillis()
            val loop = object : Runnable {
                override fun run() {
                    val now = SystemClock.uptimeMillis()
                    try {
                        tick.onTick(now - last)
                        lastTickOkMs = now
                    } catch (t: Throwable) {
                        unity.onError("", "Background tick failed: ${t.message}")
                    }
                    last = now
                    main.postAtTime(this, tickToken, now + intervalMs)
                }
            }
            main.postAtTime(loop, tickToken, last + intervalMs)
        }
    }

    fun stopBackgroundTicks() {
        main.post { main.removeCallbacksAndMessages(tickToken) }
    }

    /** True while Unity's background ticks are running (then they control the trainer themselves). */
    private fun ticksHealthy(): Boolean = SystemClock.uptimeMillis() - lastTickOkMs < TICK_TIMEOUT_MS

    /**
     * Fallback for the background: sends writes at fixed times, but only while Unity's ticks are not running
     * (see [startBackgroundTicks]). [spec] is
     * "offsetMs|address|characteristic|hex" entries separated by ';'. Replaces any earlier schedule.
     * The ride foreground service holds a wake lock, so the main looper keeps running.
     */
    fun scheduleWrites(spec: String): Int {
        val entries = parseSchedule(spec)
        val start = SystemClock.uptimeMillis()
        main.post {
            main.removeCallbacksAndMessages(scheduleToken)
            for (e in entries) {
                main.postAtTime({ if (!ticksHealthy()) write(e.address, e.characteristic, e.hex) }, scheduleToken, start + e.offsetMs)
            }
        }
        return entries.size
    }

    /** Drops writes that have not been sent yet (Unity is back in control). */
    fun cancelScheduledWrites() {
        main.post { main.removeCallbacksAndMessages(scheduleToken) }
    }

    fun close() = run {
        manager.stopScan()
        manager.disconnectAll()
        byteLog?.close()
    }

    private fun run(action: () -> Unit) {
        if (Looper.myLooper() == Looper.getMainLooper()) action() else main.post { action() }
    }

    data class ScheduledEntry(val offsetMs: Long, val address: String, val characteristic: String, val hex: String)

    companion object {
        const val TICK_TIMEOUT_MS = 3000L

        /** Parses the [scheduleWrites] format; malformed entries are skipped. */
        fun parseSchedule(spec: String): List<ScheduledEntry> = spec.split(';').mapNotNull { item ->
            val parts = item.split('|')
            val offset = parts.getOrNull(0)?.toLongOrNull()
            if (parts.size != 4 || offset == null || offset < 0 || parts[3].length % 2 != 0) null
            else ScheduledEntry(offset, parts[1], parts[2], parts[3])
        }


        fun fromHex(hex: String): ByteArray = ByteArray(hex.length / 2) {
            ((Character.digit(hex[it * 2], 16) shl 4) or Character.digit(hex[it * 2 + 1], 16)).toByte()
        }
    }
}
