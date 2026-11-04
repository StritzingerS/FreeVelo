package io.github.stritzingers.freevelo.ble

import java.util.Locale
import java.util.UUID

/** Bluetooth SIG assigned numbers used by FreeVelo (mirrors FreeVelo.Devices.Ble.GattUuids). */
object GattUuids {
    private const val BASE_SUFFIX = "-0000-1000-8000-00805f9b34fb"

    fun fromShort(shortUuid: Int): UUID =
        UUID.fromString(String.format(Locale.ROOT, "0000%04x", shortUuid) + BASE_SUFFIX)

    /** "2AD2" for standard UUIDs, the full UUID otherwise. */
    fun shortName(uuid: UUID): String {
        val text = uuid.toString()
        return if (text.startsWith("0000") && text.endsWith(BASE_SUFFIX)) {
            text.substring(4, 8).uppercase(Locale.ROOT)
        } else {
            text
        }
    }

    // Services
    val FITNESS_MACHINE_SERVICE: UUID = fromShort(0x1826)
    val CYCLING_POWER_SERVICE: UUID = fromShort(0x1818)
    val HEART_RATE_SERVICE: UUID = fromShort(0x180D)
    val BATTERY_SERVICE: UUID = fromShort(0x180F)
    val CYCLING_SPEED_CADENCE_SERVICE: UUID = fromShort(0x1816)

    /** Tacx proprietary ANT+ FE-C over BLE service (Tacx trainers). */
    val TACX_FEC_SERVICE: UUID = UUID.fromString("6e40fec1-b5a3-f393-e0a9-e50e24dcca9e")

    // FTMS
    val FITNESS_MACHINE_FEATURE: UUID = fromShort(0x2ACC)
    val INDOOR_BIKE_DATA: UUID = fromShort(0x2AD2)
    val TRAINING_STATUS: UUID = fromShort(0x2AD3)
    val SUPPORTED_POWER_RANGE: UUID = fromShort(0x2AD8)
    val FITNESS_MACHINE_CONTROL_POINT: UUID = fromShort(0x2AD9)
    val FITNESS_MACHINE_STATUS: UUID = fromShort(0x2ADA)

    // Cycling Power
    val CYCLING_POWER_MEASUREMENT: UUID = fromShort(0x2A63)

    // Heart Rate
    val HEART_RATE_MEASUREMENT: UUID = fromShort(0x2A37)

    // Battery
    val BATTERY_LEVEL: UUID = fromShort(0x2A19)

    // Descriptors
    val CLIENT_CHARACTERISTIC_CONFIG: UUID = fromShort(0x2902)

    /** Services FreeVelo can use; scan results advertising one of them are shown first. */
    val KNOWN_SERVICES: Set<UUID> = setOf(FITNESS_MACHINE_SERVICE, CYCLING_POWER_SERVICE, HEART_RATE_SERVICE)

    /** Advertised services that identify a cycling/fitness device. */
    val FITNESS_SERVICES: Set<UUID> = KNOWN_SERVICES + setOf(CYCLING_SPEED_CADENCE_SERVICE, TACX_FEC_SERVICE)

    /**
     * Characteristics FreeVelo subscribes to, in subscription order.
     * The control point comes first: its indications must be enabled before writing commands.
     */
    val SUBSCRIBE: List<UUID> = listOf(
        FITNESS_MACHINE_CONTROL_POINT,
        INDOOR_BIKE_DATA,
        FITNESS_MACHINE_STATUS,
        TRAINING_STATUS,
        CYCLING_POWER_MEASUREMENT,
        HEART_RATE_MEASUREMENT,
        BATTERY_LEVEL,
    )

    /** Characteristics read once after connecting. */
    val READ_ON_CONNECT: List<UUID> = listOf(FITNESS_MACHINE_FEATURE, SUPPORTED_POWER_RANGE, BATTERY_LEVEL)
}
