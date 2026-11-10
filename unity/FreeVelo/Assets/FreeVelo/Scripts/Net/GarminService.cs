using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FreeVelo.App;
using FreeVelo.Core.History;
using FreeVelo.Shared.Garmin;
using FreeVelo.UI;
using UnityEngine;

namespace FreeVelo.Net
{
    /// <summary>
    /// Imports the rider's own cycling activities with power from Garmin Connect (Personal API): on start, every few
    /// hours and on request. The original FIT files go into the ride history like FreeVelo's own rides (fitness, form,
    /// power curve). Rides already in the history (e.g. a trainer ride also recorded on the Garmin) are skipped.
    /// The key and token stay in a private file on the phone (garmin.json), not in backups.
    /// </summary>
    public sealed class GarminService : MonoBehaviour
    {
        /// <summary>First sync: this many days back (enough for fitness and form to be right).</summary>
        private const int FirstSyncDays = 42;

        /// <summary>Later syncs start this many days before the last one (late uploads from the device).</summary>
        private const int OverlapDays = 3;

        private const float AutoSyncHours = 6f;
        private const int MinDurationS = 300;

        [Serializable]
        private class GarminState
        {
            public string apiKey = "";
            public string token = "";
            public long tokenExpiresUtc;
            public long lastSyncUtc;
            public string lastError = "";
            public int lastImported;
        }

        private GarminState _state = new GarminState();
        private GarminPersonalApi _api;
        private Task<SyncResult> _sync;
        private float _nextAutoSync;

        private sealed class SyncResult
        {
            public readonly List<(RideRecord Record, byte[] Fit)> Rides = new List<(RideRecord, byte[])>();
            public string Token;
            public DateTime TokenExpiresUtc;
            public string Error;
            public bool KeyRejected;
        }

        private static string StatePath => Path.Combine(Application.persistentDataPath, "garmin.json");

        public bool Configured => !string.IsNullOrEmpty(_state.apiKey);

        public bool Syncing => _sync != null && !_sync.IsCompleted;

        /// <summary>Last successful sync (local time), or null.</summary>
        public DateTime? LastSync => _state.lastSyncUtc > 0 ? DateTimeOffset.FromUnixTimeSeconds(_state.lastSyncUtc).LocalDateTime : (DateTime?)null;

        public string LastError => _state.lastError;

        public int LastImported => _state.lastImported;

        private void Awake()
        {
            _api = new GarminPersonalApi(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, new UnityJson());
            try
            {
                if (File.Exists(StatePath)) _state = JsonUtility.FromJson<GarminState>(File.ReadAllText(StatePath)) ?? new GarminState();
            }
            catch (Exception e)
            {
                Debug.LogWarning("FreeVelo: Garmin state not readable – " + e.Message);
            }

            _nextAutoSync = 8f; // shortly after start, when the history is loaded
        }

        /// <summary>Stores the personal API key (from connect.garmin.com) and syncs right away.</summary>
        public void SetKey(string key)
        {
            _state = new GarminState { apiKey = (key ?? string.Empty).Trim() };
            Save();
            if (Configured) Sync();
        }

        public void RemoveKey()
        {
            _state = new GarminState();
            Save();
        }

        /// <summary>Fetches new rides in the background (no-op while a sync runs).</summary>
        public void Sync()
        {
            if (!Configured || Syncing) return;
            var state = _state;
            var known = HistoryStore.Rides.ToList();
            var ftpHistory = HistoryStore.Data.ftpHistory.ToList();
            int ftpNow = AppSettings.FtpW;
            var today = DateTime.Now.Date;
            var from = state.lastSyncUtc > 0
                ? DateTimeOffset.FromUnixTimeSeconds(state.lastSyncUtc).LocalDateTime.Date.AddDays(-OverlapDays)
                : today.AddDays(-FirstSyncDays);
            _sync = Task.Run(() => RunSync(state, known, ftpHistory, ftpNow, from, today.AddDays(1)));
        }

