using System.Globalization;
using System.Text;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Edge;

/// <summary>
/// Protocol P2-012 (protocol 5.4 rules 1 and 2): the body limit C-10, with a declared length and while
/// streaming. The requests are written by hand; the read of the answer is started before the body is
/// sent, as a proxy does, because the server core closes the connection of an oversized body at once.
/// </summary>
public sealed class BodyLimitTests : IAsyncLifetime
{
    private const int Limit = ProtocolConstants.MaxRequestBodyBytes;

    private RelayTestHost _host = null!;

    public async Task InitializeAsync() => _host = await RelayTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task One_byte_over_is_413_with_a_length_and_chunked_and_the_limit_itself_passes()
    {
        Assert.Equal(131_072, Limit);

        // One byte over, on the answer route the statement names and on route 2.
        foreach (var target in (string[])["/v1/assist", "/v1/pair/verify"])
        {
            var withLength = await SendWithLengthAsync(target, AssistBodyOfSize(Limit + 1));
            var chunked = await SendChunkedAsync(target, AssistBodyOfSize(Limit + 1));

            foreach (var response in (string?[])[withLength, chunked])
            {
                Assert.NotNull(response);
                Assert.Equal("HTTP/1.1 413 Payload Too Large", Wire.StatusLine(response));
                Assert.Equal(Wire.PayloadTooLarge, Wire.BodyOf(response));
                Assert.Contains("Connection: close", Wire.HeaderLines(response));
            }
        }

        // The limit itself passes the body step: on route 2 that is the whole route.
        var verifyAtLimit = await SendWithLengthAsync("/v1/pair/verify", AssistBodyOfSize(Limit));
        Assert.Equal("""{"ok":true}""", Wire.BodyOf(verifyAtLimit!));

        // A chunked body passes while its bytes and its chunk framing together stay within the limit
        // (W-design K-10: the server core's own count, the second line, includes the framing).
        var verifyChunked = await SendChunkedAsync("/v1/pair/verify", AssistBodyOfSize(Limit - 256));
        Assert.Equal("""{"ok":true}""", Wire.BodyOf(verifyChunked!));

        // On the answer route a valid body of exactly the limit is not refused for its size or its form.
        var assistAtLimit = await SendWithLengthAsync("/v1/assist", AssistBodyOfSize(Limit));
        Assert.NotNull(assistAtLimit);
        Assert.DoesNotContain("PAYLOAD_TOO_LARGE", assistAtLimit, StringComparison.Ordinal);
        Assert.DoesNotContain("INVALID_JSON", assistAtLimit, StringComparison.Ordinal);
        Assert.DoesNotContain(" 413 ", Wire.StatusLine(assistAtLimit), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declared_length_over_the_limit_is_refused_without_reading()
    {
        // The head says 200,000 bytes and not one of them is sent: the answer does not wait.
        using var raw = await _host.OpenRawAsync();
        var reading = Wire.ReadRawAsync(raw, timeout: TimeSpan.FromSeconds(5));
        await raw.SendAsync(_host.RawHead("POST /v1/pair/verify HTTP/1.1", ("Content-Length", "200000")));

        var response = await reading;

        Assert.NotNull(response);
        Assert.Equal("HTTP/1.1 413 Payload Too Large", Wire.StatusLine(response));
        Assert.Equal(Wire.PayloadTooLarge, Wire.BodyOf(response));
        Assert.Contains("Connection: close", Wire.HeaderLines(response));
    }

    /// <summary>A valid assist body (protocol section 15, test conventions) of exactly that many bytes.</summary>
    private static string AssistBodyOfSize(int bytes)
    {
        var empty = Bodies.Assist(transcript: string.Empty);
        var body = Bodies.Assist(transcript: new string('a', bytes - Encoding.UTF8.GetByteCount(empty)));
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(body));
        return body;
    }

    private Task<string?> SendWithLengthAsync(string target, string body) => ExchangeAsync(
        _host.RawHead(
            $"POST {target} HTTP/1.1",
            ("Content-Type", "application/json"),
            ("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture))),
        body);

    private Task<string?> SendChunkedAsync(string target, string body)
    {
        // Chunks of 16 KiB, then the last chunk.
        const int chunkSize = 16_384;
        var chunks = new StringBuilder();
        for (var offset = 0; offset < body.Length; offset += chunkSize)
        {
            var part = body.AsSpan(offset, Math.Min(chunkSize, body.Length - offset));
            chunks.Append(part.Length.ToString("X", CultureInfo.InvariantCulture)).Append("\r\n").Append(part).Append("\r\n");
        }

        chunks.Append("0\r\n\r\n");
        return ExchangeAsync(
            _host.RawHead($"POST {target} HTTP/1.1", ("Content-Type", "application/json"), ("Transfer-Encoding", "chunked")),
            chunks.ToString());
    }

    private async Task<string?> ExchangeAsync(string head, string body)
    {
        using var raw = await _host.OpenRawAsync();
        var reading = Wire.ReadRawAsync(raw);
        try
        {
            await raw.SendAsync(head + body);
        }
        catch (IOException)
        {
            // The server answered and closed while the rest of an oversized body was still on its way.
        }

        return await reading;
    }
}
