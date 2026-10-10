using System.Text;
using System.Threading.Channels;
using LiveCue.Relay.Speech;

namespace LiveCue.Relay.Tests.Support;

/// <summary>
/// The fake lab socket of protocol 15.6, scriptable per test. Unless told otherwise each socket sends
/// <c>{"type":"ready","model":"&lt;m&gt;","sampleRate":16000}</c> on connection, answers each binary frame
/// with a <c>transcript</c> that carries a member the relay must never forward, and answers the
/// end-of-stream text with <c>speechComplete</c> followed by <c>streamComplete</c>.
/// </summary>
internal sealed class FakeLabSocketConnector : ILabSocketConnector
{
    private int _connectCalls;

    /// <summary>The model the lab says it has loaded in its first message.</summary>
    public string Model { get; set; } = "nemotron";

    /// <summary>True: every connection attempt fails (an unreachable lab).</summary>
    public bool ConnectFails { get; set; }

    /// <summary>When set, a connection attempt waits for it.</summary>
    public TaskCompletionSource? ConnectGate { get; set; }

    /// <summary>Replaces the first message of a new socket. Null keeps the "ready" message; an empty text sends none.</summary>
    public string? FirstMessage { get; set; }

    /// <summary>False: binary frames and the end-of-stream text get no automatic answers.</summary>
    public bool AutoRespond { get; set; } = true;

    public int ConnectCalls => Volatile.Read(ref _connectCalls);

    /// <summary>Every socket that was opened, in order.</summary>
    public List<FakeLabSocket> Sockets { get; } = [];

    /// <summary>The socket opened last.</summary>
    public FakeLabSocket Current => Sockets[^1];

    public async Task<ILabSocket> ConnectAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _connectCalls);
        if (ConnectGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        if (ConnectFails)
        {
            throw new LabSocketException();
        }

        var socket = new FakeLabSocket(AutoRespond);
        var first = FirstMessage ?? $$"""{"type":"ready","model":"{{Model}}","sampleRate":16000}""";
        if (first.Length > 0)
        {
            socket.Enqueue(first);
        }

        lock (Sockets)
        {
            Sockets.Add(socket);
        }

        return socket;
    }
}

/// <summary>One fake lab socket: what the relay sent it, and a queue of what it sends back.</summary>
internal sealed class FakeLabSocket(bool autoRespond) : ILabSocket
{
    private readonly Channel<LabMessage> _toRelay = Channel.CreateUnbounded<LabMessage>();
    private readonly Lock _gate = new();
    private readonly List<byte[]> _audio = [];

    /// <summary>Every audio frame the relay forwarded, in order.</summary>
    public IReadOnlyList<byte[]> AudioFrames
    {
        get
        {
            lock (_gate)
            {
                return [.. _audio];
            }
        }
    }

    /// <summary>The audio bytes the relay forwarded, in total.</summary>
    public int AudioBytes
    {
        get
        {
            lock (_gate)
            {
                return _audio.Sum(frame => frame.Length);
            }
        }
    }

    /// <summary>How often the relay sent the end-of-stream text.</summary>
    public int EndStreams { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>True: the next send or receive throws <see cref="LabSocketException"/>.</summary>
    public bool Broken { get; set; }

    /// <summary>The lab sends one text message.</summary>
    public void Enqueue(string json) =>
        _toRelay.Writer.TryWrite(new LabMessage(LabMessageKind.Text, Encoding.UTF8.GetBytes(json)));

    /// <summary>The lab sends a message the relay must treat as invalid (binary, or longer than C-23).</summary>
    public void EnqueueInvalid() => _toRelay.Writer.TryWrite(new LabMessage(LabMessageKind.Invalid, default));

    /// <summary>The lab closes the socket.</summary>
    public void Close() => _toRelay.Writer.TryWrite(new LabMessage(LabMessageKind.Closed, default));

    public ValueTask SendAudioAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        ThrowIfBroken();
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _audio.Add(frame.ToArray());
        }

        if (autoRespond)
        {
            Enqueue("""{"type":"transcript","transcript":"Local words","private":"hidden"}""");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SendEndStreamAsync(CancellationToken ct)
    {
        ThrowIfBroken();
        ct.ThrowIfCancellationRequested();
        EndStreams++;
        if (autoRespond)
        {
            Enqueue("""{"type":"speechComplete","transcript":"Local words","turnId":1,"audioProcessedMs":80}""");
            Enqueue("""{"type":"streamComplete","audioProcessedMs":80}""");
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<LabMessage> ReceiveAsync(CancellationToken ct)
    {
        ThrowIfBroken();
        var message = await _toRelay.Reader.ReadAsync(ct);
        ThrowIfBroken();
        return message;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    private void ThrowIfBroken()
    {
        if (Broken)
        {
            throw new LabSocketException();
        }
    }
}
