package io.github.stritzingers.freevelo.ble

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCallback
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothProfile
import android.bluetooth.BluetoothStatusCodes
import android.content.Context
import android.os.Build
import android.os.Handler
import java.util.UUID

/**
 * One GATT connection. Android allows only one outstanding GATT operation per connection,
 * so all reads, writes and subscriptions go through a queue.
 * State is only touched on the main thread (callbacks are posted to [handler]).
 */
@SuppressLint("MissingPermission")
internal class GattConnection(
    private val context: Context,
    private val device: BluetoothDevice,
    private val listener: BleListener,
    private val byteLog: ByteLog?,
    private val handler: Handler,
) {
    private sealed class Op {
        data class Subscribe(val characteristic: BluetoothGattCharacteristic, val indicate: Boolean) : Op()
        class Write(val characteristic: BluetoothGattCharacteristic, val data: ByteArray) : Op()
        data class Read(val characteristic: BluetoothGattCharacteristic) : Op()
        object Ready : Op()
    }

    val address: String = device.address

    /** Android may return no name after a reconnect, so remember the last known one. */
    private var name: String? = device.name

    private var gatt: BluetoothGatt? = null
    private val queue = ArrayDeque<Op>()
    private var current: Op? = null
    private var closed = false
    private var reconnectAttempts = 0
    private var services: Set<UUID> = emptySet()

    private val opTimeout = Runnable {
        listener.onError(address, "GATT operation timed out: ${current?.javaClass?.simpleName}")
        current = null
        next()
    }

    private val reconnect = Runnable { if (!closed) connect() }

    private val discover = Runnable {
        val g = gatt
        if (g != null && !g.discoverServices()) {
            listener.onError(address, "Service discovery could not be started")
            g.disconnect()
        }
    }

    fun connect() {
        closed = false
        listener.onConnectionStateChanged(address, ConnectionState.CONNECTING)
        gatt = device.connectGatt(context, false, callback, BluetoothDevice.TRANSPORT_LE)
    }

    fun close() {
        closed = true
        handler.removeCallbacks(opTimeout)
        handler.removeCallbacks(reconnect)
        handler.removeCallbacks(discover)
        queue.clear()
        current = null
        gatt?.let {
            it.disconnect()
            it.close()
        }
        gatt = null
        listener.onConnectionStateChanged(address, ConnectionState.DISCONNECTED)
    }

    fun write(uuid: UUID, data: ByteArray): Boolean {
        val characteristic = find(uuid) ?: return false
        enqueue(Op.Write(characteristic, data.copyOf()))
        return true
    }

    fun read(uuid: UUID): Boolean {
        val characteristic = find(uuid) ?: return false
        enqueue(Op.Read(characteristic))
        return true
    }

    private fun find(uuid: UUID): BluetoothGattCharacteristic? =
        gatt?.services?.firstNotNullOfOrNull { it.getCharacteristic(uuid) }

    private fun enqueue(op: Op) {
        queue.addLast(op)
        if (current == null) next()
    }

    private fun next() {
        handler.removeCallbacks(opTimeout)
        val g = gatt
        val op = queue.removeFirstOrNull()
        if (g == null || op == null) {
            current = null
            return
        }
        current = op

        val started = when (op) {
            is Op.Subscribe -> subscribe(g, op)
            is Op.Write -> {
                byteLog?.append(address, op.characteristic.uuid, ByteLog.Direction.WRITE, op.data)
                writeCharacteristic(g, op.characteristic, op.data)
            }
            is Op.Read -> g.readCharacteristic(op.characteristic)
            Op.Ready -> {
                listener.onConnectionStateChanged(address, ConnectionState.READY)
                listener.onReady(address, services)
                current = null
                handler.post { if (current == null) next() }
                return
            }
        }

        if (!started) {
            listener.onError(address, "Could not start ${op.javaClass.simpleName} on ${describe(op)}")
            if (op is Op.Write) listener.onWriteResult(address, op.characteristic.uuid, false)
            current = null
            handler.post { if (current == null) next() }
            return
        }
        handler.postDelayed(opTimeout, OP_TIMEOUT_MS)
    }

    private fun opDone() {
        handler.removeCallbacks(opTimeout)
        current = null
        next()
    }

    private fun describe(op: Op): String = when (op) {
        is Op.Subscribe -> GattUuids.shortName(op.characteristic.uuid)
        is Op.Write -> GattUuids.shortName(op.characteristic.uuid)
        is Op.Read -> GattUuids.shortName(op.characteristic.uuid)
        Op.Ready -> "-"
    }

    private fun subscribe(g: BluetoothGatt, op: Op.Subscribe): Boolean {
        if (!g.setCharacteristicNotification(op.characteristic, true)) return false
        val descriptor = op.characteristic.getDescriptor(GattUuids.CLIENT_CHARACTERISTIC_CONFIG) ?: return false
        val value = if (op.indicate) {
            BluetoothGattDescriptor.ENABLE_INDICATION_VALUE
        } else {
            BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE
        }
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            g.writeDescriptor(descriptor, value) == BluetoothStatusCodes.SUCCESS
        } else {
            @Suppress("DEPRECATION")
            descriptor.value = value
            @Suppress("DEPRECATION")
            g.writeDescriptor(descriptor)
        }
    }

    private fun writeCharacteristic(g: BluetoothGatt, characteristic: BluetoothGattCharacteristic, data: ByteArray): Boolean {
        val writeType = if ((characteristic.properties and BluetoothGattCharacteristic.PROPERTY_WRITE) != 0) {
            BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT
        } else {
            BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE
        }
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            g.writeCharacteristic(characteristic, data, writeType) == BluetoothStatusCodes.SUCCESS
        } else {
            @Suppress("DEPRECATION")
            characteristic.writeType = writeType
            @Suppress("DEPRECATION")
            characteristic.value = data
            @Suppress("DEPRECATION")
            g.writeCharacteristic(characteristic)
        }
    }

    /** Queues subscriptions and initial reads after service discovery. */
    private fun setUp(g: BluetoothGatt) {
        val characteristics = g.services.flatMap { it.characteristics }
        services = g.services.map { it.uuid }.toSet()
        byteLog?.comment(describeGatt(g))

        for (uuid in GattUuids.SUBSCRIBE) {
            val ch = characteristics.firstOrNull { it.uuid == uuid } ?: continue
            val canNotify = (ch.properties and BluetoothGattCharacteristic.PROPERTY_NOTIFY) != 0
            val canIndicate = (ch.properties and BluetoothGattCharacteristic.PROPERTY_INDICATE) != 0
            if (canNotify || canIndicate) {
                queue.addLast(Op.Subscribe(ch, indicate = canIndicate && !canNotify))
            }
        }
        for (uuid in GattUuids.READ_ON_CONNECT) {
            val ch = characteristics.firstOrNull { it.uuid == uuid } ?: continue
            if ((ch.properties and BluetoothGattCharacteristic.PROPERTY_READ) != 0) {
                queue.addLast(Op.Read(ch))
            }
        }
        queue.addLast(Op.Ready)
        if (current == null) next()
    }

    /** Human readable list of services and characteristics, written to the byte log for diagnostics. */
    private fun describeGatt(g: BluetoothGatt): String = buildString {
        device.name?.let { name = it }
        append("GATT ").append(address).append(" (").append(name ?: "?").append(")")
        for (service in g.services) {
            append("\n  service ").append(GattUuids.shortName(service.uuid))
            for (ch in service.characteristics) {
                val p = ch.properties
                val props = listOfNotNull(
                    "R".takeIf { (p and BluetoothGattCharacteristic.PROPERTY_READ) != 0 },
                    "W".takeIf { (p and BluetoothGattCharacteristic.PROPERTY_WRITE) != 0 },
                    "WnR".takeIf { (p and BluetoothGattCharacteristic.PROPERTY_WRITE_NO_RESPONSE) != 0 },
                    "N".takeIf { (p and BluetoothGattCharacteristic.PROPERTY_NOTIFY) != 0 },
                    "I".takeIf { (p and BluetoothGattCharacteristic.PROPERTY_INDICATE) != 0 },
                ).joinToString("/")
                append("\n    char ").append(GattUuids.shortName(ch.uuid)).append(" [").append(props).append("]")
            }
        }
    }

    private fun handleDisconnect(g: BluetoothGatt, status: Int) {
        handler.removeCallbacks(opTimeout)
        handler.removeCallbacks(discover)
        queue.clear()
        current = null
        g.close()
        if (gatt === g) gatt = null
        if (closed) return

        listener.onConnectionStateChanged(address, ConnectionState.RECONNECTING)
        if (status != BluetoothGatt.GATT_SUCCESS) {
            listener.onError(address, "Connection lost (status $status), reconnecting")
        }
        val delay = minOf(MAX_RECONNECT_DELAY_MS, RECONNECT_BASE_DELAY_MS shl minOf(reconnectAttempts, 4))
        reconnectAttempts++
        handler.postDelayed(reconnect, delay)
    }

    private fun handleData(characteristic: UUID, data: ByteArray, direction: ByteLog.Direction) {
        byteLog?.append(address, characteristic, direction, data)
        listener.onCharacteristicData(address, characteristic, data)
    }

    private val callback = object : BluetoothGattCallback() {
        override fun onConnectionStateChange(g: BluetoothGatt, status: Int, newState: Int) {
            handler.post {
                if (closed) {
                    g.close()
                    return@post
                }
                if (newState == BluetoothProfile.STATE_CONNECTED && status == BluetoothGatt.GATT_SUCCESS) {
                    reconnectAttempts = 0
                    listener.onConnectionStateChanged(address, ConnectionState.CONNECTED)
                    // Short delay improves discovery reliability on some phones.
                    handler.postDelayed(discover, DISCOVERY_DELAY_MS)
                } else if (newState == BluetoothProfile.STATE_DISCONNECTED || status != BluetoothGatt.GATT_SUCCESS) {
                    handleDisconnect(g, status)
                }
            }
        }

        override fun onServicesDiscovered(g: BluetoothGatt, status: Int) {
            handler.post {
                if (closed) return@post
                if (status == BluetoothGatt.GATT_SUCCESS) {
                    setUp(g)
                } else {
                    listener.onError(address, "Service discovery failed (status $status)")
                    g.disconnect()
                }
            }
        }

        override fun onDescriptorWrite(g: BluetoothGatt, descriptor: BluetoothGattDescriptor, status: Int) {
            handler.post {
                if (status != BluetoothGatt.GATT_SUCCESS) {
                    listener.onError(
                        address,
                        "Subscribing to ${GattUuids.shortName(descriptor.characteristic.uuid)} failed (status $status)",
                    )
                }
                opDone()
            }
        }

        override fun onCharacteristicWrite(g: BluetoothGatt, characteristic: BluetoothGattCharacteristic, status: Int) {
            val uuid = characteristic.uuid
            handler.post {
                listener.onWriteResult(address, uuid, status == BluetoothGatt.GATT_SUCCESS)
                opDone()
            }
        }

        // Android 13+
        override fun onCharacteristicRead(
            g: BluetoothGatt,
            characteristic: BluetoothGattCharacteristic,
            value: ByteArray,
            status: Int,
        ) {
            val uuid = characteristic.uuid
            val copy = value.copyOf()
            handler.post {
                if (status == BluetoothGatt.GATT_SUCCESS) handleData(uuid, copy, ByteLog.Direction.READ)
                opDone()
            }
        }

        // Android 10–12
        @Deprecated("Deprecated in API 33")
        override fun onCharacteristicRead(g: BluetoothGatt, characteristic: BluetoothGattCharacteristic, status: Int) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) return
            val uuid = characteristic.uuid
            @Suppress("DEPRECATION")
            val copy = characteristic.value?.copyOf() ?: ByteArray(0)
            handler.post {
                if (status == BluetoothGatt.GATT_SUCCESS) handleData(uuid, copy, ByteLog.Direction.READ)
                opDone()
            }
        }

        // Android 13+
        override fun onCharacteristicChanged(
            g: BluetoothGatt,
            characteristic: BluetoothGattCharacteristic,
            value: ByteArray,
        ) {
            val uuid = characteristic.uuid
            val copy = value.copyOf()
            handler.post { handleData(uuid, copy, ByteLog.Direction.NOTIFY) }
        }

        // Android 10–12
        @Deprecated("Deprecated in API 33")
        override fun onCharacteristicChanged(g: BluetoothGatt, characteristic: BluetoothGattCharacteristic) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) return
            val uuid = characteristic.uuid
            @Suppress("DEPRECATION")
            val copy = characteristic.value?.copyOf() ?: ByteArray(0)
            handler.post { handleData(uuid, copy, ByteLog.Direction.NOTIFY) }
        }
    }

    private companion object {
        const val OP_TIMEOUT_MS = 5_000L
        const val DISCOVERY_DELAY_MS = 300L
        const val RECONNECT_BASE_DELAY_MS = 2_000L
        const val MAX_RECONNECT_DELAY_MS = 30_000L
    }
}
