using System;
using System.Collections.Generic;
using FreeVelo.Shared.Live;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FreeVelo.Shared.Api
{
    /// <summary>JSON for the API messages: System.Text.Json on the server and in tests, JsonUtility in Unity.</summary>
    public interface IJsonCodec
    {
        string Serialize(object value);

        T Deserialize<T>(string json);
    }

    /// <summary>The server answered with an error (see <see cref="ApiErrors"/> for <see cref="Code"/>).</summary>
    public sealed class ApiException : Exception
    {
        public ApiException(HttpStatusCode status, string code)
            : base($"{(int)status} {code}")
        {
            Status = status;
            Code = code;
        }

        public HttpStatusCode Status { get; }

        /// <summary>Error code from the server, or empty (e.g. a proxy error page).</summary>
        public string Code { get; }
    }

    /// <summary>
    /// The app's client for the FreeVelo server: rider identity (no email or password), sessions, group rides and
    /// the live WebSocket. The app keeps <see cref="Credentials"/> on the device; sessions are fetched when needed and
    /// renewed after a 401.
    /// </summary>
    public sealed class FreeVeloApi
    {
        private readonly HttpClient _http;
        private readonly IJsonCodec _json;
        private readonly Func<DateTime> _now;
        private string? _accessToken;
        private DateTime _accessTokenUntil;

        public FreeVeloApi(HttpClient http, IJsonCodec json, Func<DateTime>? utcNow = null)
        {
            _http = http;
            _json = json;
            _now = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>The stored identity (null until <see cref="CreateRiderAsync"/>).</summary>
        public RiderCredentials? Credentials { get; set; }

        /// <summary>
        /// Called when the server no longer knew the rider (e.g. a free host that forgets its data on restart) and the
        /// rider was registered again with the same name: the app stores the new credentials.
        /// </summary>
        public Action<RiderCredentials>? CredentialsReplaced { get; set; }

        public Uri? BaseAddress => _http.BaseAddress;

        /// <summary>First start: creates the rider and sets <see cref="Credentials"/> (the app must store them).</summary>
        public async Task<RiderCredentials> CreateRiderAsync(string displayName, CancellationToken ct = default)
        {
            var credentials = await Send<RiderCredentials>(HttpMethod.Post, ApiRoutes.Riders, new CreateRiderRequest { displayName = displayName }, authorized: false, ct);
            Credentials = credentials;
            _accessToken = null;
            return credentials;
        }

        /// <summary>A valid access token (fetches a new session when there is none or it runs out soon).</summary>
        public async Task<string> AccessTokenAsync(CancellationToken ct = default)
        {
            if (_accessToken != null && _now() < _accessTokenUntil) return _accessToken;
            if (Credentials == null) throw new InvalidOperationException("No rider yet: call CreateRiderAsync first.");
            SessionResponse session;
            try
            {
                session = await Send<SessionResponse>(HttpMethod.Post, ApiRoutes.Session, new SessionRequest { riderId = Credentials.riderId, secret = Credentials.secret }, authorized: false, ct);
            }
            catch (ApiException e) when (e.Code == ApiErrors.InvalidCredentials && !string.IsNullOrWhiteSpace(Credentials.displayName))
            {
                // The server forgot this rider: register again with the same name, nobody has to type anything.
                var renewed = await Send<RiderCredentials>(HttpMethod.Post, ApiRoutes.Riders, new CreateRiderRequest { displayName = Credentials.displayName }, authorized: false, ct);
                Credentials = renewed;
                CredentialsReplaced?.Invoke(renewed);
                session = await Send<SessionResponse>(HttpMethod.Post, ApiRoutes.Session, new SessionRequest { riderId = renewed.riderId, secret = renewed.secret }, authorized: false, ct);
            }

            _accessToken = session.accessToken;
            _accessTokenUntil = _now().AddSeconds(Math.Max(60, session.expiresIn - 300)); // renew 5 min early
            Credentials.displayName = session.rider.displayName;
            return _accessToken;
        }

        public Task<RiderInfo> RenameAsync(string displayName, CancellationToken ct = default) =>
            Send<RiderInfo>(new HttpMethod("PATCH"), ApiRoutes.Me, new UpdateRiderRequest { displayName = displayName }, authorized: true, ct);

        /// <summary>Deletes the rider on the server; the app then forgets <see cref="Credentials"/>.</summary>
        public async Task DeleteRiderAsync(CancellationToken ct = default)
        {
            await Send<object>(HttpMethod.Delete, ApiRoutes.Me, null, authorized: true, ct);
            Credentials = null;
            _accessToken = null;
        }

        public Task<LobbyList> PublicLobbiesAsync(CancellationToken ct = default) =>
            Send<LobbyList>(HttpMethod.Get, ApiRoutes.Lobbies, null, authorized: true, ct);

        public Task<LobbyDetails> CreateLobbyAsync(CreateLobbyRequest request, CancellationToken ct = default) =>
            Send<LobbyDetails>(HttpMethod.Post, ApiRoutes.Lobbies, request, authorized: true, ct);

        public Task<LobbyDetails> JoinByCodeAsync(string code, CancellationToken ct = default) =>
            Send<LobbyDetails>(HttpMethod.Post, ApiRoutes.JoinByCode, new JoinByCodeRequest { code = code }, authorized: true, ct);

        public Task<LobbyDetails> JoinPublicAsync(string lobbyId, CancellationToken ct = default) =>
            Send<LobbyDetails>(HttpMethod.Post, ApiRoutes.JoinPublic(lobbyId), null, authorized: true, ct);

        /// <summary>The ride the rider is in, or null.</summary>
        public Task<LobbyDetails?> CurrentLobbyAsync(CancellationToken ct = default) =>
            Send<LobbyDetails?>(HttpMethod.Get, ApiRoutes.CurrentLobby, null, authorized: true, ct);

        public Task LeaveLobbyAsync(CancellationToken ct = default) =>
            Send<object>(HttpMethod.Post, ApiRoutes.LeaveLobby, null, authorized: true, ct);

        /// <summary>Opens the live WebSocket of the current ride (ws:// or wss:// like the server address).</summary>
        public async Task<WebSocket> ConnectLiveAsync(CancellationToken ct = default)
        {
            string token = await AccessTokenAsync(ct);
            // Own small client: ClientWebSocket fails to connect under Unity's Mono runtime.
            var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token };
            return await TinyWebSocket.ConnectAsync(LiveUri(_http.BaseAddress ?? throw new InvalidOperationException("No server address")), headers, ct);
        }

        public static Uri LiveUri(Uri baseAddress)
        {
            var builder = new UriBuilder(new Uri(baseAddress, ApiRoutes.Live))
            {
                Scheme = baseAddress.Scheme == "https" ? "wss" : "ws",
            };
            return builder.Uri;
        }

        private async Task<T> Send<T>(HttpMethod method, string path, object? body, bool authorized, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, path);
                if (body != null) request.Content = new StringContent(_json.Serialize(body), Encoding.UTF8, "application/json");
                if (authorized) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));

                using var response = await _http.SendAsync(request, ct).ConfigureAwait(true);
                string text = response.Content != null ? await response.Content.ReadAsStringAsync().ConfigureAwait(true) : "";

                // Expired or revoked session (e.g. server restarted with a new key): get a new one once.
                if (authorized && response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _accessToken = null;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    string code = "";
                    try
                    {
                        code = _json.Deserialize<ApiError>(text)?.error ?? "";
                    }
                    catch (Exception)
                    {
                        // Not our JSON (proxy page, HTML).
                    }

                    throw new ApiException(response.StatusCode, code);
                }

                if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(text) || typeof(T) == typeof(object)) return default!;
                return _json.Deserialize<T>(text);
            }
        }
    }
}
