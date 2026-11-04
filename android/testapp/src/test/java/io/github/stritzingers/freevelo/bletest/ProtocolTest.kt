package io.github.stritzingers.freevelo.bletest

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Same byte vectors as tests/FreeVelo.Devices.Tests (C#). */
class ProtocolTest {
    private fun bytes(vararg values: Int) = ByteArray(values.size) { values[it].toByte() }

    @Test
    fun indoorBikeDataSpeedCadencePower() {
        val d = Protocol.parseIndoorBikeData(bytes(0x44, 0x00, 0xB2, 0x0C, 0xB4, 0x00, 0xFA, 0x00))!!
        assertEquals(32.5, d.speedKmh!!, 0.001)
        assertEquals(90.0, d.cadenceRpm!!, 0.001)
        assertEquals(250, d.powerW)
        assertNull(d.heartRate)
    }

    @Test
    fun indoorBikeDataMoreDataAndHeartRate() {
        val d = Protocol.parseIndoorBikeData(bytes(0x01, 0x02, 0x8C))!!
        assertNull(d.speedKmh)
        assertEquals(140, d.heartRate)
    }

    @Test
    fun indoorBikeDataAllFields() {
        val d = Protocol.parseIndoorBikeData(
            bytes(
                0xFE, 0x1F, 0x10, 0x27, 0xE8, 0x03, 0xA0, 0x00, 0x96, 0x00, 0x40, 0x42, 0x0F,
                0xFB, 0xFF, 0x2C, 0x01, 0xC8, 0x00, 0x64, 0x00, 0xFF, 0xFF, 0xFF, 0x78, 0x2D,
                0x10, 0x0E, 0x2C, 0x01,
            ),
        )!!
        assertEquals(100.0, d.speedKmh!!, 0.001)
        assertEquals(80.0, d.cadenceRpm!!, 0.001)
        assertEquals(300, d.powerW)
        assertEquals(120, d.heartRate)
    }

    @Test
    fun indoorBikeDataTruncated() {
        assertNull(Protocol.parseIndoorBikeData(bytes(0x44, 0x00, 0xB2, 0x0C, 0xB4)))
        assertNull(Protocol.parseIndoorBikeData(bytes()))
    }

    @Test
    fun cyclingPowerWithBalanceWheelAndCrank() {
        val p = Protocol.parseCyclingPower(
            bytes(0x33, 0x00, 0x2C, 0x01, 0x66, 0x10, 0x27, 0x00, 0x00, 0x00, 0x10, 0x05, 0x00, 0x00, 0x04),
        )!!
        assertEquals(300, p.powerW)
        assertEquals(5, p.crankRevolutions)
        assertEquals(1024, p.crankEventTime)
    }

    @Test
    fun heartRate() {
        assertEquals(72, Protocol.parseHeartRate(bytes(0x00, 0x48)))
        assertEquals(140, Protocol.parseHeartRate(bytes(0x1F, 0x8C, 0x00, 0x0A, 0x00)))
        assertNull(Protocol.parseHeartRate(bytes()))
    }

    @Test
    fun controlPointCommands() {
        assertArrayEquals(bytes(0x00), Protocol.requestControl())
        assertArrayEquals(bytes(0x07), Protocol.start())
        assertArrayEquals(bytes(0x05, 0xFA, 0x00), Protocol.setTargetPower(250))
        assertArrayEquals(bytes(0x05, 0x00, 0x00), Protocol.setTargetPower(-20))
        assertArrayEquals(bytes(0x11, 0x00, 0x00, 0xF4, 0x01, 0x28, 0x33), Protocol.setSimulation(5.0))
        assertArrayEquals(bytes(0x11, 0x00, 0x00, 0xA2, 0xFE, 0x50, 0x28), Protocol.setSimulation(-3.5, 0.008, 0.40))
    }

    @Test
    fun controlPointResponses() {
        assertEquals("SetTargetPower: Success", Protocol.describeControlPointResponse(bytes(0x80, 0x05, 0x01)))
        assertEquals("SetSimulation: Control not permitted", Protocol.describeControlPointResponse(bytes(0x80, 0x11, 0x05)))
        assertNull(Protocol.describeControlPointResponse(bytes(0x05, 0xFA, 0x00)))
        assertTrue(Protocol.isSuccessResponseTo(bytes(0x80, 0x00, 0x01), Protocol.OP_REQUEST_CONTROL))
        assertFalse(Protocol.isSuccessResponseTo(bytes(0x80, 0x00, 0x05), Protocol.OP_REQUEST_CONTROL))
    }

    @Test
    fun featuresAndPowerRange() {
        assertEquals(
            "cadence=true power=true erg=true sim=true",
            Protocol.describeFeatures(bytes(0x02, 0x40, 0x00, 0x00, 0x08, 0x20, 0x00, 0x00)),
        )
        assertEquals("0–2200 W, step 5 W", Protocol.describePowerRange(bytes(0x00, 0x00, 0x98, 0x08, 0x05, 0x00)))
    }

    @Test
    fun crankCadence() {
        val c = CrankCadence()
        assertNull(c.update(100, 0, 0))
        assertEquals(90.0, c.update(103, 2048, 2000)!!, 0.001)

        val rollover = CrankCadence()
        rollover.update(65535, 65000, 0)
        assertEquals(90.0, rollover.update(1, 829, 1333)!!, 0.1)

        val firstStroke = CrankCadence()
        firstStroke.update(0, 0, 0)
        assertNull(firstStroke.update(1, 15700, 1000))
        assertEquals(26.0, firstStroke.update(2, 18045, 4000)!!, 0.5)

        val stop = CrankCadence(stopTimeoutMs = 3000)
        stop.update(10, 0, 0)
        stop.update(11, 683, 700)
        assertEquals(0.0, stop.update(11, 683, 3700)!!, 0.0)
    }

    @Test
    fun tacxFecCommands() {
        val erg = TacxFec.setTargetPower(250)
        assertArrayEquals(bytes(0xA4, 0x09, 0x4F, 0x05, 0x31, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xE8, 0x03), erg.copyOf(12))
        var x = 0
        for (i in 0 until 12) x = x xor (erg[i].toInt() and 0xFF)
        assertEquals(x, erg[12].toInt() and 0xFF)

        assertArrayEquals(bytes(0x33, 0xFF, 0xFF, 0xFF, 0xFF, 0x14, 0x50, 0x50), TacxFec.setTrackResistance(5.0).copyOfRange(4, 12))
        assertArrayEquals(bytes(0x37, 0x4C, 0x1D, 0xFF, 0x40, 0x0B, 0x46, 0x00), TacxFec.setUserConfiguration(75.0).copyOfRange(4, 12))
    }

    @Test
    fun tacxFecParsing() {
        fun broadcast(vararg page: Int): ByteArray {
            val msg = bytes(0xA4, 0x09, 0x4E, 0x05, *page, 0)
            var x = 0
            for (i in 0 until 12) x = x xor (msg[i].toInt() and 0xFF)
            msg[12] = x.toByte()
            return msg
        }
        val trainer = TacxFec.parse(broadcast(0x19, 0x01, 0x55, 0x00, 0x00, 0x0F, 0x31, 0x20))!!
        assertEquals(271, trainer.powerW)
        assertEquals(85, trainer.cadenceRpm)
        assertEquals(36.0, TacxFec.parse(broadcast(0x10, 0x19, 0x00, 0x00, 0x10, 0x27, 0xFF, 0x24))!!.speedKmh!!, 0.001)

        val needsConfig = TacxFec.parse(broadcast(0x19, 0x01, 0x42, 0x00, 0x00, 0x32, 0x40, 0x30))!!
        assertTrue(needsConfig.userConfigRequired)
        assertEquals(50, needsConfig.powerW)
        assertFalse(trainer.userConfigRequired)

        val status = TacxFec.parse(broadcast(0x47, 0x33, 0x02, 0x00, 0xFF, 0xFF, 0xFF, 0xFF))!!
        assertEquals(0x33, status.lastCommand)
        assertEquals(2, status.commandSequence)
        assertEquals("pass", TacxFec.describeCommandResult(status.commandResult!!))
        assertNull(TacxFec.parse(broadcast(0x47, 0xFF, 0xFF, 0xFF, 0x16, 0x0B, 0xFF, 0x34))!!.lastCommand)

        val bad = broadcast(0x19, 0, 0, 0, 0, 0, 0, 0)
        bad[12] = (bad[12].toInt() xor 0xFF).toByte()
        assertNull(TacxFec.parse(bad))
    }
}
