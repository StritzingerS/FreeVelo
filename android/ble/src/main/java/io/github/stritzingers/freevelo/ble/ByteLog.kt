package io.github.stritzingers.freevelo.ble

import java.io.BufferedWriter
import java.io.File
import java.io.FileWriter
import java.util.UUID

/**
 * Records every GATT payload to a CSV file (format v1, read by FreeVelo.Devices.Logging.BleLogEntry):
 * `timestampMs,deviceAddress,characteristicUuid,direction(N|W|R),hexPayload`
 */
class ByteLog(val file: File) {
    private var writer: BufferedWriter? = null

    @Synchronized
    fun append(address: String, characteristic: UUID, direction: Direction, data: ByteArray) {
        val w = writer ?: open()
        w.write(formatLine(System.currentTimeMillis(), address, characteristic, direction, data))
        w.newLine()
        w.flush()
    }

    /** Adds a `# comment` line (ignored by log readers), e.g. discovered services. */
    @Synchronized
    fun comment(text: String) {
        val w = writer ?: open()
        for (line in text.lines()) {
            w.write("# $line")
            w.newLine()
        }
        w.flush()
    }

    @Synchronized
    fun close() {
        writer?.close()
        writer = null
    }

    private fun open(): BufferedWriter {
        file.parentFile?.mkdirs()
        val isNew = !file.exists() || file.length() == 0L
        val w = BufferedWriter(FileWriter(file, true))
        if (isNew) {
            w.write(HEADER)
            w.newLine()
        }
        writer = w
        return w
    }

    enum class Direction(val code: Char) {
        NOTIFY('N'),
        WRITE('W'),
        READ('R'),
    }

    companion object {
        const val HEADER = "# FreeVelo BLE log v1"

        fun formatLine(timestampMs: Long, address: String, characteristic: UUID, direction: Direction, data: ByteArray): String =
            "$timestampMs,$address,$characteristic,${direction.code},${toHex(data)}"

        fun toHex(data: ByteArray): String {
            val sb = StringBuilder(data.size * 2)
            for (b in data) {
                val v = b.toInt() and 0xFF
                sb.append(HEX[v ushr 4]).append(HEX[v and 0x0F])
            }
            return sb.toString()
        }

        private const val HEX = "0123456789abcdef"
    }
}
