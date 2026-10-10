using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Processes;

/// <summary>
/// The production runner (W-design 4.13). For every process: no shell, no window, the arguments as a
/// list, all three streams redirected, the environment cleared and refilled by policy, standard error
/// read and discarded, a time limit on the runner's clock, and a kill on time-out, cancellation, output
/// overflow and dispose.
///
/// A process started with <see cref="ProcessRequest.KillWithApplication"/> is placed in one job object
/// that this runner holds for its whole life. The job has the "kill on job close" limit, so such a
/// process cannot outlive the application, and ending it kills its whole process tree. Without the
/// flag the process is not in the job and ending it kills that one process only.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private const int BufferBytes = 16 * 1024;

    private static readonly UTF8Encoding Utf8NoMark = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeProvider _clock;
    private readonly Lock _jobGate = new();
    private JobObject? _job;

    public ProcessRunner(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        Validate(request);
        if (ct.IsCancellationRequested)
        {
            return Ended(ProcessOutcome.Cancelled);
        }

        using var process = TryStart(request);
        if (process is null)
        {
            return Ended(ProcessOutcome.StartFailed);
        }

        var wholeTree = request.KillWithApplication;
        using var limit = CreateLimit(request.Timeout);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);

        // Each stream has its own thread: a child that fills one pipe while the relay waits on another
        // would otherwise stop for ever.
        var input = process.StandardInput;
        var output = process.StandardOutput.BaseStream;
        var errors = process.StandardError.BaseStream;
        _ = OnOwnThread(() => WriteInput(input, request.StandardInput));
        _ = OnOwnThread(() => Discard(errors));
        var capture = request.MaxStandardOutputBytes > 0
            ? OnOwnThread(() => Capture(output, request.MaxStandardOutputBytes))
            : null;
        if (capture is null)
        {
            _ = OnOwnThread(() => Discard(output));
        }

        try
        {
            var captured = default(Captured);
            if (capture is not null)
            {
                // The capture ends at the end of the output, or one byte beyond the limit.
                captured = await capture.WaitAsync(stop.Token).ConfigureAwait(false);
                if (captured.Overflowed)
                {
                    await EndAsync(process, wholeTree).ConfigureAwait(false);
                    return Ended(ProcessOutcome.OutputLimitExceeded);
                }
            }

            await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
            return new ProcessResult(ProcessOutcome.Exited, process.ExitCode, captured.Bytes);
        }
        catch (OperationCanceledException)
        {
            // The tree is dead (or abandoned after W-08) before the caller hears of it.
            await EndAsync(process, wholeTree).ConfigureAwait(false);
            return Ended(ct.IsCancellationRequested ? ProcessOutcome.Cancelled : ProcessOutcome.TimedOut);
        }
    }

    public Task<IProcessSession> StartSessionAsync(ProcessRequest request, CancellationToken ct)
    {
        Validate(request);
        ct.ThrowIfCancellationRequested();

        var process = TryStart(request) ?? throw new ProcessFailedException(ProcessOutcome.StartFailed);
        return Task.FromResult<IProcessSession>(new Session(this, process, request));
    }

    /// <summary>An absolute path of an .exe file, and nothing else, may ever be started.</summary>
    private static void Validate(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.FileName)
            || !Path.IsPathFullyQualified(request.FileName)
            || !string.Equals(Path.GetExtension(request.FileName), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The file to run must be the absolute path of an .exe file.", nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.Arguments);
        foreach (var argument in request.Arguments)
        {
            if (argument is null)
            {
                throw new ArgumentException("An argument is missing.", nameof(request));
            }
        }

        if (request.MaxStandardOutputBytes < 0)
        {
            throw new ArgumentException("The output limit cannot be negative.", nameof(request));
        }

        if (request.Timeout <= TimeSpan.Zero && request.Timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException("The time limit must be positive.", nameof(request));
        }
    }

    private static ProcessResult Ended(ProcessOutcome outcome) => new(outcome, ExitCode: -1, StandardOutput: default);

    private static Task<T> OnOwnThread<T>(Func<T> work) => Task.Factory.StartNew(
        work,
        CancellationToken.None,
        TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
        TaskScheduler.Default);

    private static Task OnOwnThread(Action work) => Task.Factory.StartNew(
        work,
        CancellationToken.None,
        TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
        TaskScheduler.Default);

    /// <summary>Writes the text as UTF-8 without a byte-order mark, then closes the pipe.</summary>
    private static void WriteInput(StreamWriter writer, string? text)
    {
        try
        {
            if (!string.IsNullOrEmpty(text))
            {
                var stream = writer.BaseStream;
                stream.Write(Utf8NoMark.GetBytes(text));
                stream.Flush();
            }

            writer.Dispose();
        }
        catch (Exception exception) when (IsPipeEnding(exception))
        {
            // The child ended, or was ended, before it had read everything.
        }
    }

    /// <summary>Reads to the end and keeps nothing.</summary>
    private static void Discard(Stream stream)
    {
        try
        {
            var buffer = new byte[BufferBytes];
            while (stream.Read(buffer, 0, buffer.Length) > 0)
            {
            }
        }
        catch (Exception exception) when (IsPipeEnding(exception))
        {
        }
    }

    /// <summary>Reads to the end, or stops at the first byte beyond <paramref name="limit"/>.</summary>
    private static Captured Capture(Stream stream, int limit)
    {
        using var kept = new MemoryStream();
        try
        {
            var buffer = new byte[BufferBytes];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (kept.Length + read > limit)
                {
                    return new Captured(default, Overflowed: true);
                }

                kept.Write(buffer, 0, read);
            }
        }
        catch (Exception exception) when (IsPipeEnding(exception))
        {
            // What arrived before the pipe ended is what there is.
        }

        return new Captured(kept.ToArray(), Overflowed: false);
    }

    private static bool IsPipeEnding(Exception exception) =>
        exception is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException;

    /// <summary>The time limit as a timer of the runner's clock.</summary>
    private CancellationTokenSource CreateLimit(TimeSpan timeout) =>
        timeout == Timeout.InfiniteTimeSpan ? new CancellationTokenSource() : new CancellationTokenSource(timeout, _clock);

    private JobObject? Job()
    {
        lock (_jobGate)
        {
            return _job ??= JobObject.TryCreate();
        }
    }

    /// <summary>Starts the process, or returns null. The reason is never kept: it could hold a path.</summary>
    private Process? TryStart(ProcessRequest request)
    {
        var start = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoMark,
        };
        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            start.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        ChildEnvironment.Fill(start.Environment, request.Environment);

        Process? process = null;
        try
        {
            process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            if (request.KillWithApplication && !IsInJob(process))
            {
                // A process that cannot be tied to the application is not allowed to run.
                Kill(process, wholeTree: true);
                process.Dispose();
                return null;
            }

            return process;
        }
        catch (Exception exception) when (exception
            is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            process?.Dispose();
            return null;
        }
    }

    /// <summary>True when the process is in the job, or has already ended by itself.</summary>
    private bool IsInJob(Process process) => (Job()?.TryAssign(process) ?? false) || process.HasExited;

    private static void Kill(Process process, bool wholeTree)
    {
        try
        {
            process.Kill(entireProcessTree: wholeTree);
        }
        catch (Exception exception) when (exception
            is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            // It has ended already, or a part of its tree could not be reached.
        }
    }

    /// <summary>Ends the process and waits for it, for W-08 at the longest; then it is abandoned.</summary>
    private async Task EndAsync(Process process, bool wholeTree)
    {
        Kill(process, wholeTree);
        using var patience = new CancellationTokenSource(ProtocolConstants.KilledProcessWait, _clock);
        try
        {
            await process.WaitForExitAsync(patience.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
        }
    }

    private readonly record struct Captured(ReadOnlyMemory<byte> Bytes, bool Overflowed);

    /// <summary>A line exchange with a running process (the Codex catalogue exchange).</summary>
    private sealed class Session : IProcessSession
    {
        private readonly ProcessRunner _runner;
        private readonly Process _process;
        private readonly StreamWriter _input;
        private readonly bool _wholeTree;
        private readonly int _outputLimit;
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });

        private readonly CancellationTokenSource _limit;
        private readonly CancellationTokenRegistration _onLimit;
        private int _failure = -1;
        private int _disposed;

        public Session(ProcessRunner runner, Process process, ProcessRequest request)
        {
            _runner = runner;
            _process = process;
            _input = process.StandardInput;
            _wholeTree = request.KillWithApplication;
            _outputLimit = request.MaxStandardOutputBytes;

            var output = process.StandardOutput.BaseStream;
            var errors = process.StandardError.BaseStream;
            _ = OnOwnThread(() => Discard(errors));
            _ = OnOwnThread(() => ReadLines(output));

            _limit = runner.CreateLimit(request.Timeout);
            _onLimit = _limit.Token.Register(() => Fail(ProcessOutcome.TimedOut));
        }

        public async ValueTask WriteLineAsync(string line, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(line);
            ct.ThrowIfCancellationRequested();
            try
            {
                var stream = _input.BaseStream;
                await stream.WriteAsync(Utf8NoMark.GetBytes(line + "\n"), ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The process is gone: by the limit, by overflow, or by itself.
                var failure = Volatile.Read(ref _failure);
                throw new ProcessFailedException(failure < 0 ? ProcessOutcome.Exited : (ProcessOutcome)failure);
            }
        }

        public async ValueTask<string?> ReadLineAsync(CancellationToken ct)
        {
            while (true)
            {
                if (_lines.Reader.TryRead(out var line))
                {
                    return line;
                }

                // False at the end of the output. A session that failed throws its
                // ProcessFailedException here, after the lines that arrived before the failure.
                if (!await _lines.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    return null;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await _onLimit.DisposeAsync().ConfigureAwait(false);
            try
            {
                _input.Dispose();
            }
            catch (Exception exception) when (IsPipeEnding(exception))
            {
            }

            await _runner.EndAsync(_process, _wholeTree).ConfigureAwait(false);
            _lines.Writer.TryComplete();
            _limit.Dispose();
            _process.Dispose();
        }

        /// <summary>The session ends with <paramref name="outcome"/>: the process is killed at once.</summary>
        private void Fail(ProcessOutcome outcome)
        {
            if (Interlocked.CompareExchange(ref _failure, (int)outcome, -1) != -1)
            {
                return;
            }

            _lines.Writer.TryComplete(new ProcessFailedException(outcome));
            Kill(_process, _wholeTree);
        }

        /// <summary>Splits standard output into lines, counting every byte against the limit.</summary>
        private void ReadLines(Stream stream)
        {
            var pending = new MemoryStream();
            try
            {
                var buffer = new byte[BufferBytes];
                long total = 0;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > _outputLimit)
                    {
                        if (_outputLimit > 0)
                        {
                            Fail(ProcessOutcome.OutputLimitExceeded);
                            return;
                        }

                        // A limit of zero discards the output.
                        continue;
                    }

                    var start = 0;
                    for (var index = 0; index < read; index++)
                    {
                        if (buffer[index] != (byte)'\n')
                        {
                            continue;
                        }

                        pending.Write(buffer, start, index - start);
                        Publish(pending);
                        start = index + 1;
                    }

                    pending.Write(buffer, start, read - start);
                }

                if (pending.Length > 0)
                {
                    Publish(pending);
                }
            }
            catch (Exception exception) when (IsPipeEnding(exception))
            {
            }
            finally
            {
                pending.Dispose();
                _lines.Writer.TryComplete();
            }
        }

        private void Publish(MemoryStream pending)
        {
            var bytes = pending.GetBuffer().AsSpan(0, (int)pending.Length);
            if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
            {
                bytes = bytes[..^1];
            }

            _lines.Writer.TryWrite(Encoding.UTF8.GetString(bytes));
            pending.SetLength(0);
        }
    }
}
