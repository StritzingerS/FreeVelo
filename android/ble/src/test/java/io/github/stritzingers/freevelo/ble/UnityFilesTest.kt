package io.github.stritzingers.freevelo.ble

import io.github.stritzingers.freevelo.files.UnityFiles
import org.junit.Assert.assertEquals
import org.junit.Test

class UnityFilesTest {
    @Test
    fun mimeTypes() {
        assertEquals("application/vnd.ant.fit", UnityFiles.mimeTypeFor("/data/x/rides/2026-11-04 4x4.FIT"))
        assertEquals("text/csv", UnityFiles.mimeTypeFor("ble.csv"))
        assertEquals("application/octet-stream", UnityFiles.mimeTypeFor("noextension"))
    }
}
