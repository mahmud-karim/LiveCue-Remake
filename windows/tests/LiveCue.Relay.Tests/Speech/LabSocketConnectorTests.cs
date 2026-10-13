using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Speech;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// Protocol 11.3 from the relay's side, with the production connector against a loopback stub socket:
/// what it dials and what it does not send, the handshake limit C-47 on the manual clock, the bound
/// C-23, and how every way a lab socket can end is reported.
/// </summary>
public sealed class LabSocketConnectorTests
{
    /// <summary>The longest a single step of a test waits before it fails instead of hanging.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task No_origin_no_proxy_and_the_handshake_limit()
    {
        await using var server = new StubLabServer();
        var clock = new ManualTimeProvider();
        var connector = new LabSocketConnector(server.Context(clock));

        // The address is loopback and the lab port, and nothing else.
        Assert.Equal(new Uri("ws://127.0.0.1:47842/relay"), LabSocketConnector.RelayUri(47842));
        using (var client = LabSocketConnector.CreateClient())
        {
            Assert.Null(client.Options.Proxy);
            Assert.False(client.Options.UseDefaultCredentials);
            Assert.Equal(TimeSpan.Zero, client.Options.KeepAliveInterval);
        }

        await using (var lab = await connector.ConnectAsync(CancellationToken.None))
        {
            var head = Assert.Single(server.RequestHeads);
            var lines = head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("GET /relay HTTP/1.1", lines[0]);
            Assert.Contains($"Host: 127.0.0.1:{server.Port}", lines);
            Assert.Contains("Sec-WebSocket-Version: 13", lines);

            // The lab refuses any Origin; and the relay offers nothing it would have to honour.
            var names = lines.Skip(1).Select(line => line.Split(':', 2)[0]).ToList();
            foreach (var absent in new[] { "Origin", "Sec-WebSocket-Extensions", "Sec-WebSocket-Protocol", "Authorization", "Cookie", "Proxy-Authorization" })
            {
                Assert.DoesNotContain(names, name => name.Equals(absent, StringComparison.OrdinalIgnoreCase));
            }
        }

        // A lab that accepts the connection and never answers: the attempt ends at C-47, not before.
        server.AnswersHandshake = false;
        var pending = connector.ConnectAsync(CancellationToken.None);
        await SpeechWait.UntilAsync(() => server.RequestHeads.Count == 2, "the second handshake arrived");
        clock.Advance(ProtocolConstants.LabHandshakeTimeout - TimeSpan.FromMilliseconds(1));
        await Task.Delay(100);
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<LabSocketException>(() => pending.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(0, clock.ScheduledTimers);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task A_lab_that_is_not_there_and_a_wait_that_is_cancelled()
    {
        var clock = new ManualTimeProvider();

        // Nothing listens on this port: the failure is the fixed one, with nothing of the system's text.
        // (Windows tries a refused loopback connection again for about two seconds before it gives up.)
        var nobody = new LabSocketConnector(SpeechContexts.Create(FreePort(), clock));
        var failure = await Assert.ThrowsAsync<LabSocketException>(() => nobody.ConnectAsync(CancellationToken.None));
        Assert.Equal("The lab socket failed.", failure.Message);
        Assert.Null(failure.InnerException);

        // The caller's own cancellation is not a lab failure.
        await using var server = new StubLabServer { AnswersHandshake = false };
        var connector = new LabSocketConnector(server.Context(clock));
        using var cancel = new CancellationTokenSource();
        var pending = connector.ConnectAsync(cancel.Token);
        await SpeechWait.UntilAsync(() => server.RequestHeads.Count == 1, "the handshake arrived");
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(15)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connector.ConnectAsync(cancel.Token));
        Assert.Equal(0, clock.ScheduledTimers);
    }

    [Fact]
    public async Task An_oversized_message_is_invalid()
    {
        await using var server = new StubLabServer();
        var connector = new LabSocketConnector(server.Context(new ManualTimeProvider()));
        await using var lab = await connector.ConnectAsync(CancellationToken.None);
        var fromLab = await server.NextSocketAsync();

        // Every exchange starts the relay's read before the lab's send, and bounds both. The connector
        // reports a message beyond C-23 at once and leaves the rest of it on the connection until its
        // next read. Windows takes one send of any size into an empty send buffer, but holds the next
        // send back while more than the buffer's size is still unread: a test that awaited a send before
        // it read would then wait for ever, which is how this test once hung on CI.
        async Task<LabMessage> ExchangeAsync(Func<Task> send)
        {
            var receiving = lab.ReceiveAsync(CancellationToken.None).AsTask();
            await send().WaitAsync(Patience);
            return await receiving.WaitAsync(Patience);
        }

        // Exactly C-23 bytes is a message.
        var atLimit = new string('a', ProtocolConstants.MaxLabMessageBytes);
        var whole = await ExchangeAsync(() => StubLabServer.SendTextAsync(fromLab, atLimit));
        Assert.Equal(LabMessageKind.Text, whole.Kind);
        Assert.Equal(atLimit, Encoding.UTF8.GetString(whole.Bytes.Span));

        // One byte more is not, and neither is one far beyond; nothing of either is handed on.
        var invalid = new LabMessage(LabMessageKind.Invalid, default);
        Assert.Equal(invalid, await ExchangeAsync(
            () => StubLabServer.SendTextAsync(fromLab, new string('b', ProtocolConstants.MaxLabMessageBytes + 1))));
        Assert.Equal(invalid, await ExchangeAsync(
            () => StubLabServer.SendTextAsync(fromLab, new string('c', 5 * ProtocolConstants.MaxLabMessageBytes))));

        // A binary message is invalid too, whatever its size. The read that reports it first skips what
        // is left of the message before it.
        Assert.Equal(invalid, await ExchangeAsync(
            () => fromLab.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None)));

        // The message after an invalid one is read from its own first byte.
        var next = await ExchangeAsync(
            () => StubLabServer.SendTextAsync(fromLab, """{"type":"transcript","transcript":"after"}"""));
        Assert.Equal(LabMessageKind.Text, next.Kind);
        Assert.Equal("""{"type":"transcript","transcript":"after"}""", Encoding.UTF8.GetString(next.Bytes.Span));
    }

