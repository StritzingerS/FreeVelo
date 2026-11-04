package io.github.stritzingers.freevelo.bletest

import android.Manifest
import android.app.Activity
import android.content.ContentValues
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Typeface
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.provider.MediaStore
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.view.WindowManager
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import androidx.core.content.FileProvider
import io.github.stritzingers.freevelo.ble.BleListener
import io.github.stritzingers.freevelo.ble.BleManager
import io.github.stritzingers.freevelo.ble.ByteLog
import io.github.stritzingers.freevelo.ble.ConnectionState
import io.github.stritzingers.freevelo.ble.GattUuids
import io.github.stritzingers.freevelo.ble.ScannedDevice
import java.io.File
import java.io.IOException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.UUID

/**
 * FreeVelo BLE test app (phase 0): scan, connect trainer / HR / power meter,
 * show live values, control the trainer in ERG and SIM mode, record a byte log.
 */
class MainActivity : Activity(), BleListener {
    private val handler = Handler(Looper.getMainLooper())
    private lateinit var ble: BleManager
    private lateinit var byteLog: ByteLog

    private val devices = linkedMapOf<String, ScannedDevice>()
    private val states = mutableMapOf<String, ConnectionState>()
    private val connectedServices = mutableMapOf<String, Set<UUID>>()
    private val liveByDevice = linkedMapOf<String, String>()
    private val cadenceByDevice = mutableMapOf<String, CrankCadence>()
    private val logLines = ArrayDeque<String>()

    private enum class TrainerProtocol { FTMS, TACX_FEC }

    private var trainerAddress: String? = null
    private var trainerProtocol: TrainerProtocol? = null
    private var hasControl = false
    private var fecSpeedKmh: Double? = null
    private var fecLastCommandStatus: Triple<Int, Int, Int>? = null
    private var fecUserConfigSentAt = 0L
    private var ergTargetW = 150
    private var gradePercent = 0.0

    private lateinit var liveText: TextView
    private lateinit var deviceList: LinearLayout
    private lateinit var trainerText: TextView
    private lateinit var ergText: TextView
    private lateinit var gradeText: TextView
    private lateinit var logText: TextView

    private val stopScan = Runnable {
        ble.stopScan()
        log("Scan stopped")
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        val stamp = SimpleDateFormat("yyyyMMdd-HHmmss", Locale.ROOT).format(Date())
        byteLog = ByteLog(File(filesDir, "logs/ble-$stamp.csv"))
        ble = BleManager(this, this, byteLog)

        buildUi()
        requestMissingPermissions()
        log("Log file: ${byteLog.file.name}")
    }

    override fun onDestroy() {
        handler.removeCallbacks(stopScan)
        ble.stopScan()
        ble.disconnectAll()
        byteLog.close()
        super.onDestroy()
    }

    // ---------------------------------------------------------------- UI

    private fun buildUi() {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val p = dp(16)
            setPadding(p, p, p, p)
        }

        root.addView(text("FreeVelo BLE Test", 22f, bold = true))
        liveText = text("–", 26f, mono = true)
        root.addView(liveText)

        root.addView(section("Devices"))
        root.addView(row(
            button("Scan") { startScan() },
            button("Stop scan") { handler.removeCallbacks(stopScan); ble.stopScan(); log("Scan stopped") },
            button("Disconnect all") { disconnectAll() },
        ))
        deviceList = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        root.addView(deviceList)

        root.addView(section("Trainer control (FTMS or Tacx FE-C)"))
        trainerText = text("No trainer connected", 14f)
        root.addView(trainerText)
        root.addView(row(
            button("Request control") { ftmsOnly { writeTrainer(Protocol.requestControl(), "Request control") } },
            button("Start") { ftmsOnly { writeTrainer(Protocol.start(), "Start") } },
            button("Stop") { ftmsOnly { writeTrainer(Protocol.stop(), "Stop") } },
        ))

        ergText = text("", 18f, mono = true)
        root.addView(ergText)
        root.addView(row(
            button("−50") { changeErg(-50) },
            button("−10") { changeErg(-10) },
            button("+10") { changeErg(10) },
            button("+50") { changeErg(50) },
        ))
        root.addView(row(button("Set ERG power") { sendErg() }))

