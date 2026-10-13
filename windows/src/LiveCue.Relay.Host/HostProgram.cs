using System.Reflection;
using System.Threading.Channels;
using LiveCue.Hosting;

namespace LiveCue.Relay.Host;

/// <summary>Where the host writes, and whether that is an interactive console.</summary>
/// <param name="Writer">Standard output.</param>
/// <param name="IsRedirected">True when standard output goes to a file, a pipe or a wrapper that keeps
/// it (<c>Console.IsOutputRedirected</c>, read once at start).</param>
internal sealed record HostOutput(TextWriter Writer, bool IsRedirected);

/// <summary>
/// LiveCueRelayHost.exe without its process: the relay supervisor without a window (architecture 3.14,
/// W-design 4.16). Tests call <see cref="RunAsync"/> with fake hosting services.
///
/// <code>
/// (no argument)     the checks, the relay with the saved settings, one line per event, stop on Ctrl+C
/// --check-only      the checks; "LiveCue Relay prerequisites passed." or the failure text
/// --show-pairing    the pairing payload and a terminal QR, once; no relay, no port
/// --events json     one JSON object per line instead of text
/// --include-content with --events json: the lines also carry conversation text
/// --endpoint, --port, --data-dir, --codex, --lab, --mode    this run only
/// </code>
///
/// Exit codes: 0 after a clean stop (and after a passed check or a shown pairing); 2 for a usage error,
/// a failed prerequisite, a start refused by check 6a, and a pairing that cannot be shown; 3 for a relay
/// start fault and for a relay that stopped by itself.
/// </summary>
internal static class HostProgram
{
    public const int ExitClean = 0;
    public const int ExitPrerequisite = 2;
    public const int ExitStartFault = 3;

    public const string NoPairingText = "No pairing has been created yet. Start LiveCue once, then run this again.";
    public const string PairingUnreadableText = "The saved pairing could not be read. Reset pairing to create a new one.";
    public const string NoAddressText = "The PC address could not be resolved.";

    /// <summary>The one line of <c>--show-pairing</c> when its output is not an interactive console.</summary>
    public const string PairingNeedsConsoleText =
        "The pairing is shown only in an interactive console. Run --show-pairing in a terminal window, without redirecting its output.";

    private const string StartFailedText = "Could not start the relay.";
    private const string StoppedByItselfText = "The relay stopped after an internal error.";
    private const string PhoneSeenLine = "iPhone seen.";

