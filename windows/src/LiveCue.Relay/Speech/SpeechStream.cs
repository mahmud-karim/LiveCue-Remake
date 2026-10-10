using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using LiveCue.Relay.Core;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Speech;

/// <summary>The states of one stream (W-design 4.11).</summary>
internal enum StreamState
{
    Admitting,
    Preparing,
    Connecting,
    AwaitingLabReady,
    Streaming,
    Finalizing,
    Closing,
    Closed,
}

/// <summary>Everything that can happen to a stream. Each is posted to its mailbox and handled by its loop.</summary>
internal enum StreamSignal
{
    PhoneBinary,
    PhoneText,
    PhoneFrameTooLarge,

    /// <summary>The phone's close frame arrived.</summary>
    PhoneClosed,

    /// <summary>The connection to the phone failed, in either direction.</summary>
    PhoneGone,

    /// <summary>A frame toward the phone has been in the writer for W-04.</summary>
    PhoneWriteStalled,

    /// <summary>C-58 has passed since the relay's close frame was handed to the writer.</summary>
    CloseWaitElapsed,

    Prepared,
    LabConnected,
    LabConnectFailed,
    LabMessage,

    /// <summary>A receive from the lab or a send to it threw <see cref="LabSocketException"/>.</summary>
    LabFailed,

    LoadingTimer,
    LoadLimitTimer,
    LifetimeTimer,
    IdleTimer,
    FinalizeTimer,
    StreamTimer,

    Paused,
    PairingRevoked,
    Stopping,
}

/// <summary>
/// One speech stream: one accepted socket from admission to the last close (protocol section 10), as the
/// transition table of W-design 4.11.
///
/// It runs as a single loop over one mailbox. Everything else only posts to the mailbox: the phone's
/// reader and writer, the lab's reader and writer, the prepare and connect tasks, the timers and the three
/// tokens (pause, pairing reset, stop). Only the loop changes the state and decides what is sent, so no
/// lock exists and no rule can be applied out of order. The loop never waits for anything but the next
/// mail: a slow phone or a slow lab cannot stop it from seeing a timer.
///
/// Inside one step, timers are set and events are published before a frame is queued. Whoever sees a
/// frame arrive can therefore rely on the state that frame announces.
/// </summary>
internal sealed class SpeechStream : IDisposable
{
    private const string DiagnosticSource = "speech";

    private readonly RelayContext _context;
    private readonly ILocalModelController _localModel;
    private readonly ILabSocketConnector _connector;
    private readonly SpeechLease _speechLease;
    private readonly SpeechConnection _connection;
    private readonly Channel<Mail> _mailbox = Channel.CreateUnbounded<Mail>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly PhoneSocket _phone;
    private readonly StreamWatchdogs _watchdogs;
    private readonly AudioLimiter _limiter;
    private readonly CancellationTokenSource _work = new();
    private readonly SemaphoreSlim _labMessageTaken = new(0);
    private readonly List<CancellationTokenRegistration> _watched = [];

    private StreamState _state = StreamState.Admitting;
    private string _model = string.Empty;
    private IDisposable? _lease;
    private ILabSocket? _lab;
    private Channel<LabOutgoing>? _labQueue;
    private Task _labDisposal = Task.CompletedTask;
    private long _labBacklog;
    private bool _admitted;
    private bool _failed;
    private bool _endPublished;
    private bool _workStopped;

