using System.Net;
using System.Text.Json;
using FreeVelo.Shared.Api;
using FreeVelo.Shared.Live;
using Microsoft.AspNetCore.TestHost;

namespace FreeVelo.Server.Tests;

/// <summary>System.Text.Json for the client in tests (the app uses Unity's JsonUtility).</summary>
public sealed class TestJson : IJsonCodec
{
    public string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), ServerFactory.Json);

    public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, ServerFactory.Json)!;
}

/// <summary>The app's client code (FreeVelo.Shared) against the real server.</summary>
public sealed class ClientTests : IDisposable
{
    private readonly ServerFactory _server = new();

    public void Dispose() => _server.Dispose();

    private FreeVeloApi NewApi() => new(_server.CreateClient(), new TestJson());

    [Fact]
    public async Task FirstStartCreateRideJoinWithCodeLeave()
    {
        var sami = NewApi();
        var credentials = await sami.CreateRiderAsync("Sami");
        Assert.Equal("Sami", credentials.displayName);
        Assert.Same(credentials, sami.Credentials);

        var lobby = await sami.CreateLobbyAsync(new CreateLobbyRequest { name = "Sunday", mode = LobbyMode.KeepTogether });
        Assert.Equal(4, lobby.code.Length);

        var anna = NewApi();
        await anna.CreateRiderAsync("Anna");
        var joined = await anna.JoinByCodeAsync(lobby.code.ToLowerInvariant());
        Assert.Equal(2, joined.riders.Length);
        Assert.Equal(lobby.id, (await anna.CurrentLobbyAsync())!.id);

        var error = await Assert.ThrowsAsync<ApiException>(() => anna.JoinByCodeAsync("ZZZZ"));
        Assert.Equal(HttpStatusCode.NotFound, error.Status);
        Assert.Equal(ApiErrors.LobbyNotFound, error.Code);

        await anna.LeaveLobbyAsync();
        Assert.Null(await anna.CurrentLobbyAsync());
        Assert.Empty((await anna.PublicLobbiesAsync()).lobbies); // Sami's ride is private
    }

    [Fact]
    public async Task StoredCredentialsSignInAgainAfterAnAppRestart()
    {
        var first = NewApi();
        var credentials = await first.CreateRiderAsync("Sami");

        var restarted = NewApi();
        restarted.Credentials = new RiderCredentials { riderId = credentials.riderId, secret = credentials.secret };
        var renamed = await restarted.RenameAsync("Sami S.");
        Assert.Equal("Sami S.", renamed.displayName);

        await restarted.DeleteRiderAsync();
        Assert.Null(restarted.Credentials);
        var gone = new FreeVeloApi(_server.CreateClient(), new TestJson()) { Credentials = credentials };
        var error = await Assert.ThrowsAsync<ApiException>(() => gone.CurrentLobbyAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, error.Status);
    }

    [Fact]
    public async Task InvalidNameIsReportedWithItsCode()
    {
        var error = await Assert.ThrowsAsync<ApiException>(() => NewApi().CreateRiderAsync("X"));
        Assert.Equal(ApiErrors.InvalidDisplayName, error.Code);
    }

