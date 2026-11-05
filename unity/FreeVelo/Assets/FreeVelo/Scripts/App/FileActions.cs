using System;
using System.IO;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>
    /// Share / save files (Android share sheet and Downloads; in the editor: show in Explorer).
    /// Both return a message for the user, including the reason when something fails.
    /// </summary>
    public static class FileActions
    {
        private const string FilesClass = "io.github.stritzingers.freevelo.files.UnityFiles";

        /// <summary>Opens the share sheet (e-mail, Drive, messenger, …).</summary>
        public static string Share(string path, string title)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "✗ " + L.Get("file_missing");
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var files = new AndroidJavaClass(FilesClass))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    files.CallStatic("share", player.GetStatic<AndroidJavaObject>("currentActivity"), path, title);
                }

                return null;
#elif UNITY_EDITOR
                UnityEditor.EditorUtility.RevealInFinder(path);
                return path;
#else
                return path;
#endif
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return Explain(e);
            }
        }

        /// <summary>Copies the file to Downloads/FreeVelo; returns a message for the user.</summary>
        public static string SaveToDownloads(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "✗ " + L.Get("file_missing");
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var files = new AndroidJavaClass(FilesClass))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    string saved = files.CallStatic<string>("saveToDownloads", player.GetStatic<AndroidJavaObject>("currentActivity"), path, Path.GetFileName(path));
                    return string.IsNullOrEmpty(saved) ? "✗ " + L.Get("save_failed") : "✓ " + saved;
                }
#elif UNITY_EDITOR
                UnityEditor.EditorUtility.RevealInFinder(path);
                return path;
#else
                return path;
#endif
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return Explain(e);
            }
        }

        private static string Explain(Exception e)
        {
            string text = e.Message ?? e.GetType().Name;
            // An outdated freevelo-ble.aar does not contain the file helpers yet.
            if (text.Contains("ClassNotFound") || text.Contains("UnityFiles") || text.Contains("NoSuchMethod"))
            {
                return "✗ " + L.Get("plugin_outdated");
            }

            return "✗ " + text;
        }
    }
}
