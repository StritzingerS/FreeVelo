package io.github.stritzingers.freevelo.files

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.provider.OpenableColumns
import java.io.File

/**
 * Invisible activity that opens the system file picker and copies the chosen files into a folder of the
 * app (EXTRA_DEST). Unity scans that folder when it regains focus (no callback needed).
 */
class FilePickerActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (savedInstanceState == null) {
            val pick = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
                addCategory(Intent.CATEGORY_OPENABLE)
                type = "*/*"
                putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true)
            }
            @Suppress("DEPRECATION")
            startActivityForResult(pick, REQUEST)
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        @Suppress("DEPRECATION")
        super.onActivityResult(requestCode, resultCode, data)
        val dest = File(intent.getStringExtra(EXTRA_DEST) ?: filesDir.path)
        dest.mkdirs()
        if (requestCode == REQUEST && resultCode == RESULT_OK && data != null) {
            val uris = mutableListOf<Uri>()
            val clip = data.clipData
            if (clip != null) {
                for (i in 0 until clip.itemCount) uris.add(clip.getItemAt(i).uri)
            } else {
                data.data?.let { uris.add(it) }
            }
            for (uri in uris) {
                try {
                    copy(uri, dest)
                } catch (e: Exception) {
                    File(dest, "import-error-${System.currentTimeMillis()}.txt").writeText("$uri: ${e.message}")
                }
            }
        }
        finish()
    }

    private fun copy(uri: Uri, dest: File) {
        val name = (displayName(uri) ?: "import-${System.currentTimeMillis()}").replace('/', '_')
        contentResolver.openInputStream(uri)?.use { input ->
            File(dest, name).outputStream().use { input.copyTo(it) }
        }
    }

    private fun displayName(uri: Uri): String? =
        contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
            if (c.moveToFirst()) c.getString(0) else null
        }

    companion object {
        const val EXTRA_DEST = "dest"
        private const val REQUEST = 4711
    }
}
