using System.Globalization;
using System.Net.WebSockets;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Speech;

/// <summary>
/// Opens the lab's <c>/relay</c> socket (protocol 11.3): <c>ws://127.0.0.1:&lt;lab port&gt;/relay</c>, and
/// nothing else. The address is loopback by construction and never comes from a request. No proxy is
/// used, no <c>Origin</c> is sent (the lab refuses any), no subprotocol and no compression are offered,
/// and keep-alive is off. The handshake is bounded by C-47 on the relay's clock.
///
/// Every failure is reported as <see cref="LabSocketException"/>, whose text is fixed: nothing the lab or
/// the network said can travel further.
/// </summary>
internal sealed class LabSocketConnector : ILabSocketConnector
{
    private readonly TimeProvider _clock;
    private readonly int _labPort;

    public LabSocketConnector(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _clock = context.Clock;
        _labPort = context.Options.LabPort;
    }

    /// <summary>The address that is dialled.</summary>
    internal static Uri RelayUri(int labPort) =>
        new(string.Create(CultureInfo.InvariantCulture, $"ws://127.0.0.1:{labPort}/relay"));

    /// <summary>A client set up as this connector uses it, before any connection.</summary>
    internal static ClientWebSocket CreateClient()
    {
        var client = new ClientWebSocket();
        var options = client.Options;
        options.Proxy = null;
        options.UseDefaultCredentials = false;
        options.KeepAliveInterval = TimeSpan.Zero;
        return client;
    }

    public async Task<ILabSocket> ConnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var client = CreateClient();
        try
        {
            using var limit = new CancellationTokenSource(ProtocolConstants.LabHandshakeTimeout, _clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
            await client.ConnectAsync(RelayUri(_labPort), linked.Token).ConfigureAwait(false);
            return new LabSocket(client);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            client.Dispose();
            throw;
        }
#pragma warning disable CA1031 // Protocol 10.6: whatever went wrong is reduced to "the lab socket failed".
        catch (Exception)
#pragma warning restore CA1031
        {
            client.Dispose();
            throw new LabSocketException();
        }
    }

    /// <summary>One open lab socket. The stream gives it one sender and one receiver.</summary>
    private sealed class LabSocket(ClientWebSocket socket) : ILabSocket
    {
        private static readonly byte[] EndStream = "{\"type\":\"endStream\"}"u8.ToArray();

        /// <summary>One byte more than C-23, so that a message beyond the limit is seen and not stored.</summary>
        private readonly byte[] _buffer = new byte[ProtocolConstants.MaxLabMessageBytes + 1];

        /// <summary>True while the rest of a message that was reported as invalid is still unread.</summary>
        private bool _skipRest;

        public ValueTask SendAudioAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
            SendAsync(frame, WebSocketMessageType.Binary, ct);

        public ValueTask SendEndStreamAsync(CancellationToken ct) =>
            SendAsync(EndStream, WebSocketMessageType.Text, ct);

        public async ValueTask<LabMessage> ReceiveAsync(CancellationToken ct)
        {
            try
            {
                while (_skipRest)
                {
                    var rest = await socket.ReceiveAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
                    if (rest.MessageType == WebSocketMessageType.Close)
                    {
                        return new LabMessage(LabMessageKind.Closed, default);
                    }

                    _skipRest = !rest.EndOfMessage;
                }

                var count = 0;
                while (true)
                {
                    var part = await socket.ReceiveAsync(_buffer.AsMemory(count), ct).ConfigureAwait(false);
                    if (part.MessageType == WebSocketMessageType.Close)
                    {
                        return new LabMessage(LabMessageKind.Closed, default);
                    }

                    count += part.Count;
                    if (part.MessageType != WebSocketMessageType.Text || count > ProtocolConstants.MaxLabMessageBytes)
                    {
                        // A binary message, or one byte beyond C-23: reported at once and never assembled.
                        _skipRest = !part.EndOfMessage;
                        return new LabMessage(LabMessageKind.Invalid, default);
                    }

                    if (part.EndOfMessage)
                    {
                        return new LabMessage(LabMessageKind.Text, _buffer.AsSpan(0, count).ToArray());
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (WebSocketException exception)
                when (exception.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                return new LabMessage(LabMessageKind.Closed, default);
            }
#pragma warning disable CA1031 // Protocol 10.6: whatever went wrong is reduced to "the lab socket failed".
            catch (Exception)
#pragma warning restore CA1031
            {
                throw new LabSocketException();
            }
        }

        public ValueTask DisposeAsync()
        {
            // No close handshake: the stream is over, and the lab ends its session when the connection ends.
            socket.Abort();
            socket.Dispose();
            return ValueTask.CompletedTask;
        }

        private async ValueTask SendAsync(ReadOnlyMemory<byte> bytes, WebSocketMessageType type, CancellationToken ct)
        {
            try
            {
                await socket.SendAsync(bytes, type, endOfMessage: true, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Protocol 10.6: whatever went wrong is reduced to "the lab socket failed".
            catch (Exception)
#pragma warning restore CA1031
            {
                throw new LabSocketException();
            }
        }
    }
}
