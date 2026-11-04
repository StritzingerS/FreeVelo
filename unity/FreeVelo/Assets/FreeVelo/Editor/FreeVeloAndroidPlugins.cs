using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using UnityEditor;
using UnityEngine;

namespace FreeVelo.EditorTools
{
    /// <summary>
    /// Downloads the FreeVelo BLE library (built by GitHub Actions; not stored in Git).
    /// Runs automatically when the file is missing; menu: FreeVelo → Update Android plugins.
    /// Its Kotlin dependency is declared in Plugins/Android/FreeVeloDependencies.androidlib.
    /// </summary>
    [InitializeOnLoad]
    public static class FreeVeloAndroidPlugins
    {
        private const string Folder = "Assets/Plugins/Android";

        private static readonly (string Url, string File)[] Files =
        {
            ("https://github.com/StritzingerS/FreeVelo/releases/download/ble-android-latest/freevelo-ble.aar", "freevelo-ble.aar"),
        };

        static FreeVeloAndroidPlugins()
        {
            EditorApplication.delayCall += () =>
            {
                // Earlier versions downloaded kotlin-stdlib as a jar, which duplicates Unity's own copy.
                foreach (var old in Directory.Exists(Folder) ? Directory.GetFiles(Folder, "kotlin-stdlib-*.jar") : new string[0])
                {
                    AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
                    Debug.Log("FreeVelo: removed " + old + " (Kotlin is now a Gradle dependency)");
                }

                if (Files.Any(f => !File.Exists(Path.Combine(Folder, f.File)))) Download(onlyMissing: true);
            };
        }

        [MenuItem("FreeVelo/Update Android plugins")]
        private static void UpdateAll() => Download(onlyMissing: false);

        private static void Download(bool onlyMissing)
        {
            Directory.CreateDirectory(Folder);
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                foreach (var (url, file) in Files)
                {
                    string path = Path.Combine(Folder, file);
                    if (onlyMissing && File.Exists(path)) continue;
                    try
                    {
                        EditorUtility.DisplayProgressBar("FreeVelo", "Downloading " + file, 0.5f);
                        byte[] bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
                        File.WriteAllBytes(path, bytes);
                        Debug.Log($"FreeVelo: downloaded {file} ({bytes.Length / 1024} KB)");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"FreeVelo: could not download {url}: {e.Message}");
                    }
                    finally
                    {
                        EditorUtility.ClearProgressBar();
                    }
                }
            }

            AssetDatabase.Refresh();
        }
    }
}