    /// <summary>The version of this executable ("1.0.0"), which the relay reports to phones.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The production services for one run.</summary>
    public static HostingServices CreateServices(HostArguments arguments) => HostingFactory.Create(
        new SupervisorOptions(DataFolder.Resolve(arguments.DataDirectory), Version, arguments.Overrides));

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        Func<HostArguments, HostingServices> services,
        HostOutput output,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);
        var writer = output.Writer;

        if (!HostArguments.TryParse(args, out var arguments))
        {
            await writer.WriteLineAsync(HostArguments.Usage).ConfigureAwait(false);
            return ExitPrerequisite;
        }

        // The token of the real data folder is printed to an interactive console only. This is decided
        // before anything is composed or read, so nothing of the pairing can reach a redirected output.
        if (arguments is { ShowPairing: true, DataDirectory: null } && output.IsRedirected)
        {
            await writer.WriteLineAsync(PairingNeedsConsoleText).ConfigureAwait(false);
            return ExitPrerequisite;
        }

        var hosting = services(arguments);
        var supervisor = hosting.Supervisor;
        await using (supervisor.ConfigureAwait(false))
        {
            try
            {
                if (arguments.ShowPairing)
                {
                    return await ShowPairingAsync(arguments, hosting, writer, cancel).ConfigureAwait(false);
                }

                if (arguments.CheckOnly)
                {
                    return await CheckOnlyAsync(supervisor, writer, cancel).ConfigureAwait(false);
                }

                return await RunRelayAsync(arguments, supervisor, writer, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return ExitClean;
            }
        }
    }

    private static string ContainmentWarning(PrerequisiteReport report) => PrerequisiteTexts.ContainmentWarning(
        report.CodexVersion, report.SettingsLoad.Settings.CodexContainmentCheckedVersion);

    private static async Task<int> CheckOnlyAsync(IRelaySupervisor supervisor, TextWriter writer, CancellationToken cancel)
    {
        var report = await supervisor.CheckAsync(cancel).ConfigureAwait(false);
        await writer.WriteLineAsync(report.FirstFailure is { } failed
            ? PrerequisiteTexts.Failure(failed.Id)
            : PrerequisiteTexts.Passed).ConfigureAwait(false);

        // A warning only: it changes no exit code.
        if (report.ContainmentCheckIsStale)
        {
            await writer.WriteLineAsync(ContainmentWarning(report)).ConfigureAwait(false);
        }

        return report.Passed ? ExitClean : ExitPrerequisite;
    }

    private static async Task<int> RunRelayAsync(
        HostArguments arguments, IRelaySupervisor supervisor, TextWriter writer, CancellationToken cancel)
    {
        // The printer is the only writer while the supervisor works; the host writes after it has ended.
        var lines = new EventLines(writer, arguments.JsonEvents, arguments.IncludeContent);
        using var stopPrinting = new CancellationTokenSource();
        var printing = Task.Run(() => PrintAsync(supervisor.Events, lines, stopPrinting.Token), CancellationToken.None);
        async Task EndPrintingAsync()
        {
            await stopPrinting.CancelAsync().ConfigureAwait(false);
            await printing.ConfigureAwait(false);
        }

        StartResult result;
        try
        {
            result = await supervisor.StartAsync(cancel).ConfigureAwait(false);
        }
        catch
        {
            await EndPrintingAsync().ConfigureAwait(false);
            throw;
        }

        if (result.Outcome != StartOutcome.Started)
        {
            await EndPrintingAsync().ConfigureAwait(false);

            // A failed prerequisite and the refusal of check 6a are exit code 2; a relay that could not
            // start is exit code 3.
            var exitCode = result.Outcome is StartOutcome.PrerequisiteFailed or StartOutcome.ExposedInPrivateMode
                ? ExitPrerequisite
                : ExitStartFault;
            lines.Failure(result.FailureText ?? StartFailedText, exitCode);
            return exitCode;
        }

        // Until Ctrl+C, or until the relay stops by itself. It is never restarted here.
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancel.Register(() => stopRequested.TrySetResult()))
        {
            await Task.WhenAny(stopRequested.Task, lines.RelayStoppedByItself).ConfigureAwait(false);
        }

        await supervisor.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await EndPrintingAsync().ConfigureAwait(false);

        if (lines.RelayStoppedByItself.IsCompleted)
        {
            lines.Failure(StoppedByItselfText, ExitStartFault);
            return ExitStartFault;
        }

        return ExitClean;
    }

    /// <summary>
    /// <c>--show-pairing</c>: no relay is started and no port is bound. The checks run (what they say
    /// about Codex is ignored), the endpoint is resolved the way a start resolves it (the override,
    /// otherwise the settings and this PC's Tailscale name), and the stored pairing is read through
    /// <see cref="StoredPairingExport"/>. This is the only place the token is ever printed, and only on
    /// this explicit request.
    /// </summary>
    private static async Task<int> ShowPairingAsync(
        HostArguments arguments, HostingServices hosting, TextWriter writer, CancellationToken cancel)
    {
        var report = await hosting.Supervisor.CheckAsync(cancel).ConfigureAwait(false);

        // Codex is not needed to show a pairing; the data folder and the Tailscale name are.
        var blocking = report.Results.FirstOrDefault(result =>
            result.State == PrerequisiteState.Failed
            && result.Id is not (PrerequisiteId.CodexFound or PrerequisiteId.CodexSignedIn));
        if (blocking is not null)
        {
            await writer.WriteLineAsync(PrerequisiteTexts.Failure(blocking.Id)).ConfigureAwait(false);
            return ExitPrerequisite;
        }

        var settings = report.SettingsLoad.Settings;
        var mode = arguments.Overrides.Mode ?? settings.ConnectionMode;
        var endpoint = arguments.Overrides.Endpoint
            ?? (report.DnsName is { } dnsName
                ? hosting.Endpoints.Resolve(settings with { ConnectionMode = mode }, dnsName)
                : null);
        if (endpoint is null)
        {
            await writer.WriteLineAsync(NoAddressText).ConfigureAwait(false);
            return ExitPrerequisite;
        }

        string? payload;
        try
        {
            payload = await StoredPairingExport
                .ReadPayloadAsync(DataFolder.Resolve(arguments.DataDirectory), endpoint, cancel)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            await writer.WriteLineAsync(PairingUnreadableText).ConfigureAwait(false);
            return ExitPrerequisite;
        }
        catch (ArgumentException)
        {
            await writer.WriteLineAsync(NoAddressText).ConfigureAwait(false);
            return ExitPrerequisite;
        }

        if (payload is null)
        {
            await writer.WriteLineAsync(NoPairingText).ConfigureAwait(false);
            return ExitPrerequisite;
        }

        await writer.WriteLineAsync(payload).ConfigureAwait(false);
        foreach (var line in TerminalQr.Lines(payload))
        {
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }

        return ExitClean;
    }

    private static async Task PrintAsync(ChannelReader<HostEvent> events, EventLines lines, CancellationToken stop)
    {
        try
        {
            while (await events.WaitToReadAsync(stop).ConfigureAwait(false))
            {
                Drain(events, lines);
            }
        }
        catch (OperationCanceledException)
        {
            // Nothing more is coming for this run; what is already queued is still printed.
            Drain(events, lines);
        }
    }

    private static void Drain(ChannelReader<HostEvent> events, EventLines lines)
    {
        while (events.TryRead(out var hostEvent))
        {
            lines.Write(hostEvent);
        }
    }

    private static string ReadVersion()
    {
        var informational = typeof(HostProgram).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0";
        }

        // Drop build metadata ("1.0.0+abc123") if a build ever adds it.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }

    /// <summary>
    /// The lines of one run, in the text or the JSON form. Besides the line of each event it writes the
    /// containment warning once, after the checks, and notices a relay that stopped by itself.
    /// </summary>
    private sealed class EventLines(TextWriter writer, bool json, bool includeContent)
    {
        private readonly TaskCompletionSource _stoppedByItself = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _warned;
        private string? _lastLine;

        /// <summary>Completes when the relay published that it stopped after a fault.</summary>
        public Task RelayStoppedByItself => _stoppedByItself.Task;

        public void Write(HostEvent hostEvent)
        {
            var line = json ? ConsoleEventPrinter.Json(hostEvent, includeContent) : ConsoleEventPrinter.Text(hostEvent);

            // Every request of a phone raises this event. In the text form one line says it until
            // something else happens.
            if (line is not null && !(line == PhoneSeenLine && _lastLine == PhoneSeenLine))
            {
                WriteLine(line);
            }

            switch (hostEvent)
            {
                case PrerequisitesChecked { Report.ContainmentCheckIsStale: true } checkedNow when !_warned:
                    _warned = true;
                    var warning = ContainmentWarning(checkedNow.Report);
                    WriteLine(json ? ConsoleEventPrinter.JsonMessage("Warning", warning) : warning);
                    break;

                case RelayEventForwarded { Event: RelayStopped { Reason: RelayStopReason.Fault } }:
                    _stoppedByItself.TrySetResult();
                    break;

                default:
                    break;
            }
        }

        /// <summary>The reason the host ends with a non-zero exit code. Called after the printer has ended.</summary>
        public void Failure(string text, int exitCode) =>
            WriteLine(json ? ConsoleEventPrinter.JsonMessage("Failure", text, exitCode) : text);

        private void WriteLine(string line)
        {
            _lastLine = line;
            writer.WriteLine(line);
        }
    }
}
