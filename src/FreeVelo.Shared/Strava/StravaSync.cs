using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FreeVelo.Shared.Api;

namespace FreeVelo.Shared.Strava
{
    public enum StravaItemStatus
    {
        Waiting = 0,
        Processing = 1,
        Done = 2,
        Failed = 3,
    }

    /// <summary>A ride to upload (persisted on the phone).</summary>
    [Serializable]
    public class StravaQueueItem
    {
        /// <summary>Full path of the FIT file.</summary>
        public string file = "";
        public string name = "";

        /// <summary><see cref="StravaItemStatus"/> as int (JsonUtility).</summary>
        public int status;
        public long uploadId;
        public long activityId;
        public string error = "";
        public int attempts;

        /// <summary>Not before this time (Unix seconds) – back-off after network errors.</summary>
        public long retryAfter;

        public StravaItemStatus Status
        {
            get => (StravaItemStatus)status;
            set => status = (int)value;
        }
    }

    /// <summary>Everything the app keeps about Strava (stored as JSON on the phone).</summary>
    [Serializable]
    public class StravaState
    {
        public StravaTokens tokens = new StravaTokens();
        public bool autoUpload = true;
        public List<StravaQueueItem> uploads = new List<StravaQueueItem>();

        /// <summary>Last successful import of the rider's rides (Unix seconds, 0 = never).</summary>
        public long lastImportUtc;
        public int lastImported;
        public string importError = "";
    }

    /// <summary>
    /// The app's Strava logic: connecting (through the FreeVelo server, which holds the client secret), keeping the
    /// access token fresh, and an upload queue that survives app restarts and offline periods.
    /// Not thread-safe: call from one thread (Unity main thread).
    /// </summary>
    public sealed class StravaSync
    {
        /// <summary>Uploads remembered in the list (older finished ones are dropped).</summary>
        public const int MaxItems = 30;

        private static readonly TimeSpan[] Backoff =
        {
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1),
        };

        private readonly FreeVeloApi _server;
        private readonly StravaUploader _strava;
        private readonly Action _save;
        private readonly Func<DateTime> _now;
        private string? _loginState;
        private bool _processing;

        public StravaSync(FreeVeloApi server, StravaUploader strava, StravaState state, Action save, Func<DateTime>? utcNow = null)
        {
            _server = server;
            _strava = strava;
            State = state;
            _save = save;
            _now = utcNow ?? (() => DateTime.UtcNow);
        }

        public StravaState State { get; }

        public bool Connected => !string.IsNullOrEmpty(State.tokens?.refreshToken);

        public string AthleteName => State.tokens?.athleteName ?? "";

        /// <summary>Connected with permission to read activities (older connections only allowed uploads).</summary>
        public bool CanRead => Connected && State.tokens.canRead;

        /// <summary>A login was started and not yet finished.</summary>
        public bool Connecting => _loginState != null;

        /// <summary>Reads a FIT file (replaceable in tests).</summary>
        public Func<string, byte[]?> ReadFile { get; set; } = path => File.Exists(path) ? File.ReadAllBytes(path) : null;

        // ------------------------------------------------------------------ connecting

        /// <summary>Starts a login; open the returned page in the browser / Strava app.</summary>
        public async Task<string> BeginConnectAsync(CancellationToken ct = default)
        {
            string state = FreeVeloApi.NewStravaState();
            string url = await _server.StravaStartAsync(state, ct).ConfigureAwait(true);
            _loginState = state;
            return url;
        }

        /// <summary>
        /// Asks the server whether the rider approved: true when connected now, false while waiting. Throws
        /// <see cref="ApiException"/> when declined or expired (the login is then over).
        /// </summary>
        public async Task<bool> PollConnectAsync(CancellationToken ct = default)
        {
            if (_loginState == null) return Connected;
            try
            {
                var tokens = await _server.StravaClaimAsync(_loginState, ct).ConfigureAwait(true);
                if (tokens == null || string.IsNullOrEmpty(tokens.refreshToken)) return false;
                _loginState = null;
                State.tokens = tokens;
                _save();
                return true;
            }
            catch (ApiException)
            {
                _loginState = null;
                throw;
            }
        }

        public void CancelConnect() => _loginState = null;