        gradeText = text("", 18f, mono = true)
        root.addView(gradeText)
        root.addView(row(
            button("−5") { changeGrade(-5.0) },
            button("−1") { changeGrade(-1.0) },
            button("+1") { changeGrade(1.0) },
            button("+5") { changeGrade(5.0) },
        ))
        root.addView(row(button("Set grade (SIM)") { sendGrade() }))

        root.addView(section("Log"))
        root.addView(row(
            button("Share byte log") { shareLog() },
            button("Save to Downloads") { saveLogToDownloads() },
        ))
        logText = text("", 12f, mono = true)
        root.addView(logText)

        val scroll = ScrollView(this).apply { addView(root) }
        scroll.setOnApplyWindowInsetsListener { v, insets ->
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                val bars = insets.getInsets(WindowInsets.Type.systemBars())
                v.setPadding(bars.left, bars.top, bars.right, bars.bottom)
            } else {
                @Suppress("DEPRECATION")
                v.setPadding(
                    insets.systemWindowInsetLeft,
                    insets.systemWindowInsetTop,
                    insets.systemWindowInsetRight,
                    insets.systemWindowInsetBottom,
                )
            }
            insets
        }
        setContentView(scroll)

        refreshErgAndGrade()
        refreshLive()
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun text(value: String, sizeSp: Float, bold: Boolean = false, mono: Boolean = false) =
        TextView(this).apply {
            text = value
            textSize = sizeSp
            if (mono) typeface = Typeface.MONOSPACE
            if (bold) setTypeface(typeface, Typeface.BOLD)
            setPadding(0, dp(4), 0, dp(4))
        }

    private fun section(title: String) = text(title, 18f, bold = true).apply { setPadding(0, dp(16), 0, dp(4)) }

    private fun button(label: String, onClick: () -> Unit) = Button(this).apply {
        text = label
        isAllCaps = false
        setOnClickListener { onClick() }
    }

    private fun row(vararg views: View) = LinearLayout(this).apply {
        orientation = LinearLayout.HORIZONTAL
        for (v in views) {
            addView(v, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        }
    }

    private fun log(message: String) {
        val time = SimpleDateFormat("HH:mm:ss", Locale.ROOT).format(Date())
        logLines.addFirst("$time $message")
        while (logLines.size > 40) logLines.removeLast()
        if (::logText.isInitialized) logText.text = logLines.joinToString("\n")
    }

    private fun refreshErgAndGrade() {
        ergText.text = "ERG target: $ergTargetW W"
        gradeText.text = String.format(Locale.ROOT, "SIM grade: %+.1f %%", gradePercent)
    }

    private fun refreshLive() {
        liveText.text = if (liveByDevice.isEmpty()) "No data yet" else liveByDevice.values.joinToString("\n")
    }

    private fun refreshDevices() {
        deviceList.removeAllViews()
        val sorted = devices.values.sortedWith(
            compareByDescending<ScannedDevice> { states.containsKey(it.address) }
                .thenByDescending { it.isKnownDevice }
                .thenByDescending { it.rssi },
        )
        for (d in sorted) {
            val state = states[d.address]
            val services = connectedServices[d.address] ?: d.services
            val tags = buildList {
                if (GattUuids.FITNESS_MACHINE_SERVICE in services) add("FTMS")
                if (GattUuids.CYCLING_POWER_SERVICE in services) add("Power")
                if (GattUuids.HEART_RATE_SERVICE in services) add("HR")
                if (GattUuids.CYCLING_SPEED_CADENCE_SERVICE in services) add("Speed/Cad")
                if (GattUuids.TACX_FEC_SERVICE in services) add("Tacx")
            }.joinToString(" ")
            val label = "${d.name ?: d.address}  [$tags]  ${d.rssi} dBm\n${state ?: "tap to connect"}"
            deviceList.addView(button(label) {
                guarded {
                    if (ble.isConnected(d.address)) {
                        ble.disconnect(d.address)
                    } else {
                        ble.connect(d.address)
                    }
                }
            })
        }
    }

    private fun refreshTrainer() {
        val t = trainerAddress
        trainerText.text = if (t == null) {
            "No trainer connected"
        } else {
            when (trainerProtocol) {
                TrainerProtocol.TACX_FEC -> "Trainer: ${devices[t]?.name ?: t} – Tacx FE-C (no FTMS)"
                else -> "Trainer: ${devices[t]?.name ?: t} – FTMS, control: ${if (hasControl) "yes" else "no"}"
            }
        }
    }

    // ---------------------------------------------------------------- Actions

    private fun requestMissingPermissions() {
        val needed = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            arrayOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_CONNECT)
        } else {
            arrayOf(Manifest.permission.ACCESS_FINE_LOCATION)
        }
        val missing = needed.filter { checkSelfPermission(it) != PackageManager.PERMISSION_GRANTED }
        if (missing.isNotEmpty()) requestPermissions(missing.toTypedArray(), REQUEST_PERMISSIONS)
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == REQUEST_PERMISSIONS) {
            val granted = grantResults.isNotEmpty() && grantResults.all { it == PackageManager.PERMISSION_GRANTED }
            log(if (granted) "Bluetooth permissions granted" else "Bluetooth permissions DENIED – app cannot work")
        }
    }

    private fun startScan() {
        if (!ble.isBluetoothEnabled) {
            log("Bluetooth is switched off")
            return
        }
        devices.keys.retainAll { states.containsKey(it) }
        refreshDevices()
        if (!guarded { ble.startScan() }) return
        log("Scanning for 20 s …")
        handler.removeCallbacks(stopScan)
        handler.postDelayed(stopScan, 20_000)
    }

    /** Runs [action]; logs instead of crashing if Bluetooth permissions are missing. */
    private fun guarded(action: () -> Unit): Boolean = try {
        action()
        true
    } catch (e: SecurityException) {
        log("Missing Bluetooth permission – please allow it in the app settings")
        requestMissingPermissions()
        false
    }

    private fun disconnectAll() {
        ble.disconnectAll()
        states.clear()
        connectedServices.clear()
        liveByDevice.clear()
        trainerAddress = null
        trainerProtocol = null
        hasControl = false
        refreshDevices()
        refreshTrainer()
        refreshLive()
    }

    private fun changeErg(delta: Int) {
        ergTargetW = (ergTargetW + delta).coerceIn(0, 2000)
        refreshErgAndGrade()
    }

    private fun changeGrade(delta: Double) {
        gradePercent = (gradePercent + delta).coerceIn(-20.0, 25.0)
        refreshErgAndGrade()
    }

    private fun sendErg() {
        val label = "ERG $ergTargetW W"
        when (trainerProtocol) {
            TrainerProtocol.TACX_FEC -> writeTrainer(TacxFec.setTargetPower(ergTargetW), label)
            else -> {
                ensureControl()
                writeTrainer(Protocol.setTargetPower(ergTargetW), label)
            }
        }
    }

    private fun sendGrade() {
        val label = String.format(Locale.ROOT, "SIM grade %+.1f %%", gradePercent)
        when (trainerProtocol) {
            TrainerProtocol.TACX_FEC -> writeTrainer(TacxFec.setTrackResistance(gradePercent), label)
            else -> {
                ensureControl()
                writeTrainer(Protocol.setSimulation(gradePercent), label)
            }
        }
    }

    private fun sendFecUserConfig() {
        fecUserConfigSentAt = SystemClock.elapsedRealtime()
        writeTrainer(TacxFec.setUserConfiguration(RIDER_WEIGHT_KG), "User config ${RIDER_WEIGHT_KG.toInt()} kg")
    }

    private fun ftmsOnly(action: () -> Unit) {
        if (trainerProtocol == TrainerProtocol.TACX_FEC) log("Not needed for Tacx FE-C") else action()
    }

    /** FTMS needs "request control" (and often "start") before target commands; writes are queued in order. */
    private fun ensureControl() {
        if (!hasControl) {
            writeTrainer(Protocol.requestControl(), "Request control")
            writeTrainer(Protocol.start(), "Start")
        }
    }

    /** Writes to the FTMS control point or the Tacx FE-C TX characteristic. */
    private fun writeTrainer(data: ByteArray, label: String) {
        val trainer = trainerAddress
        if (trainer == null) {
            log("No controllable trainer connected (neither FTMS nor Tacx FE-C found)")
            return
        }
        val characteristic = if (trainerProtocol == TrainerProtocol.TACX_FEC) {
            GattUuids.TACX_FEC_TX
        } else {
            GattUuids.FITNESS_MACHINE_CONTROL_POINT
        }
        if (ble.write(trainer, characteristic, data)) {
            log("→ $label (${ByteLog.toHex(data)})")
        } else {
            log("✗ $label: control point not available")
        }
    }

    private fun shareLog() {
        val file = byteLog.file
        if (!file.exists()) {
            log("Log is empty")
            return
        }
        val uri = FileProvider.getUriForFile(this, "$packageName.fileprovider", file)
        val intent = Intent(Intent.ACTION_SEND).apply {
            type = "text/csv"
            putExtra(Intent.EXTRA_STREAM, uri)
            putExtra(Intent.EXTRA_SUBJECT, "FreeVelo BLE log ${file.name}")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        startActivity(Intent.createChooser(intent, "Share BLE log"))
    }

    /** Copies the current byte log to Downloads/FreeVelo (no storage permission needed on Android 10+). */
    private fun saveLogToDownloads() {
        val file = byteLog.file
        if (!file.exists()) {
            log("Log is empty")
            return
        }
        val name = "FreeVelo-${file.name}"
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, name)
            put(MediaStore.MediaColumns.MIME_TYPE, "text/csv")
            put(MediaStore.MediaColumns.RELATIVE_PATH, "${Environment.DIRECTORY_DOWNLOADS}/FreeVelo")
        }
        try {
            val uri = contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
                ?: throw IOException("could not create file")
            val out = contentResolver.openOutputStream(uri) ?: throw IOException("could not open file")
            out.use { o -> file.inputStream().use { it.copyTo(o) } }
            log("Saved: Downloads/FreeVelo/$name")
        } catch (e: Exception) {
            log("✗ Saving failed: ${e.message}")
        }
    }

    // ---------------------------------------------------------------- BleListener

    override fun onScanResult(device: ScannedDevice) {
        val previous = devices[device.address]
        val merged = if (previous != null) {
            device.copy(name = device.name ?: previous.name, services = previous.services + device.services)
        } else {
            device
        }
        devices[device.address] = merged
        if (previous == null || previous.services != merged.services) refreshDevices()
    }

    override fun onConnectionStateChanged(address: String, state: ConnectionState) {
        if (state == ConnectionState.DISCONNECTED) {
            states.remove(address)
            connectedServices.remove(address)
        } else {
            states[address] = state
        }
        if (state != ConnectionState.READY) {
            liveByDevice.remove(address)
            liveByDevice.remove("$address-fec")
            cadenceByDevice.remove(address)
            if (address == trainerAddress) {
                hasControl = false
                if (state == ConnectionState.DISCONNECTED) {
                    trainerAddress = null
                    trainerProtocol = null
                }
            }
        }
        log("${name(address)}: $state")
        refreshDevices()
        refreshTrainer()
        refreshLive()
    }

    override fun onReady(address: String, services: Set<UUID>) {
        connectedServices[address] = services
        log("${name(address)} services: ${services.joinToString(" ") { GattUuids.shortName(it).take(8) }}")
        when {
            GattUuids.FITNESS_MACHINE_SERVICE in services -> {
                trainerAddress = address
                trainerProtocol = TrainerProtocol.FTMS
                log("${name(address)} is an FTMS trainer")
            }
            GattUuids.TACX_FEC_SERVICE in services -> {
                trainerAddress = address
                trainerProtocol = TrainerProtocol.TACX_FEC
                log("${name(address)}: no FTMS, using Tacx FE-C")
                fecLastCommandStatus = null
                sendFecUserConfig()
            }
            GattUuids.CYCLING_POWER_SERVICE in services && trainerAddress == null ->
                log("${name(address)}: power only, no trainer control")
        }
        refreshDevices()
        refreshTrainer()
    }

    override fun onCharacteristicData(address: String, characteristic: UUID, data: ByteArray) {
        val n = name(address)
        when (characteristic) {
            GattUuids.INDOOR_BIKE_DATA -> Protocol.parseIndoorBikeData(data)?.let { d ->
                // Trainers may split data over packets; only update the fields present.
                if (d.powerW != null || d.cadenceRpm != null || d.speedKmh != null) {
                    liveByDevice[address] = String.format(
                        Locale.ROOT,
                        "%s\n  %s W  %s rpm  %s km/h",
                        n,
                        d.powerW?.toString() ?: "–",
                        d.cadenceRpm?.let { "%.0f".format(Locale.ROOT, it) } ?: "–",
                        d.speedKmh?.let { "%.1f".format(Locale.ROOT, it) } ?: "–",
                    )
                    refreshLive()
                }
            }

            GattUuids.CYCLING_POWER_MEASUREMENT -> Protocol.parseCyclingPower(data)?.let { p ->
                // Prefer FTMS data for an FTMS trainer itself.
                if (address == trainerAddress && trainerProtocol == TrainerProtocol.FTMS) return@let
                val calc = cadenceByDevice.getOrPut(address) { CrankCadence() }
                // Some devices (e.g. Tacx Neo) send crank data only in every few packets: keep the last cadence.
                val cadence = if (p.crankRevolutions != null && p.crankEventTime != null) {
                    calc.update(p.crankRevolutions, p.crankEventTime, SystemClock.elapsedRealtime())
                } else {
                    calc.cadenceRpm
                }
                liveByDevice[address] = String.format(
                    Locale.ROOT,
                    "%s\n  %d W  %s rpm",
                    n,
                    p.powerW,
                    cadence?.let { "%.0f".format(Locale.ROOT, it) } ?: "–",
                )
                refreshLive()
            }

            GattUuids.HEART_RATE_MEASUREMENT -> Protocol.parseHeartRate(data)?.let { bpm ->
                liveByDevice[address] = "$n\n  ♥ $bpm bpm"
                refreshLive()
            }

            GattUuids.FITNESS_MACHINE_CONTROL_POINT -> {
                log("← ${Protocol.describeControlPointResponse(data) ?: ByteLog.toHex(data)}")
                if (Protocol.isSuccessResponseTo(data, Protocol.OP_REQUEST_CONTROL)) {
                    hasControl = true
                    refreshTrainer()
                }
            }

            GattUuids.TACX_FEC_RX -> TacxFec.parse(data)?.let { f ->
                if (f.page == 0x10) fecSpeedKmh = f.speedKmh
                if (f.page == 0x47 && f.lastCommand != null) {
                    val status = Triple(f.lastCommand, f.commandSequence ?: 0, f.commandResult ?: 255)
                    if (status != fecLastCommandStatus) {
                        fecLastCommandStatus = status
                        log(String.format(Locale.ROOT, "← FE-C page 0x%02X #%d: %s",
                            status.first, status.second, TacxFec.describeCommandResult(status.third)))
                    }
                }
                // The trainer asks for rider/bike weight again (seen on the Neo 2 after SIM commands).
                if (f.userConfigRequired && address == trainerAddress &&
                    SystemClock.elapsedRealtime() - fecUserConfigSentAt > 10_000
                ) {
                    log("Trainer requests user configuration")
                    sendFecUserConfig()
                }
                if (f.page == 0x19) {
                    liveByDevice["$address-fec"] = String.format(
                        Locale.ROOT,
                        "%s (FE-C)\n  %s W  %s rpm  %s km/h",
                        n,
                        f.powerW?.toString() ?: "–",
                        f.cadenceRpm?.toString() ?: "–",
                        fecSpeedKmh?.let { "%.1f".format(Locale.ROOT, it) } ?: "–",
                    )
                    refreshLive()
                }
            }

            GattUuids.FITNESS_MACHINE_FEATURE ->
                log("$n features: ${Protocol.describeFeatures(data) ?: ByteLog.toHex(data)}")

            GattUuids.SUPPORTED_POWER_RANGE ->
                log("$n power range: ${Protocol.describePowerRange(data) ?: ByteLog.toHex(data)}")

            GattUuids.BATTERY_LEVEL ->
                if (data.isNotEmpty()) log("$n battery: ${data[0].toInt() and 0xFF} %")

            GattUuids.FITNESS_MACHINE_STATUS ->
                log("$n status: ${ByteLog.toHex(data)}")
        }
    }

    override fun onWriteResult(address: String, characteristic: UUID, success: Boolean) {
        if (!success) log("✗ write to ${GattUuids.shortName(characteristic)} failed")
    }

    override fun onError(address: String?, message: String) {
        log("⚠ ${address?.let { name(it) + ": " } ?: ""}$message")
    }

    private fun name(address: String): String = devices[address]?.name ?: address

    private companion object {
        const val REQUEST_PERMISSIONS = 1
        const val RIDER_WEIGHT_KG = 75.0
    }
}