    [Fact]
    public async Task LiveLinkFillsTheGroupStateAndSendsStates()
    {
        var sami = NewApi();
        await sami.CreateRiderAsync("Sami");
        var lobby = await sami.CreateLobbyAsync(new CreateLobbyRequest { name = "Sunday" });
        var anna = NewApi();
        await anna.CreateRiderAsync("Anna");
        await anna.JoinByCodeAsync(lobby.code);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        double Now() => clock.Elapsed.TotalSeconds;
        var samiState = new GroupRideState();
        var annaState = new GroupRideState();
        using var samiLink = new LiveLink(await Connect(sami), samiState, Now);
        using var annaLink = new LiveLink(await Connect(anna), annaState, Now);

        for (int i = 0; i < 40; i++)
        {
            samiLink.Post(new RiderStateMessage { Wkg = 3.5f, CadenceRpm = 90, LaneM = samiState.MyHomeLaneM });
            annaLink.Post(new RiderStateMessage { Wkg = 2.5f, CadenceRpm = 85, LaneM = annaState.MyHomeLaneM });
            await Task.Delay(50);
        }

        Assert.Equal(LobbyMode.KeepTogether, annaState.Mode);
        Assert.Equal(1, annaState.WelcomeCount);
        var riders = annaState.Riders(Now());
        Assert.Equal(2, riders.Count);
        var me = Assert.Single(riders, r => r.IsMe);
        Assert.Equal("Anna", me.DisplayName);
        var other = Assert.Single(riders, r => !r.IsMe);
        Assert.Equal("Sami", other.DisplayName);
        Assert.True(other.DistanceM > me.DistanceM, $"Sami pushes more W/kg and rides ahead: sami {other.DistanceM} {other.Wkg} {other.Flags}, anna {me.DistanceM} {me.Wkg} {me.Flags}");
        Assert.True(annaState.TryGetMine(Now(), out var mine) && mine.DistanceM > 0);

        samiLink.Post(new EmojiMessage { Emoji = Emoji.Fire });
        await WaitUntil(() => annaState.EmojisSince(0).Count == 1);
        Assert.Equal("Sami", annaState.NameOf(annaState.EmojisSince(0)[0].Slot));

        // Anna leaves: the server ends her connection with "left the ride".
        await anna.LeaveLobbyAsync();
        await annaLink.Receiving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LiveLink.LeftRide, annaLink.CloseStatus);
        await samiLink.CloseAsync();
    }

    [Fact]
    public void RidersAreInterpolatedBetweenSnapshotsAndExtrapolatedBriefly()
    {
        var state = new GroupRideState();
        state.Apply(new WelcomeMessage { YourSlot = 1 }, 0);
        state.Apply(new RosterMessage { Riders = { new RosterEntry { Slot = 1, DisplayName = "Me" }, new RosterEntry { Slot = 2, DisplayName = "Ben" } } }, 0);
        state.Apply(Snapshot(2, 100, 10), 1.0);
        state.Apply(Snapshot(2, 102.5, 10), 1.25);

        // Shown 0.35 s in the past: at 1.475 s → 1.125 s, halfway between the snapshots.
        Assert.Equal(101.25, state.Riders(1.475).Single().DistanceM, 6);

        // No more snapshots: carried on at its speed, at most 1 s.
        Assert.Equal(102.5 + (10 * 0.5), state.Riders(1.25 + 0.35 + 0.5).Single().DistanceM, 6);
        Assert.Equal(102.5 + 10, state.Riders(10).Single().DistanceM, 6);

        // Before the first snapshot: the first one.
        Assert.Equal(100, state.Riders(0.5).Single().DistanceM, 6);
        Assert.False(state.TryGetMine(2, out _)); // no snapshot of slot 1 yet
    }

    [Fact]
    public void RosterChangesDropRidersAndBumpTheVersion()
    {
        var state = new GroupRideState();
        state.Apply(new RosterMessage { Riders = { new RosterEntry { Slot = 2 } } }, 0);
        int version = state.RosterVersion;
        state.Apply(Snapshot(2, 10, 5), 0.1);
        state.Apply(new RosterMessage(), 0.2);
        Assert.NotEqual(version, state.RosterVersion);
        Assert.Empty(state.Riders(1));
    }

    [Theory]
    [InlineData("http://192.168.1.20:8080", "ws://192.168.1.20:8080/api/v1/live")]
    [InlineData("https://freevelo.duckdns.org", "wss://freevelo.duckdns.org/api/v1/live")]
    public void LiveUriFollowsTheServerAddress(string server, string live)
    {
        Assert.Equal(live, FreeVeloApi.LiveUri(new Uri(server)).ToString());
    }

    private static SnapshotMessage Snapshot(ushort slot, double distance, float speed) => new()
    {
        Riders = { new SnapshotEntry { Slot = slot, DistanceM = distance, SpeedMps = speed } },
    };

    /// <summary>TestServer cannot be reached by ClientWebSocket, so tests connect through its own client.</summary>
    private async Task<System.Net.WebSockets.WebSocket> Connect(FreeVeloApi api)
    {
        string token = await api.AccessTokenAsync();
        var client = _server.Server.CreateWebSocketClient();
        client.ConfigureRequest = r => r.Headers.Authorization = "Bearer " + token;
        return await client.ConnectAsync(FreeVeloApi.LiveUri(new Uri("http://localhost")), CancellationToken.None);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }
}
