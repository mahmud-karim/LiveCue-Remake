using System.Reflection;
using System.Threading.Channels;
using LiveCue.Hosting;

namespace LiveCue.Relay.Host;

/// <summary>
/// LiveCueRelayHost.exe without its process: the relay supervisor without a window (architecture 3.14).
/// Tests call <see cref="RunAsync"/> with fake hosting services.
///
/// Exit codes: 0 clean, 2 prerequisite or usage failure, 3 relay start fault.
///
/// First version (foundation): arguments, <c>--check-only</c>, start, text events and a clean stop.
/// <c>--show-pairing</c> runs its whole path, but the relay library has no stored pairing to read yet,
/// so it reports that none exists; the terminal QR and JSON events are work package WP-W06.
/// </summary>
internal static class HostProgram
{
    public const int ExitClean = 0;
    public const int ExitPrerequisite = 2;
    public const int ExitStartFault = 3;

    public const string NoPairingText = "No pairing has been created yet. Start LiveCue once, then run this again.";
    public const string PairingUnreadableText = "The saved pairing could not be read. Reset pairing to create a new one.";
    public const string NoAddressText = "The PC address could not be resolved.";

    /// <summary>The version of this executable ("1.0.0"), which the relay reports to phones.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The production services for one run.</summary>
    public static HostingServices CreateServices(HostArguments arguments) => HostingFactory.Create(
        new SupervisorOptions(DataFolder.Resolve(arguments.DataDirectory), Version, arguments.Overrides));

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        Func<HostArguments, HostingServices> services,
        TextWriter output,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);

        if (!HostArguments.TryParse(args, out var arguments))
        {
            await output.WriteLineAsync(HostArguments.Usage).ConfigureAwait(false);
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
                    return await ShowPairingAsync(arguments, hosting, output, cancel).ConfigureAwait(false);
                }

                if (arguments.CheckOnly)
                {
                    var report = await supervisor.CheckAsync(cancel).ConfigureAwait(false);
                    await output.WriteLineAsync(report.FirstFailure is { } failed
                        ? PrerequisiteTexts.Failure(failed.Id)
                        : PrerequisiteTexts.Passed).ConfigureAwait(false);
                    return report.Passed ? ExitClean : ExitPrerequisite;
                }

                return await RunRelayAsync(supervisor, output, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return ExitClean;
            }
        }
    }

    private static async Task<int> RunRelayAsync(IRelaySupervisor supervisor, TextWriter output, CancellationToken cancel)
    {
        // The printer is the only writer while the relay runs; it ends when the supervisor is disposed.
        using var stopPrinting = new CancellationTokenSource();
        var printing = Task.Run(() => PrintAsync(supervisor.Events, output, stopPrinting.Token), CancellationToken.None);

        var result = await supervisor.StartAsync(cancel).ConfigureAwait(false);
        if (result.Outcome != StartOutcome.Started)
        {
            await stopPrinting.CancelAsync().ConfigureAwait(false);
            await printing.ConfigureAwait(false);
            await output.WriteLineAsync(result.FailureText ?? "Could not start the relay.").ConfigureAwait(false);
            return result.Outcome == StartOutcome.PrerequisiteFailed ? ExitPrerequisite : ExitStartFault;
        }

        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancel.Register(() => stopRequested.TrySetResult()))
        {
            await stopRequested.Task.ConfigureAwait(false);
        }

        await supervisor.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await stopPrinting.CancelAsync().ConfigureAwait(false);
        await printing.ConfigureAwait(false);
        return ExitClean;
    }

    /// <summary>
    /// <c>--show-pairing</c>: no relay is started and no port is bound. The endpoint is resolved the way a
    /// start resolves it (the override, otherwise the settings and this PC's Tailscale name), and the
    /// stored pairing is read through <see cref="StoredPairingExport"/>. This is the only place the token
    /// is ever printed, and only on this explicit request.
    /// </summary>
    private static async Task<int> ShowPairingAsync(
        HostArguments arguments, HostingServices hosting, TextWriter output, CancellationToken cancel)
    {
        var dataDirectory = DataFolder.Resolve(arguments.DataDirectory);
        var report = await hosting.Prerequisites.CheckAsync(
            new PrerequisiteOptions(dataDirectory, arguments.Overrides.CodexPath, SkipTailscale: arguments.Overrides.Endpoint is not null),
            cancel).ConfigureAwait(false);

        var settings = report.SettingsLoad.Settings;
        var mode = arguments.Overrides.Mode ?? settings.ConnectionMode;
        var endpoint = arguments.Overrides.Endpoint
            ?? (report.DnsName is { } dnsName
                ? hosting.Endpoints.Resolve(settings with { ConnectionMode = mode }, dnsName)
                : null);
        if (endpoint is null)
        {
            // Codex is not needed to show a pairing; only the folder and the Tailscale name are.
            var blocking = report.Results.FirstOrDefault(result =>
                result.State == PrerequisiteState.Failed
                && result.Id is not (PrerequisiteId.CodexFound or PrerequisiteId.CodexSignedIn));
            await output.WriteLineAsync(blocking is null ? NoAddressText : PrerequisiteTexts.Failure(blocking.Id))
                .ConfigureAwait(false);
            return ExitPrerequisite;
        }

        string? payload;
        try
        {
            payload = await StoredPairingExport.ReadPayloadAsync(dataDirectory, endpoint, cancel).ConfigureAwait(false);
        }
        catch (IOException)
        {
            await output.WriteLineAsync(PairingUnreadableText).ConfigureAwait(false);
            return ExitPrerequisite;
        }

        if (payload is null)
        {
            await output.WriteLineAsync(NoPairingText).ConfigureAwait(false);
            return ExitPrerequisite;
        }

        await output.WriteLineAsync(payload).ConfigureAwait(false);
        return ExitClean;
    }

    private static async Task PrintAsync(ChannelReader<HostEvent> events, TextWriter output, CancellationToken stop)
    {
        try
        {
            while (await events.WaitToReadAsync(stop).ConfigureAwait(false))
            {
                Drain(events, output);
            }
        }
        catch (OperationCanceledException)
        {
            // Nothing more is coming for this run; what is already queued is still printed.
            Drain(events, output);
        }
    }

    private static void Drain(ChannelReader<HostEvent> events, TextWriter output)
    {
        while (events.TryRead(out var hostEvent))
        {
            if (ConsoleEventPrinter.Text(hostEvent) is { } line)
            {
                output.WriteLine(line);
            }
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
}
