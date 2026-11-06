using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FreeVelo.Core.History;
using FreeVelo.Core.Plans;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>
    /// Backup as one ZIP file: settings, ride history, FIT files, own workouts and the training plan. Exported via the
    /// share sheet; restored by picking the ZIP in the import file picker (it lands in the import folder).
    /// Not included: the server identity and the Strava login (both are set up again on the new phone).
    /// </summary>
    public static class Backup
    {
        private const string SettingsEntry = "freevelo-settings.json";
        private const string HistoryEntry = "history.json";
        private const string PlanEntry = "plan.json";
        private const string RidesPrefix = "rides/";
        private const string WorkoutsPrefix = "workouts/";

        [Serializable]
        private class Entry
        {
            public string key = "";
            public string value = "";
        }

        [Serializable]
        private class Settings
        {
            public int version = 1;
            public string created = "";
            public List<Entry> ints = new List<Entry>();
            public List<Entry> floats = new List<Entry>();
            public List<Entry> strings = new List<Entry>();
        }

        public static bool IsBackup(string path)
        {
            if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                using var zip = ZipFile.OpenRead(path);
                return zip.GetEntry(SettingsEntry) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Writes the backup ZIP and opens the share sheet. Returns the message for a toast.</summary>
        public static string Export()
        {
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "export");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"FreeVelo-backup-{DateTime.Now:yyyy-MM-dd}.zip");
                if (File.Exists(path)) File.Delete(path);

                var settings = new Settings { created = DateTime.UtcNow.ToString("o") };
                foreach (var k in AppSettings.BackupIntKeys.Where(PlayerPrefs.HasKey)) settings.ints.Add(new Entry { key = k, value = PlayerPrefs.GetInt(k).ToString() });
                foreach (var k in AppSettings.BackupFloatKeys.Where(PlayerPrefs.HasKey)) settings.floats.Add(new Entry { key = k, value = PlayerPrefs.GetFloat(k).ToString("R", System.Globalization.CultureInfo.InvariantCulture) });
                foreach (var k in AppSettings.BackupStringKeys.Where(PlayerPrefs.HasKey)) settings.strings.Add(new Entry { key = k, value = PlayerPrefs.GetString(k) });

                using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    WriteText(zip, SettingsEntry, JsonUtility.ToJson(settings, true));
                    WriteText(zip, HistoryEntry, JsonUtility.ToJson(HistoryStore.Data));
                    if (PlanStore.Plan != null) WriteText(zip, PlanEntry, JsonUtility.ToJson(PlanStore.Plan));
                    AddFolder(zip, HistoryStore.RidesFolder, RidesPrefix, "*.fit");
                    AddFolder(zip, WorkoutStore.Library.Folder, WorkoutsPrefix, "*");
                }

                return FileActions.Share(path, L.Get("backup_export"));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return "✗ " + e.Message;
            }
        }

        /// <summary>
        /// Restores a backup: settings are overwritten, rides and workouts are added (nothing is deleted), the plan from
        /// the backup replaces the current one. Returns the message for a toast.
        /// </summary>
        public static string Restore(string path)
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var settings = JsonUtility.FromJson<Settings>(ReadText(zip, SettingsEntry));
                foreach (var e in settings.ints.Where(e => AppSettings.BackupIntKeys.Contains(e.key))) PlayerPrefs.SetInt(e.key, int.Parse(e.value));
                foreach (var e in settings.floats.Where(e => AppSettings.BackupFloatKeys.Contains(e.key))) PlayerPrefs.SetFloat(e.key, float.Parse(e.value, System.Globalization.CultureInfo.InvariantCulture));
                foreach (var e in settings.strings.Where(e => AppSettings.BackupStringKeys.Contains(e.key))) PlayerPrefs.SetString(e.key, e.value);
                PlayerPrefs.Save();
                AppSettings.ReloadAfterRestore();

                // Files: rides and own workouts are added (existing files stay).
                int rides = ExtractFolder(zip, RidesPrefix, HistoryStore.RidesFolder);
                ExtractFolder(zip, WorkoutsPrefix, WorkoutStore.Library.Folder);
                WorkoutStore.Library.Reload();

                // History: merged by ride id; FTP history merged by time.
                if (zip.GetEntry(HistoryEntry) != null)
                {
                    var restored = JsonUtility.FromJson<RideHistoryData>(ReadText(zip, HistoryEntry));
                    var merged = HistoryStore.Data;
                    foreach (var r in restored.rides ?? new List<RideRecord>())
                    {
                        if (!merged.rides.Any(x => x.id == r.id)) merged.rides.Add(r);
                    }

                    foreach (var f in restored.ftpHistory ?? new List<FtpChange>())
                    {
                        if (!merged.ftpHistory.Any(x => x.timeUtc == f.timeUtc)) merged.ftpHistory.Add(f);
                    }

                    merged.ftpHistory.Sort((a, b) => a.timeUtc.CompareTo(b.timeUtc));
                    HistoryStore.Replace(merged);
                }

                if (zip.GetEntry(PlanEntry) != null) PlanStore.Replace(JsonUtility.FromJson<TrainingPlan>(ReadText(zip, PlanEntry)));
                HistoryStore.ImportMissingRides(); // FIT files without a summary
                return "✓ " + string.Format(L.Get("backup_restored"), rides);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return "✗ " + L.Get("backup_failed") + ": " + e.Message;
            }
        }

        private static void WriteText(ZipArchive zip, string name, string text)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(text);
        }

        private static string ReadText(ZipArchive zip, string name)
        {
            var entry = zip.GetEntry(name) ?? throw new InvalidDataException("Not a FreeVelo backup");
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }

        private static void AddFolder(ZipArchive zip, string folder, string prefix, string pattern)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, pattern)) zip.CreateEntryFromFile(file, prefix + Path.GetFileName(file));
        }

        private static int ExtractFolder(ZipArchive zip, string prefix, string folder)
        {
            int count = 0;
            Directory.CreateDirectory(folder);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.Name.Length > 0))
            {
                // Only plain file names (no paths from the ZIP) – a backup can't write outside the folder.
                string target = Path.Combine(folder, Path.GetFileName(entry.Name));
                if (File.Exists(target)) continue;
                entry.ExtractToFile(target);
                count++;
            }

            return count;
        }
    }
}
