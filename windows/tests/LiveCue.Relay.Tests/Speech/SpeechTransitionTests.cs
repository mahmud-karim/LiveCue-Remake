using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Speech;

namespace LiveCue.Relay.Tests.Speech;

/// <summary>
/// The rows of the transition table (W-design 4.11) that no protocol statement names: the backlog toward
/// the lab (C-73), the socket lifetime (C-55), the queue toward the phone (W-03), a write that stalls
/// (W-04) and the wait for the phone's close (C-58). Every other row has its test in the classes named
/// after the protocol statements.
/// </summary>
public sealed class SpeechTransitionTests
{
    [Theory]
    [InlineData("backlog C-73")]
    [InlineData("lifetime C-55")]
    [InlineData("phone queue overflow W-03")]
    [InlineData("write stall W-04")]
    [InlineData("close-handshake wait C-58")]
    public async Task Each_row_of_the_table(string row)
    {
        switch (row)
        {
            case "backlog C-73":
                await BacklogAsync();
                break;
            case "lifetime C-55":
                await LifetimeAsync();
                break;
            case "phone queue overflow W-03":
                await UnreadingPhone.OverflowsTheQueueAsync();
                break;
            case "write stall W-04":
                await UnreadingPhone.StallsTheWriterAsync();
                break;
            default:
                await CloseWaitAsync();
                break;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_lab_socket_that_opens_after_the_stream_has_failed_is_disposed(bool afterTheClose)
    {
        // A connector that does not stop when its wait is cancelled, and delivers a socket late.
        var connector = new LateLabConnector();
        await using var rig = await SpeechRig.StartWithConnectorAsync(connector);
        using var phone = await rig.OpenAsync();
        await phone.ExpectAsync("loading");
        await SpeechWait.UntilAsync(() => connector.Calls == 1, "the connect began");

        // The stream fails while the lab socket is still being opened.
        await phone.SendAudioAsync(2560);
        await phone.ExpectAsync("error");
        if (afterTheClose)
        {
            // The next stream is admitted, so the first has ended. It is held before it would connect.
            await phone.ReadToEndAsync();
            rig.Model.PrepareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var next = await rig.OpenAdmittedAsync();
        }

        // The socket arrives when nobody wants it any more: while the stream is closing, or after it has
        // ended. Either way it is disposed and never used.
        var late = connector.Deliver();
        await SpeechWait.UntilAsync(() => late.Disposed, "the late lab socket is disposed");
        Assert.Equal(0, late.SendCalls);
    }

    /// <summary>
    /// "Streaming | phone binary, lab queue would pass C-73 | Fail(AUDIO_LIMIT)". The lab takes nothing,
    /// and the clock is ten seconds past "ready", so the allowance of C-72 is far away: only the backlog
    /// can refuse. Exactly C-73 bytes may wait; two more bytes may not.
    /// </summary>
    private static async Task BacklogAsync()
    {
        var connector = new StallingLabConnector();
        await using var rig = await SpeechRig.StartWithConnectorAsync(connector);
        var frames = ProtocolConstants.LabBacklogBytes / ProtocolConstants.AudioFrameBytes;
        var rest = ProtocolConstants.LabBacklogBytes % ProtocolConstants.AudioFrameBytes;

        // Exactly C-73 waiting: accepted. When the lab starts to take frames, every byte arrives.
        using (var atLimit = await rig.OpenAdmittedAsync())
        {
            await atLimit.ExpectAsync("ready");
            var lab = connector.Current;
            rig.Clock.Advance(TimeSpan.FromSeconds(10));
            Assert.True(AudioLimiter.AllowedBytes(TimeSpan.FromSeconds(10)) > 3 * ProtocolConstants.LabBacklogBytes);
            for (var sent = 0; sent < frames; sent++)
            {
                await atLimit.SendAudioAsync(ProtocolConstants.AudioFrameBytes);
            }

            await atLimit.SendAudioAsync(rest);
            await SpeechWait.UntilAsync(() => lab.SendCalls == 1, "the first write to the lab began");
            Assert.Equal(0, lab.WrittenBytes);

            lab.Release();
            await SpeechWait.UntilAsync(() => lab.WrittenBytes == ProtocolConstants.LabBacklogBytes, "the backlog was written");

            // What was written no longer counts: the same amount is accepted again.
            for (var sent = 0; sent < frames; sent++)
            {
                await atLimit.SendAudioAsync(ProtocolConstants.AudioFrameBytes);
            }

            await SpeechWait.UntilAsync(
                () => lab.WrittenBytes == ProtocolConstants.LabBacklogBytes + (frames * ProtocolConstants.AudioFrameBytes),
                "the second round was written");
            await atLimit.SendEndStreamAsync();
            var ending = await atLimit.ReadToEndAsync();
            Assert.Empty(ending.Errors);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.Status);
        }

        // Two bytes beyond C-73 waiting: refused, and the lab still has nothing.
        using (var beyond = await rig.OpenAdmittedAsync())
        {
            await beyond.ExpectAsync("ready");
            var lab = connector.Current;
            rig.Clock.Advance(TimeSpan.FromSeconds(10));
            for (var sent = 0; sent < frames; sent++)
            {
                await beyond.SendAudioAsync(ProtocolConstants.AudioFrameBytes);
            }

            await beyond.SendAudioAsync(rest);
            await beyond.SendAudioAsync(2);

            await SpeechFrameRuleTests.AssertEndsAsync(beyond, "AUDIO_LIMIT", WebSocketCloseStatus.InternalServerError);
            Assert.Equal(0, lab.WrittenBytes);
            Assert.True(lab.Disposed);
        }
    }

    /// <summary>
    /// "any | lifetime timer | Abort the phone socket". C-55 is a last line: every state has a shorter
    /// limit of its own, so with all of them working it never fires first. Here the clock jumps past all
    /// of them at once, for a phone that never answers anything: whatever each limit did on the way,
    /// the socket is gone, without one more moment on the clock, and nothing is left behind.
    /// </summary>
    private static async Task LifetimeAsync()
    {
        await using var rig = await SpeechRig.StartAsync();
        var timersAtRest = rig.Clock.ScheduledTimers;
        using var phone = await SpeechStates.ReachAsync(rig, "awaiting-lab-ready");
        var lab = rig.Lab.Current;
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());

        rig.Clock.Advance(ProtocolConstants.SocketLifetime);

        // The phone answers no close. The relay ended the connection by itself.
        await SpeechWait.UntilAsync(() => lab.Disposed, "the lab socket is disposed");
        rig.Lab.FirstMessage = null;
        using (var next = await rig.OpenAdmittedAsync())
        {
            await next.ExpectAsync("ready");
        }

        var ending = await phone.ReadToEndAsync(answerClose: false);
        Assert.DoesNotContain(ending.Types, type => type is not ("loading" or "error"));
        await SpeechWait.UntilAsync(() => rig.Clock.ScheduledTimers == timersAtRest, "every timer is stopped");
    }

