package io.github.stritzingers.freevelo.files

import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.database.MatrixCursor
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.provider.OpenableColumns
import java.io.File
import java.io.FileNotFoundException

/**
 * Read-only provider so other apps (share sheet) can read FreeVelo files, e.g. FIT rides.
 * URI: content://<applicationId>.freevelo.files/<absolute file path>; only files inside the
 * app's own directories are served. Small replacement for androidx FileProvider (no dependency).
 */
class FreeVeloFileProvider : ContentProvider() {
    override fun onCreate(): Boolean = true

    private fun fileFor(uri: Uri): File {
        val ctx = context ?: throw FileNotFoundException("No context")
        val file = File(uri.path ?: throw FileNotFoundException("No path")).canonicalFile
        val roots = listOfNotNull(ctx.filesDir, ctx.cacheDir, ctx.getExternalFilesDir(null)).map { it.canonicalPath }
        if (roots.none { file.path.startsWith(it + File.separator) }) throw FileNotFoundException("Not an app file: $file")
        if (!file.isFile) throw FileNotFoundException("Missing: $file")
        return file
    }

    override fun openFile(uri: Uri, mode: String): ParcelFileDescriptor =
        ParcelFileDescriptor.open(fileFor(uri), ParcelFileDescriptor.MODE_READ_ONLY)

    override fun query(uri: Uri, projection: Array<out String>?, selection: String?, selectionArgs: Array<out String>?, sortOrder: String?): Cursor {
        val file = fileFor(uri)
        val cursor = MatrixCursor(arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE))
        cursor.addRow(arrayOf<Any>(file.name, file.length()))
        return cursor
    }

    override fun getType(uri: Uri): String = UnityFiles.mimeTypeFor(uri.path ?: "")

    override fun insert(uri: Uri, values: ContentValues?): Uri? = throw UnsupportedOperationException("read-only")

    override fun delete(uri: Uri, selection: String?, selectionArgs: Array<out String>?): Int = 0

    override fun update(uri: Uri, values: ContentValues?, selection: String?, selectionArgs: Array<out String>?): Int = 0
}
