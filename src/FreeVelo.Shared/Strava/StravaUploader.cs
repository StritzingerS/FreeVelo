using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FreeVelo.Shared.Strava
{
    public enum StravaUploadOutcome
    {
        /// <summary>Strava created the activity (or already had it).</summary>
        Done,

        /// <summary>Uploaded, Strava is still processing it: ask again later with the upload id.</summary>
        Processing,

        /// <summary>Strava rejected the file (corrupt, too short, …) – retrying won't help.</summary>
        Rejected,

        /// <summary>Access token expired or revoked.</summary>
        Unauthorized,

        /// <summary>No network, Strava down or rate limit – try again later.</summary>
        TryLater,
    }

    public sealed class StravaUploadResult
    {
        public StravaUploadOutcome Outcome { get; set; }
        public long UploadId { get; set; }
        public long ActivityId { get; set; }
        public string Error { get; set; } = "";

        public override string ToString() => $"{Outcome} upload={UploadId} activity={ActivityId} {Error}";
    }

    /// <summary>
    /// Uploads FIT files directly from the phone to Strava (API v3 uploads) with the rider's own access token.
    /// Strava's answers are read with a few regular expressions, so no JSON library is needed (Unity / IL2CPP).
    /// </summary>
    public sealed class StravaUploader
    {
        public const string ApiBase = "https://www.strava.com/api/v3";

        private readonly HttpClient _http;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public StravaUploader(HttpClient http, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _http = http;
            _delay = delay ?? Task.Delay;
        }

        /// <summary>How long <see cref="UploadAsync"/> waits for Strava to process the file before returning Processing.</summary>
        public TimeSpan ProcessingWait { get; set; } = TimeSpan.FromSeconds(40);

        /// <summary>Uploads a FIT file and waits a little for the activity.</summary>
        public async Task<StravaUploadResult> UploadAsync(string accessToken, byte[] fit, string fileName, string name, CancellationToken ct = default)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(fit);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", fileName);
            form.Add(new StringContent("fit"), "data_type");
            form.Add(new StringContent("1"), "trainer");
            form.Add(new StringContent(fileName), "external_id");
            if (!string.IsNullOrWhiteSpace(name)) form.Add(new StringContent(name), "name");

            var result = await Send(HttpMethod.Post, ApiBase + "/uploads", accessToken, form, ct).ConfigureAwait(false);
            if (result.Outcome != StravaUploadOutcome.Processing) return result;
            return await WaitAsync(accessToken, result.UploadId, ct).ConfigureAwait(false);
        }

        /// <summary>Checks an upload that was still processing.</summary>
        public Task<StravaUploadResult> StatusAsync(string accessToken, long uploadId, CancellationToken ct = default) =>
            Send(HttpMethod.Get, $"{ApiBase}/uploads/{uploadId}", accessToken, null, ct);

        /// <summary>Revokes FreeVelo's access on Strava (when the rider disconnects). Errors are ignored.</summary>
        public async Task DeauthorizeAsync(string accessToken, CancellationToken ct = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.strava.com/oauth/deauthorize");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new FormUrlEncodedContent(new[] { new System.Collections.Generic.KeyValuePair<string, string>("access_token", accessToken) });
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                // Offline: the tokens are deleted on the phone anyway.
            }
        }

        private async Task<StravaUploadResult> WaitAsync(string accessToken, long uploadId, CancellationToken ct)
        {
            var waited = TimeSpan.Zero;
            var step = TimeSpan.FromSeconds(2);
            var result = new StravaUploadResult { Outcome = StravaUploadOutcome.Processing, UploadId = uploadId };
            while (waited < ProcessingWait)
            {
                await _delay(step, ct).ConfigureAwait(false);
                waited += step;
                result = await StatusAsync(accessToken, uploadId, ct).ConfigureAwait(false);
                if (result.Outcome != StravaUploadOutcome.Processing) return result;
            }

            return result;
        }

        private async Task<StravaUploadResult> Send(HttpMethod method, string url, string accessToken, HttpContent? content, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url) { Content = content };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                string body = response.Content != null ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : "";
                return Interpret(response.StatusCode, body);
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
            {
                return new StravaUploadResult { Outcome = StravaUploadOutcome.TryLater, Error = e.Message };
            }
        }

        /// <summary>Turns Strava's answer into an outcome (public for tests).</summary>
        public static StravaUploadResult Interpret(HttpStatusCode status, string body)
        {
            var result = new StravaUploadResult
            {
                UploadId = Long(body, "id") ?? 0,
                ActivityId = Long(body, "activity_id") ?? 0,
                Error = Text(body, "error") ?? "",
            };

            if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
            {
                result.Outcome = StravaUploadOutcome.Unauthorized;
                return result;
            }

            if ((int)status == 429 || (int)status >= 500)
            {
                result.Outcome = StravaUploadOutcome.TryLater;
                if (result.Error.Length == 0) result.Error = $"HTTP {(int)status}";
                return result;
            }

            if (result.Error.Length > 0)
            {
                // "… duplicate of <a href='/activities/123'>…" – Strava already has this ride.
                var duplicate = Regex.Match(result.Error, @"duplicate of.*?/activities/(\d+)", RegexOptions.IgnoreCase);
                if (duplicate.Success)
                {
                    result.Outcome = StravaUploadOutcome.Done;
                    result.ActivityId = long.Parse(duplicate.Groups[1].Value);
                    return result;
                }

                result.Outcome = StravaUploadOutcome.Rejected;
                result.Error = Regex.Replace(result.Error, "<[^>]+>", "");
                return result;
            }

            if (!((int)status >= 200 && (int)status < 300))
            {
                result.Outcome = StravaUploadOutcome.Rejected;
                result.Error = Text(body, "message") ?? $"HTTP {(int)status}";
                return result;
            }

            result.Outcome = result.ActivityId > 0 ? StravaUploadOutcome.Done : StravaUploadOutcome.Processing;
            return result;
        }

        private static long? Long(string json, string field)
        {
            var m = Regex.Match(json, "\"" + field + "\"\\s*:\\s*(\\d+)");
            return m.Success ? long.Parse(m.Groups[1].Value) : (long?)null;
        }

        private static string? Text(string json, string field)
        {
            var m = Regex.Match(json, "\"" + field + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }
    }
}