    /// <summary>
    /// "Closing | phone close frame received, or C-58 passed since the close was sent | Abort the socket
    /// if it is still open | Closed". A phone that reads the error and the close frame and then stays
    /// silent holds the stream, and after "ready" also the lease, for exactly C-58.
    /// </summary>
    private static async Task CloseWaitAsync()
    {
        await using var rig = await SpeechRig.StartAsync();
        var timersAtRest = rig.Clock.ScheduledTimers;
        using var phone = await rig.OpenReadyAsync();
        await phone.SendAudioAsync(2561);

        var ending = await phone.ReadToEndAsync(answerClose: false);
        Assert.Equal("AUDIO_LIMIT", ending.Reason);
        Assert.Equal(WebSocketState.CloseReceived, phone.Socket.State);

        // One millisecond before C-58 the relay still waits for the answer.
        rig.Clock.Advance(ProtocolConstants.CloseHandshakeWait - TimeSpan.FromMilliseconds(1));
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());

        // At C-58 it stops waiting: the lease is free and nothing of the stream is left.
        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        using (var next = await rig.OpenAdmittedAsync())
        {
            await next.ExpectAsync("ready");
        }

        await SpeechWait.UntilAsync(() => rig.Clock.ScheduledTimers == timersAtRest, "every timer is stopped");
        Assert.True(rig.Lab.Sockets[0].Disposed);
    }
}

/// <summary>
/// A phone that stops reading (test plan S-29). The lab keeps producing text; the relay may neither
/// store it without bound nor wait for ever.
/// </summary>
internal static class UnreadingPhone
{
    /// <summary>A transcript event of the largest text a frame carries (C-22), three bytes per character.</summary>
    private static readonly string LargeTranscript =
        $$"""{"type":"transcript","turnId":1,"transcript":"{{new string('語', ProtocolConstants.MaxTranscriptUnits)}}"}""";

    /// <summary>
    /// "any | phone queue overflow (W-03) | Abort the phone socket". The lab produces far more frames
    /// than W-03 while the phone reads nothing. No time passes on the clock: the queue alone ends it.
    /// </summary>
    public static async Task OverflowsTheQueueAsync()
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.AutoRespond = false;
        using var phone = await rig.OpenReadyAsync();
        var lab = rig.Lab.Current;

        var produced = 3 * ProtocolConstants.MaxPhoneQueueFrames;
        for (var sent = 0; sent < produced; sent++)
        {
            lab.Enqueue(LargeTranscript);
        }

        // The stream is over without a word and without the clock: the lab socket is disposed, the lease
        // is free, and the desktop was told that the stream stopped.
        await SpeechWait.UntilAsync(() => lab.Disposed, "the lab socket is disposed");
        await rig.Events.WaitForAsync<SpeechStreamChanged>(changed => changed.Phase == SpeechStreamPhase.Stopped);
        using (var next = await rig.OpenAdmittedAsync())
        {
        }

