using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol 10.1 (P2-107, P2-108, P2-119): what happens before the upgrade. Requests are written to the
/// socket by hand, so that the test decides what is on the wire: no token, a wrong token, a reset
/// token and a browser are answered as plain HTTP, an offered extension or subprotocol is ignored, and
/// one socket beyond C-32 is HTTP 429.
/// </summary>
public sealed class SpeechUpgradeTests
{
    private const string Unauthorized = """{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}""";

    private const string LoadingFrame =
        """{"type":"loading","provider":"nemotron","elapsedSeconds":0,"message":"Loading Nemotron on PC · 0 s. Microphone starts when ready."}""";

    [Fact]
    public async Task No_token_a_wrong_token_and_a_reset_token_are_http_401()
    {
        await using var rig = await SpeechRig.StartAsync();
        var host = rig.Host;
        var resetToken = host.Token;
        await rig.Relay.ResetPairingAsync(CancellationToken.None);
        host.Events.Clear();

        foreach (var authorization in new[] { null, TestTokens.Bearer(TestTokens.WellFormedUnknown), TestTokens.Bearer(resetToken) })
        {
            using var raw = await host.OpenRawAsync();
            await raw.SendAsync(RawSpeech.UpgradeHead(host.Port, authorization));

            var response = await raw.ReadResponseAsync();

            Assert.NotNull(response);
            Assert.Equal("HTTP/1.1 401 Unauthorized", response.StatusLine);
            Assert.Equal(Unauthorized, response.Body);
            Assert.Null(response.Header("Upgrade"));
            Assert.Null(response.Header("Sec-WebSocket-Accept"));
        }

        // Nothing was upgraded, so nothing was admitted, prepared or shown on the desktop.
        await host.Events.SettleAsync();
        Assert.Equal(0, rig.Model.PrepareCalls);
        Assert.Empty(host.Events.OfType<SpeechStreamChanged>());
        Assert.Empty(host.Events.OfType<PhoneSeen>());

        // The token that replaced the reset one is upgraded.
        using var accepted = await host.OpenRawAsync();
        await accepted.SendAsync(RawSpeech.UpgradeHead(host.Port, TestTokens.Bearer(await host.ReadTokenAsync())));
        Assert.Equal(101, (await accepted.ReadResponseAsync())?.Status);
    }

    [Theory]
    [InlineData("Origin", "https://pc.example.test")]
    [InlineData("Origin", "null")]
    [InlineData("Sec-Fetch-Site", "same-origin")]
    public async Task An_origin_header_is_http_401(string name, string value)
    {
        await using var rig = await SpeechRig.StartAsync();
        var host = rig.Host;
        using var raw = await host.OpenRawAsync();

        // A valid token does not help a browser.
        await raw.SendAsync(RawSpeech.UpgradeHead(host.Port, TestTokens.Bearer(host.Token), (name, value)));
        var response = await raw.ReadResponseAsync();

        Assert.NotNull(response);
        Assert.Equal(401, response.Status);
        Assert.Equal(Unauthorized, response.Body);
        Assert.Null(response.Header("Upgrade"));
        Assert.Equal(0, rig.Model.PrepareCalls);
    }

