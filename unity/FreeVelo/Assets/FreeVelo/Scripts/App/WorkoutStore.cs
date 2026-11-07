using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FreeVelo.Core.Workouts;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>
    /// The user's own workouts (persistentDataPath/workouts) and the import inbox (persistentDataPath/import),
    /// which the Android file picker fills. <see cref="ProcessImports"/> runs at start and when the app regains focus.
    /// </summary>
    public static class WorkoutStore
    {
        private static WorkoutLibrary _library;

        public static WorkoutLibrary Library
        {
            get
            {
                if (_library == null)
                {
                    _library = new WorkoutLibrary(Path.Combine(Application.persistentDataPath, "workouts"));
                    _library.Reload();
                }

                return _library;
            }
        }

        public static string ImportFolder => Path.Combine(Application.persistentDataPath, "import");

        /// <summary>Result of the last import/export action, shown on the workouts screen.</summary>
        public static string LastMessage { get; set; }

        /// <summary>Imports all files waiting in the import folder (.zwo/.erg/.mrc); others are reported.</summary>
        public static void ProcessImports()
        {
            if (!Directory.Exists(ImportFolder)) return;
            var imported = new List<string>();
            var failed = new List<string>();
            foreach (var path in Directory.GetFiles(ImportFolder))
            {
                string name = Path.GetFileName(path);
                try
                {
                    if (name.StartsWith("import-error-", StringComparison.Ordinal))
                    {
                        failed.Add(File.ReadAllText(path));
                    }
                    else if (Backup.IsBackup(path))
                    {
                        // A FreeVelo backup picked in the same file picker
                        string message = Backup.Restore(path);
                        FreeVelo.UI.Ui.Toast(message, 6f);
                        if (message.StartsWith("✓", StringComparison.Ordinal)) imported.Add(L.Get("backup")); else failed.Add(message);
                    }
                    else
                    {
                        var workout = Library.Import(name, File.ReadAllText(path, Encoding.UTF8));
                        imported.Add(workout.Name.Get(AppSettings.Language));
                    }
                }
                catch (Exception e)
                {
                    failed.Add($"{name}: {e.Message}");
                }
                finally
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            if (imported.Count + failed.Count == 0) return;
            var sb = new StringBuilder();
            if (imported.Count > 0) sb.Append("✓ ").Append(L.Get("imported")).Append(": ").Append(string.Join(", ", imported));
            if (failed.Count > 0)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("✗ ").Append(string.Join("\n✗ ", failed));
            }

            LastMessage = sb.ToString();
        }

        /// <summary>Opens the file picker (Android) or a file dialog (editor) to import workouts.</summary>
        public static void PickFiles()
        {
            Directory.CreateDirectory(ImportFolder);
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var files = new AndroidJavaClass("io.github.stritzingers.freevelo.files.UnityFiles"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    files.CallStatic("pickFiles", player.GetStatic<AndroidJavaObject>("currentActivity"), ImportFolder);
                }
#elif UNITY_EDITOR
                string path = UnityEditor.EditorUtility.OpenFilePanel("Import workout or backup", string.Empty, "zwo,erg,mrc,zip");
                if (!string.IsNullOrEmpty(path))
                {
                    File.Copy(path, Path.Combine(ImportFolder, Path.GetFileName(path)), true);
                    ProcessImports();
                }
#endif
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                LastMessage = "✗ " + (e.Message.Contains("ClassNotFound") ? L.Get("plugin_outdated") : e.Message);
            }
        }

        /// <summary>Shares a workout as standard ZWO (Zwift, TrainerRoad, intervals.icu, …).</summary>
        public static void Share(Workout workout)
        {
            string folder = Path.Combine(Application.persistentDataPath, "export");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, (string.IsNullOrEmpty(workout.Id) ? "workout" : workout.Id) + ".zwo");
            File.WriteAllText(path, WorkoutFiles.ToZwo(workout, strict: true, ftpW: AppSettings.FtpW), new UTF8Encoding(false));
            LastMessage = FileActions.Share(path, L.Get("share_workout"));
        }

        public static void Delete(Workout workout)
        {
            Library.Delete(workout.Id);
            LastMessage = "✓ " + L.Get("deleted") + ": " + workout.Name.Get(AppSettings.Language);
        }
    }
}