    [Fact]
    public async Task The_rest_of_an_oversized_message_is_skipped_however_much_of_it_is_still_on_its_way()
    {
        // The condition that made the test above hang, forced: a message so far beyond C-23 that most of
        // it is still in the lab's send buffer when the connector reports it. The lab's next send then
        // waits for a reader. The connector's next read is that reader, and it skips to the next message.
        await using var server = new StubLabServer();
        var connector = new LabSocketConnector(server.Context(new ManualTimeProvider()));
        await using var lab = await connector.ConnectAsync(CancellationToken.None);
        var fromLab = await server.NextSocketAsync();

        var huge = StubLabServer.SendTextAsync(fromLab, new string('h', 256 * ProtocolConstants.MaxLabMessageBytes));
        var first = await lab.ReceiveAsync(CancellationToken.None).AsTask().WaitAsync(Patience);
        Assert.Equal(LabMessageKind.Invalid, first.Kind);

        var receiving = lab.ReceiveAsync(CancellationToken.None).AsTask();
        await huge.WaitAsync(Patience);
        await StubLabServer.SendTextAsync(fromLab, """{"type":"transcript","transcript":"after"}""").WaitAsync(Patience);
        var next = await receiving.WaitAsync(Patience);

        Assert.Equal(LabMessageKind.Text, next.Kind);
        Assert.Equal("""{"type":"transcript","transcript":"after"}""", Encoding.UTF8.GetString(next.Bytes.Span));
    }

