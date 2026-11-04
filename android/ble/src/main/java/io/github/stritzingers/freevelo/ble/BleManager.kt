package io.github.stritzingers.freevelo.ble

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothManager
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.Context
import android.os.Handler
import android.os.Looper
import java.util.UUID

/**
 * Entry point of the FreeVelo BLE library: scanning and managing several connections
 * (e.g. trainer + heart rate strap + power meter) at once.
 *
 * The caller must hold the Bluetooth runtime permissions before using it.
 * All [BleListener] callbacks are delivered on the main thread.
 */
@SuppressLint("MissingPermission")
class BleManager(
    context: Context,
    private val listener: BleListener,
    private val byteLog: ByteLog? = null,
) {
    private val appContext = context.applicationContext
    private val adapter: BluetoothAdapter? =
        (appContext.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager?)?.adapter
    private val handler = Handler(Looper.getMainLooper())
    private val connections = mutableMapOf<String, GattConnection>()
    private var scanning = false

    private val scanCallback = object : ScanCallback() {
        override fun onScanResult(callbackType: Int, result: ScanResult) {
            val record = result.scanRecord
            val services = record?.serviceUuids?.map { it.uuid }?.toSet() ?: emptySet()
            val name = result.device.name ?: record?.deviceName
            val device = ScannedDevice(result.device.address, name, result.rssi, services)
            handler.post { listener.onScanResult(device) }
        }

        override fun onScanFailed(errorCode: Int) {
            scanning = false
            handler.post { listener.onError(null, "Scan failed (error $errorCode)") }
        }
    }

    val isBluetoothAvailable: Boolean get() = adapter != null

    val isBluetoothEnabled: Boolean get() = adapter?.isEnabled == true

    val isScanning: Boolean get() = scanning

    fun startScan() {
        if (scanning) return
        val scanner = adapter?.bluetoothLeScanner
        if (scanner == null || !isBluetoothEnabled) {
            listener.onError(null, "Bluetooth is not available or switched off")
            return
        }
        val settings = ScanSettings.Builder()
            .setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY)
            .build()
        // No filter: some trainers do not advertise all their services.
        scanner.startScan(null, settings, scanCallback)
        scanning = true
    }

    fun stopScan() {
        if (!scanning) return
        scanning = false
        if (isBluetoothEnabled) {
            adapter?.bluetoothLeScanner?.stopScan(scanCallback)
        }
    }

    fun connect(address: String) {
        if (connections.containsKey(address)) return
        val device = adapter?.getRemoteDevice(address) ?: run {
            listener.onError(address, "Bluetooth not available")
            return
        }
        val connection = GattConnection(appContext, device, listener, byteLog, handler)
        connections[address] = connection
        connection.connect()
    }

    fun disconnect(address: String) {
        connections.remove(address)?.close()
    }

    fun disconnectAll() {
        connections.values.forEach { it.close() }
        connections.clear()
    }

    fun isConnected(address: String): Boolean = connections.containsKey(address)

    /** Queues a write; returns false if the device or characteristic is not available. */
    fun write(address: String, characteristic: UUID, data: ByteArray): Boolean =
        connections[address]?.write(characteristic, data) ?: false

    /** Queues a read; the value arrives via [BleListener.onCharacteristicData]. */
    fun read(address: String, characteristic: UUID): Boolean =
        connections[address]?.read(characteristic) ?: false
}
