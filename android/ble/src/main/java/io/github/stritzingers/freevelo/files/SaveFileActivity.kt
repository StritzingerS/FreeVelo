package io.github.stritzingers.freevelo.files

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import java.io.File

/**
 * Invisible activity that opens the system "save as" dialog (Downloads preselected by most phones) for a file of the
 * app (EXTRA_SOURCE) and copies it to the place the user picks. Works on every phone, unlike writing to
 * MediaStore Downloads directly, which failed silently on some (OnePlus, Xiaomi).
 */
class SaveFileActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (savedInstanceState == null) {
            val source = File(intent.getStringExtra(EXTRA_SOURCE) ?: "")
            val create = Intent(Intent.ACTION_CREATE_DOCUMENT).apply {
                addCategory(Intent.CATEGORY_OPENABLE)
                type = UnityFiles.mimeTypeFor(source.name)
                putExtra(Intent.EXTRA_TITLE, source.name)
            }
            @Suppress("DEPRECATION")
            startActivityForResult(create, REQUEST)
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        @Suppress("DEPRECATION")
        super.onActivityResult(requestCode, resultCode, data)
        val uri = data?.data
        if (requestCode == REQUEST && resultCode == RESULT_OK && uri != null) {
            val source = File(intent.getStringExtra(EXTRA_SOURCE) ?: "")
            val message = try {
                contentResolver.openOutputStream(uri, "w")?.use { out -> source.inputStream().use { it.copyTo(out) } }
                    ?: throw IllegalStateException("cannot open the target")
                "✓ ${source.name}"
            } catch (e: Exception) {
                "✗ ${e.message ?: e.javaClass.simpleName}"
            }
            Toast.makeText(this, message, Toast.LENGTH_LONG).show()
        }
        finish()
    }

    companion object {
        const val EXTRA_SOURCE = "source"
        private const val REQUEST = 4712
    }
}
