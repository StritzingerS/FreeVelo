using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace FreeVelo.Shared.Live
{
    /// <summary>
    /// The app's end of the live WebSocket: receives on a background task into a <see cref="GroupRideState"/>,
    /// sends rider states and emojis (one send at a time, safe from any thread). When the connection ends,
    /// <see cref="CloseStatus"/> tells why (see the server's close codes, e.g. 4410 = left the ride).
    /// </summary>
    public sealed class LiveLink : IDisposable
    {
        /// <summary>Custom close codes sent by the server.</summary>
        public const int NotInRide = 4404;
        public const int Replaced = 4409;
        public const int LeftRide = 4410;
        public const int RideClosed = 4411;

        private readonly WebSocket _socket;
        private readonly GroupRideState _state;
        private readonly Func<double> _clock;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public LiveLink(WebSocket socket, GroupRideState state, Func<double> clockS)
        {
            _socket = socket;
            _state = state;
            _clock = clockS;
            Receiving = Task.Run(ReceiveLoop);
        }

        /// <summary>Completes when the connection has ended.</summary>
        public Task Receiving { get; }

        public bool IsOpen => _socket.State == WebSocketState.Open && !Receiving.IsCompleted;

        /// <summary>Why the connection ended (null while open or when it simply dropped).</summary>
        public int? CloseStatus { get; private set; }

        /// <summary>Sends without waiting for the network; a frame that cannot be sent right now is dropped.</summary>
        public void Post(LiveMessage message)
        {
            if (!IsOpen) return;
            byte[] frame = LiveCodec.Encode(message);
            _ = SendFrame(frame);
        }

        public async Task CloseAsync()
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    using var timeout = new CancellationTokenSource(2000);
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is ObjectDisposedException)
            {
            }

            _stop.Cancel();
        }

        public void Dispose()
        {
            _stop.Cancel();
            _socket.Dispose();
        }

        private async Task SendFrame(byte[] frame)
        {
            if (!await _sendLock.WaitAsync(0).ConfigureAwait(false)) return; // previous send still running: drop
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                // Connection gone; the receive loop ends too.
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[LiveCodec.MaxFrameBytes];
            try
            {
                while (_socket.State == WebSocketState.Open)
                {
                    int length = 0;
                    WebSocketReceiveResult result;
                    do
                    {
                        if (length >= buffer.Length) return;
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), _stop.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            CloseStatus = (int?)result.CloseStatus;
                            return;
                        }

                        length += result.Count;
                    }
                    while (!result.EndOfMessage);

                    LiveMessage message;
                    try
                    {
                        message = LiveCodec.Decode(buffer, length);
                    }
                    catch (FormatException)
                    {
                        continue; // unknown message (newer server): ignore
                    }

                    if (message is ByeMessage bye)
                    {
                        // The server ends the connection; the reason arrives here even if a proxy loses the close frame.
                        CloseStatus = bye.Code;
                        _ = CloseAsync();
                        return;
                    }

                    _state.Apply(message, _clock());
                }
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is ObjectDisposedException)
            {
                // Dropped (dead spot, server restart).
            }
        }
    }
}