    [Fact]
    public async Task Audio_and_the_end_of_stream_text_reach_the_lab_as_they_are()
    {
        await using var server = new StubLabServer();
        var connector = new LabSocketConnector(server.Context(new ManualTimeProvider()));
        var lab = await connector.ConnectAsync(CancellationToken.None);
        var fromRelay = await server.NextSocketAsync();
        var audio = Enumerable.Range(0, 2560).Select(index => (byte)(index * 3)).ToArray();

        await lab.SendAudioAsync(audio, CancellationToken.None);
        await lab.SendEndStreamAsync(CancellationToken.None);

        var first = await StubLabServer.ReceiveAsync(fromRelay);
        var second = await StubLabServer.ReceiveAsync(fromRelay);
        Assert.Equal(WebSocketMessageType.Binary, first?.Type);
        Assert.Equal(audio, first?.Bytes);
        Assert.Equal(WebSocketMessageType.Text, second?.Type);
        Assert.Equal("""{"type":"endStream"}""", Encoding.UTF8.GetString(second!.Value.Bytes));

        // Disposing drops the connection; the lab sees its session end. Afterwards the socket only fails.
        await lab.DisposeAsync();
        Assert.Null(await StubLabServer.ReceiveAsync(fromRelay));
        await Assert.ThrowsAsync<LabSocketException>(async () => await lab.SendAudioAsync(audio, CancellationToken.None));
        await Assert.ThrowsAsync<LabSocketException>(async () => await lab.ReceiveAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_close_frame_and_a_connection_that_ends_early_are_both_closed(bool withCloseFrame)
    {
        await using var server = new StubLabServer();
        var connector = new LabSocketConnector(server.Context(new ManualTimeProvider()));
        await using var lab = await connector.ConnectAsync(CancellationToken.None);
        var fromLab = await server.NextSocketAsync();
        var receiving = lab.ReceiveAsync(CancellationToken.None).AsTask();

        if (withCloseFrame)
        {
            await fromLab.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        else
        {
            server.DropConnections();
        }

        var message = await receiving.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(new LabMessage(LabMessageKind.Closed, default), message);
    }

    [Fact]
    public async Task The_production_connector_carries_a_whole_stream()
    {
        // No fake between the bridge and the lab socket: a relay with the production connector, dialling
        // the stub. The phone's audio arrives at the lab; the lab's events arrive at the phone, filtered.
        await using var server = new StubLabServer();
        await using var rig = await SpeechRig.StartWithConnectorAsync(connector: null, labPort: server.Port);
        using var phone = await rig.OpenAsync("qwen3");
        await phone.ExpectAsync("loading");
        var lab = await server.NextSocketAsync();

        await StubLabServer.SendTextAsync(lab, """{"type":"ready","model":"qwen3","sampleRate":16000}""");
        await phone.ExpectAsync("ready");
        await phone.SendAudioAsync(new byte[2560]);
        var audio = await StubLabServer.ReceiveAsync(lab);
        await StubLabServer.SendTextAsync(lab, """{"type":"transcript","turnId":1,"transcript":"Local words","private":"hidden"}""");
        var transcript = await phone.ReceiveTextAsync();
        await phone.SendEndStreamAsync();
        var command = await StubLabServer.ReceiveAsync(lab);
        await StubLabServer.SendTextAsync(lab, """{"type":"streamComplete","audioProcessedMs":80,"finalizationMs":2}""");
        var ending = await phone.ReadToEndAsync();

        Assert.Equal(2560, audio?.Bytes.Length);
        Assert.Equal(WebSocketMessageType.Binary, audio?.Type);
        Assert.Equal("""{"type":"transcript","turnId":1,"transcript":"Local words"}""", transcript);
        Assert.Equal("""{"type":"endStream"}""", Encoding.UTF8.GetString(command!.Value.Bytes));
        Assert.Equal(["""{"type":"streamComplete","audioProcessedMs":80,"finalizationMs":2}"""], ending.Frames);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);

        // The relay let go of the lab socket when the stream ended.
        Assert.Null(await StubLabServer.ReceiveAsync(lab));
        Assert.Single(server.RequestHeads);
    }

    /// <summary>A loopback port that was free a moment ago and has no listener.</summary>
    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
