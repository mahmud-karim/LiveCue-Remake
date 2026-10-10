using System.Net.WebSockets;
using System.Threading.Channels;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Speech;

/// <summary>
/// The phone's side of one stream: receiving with the bound of C-20, the queue of frames toward the
/// phone (W-03) with its one writer (W-04), and the close handshake (C-58).
///
/// It decides nothing about the stream. What it learns it posts to the stream's mailbox, and the stream's
/// loop tells it what to queue. Two loops run here: one reads, one writes. A WebSocket allows exactly one
/// receive and one send at a time, so every frame and every close frame goes through the one writer.
///
/// What bounds a hostile phone:
/// - A message is assembled in a buffer of C-20 plus one byte. The first byte beyond C-20 is reported,
///   and everything after it is read into the same buffer and thrown away.
/// - At most one received message is waiting for the stream at any time: the reader does not read the
///   next one before the stream has taken the last.
/// - At most W-03 frames wait to be written. The stream aborts the socket when one more would.
/// - A write that does not finish within W-04 is reported, and the stream aborts the socket.
/// </summary>
internal sealed class PhoneSocket : IDisposable
{
    private readonly WebSocket _socket;
    private readonly TimeProvider _clock;
    private readonly Action<StreamSignal, ReadOnlyMemory<byte>> _post;
    private readonly Channel<Outgoing> _outgoing = Channel.CreateUnbounded<Outgoing>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly SemaphoreSlim _taken = new(0);
    private readonly ITimer _stallTimer;
    private readonly ITimer _closeWaitTimer;

    private Task _writer = Task.CompletedTask;
    private int _queued;
    private long _writeStarted;
    private volatile bool _writing;
    private volatile bool _discard;

