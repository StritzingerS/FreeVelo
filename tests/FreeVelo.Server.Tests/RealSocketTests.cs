using FreeVelo.Shared.Api;
using FreeVelo.Shared.Live;

namespace FreeVelo.Server.Tests;

/// <summary>
/// The app's own WebSocket client (TinyWebSocket, used because ClientWebSocket fails under Unity's Mono) against the
/// server on a real Kestrel socket.
/// </summary>
public sealed class RealSocketTests : IDisposable
{
    private readonly ServerFactory _server = new();

    public RealSocketTests()
    {
        _server.UseKestrel(0);
        _server.StartServer();
    }

    public void Dispose() => _server.Dispose();

    private FreeVeloApi NewApi() => new(_server.CreateClient(), new TestJson());

    [Fact]
    public async Task GroupRideOverRealSockets()
    {
        var sami = NewApi();
        await sami.CreateRiderAsync("Sami");
        var lobby = await sami.CreateLobbyAsync(new CreateLobbyRequest { name = "Real sockets" });
        var anna = NewApi();
        await anna.CreateRiderAsync("Anna");
        await anna.JoinByCodeAsync(lobby.code);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        double Now() => clock.Elapsed.TotalSeconds;
        var samiState = new GroupRideState();
        var annaState = new GroupRideState();
        var samiSocket = await sami.ConnectLiveAsync();
        Assert.IsType<TinyWebSocket>(samiSocket);
        using var samiLink = new LiveLink(samiSocket, samiState, Now);
        using var annaLink = new LiveLink(await anna.ConnectLiveAsync(), annaState, Now);

        for (int i = 0; i < 40; i++)
        {
            samiLink.Post(new RiderStateMessage { Wkg = 3.5f, CadenceRpm = 90 });
            annaLink.Post(new RiderStateMessage { Wkg = 2.5f, CadenceRpm = 85 });
            await Task.Delay(50);
        }

        var riders = annaState.Riders(Now());
        Assert.Equal(2, riders.Count);
        Assert.True(riders.Single(r => !r.IsMe).DistanceM > riders.Single(r => r.IsMe).DistanceM);

        // Server-side close (left the ride) reaches the app with its reason.
        await anna.LeaveLobbyAsync();
        await annaLink.Receiving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LiveLink.LeftRide, annaLink.CloseStatus);

        // App-side close completes the handshake.
        await samiLink.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConnectWithoutRideIsTurnedAway()
    {
        var loner = NewApi();
        await loner.CreateRiderAsync("Loner");
        using var link = new LiveLink(await loner.ConnectLiveAsync(), new GroupRideState(), () => 0);
        await link.Receiving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LiveLink.NotInRide, link.CloseStatus);
    }
}
