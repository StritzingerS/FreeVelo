using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FreeVelo.Shared.Api;

namespace FreeVelo.Shared.Strava
{
    /// <summary>One activity from Strava's activity list (public fields for Unity's JsonUtility).</summary>
    [Serializable]
    public class StravaActivity
    {
        public long id;
        public string name = "";

        /// <summary>Legacy type: "Ride" (road, gravel, MTB), "VirtualRide", "EBikeRide", "Run", …</summary>
        public string type = "";

        /// <summary>Start in UTC, ISO 8601 ("2026-11-08T09:15:02Z").</summary>
        public string start_date = "";
        public double elapsed_time;
        public double moving_time;

        /// <summary>Power from a power meter (not Strava's estimate).</summary>
        public bool device_watts;
        public bool trainer;

        /// <summary>The uploaded file's name for uploads through the API (FreeVelo: the FIT file name).</summary>
        public string external_id = "";

        public DateTime StartUtc => DateTime.TryParse(start_date, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;

        /// <summary>A bike ride (outdoors or on a trainer, not an e-bike) recorded with a power meter.</summary>
        public bool IsRideWithPower => (type == "Ride" || type == "VirtualRide") && device_watts;
    }

    [Serializable]
    public class StravaActivityList
    {
        public List<StravaActivity> items = new List<StravaActivity>();
    }

    /// <summary>Strava refused or could not answer a read request.</summary>
    public sealed class StravaReadException : Exception
    {
        public StravaReadException(HttpStatusCode status, string message)
            : base(message)
        {
            Status = status;
        }

        public HttpStatusCode Status { get; }

        /// <summary>Token expired or revoked, or the rider did not allow reading activities.</summary>
        public bool Unauthorized => Status == HttpStatusCode.Unauthorized || Status == HttpStatusCode.Forbidden;

        /// <summary>Strava's rate limit (100 reads per 15 minutes): try again later.</summary>
        public bool RateLimited => (int)Status == 429;
    }

    /// <summary>The per-second data of an activity; missing values are NaN.</summary>
    public sealed class StravaStreams
    {
        public double[] Time = new double[0];
        public double[] Watts = new double[0];
        public double[] HeartRate = new double[0];
        public double[] Cadence = new double[0];
        public double[] Distance = new double[0];
        public double[] Altitude = new double[0];
        public double[] Speed = new double[0];

        public int Count => Time.Length;

        /// <summary>Value <paramref name="i"/> of a stream, or null when missing.</summary>
        public static double? At(double[] stream, int i) => i < stream.Length && !double.IsNaN(stream[i]) ? stream[i] : (double?)null;

        /// <summary>
        /// Reads the answer of /activities/{id}/streams?key_by_type=true by hand: the data arrays can contain null
        /// (e.g. power drop-outs), which neither JsonUtility nor System.Text.Json read into a number array.
        /// </summary>
        public static StravaStreams Parse(string json) => new StravaStreams
        {
            Time = Data(json, "time"),
            Watts = Data(json, "watts"),
            HeartRate = Data(json, "heartrate"),
            Cadence = Data(json, "cadence"),
            Distance = Data(json, "distance"),
            Altitude = Data(json, "altitude"),
            Speed = Data(json, "velocity_smooth"),
        };

        private static double[] Data(string json, string key)
        {
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return new double[0];
            int d = json.IndexOf("\"data\"", k, StringComparison.Ordinal);
            int open = d < 0 ? -1 : json.IndexOf('[', d);
            int close = open < 0 ? -1 : json.IndexOf(']', open);
            if (close < 0) return new double[0];
            string body = json.Substring(open + 1, close - open - 1);
            if (body.Trim().Length == 0) return new double[0];
            var parts = body.Split(',');
            var values = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                values[i] = p == "true" ? 1 : p == "false" ? 0
                    : double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
            }

            return values;
        }
    }

    /// <summary>
    /// Reads the rider's own activities from Strava (API v3, scope activity:read_all) for the ride history: the list of
    /// activities since a date and the per-second data of one activity. Strava's API does not hand out the original
    /// files, so FreeVelo rebuilds the ride from the streams.
    /// </summary>
    public sealed class StravaReader
    {
        public const int PageSize = 100;

        /// <summary>Never read more pages than this in one go (Strava's read limit is 100 requests per 15 minutes).</summary>
        public const int MaxPages = 5;

        private readonly HttpClient _http;
        private readonly IJsonCodec _json;

        public StravaReader(HttpClient http, IJsonCodec json)
        {
            _http = http;
            _json = json;
        }

        /// <summary>The rider's activities that started after <paramref name="afterUtc"/>.</summary>
        public async Task<List<StravaActivity>> ActivitiesAsync(string token, DateTime afterUtc, CancellationToken ct = default)
        {
            long after = new DateTimeOffset(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var result = new List<StravaActivity>();
            for (int page = 1; page <= MaxPages; page++)
            {
                string json = await GetAsync(token, $"{StravaUploader.ApiBase}/athlete/activities?after={after}&per_page={PageSize}&page={page}", ct)
                    .ConfigureAwait(false);
                var items = ParseActivities(json, _json);
                result.AddRange(items);
                if (items.Count < PageSize) break;
            }

            return result;
        }

        /// <summary>Time, power, heart rate, cadence, distance, altitude and speed of one activity.</summary>
        public async Task<StravaStreams> StreamsAsync(string token, long activityId, CancellationToken ct = default)
        {
            string url = $"{StravaUploader.ApiBase}/activities/{activityId.ToString(CultureInfo.InvariantCulture)}/streams" +
                "?keys=time,watts,heartrate,cadence,distance,altitude,velocity_smooth&key_by_type=true";
            return StravaStreams.Parse(await GetAsync(token, url, ct).ConfigureAwait(false));
        }

        /// <summary>The list is a JSON array; JsonUtility needs an object around it.</summary>
        public static List<StravaActivity> ParseActivities(string json, IJsonCodec codec)
        {
            json = json.Trim();
            if (json.Length == 0 || json == "[]") return new List<StravaActivity>();
            return codec.Deserialize<StravaActivityList>("{\"items\":" + json + "}")?.items ?? new List<StravaActivity>();
        }

        private async Task<string> GetAsync(string token, string url, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new StravaReadException(response.StatusCode, $"Strava answered {(int)response.StatusCode}");
            }

            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
    }
}
