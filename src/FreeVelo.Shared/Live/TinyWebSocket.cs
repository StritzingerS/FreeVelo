using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FreeVelo.Shared.Live
{
    /// <summary>
    /// A small WebSocket client (RFC 6455) on a plain TCP connection with TLS. Used instead of
    /// <see cref="ClientWebSocket"/>, which fails to connect under Unity's Mono runtime ("Unable to connect to the
    /// remote server") although normal HTTPS works there. Supports text/binary messages, fragmentation, ping/pong and
    /// the close handshake; an HTTP proxy from HTTPS_PROXY (CONNECT) is used when set.
    /// </summary>
    public sealed class TinyWebSocket : WebSocket
    {
        private readonly TcpClient _tcp;
        private readonly Stream _stream;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly byte[] _header = new byte[14];
        private WebSocketState _state = WebSocketState.Open;
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeDescription;
        private bool _closeSent;
        private int _readers;

        // The frame being read: what is left of its payload, and its type.
        private long _remaining;
        private bool _fin = true;
        private WebSocketMessageType _messageType = WebSocketMessageType.Binary;

        private TinyWebSocket(TcpClient tcp, Stream stream)
        {
            _tcp = tcp;
            _stream = stream;
        }

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;

        public override string? CloseStatusDescription => _closeDescription;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        /// <summary>Connects (ws:// or wss://) and completes the opening handshake.</summary>
        public static async Task<TinyWebSocket> ConnectAsync(Uri uri, IDictionary<string, string>? headers, CancellationToken ct = default)
        {
            bool secure = uri.Scheme == "wss" || uri.Scheme == "https";
            int port = uri.IsDefaultPort ? (secure ? 443 : 80) : uri.Port;
            var tcp = new TcpClient { NoDelay = true };
            try
            {
                Stream stream;
                var proxy = Proxy(uri);
                if (proxy != null)
                {
                    await tcp.ConnectAsync(proxy.Host, proxy.Port).ConfigureAwait(false);
                    stream = tcp.GetStream();
                    await WriteAscii(stream, $"CONNECT {uri.Host}:{port} HTTP/1.1\r\nHost: {uri.Host}:{port}\r\n\r\n", ct).ConfigureAwait(false);
                    string reply = await ReadHeaders(stream, ct).ConfigureAwait(false);
                    if (!reply.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) && !reply.StartsWith("HTTP/1.0 200", StringComparison.Ordinal))
                    {
                        throw new WebSocketException("Proxy refused: " + FirstLine(reply));
                    }
                }
                else
                {
                    await tcp.ConnectAsync(uri.Host, port).ConfigureAwait(false);
                    stream = tcp.GetStream();
                }

                if (secure)
                {
                    var ssl = new SslStream(stream, false);
                    await ssl.AuthenticateAsClientAsync(uri.Host).ConfigureAwait(false);
                    stream = ssl;
                }

                string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                var request = new StringBuilder();
                request.Append("GET ").Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");
                request.Append("Host: ").Append(uri.IsDefaultPort ? uri.Host : uri.Host + ":" + port).Append("\r\n");
                request.Append("Upgrade: websocket\r\nConnection: Upgrade\r\n");
                request.Append("Sec-WebSocket-Key: ").Append(key).Append("\r\n");
                request.Append("Sec-WebSocket-Version: 13\r\n");
                if (headers != null)
                {
                    foreach (var h in headers) request.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
                }

                request.Append("\r\n");
                await WriteAscii(stream, request.ToString(), ct).ConfigureAwait(false);

                string response = await ReadHeaders(stream, ct).ConfigureAwait(false);
                if (!response.StartsWith("HTTP/1.1 101", StringComparison.Ordinal))
                {
                    throw new WebSocketException("The server did not accept the WebSocket: " + FirstLine(response));
                }

                string expected = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                if (response.IndexOf(expected, StringComparison.Ordinal) < 0) throw new WebSocketException("Bad Sec-WebSocket-Accept");

                return new TinyWebSocket(tcp, stream);
            }
            catch
            {
                tcp.Dispose();
                throw;
            }
        }

        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (_state != WebSocketState.Open && _state != WebSocketState.CloseReceived) throw new WebSocketException("The WebSocket is not open.");
            byte opcode = messageType == WebSocketMessageType.Text ? (byte)1 : (byte)2;
            await SendFrame(opcode, buffer, endOfMessage, cancellationToken).ConfigureAwait(false);
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readers);
            try
            {
                return await Receive(buffer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _readers);
            }
        }

        private async Task<WebSocketReceiveResult> Receive(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_closeStatus.HasValue && _remaining == 0)
                {
                    return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, _closeStatus, _closeDescription);
                }

                if (_remaining == 0 && !await ReadFrameHeader(cancellationToken).ConfigureAwait(false))
                {
                    continue; // ping / pong handled
                }

                if (_closeStatus.HasValue && _remaining == 0)
                {
                    return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, _closeStatus, _closeDescription);
                }

                int count = (int)Math.Min(_remaining, buffer.Count);
                if (count > 0) await ReadExactly(_stream, buffer.Array!, buffer.Offset, count, cancellationToken).ConfigureAwait(false);
                _remaining -= count;
                return new WebSocketReceiveResult(count, _messageType, _remaining == 0 && _fin);
            }
        }

        public override async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            if (_closeSent || _state == WebSocketState.Closed || _state == WebSocketState.Aborted) return;
            var payload = new List<byte> { (byte)((int)closeStatus >> 8), (byte)((int)closeStatus & 0xFF) };
            if (!string.IsNullOrEmpty(statusDescription)) payload.AddRange(Encoding.UTF8.GetBytes(statusDescription));
            _closeSent = true;
            try
            {
                await SendFrame(8, new ArraySegment<byte>(payload.ToArray()), true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException)
            {
                _state = WebSocketState.Aborted;
                return;
            }

            _state = _state == WebSocketState.CloseReceived ? WebSocketState.Closed : WebSocketState.CloseSent;
            if (_state == WebSocketState.Closed) Shutdown();
        }

        public override async Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            await CloseOutputAsync(closeStatus, statusDescription, cancellationToken).ConfigureAwait(false);

            // Wait for the server's close: a receive loop that is already reading sees it; otherwise read here
            // (data frames still arriving are skipped). Never two reads at once (TLS streams do not allow it).
            if (Volatile.Read(ref _readers) > 0)
            {
                while (_state == WebSocketState.CloseSent && !cancellationToken.IsCancellationRequested) await Task.Delay(20).ConfigureAwait(false);
            }
            else
            {
                var scratch = new byte[1024];
                while (_state == WebSocketState.CloseSent)
                {
                    var result = await ReceiveAsync(new ArraySegment<byte>(scratch), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                }
            }

            Shutdown();
        }

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
            Shutdown();
        }

        public override void Dispose()
        {
            if (_state != WebSocketState.Closed) _state = WebSocketState.Aborted;
            Shutdown();
        }

        /// <summary>Reads the next frame header. Returns false after handling a control frame (ping/pong/close).</summary>
        private async Task<bool> ReadFrameHeader(CancellationToken ct)
        {
            await ReadExactly(_stream, _header, 0, 2, ct).ConfigureAwait(false);
            bool fin = (_header[0] & 0x80) != 0;
            int opcode = _header[0] & 0x0F;
            bool masked = (_header[1] & 0x80) != 0;
            long length = _header[1] & 0x7F;
            if (length == 126)
            {
                await ReadExactly(_stream, _header, 0, 2, ct).ConfigureAwait(false);
                length = (_header[0] << 8) | _header[1];
            }
            else if (length == 127)
            {
                await ReadExactly(_stream, _header, 0, 8, ct).ConfigureAwait(false);
                length = 0;
                for (int i = 0; i < 8; i++) length = (length << 8) | _header[i];
            }

            byte[]? mask = null;
            if (masked)
            {
                mask = new byte[4];
                await ReadExactly(_stream, mask, 0, 4, ct).ConfigureAwait(false);
            }

            if (opcode >= 8)
            {
                // Control frame: small payload, read it whole.
                var payload = new byte[length];
                if (length > 0) await ReadExactly(_stream, payload, 0, (int)length, ct).ConfigureAwait(false);
                if (mask != null) for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];
                switch (opcode)
                {
                    case 9: // ping → pong
                        await SendFrame(10, new ArraySegment<byte>(payload), true, ct).ConfigureAwait(false);
                        return false;
                    case 10: // pong
                        return false;
                    default: // close
                        _closeStatus = payload.Length >= 2 ? (WebSocketCloseStatus)((payload[0] << 8) | payload[1]) : WebSocketCloseStatus.Empty;
                        _closeDescription = payload.Length > 2 ? Encoding.UTF8.GetString(payload, 2, payload.Length - 2) : string.Empty;
                        if (_state == WebSocketState.CloseSent)
                        {
                            _state = WebSocketState.Closed;
                        }
                        else
                        {
                            // Answer the server's close right away (completes the handshake).
                            _state = WebSocketState.CloseReceived;
                            await CloseOutputAsync(_closeStatus.Value, null, ct).ConfigureAwait(false);
                        }

                        _remaining = 0;
                        return true;
                }
            }

            if (masked) throw new WebSocketException("Masked frame from the server");
            if (opcode == 1) _messageType = WebSocketMessageType.Text;
            else if (opcode == 2) _messageType = WebSocketMessageType.Binary;
            _fin = fin;
            _remaining = length;
            if (length == 0) return true;
            return true;
        }

        private async Task SendFrame(byte opcode, ArraySegment<byte> payload, bool fin, CancellationToken ct)
        {
            int length = payload.Count;
            var frame = new byte[length + 14];
            int p = 0;
            frame[p++] = (byte)((fin ? 0x80 : 0) | opcode);
            if (length < 126)
            {
                frame[p++] = (byte)(0x80 | length);
            }
            else if (length <= ushort.MaxValue)
            {
                frame[p++] = 0x80 | 126;
                frame[p++] = (byte)(length >> 8);
                frame[p++] = (byte)length;
            }
            else
            {
                frame[p++] = 0x80 | 127;
                for (int i = 7; i >= 0; i--) frame[p++] = (byte)((long)length >> (8 * i));
            }

            // Clients must mask every frame.
            var mask = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(mask);
            Array.Copy(mask, 0, frame, p, 4);
            p += 4;
            for (int i = 0; i < length; i++) frame[p + i] = (byte)(payload.Array![payload.Offset + i] ^ mask[i % 4]);

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(frame, 0, p + length, ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void Shutdown()
        {
            try
            {
                _stream.Dispose();
            }
            catch (Exception)
            {
                // already gone
            }

            _tcp.Dispose();
        }

        private static async Task ReadExactly(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            while (count > 0)
            {
                int n = await stream.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);
                if (n == 0) throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
                offset += n;
                count -= n;
            }
        }

        private static async Task WriteAscii(Stream stream, string text, CancellationToken ct)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Reads an HTTP header block (up to the empty line) byte by byte, so no body bytes are consumed.</summary>
        private static async Task<string> ReadHeaders(Stream stream, CancellationToken ct)
        {
            var bytes = new List<byte>(256);
            var one = new byte[1];
            while (bytes.Count < 16384)
            {
                await ReadExactly(stream, one, 0, 1, ct).ConfigureAwait(false);
                bytes.Add(one[0]);
                int n = bytes.Count;
                if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n') break;
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private static string FirstLine(string text)
        {
            int end = text.IndexOf('\r');
            return end >= 0 ? text.Substring(0, end) : text;
        }

        /// <summary>HTTP proxy from HTTPS_PROXY / HTTP_PROXY (respecting NO_PROXY), or null.</summary>
        private static Uri? Proxy(Uri target)
        {
            string? value = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy")
                ?? Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("http_proxy");
            if (string.IsNullOrEmpty(value) || target.IsLoopback) return null;
            string noProxy = Environment.GetEnvironmentVariable("NO_PROXY") ?? Environment.GetEnvironmentVariable("no_proxy") ?? string.Empty;
            foreach (var entry in noProxy.Split(','))
            {
                string e = entry.Trim().TrimStart('.');
                if (e == "*" || (e.Length > 0 && (target.Host == e || target.Host.EndsWith("." + e, StringComparison.OrdinalIgnoreCase)))) return null;
            }

            return Uri.TryCreate(value, UriKind.Absolute, out var proxy) ? proxy : null;
        }
    }
}
