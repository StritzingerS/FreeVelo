using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Strava;

/// <summary>Strava API app (strava.com/settings/api). The secret comes from the environment (Strava__ClientSecret).</summary>
public sealed class StravaOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>Public address of this server for the redirect (default: taken from the request).</summary>
    public string PublicBaseUrl { get; set; } = "";

    public bool Configured => ClientId.Length > 0 && ClientSecret.Length > 0;
}

/// <summary>
/// Connecting Strava for the apps. Nothing is stored permanently: a login started by an app is kept in memory
/// (by its random state) until Strava sends the rider back, the tokens are exchanged with the client secret and the
/// app collects them once – then they are forgotten.
/// </summary>
public sealed class StravaService
{
    public const string HttpClientName = "strava";
    public const string RequiredScope = "activity:write";
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    private readonly StravaOptions _options;
    private readonly IHttpClientFactory _http;
    private readonly TimeProvider _time;
    private readonly ILogger<StravaService> _log;
    private readonly ConcurrentDictionary<string, Pending> _pending = new();

    public StravaService(StravaOptions options, IHttpClientFactory http, TimeProvider time, ILogger<StravaService> log)
    {
        _options = options;
        _http = http;
        _time = time;
        _log = log;
    }

    public bool Configured => _options.Configured;

    private sealed class Pending
    {
        public DateTimeOffset Created;
        public string RedirectUri = "";
        public StravaTokens? Tokens;
        public string? Error;
    }

    public static bool ValidState(string? state) =>
        state is { Length: 64 } && state.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>Registers a login and returns Strava's approval page for it.</summary>
    public string Start(string state, string redirectUri)
    {
        Cleanup();
        _pending[state] = new Pending { Created = _time.GetUtcNow(), RedirectUri = redirectUri };
        return "https://www.strava.com/oauth/mobile/authorize"
            + $"?client_id={Uri.EscapeDataString(_options.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + "&response_type=code&approval_prompt=auto"
            + $"&scope={Uri.EscapeDataString(RequiredScope)}"
            + $"&state={state}";
    }

    public string RedirectUri(HttpRequest request) =>
        (_options.PublicBaseUrl.Length > 0 ? _options.PublicBaseUrl.TrimEnd('/') : $"{request.Scheme}://{request.Host}") + ApiRoutes.StravaCallback;

    /// <summary>Strava sent the rider back. Returns false if the state is unknown or expired.</summary>
    public async Task<bool> Complete(string state, string? code, string? scope, string? error, CancellationToken ct)
    {
        Cleanup();
        if (!_pending.TryGetValue(state, out var pending)) return false;

        bool hasScope = (scope ?? "").Split(',').Contains(RequiredScope);
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || !hasScope)
        {
            // Denied, or the rider unticked "upload activities" on the approval page.
            pending.Error = ApiErrors.StravaDenied;
            return true;
        }

        try
        {
            var response = await Token(new Dictionary<string, string>
            {
                ["code"] = code!,
                ["grant_type"] = "authorization_code",
            }, ct);
            pending.Tokens = response == null ? null : ToTokens(response);
            if (pending.Tokens == null) pending.Error = ApiErrors.StravaFailed;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogWarning(e, "Strava token exchange failed");
            pending.Error = ApiErrors.StravaFailed;
        }

        return true;
    }

    public enum ClaimResult
    {
        Unknown,
        Waiting,
        Done,
        Failed,
    }

    /// <summary>The app collects the tokens of its login (once).</summary>
    public ClaimResult Claim(string state, out StravaTokens? tokens, out string? error)
    {
        Cleanup();
        tokens = null;
        error = null;
        if (!_pending.TryGetValue(state, out var pending)) return ClaimResult.Unknown;
        if (pending.Tokens == null && pending.Error == null) return ClaimResult.Waiting;

        _pending.TryRemove(state, out _);
        tokens = pending.Tokens;
        error = pending.Error;
        return tokens != null ? ClaimResult.Done : ClaimResult.Failed;
    }

    /// <summary>New access token (they last 6 hours). Null if Strava refused the refresh token.</summary>
    public async Task<StravaTokens?> Refresh(string refreshToken, CancellationToken ct)
    {
        var response = await Token(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        }, ct);
        return response == null ? null : ToTokens(response);
    }

    private async Task<TokenResponse?> Token(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = _options.ClientId;
        form["client_secret"] = _options.ClientSecret;
        using var http = _http.CreateClient(HttpClientName);
        using var response = await http.PostAsync("https://www.strava.com/oauth/token", new FormUrlEncodedContent(form), ct);
        if ((int)response.StatusCode is >= 400 and < 500)
        {
            // Strava says which field was wrong, e.g. Application/client_secret (server settings) or
            // RefreshToken/refresh_token (revoked by the rider). Contains no tokens.
            string reason = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Strava refused the token request: {Status} {Reason}", (int)response.StatusCode, reason.Length > 300 ? reason[..300] : reason);
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
    }

    private static StravaTokens? ToTokens(TokenResponse r)
    {
        if (string.IsNullOrEmpty(r.AccessToken) || string.IsNullOrEmpty(r.RefreshToken)) return null;
        return new StravaTokens
        {
            accessToken = r.AccessToken,
            refreshToken = r.RefreshToken,
            expiresAt = r.ExpiresAt,
            athleteId = r.Athlete?.Id ?? 0,
            athleteName = $"{r.Athlete?.FirstName} {r.Athlete?.LastName}".Trim(),
        };
    }

    private void Cleanup()
    {
        var limit = _time.GetUtcNow() - PendingLifetime;
        foreach (var entry in _pending)
        {
            if (entry.Value.Created < limit) _pending.TryRemove(entry.Key, out _);
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
        [JsonPropertyName("athlete")] public AthleteResponse? Athlete { get; set; }
    }

    private sealed class AthleteResponse
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("firstname")] public string? FirstName { get; set; }
        [JsonPropertyName("lastname")] public string? LastName { get; set; }
    }
}
