package io.github.stritzingers.freevelo.ble

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.UUID

class ScannedDeviceTest {
    private fun device(name: String?, vararg services: UUID) = ScannedDevice("AA:BB", name, -60, services.toSet())

    @Test
    fun fitnessServicesAreAccepted() {
        assertTrue(device(null, GattUuids.FITNESS_MACHINE_SERVICE).isFitnessDevice)
        assertTrue(device("Some PM", GattUuids.CYCLING_POWER_SERVICE).isFitnessDevice)
        assertTrue(device("Strap", GattUuids.HEART_RATE_SERVICE).isFitnessDevice)
        assertTrue(device(null, GattUuids.CYCLING_SPEED_CADENCE_SERVICE).isFitnessDevice)
        assertTrue(device(null, GattUuids.TACX_FEC_SERVICE).isFitnessDevice)
    }

    @Test
    fun knownFitnessNamesWithoutServicesAreAccepted() {
        assertTrue(device("Tacx Neo 2T 12345").isFitnessDevice)
        assertTrue(device("KICKR CORE 5D21").isFitnessDevice)
        assertTrue(device("HRM-Pro:123456").isFitnessDevice)
        assertTrue(device("Polar H10 ABCD").isFitnessDevice)
        assertTrue(device("Zwift Click").isFitnessDevice)
    }

    @Test
    fun otherDevicesAreRejected() {
        val audio = UUID.fromString("0000110b-0000-1000-8000-00805f9b34fb")
        assertFalse(device("WH-1000XM5", audio).isFitnessDevice)
        assertFalse(device("Galaxy Buds2 Pro").isFitnessDevice)
        assertFalse(device("[TV] Samsung").isFitnessDevice)
        assertFalse(device(null).isFitnessDevice)
    }
}