    public SpeechStream(
        RelayContext context,
        ILocalModelController localModel,
        ILabSocketConnector connector,
        SpeechLease speechLease,
        SpeechConnection connection)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(localModel);
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(speechLease);
        ArgumentNullException.ThrowIfNull(connection);
        _context = context;
        _localModel = localModel;
        _connector = connector;
        _speechLease = speechLease;
        _connection = connection;
        _phone = new PhoneSocket(connection.Socket, context.Clock, (signal, bytes) => Post(new Mail(signal, bytes)));
        _watchdogs = new StreamWatchdogs(context.Clock, connection.UpgradeTimestamp, signal => Post(new Mail(signal)));
        _limiter = new AudioLimiter(context.Clock);
    }

    /// <summary>True while a state before <c>Closing</c> holds: the stream is still alive for the phone.</summary>
    private bool IsOpen => _state is not (StreamState.Closing or StreamState.Closed);

    /// <summary>True while the model is being prepared and the phone is shown <c>loading</c>.</summary>
    private bool IsLoading =>
        _state is StreamState.Preparing or StreamState.Connecting or StreamState.AwaitingLabReady;

    /// <summary>
    /// Runs the stream to its end: the phone socket is closed or aborted, the lab socket is disposed and
    /// the lease is released when this completes. Never throws.
    /// </summary>
    public async Task RunAsync()
    {
        try
        {
            Admit();
            _phone.Start();
            while (_state != StreamState.Closed)
            {
                Handle(await _mailbox.Reader.ReadAsync().ConfigureAwait(false));
            }
        }
#pragma warning disable CA1031 // Nothing may escape a stream: it ends, and says only that it failed.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _context.Events.Diagnostic(DiagnosticSource, "A speech stream failed unexpectedly.", exception);
            _phone.Abort();
            _state = StreamState.Closed;
        }

        try
        {
            await FinishAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // As above.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _context.Events.Diagnostic(DiagnosticSource, "A speech stream could not be closed cleanly.", exception);
            _phone.Abort();
            ReleaseLease();
        }
    }

    public void Dispose()
    {
        _watchdogs.Dispose();
        _phone.Dispose();
        _work.Dispose();
    }

    // ---- Admission ---------------------------------------------------------------------------------

    /// <summary>Steps S-1 to S-6, then the first row of the table. Runs before anything is read.</summary>
    private void Admit()
    {
        var refusal = SpeechAdmission.Admit(
            _connection, _context.Access, _localModel, _speechLease, out _model, out _lease);
        if (refusal is { } error)
        {
            Fail(error);
            return;
        }

        _admitted = true;
        _state = StreamState.Preparing;
        _watchdogs.StartLoading();
        Watch(StreamSignal.Paused, _context.Access.WhileAccepting);
        Watch(StreamSignal.PairingRevoked, _connection.PairingRevoked);
        Watch(StreamSignal.Stopping, _context.Access.Stopping);
        Publish(SpeechStreamPhase.Loading, code: null, FixedTexts.SpeechLoading(_model));

        var model = _model;
        var wait = _work.Token;
        _ = Task.Run(() => PrepareAsync(model, wait), CancellationToken.None);
        Queue(SpeechFrames.Loading(_model, ElapsedSeconds()));
    }

    // ---- The loop's one step -----------------------------------------------------------------------

    private void Handle(in Mail mail)
    {
        switch (mail.Signal)
        {
            case StreamSignal.PhoneBinary:
                OnPhoneBinary(mail.Bytes);
                _phone.MessageTaken();
                break;

            case StreamSignal.PhoneText:
                OnPhoneText(mail.Bytes);
                _phone.MessageTaken();
                break;

            case StreamSignal.PhoneFrameTooLarge:
                if (IsOpen)
                {
                    Fail(RelayErrors.FrameTooLarge);
                }

                break;

            case StreamSignal.PhoneClosed:
            case StreamSignal.PhoneGone:
                OnPhoneGone();
                break;

            case StreamSignal.PhoneWriteStalled:
                if (_phone.IsWriteStalled)
                {
                    AbortPhone();
                }

                break;

            case StreamSignal.CloseWaitElapsed:
                if (_state == StreamState.Closing)
                {
                    AbortPhone();
                }

                break;

            case StreamSignal.Prepared:
                OnPrepared(mail.Outcome);
                break;

            case StreamSignal.LabConnected:
                OnLabConnected(mail.Lab!);
                break;

            case StreamSignal.LabConnectFailed:
                if (_state == StreamState.Connecting)
                {
                    Fail(RelayErrors.LabConnectionFailed);
                }

                break;

            case StreamSignal.LabMessage:
                OnLabMessage(mail.LabKind, mail.Bytes);
                _labMessageTaken.Release();
                break;

            case StreamSignal.LabFailed:
                if (_state is StreamState.AwaitingLabReady or StreamState.Streaming or StreamState.Finalizing)
                {
                    Fail(RelayErrors.LabConnectionFailed);
                }

                break;

            case StreamSignal.LoadingTimer:
                if (IsLoading)
                {
                    Queue(SpeechFrames.Loading(_model, ElapsedSeconds()));
                }

                break;

            case StreamSignal.LoadLimitTimer:
                if (IsLoading)
                {
                    Fail(RelayErrors.ModelLoadTimeout);
                }

                break;

            case StreamSignal.LifetimeTimer:
                AbortPhone();
                break;

            case StreamSignal.IdleTimer:
                if (_state == StreamState.Streaming && _watchdogs.IdleLimitPassed)
                {
                    Fail(RelayErrors.AudioIdleTimeout);
                }

                break;

            case StreamSignal.FinalizeTimer:
                if (_state == StreamState.Finalizing)
                {
                    Fail(RelayErrors.FinalizeTimeout);
                }

                break;

            case StreamSignal.StreamTimer:
                if (_state is StreamState.Streaming or StreamState.Finalizing)
                {
                    Fail(RelayErrors.SessionLimit);
                }

                break;

            case StreamSignal.Paused:
                if (IsOpen)
                {
                    Fail(RelayErrors.RelayPaused);
                }

                break;

            case StreamSignal.PairingRevoked:
                if (IsOpen)
                {
                    Fail(RelayErrors.PairingChanged);
                }

                break;

            case StreamSignal.Stopping:
                if (IsOpen)
                {
                    Fail(RelayErrors.RelayStopping);
                }

                break;
        }
    }

    // ---- The phone ---------------------------------------------------------------------------------

    private void OnPhoneBinary(ReadOnlyMemory<byte> audio)
    {
        if (IsLoading || _state == StreamState.Finalizing)
        {
            Fail(RelayErrors.AudioOutOfSequence);
            return;
        }

        if (_state != StreamState.Streaming || audio.IsEmpty)
        {
            // An empty frame while streaming is dropped. It is not audio: the idle limit keeps counting.
            return;
        }

        if ((audio.Length & 1) != 0
            || !_limiter.TryAccept(audio.Length)
            || Interlocked.Read(ref _labBacklog) + audio.Length > ProtocolConstants.LabBacklogBytes)
        {
            // An odd length, more than the allowance of C-72, or more than the backlog of C-73.
            Fail(RelayErrors.AudioLimit);
            return;
        }

        _watchdogs.RestartIdle();
        Interlocked.Add(ref _labBacklog, audio.Length);
        _labQueue!.Writer.TryWrite(new LabOutgoing(audio, EndStream: false));
    }

    private void OnPhoneText(ReadOnlyMemory<byte> text)
    {
        if (IsLoading || _state == StreamState.Finalizing)
        {
            // Before "ready" and after "endStream" every frame is out of sequence, whatever it says.
            Fail(RelayErrors.AudioOutOfSequence);
            return;
        }

        if (_state != StreamState.Streaming)
        {
            return;
        }

        if (!IsEndStream(text))
        {
            Fail(RelayErrors.UnsupportedCommand);
            return;
        }

        _watchdogs.StartFinalizing();
        _state = StreamState.Finalizing;
        _labQueue!.Writer.TryWrite(new LabOutgoing(default, EndStream: true));
    }

    /// <summary>
    /// The phone closed, or its connection failed. Before <c>Closing</c> that ends an admitted stream as
    /// "stopped"; while closing it is what the relay was waiting for.
    /// </summary>
    private void OnPhoneGone()
    {
        if (_state == StreamState.Closed)
        {
            return;
        }

        if (IsOpen)
        {
            if (_state == StreamState.Preparing)
            {
                // Decision A-07: the lease goes at once; a load that was begun goes on without this stream.
                ReleaseLease();
            }

            PublishEnd(SpeechStreamPhase.Stopped, code: null, FixedTexts.SpeechStopped(_model));
        }

        EnterClosed();
    }

    /// <summary>The lifetime passed, the phone does not read, or it did not answer the close: no more words.</summary>
    private void AbortPhone()
    {
        if (_state == StreamState.Closed)
        {
            return;
        }

        _phone.Abort();
        EnterClosed();
    }

    /// <summary>Any JSON object of at most C-25 bytes whose <c>type</c> is the string <c>endStream</c>.</summary>
    private static bool IsEndStream(ReadOnlyMemory<byte> text) =>
        text.Length <= ProtocolConstants.MaxSpeechTextBytes
        && WireJson.TryParseObject(text, out var command)
        && command.TryGetProperty("type", out var type)
        && type.ValueKind == JsonValueKind.String
        && type.ValueEquals("endStream");

    // ---- Prepare and connect -----------------------------------------------------------------------

    private async Task PrepareAsync(string model, CancellationToken wait)
    {
        PrepareOutcome outcome;
        try
        {
            outcome = await _localModel.PrepareAsync(model, wait).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            // Only the wait ended. A load that was begun continues as an ordinary operation.
            return;
        }
#pragma warning disable CA1031 // Protocol 10.6: whatever went wrong is reduced to "the load failed".
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _context.Events.Diagnostic(DiagnosticSource, "Preparing the PC model failed unexpectedly.", exception);
            outcome = PrepareOutcome.LoadFailed;
        }

        Post(new Mail(StreamSignal.Prepared, Outcome: outcome));
    }

    private void OnPrepared(PrepareOutcome outcome)
    {
        if (_state != StreamState.Preparing)
        {
            return;
        }

        switch (outcome)
        {
            case PrepareOutcome.Ready:
                _state = StreamState.Connecting;
                var wait = _work.Token;
                _ = Task.Run(() => ConnectAsync(wait), CancellationToken.None);
                break;

            case PrepareOutcome.Busy:
                Fail(RelayErrors.ModelBusy);
                break;

            case PrepareOutcome.OperationInProgress:
                Fail(RelayErrors.ModelOperationInProgress);
                break;

            default:
                Fail(RelayErrors.ModelLoadFailed);
                break;
        }
    }

    private async Task ConnectAsync(CancellationToken wait)
    {
        ILabSocket lab;
        try
        {
            lab = await _connector.ConnectAsync(wait).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            return;
        }
#pragma warning disable CA1031 // Protocol 10.6: whatever went wrong is reduced to "the lab socket failed".
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (exception is not LabSocketException)
            {
                _context.Events.Diagnostic(DiagnosticSource, "Opening the lab socket failed unexpectedly.", exception);
            }

            Post(new Mail(StreamSignal.LabConnectFailed));
            return;
        }

        if (!Post(new Mail(StreamSignal.LabConnected, Lab: lab)))
        {
            // The stream ended while the socket was being opened.
            await DisposeLabAsync(lab).ConfigureAwait(false);
        }
    }

    private void OnLabConnected(ILabSocket lab)
    {
        if (_state != StreamState.Connecting)
        {
            // The stream moved on while the socket was being opened: it is of no use.
            _labDisposal = Task.WhenAll(_labDisposal, DisposeLabAsync(lab));
            return;
        }

        _lab = lab;
        _state = StreamState.AwaitingLabReady;
        var wait = _work.Token;
        _ = Task.Run(() => LabReceiveLoopAsync(lab, wait), CancellationToken.None);
    }

    // ---- The lab -----------------------------------------------------------------------------------

    private void OnLabMessage(LabMessageKind kind, ReadOnlyMemory<byte> bytes)
    {
        if (_state is not (StreamState.AwaitingLabReady or StreamState.Streaming or StreamState.Finalizing))
        {
            return;
        }

        var awaitingReady = _state == StreamState.AwaitingLabReady;
        if (kind == LabMessageKind.Closed)
        {
            Fail(RelayErrors.LabDisconnected);
            return;
        }

        if (kind != LabMessageKind.Text || !WireJson.TryParseObject(bytes, out var message))
        {
            // A binary or oversized message, or text that is not one JSON object.
            Fail(awaitingReady ? RelayErrors.ModelNotReady : RelayErrors.LabProtocolError);
            return;
        }

        if (awaitingReady)
        {
            // The lab's first message must be "ready" for the requested model at 16,000 Hz. Anything
            // else, an event of a type the relay does not know included, means the model is not ready.
            if (LabEventFilter.IsReadyFor(message, _model))
            {
                BecomeReady();
            }
            else
            {
                Fail(RelayErrors.ModelNotReady);
            }

            return;
        }

        var lab = LabEventFilter.Classify(message);
        switch (lab)
        {
            case LabEventKind.Error:
                Fail(RelayErrors.LabInferenceFailed);
                break;

            case LabEventKind.SpeechStart:
            case LabEventKind.Transcript:
            case LabEventKind.SpeechComplete:
                Forward(lab, message);
                break;

            case LabEventKind.StreamComplete:
                if (_state == StreamState.Streaming)
                {
                    Fail(RelayErrors.LabProtocolError);
                }
                else
                {
                    Forward(lab, message);
                }

                break;

            default:
                // Another type, "audioProgress", or a second "ready": dropped.
                break;
        }
    }

    private void BecomeReady()
    {
        var sinceUpgrade = _watchdogs.SinceUpgrade;
        _watchdogs.StopLoading();
        _limiter.Start();
        _watchdogs.StartStreaming();
        _state = StreamState.Streaming;

        var queue = Channel.CreateUnbounded<LabOutgoing>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        _labQueue = queue;
        var lab = _lab!;
        var wait = _work.Token;
        _ = Task.Run(() => LabSendLoopAsync(lab, queue.Reader, wait), CancellationToken.None);

        Publish(SpeechStreamPhase.Ready, code: null, FixedTexts.SpeechReady(_model));
        Queue(SpeechFrames.Ready(_model, sinceUpgrade));
    }

    /// <summary>Sends the frame rebuilt from the allowlist. <c>streamComplete</c> also ends the stream cleanly.</summary>
    private void Forward(LabEventKind kind, JsonElement message)
    {
        var frame = LabEventFilter.Rebuild(kind, message, out var transcript);
        if (frame is null)
        {
            Fail(RelayErrors.LabProtocolError);
            return;
        }

        if (kind == LabEventKind.StreamComplete)
        {
            EnterClosing();
            PublishEnd(SpeechStreamPhase.Stopped, code: null, FixedTexts.SpeechStopped(_model));
            Queue(frame);
            QueueClose(WebSocketCloseStatus.NormalClosure, string.Empty);
            return;
        }

        if (kind == LabEventKind.SpeechComplete && transcript is not null)
        {
            _context.Events.Publish(new SpeechText(_context.Events.Now, transcript));
        }

        Queue(frame);
    }

    private async Task LabReceiveLoopAsync(ILabSocket lab, CancellationToken wait)
    {
        try
        {
            while (true)
            {
                var message = await lab.ReceiveAsync(wait).ConfigureAwait(false);
                if (!Post(new Mail(StreamSignal.LabMessage, message.Bytes, LabKind: message.Kind))
                    || message.Kind == LabMessageKind.Closed)
                {
                    return;
                }

                // One lab message at a time: the next is read when the loop has dealt with this one.
                await _labMessageTaken.WaitAsync(wait).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // Protocol 10.6: whatever went wrong is reduced to "the lab socket failed".
        catch (Exception exception)
#pragma warning restore CA1031
        {
            ReportLabFailure(exception, wait);
        }
    }

    private async Task LabSendLoopAsync(ILabSocket lab, ChannelReader<LabOutgoing> queue, CancellationToken wait)
    {
        try
        {
            await foreach (var item in queue.ReadAllAsync(wait).ConfigureAwait(false))
            {
                if (item.EndStream)
                {
                    await lab.SendEndStreamAsync(wait).ConfigureAwait(false);
                }
                else
                {
                    await lab.SendAudioAsync(item.Audio, wait).ConfigureAwait(false);
                    Interlocked.Add(ref _labBacklog, -item.Audio.Length);
                }
            }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // Protocol 10.6: whatever went wrong is reduced to "the lab socket failed".
        catch (Exception exception)
#pragma warning restore CA1031
        {
            ReportLabFailure(exception, wait);
        }
    }

    private void ReportLabFailure(Exception exception, CancellationToken wait)
    {
        if (wait.IsCancellationRequested)
        {
            // The stream already let go of the lab; what its socket says now means nothing.
            return;
        }

        if (exception is not LabSocketException)
        {
            _context.Events.Diagnostic(DiagnosticSource, "The lab socket failed unexpectedly.", exception);
        }

        Post(new Mail(StreamSignal.LabFailed));
    }

    private static async Task DisposeLabAsync(ILabSocket? lab)
    {
        if (lab is null)
        {
            return;
        }

        try
        {
            await lab.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A socket that cannot be disposed cleanly is still gone.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    // ---- Ending ------------------------------------------------------------------------------------

    /// <summary>
    /// Once per stream; a second failure is ignored, so exactly one <c>error</c> frame is ever sent. The
    /// frame is followed by the close frame with the close code of protocol 10.6 and the code as its
    /// reason.
    /// </summary>
    private void Fail(RelayError error)
    {
        if (_failed || !IsOpen)
        {
            return;
        }

        _failed = true;
        var from = _state;
        var status = from == StreamState.Admitting ? WebSocketCloseStatus.PolicyViolation
            : error.Code == RelayCodes.FrameTooLarge ? WebSocketCloseStatus.MessageTooBig
            : error.Code == RelayCodes.RelayStopping ? WebSocketCloseStatus.EndpointUnavailable
            : WebSocketCloseStatus.InternalServerError;

        if (from == StreamState.Preparing)
        {
            // Decision A-07: the lease goes at once, before the close handshake; only the wait is cancelled.
            ReleaseLease();
        }

        EnterClosing();
        if (from != StreamState.Admitting)
        {
            PublishEnd(SpeechStreamPhase.Failed, error.Code, error.Text);
        }

        Queue(SpeechFrames.Error(error));
        QueueClose(status, error.Code);
    }

    /// <summary>
    /// The stream is over for the phone; what is still queued is written, then the close frame. The lab
    /// has no further use: its loops are cancelled and its socket is disposed at once.
    /// </summary>
    private void EnterClosing()
    {
        _state = StreamState.Closing;
        _watchdogs.StopAllButLifetime();
        _phone.StopDelivering();
        StopWork();
    }

    private void EnterClosed()
    {
        _state = StreamState.Closed;
        _watchdogs.Dispose();
        _phone.StopDelivering();
        StopWork();
    }

    /// <summary>Ends the wait on prepare, the connect and both lab loops, and disposes the lab socket.</summary>
    private void StopWork()
    {
        if (_workStopped)
        {
            return;
        }

        _workStopped = true;
        _work.Cancel();
        _labQueue?.Writer.TryComplete();
        _labDisposal = Task.WhenAll(_labDisposal, DisposeLabAsync(_lab));
        _lab = null;
    }

    /// <summary>The entry of <c>Closed</c>: everything the stream held is given back.</summary>
    private async Task FinishAsync()
    {
        _watchdogs.Dispose();
        StopWork();
        foreach (var registration in _watched)
        {
            registration.Dispose();
        }

        _mailbox.Writer.TryComplete();
        while (_mailbox.Reader.TryRead(out var late))
        {
            if (late.Lab is { } unused)
            {
                _labDisposal = Task.WhenAll(_labDisposal, DisposeLabAsync(unused));
            }
        }

        await _labDisposal.ConfigureAwait(false);

        // Both sockets are closed from here on: the lab's is disposed, and the phone's is either aborted
        // now or has sent its close frame and only waits for the answer.
        var answerClose = _phone.End();
        ReleaseLease();
        if (_admitted)
        {
            PublishEnd(SpeechStreamPhase.Stopped, code: null, FixedTexts.SpeechStopped(_model));
        }

        if (answerClose)
        {
            await _phone.AnswerCloseAsync().ConfigureAwait(false);
        }
    }

    private void ReleaseLease()
    {
        _lease?.Dispose();
        _lease = null;
    }

    // ---- Small helpers -----------------------------------------------------------------------------

    private bool Post(in Mail mail) => _mailbox.Writer.TryWrite(mail);

    private void Watch(StreamSignal signal, CancellationToken token)
    {
        if (token.CanBeCanceled)
        {
            _watched.Add(token.Register(() => Post(new Mail(signal))));
        }
    }

    /// <summary>Queues a frame for the phone. A queue that is full (W-03) means the phone is not reading.</summary>
    private void Queue(ReadOnlyMemory<byte> frame)
    {
        if (_state != StreamState.Closed && !_phone.TryQueue(frame))
        {
            AbortPhone();
        }
    }

    private void QueueClose(WebSocketCloseStatus status, string reason)
    {
        if (_state != StreamState.Closed && !_phone.TryQueueClose(status, reason))
        {
            AbortPhone();
        }
    }

    private void Publish(SpeechStreamPhase phase, string? code, string message) =>
        _context.Events.Publish(new SpeechStreamChanged(_context.Events.Now, phase, _model, code, message));

    /// <summary>An admitted stream publishes exactly one ending: "failed" with its code, or "stopped".</summary>
    private void PublishEnd(SpeechStreamPhase phase, string? code, string message)
    {
        if (_endPublished)
        {
            return;
        }

        _endPublished = true;
        Publish(phase, code, message);
    }

    /// <summary>Whole seconds since the upgrade.</summary>
    private int ElapsedSeconds() =>
        (int)Math.Min(int.MaxValue, Math.Floor(_watchdogs.SinceUpgrade.TotalSeconds));

    private readonly record struct Mail(
        StreamSignal Signal,
        ReadOnlyMemory<byte> Bytes = default,
        PrepareOutcome Outcome = default,
        ILabSocket? Lab = null,
        LabMessageKind LabKind = default);

    private readonly record struct LabOutgoing(ReadOnlyMemory<byte> Audio, bool EndStream);
}
