using System.Net.WebSockets;
using FreeVelo.Server.Live;
using FreeVelo.Shared.Api;
using FreeVelo.Shared.Live;
using Microsoft.AspNetCore.TestHost;

namespace FreeVelo.Server.Tests;

/// <summary>An app's live connection in a test: sends rider states, collects what the server sends.</summary>
public sealed class LiveClient : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly List<LiveMessage> _received = new();
    private readonly Task _receiving;

    private LiveClient(WebSocket socket)
    {
        _socket = socket;
        _receiving = Receive();
    }

    public WebSocketCloseStatus? CloseStatus { get; private set; }

    public bool IsClosed => CloseStatus != null;

    public static async Task<LiveClient> Connect(ServerFactory server, TestRider rider)
    {
        var client = server.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = "Bearer " + rider.AccessToken;
        var socket = await client.ConnectAsync(new Uri("ws://localhost" + ApiRoutes.Live), CancellationToken.None);
        return new LiveClient(socket);
    }

    public Task Send(LiveMessage message) =>
        _socket.SendAsync(LiveCodec.Encode(message), WebSocketMessageType.Binary, true, CancellationToken.None);

    public Task SendState(double wkg, byte cadence = 85, double distanceM = 0, float lane = 0) =>
        Send(new RiderStateMessage { Wkg = (float)wkg, CadenceRpm = cadence, DistanceM = distanceM, LaneM = lane, SpeedMps = 8 });

    public List<T> All<T>() where T : LiveMessage
    {
        lock (_received) return _received.OfType<T>().ToList();
    }

    /// <summary>Waits for a message of type <typeparamref name="T"/> matching <paramref name="match"/> (newest first).</summary>
    public async Task<T> WaitFor<T>(Func<T, bool>? match = null, int timeoutMs = 5000) where T : LiveMessage
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            var found = All<T>().LastOrDefault(m => match == null || match(m));
            if (found != null) return found;
            await Task.Delay(20);
        }

        throw new TimeoutException("No " + typeof(T).Name);
    }

    public async Task WaitClosed(int timeoutMs = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!IsClosed && DateTime.UtcNow < until) await Task.Delay(20);
        if (!IsClosed) throw new TimeoutException("Still open");
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                using var timeout = new CancellationTokenSource(2000);
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or InvalidOperationException)
            {
            }
        }

        await Task.WhenAny(_receiving, Task.Delay(1000));
        _socket.Dispose();
    }

    private async Task Receive()
    {
        var buffer = new byte[LiveCodec.MaxFrameBytes];
        try
        {
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    CloseStatus = result.CloseStatus;
                    return;
                }

                var message = LiveCodec.Decode(buffer, result.Count);
                lock (_received) _received.Add(message);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException)
        {
            CloseStatus ??= WebSocketCloseStatus.EndpointUnavailable;
        }
    }
}

public sealed class LiveTests : IDisposable
{
    private readonly ServerFactory _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task KeepTogetherRideStrongerRiderAheadBothMoving()
    {
        var (sami, anna, _) = await RideTogether(LobbyMode.KeepTogether);
        await using var samiLive = await LiveClient.Connect(_server, sami);
        await using var annaLive = await LiveClient.Connect(_server, anna);

        var welcome = await samiLive.WaitFor<WelcomeMessage>();
        Assert.Equal(LobbyMode.KeepTogether, welcome.Mode);
        var roster = await annaLive.WaitFor<RosterMessage>(r => r.Riders.Count == 2);
        Assert.Equal(new[] { "Sami", "Anna" }, roster.Riders.Select(r => r.DisplayName));
        Assert.NotEqual(roster.Riders[0].HomeLaneM, roster.Riders[1].HomeLaneM);
        ushort samiSlot = roster.Riders.Single(r => r.RiderId == sami.Id).Slot;
        ushort annaSlot = roster.Riders.Single(r => r.RiderId == anna.Id).Slot;

        // Sami 3.5 W/kg, Anna 2.5 W/kg for 2 s of server ticks (40 ticks at 20 Hz, each simulating 50 ms).
        for (int i = 0; i < 40; i++)
        {
            await samiLive.SendState(3.5);
            await annaLive.SendState(2.5);
            await Task.Delay(50);
        }

        var snapshot = await annaLive.WaitFor<SnapshotMessage>(s => s.Riders.Count == 2 && s.Riders.All(r => r.Flags.HasFlag(RiderFlags.Active)));
        var samiPos = snapshot.Riders.Single(r => r.Slot == samiSlot);
        var annaPos = snapshot.Riders.Single(r => r.Slot == annaSlot);
        Assert.True(snapshot.AnchorDistanceM > 0, "the group moves");
        Assert.True(samiPos.DistanceM > annaPos.DistanceM, "the stronger rider is ahead");
        Assert.True(samiPos.Flags.HasFlag(RiderFlags.Connected));
        Assert.Equal(3.5f, samiPos.Wkg);
        Assert.Equal(85, samiPos.CadenceRpm);

        // Snapshots keep coming with increasing ticks.
        var later = await annaLive.WaitFor<SnapshotMessage>(s => s.Tick > snapshot.Tick + 3);
        Assert.True(later.AnchorDistanceM > snapshot.AnchorDistanceM);
    }

