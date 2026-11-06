using System.Net;
using FreeVelo.Server.Lobbies;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Tests;

public sealed class LobbyTests : IDisposable
{
    private readonly ServerFactory _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task PrivateRideIsJoinedWithItsFourLetterCode()
    {
        var sami = await TestRider.Create(_server, "Sami");
        var anna = await TestRider.Create(_server, "Anna");

        var created = await Create(sami, new CreateLobbyRequest { name = "Sunday 4x4", workoutId = "4x4", workoutName = "4×4" });
        Assert.Equal(4, created.code.Length);
        Assert.All(created.code, c => Assert.Contains(c, LobbyRules.CodeAlphabet));
        Assert.True(created.isPrivate);
        Assert.Equal(sami.Id, created.hostRiderId);
        Assert.Equal(LobbyMode.KeepTogether, created.mode);

        // Not in the public list.
        Assert.Empty((await (await anna.Client.GetAsync(ApiRoutes.Lobbies)).Read<LobbyList>()).lobbies);

        // Typed casually: lower case with a space.
        string typed = created.code.ToLowerInvariant().Insert(2, " ");
        var joined = await anna.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = typed });
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var details = await joined.Read<LobbyDetails>();
        Assert.Equal(created.id, details.id);
        Assert.Equal(new[] { "Sami", "Anna" }, details.riders.Select(r => r.displayName));
    }

    [Fact]
    public async Task PrivateRideCannotBeJoinedFromTheListById()
    {
        var sami = await TestRider.Create(_server, "Sami");
        var anna = await TestRider.Create(_server, "Anna");
        var created = await Create(sami, new CreateLobbyRequest { name = "Secret ride" });
        var response = await anna.Client.PostAsync(ApiRoutes.JoinPublic(created.id), null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WrongCodeIsNotFound()
    {
        var anna = await TestRider.Create(_server, "Anna");
        var response = await anna.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = "ZZZZ" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiErrors.LobbyNotFound, (await response.Read<ApiError>()).error);
    }

    [Fact]
    public async Task PublicRideIsListedAndJoinable()
    {
        var sami = await TestRider.Create(_server, "Sami");
        var anna = await TestRider.Create(_server, "Anna");
        var created = await Create(sami, new CreateLobbyRequest { name = "Open endurance", isPrivate = false, mode = LobbyMode.Free, maxRiders = 6 });

        var list = (await (await anna.Client.GetAsync(ApiRoutes.Lobbies)).Read<LobbyList>()).lobbies;
        var entry = Assert.Single(list);
        Assert.Equal("Open endurance", entry.name);
        Assert.Equal("Sami", entry.hostName);
        Assert.Equal(1, entry.riderCount);
        Assert.Equal(LobbyMode.Free, entry.mode);

        Assert.Equal(HttpStatusCode.OK, (await anna.Client.PostAsync(ApiRoutes.JoinPublic(created.id), null)).StatusCode);
        Assert.Equal(2, (await (await anna.Client.GetAsync(ApiRoutes.Lobbies)).Read<LobbyList>()).lobbies[0].riderCount);
    }

    [Fact]
    public async Task FullRideRefusesMoreRiders()
    {
        var sami = await TestRider.Create(_server, "Sami");
        var anna = await TestRider.Create(_server, "Anna");
        var ben = await TestRider.Create(_server, "Ben");
        var created = await Create(sami, new CreateLobbyRequest { name = "Duo", maxRiders = 2 });
        Assert.Equal(HttpStatusCode.OK, (await anna.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = created.code })).StatusCode);
        var third = await ben.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = created.code });
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal(ApiErrors.LobbyFull, (await third.Read<ApiError>()).error);

        // Joining again while already in it is fine (e.g. after an app restart).
        Assert.Equal(HttpStatusCode.OK, (await anna.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = created.code })).StatusCode);
    }

    [Fact]
    public async Task HostLeavingPassesTheHostOnAndTheLastRiderClosesTheRide()
    {
        var sami = await TestRider.Create(_server, "Sami");
        var anna = await TestRider.Create(_server, "Anna");
        var created = await Create(sami, new CreateLobbyRequest { name = "Evening" });
        await anna.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = created.code });

        await sami.Client.PostAsync(ApiRoutes.LeaveLobby, null);
        var current = await (await anna.Client.GetAsync(ApiRoutes.CurrentLobby)).Read<LobbyDetails>();
        Assert.Equal(anna.Id, current.hostRiderId);
        Assert.Equal(HttpStatusCode.NoContent, (await sami.Client.GetAsync(ApiRoutes.CurrentLobby)).StatusCode);

        await anna.Client.PostAsync(ApiRoutes.LeaveLobby, null);
        var gone = await sami.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = created.code });
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task ARiderIsInOneRideAtATime()
    {
        var sami = await TestRider.Create(_server, "Sami");
        var anna = await TestRider.Create(_server, "Anna");
        var first = await Create(sami, new CreateLobbyRequest { name = "First" });
        var second = await Create(anna, new CreateLobbyRequest { name = "Second" });
        await sami.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = second.code });

        Assert.Equal(second.id, (await (await sami.Client.GetAsync(ApiRoutes.CurrentLobby)).Read<LobbyDetails>()).id);
        // Sami was alone in the first ride, so it closed.
        Assert.Equal(HttpStatusCode.NotFound, (await anna.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = first.code })).StatusCode);
    }

    [Fact]
    public async Task IdleRideCloses()
    {
        var sami = await TestRider.Create(_server, "Sami");
        var anna = await TestRider.Create(_server, "Anna");
        var created = await Create(sami, new CreateLobbyRequest { name = "Forgotten" });
        _server.Time.Advance(LobbyRegistry.IdleTimeout + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.NotFound, (await anna.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = created.code })).StatusCode);
    }

    [Theory]
    [InlineData("X", 8)]
    [InlineData("Fine name", 1)]
    [InlineData("Fine name", 21)]
    public async Task RideSettingsAreValidated(string name, int maxRiders)
    {
        var sami = await TestRider.Create(_server, "Sami");
        var response = await sami.Client.Post(ApiRoutes.Lobbies, new CreateLobbyRequest { name = name, maxRiders = maxRiders });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrors.InvalidLobby, (await response.Read<ApiError>()).error);
    }

    [Fact]
    public async Task GuessingCodesIsRateLimited()
    {
        using var limited = new ServerFactory(codeJoinsPerMinute: 3);
        var anna = await TestRider.Create(limited, "Anna");
        var codes = new List<HttpStatusCode>();
        foreach (string guess in new[] { "AAAA", "AAAB", "AAAC", "AAAD", "AAAE" })
        {
            codes.Add((await anna.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = guess })).StatusCode);
        }

        Assert.Equal(3, codes.Count(c => c == HttpStatusCode.NotFound));
        Assert.Equal(2, codes.Count(c => c == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public void CodesAreUniqueAmongOpenRides()
    {
        var registry = new LobbyRegistry(TimeProvider.System);
        var codes = Enumerable.Range(0, 2000).Select(i => registry.Create(new CreateLobbyRequest { name = "Ride " + i }, "rider" + i).Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[A-HJKMNP-Z]{4}$", c));
    }

    [Fact]
    public void CodeNormalization()
    {
        Assert.Equal("ABCD", LobbyRules.NormalizeCode(" ab-cd "));
        Assert.Equal("", LobbyRules.NormalizeCode(null));
    }

    private static async Task<LobbyDetails> Create(TestRider rider, CreateLobbyRequest request)
    {
        var response = await rider.Client.Post(ApiRoutes.Lobbies, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Read<LobbyDetails>();
    }
}
