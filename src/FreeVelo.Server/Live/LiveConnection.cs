using System.Net.WebSockets;
using System.Threading.Channels;

namespace FreeVelo.Server.Live;

/// <summary>
/// One app's WebSocket. Outgoing frames go through a small queue with one writer task (WebSockets allow only one
/// send at a time); if a phone falls behind, the oldest frames are dropped – a newer snapshot replaces them anyway.
/// </summary>
public sealed class LiveConnection
{
    private readonly Channel<byte[]> _outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(16)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly CancellationTokenSource _closed = new();
    private (WebSocketCloseStatus Status, string Reason)? _closeRequest;

    public LiveConnection(WebSocket socket, string riderId)
    {
        Socket = socket;
        RiderId = riderId;
    }

    public WebSocket Socket { get; }

    public string RiderId { get; }

    /// <summary>Cancelled when the server closes this connection (replaced, rider left, ride closed).</summary>
    public CancellationToken Closed => _closed.Token;

    public void Send(byte[] frame) => _outbox.Writer.TryWrite(frame);

    /// <summary>Closes the connection after the queued frames are sent.</summary>
    public void Close(WebSocketCloseStatus status, string reason)
    {
        _closeRequest ??= (status, reason);
        _outbox.Writer.TryComplete();
    }

    public async Task SendLoop(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _outbox.Reader.ReadAllAsync(cancellationToken))
            {
                if (Socket.State != WebSocketState.Open) break;
                await Socket.SendAsync(frame, WebSocketMessageType.Binary, true, cancellationToken);
            }

            // Server-side close, or the answer to the app's close (state CloseReceived): completes the handshake.
            if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                var (status, reason) = _closeRequest ?? (WebSocketCloseStatus.NormalClosure, "bye");
                await Socket.CloseOutputAsync(status, reason, cancellationToken);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
            // The app went away; the receive loop notices too.
        }
        finally
        {
            _closed.Cancel();
        }
    }
}
