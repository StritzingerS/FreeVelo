using System.Net;
using System.Net.Http.Headers;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Tests;

/// <summary>A rider as the app would be: created once, then signed in with the stored id and secret.</summary>
public sealed class TestRider
{
    private TestRider(HttpClient client, RiderCredentials credentials)
    {
        Client = client;
        Credentials = credentials;
    }

    /// <summary>Client that sends this rider's access token.</summary>
    public HttpClient Client { get; }

    public RiderCredentials Credentials { get; }

    public string Id => Credentials.riderId;

    public static async Task<TestRider> Create(ServerFactory server, string name)
    {
        var client = server.CreateClient();
        var created = await client.Post(ApiRoutes.Riders, new CreateRiderRequest { displayName = name });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var credentials = await created.Read<RiderCredentials>();
        var session = await client.Post(ApiRoutes.Session, new SessionRequest { riderId = credentials.riderId, secret = credentials.secret });
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await session.Read<SessionResponse>()).accessToken);
        return new TestRider(client, credentials);
    }
}

public sealed class RiderTests : IDisposable
{
    private readonly ServerFactory _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task Health()
    {
        Assert.Equal(HttpStatusCode.OK, (await _server.CreateClient().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task CreateWithJustANameThenSignInWithTheStoredSecret()
    {
        var client = _server.CreateClient();
        var created = await client.Post(ApiRoutes.Riders, new CreateRiderRequest { displayName = "  Sami  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var credentials = await created.Read<RiderCredentials>();
        Assert.Equal("Sami", credentials.displayName);
        Assert.True(credentials.secret.Length >= 40);

        var session = await client.Post(ApiRoutes.Session, new SessionRequest { riderId = credentials.riderId, secret = credentials.secret });
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var body = await session.Read<SessionResponse>();
        Assert.Equal(12 * 3600, body.expiresIn);
        Assert.Equal("Sami", body.rider.displayName);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.accessToken);
        var me = await client.GetAsync(ApiRoutes.Me);
        Assert.Equal(credentials.riderId, (await me.Read<RiderInfo>()).id);
    }

    [Fact]
    public async Task WrongSecretOrUnknownRiderIsRefused()
    {
        var anna = await TestRider.Create(_server, "Anna");
        var client = _server.CreateClient();
        var wrong = await client.Post(ApiRoutes.Session, new SessionRequest { riderId = anna.Id, secret = "guess" });
        var unknown = await client.Post(ApiRoutes.Session, new SessionRequest { riderId = "nobody", secret = anna.Credentials.secret });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(ApiErrors.InvalidCredentials, (await wrong.Read<ApiError>()).error);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("   ")]
    [InlineData("A name that is way too long for the list")]
    public async Task DisplayNameIsValidated(string name)
    {
        var response = await _server.CreateClient().Post(ApiRoutes.Riders, new CreateRiderRequest { displayName = name });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrors.InvalidDisplayName, (await response.Read<ApiError>()).error);
    }

    [Fact]
    public async Task Rename()
    {
        var anna = await TestRider.Create(_server, "Anna");
        var renamed = await anna.Client.PatchAsync(ApiRoutes.Me, System.Net.Http.Json.JsonContent.Create(new UpdateRiderRequest { displayName = "Anna B." }, options: ServerFactory.Json));
        Assert.Equal("Anna B.", (await renamed.Read<RiderInfo>()).displayName);
    }

    [Fact]
    public async Task DeleteRemovesTheRider()
    {
        var anna = await TestRider.Create(_server, "Anna");
        Assert.Equal(HttpStatusCode.NoContent, (await anna.Client.DeleteAsync(ApiRoutes.Me)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anna.Client.GetAsync(ApiRoutes.Me)).StatusCode);
        var session = await _server.CreateClient().Post(ApiRoutes.Session, new SessionRequest { riderId = anna.Id, secret = anna.Credentials.secret });
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Fact]
    public async Task ApiNeedsAnAccessToken()
    {
        var client = _server.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiRoutes.Me)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiRoutes.Lobbies)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiRoutes.Me)).StatusCode);
    }

    [Fact]
    public async Task SignInIsRateLimitedPerClient()
    {
        using var limited = new ServerFactory(identityPerMinute: 3);
        var client = limited.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (int i = 0; i < 5; i++)
        {
            codes.Add((await client.Post(ApiRoutes.Session, new SessionRequest { riderId = "x", secret = "guess " + i })).StatusCode);
        }

        Assert.Equal(3, codes.Count(c => c == HttpStatusCode.Unauthorized));
        Assert.Equal(2, codes.Count(c => c == HttpStatusCode.TooManyRequests));
    }
}