    public PhoneSocket(WebSocket socket, TimeProvider clock, Action<StreamSignal, ReadOnlyMemory<byte>> post)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(post);
        _socket = socket;
        _clock = clock;
        _post = post;
        _stallTimer = clock.CreateTimer(
            static state => ((PhoneSocket)state!)._post(StreamSignal.PhoneWriteStalled, default),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _closeWaitTimer = clock.CreateTimer(
            static state => ((PhoneSocket)state!)._post(StreamSignal.CloseWaitElapsed, default),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// True when the write in progress has really lasted W-04. A stall signal that was already on its way
    /// when its write finished finds this false.
    /// </summary>
    public bool IsWriteStalled =>
        _writing && _clock.GetElapsedTime(Volatile.Read(ref _writeStarted)) >= ProtocolConstants.PhoneWriteStall;

    /// <summary>Starts the two loops.</summary>
    public void Start()
    {
        _ = Task.Run(ReceiveLoopAsync);
        _writer = Task.Run(WriteLoopAsync);
    }

    /// <summary>
    /// Queues one text frame. False when W-03 frames are already waiting: the phone is not reading, and
    /// the caller aborts.
    /// </summary>
    public bool TryQueue(ReadOnlyMemory<byte> frame) => TryQueue(new Outgoing(frame, IsClose: false, default, null));

    /// <summary>
    /// Queues the close frame behind everything queued so far. Nothing is written after it. The C-58
    /// wait begins when the writer reaches it.
    /// </summary>
    public bool TryQueueClose(WebSocketCloseStatus status, string reason) =>
        TryQueue(new Outgoing(default, IsClose: true, status, reason));

    /// <summary>The stream has dealt with the message that was posted last; the next one may be read.</summary>
    public void MessageTaken() => _taken.Release();

    /// <summary>
    /// From now on nothing that arrives is handed to the stream: it is read and thrown away, and only the
    /// phone's close frame or the end of the connection is still reported.
    /// </summary>
    public void StopDelivering()
    {
        _discard = true;
        _taken.Release();
    }

    /// <summary>Drops the connection without a close frame. Safe from any thread and more than once.</summary>
    public void Abort()
    {
        StopDelivering();
        if (_socket.State is not (WebSocketState.Closed or WebSocketState.Aborted))
        {
            _socket.Abort();
        }
    }

    /// <summary>
    /// The stream has ended; nothing more is queued. Returns true when the phone closed first and nothing
    /// is waiting to be written, so that its close frame can still be answered
    /// (<see cref="AnswerCloseAsync"/>). In every other case a socket whose close handshake is not
    /// complete is aborted here.
    /// </summary>
    public bool End()
    {
        StopDelivering();
        _outgoing.Writer.TryComplete();
        if (_socket.State == WebSocketState.CloseReceived && Volatile.Read(ref _queued) == 0)
        {
            return true;
        }

        Abort();
        return false;
    }

    /// <summary>
    /// Answers the phone's close frame with a normal close, within C-58; aborts when that does not work.
    /// </summary>
    public async Task AnswerCloseAsync()
    {
        try
        {
            using var limit = new CancellationTokenSource(ProtocolConstants.CloseHandshakeWait, _clock);
            await _writer.WaitAsync(limit.Token).ConfigureAwait(false);
            await _socket
                .CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, limit.Token)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Whatever the transport threw, the phone is gone: the socket is aborted below.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        Abort();
    }

    public void Dispose()
    {
        _stallTimer.Dispose();
        _closeWaitTimer.Dispose();
    }

    /// <summary>Sets a one-shot timer, or stops it. A timer that was disposed meanwhile stays stopped.</summary>
    private static void Set(ITimer timer, TimeSpan due)
    {
        try
        {
            timer.Change(due, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool TryQueue(Outgoing item)
    {
        if (Interlocked.Increment(ref _queued) > ProtocolConstants.MaxPhoneQueueFrames)
        {
            Interlocked.Decrement(ref _queued);
            return false;
        }

        if (_outgoing.Writer.TryWrite(item))
        {
            return true;
        }

        Interlocked.Decrement(ref _queued);
        return false;
    }

    private async Task ReceiveLoopAsync()
    {
        // One byte more than C-20, so that a message beyond the limit is seen and never stored.
        var buffer = new byte[ProtocolConstants.MaxPhoneFrameBytes + 1];
        try
        {
            while (true)
            {
                var count = 0;
                WebSocketMessageType type;
                bool complete;
                do
                {
                    var part = await _socket.ReceiveAsync(buffer.AsMemory(count), CancellationToken.None).ConfigureAwait(false);
                    if (part.MessageType == WebSocketMessageType.Close)
                    {
                        _post(StreamSignal.PhoneClosed, default);
                        return;
                    }

                    type = part.MessageType;
                    complete = part.EndOfMessage;
                    count += part.Count;
                }
                while (!complete && count < buffer.Length);

                if (!_discard && count > ProtocolConstants.MaxPhoneFrameBytes)
                {
                    _discard = true;
                    _post(StreamSignal.PhoneFrameTooLarge, default);
                }

                if (_discard)
                {
                    await DiscardAsync(buffer).ConfigureAwait(false);
                    return;
                }

                _post(
                    type == WebSocketMessageType.Binary ? StreamSignal.PhoneBinary : StreamSignal.PhoneText,
                    buffer.AsSpan(0, count).ToArray());
                await _taken.WaitAsync().ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Whatever the transport threw, the meaning is the same: the phone is gone.
        catch (Exception)
#pragma warning restore CA1031
        {
            _post(StreamSignal.PhoneGone, default);
        }
    }

    /// <summary>
    /// Reads and throws away until the phone's close frame arrives. The end of the connection, and the
    /// abort that follows C-58, end it with an exception.
    /// </summary>
    private async Task DiscardAsync(byte[] buffer)
    {
        while (true)
        {
            var part = await _socket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            if (part.MessageType == WebSocketMessageType.Close)
            {
                _post(StreamSignal.PhoneClosed, default);
                return;
            }
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var item in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                // The timers are set before the write, so that whoever sees the frame arrive can rely on them.
                Volatile.Write(ref _writeStarted, _clock.GetTimestamp());
                _writing = true;
                Set(_stallTimer, ProtocolConstants.PhoneWriteStall);
                try
                {
                    if (item.IsClose)
                    {
                        Set(_closeWaitTimer, ProtocolConstants.CloseHandshakeWait);
                        await _socket
                            .CloseOutputAsync(item.Status, item.Reason, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await _socket
                            .SendAsync(item.Frame, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    _writing = false;
                    Set(_stallTimer, Timeout.InfiniteTimeSpan);
                }

                Interlocked.Decrement(ref _queued);
                if (item.IsClose)
                {
                    return;
                }
            }
        }
#pragma warning disable CA1031 // Whatever the transport threw, the meaning is the same: the phone is gone.
        catch (Exception)
#pragma warning restore CA1031
        {
            _post(StreamSignal.PhoneGone, default);
        }
    }

    private readonly record struct Outgoing(
        ReadOnlyMemory<byte> Frame,
        bool IsClose,
        WebSocketCloseStatus Status,
        string? Reason);
}