    [Fact]
    public async Task FreeModeRelaysOwnPositionsAndJoinerStartsAtTheGroup()
    {
        var (sami, anna, _) = await RideTogether(LobbyMode.Free);
        await using var samiLive = await LiveClient.Connect(_server, sami);
        await samiLive.WaitFor<WelcomeMessage>();
        await samiLive.SendState(2.0, distanceM: 1234.5, lane: 1.2f);
        await samiLive.WaitFor<SnapshotMessage>(s => s.Riders.Any(r => r.DistanceM == 1234.5));

        // Anna joins mid-ride: she starts where the group is.
        await using var annaLive = await LiveClient.Connect(_server, anna);
        var welcome = await annaLive.WaitFor<WelcomeMessage>();
        Assert.Equal(LobbyMode.Free, welcome.Mode);
        Assert.Equal(1234.5, welcome.StartDistanceM);

        var snapshot = await annaLive.WaitFor<SnapshotMessage>(s => s.Riders.Any(r => r.DistanceM == 1234.5));
        var samiEntry = snapshot.Riders.Single(r => r.DistanceM == 1234.5);
        Assert.Equal(1.2f, samiEntry.LaneM);
        Assert.Equal(0, snapshot.AnchorDistanceM);
    }

    [Fact]
    public async Task EmojiReachesEveryoneButNotTooOften()
    {
        var (sami, anna, _) = await RideTogether(LobbyMode.KeepTogether);
        await using var samiLive = await LiveClient.Connect(_server, sami);
        await using var annaLive = await LiveClient.Connect(_server, anna);
        var welcome = await samiLive.WaitFor<WelcomeMessage>();

        await samiLive.Send(new EmojiMessage { Emoji = Emoji.Fire });
        await samiLive.Send(new EmojiMessage { Emoji = Emoji.Clap }); // within a second: dropped
        var e = await annaLive.WaitFor<EmojiEventMessage>();
        Assert.Equal(Emoji.Fire, e.Emoji);
        Assert.Equal(welcome.YourSlot, e.Slot);
        await Task.Delay(300);
        Assert.Single(annaLive.All<EmojiEventMessage>());
    }

    [Fact]
    public async Task RiderNotInARideIsTurnedAway()
    {
        var loner = await TestRider.Create(_server, "Loner");
        await using var live = await LiveClient.Connect(_server, loner);
        await live.WaitClosed();
        Assert.Equal(LiveCloseCodes.NotInRide, live.CloseStatus);
    }

    [Fact]
    public async Task LeavingOverRestClosesTheLiveConnectionAndUpdatesTheRoster()
    {
        var (sami, anna, _) = await RideTogether(LobbyMode.KeepTogether);
        await using var samiLive = await LiveClient.Connect(_server, sami);
        await using var annaLive = await LiveClient.Connect(_server, anna);
        await samiLive.WaitFor<RosterMessage>(r => r.Riders.Count == 2);

        await anna.Client.PostAsync(ApiRoutes.LeaveLobby, null);
        await annaLive.WaitClosed();
        Assert.Equal(LiveCloseCodes.LeftRide, annaLive.CloseStatus);
        await samiLive.WaitFor<RosterMessage>(r => r.Riders.Count == 1);
        await samiLive.WaitFor<SnapshotMessage>(s => s.Riders.Count == 1);
    }

    [Fact]
    public async Task ReconnectReplacesTheOldConnectionAndKeepsTheSlot()
    {
        var (sami, _, _) = await RideTogether(LobbyMode.KeepTogether);
        await using var first = await LiveClient.Connect(_server, sami);
        var firstWelcome = await first.WaitFor<WelcomeMessage>();

        await using var second = await LiveClient.Connect(_server, sami);
        var secondWelcome = await second.WaitFor<WelcomeMessage>();
        await first.WaitClosed();
        Assert.Equal(LiveCloseCodes.Replaced, first.CloseStatus);
        Assert.Equal(firstWelcome.YourSlot, secondWelcome.YourSlot);
    }

    [Fact]
    public async Task DisconnectedRiderIsTowedAndStaysInTheRide()
    {
        var (sami, anna, _) = await RideTogether(LobbyMode.KeepTogether);
        await using var samiLive = await LiveClient.Connect(_server, sami);
        var annaLive = await LiveClient.Connect(_server, anna);
        var roster = await samiLive.WaitFor<RosterMessage>(r => r.Riders.Count == 2);
        ushort annaSlot = roster.Riders.Single(r => r.RiderId == anna.Id).Slot;
        await annaLive.DisposeAsync(); // dead spot

        for (int i = 0; i < 80; i++)
        {
            await samiLive.SendState(2.5);
            await Task.Delay(50);
        }

        var snapshot = await samiLive.WaitFor<SnapshotMessage>(s => s.Riders.Count == 2);
        var anna2 = snapshot.Riders.Single(r => r.Slot == annaSlot);
        Assert.False(anna2.Flags.HasFlag(RiderFlags.Connected));
        Assert.True(anna2.Flags.HasFlag(RiderFlags.Towed));

        var current = await (await anna.Client.GetAsync(ApiRoutes.CurrentLobby)).Read<LobbyDetails>();
        Assert.Equal(2, current.riders.Length);
    }

