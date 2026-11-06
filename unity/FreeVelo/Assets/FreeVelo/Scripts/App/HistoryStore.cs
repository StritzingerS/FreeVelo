using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FreeVelo.Core.History;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>
    /// The ride history on the phone (persistentDataPath/history.json): one summary per ride plus the FTP history.
    /// At the first start after the update, the FIT files already saved in persistentDataPath/rides are read once in the
    /// background and added (marked as imported).
    /// </summary>
    public static class HistoryStore
    {
        private static RideHistoryData _data;
        private static Task<List<RideRecord>> _import;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "history.json");

        public static string RidesFolder => Path.Combine(Application.persistentDataPath, "rides");

        /// <summary>Raised on the main thread when rides were added (live or imported).</summary>
        public static event Action Changed;

        public static RideHistoryData Data
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }

        public static IReadOnlyList<RideRecord> Rides => Data.rides;

        /// <summary>True while old FIT files are being read.</summary>
        public static bool Importing => _import != null && !_import.IsCompleted;

        private static void Load()
        {
            try
            {
                _data = File.Exists(FilePath) ? JsonUtility.FromJson<RideHistoryData>(File.ReadAllText(FilePath)) : null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            _data ??= new RideHistoryData();
            _data.rides ??= new List<RideRecord>();
            _data.ftpHistory ??= new List<FtpChange>();
        }

        public static void Save()
        {
            try
            {
                Data.rides.Sort((a, b) => a.startUtc.CompareTo(b.startUtc));
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Data));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Reads FIT files that are not in the history yet (in the background). Call at start.</summary>
        public static void ImportMissingRides()
        {
            if (Importing) return;
            var known = new HashSet<string>(Data.rides.Select(r => r.id));
            string folder = RidesFolder;
            int ftp = AppSettings.FtpW;
            _import = Task.Run(() =>
            {
                var found = new List<RideRecord>();
                if (!Directory.Exists(folder)) return found;
                foreach (var path in Directory.GetFiles(folder, "*.fit"))
                {
                    string name = Path.GetFileName(path);
                    if (known.Contains(name)) continue;
                    try
                    {
                        var record = RideAnalysis.FromFit(name, File.ReadAllBytes(path), ftp);
                        if (record.durationS >= 60) found.Add(record);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"FreeVelo: could not read {name} – {e.Message}");
                    }
                }

                return found;
            });
        }

        /// <summary>Call every frame: takes over the imported rides on the main thread.</summary>
        public static void Poll()
        {
            if (_import == null || !_import.IsCompleted) return;
            var task = _import;
            _import = null;
            if (task.IsFaulted || task.Result.Count == 0) return;
            foreach (var r in task.Result)
            {
                if (!Data.rides.Any(x => x.id == r.id)) Data.rides.Add(r);
            }

            Save();
            Debug.Log($"FreeVelo: {task.Result.Count} rides imported into the history");
            Changed?.Invoke();
        }

        public static void Add(RideRecord record)
        {
            Data.rides.RemoveAll(r => r.id == record.id);
            Data.rides.Add(record);
            Save();
            Changed?.Invoke();
        }

        public static RideRecord Find(string id) => Data.rides.FirstOrDefault(r => r.id == id);

        /// <summary>FTP changed: kept once per day (the last value of the day counts).</summary>
        public static void RecordFtp(int ftpW, string source)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var list = Data.ftpHistory;
            var last = list.Count > 0 ? list[list.Count - 1] : null;
            if (last != null && last.ftpW == ftpW) return;
            if (last != null && DateTimeOffset.FromUnixTimeSeconds(last.timeUtc).LocalDateTime.Date == DateTime.Now.Date && last.source == source)
            {
                last.ftpW = ftpW;
                last.timeUtc = now;
            }
            else
            {
                list.Add(new FtpChange { timeUtc = now, ftpW = ftpW, source = source });
            }

            Save();
        }

        /// <summary>Replaces everything (backup restore).</summary>
        public static void Replace(RideHistoryData data)
        {
            _data = data ?? new RideHistoryData();
            _data.rides ??= new List<RideRecord>();
            _data.ftpHistory ??= new List<FtpChange>();
            Save();
            Changed?.Invoke();
        }
    }
}