    [Theory]
    [InlineData("Sec-WebSocket-Extensions", "permessage-deflate")]
    [InlineData("Sec-WebSocket-Extensions", "permessage-deflate; client_max_window_bits")]
    [InlineData("Sec-WebSocket-Extensions", "x-webkit-deflate-frame, permessage-deflate; server_no_context_takeover")]
    [InlineData("Sec-WebSocket-Protocol", "livecue.v2, chat")]
    public async Task An_offered_extension_is_ignored_and_the_upgrade_is_accepted(string name, string offer)
    {
        await using var rig = await SpeechRig.StartAsync();
        var host = rig.Host;
        using var raw = await host.OpenRawAsync();

        await raw.SendAsync(RawSpeech.UpgradeHead(host.Port, TestTokens.Bearer(host.Token), (name, offer)));
        var response = await raw.ReadResponseAsync();

        // Accepted, and nothing that was offered was taken: no compression, no subprotocol.
        Assert.NotNull(response);
        Assert.Equal("HTTP/1.1 101 Switching Protocols", response.StatusLine);
        Assert.Equal(RawSpeech.AcceptForKey, response.Header("Sec-WebSocket-Accept"));
        Assert.DoesNotContain(response.HeaderNames, header => header.Equals("Sec-WebSocket-Extensions", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(response.HeaderNames, header => header.Equals("Sec-WebSocket-Protocol", StringComparison.OrdinalIgnoreCase));

        // The first frame is "loading": a final text frame with no reserved bit set (not compressed).
        var (first, payload) = await RawSpeech.ReadFrameAsync(raw.Stream);
        Assert.Equal(0x81, first);
        Assert.Equal(LoadingFrame, Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public async Task A_repeated_model_header_is_refused_by_a_typed_frame_after_the_upgrade()
    {
        await using var rig = await SpeechRig.StartAsync();
        var host = rig.Host;
        using var raw = await host.OpenRawAsync();

        // Two header lines: no single value, so the model is unknown. The upgrade is accepted all the same.
        await raw.SendAsync(RawSpeech.UpgradeHead(
            host.Port, TestTokens.Bearer(host.Token), ("X-LiveCue-Speech-Model", "qwen3")));
        var response = await raw.ReadResponseAsync();
        var (first, payload) = await RawSpeech.ReadFrameAsync(raw.Stream);
        var (close, reason) = await RawSpeech.ReadFrameAsync(raw.Stream);

        Assert.Equal(101, response?.Status);
        Assert.Equal(0x81, first);
        Assert.Equal(
            """{"type":"error","code":"INVALID_SPEECH_MODEL","message":"Unknown speech model."}""",
            Encoding.UTF8.GetString(payload));
        Assert.Equal(0x88, close);
        Assert.Equal(1008, BinaryPrimitives.ReadUInt16BigEndian(reason));
        Assert.Equal("INVALID_SPEECH_MODEL", Encoding.UTF8.GetString(reason.AsSpan(2)));
        Assert.Equal(0, rig.Model.PrepareCalls);
    }

    [Fact]
    public async Task One_socket_beyond_the_cap_is_http_429()
    {
        await using var rig = await SpeechRig.StartAsync();
        var host = rig.Host;
        rig.Model.PrepareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var phones = new List<SpeechPhone>();
        try
        {
            // C-32 sockets: one admitted and loading, the others being refused. A refused socket counts
            // until it is closed, and these phones have not answered the close yet.
            phones.Add(await rig.OpenAsync());
            await phones[0].ExpectAsync("loading");
            for (var refused = 1; refused < ProtocolConstants.MaxSpeechSockets; refused++)
            {
                phones.Add(await rig.OpenAsync());
                var error = await phones[refused].ExpectAsync("error");
                Assert.Equal("SPEECH_BUSY", error.GetProperty("code").GetString());
            }

            // One more is answered as plain HTTP, before any upgrade.
            using (var raw = await host.OpenRawAsync())
            {
                await raw.SendAsync(RawSpeech.UpgradeHead(host.Port, TestTokens.Bearer(host.Token)));
                var response = await raw.ReadResponseAsync();

                Assert.NotNull(response);
                Assert.Equal("HTTP/1.1 429 Too Many Requests", response.StatusLine);
                Assert.Equal("""{"error":"Too many requests in progress.","code":"TOO_MANY_REQUESTS"}""", response.Body);
                Assert.Null(response.Header("Upgrade"));
            }

            using (var socket = rig.CreateSocket())
            {
                await Assert.ThrowsAsync<WebSocketException>(
                    () => socket.ConnectAsync(host.SpeechUri, CancellationToken.None));
            }

            Assert.Equal(1, rig.Model.PrepareCalls);

            // The admitted stream was not disturbed, and a refused socket that completes its close gives
            // its place back: the next upgrade is accepted.
            await phones[^1].ReadToEndAsync();
            await RawSpeech.UpgradeEventuallyAsync(host);
            rig.Clock.Advance(ProtocolConstants.LoadingFrameInterval);
            await phones[0].ExpectAsync("loading");
        }
        finally
        {
            phones.ForEach(phone => phone.Dispose());
        }
    }
}

/// <summary>A speech upgrade written by hand, and the server's frames read by hand.</summary>
internal static class RawSpeech
{
    /// <summary>The key of the RFC 6455 example, and the answer the RFC gives for it.</summary>
    public const string Key = "dGhlIHNhbXBsZSBub25jZQ==";

    public const string AcceptForKey = "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=";

    /// <summary>
    /// A well-formed upgrade of <c>GET /v1/speech</c> for <c>nemotron</c> with protocol 2. Null leaves
    /// <c>Authorization</c> out. Extra headers come after the phone's own, so a name can repeat.
    /// </summary>
    public static string UpgradeHead(int port, string? authorization, params (string Name, string Value)[] extra)
    {
        var headers = new List<(string Name, string Value)>();
        if (authorization is not null)
        {
            headers.Add(("Authorization", authorization));
        }

        headers.Add(("X-LiveCue-Protocol", "2"));
        headers.Add(("X-LiveCue-Speech-Model", "nemotron"));
        headers.Add(("Upgrade", "websocket"));
        headers.Add(("Connection", "Upgrade"));
        headers.Add(("Sec-WebSocket-Version", "13"));
        headers.Add(("Sec-WebSocket-Key", Key));
        headers.AddRange(extra);
        return RawHttp.Head("GET /v1/speech HTTP/1.1", port, [.. headers]);
    }

    /// <summary>
    /// One frame from the server: its first byte (FIN, the three reserved bits, the opcode) and its
    /// payload. A server never masks.
    /// </summary>
    public static async Task<(byte First, byte[] Payload)> ReadFrameAsync(Stream stream)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var head = new byte[2];
        await stream.ReadExactlyAsync(head, limit.Token);
        Assert.Equal(0, head[1] & 0x80);
        long length = head[1] & 0x7F;
        if (length == 126)
        {
            var extended = new byte[2];
            await stream.ReadExactlyAsync(extended, limit.Token);
            length = BinaryPrimitives.ReadUInt16BigEndian(extended);
        }
        else if (length == 127)
        {
            var extended = new byte[8];
            await stream.ReadExactlyAsync(extended, limit.Token);
            length = BinaryPrimitives.ReadInt64BigEndian(extended);
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, limit.Token);
        return (head[0], payload);
    }

    /// <summary>
    /// Upgrades one more socket, trying again for a short real time while the answer is 429: a place
    /// that was just given back is free a moment later.
    /// </summary>
    public static async Task UpgradeEventuallyAsync(RelayTestHost host)
    {
        var patience = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            using var raw = await host.OpenRawAsync();
            await raw.SendAsync(UpgradeHead(host.Port, TestTokens.Bearer(host.Token)));
            var response = await raw.ReadResponseAsync();
            Assert.NotNull(response);
            if (response.Status == 101)
            {
                return;
            }

            Assert.Equal(429, response.Status);
            Assert.True(patience.Elapsed < TimeSpan.FromSeconds(10), "The place of a closed speech socket was not given back.");
            await Task.Delay(10);
        }
    }
}
