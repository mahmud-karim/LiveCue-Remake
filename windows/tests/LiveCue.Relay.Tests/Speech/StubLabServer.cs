using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using LiveCue.Relay.Core;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// A stand-in for the lab's <c>/relay</c> socket on a loopback port the system picks. It does the
/// WebSocket handshake by hand, so that a test can read exactly what the relay sent, and can be told
/// never to answer one.
/// </summary>
internal sealed class StubLabServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Channel<WebSocket> _sockets = Channel.CreateUnbounded<WebSocket>();
    private readonly Lock _gate = new();
    private readonly List<string> _heads = [];
    private readonly List<TcpClient> _clients = [];
    private readonly Task _accepting;

    public StubLabServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    /// <summary>False: a connection is accepted and its request is read, but no answer is ever written.</summary>
    public bool AnswersHandshake { get; set; } = true;

    /// <summary>The request heads received so far, as sent.</summary>
    public IReadOnlyList<string> RequestHeads
    {
        get
        {
            lock (_gate)
            {
                return [.. _heads];
            }
        }
    }

    /// <summary>A relay context whose lab port is this server's and whose clock is the given one.</summary>
    public RelayContext Context(TimeProvider clock) => SpeechContexts.Create(Port, clock);

    /// <summary>The server's end of the next socket whose handshake it answered.</summary>
    public async Task<WebSocket> NextSocketAsync()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await _sockets.Reader.ReadAsync(limit.Token);
    }

    public static async Task SendTextAsync(WebSocket socket, string text) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    /// <summary>One whole message from the relay, or null for a close frame or the end of the connection.</summary>
    public static async Task<(WebSocketMessageType Type, byte[] Bytes)?> ReceiveAsync(WebSocket socket)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var message = new MemoryStream();
        var buffer = new byte[32 * 1024];
        try
        {
            while (true)
            {
                var part = await socket.ReceiveAsync(buffer.AsMemory(), limit.Token);
                if (part.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, part.Count);
                if (part.EndOfMessage)
                {
                    return (part.MessageType, message.ToArray());
                }
            }
        }
        catch (WebSocketException)
        {
            return null;
        }
    }

    /// <summary>Drops every connection without a close frame, as a container that is stopped would.</summary>
    public void DropConnections()
    {
        lock (_gate)
        {
            _clients.ForEach(client => client.Dispose());
            _clients.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        DropConnections();
        try
        {
            await _accepting;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        _stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            lock (_gate)
            {
                _clients.Add(client);
            }

            _ = Task.Run(() => HandshakeAsync(client));
        }
    }

    private async Task HandshakeAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            var head = new List<byte>();
            var one = new byte[1];
            while (head.Count < 4 || head[^4] != '\r' || head[^3] != '\n' || head[^2] != '\r' || head[^1] != '\n')
            {
                if (await stream.ReadAsync(one, _stopping.Token) == 0)
                {
                    return;
                }

                head.Add(one[0]);
            }

            var text = Encoding.ASCII.GetString([.. head]);
            lock (_gate)
            {
                _heads.Add(text);
            }

            if (!AnswersHandshake)
            {
                return;
            }

            var key = text.Split("\r\n")
                .First(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                .Split(':', 2)[1]
                .Trim();
#pragma warning disable CA5350 // RFC 6455 fixes SHA-1 for the handshake answer; nothing is protected by it.
            var accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
#pragma warning restore CA5350
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                    + $"Sec-WebSocket-Accept: {accept}\r\n\r\n"),
                _stopping.Token);
            _sockets.Writer.TryWrite(WebSocket.CreateFromStream(
                stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero }));
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The relay went away during the handshake, or the test is over.
        }
    }
}

/// <summary>A relay context built by hand for a test that needs one without a relay.</summary>
internal static class SpeechContexts
{
    public static RelayContext Create(int labPort, TimeProvider clock)
    {
        var options = new RelayOptions(
            Port: 0,
            AdvertisedEndpoint: RelayTestHost.Endpoint,
            Mode: ConnectionMode.Private,
            DataDirectory: @"C:\LiveCue-tests\data",
            CodexExecutablePath: null,
            LabFolder: @"C:\LiveCue-tests\asr",
            LabPort: labPort,
            RelayVersion: RelayTestHost.RelayVersion,
            WarmCatalogOnStart: false);
        var processes = new FakeProcessRunner();
        return new RelayContext(
            options,
            new RelayDependencies { Clock = clock, Processes = processes },
            new SilentEvents(clock),
            new OpenAccess(),
            processes,
            RelayTestHost.Endpoint);
    }

    /// <summary>An event sink that keeps nothing.</summary>
    private sealed class SilentEvents(TimeProvider clock) : IRelayEventSink
    {
        public DateTimeOffset Now => clock.GetUtcNow();

        public void Publish(RelayEvent relayEvent)
        {
        }

        public void Diagnostic(string source, string fixedText, Exception? exception = null)
        {
        }
    }

    /// <summary>A relay that accepts and never stops.</summary>
    private sealed class OpenAccess : IAccessState
    {
        public bool Accepting => true;

        public CancellationToken WhileAccepting => CancellationToken.None;

        public CancellationToken Stopping => CancellationToken.None;
    }
}
