using System.IO;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>Share / save files (Android share sheet and Downloads; in the editor: show in Explorer).</summary>
    public static class FileActions
    {
        private const string FilesClass = "io.github.stritzingers.freevelo.files.UnityFiles";

        /// <summary>Opens the share sheet (e-mail, Drive, messenger, …).</summary>
        public static void Share(string path, string title)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var files = new AndroidJavaClass(FilesClass))
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                files.CallStatic("share", player.GetStatic<AndroidJavaObject>("currentActivity"), path, title);
            }
#elif UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(path);
#endif
        }

        /// <summary>Copies the file to Downloads/FreeVelo; returns a message for the user.</summary>
        public static string SaveToDownloads(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "–";
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var files = new AndroidJavaClass(FilesClass))
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                string saved = files.CallStatic<string>("saveToDownloads", player.GetStatic<AndroidJavaObject>("currentActivity"), path, Path.GetFileName(path));
                return string.IsNullOrEmpty(saved) ? "✗" : "✓ " + saved;
            }
#else
            UnityEditor.EditorUtility.RevealInFinder(path);
            return path;
#endif
        }
    }
}
