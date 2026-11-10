using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FreeVelo.App;
using FreeVelo.Core.History;
using FreeVelo.Core.Recording;
using FreeVelo.Ride;
using FreeVelo.Shared.Api;
using FreeVelo.Shared.Strava;
using FreeVelo.UI;
using UnityEngine;

namespace FreeVelo.Net
{
    /// <summary>
    /// Strava in the app: connecting (login in the browser / Strava app, tokens collected from the FreeVelo server),
    /// automatic upload of finished rides and a queue that retries while offline, and the import of the rider's other
    /// rides with power (e.g. outdoors with a power meter) into the history. The upload logic is in
    /// <see cref="StravaSync"/> (FreeVelo.Shared, tested); this class stores its state and drives it.
    /// </summary>
    public sealed class StravaService : MonoBehaviour
    {
        private const string PrefsKey = "strava";
        private const float PollConnectS = 2f;
        private const float ConnectTimeoutS = 600f;
        private const float ProcessIntervalS = 20f;

        private StravaSync _sync;
        private string _syncServer;
        private StravaState _state;
        private float _connectStartedAt;
        private float _nextPoll;
        private float _nextProcess;
        private bool _polling;
        private Task _processing;
        private StravaReader _reader;
        private bool _importing;
        private float _nextImport;

        /// <summary>First import: this many days back (enough for fitness and form).</summary>
        private const int FirstImportDays = 42;

        /// <summary>Later imports start this many days before the last one (rides uploaded late, e.g. from a bike computer).</summary>
        private const int OverlapDays = 3;

        private const float ImportIntervalS = 6 * 3600f;
        private const int MinDurationS = 300;

        /// <summary>Activities whose data is read in one import (Strava allows 100 reads per 15 minutes); the rest come next time.</summary>
        private const int MaxReadsPerImport = 40;

        public bool Connected => _state.tokens != null && !string.IsNullOrEmpty(_state.tokens.refreshToken);

        public bool Connecting => _sync != null && _sync.Connecting;

        public string AthleteName => _state.tokens?.athleteName ?? "";

        /// <summary>Connected with permission to read activities (connections made before the import only allowed uploads).</summary>
        public bool CanImport => Connected && _state.tokens.canRead;

        public bool Importing => _importing;

        /// <summary>Last successful import (local time), or null.</summary>
        public DateTime? LastImport => _state.lastImportUtc > 0 ? DateTimeOffset.FromUnixTimeSeconds(_state.lastImportUtc).LocalDateTime : (DateTime?)null;

        public int LastImported => _state.lastImported;

        public string ImportError => _state.importError ?? "";

        public bool AutoUpload
        {
            get => _state.autoUpload;
            set
            {
                _state.autoUpload = value;
                Save();
            }
        }

        /// <summary>True while an upload is running.</summary>
        public bool Uploading => _processing != null && !_processing.IsCompleted;

        public void Initialize(RideController ride)
        {
            ride.RideSaved += OnRideSaved;
        }

        private void Awake()
        {
            try
            {
                string json = PlayerPrefs.GetString(PrefsKey, "");
                _state = string.IsNullOrEmpty(json) ? new StravaState() : JsonUtility.FromJson<StravaState>(json);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _state = new StravaState();
            }

            _state.tokens ??= new StravaTokens();
            _state.uploads ??= new System.Collections.Generic.List<StravaQueueItem>();
            _nextProcess = 5f; // after start-up: upload what is still waiting
            _nextImport = 10f; // and fetch new rides, once the history is loaded
        }