        /// <summary>Forgets the tokens and revokes FreeVelo's access on Strava (best effort).</summary>
        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            string token = State.tokens?.accessToken ?? "";
            State.tokens = new StravaTokens();
            _save();
            if (token.Length > 0) await _strava.DeauthorizeAsync(token, ct).ConfigureAwait(true);
        }

        /// <summary>A valid access token (refreshed through the server when it expires within 5 minutes).</summary>
        public async Task<string> AccessTokenAsync(CancellationToken ct = default, bool forceRefresh = false)
        {
            var tokens = State.tokens;
            if (!Connected) throw new InvalidOperationException("Strava not connected");
            long now = Unix(_now());
            if (!forceRefresh && tokens.expiresAt - now > 300 && tokens.accessToken.Length > 0) return tokens.accessToken;

            StravaTokens fresh;
            try
            {
                fresh = await _server.StravaRefreshAsync(tokens.refreshToken, ct).ConfigureAwait(true);
            }
            catch (ApiException e) when (e.Code == ApiErrors.StravaDenied)
            {
                // Revoked on strava.com (or the app was deleted there): the rider has to connect again.
                State.tokens = new StravaTokens();
                _save();
                throw;
            }

            fresh.athleteId = tokens.athleteId;
            fresh.athleteName = tokens.athleteName;
            fresh.canRead = tokens.canRead;
            State.tokens = fresh;
            _save();
            return fresh.accessToken;
        }

        // ------------------------------------------------------------------ uploads

        public StravaQueueItem? Find(string file) => State.uploads.FirstOrDefault(i => i.file == file);

        /// <summary>Adds a ride to the queue (again, if it failed before). Returns the item.</summary>
        public StravaQueueItem Enqueue(string file, string name)
        {
            var item = Find(file);
            if (item == null)
            {
                item = new StravaQueueItem { file = file, name = name };
                State.uploads.Add(item);
            }
            else if (item.Status == StravaItemStatus.Failed)
            {
                item.Status = StravaItemStatus.Waiting;
                item.error = "";
                item.attempts = 0;
            }

            item.retryAfter = 0;
            Trim();
            _save();
            return item;
        }

        /// <summary>Something to do now (and connected)?</summary>
        public bool HasWork => Connected && State.uploads.Any(Due);

        /// <summary>Uploads the waiting rides whose time has come. Safe to call often (does nothing while running).</summary>
        public async Task ProcessAsync(CancellationToken ct = default)
        {
            if (_processing || !Connected) return;
            _processing = true;
            try
            {
                foreach (var item in State.uploads.Where(Due).ToList())
                {
                    if (!Connected) break;
                    await ProcessItem(item, ct).ConfigureAwait(true);
                    _save();
                }
            }
            finally
            {
                _processing = false;
            }
        }

        private bool Due(StravaQueueItem item) =>
            (item.Status == StravaItemStatus.Waiting || item.Status == StravaItemStatus.Processing) && item.retryAfter <= Unix(_now());

        private async Task ProcessItem(StravaQueueItem item, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string token;
                try
                {
                    token = await AccessTokenAsync(ct, forceRefresh: attempt > 0).ConfigureAwait(true);
                }
                catch (ApiException e) when (e.Code == ApiErrors.StravaDenied)
                {
                    return; // disconnected: the item stays in the queue for after reconnecting
                }
                catch (Exception e) when (e is ApiException || e is System.Net.Http.HttpRequestException || e is TaskCanceledException)
                {
                    RetryLater(item, e.Message);
                    return;
                }

                StravaUploadResult result;
                if (item.Status == StravaItemStatus.Processing && item.uploadId > 0)
                {
                    result = await _strava.StatusAsync(token, item.uploadId, ct).ConfigureAwait(true);
                }
                else
                {
                    var bytes = ReadFile(item.file);
                    if (bytes == null)
                    {
                        item.Status = StravaItemStatus.Failed;
                        item.error = "FIT file not found";
                        return;
                    }

                    result = await _strava.UploadAsync(token, bytes, Path.GetFileName(item.file), item.name, ct).ConfigureAwait(true);
                }

                switch (result.Outcome)
                {
                    case StravaUploadOutcome.Done:
                        item.Status = StravaItemStatus.Done;
                        item.activityId = result.ActivityId;
                        item.error = "";
                        return;
                    case StravaUploadOutcome.Processing:
                        item.Status = StravaItemStatus.Processing;
                        item.uploadId = result.UploadId;
                        item.retryAfter = Unix(_now()) + 30;
                        return;
                    case StravaUploadOutcome.Rejected:
                        item.Status = StravaItemStatus.Failed;
                        item.error = result.Error;
                        return;
                    case StravaUploadOutcome.Unauthorized:
                        continue; // refresh the token once and try again
                    default:
                        RetryLater(item, result.Error);
                        return;
                }
            }

            RetryLater(item, "Strava refused the access token");
        }

        private void RetryLater(StravaQueueItem item, string error)
        {
            item.error = error;
            item.retryAfter = Unix(_now()) + (long)Backoff[Math.Min(item.attempts, Backoff.Length - 1)].TotalSeconds;
            item.attempts++;
        }

        private void Trim()
        {
            while (State.uploads.Count > MaxItems)
            {
                var old = State.uploads.FirstOrDefault(i => i.Status == StravaItemStatus.Done || i.Status == StravaItemStatus.Failed);
                if (old == null) break;
                State.uploads.Remove(old);
            }
        }

        private static long Unix(DateTime utc) => (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }
}
