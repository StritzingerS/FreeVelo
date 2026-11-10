using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FreeVelo.Shared.Api;

namespace FreeVelo.Shared.Garmin
{
    /// <summary>One ride from the Garmin activity search (public fields for Unity's JsonUtility).</summary>
    [Serializable]
    public class GarminActivity
    {
        public long activityId;
        public string activityName = "";

        /// <summary>Start in UTC, "yyyy-MM-dd HH:mm:ss".</summary>
        public string startTimeGMT = "";

        /// <summary>Timer duration in seconds.</summary>
        public double duration;
        public double movingDuration;
        public double distance;
        public double elevationGain;
        public double avgPower;
        public double normPower;
        public double trainingStressScore;
        public double averageHR;

        public DateTime StartUtc => DateTime.TryParseExact(startTimeGMT, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;

        /// <summary>Recorded with a power meter.</summary>
        public bool HasPower => avgPower > 0 || normPower > 0;
    }

    [Serializable]
    public class GarminActivityList
    {
        public List<GarminActivity> items = new List<GarminActivity>();
    }

    /// <summary>The personal API key was rejected (expired, deleted or mistyped).</summary>
    public sealed class GarminKeyException : Exception
    {
        public GarminKeyException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Garmin Connect Personal API (developer.garmin.com/gc-developer-program/personal): a user's own Garmin data with a
    /// personal API key they create at connect.garmin.com/app/settings/personalApiToken. Read-only – activities cannot
    /// be uploaded. The key is exchanged for a bearer token (a JWT, valid 24 h); all requests send that token.
    /// </summary>
    public sealed class GarminPersonalApi
    {
        public const string BaseUrl = "https://personal.connectapi.garmin.com";
        public const string TokenPath = "/personalapitoken-service/personal-api/personal-api-key/token";
        public const string SearchPath = "/activitylist-service/activities/search/activities";
        public const string DownloadPath = "/download-service/files/activity/";
        public const string KeyPage = "https://connect.garmin.com/app/settings/personalApiToken";

        /// <summary>Page size of the activity search (Garmin's default and maximum used here).</summary>
        public const int PageSize = 99;

        private readonly HttpClient _http;
        private readonly IJsonCodec _json;

        public GarminPersonalApi(HttpClient http, IJsonCodec json)
        {
            _http = http;
            _json = json;
        }

        /// <summary>Exchanges the personal API key for a bearer token and its expiry (UTC).</summary>
        public async Task<(string Token, DateTime ExpiresUtc)> ObtainTokenAsync(string apiKey, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + TokenPath);
            request.Headers.TryAddWithoutValidation("Authorization", "ApiKey " + apiKey.Trim());
            request.Headers.Accept.ParseAdd("*/*");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new GarminKeyException($"Garmin rejected the API key ({(int)response.StatusCode})");
            }

            response.EnsureSuccessStatusCode();
            string token = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim().Trim('"');
            return (token, JwtExpiry(token) ?? DateTime.UtcNow.AddHours(23));
        }

        /// <summary>Cycling activities that started between the two dates (local dates, inclusive), newest first.</summary>
        public async Task<List<GarminActivity>> SearchCyclingAsync(string token, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
        {
            var result = new List<GarminActivity>();
            for (int start = 0; ; start += PageSize)
            {
                string url = $"{BaseUrl}{SearchPath}?activityType=cycling&startDate={fromDate:yyyy-MM-dd}&endDate={toDate:yyyy-MM-dd}" +
                    $"&start={start}&limit={PageSize}";
                string json = await GetStringAsync(token, url, ct).ConfigureAwait(false);
                var page = ParseActivities(json, _json);
                result.AddRange(page);
                if (page.Count < PageSize) break;
            }

            return result;
        }

        /// <summary>The original FIT file of an activity (null if the original is not a FIT file, e.g. a manual entry).</summary>
        public async Task<byte[]?> DownloadFitAsync(string token, long activityId, CancellationToken ct = default)
        {
            using var request = Authorized(token, HttpMethod.Get, BaseUrl + DownloadPath + activityId.ToString(CultureInfo.InvariantCulture));
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            ThrowIfUnauthorized(response);
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.NoContent) return null;
            response.EnsureSuccessStatusCode();
            byte[] data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return FitFromDownload(data);
        }

        /// <summary>The download is a ZIP with the original file; a bare FIT file is accepted as well.</summary>
        public static byte[]? FitFromDownload(byte[] data)
        {
            if (IsFit(data)) return data;
            try
            {
                using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
                var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".fit", StringComparison.OrdinalIgnoreCase));
                if (entry == null) return null;
                using var stream = entry.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                byte[] fit = copy.ToArray();
                return IsFit(fit) ? fit : null;
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        /// <summary>FIT files say ".FIT" at bytes 8–11.</summary>
        public static bool IsFit(byte[] data) => data.Length > 12 && data[8] == (byte)'.' && data[9] == (byte)'F' && data[10] == (byte)'I' && data[11] == (byte)'T';

        /// <summary>The activity search answers with a JSON array; JsonUtility needs an object around it.</summary>
        public static List<GarminActivity> ParseActivities(string json, IJsonCodec codec)
        {
            json = json.Trim();
            if (json.Length == 0 || json == "[]") return new List<GarminActivity>();
            return codec.Deserialize<GarminActivityList>("{\"items\":" + json + "}")?.items ?? new List<GarminActivity>();
        }

        /// <summary>Expiry ("exp" claim) of a JWT, or null.</summary>
        public static DateTime? JwtExpiry(string jwt)
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            try
            {
                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                int i = text.IndexOf("\"exp\"", StringComparison.Ordinal);
                if (i < 0) return null;
                int colon = text.IndexOf(':', i);
                int end = colon + 1;
                while (end < text.Length && (char.IsWhiteSpace(text[end]) || char.IsDigit(text[end]))) end++;
                return long.TryParse(text.Substring(colon + 1, end - colon - 1).Trim(), out long exp)
                    ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime
                    : (DateTime?)null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private async Task<string> GetStringAsync(string token, string url, CancellationToken ct)
        {
            using var request = Authorized(token, HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            ThrowIfUnauthorized(response);
            if (response.StatusCode == HttpStatusCode.NoContent) return "[]";
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private static HttpRequestMessage Authorized(string token, HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        private static void ThrowIfUnauthorized(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new GarminKeyException("Garmin token rejected (401)");
        }
    }
}