        private async Task<SyncResult> RunSync(GarminState state, List<RideRecord> known, List<FtpChange> ftpHistory, int ftpNow, DateTime from, DateTime to)
        {
            var result = new SyncResult { Token = state.token, TokenExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(state.tokenExpiresUtc).UtcDateTime };
            try
            {
                // A new token when there is none or it expires within the hour (they last 24 h).
                if (string.IsNullOrEmpty(result.Token) || result.TokenExpiresUtc < DateTime.UtcNow.AddHours(1))
                {
                    (result.Token, result.TokenExpiresUtc) = await _api.ObtainTokenAsync(state.apiKey);
                }

                var activities = await _api.SearchCyclingAsync(result.Token, from, to);
                var accepted = new List<RideRecord>(known);
                foreach (var a in activities.OrderBy(a => a.StartUtc))
                {
                    if (!a.HasPower || a.duration < MinDurationS || a.StartUtc == DateTime.MinValue) continue;
                    if (known.Any(r => r.garminId == a.activityId)) continue;
                    if (TrainingLoad.IsDuplicate(accepted, a.StartUtc, a.duration)) continue; // e.g. a FreeVelo trainer ride

                    byte[] fit = await _api.DownloadFitAsync(result.Token, a.activityId);
                    if (fit == null) continue;
                    int ftp = TrainingLoad.FtpOn(ftpHistory, a.StartUtc, ftpNow);
                    var record = RideAnalysis.FromFit($"garmin-{a.activityId}.fit", fit, ftp);
                    if (record.durationS < MinDurationS) continue;
                    record.title = string.IsNullOrWhiteSpace(a.activityName) ? "Garmin" : a.activityName;
                    record.garminId = a.activityId;
                    accepted.Add(record);
                    result.Rides.Add((record, fit));
                }
            }
            catch (GarminKeyException e)
            {
                result.KeyRejected = true;
                result.Error = e.Message;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }

            return result;
        }

        private void Update()
        {
            if (Configured && !Syncing && Time.unscaledTime >= _nextAutoSync)
            {
                _nextAutoSync = Time.unscaledTime + (AutoSyncHours * 3600f);
                Sync();
            }

            if (_sync == null || !_sync.IsCompleted) return;
            var task = _sync;
            _sync = null;
            var result = task.IsFaulted ? new SyncResult { Error = task.Exception?.GetBaseException().Message } : task.Result;

            if (result.KeyRejected)
            {
                // An expired or deleted key: the token is useless too; the settings ask for a new key.
                _state.token = string.Empty;
                _state.tokenExpiresUtc = 0;
                _state.lastError = L.Get("garmin_key_rejected");
            }
            else if (!string.IsNullOrEmpty(result.Error))
            {
                _state.lastError = L.Get("garmin_sync_failed") + " " + result.Error;
            }
            else
            {
                _state.token = result.Token;
                _state.tokenExpiresUtc = new DateTimeOffset(DateTime.SpecifyKind(result.TokenExpiresUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
                _state.lastSyncUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _state.lastError = string.Empty;
                _state.lastImported = result.Rides.Count;
                foreach (var (record, fit) in result.Rides)
                {
                    try
                    {
                        Directory.CreateDirectory(HistoryStore.RidesFolder);
                        File.WriteAllBytes(Path.Combine(HistoryStore.RidesFolder, record.id), fit); // for the ride detail
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("FreeVelo: Garmin FIT not saved – " + e.Message);
                    }

                    HistoryStore.Add(record);
                }

                if (result.Rides.Count > 0) Ui.Toast(string.Format(L.Get("garmin_imported"), result.Rides.Count));
            }

            Save();
        }

        private void Save()
        {
            try
            {
                File.WriteAllText(StatePath, JsonUtility.ToJson(_state));
            }
            catch (Exception e)
            {
                Debug.LogWarning("FreeVelo: Garmin state not saved – " + e.Message);
            }
        }
    }
}
