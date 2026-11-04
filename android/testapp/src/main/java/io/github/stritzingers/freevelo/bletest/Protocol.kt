package io.github.stritzingers.freevelo.bletest

import kotlin.math.roundToInt

/**
 * Minimal protocol helpers for the BLE test app's live display.
 * The real implementation (used by the Unity app) is FreeVelo.Devices in C#;
 * both are tested with the same byte vectors.
 */
object Protocol {
    data class BikeData(val speedKmh: Double?, val cadenceRpm: Double?, val powerW: Int?, val heartRate: Int?)

    data class PowerData(val powerW: Int, val crankRevolutions: Int?, val crankEventTime: Int?)

    private fun u8(b: ByteArray, i: Int): Int = b[i].toInt() and 0xFF
    private fun u16(b: ByteArray, i: Int): Int = u8(b, i) or (u8(b, i + 1) shl 8)
    private fun s16(b: ByteArray, i: Int): Int = u16(b, i).toShort().toInt()
    private fun bit(flags: Int, n: Int): Boolean = (flags and (1 shl n)) != 0

    /** FTMS Indoor Bike Data (0x2AD2). Returns null for malformed payloads. */
    fun parseIndoorBikeData(b: ByteArray): BikeData? = try {
        val flags = u16(b, 0)
        var i = 2
        var speed: Double? = null
        var cadence: Double? = null
        var power: Int? = null
        var hr: Int? = null
        if (!bit(flags, 0)) { speed = u16(b, i) / 100.0; i += 2 }
        if (bit(flags, 1)) i += 2
        if (bit(flags, 2)) { cadence = u16(b, i) / 2.0; i += 2 }
        if (bit(flags, 3)) i += 2
        if (bit(flags, 4)) i += 3
        if (bit(flags, 5)) i += 2
        if (bit(flags, 6)) { power = s16(b, i); i += 2 }
        if (bit(flags, 7)) i += 2
        if (bit(flags, 8)) i += 5
        if (bit(flags, 9)) { hr = u8(b, i); i += 1 }
        BikeData(speed, cadence, power, hr)
    } catch (e: IndexOutOfBoundsException) {
        null
    }

    /** Cycling Power Measurement (0x2A63). */
    fun parseCyclingPower(b: ByteArray): PowerData? = try {
        val flags = u16(b, 0)
        val power = s16(b, 2)
        var i = 4
        if (bit(flags, 0)) i += 1
        if (bit(flags, 2)) i += 2
        if (bit(flags, 4)) i += 6
        if (bit(flags, 5)) {
            PowerData(power, u16(b, i), u16(b, i + 2))
        } else {
            PowerData(power, null, null)
        }
    } catch (e: IndexOutOfBoundsException) {
        null
    }

    /** Heart Rate Measurement (0x2A37). */
    fun parseHeartRate(b: ByteArray): Int? = try {
        if (bit(u8(b, 0), 0)) u16(b, 1) else u8(b, 1)
    } catch (e: IndexOutOfBoundsException) {
        null
    }

    /** FTMS control point response: "SetTargetPower: Success" etc., or null if not a response. */
    fun describeControlPointResponse(b: ByteArray): String? {
        if (b.size < 3 || u8(b, 0) != 0x80) return null
        val op = when (u8(b, 1)) {
            0x00 -> "RequestControl"
            0x01 -> "Reset"
            0x05 -> "SetTargetPower"
            0x07 -> "Start"
            0x08 -> "Stop/Pause"
            0x11 -> "SetSimulation"
            0x13 -> "SpinDown"
            else -> String.format("Op 0x%02X", u8(b, 1))
        }
        val result = when (u8(b, 2)) {
            0x01 -> "Success"
            0x02 -> "Not supported"
            0x03 -> "Invalid parameter"
            0x04 -> "Operation failed"
            0x05 -> "Control not permitted"
            else -> String.format("Result 0x%02X", u8(b, 2))
        }
        return "$op: $result"
    }

    fun isSuccessResponseTo(b: ByteArray, opCode: Int): Boolean =
        b.size >= 3 && u8(b, 0) == 0x80 && u8(b, 1) == opCode && u8(b, 2) == 0x01

    /** FTMS Fitness Machine Feature (0x2ACC) as readable text. */
    fun describeFeatures(b: ByteArray): String? {
        if (b.size < 8) return null
        val machine = u16(b, 0) or (u16(b, 2) shl 16)
        val target = u16(b, 4) or (u16(b, 6) shl 16)
        return "cadence=${bit(machine, 1)} power=${bit(machine, 14)} erg=${bit(target, 3)} sim=${bit(target, 13)}"
    }

    /** FTMS Supported Power Range (0x2AD8). */
    fun describePowerRange(b: ByteArray): String? =
        if (b.size < 6) null else "${s16(b, 0)}–${s16(b, 2)} W, step ${u16(b, 4)} W"

    // --- FTMS control point commands ---

    const val OP_REQUEST_CONTROL = 0x00

    fun requestControl(): ByteArray = byteArrayOf(0x00)

    fun start(): ByteArray = byteArrayOf(0x07)

    fun stop(): ByteArray = byteArrayOf(0x08, 0x01)

    fun setTargetPower(watts: Int): ByteArray {
        val w = watts.coerceIn(0, Short.MAX_VALUE.toInt())
        return byteArrayOf(0x05, (w and 0xFF).toByte(), ((w shr 8) and 0xFF).toByte())
    }

    fun setSimulation(gradePercent: Double, crr: Double = 0.004, cw: Double = 0.51): ByteArray {
        val grade = (gradePercent * 100).roundToInt().coerceIn(Short.MIN_VALUE.toInt(), Short.MAX_VALUE.toInt())
        val crrRaw = (crr * 10000).roundToInt().coerceIn(0, 255)
        val cwRaw = (cw * 100).roundToInt().coerceIn(0, 255)
        return byteArrayOf(
            0x11,
            0x00, 0x00, // wind speed 0
            (grade and 0xFF).toByte(), ((grade shr 8) and 0xFF).toByte(),
            crrRaw.toByte(),
            cwRaw.toByte(),
        )
    }
}

/** Cadence from cumulative crank revolutions (same algorithm as C# CrankCadenceCalculator). */
class CrankCadence(private val stopTimeoutMs: Long = 3000) {
    private var lastRevs: Int? = null
    private var lastTime = 0
    private var lastChangeMs = 0L

    var cadenceRpm: Double? = null
        private set

    fun update(revolutions: Int, eventTime: Int, nowMs: Long): Double? {
        val prev = lastRevs
        if (prev == null) {
            remember(revolutions, eventTime, nowMs)
            return cadenceRpm
        }
        val dRevs = (revolutions - prev) and 0xFFFF
        val dTicks = (eventTime - lastTime) and 0xFFFF
        if (dRevs == 0 || dTicks == 0) {
            if (nowMs - lastChangeMs >= stopTimeoutMs) cadenceRpm = 0.0
            return cadenceRpm
        }
        if (dRevs <= 20) cadenceRpm = dRevs * 60.0 * 1024.0 / dTicks
        remember(revolutions, eventTime, nowMs)
        return cadenceRpm
    }

    private fun remember(revolutions: Int, eventTime: Int, nowMs: Long) {
        lastRevs = revolutions
        lastTime = eventTime
        lastChangeMs = nowMs
    }
}
