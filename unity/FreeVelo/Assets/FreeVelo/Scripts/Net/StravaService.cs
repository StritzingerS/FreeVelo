using System;
using System.Net.Http;
using System.Threading.Tasks;
using FreeVelo.App;
using FreeVelo.Ride;
using FreeVelo.Shared.Api;
using FreeVelo.Shared.Strava;
using FreeVelo.UI;
using UnityEngine;

namespace FreeVelo.Net
{
    /// <summary>
    /// Strava in the app: connecting (login in the browser / Strava app, tokens collected from the FreeVelo server),
    /// automatic upload of finished rides and a queue that retries while offline. The logic is in
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

        public bool Connected => _state.tokens != null && !string.IsNullOrEmpty(_state.tokens.refreshToken);

        public bool Connecting => _sync != null && _sync.Connecting;

        public string AthleteName => _state.tokens?.athleteName ?? "";

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

        private void Update()
        {
            float now = Time.unscaledTime;
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