        // When the phone reads at last, it finds some frames and then a connection that was dropped:
        // no error frame and no close frame were ever queued behind what it did not read.
        var ending = await phone.ReadToEndAsync();
        Assert.True(ending.Dropped);
        Assert.InRange(ending.Frames.Count, 0, produced - ProtocolConstants.MaxPhoneQueueFrames);
        Assert.All(ending.Types, type => Assert.Equal("transcript", type));
    }

    /// <summary>
    /// "any | phone write stall (W-04) | Abort the phone socket". Fewer frames than W-03, but more bytes
    /// than the connection takes while the phone reads nothing: one write cannot finish. The stream
    /// lives until W-04 has passed on the clock, and not longer.
    /// </summary>
    public static async Task StallsTheWriterAsync()
    {
        await using var rig = await SpeechRig.StartAsync();
        rig.Lab.AutoRespond = false;
        using var phone = await rig.OpenReadyAsync();
        var lab = rig.Lab.Current;

        var produced = ProtocolConstants.MaxPhoneQueueFrames / 2;
        for (var sent = 0; sent < produced; sent++)
        {
            lab.Enqueue(LargeTranscript);
        }

        // Real time alone changes nothing: the stream is still there.
        await Task.Delay(300);
        Assert.False(lab.Disposed);
        Assert.Equal("SPEECH_BUSY 1008", await rig.AttemptAsync());

        // W-04 on the clock ends it. (A second step covers a write that began after the first.)
        for (var step = 0; step < 2 && !lab.Disposed; step++)
        {
            rig.Clock.Advance(ProtocolConstants.PhoneWriteStall);
            await WaitBrieflyAsync(() => lab.Disposed);
        }

        Assert.True(lab.Disposed);
        using (var next = await rig.OpenAdmittedAsync())
        {
        }

        var ending = await phone.ReadToEndAsync();
        Assert.True(ending.Dropped);
        Assert.InRange(ending.Frames.Count, 0, produced - 1);
        Assert.All(ending.Types, type => Assert.Equal("transcript", type));
    }

    private static async Task WaitBrieflyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }
    }
}

/// <summary>
/// A lab connector that ignores the cancellation of its wait and hands over its socket only when the
/// test says so: a connection attempt that succeeds too late.
/// </summary>
internal sealed class LateLabConnector : ILabSocketConnector
{
    private readonly TaskCompletionSource<ILabSocket> _socket = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>The pending attempt completes now, with this socket.</summary>
    public StallingLabSocket Deliver()
    {
        var socket = new StallingLabSocket();
        _socket.TrySetResult(socket);
        return socket;
    }

    public Task<ILabSocket> ConnectAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return _socket.Task;
    }
}

/// <summary>A lab connector whose sockets take no audio until the test lets them.</summary>
internal sealed class StallingLabConnector : ILabSocketConnector
{
    private readonly Lock _gate = new();
    private readonly List<StallingLabSocket> _sockets = [];

    /// <summary>The socket opened last.</summary>
    public StallingLabSocket Current
    {
        get
        {
            lock (_gate)
            {
                return _sockets[^1];
            }
        }
    }

    public Task<ILabSocket> ConnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var socket = new StallingLabSocket();
        lock (_gate)
        {
            _sockets.Add(socket);
        }

        return Task.FromResult<ILabSocket>(socket);
    }
}

/// <summary>
/// A lab socket for "nemotron" whose writes wait: nothing is taken until <see cref="Release"/>. After the
/// end-of-stream text it answers with <c>streamComplete</c>.
/// </summary>
internal sealed class StallingLabSocket : ILabSocket
{
    private readonly Channel<LabMessage> _toRelay = Channel.CreateUnbounded<LabMessage>();
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _sendCalls;
    private int _writtenBytes;

    public StallingLabSocket()
    {
        Emit("""{"type":"ready","model":"nemotron","sampleRate":16000}""");
    }

    /// <summary>Writes that began, finished or not.</summary>
    public int SendCalls => Volatile.Read(ref _sendCalls);

    /// <summary>Audio bytes of the writes that finished.</summary>
    public int WrittenBytes => Volatile.Read(ref _writtenBytes);

    public bool Disposed { get; private set; }

    /// <summary>From now on every write finishes at once.</summary>
    public void Release() => _released.TrySetResult();

    public void Emit(string json) =>
        _toRelay.Writer.TryWrite(new LabMessage(LabMessageKind.Text, Encoding.UTF8.GetBytes(json)));

    public async ValueTask SendAudioAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        Interlocked.Increment(ref _sendCalls);
        await _released.Task.WaitAsync(ct);
        Interlocked.Add(ref _writtenBytes, frame.Length);
    }

    public async ValueTask SendEndStreamAsync(CancellationToken ct)
    {
        await _released.Task.WaitAsync(ct);
        Emit("""{"type":"streamComplete","audioProcessedMs":0}""");
    }

    public ValueTask<LabMessage> ReceiveAsync(CancellationToken ct) => _toRelay.Reader.ReadAsync(ct);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
