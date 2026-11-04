package io.github.stritzingers.freevelo.ble

import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.util.UUID

class ByteLogTest {
    @get:Rule
    val tmp = TemporaryFolder()

    @Test
    fun formatsLineInLogFormatV1() {
        val line = ByteLog.formatLine(
            42,
            "AA:BB:CC:DD:EE:FF",
            GattUuids.HEART_RATE_MEASUREMENT,
            ByteLog.Direction.READ,
            byteArrayOf(0x00, 0x48),
        )
        // Must stay parseable by FreeVelo.Devices.Logging.BleLogEntry (C#).
        assertEquals("42,AA:BB:CC:DD:EE:FF,00002a37-0000-1000-8000-00805f9b34fb,R,0048", line)
    }

    @Test
    fun hexIsLowercaseAndUnsigned() {
        assertEquals("00ff7f80", ByteLog.toHex(byteArrayOf(0x00, 0xFF.toByte(), 0x7F, 0x80.toByte())))
    }

    @Test
    fun writesHeaderOnceAndAppends() {
        val file = File(tmp.root, "logs/test.csv")
        val log = ByteLog(file)
        log.append("A", GattUuids.INDOOR_BIKE_DATA, ByteLog.Direction.NOTIFY, byteArrayOf(0x44))
        log.close()
        ByteLog(file).apply {
            append("A", GattUuids.FITNESS_MACHINE_CONTROL_POINT, ByteLog.Direction.WRITE, byteArrayOf(0x00))
            close()
        }

        val lines = file.readLines()
        assertEquals(3, lines.size)
        assertEquals(ByteLog.HEADER, lines[0])
        assertEquals("N", lines[1].split(',')[3])
        assertEquals("W", lines[2].split(',')[3])
    }

    @Test
    fun uuidHelpers() {
        assertEquals(UUID.fromString("00001826-0000-1000-8000-00805f9b34fb"), GattUuids.FITNESS_MACHINE_SERVICE)
        assertEquals("2AD9", GattUuids.shortName(GattUuids.FITNESS_MACHINE_CONTROL_POINT))
        val custom = UUID.fromString("6e40fec1-b5a3-f393-e0a9-e50e24dcca9e")
        assertEquals(custom.toString(), GattUuids.shortName(custom))
    }

    @Test
    fun hexRoundTrip() {
        val data = byteArrayOf(0x00, 0x7F, 0x80.toByte(), 0xFF.toByte(), 0x4E)
        assertEquals("007f80ff4e", ByteLog.toHex(UnityBleBridge.fromHex("007F80ff4e")))
        assertEquals(ByteLog.toHex(data), ByteLog.toHex(UnityBleBridge.fromHex(ByteLog.toHex(data))))
    }
}
