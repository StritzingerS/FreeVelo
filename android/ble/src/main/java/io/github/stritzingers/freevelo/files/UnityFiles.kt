package io.github.stritzingers.freevelo.files

import android.app.Activity
import android.content.ClipData
import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Environment
import android.provider.MediaStore
import java.io.File
import java.util.Locale

/** File helpers called from Unity (static methods). */
object UnityFiles {
    fun authority(context: Context): String = "${context.packageName}.freevelo.files"

    fun mimeTypeFor(path: String): String = when (path.substringAfterLast('.', "").lowercase(Locale.ROOT)) {
        "fit" -> "application/vnd.ant.fit"
        "csv" -> "text/csv"
        "json" -> "application/json"
        "zwo", "xml" -> "application/xml"
        else -> "application/octet-stream"
    }

    /** Opens the Android share sheet for a file inside the app's directories. */
    @JvmStatic
    fun share(activity: Activity, path: String, title: String) {
        val file = File(path)
        val uri = Uri.Builder().scheme("content").authority(authority(activity)).path(file.absolutePath).build()
        val send = Intent(Intent.ACTION_SEND).apply {
            type = mimeTypeFor(path)
            putExtra(Intent.EXTRA_STREAM, uri)
            putExtra(Intent.EXTRA_SUBJECT, file.nameWithoutExtension)
            clipData = ClipData.newRawUri(file.name, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        val chooser = Intent.createChooser(send, title).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        activity.runOnUiThread { activity.startActivity(chooser) }
    }

    /** Opens the system file picker; chosen files are copied into [destDir]. */
    @JvmStatic
    fun pickFiles(activity: Activity, destDir: String) {
        val intent = Intent(activity, FilePickerActivity::class.java).putExtra(FilePickerActivity.EXTRA_DEST, destDir)
        activity.runOnUiThread { activity.startActivity(intent) }
    }

    /**
     * Copies a file to Downloads/FreeVelo (no storage permission needed on Android 10+).
     * Returns the visible path ("Download/FreeVelo/…") or "" on failure.
     */
    @JvmStatic
    fun saveToDownloads(context: Context, path: String, displayName: String): String = try {
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, displayName)
            // octet-stream keeps the ".fit" name unchanged
            put(MediaStore.MediaColumns.MIME_TYPE, "application/octet-stream")
            put(MediaStore.MediaColumns.RELATIVE_PATH, "${Environment.DIRECTORY_DOWNLOADS}/FreeVelo")
        }
        val resolver = context.contentResolver
        val uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: throw IllegalStateException("insert failed")
        val out = resolver.openOutputStream(uri) ?: throw IllegalStateException("open failed")
        out.use { o -> File(path).inputStream().use { it.copyTo(o) } }
        "${Environment.DIRECTORY_DOWNLOADS}/FreeVelo/$displayName"
    } catch (e: Exception) {
        ""
    }
}