        private void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(_state));
            PlayerPrefs.Save();
        }

        private StravaSync Sync()
        {
            string server = AppSettings.ServerUrl;
            if (_sync == null || _syncServer != server)
            {
                if (string.IsNullOrEmpty(server)) throw new InvalidOperationException(L.Get("no_server"));
                // Long timeout: a sleeping free server needs up to about a minute to wake up.
                var api = new FreeVeloApi(new HttpClient { BaseAddress = new Uri(server + "/"), Timeout = TimeSpan.FromSeconds(70) }, new UnityJson());
                var uploader = new StravaUploader(new HttpClient { Timeout = TimeSpan.FromSeconds(120) });
                _sync = new StravaSync(api, uploader, _state, Save);
                _syncServer = server;
            }

            return _sync;
        }

        // ------------------------------------------------------------------ connecting

        public async void Connect()
        {
            try
            {
                string url = await Sync().BeginConnectAsync();
                _connectStartedAt = Time.unscaledTime;
                _nextPoll = Time.unscaledTime + PollConnectS;
                Application.OpenURL(url);
            }
            catch (Exception e)
            {
                Ui.Toast(Message(e));
            }
        }

        public void CancelConnect() => _sync?.CancelConnect();

        public async void Disconnect()
        {
            try
            {
                await Sync().DisconnectAsync();
                Ui.Toast(L.Get("strava_disconnected"));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private async void PollConnect()
        {
            if (_polling) return;
            _polling = true;
            try
            {
                if (await _sync.PollConnectAsync())
                {
                    Ui.Toast(string.Format(L.Get("strava_connected_as"), AthleteName));
                    _nextProcess = 0;
                    _nextImport = 0;
                }
            }
            catch (ApiException e)
            {
                Ui.Toast(e.Code == ApiErrors.StravaDenied ? L.Get("strava_denied") : Message(e));
            }
            catch (Exception e)
            {
                Debug.LogWarning("FreeVelo: Strava login check failed – " + e.Message); // try again
            }
            finally
            {
                _polling = false;
            }
        }

        // ------------------------------------------------------------------ uploads

        private void OnRideSaved(RideSummary summary)
        {
            if (!Connected || !AutoUpload) return;
            Upload(summary.FitPath, summary.Title);
        }

        /// <summary>Queues a ride for upload and starts right away.</summary>
        public void Upload(string fitPath, string name)
        {
            try
            {
                Sync().Enqueue(fitPath, name);
                _nextProcess = 0;
            }
            catch (Exception e)
            {
                Ui.Toast(Message(e));
            }
        }

        public StravaQueueItem Status(string fitPath) => _state.uploads.Find(i => i.file == fitPath);

        public static void OpenActivity(long activityId) => Application.OpenURL("https://www.strava.com/activities/" + activityId);

        private async Task ProcessAsync()
        {
            var sync = Sync();
            int doneBefore = _state.uploads.FindAll(i => i.Status == StravaItemStatus.Done).Count;
            await sync.ProcessAsync();
            int done = _state.uploads.FindAll(i => i.Status == StravaItemStatus.Done).Count;
            if (done > doneBefore) Ui.Toast(L.Get("strava_uploaded"));
            if (!sync.Connected) Ui.Toast(L.Get("strava_revoked")); // revoked on strava.com: rides wait for a new login
        }

        // ------------------------------------------------------------------ import

        /// <summary>
        /// Fetches the rider's new rides with power from Strava into the history (in the background). Skipped: rides
        /// FreeVelo uploaded itself, rides imported before, and rides already in the history (same start and length,
        /// e.g. a trainer ride also recorded on a bike computer).
        /// </summary>
        public async void Import()
        {
            if (_importing || !CanImport) return;
            _importing = true;
            _nextImport = Time.unscaledTime + ImportIntervalS;
            try
            {
                string token = await Sync().AccessTokenAsync(); // on the main thread: may save refreshed tokens
                _reader ??= new StravaReader(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, new UnityJson());
                var known = HistoryStore.Rides.ToList();
                var ftpHistory = HistoryStore.Data.ftpHistory.ToList();
                int ftpNow = AppSettings.FtpW;
                var from = _state.lastImportUtc > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(_state.lastImportUtc).UtcDateTime.AddDays(-OverlapDays)
                    : DateTime.UtcNow.AddDays(-FirstImportDays);
                string folder = HistoryStore.RidesFolder;
                var (rides, complete) = await Task.Run(() => FetchRides(_reader, token, known, ftpHistory, ftpNow, from, folder));

                foreach (var record in rides) HistoryStore.Add(record);
                if (complete) _state.lastImportUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                else _nextImport = Time.unscaledTime + (16 * 60f); // more to read after Strava's 15-minute limit
                _state.lastImported = rides.Count;
                _state.importError = "";
                if (rides.Count > 0) Ui.Toast(string.Format(L.Get("strava_imported"), rides.Count));
            }
            catch (StravaReadException e) when (e.RateLimited)
            {
                _nextImport = Time.unscaledTime + (16 * 60f);
            }
            catch (StravaReadException e) when (e.Unauthorized)
            {
                _state.importError = L.Get("strava_import_denied");
            }
            catch (ApiException e) when (e.Code == ApiErrors.StravaDenied)
            {
                Ui.Toast(L.Get("strava_revoked"));
            }
            catch (Exception e)
            {
                Debug.LogWarning("FreeVelo: Strava import failed – " + e.Message);
                _state.importError = L.Get("strava_failed");
                _nextImport = Time.unscaledTime + (30 * 60f);
            }
            finally
            {
                _importing = false;
                Save();
            }
        }

        /// <summary>Background thread: reads the new rides and writes their FIT files. Returns the records and whether all were read.</summary>
        private static async Task<(List<RideRecord> Rides, bool Complete)> FetchRides(StravaReader reader, string token, List<RideRecord> known,
            List<FtpChange> ftpHistory, int ftpNow, DateTime fromUtc, string folder)
        {
            var activities = await reader.ActivitiesAsync(token, fromUtc);
            var accepted = new List<RideRecord>(known);
            var rides = new List<RideRecord>();
            int reads = 0;
            foreach (var a in activities.OrderBy(a => a.StartUtc))
            {
                if (!a.IsRideWithPower || a.moving_time < MinDurationS || a.StartUtc == DateTime.MinValue) continue;
                if (known.Any(r => r.stravaId == a.id)) continue;
                if (!string.IsNullOrEmpty(a.external_id) && known.Any(r => r.id == a.external_id)) continue; // uploaded by FreeVelo
                if (TrainingLoad.IsDuplicate(accepted, a.StartUtc, a.moving_time)) continue;
                if (reads >= MaxReadsPerImport) return (rides, false);

                reads++;
                StravaStreams streams;
                try
                {
                    streams = await reader.StreamsAsync(token, a.id);
                }
                catch (StravaReadException e) when (e.RateLimited)
                {
                    return (rides, false); // keep what was read; the rest after the limit
                }

                var samples = new List<FitRecord>(streams.Count);
                for (int i = 0; i < streams.Count; i++)
                {
                    int? Int(double[] s) => StravaStreams.At(s, i) is double v ? (int)Math.Round(v) : (int?)null;
                    samples.Add(new FitRecord(a.StartUtc.AddSeconds(streams.Time[i]), Int(streams.Watts), Int(streams.HeartRate), Int(streams.Cadence),
                        StravaStreams.At(streams.Distance, i), StravaStreams.At(streams.Speed, i), StravaStreams.At(streams.Altitude, i)));
                }

                samples = RideAnalysis.OneHz(samples);
                if (samples.Count < MinDurationS) continue;
                string id = $"strava-{a.id}.fit";
                var record = RideAnalysis.Summarize(id, string.IsNullOrWhiteSpace(a.name) ? "Strava" : a.name, a.StartUtc, samples,
                    TrainingLoad.FtpOn(ftpHistory, a.StartUtc, ftpNow));
                record.imported = true;
                record.stravaId = a.id;
                try
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, id), FitActivityWriter.Write(RideAnalysis.ToRecording(record.title, samples)));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("FreeVelo: Strava ride not saved as FIT – " + e.Message); // the record still counts
                }

                accepted.Add(record);
                rides.Add(record);
            }

            return (rides, true);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _nextImport && CanImport && !_importing) Import();
            if (Connecting)
            {
                if (now - _connectStartedAt > ConnectTimeoutS) _sync.CancelConnect();
                else if (now >= _nextPoll)
                {
                    _nextPoll = now + PollConnectS;
                    PollConnect();
                }
            }

            if (now >= _nextProcess && !Uploading && Connected)
            {
                _nextProcess = now + ProcessIntervalS;
                try
                {
                    if (Sync().HasWork) _processing = ProcessAsync();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Back from the Strava login: check at once.
            if (hasFocus && Connecting) _nextPoll = 0;
            if (hasFocus) _nextProcess = Mathf.Min(_nextProcess, Time.unscaledTime + 1f);
        }

        private static string Message(Exception e)
        {
            if (e is ApiException api)
            {
                if (api.Code == ApiErrors.StravaNotConfigured) return L.Get("strava_not_configured");
                if (api.Code == ApiErrors.TooManyRequests) return L.Get("err_too_many");
            }

            Debug.LogException(e);
            return L.Get("strava_failed");
        }
    }
}
