package io.github.stritzingers.freevelo.ride

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import io.github.stritzingers.freevelo.ble.R

/**
 * Foreground service during a ride: keeps the process, the Bluetooth connections and the CPU alive while the
 * screen is off or another app is in front, and shows an ongoing notification (ride time counts up by itself).
 * Started and stopped from Unity through the static methods.
 */
class RideService : Service() {
    private var wakeLock: PowerManager.WakeLock? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopForegroundCompat()
            stopSelf()
            return START_NOT_STICKY
        }

        val title = intent?.getStringExtra(EXTRA_TITLE) ?: "FreeVelo"
        val text = intent?.getStringExtra(EXTRA_TEXT) ?: ""
        val startedAt = intent?.getLongExtra(EXTRA_STARTED_AT, System.currentTimeMillis()) ?: System.currentTimeMillis()
        val notification = buildNotification(this, title, text, startedAt)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }

        if (wakeLock == null) {
            val pm = getSystemService(Context.POWER_SERVICE) as PowerManager
            wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "FreeVelo:ride").apply {
                setReferenceCounted(false)
                acquire(MAX_RIDE_MS)
            }
        }

        return START_NOT_STICKY
    }

    override fun onDestroy() {
        wakeLock?.let { if (it.isHeld) it.release() }
        wakeLock = null
        super.onDestroy()
    }

    /** The user swiped the app away: end the service with it. */
    override fun onTaskRemoved(rootIntent: Intent?) {
        stopForegroundCompat()
        stopSelf()
        super.onTaskRemoved(rootIntent)
    }

    private fun stopForegroundCompat() {
        stopForeground(STOP_FOREGROUND_REMOVE)
    }

    companion object {
        const val CHANNEL_ID = "freevelo_ride"
        const val NOTIFICATION_ID = 4711
        const val ACTION_START = "io.github.stritzingers.freevelo.ride.START"
        const val ACTION_STOP = "io.github.stritzingers.freevelo.ride.STOP"
        const val EXTRA_TITLE = "title"
        const val EXTRA_TEXT = "text"
        const val EXTRA_STARTED_AT = "startedAt"
        private const val MAX_RIDE_MS = 8L * 60 * 60 * 1000

        /** Starts (or updates) the foreground service; [startedAtMillis] drives the notification's ride clock. */
        @JvmStatic
        fun start(context: Context, title: String, text: String, startedAtMillis: Long) {
            ensureChannel(context)
            val intent = Intent(context, RideService::class.java)
                .setAction(ACTION_START)
                .putExtra(EXTRA_TITLE, title)
                .putExtra(EXTRA_TEXT, text)
                .putExtra(EXTRA_STARTED_AT, startedAtMillis)
            try {
                context.startForegroundService(intent)
            } catch (e: Exception) {
                // Not allowed right now (e.g. app not in the foreground): the ride continues without the service.
            }
        }

        /** Updates the notification text without restarting the service. */
        @JvmStatic
        fun update(context: Context, title: String, text: String, startedAtMillis: Long) {
            ensureChannel(context)
            val nm = context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            nm.notify(NOTIFICATION_ID, buildNotification(context, title, text, startedAtMillis))
        }

        @JvmStatic
        fun stop(context: Context) {
            context.stopService(Intent(context, RideService::class.java))
        }

        private fun ensureChannel(context: Context) {
            val nm = context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            if (nm.getNotificationChannel(CHANNEL_ID) != null) return
            val channel = NotificationChannel(CHANNEL_ID, "Ride", NotificationManager.IMPORTANCE_LOW).apply {
                description = "Shown while a ride is recorded"
                setShowBadge(false)
            }
            nm.createNotificationChannel(channel)
        }

        private fun buildNotification(context: Context, title: String, text: String, startedAtMillis: Long): Notification {
            val launch = context.packageManager.getLaunchIntentForPackage(context.packageName)?.apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED)
            }
            val content = launch?.let {
                PendingIntent.getActivity(context, 0, it, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            }
            return Notification.Builder(context, CHANNEL_ID)
                .setSmallIcon(R.drawable.freevelo_notification)
                .setContentTitle(title)
                .setContentText(text)
                .setWhen(startedAtMillis)
                .setUsesChronometer(true)
                .setShowWhen(true)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setCategory(Notification.CATEGORY_WORKOUT)
                .setContentIntent(content)
                .build()
        }
    }
}
