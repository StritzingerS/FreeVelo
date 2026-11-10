using System.Net;
using System.Text;
using FreeVelo.Shared.Api;
using FreeVelo.Shared.Strava;

namespace FreeVelo.Server.Tests;

/// <summary>A stand-in for strava.com: answers the token endpoint and the upload API, records requests.</summary>
public sealed class FakeStrava : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string Body, string? Auth)> Requests { get; } = new();

    /// <summary>Answer per request (default: a token response for the token endpoint).</summary>
    public Func<HttpRequestMessage, string, HttpResponseMessage>? Respond { get; set; }

    public int Exchanges { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string body = request.Content != null ? await request.Content.ReadAsStringAsync(ct) : "";
        Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.Parameter));
        if (Respond != null) return Respond(request, body);
        return TokenEndpoint(body);
    }

    public HttpResponseMessage TokenEndpoint(string body)
    {
        if (body.Contains("grant_type=authorization_code") && body.Contains("code=good"))
        {
            Exchanges++;
            return Json(HttpStatusCode.OK, """
                {"token_type":"Bearer","expires_at":1900000000,"expires_in":21600,"refresh_token":"refresh-1",
                 "access_token":"access-1","athlete":{"id":4242,"firstname":"Sami","lastname":"S.","profile":"https://x/p.jpg"}}
                """);
        }

        if (body.Contains("grant_type=refresh_token") && body.Contains("refresh_token=refresh-"))
        {
            return Json(HttpStatusCode.OK, """{"token_type":"Bearer","access_token":"access-2","expires_at":1900021600,"expires_in":21600,"refresh_token":"refresh-2"}""");
        }

        return Json(HttpStatusCode.BadRequest, """{"message":"Bad Request","errors":[{"resource":"RefreshToken","field":"refresh_token","code":"invalid"}]}""");
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public sealed class StravaTests : IDisposable
{
    private readonly FakeStrava _strava = new();
    private readonly ServerFactory _server;
    private readonly HttpClient _browser;

    public StravaTests()
    {
        _server = new ServerFactory(settings: new Dictionary<string, string>
        {
            ["Strava:ClientId"] = "12345",
            ["Strava:ClientSecret"] = "very-secret",
        }, stravaHandler: _strava);
        _browser = _server.CreateClient();
    }

    public void Dispose() => _server.Dispose();

    private FreeVeloApi Api() => new(_server.CreateClient(), new TestJson());

    private static string Query(string url, string key) =>
        Uri.UnescapeDataString(new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).First(p => p[0] == key)[1]);

    [Fact]
    public async Task ConnectExchangesTheCodeOnTheServerAndTheAppCollectsTheTokensOnce()
    {
        var api = Api();
        string state = FreeVeloApi.NewStravaState();
        string url = await api.StravaStartAsync(state);

        Assert.StartsWith("https://www.strava.com/oauth/mobile/authorize?", url);
        Assert.Equal("12345", Query(url, "client_id"));
        Assert.Equal("activity:write,activity:read_all", Query(url, "scope"));
        Assert.Equal(state, Query(url, "state"));
        Assert.EndsWith(ApiRoutes.StravaCallback, Query(url, "redirect_uri"));
        Assert.DoesNotContain("very-secret", url);

        Assert.Null(await api.StravaClaimAsync(state)); // the rider is still on the Strava page

        // Strava sends the browser back to the server
        var page = await _browser.GetStringAsync($"{ApiRoutes.StravaCallback}?state={state}&code=good&scope=read,activity:write");
        Assert.Contains("Strava ist verbunden", page);
        Assert.Contains("client_secret=very-secret", _strava.Requests.Single().Body);

        var tokens = await api.StravaClaimAsync(state);
        Assert.NotNull(tokens);
        Assert.Equal(("access-1", "refresh-1", 1900000000L, 4242L, "Sami S."),
            (tokens!.accessToken, tokens.refreshToken, tokens.expiresAt, tokens.athleteId, tokens.athleteName));
        Assert.False(tokens.canRead); // "view activities" was not ticked: uploads only, no import

        // Collected once, then forgotten
        var again = await Assert.ThrowsAsync<ApiException>(() => api.StravaClaimAsync(state));
        Assert.Equal(HttpStatusCode.NotFound, again.Status);
    }

    [Theory]
    [InlineData("error=access_denied&scope=")]
    [InlineData("code=good&scope=read")] // "upload activities" unticked on the approval page
    public async Task DeclinedOrWithoutUploadPermissionIsReportedToTheApp(string query)
    {
        var api = Api();
        string state = FreeVeloApi.NewStravaState();
        await api.StravaStartAsync(state);
        var page = await _browser.GetStringAsync($"{ApiRoutes.StravaCallback}?state={state}&{query}");
        Assert.Contains("nicht verbunden", page);
        Assert.Equal(0, _strava.Exchanges);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.StravaClaimAsync(state));
        Assert.Equal(ApiErrors.StravaDenied, error.Code);
    }

    [Fact]
    public async Task UnknownOrMalformedStatesAreRefused()
    {
        var page = await _browser.GetStringAsync($"{ApiRoutes.StravaCallback}?state={FreeVeloApi.NewStravaState()}&code=good&scope=activity:write");
        Assert.Contains("abgelaufen", page);
        Assert.Equal(0, _strava.Exchanges); // no code exchange for logins the server didn't start

        var error = await Assert.ThrowsAsync<ApiException>(() => Api().StravaStartAsync("short"));
        Assert.Equal(ApiErrors.InvalidState, error.Code);
    }

    [Fact]
    public async Task LoginsExpire()
    {
        var api = Api();
        string state = FreeVeloApi.NewStravaState();
        await api.StravaStartAsync(state);
        _server.Time.Advance(TimeSpan.FromMinutes(11));
        var page = await _browser.GetStringAsync($"{ApiRoutes.StravaCallback}?state={state}&code=good&scope=activity:write");
        Assert.Contains("abgelaufen", page);
    }

    [Fact]
    public async Task RemembersWhetherReadingWasAllowed()
    {
        var api = Api();
        string state = FreeVeloApi.NewStravaState();
        await api.StravaStartAsync(state);
        await _browser.GetStringAsync($"{ApiRoutes.StravaCallback}?state={state}&code=good&scope=read,activity:write,activity:read_all");
        Assert.True((await api.StravaClaimAsync(state))!.canRead);
    }

    [Fact]
    public async Task RefreshUsesTheSecretAndReportsRevokedTokens()
    {
        var api = Api();
        var tokens = await api.StravaRefreshAsync("refresh-1");
        Assert.Equal(("access-2", "refresh-2"), (tokens.accessToken, tokens.refreshToken));
        Assert.Contains("client_secret=very-secret", _strava.Requests.Last().Body);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.StravaRefreshAsync("revoked"));
        Assert.Equal(ApiErrors.StravaDenied, error.Code);
    }

    [Fact]
    public async Task WithoutStravaSettingsTheServerSaysSo()
    {
        using var plain = new ServerFactory();
        var api = new FreeVeloApi(plain.CreateClient(), new TestJson());
        var error = await Assert.ThrowsAsync<ApiException>(() => api.StravaStartAsync(FreeVeloApi.NewStravaState()));
        Assert.Equal((HttpStatusCode.ServiceUnavailable, ApiErrors.StravaNotConfigured), (error.Status, error.Code));
    }

    // ------------------------------------------------------------------ app side: queue and uploads

    private StravaSync Sync(StravaState state, Func<DateTime> now)
    {
        var uploader = new StravaUploader(new HttpClient(_strava), (_, _) => Task.CompletedTask);
        return new StravaSync(Api(), uploader, state, () => { }, now) { ReadFile = path => path.EndsWith("missing.fit") ? null : new byte[] { 1, 2, 3 } };
    }

    private static StravaState Connected(long expiresAt) => new()
    {
        tokens = new StravaTokens { accessToken = "access-1", refreshToken = "refresh-1", expiresAt = expiresAt, athleteId = 4242, athleteName = "Sami S." },
    };

    [Fact]
    public async Task UploadsQueuedRidesRefreshingAnExpiredTokenFirst()
    {
        var now = new DateTime(2026, 11, 6, 16, 0, 0, DateTimeKind.Utc);
        long unixNow = new DateTimeOffset(now).ToUnixTimeSeconds();
        var state = Connected(unixNow + 60); // expires in a minute: refresh first
        int statusChecks = 0;
        _strava.Respond = (request, body) =>
        {
            string url = request.RequestUri!.ToString();
            if (url.Contains("/oauth/token")) return _strava.TokenEndpoint(body);
            if (request.Method == HttpMethod.Post && url.EndsWith("/api/v3/uploads"))
                return FakeStrava.Json(HttpStatusCode.Created, """{"id":777,"id_str":"777","external_id":"x.fit","error":null,"status":"Your activity is still being processed.","activity_id":null}""");
            statusChecks++;
            return FakeStrava.Json(HttpStatusCode.OK, """{"id":777,"error":null,"status":"Your activity is ready.","activity_id":555}""");
        };

        var sync = Sync(state, () => now);
        sync.Enqueue("/rides/2026-11-06_16-24_FTP-Rampentest.fit", "FTP-Rampentest");
        Assert.True(sync.HasWork);
        await sync.ProcessAsync();

        var item = state.uploads.Single();
        Assert.Equal((StravaItemStatus.Done, 555L), (item.Status, item.activityId));
        Assert.Equal("access-2", state.tokens.accessToken); // refreshed through the server
        Assert.Equal("Sami S.", state.tokens.athleteName); // kept across the refresh
        Assert.Equal(1, statusChecks);

        var upload = _strava.Requests.Single(r => r.Url.EndsWith("/api/v3/uploads"));
        Assert.Equal("access-2", upload.Auth);
        Assert.Contains("name=data_type", upload.Body);
        Assert.Contains("FTP-Rampentest", upload.Body);
        Assert.False(sync.HasWork);
    }

    [Fact]
    public async Task DuplicatesCountAsDoneRejectedFilesFailAndOfflineRetriesLater()
    {
        var now = new DateTime(2026, 11, 6, 16, 0, 0, DateTimeKind.Utc);
        var state = Connected(1900000000);
        string answer = "duplicate";
        _strava.Respond = (request, body) => answer switch
        {
            "duplicate" => FakeStrava.Json(HttpStatusCode.Created,
                """{"id":1,"error":"2026-11-06_16-24.fit duplicate of <a href='/activities/999' target='_blank'>Ramp</a>","status":"There was an error processing your activity.","activity_id":null}"""),
            "rejected" => FakeStrava.Json(HttpStatusCode.Created, """{"id":2,"error":"Time information is missing from every data point.","status":"There was an error processing your activity.","activity_id":null}"""),
            _ => throw new HttpRequestException("No such host is known."),
        };

        var sync = Sync(state, () => now);
        var duplicate = sync.Enqueue("/rides/a.fit", "A");
        await sync.ProcessAsync();
        Assert.Equal((StravaItemStatus.Done, 999L), (duplicate.Status, duplicate.activityId));

        answer = "rejected";
        var rejected = sync.Enqueue("/rides/b.fit", "B");
        await sync.ProcessAsync();
        Assert.Equal(StravaItemStatus.Failed, rejected.Status);
        Assert.Contains("Time information", rejected.error);

        var missing = sync.Enqueue("/rides/missing.fit", "C");
        await sync.ProcessAsync();
        Assert.Equal(StravaItemStatus.Failed, missing.Status);

        answer = "offline";
        var offline = sync.Enqueue("/rides/d.fit", "D");
        await sync.ProcessAsync();
        Assert.Equal(StravaItemStatus.Waiting, offline.Status);
        Assert.False(sync.HasWork); // back-off: not again right away
        now = now.AddMinutes(2);
        Assert.True(sync.HasWork);

        // A failed ride can be queued again by hand
        sync.Enqueue("/rides/b.fit", "B");
        Assert.Equal(StravaItemStatus.Waiting, rejected.Status);
    }

    [Fact]
    public async Task RevokedOnStravaDisconnectsAndKeepsTheRideQueued()
    {
        var state = Connected(1900000000);
        state.tokens.refreshToken = "revoked";
        _strava.Respond = (request, body) => request.RequestUri!.ToString().Contains("/oauth/token")
            ? _strava.TokenEndpoint(body)
            : FakeStrava.Json(HttpStatusCode.Unauthorized, """{"message":"Authorization Error","errors":[{"resource":"Athlete","field":"access_token","code":"invalid"}]}""");

        var sync = Sync(state, () => DateTime.UtcNow);
        var item = sync.Enqueue("/rides/a.fit", "A");
        await sync.ProcessAsync();
        Assert.False(sync.Connected);
        Assert.Equal(StravaItemStatus.Waiting, item.Status);
    }

    [Fact]
    public async Task ConnectAndDisconnectThroughSync()
    {
        var state = new StravaState();
        _strava.Respond = (request, body) => request.RequestUri!.ToString().Contains("/oauth/token")
            ? _strava.TokenEndpoint(body)
            : new HttpResponseMessage(HttpStatusCode.OK);
        var sync = Sync(state, () => DateTime.UtcNow);
        string url = await sync.BeginConnectAsync();
        Assert.True(sync.Connecting);
        Assert.False(await sync.PollConnectAsync());

        await _browser.GetStringAsync($"{ApiRoutes.StravaCallback}?state={Query(url, "state")}&code=good&scope=activity:write");
        Assert.True(await sync.PollConnectAsync());
        Assert.True(sync.Connected);
        Assert.Equal("Sami S.", sync.AthleteName);

        await sync.DisconnectAsync();
        Assert.False(sync.Connected);
        var deauth = _strava.Requests.Last();
        Assert.Equal(("https://www.strava.com/oauth/deauthorize", "access-1"), (deauth.Url, deauth.Auth));
    }

    [Theory]
    [InlineData(201, """{"id":7,"error":null,"status":"Your activity is still being processed.","activity_id":null}""", StravaUploadOutcome.Processing)]
    [InlineData(200, """{"id":7,"error":null,"status":"Your activity is ready.","activity_id":12}""", StravaUploadOutcome.Done)]
    [InlineData(429, """{"message":"Rate Limit Exceeded"}""", StravaUploadOutcome.TryLater)]
    [InlineData(503, "<html>down</html>", StravaUploadOutcome.TryLater)]
    [InlineData(401, """{"message":"Authorization Error"}""", StravaUploadOutcome.Unauthorized)]
    [InlineData(400, """{"message":"Bad Request","errors":[]}""", StravaUploadOutcome.Rejected)]
    public void InterpretsStravaAnswers(int status, string body, StravaUploadOutcome expected) =>
        Assert.Equal(expected, StravaUploader.Interpret((HttpStatusCode)status, body).Outcome);
}
