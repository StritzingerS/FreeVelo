using System;
using System.IO;
using System.Net.Http;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FreeVelo.EditorTools
{
    /// <summary>
    /// Keeps the FreeVelo BLE/file plugin (freevelo-ble.aar, built by GitHub Actions, not stored in Git) up to date:
    /// - once per editor session and before every Android build it compares the local file with the
    ///   latest release on GitHub and downloads it if it differs,
    /// - menu FreeVelo → Update Android plugins forces a download.
    /// Its Kotlin dependency is declared in Plugins/Android/FreeVeloDependencies.androidlib.
    /// </summary>
    [InitializeOnLoad]
    public static class FreeVeloAndroidPlugins
    {
        private const string Folder = "Assets/Plugins/Android";
        private const string FileName = "freevelo-ble.aar";
        private const string Url = "https://github.com/StritzingerS/FreeVelo/releases/download/ble-android-latest/freevelo-ble.aar";
        private const string SessionKey = "FreeVelo.PluginChecked";

        private static string LocalPath => Path.Combine(Folder, FileName);

        static FreeVeloAndroidPlugins()
        {
            EditorApplication.delayCall += () =>
            {
                // Earlier versions downloaded kotlin-stdlib as a jar, which duplicates Unity's own copy.
                if (Directory.Exists(Folder))
                {
                    foreach (var old in Directory.GetFiles(Folder, "kotlin-stdlib-*.jar"))
                    {
                        AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
                        Debug.Log("FreeVelo: removed " + old + " (Kotlin is now a Gradle dependency)");
                    }
                }

                if (SessionState.GetBool(SessionKey, false)) return;
                SessionState.SetBool(SessionKey, true);
                UpdateIfChanged(interactive: false);
            };
        }

        [MenuItem("FreeVelo/Update Android plugins")]
        private static void ForceUpdate() => Download();

        /// <summary>Downloads the plugin if it is missing or differs from the latest release. Returns true if up to date.</summary>
        public static bool UpdateIfChanged(bool interactive)
        {
            try
            {
                long remoteSize = RemoteSize();
                bool missing = !File.Exists(LocalPath);
                if (!missing && remoteSize > 0 && new FileInfo(LocalPath).Length == remoteSize) return true;
                if (!missing && remoteSize <= 0) return true; // offline: keep the local copy
                return Download();
            }
            catch (Exception e)
            {
                Debug.LogWarning("FreeVelo: could not check the Android plugin version: " + e.Message);
                return File.Exists(LocalPath);
            }
        }

        private static long RemoteSize()
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
            using (var request = new HttpRequestMessage(HttpMethod.Head, Url))
            using (var response = client.SendAsync(request).GetAwaiter().GetResult())
            {
                return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength ?? -1 : -1;
            }
        }

        private static bool Download()
        {
            Directory.CreateDirectory(Folder);
            try
            {
                EditorUtility.DisplayProgressBar("FreeVelo", "Downloading " + FileName, 0.5f);
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                {
                    byte[] bytes = client.GetByteArrayAsync(Url).GetAwaiter().GetResult();
                    File.WriteAllBytes(LocalPath, bytes);
                    Debug.Log($"FreeVelo: downloaded {FileName} ({bytes.Length / 1024} KB)");
                }

                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"FreeVelo: could not download {Url}: {e.Message}");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Updates the plugin before every Android build, so an outdated AAR never ends up in an APK.</summary>
        private sealed class PreBuild : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                if (report.summary.platform != BuildTarget.Android) return;
                if (!UpdateIfChanged(interactive: false))
                {
                    throw new BuildFailedException("FreeVelo: freevelo-ble.aar is missing – menu FreeVelo → Update Android plugins.");
                }
            }
        }
    }
}