    [Fact]
    public async Task LiveNeedsAToken()
    {
        var client = _server.Server.CreateWebSocketClient();
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(new Uri("ws://localhost" + ApiRoutes.Live), CancellationToken.None));
    }

    /// <summary>Sami creates a ride, Anna joins with the code.</summary>
    private async Task<(TestRider Sami, TestRider Anna, LobbyDetails Lobby)> RideTogether(LobbyMode mode)
    {
        var sami = await TestRider.Create(_server, "Sami");
        var anna = await TestRider.Create(_server, "Anna");
        var lobby = await (await sami.Client.Post(ApiRoutes.Lobbies, new CreateLobbyRequest { name = "Test ride", mode = mode })).Read<LobbyDetails>();
        await anna.Client.Post(ApiRoutes.JoinByCode, new JoinByCodeRequest { code = lobby.code });
        return (sami, anna, lobby);
    }
}

public class LiveCodecTests
{
    [Fact]
    public void AllMessagesRoundTrip()
    {
        var state = (RiderStateMessage)RoundTrip(new RiderStateMessage { DistanceM = 12345.678, LaneM = -1.5f, SpeedMps = 9.25f, Wkg = 2.75f, CadenceRpm = 92 });
        Assert.Equal(12345.678, state.DistanceM);
        Assert.Equal(-1.5f, state.LaneM);
        Assert.Equal(92, state.CadenceRpm);

        Assert.Equal(Emoji.Wave, ((EmojiMessage)RoundTrip(new EmojiMessage { Emoji = Emoji.Wave })).Emoji);

        var welcome = (WelcomeMessage)RoundTrip(new WelcomeMessage { YourSlot = 3, Mode = LobbyMode.Free, StartDistanceM = 500 });
        Assert.Equal((3, LobbyMode.Free, 500.0), (welcome.YourSlot, welcome.Mode, welcome.StartDistanceM));

        var roster = (RosterMessage)RoundTrip(new RosterMessage { Riders = { new RosterEntry { Slot = 1, RiderId = "abc", DisplayName = "Jürgen 🚴", HomeLaneM = 0.5f } } });
        Assert.Equal("Jürgen 🚴", roster.Riders[0].DisplayName);

        var snapshot = (SnapshotMessage)RoundTrip(new SnapshotMessage
        {
            Tick = 77,
            AnchorDistanceM = 999.5,
            AnchorSpeedMps = 8.5f,
            Riders = { new SnapshotEntry { Slot = 2, DistanceM = 1000, LaneM = 1, SpeedMps = 8, Wkg = 3, CadenceRpm = 90, Flags = RiderFlags.Active | RiderFlags.Connected } },
        });
        Assert.Equal(77u, snapshot.Tick);
        Assert.Equal(RiderFlags.Active | RiderFlags.Connected, snapshot.Riders[0].Flags);

        var emoji = (EmojiEventMessage)RoundTrip(new EmojiEventMessage { Slot = 4, Emoji = Emoji.Muscle });
        Assert.Equal((4, Emoji.Muscle), (emoji.Slot, emoji.Emoji));
    }

    [Fact]
    public void SnapshotOfEightRidersIsSmall()
    {
        var snapshot = new SnapshotMessage();
        for (ushort i = 1; i <= 8; i++) snapshot.Riders.Add(new SnapshotEntry { Slot = i });
        Assert.InRange(LiveCodec.Encode(snapshot).Length, 1, 250);
    }

    [Fact]
    public void BrokenFramesAreRejected()
    {
        byte[] good = LiveCodec.Encode(new RiderStateMessage { Wkg = 2 });
        Assert.Throws<FormatException>(() => LiveCodec.Decode(good, good.Length - 1)); // truncated
        Assert.Throws<FormatException>(() => LiveCodec.Decode(good.Concat(new byte[] { 0 }).ToArray())); // trailing byte
        Assert.Throws<FormatException>(() => LiveCodec.Decode(new byte[] { 99 })); // unknown type
        Assert.Throws<FormatException>(() => LiveCodec.Decode(Array.Empty<byte>()));

        byte[] nan = LiveCodec.Encode(new RiderStateMessage { Wkg = float.NaN });
        Assert.Throws<FormatException>(() => LiveCodec.Decode(nan));
    }

    private static LiveMessage RoundTrip(LiveMessage message) => LiveCodec.Decode(LiveCodec.Encode(message));
}
