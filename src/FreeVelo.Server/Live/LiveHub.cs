using System.Collections.Concurrent;
using System.Net.WebSockets;
using FreeVelo.Server.Lobbies;
using FreeVelo.Server.Riders;
using FreeVelo.Shared.Live;

namespace FreeVelo.Server.Live;

public sealed class LiveOptions
{
    /// <summary>Server tick: snapshots per second = 1000 / TickMs (4 Hz).</summary>
    public int TickMs { get; set; } = 250;

    /// <summary>How long a rider whose app disconnected stays in the ride (dead spot, app restart).</summary>
    public int AwayGraceSeconds { get; set; } = 120;
}

/// <summary>
/// All live group rides. The REST registry decides who is in which ride; the hub follows it every tick, runs the
/// WebSocket of each app (receive loop here, send loop in <see cref="LiveConnection"/>) and ticks every session.
/// </summary>
public sealed class LiveHub
{
    private readonly ConcurrentDictionary<string, LobbySession> _sessions = new();
    private readonly LobbyRegistry _lobbies;
    private readonly RiderStore _riders;
    private readonly TimeProvider _time;
    private readonly LiveOptions _options;
    private readonly ILogger<LiveHub> _log;

    public LiveHub(LobbyRegistry lobbies, RiderStore riders, TimeProvider time, LiveOptions options, ILogger<LiveHub> log)
    {
        _lobbies = lobbies;
        _riders = riders;
        _time = time;
        _options = options;
        _log = log;
    }

    public int SessionCount => _sessions.Count;

    /// <summary>Runs one app connection until it closes.</summary>
    public async Task Run(WebSocket socket, string riderId, CancellationToken aborted)
    {
        var connection = new LiveConnection(socket, riderId);
        var lobby = _lobbies.Of(riderId);
        if (lobby == null)
        {
            _log.LogInformation("Live: rider {Rider} connected without a ride", riderId[..6]);
            await socket.SendAsync(LiveCodec.Encode(new ByeMessage { Code = (ushort)LiveCloseCodes.NotInRide }), WebSocketMessageType.Binary, true, aborted);
            await socket.CloseAsync(LiveCloseCodes.NotInRide, "not_in_ride", aborted);
            return;
        }

        var session = _sessions.GetOrAdd(lobby.Id, id => new LobbySession(id, lobby.Mode, lobby.RouteId, _time, _options.TickMs));
        string name = _riders.Find(riderId)?.DisplayName ?? "";
        lock (session.Sync) session.Attach(riderId, name, connection);
        _lobbies.Touch(riderId);
        _log.LogInformation("Live: {Name} ({Rider}) connected to ride {Lobby}", name, riderId[..6], lobby.Code);
        var started = DateTime.UtcNow;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(aborted, connection.Closed);
        var sending = connection.SendLoop(aborted);
        try
        {
            await ReceiveLoop(connection, session, stop.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
            // Connection dropped or closed by the server.
        }
        finally
        {
            _log.LogInformation("Live: {Name} ({Rider}) disconnected from ride {Lobby} after {Seconds:0} s ({State}, {Status})", name, riderId[..6], lobby.Code, (DateTime.UtcNow - started).TotalSeconds, socket.State, socket.CloseStatus);
            lock (session.Sync) session.Detach(riderId, connection);
            connection.Close(WebSocketCloseStatus.NormalClosure, "bye");
            await sending;
        }
    }

    private async Task ReceiveLoop(LiveConnection connection, LobbySession session, CancellationToken token)
    {
        var buffer = new byte[LiveCodec.MaxFrameBytes];
        while (connection.Socket.State == WebSocketState.Open)
        {
            int length = 0;
            WebSocketReceiveResult result;
            do
            {
                if (length >= buffer.Length)
                {
                    await connection.Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "too_big", token);
                    return;
                }

                result = await connection.Socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                length += result.Count;
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Binary) continue;
            LiveMessage message;
            try
            {
                message = LiveCodec.Decode(buffer, length);
            }
            catch (FormatException)
            {
                continue; // ignore broken frames (e.g. a newer app version)
            }

            lock (session.Sync) session.Handle(connection.RiderId, message);
        }
    }

    /// <summary>One tick for all rides (4 times a second).</summary>
    public void Tick(double dtSeconds)
    {
        var grace = TimeSpan.FromSeconds(_options.AwayGraceSeconds);
        foreach (var (lobbyId, session) in _sessions)
        {
            try
            {
                var lobby = _lobbies.Get(lobbyId);
                lock (session.Sync)
                {
                    if (lobby == null)
                    {
                        session.CloseAll(LiveCloseCodes.RideClosed, "ride_closed");
                        _sessions.TryRemove(lobbyId, out _);
                        continue;
                    }

                    // Riders away too long leave the ride (the registry closes it when nobody is left).
                    foreach (string away in session.AwayLongerThan(grace)) _lobbies.Leave(away);

                    // Follow the registry: joins and leaves over REST.
                    var (_, members) = _lobbies.Members(lobby);
                    foreach (string gone in session.MemberIds.Except(members).ToList()) session.RemoveMember(gone, LiveCloseCodes.LeftRide, "left_ride");
                    foreach (string joined in members.Except(session.MemberIds).ToList()) session.AddMember(joined, _riders.Find(joined)?.DisplayName ?? "");

                    if (session.HasConnections)
                    {
                        foreach (string id in session.MemberIds) _lobbies.Touch(id);
                    }

                    session.Tick(dtSeconds);
                }
            }
            catch (Exception e)
            {
                _log.LogError(e, "Tick of ride {LobbyId} failed", lobbyId);
            }
        }
    }
}

/// <summary>Drives <see cref="LiveHub.Tick"/> at a fixed rate.</summary>
public sealed class LiveTicker : BackgroundService
{
    private readonly LiveHub _hub;
    private readonly LiveOptions _options;

    public LiveTicker(LiveHub hub, LiveOptions options)
    {
        _hub = hub;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(_options.TickMs);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _hub.Tick(interval.TotalSeconds);
        }
    }
}
