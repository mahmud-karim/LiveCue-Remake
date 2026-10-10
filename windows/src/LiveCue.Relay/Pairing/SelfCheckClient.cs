using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Pairing;

/// <summary>
/// The production self-check client (protocol 4.5): one GET of the advertised health URL with the three
/// headers. Redirects are refused, so the token is sent to the advertised endpoint and nowhere else; no
/// cookie is kept and no proxy is used. At most W-06 bytes of the answer are read.
///
/// Each check opens exactly one connection and closes it. The HTTP stack would otherwise repeat a
/// request whose connection was closed before any answer; a second connection is refused here, so one
/// check is one request on the wire.
/// </summary>
internal sealed class SelfCheckClient : ISelfCheckClient
{
    private static readonly string ProtocolVersionText =
        ProtocolConstants.ProtocolVersion.ToString(CultureInfo.InvariantCulture);

    public async Task<bool> GetHealthAsync(Uri healthUrl, string token, string nonce, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(healthUrl);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(nonce);
        ct.ThrowIfCancellationRequested();

        try
        {
            var connections = 0;
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectCallback = (context, cancel) => Interlocked.Increment(ref connections) == 1
                    ? ConnectAsync(context.DnsEndPoint, cancel)
                    : throw new IOException("A self-check uses one connection."),
            };

            // The caller's token is the only time limit (C-49, on the relay's clock).
            using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get, healthUrl);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            request.Headers.TryAddWithoutValidation("X-LiveCue-Protocol", ProtocolVersionText);
            request.Headers.TryAddWithoutValidation("X-LiveCue-Self-Check", nonce);

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                // A redirect, a 401, the edge proxy's own error: all "not verified".
                return false;
            }

            var body = await ReadBoundedAsync(response.Content, ct).ConfigureAwait(false);
            return body is not null && IsHealthy(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            // Refused, unreachable, a TLS failure, a connection that broke: all "not verified".
            return false;
        }
    }

    private static async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>The body, or null when it is longer than W-06.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            // One byte more than the limit, to see whether there is more.
            var buffer = new byte[ProtocolConstants.MaxProbeBodyBytes + 1];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer[..total];
                }

                total += read;
            }

            return null;
        }
    }

    /// <summary>Protocol 4.5: an object with <c>ok</c> true and <c>protocol</c> equal to C-01.</summary>
    private static bool IsHealthy(byte[] body) =>
        WireJson.TryParseObject(body, out var root)
        && root.TryGetProperty("ok", out var ok)
        && ok.ValueKind == JsonValueKind.True
        && root.TryGetProperty("protocol", out var protocol)
        && protocol.ValueKind == JsonValueKind.Number
        && protocol.TryGetInt32(out var version)
        && version == ProtocolConstants.ProtocolVersion;
}
