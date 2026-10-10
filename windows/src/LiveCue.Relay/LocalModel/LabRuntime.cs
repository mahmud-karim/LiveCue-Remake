using System.Globalization;
using System.Net;
using LiveCue.Relay.Core;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.LocalModel;

/// <summary>
/// The lab as the relay sees it (protocol 9.2): the health probe of protocol 11.2 and the two launcher
/// scripts of protocol 11.5 (W-design 4.10).
///
/// The probe is one <c>GET http://127.0.0.1:&lt;lab port&gt;/health</c> with no proxy and no redirect,
/// limited by C-45 on the relay's clock and by W-06. The scripts run through the process runner: the
/// PowerShell of the system directory, the relay's own script copy as <c>-File</c>, every argument a
/// separate list element, the environment allowlist, the output discarded, the limits of C-46, and outside
/// the kill-on-close job, because they start Docker Desktop and a container that outlive the relay. The
/// working directory is the system directory, so a run never depends on where the application was started.
/// Every failure is reduced to "no lab" or "failed"; nothing a script or the lab says leaves this class.
/// </summary>
internal sealed class LabRuntime : ILabRuntime
{
    /// <summary>
    /// One client for the process: no proxy, no redirect, no cookie, no decompression. It keeps no
    /// connection (every probe closes its own), so relay instances share it without sharing anything else.
    /// The time limit is C-45 on the relay's clock, so the client's own real-time limit is switched off.
    /// </summary>
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly RelayContext _context;
    private readonly Uri _healthUri;

    public LabRuntime(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _healthUri = new Uri(string.Create(
            CultureInfo.InvariantCulture, $"http://127.0.0.1:{context.Options.LabPort}/health"));
    }

    /// <summary>
    /// The lab folder rule the relay applies at use (W-design 4.10): a drive-letter path (<c>X:\…</c>), so
    /// not a UNC path, not a device-namespace path, not relative and not drive-relative. A folder that
    /// fails is "no lab folder", the same answer the hosting library gives when the setting is saved.
    /// Whether the drive is a fixed local volume is the hosting library's check at every relay start.
    /// </summary>
    public static bool IsUsableLabFolder(string? labFolder) =>
        labFolder is { Length: >= 3 }
        && char.IsAsciiLetter(labFolder[0])
        && labFolder[1] == ':'
        && labFolder[2] is '\\' or '/';

    public async Task<LabHealth?> ProbeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var limit = new CancellationTokenSource(ProtocolConstants.LabProbeTimeout, _context.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _healthUri);

            // One connection per probe: the container is replaced by every start, so none is worth keeping.
            request.Headers.ConnectionClose = true;
            using var response = await Client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentLength > ProtocolConstants.MaxProbeBodyBytes)
            {
                return null;
            }

            // One byte more than W-06 is asked for, so that a longer reply is seen and never held.
            var buffer = new byte[ProtocolConstants.MaxProbeBodyBytes + 1];
            var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, linked.Token)
                    .ConfigureAwait(false);
                return read > ProtocolConstants.MaxProbeBodyBytes ? null : LabHealthParser.Parse(buffer.AsMemory(0, read));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Protocol 9.2: any failure of the probe means "no lab".
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    public Task<bool> StartAsync(string model, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);

        // The model reaches a command line: nothing but a member of C-04 ever does, whoever calls.
        return ProtocolConstants.SpeechModels.Contains(model, StringComparer.Ordinal)
            ? RunAsync(LabScripts.StartFileName, ProtocolConstants.LabStartTimeout, model, ct)
            : Task.FromResult(false);
    }

    public Task<bool> StopAsync(CancellationToken ct) =>
        RunAsync(LabScripts.StopFileName, ProtocolConstants.LabStopTimeout, model: null, ct);

    private async Task<bool> RunAsync(string launcher, TimeSpan limit, string? model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var options = _context.Options;
        var labFolder = options.LabFolder;
        if (!IsUsableLabFolder(labFolder) || !LabScripts.TryMakeCurrent(options.DataDirectory, launcher))
        {
            return false;
        }

        var request = new ProcessRequest
        {
            FileName = LabScripts.PowerShellPath,
            Arguments = model is null
                ? LabScripts.StopArguments(options.DataDirectory, options.LabPort)
                : LabScripts.StartArguments(options.DataDirectory, labFolder!, model, options.LabPort),
            WorkingDirectory = Environment.SystemDirectory,
            Environment = EnvironmentPolicy.LabScriptAllowlist,
            StandardInput = null,
            MaxStandardOutputBytes = 0,
            Timeout = limit,
            KillWithApplication = false,
        };

        ProcessResult result;
        try
        {
            result = await _context.Processes.RunAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Protocol 9.2: script failures are reduced to "failed".
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }

        if (result.Outcome == ProcessOutcome.Cancelled)
        {
            ct.ThrowIfCancellationRequested();
        }

        // Exit code 0 is success; anything else, the limit included, is failure. Output was discarded.
        return result is { Outcome: ProcessOutcome.Exited, ExitCode: 0 };
    }
}
