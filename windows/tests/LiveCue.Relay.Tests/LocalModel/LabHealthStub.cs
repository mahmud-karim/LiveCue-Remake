using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LiveCue.Relay.Tests.LocalModel;

/// <summary>
/// A loopback stand-in for the lab's <c>GET /health</c> (protocol 11.2), on a port the system picks. It
/// reads one request head per connection, keeps it, and then does what the test scripted: the bytes are
/// written by hand, so a reply can be oversized, malformed, slow or missing.
/// </summary>
internal sealed class LabHealthStub : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _gate = new();
    private readonly List<string> _requests = [];
    private readonly Task _accepting;
    private int _disposed;

    public LabHealthStub(Func<NetworkStream, CancellationToken, Task> respond)
    {
        Respond = respond;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    /// <summary>What happens after a request head arrived. The connection is closed when it returns.</summary>
    public Func<NetworkStream, CancellationToken, Task> Respond { get; set; }

    /// <summary>Every request head received so far, as text.</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>A complete reply with a truthful <c>Content-Length</c>.</summary>
    public static Func<NetworkStream, CancellationToken, Task> Reply(
        string body, string statusLine = "200 OK", params string[] extraHeaders) => (stream, ct) =>
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = new StringBuilder();
        head.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {statusLine}\r\n");
        head.Append("Content-Type: application/json\r\n");
        head.Append(CultureInfo.InvariantCulture, $"Content-Length: {bytes.Length}\r\n");
        foreach (var header in extraHeaders)
        {
            head.Append(header).Append("\r\n");
        }

        head.Append("Connection: close\r\n\r\n");
        return WriteAsync(stream, [.. Encoding.ASCII.GetBytes(head.ToString()), .. bytes], ct);
    };

    /// <summary>Exactly these bytes, whatever they are.</summary>
    public static Func<NetworkStream, CancellationToken, Task> Raw(string text) =>
        (stream, ct) => WriteAsync(stream, Encoding.UTF8.GetBytes(text), ct);

    /// <summary>These bytes, and then silence: the connection stays open until the stub is disposed.</summary>
    public static Func<NetworkStream, CancellationToken, Task> RawThenSilence(string text) => async (stream, ct) =>
    {
        await WriteAsync(stream, Encoding.UTF8.GetBytes(text), ct);
        await Task.Delay(Timeout.Infinite, ct);
    };

    /// <summary>The connection is closed without a byte of reply.</summary>
    public static Func<NetworkStream, CancellationToken, Task> Hangup() => (_, _) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stopping.CancelAsync();
        _listener.Stop();
        try
        {
            await _accepting;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // The accept loop ended with the listener.
        }

        _stopping.Dispose();
    }

    private static async Task WriteAsync(NetworkStream stream, byte[] bytes, CancellationToken ct)
    {
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = new StringBuilder();
                var one = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await stream.ReadAsync(one, _stopping.Token) == 0)
                    {
                        return;
                    }

                    head.Append((char)one[0]);
                }

                lock (_gate)
                {
                    _requests.Add(head.ToString());
                }

                await Respond(stream, _stopping.Token);

                // Sent before the close, so the client sees the end of the reply and not a reset.
                client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // The client went away, or the test ended.
            }
        }
    }
}
