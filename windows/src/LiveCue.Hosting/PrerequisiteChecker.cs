using System.Security;

namespace LiveCue.Hosting;

/// <summary>
/// Checks 1 to 6 of architecture 3.11, in order, each at most once (W-design 4.14):
///
/// <code>
/// 1 DataFolder          the folder exists or was created; settings.json is then loaded
/// 2 CodexFound          the locator has no problem                         detail: the path
/// 3 CodexSignedIn       "codex login status" exits with 0
/// 4 TailscaleInstalled  the CLI has an executable path
/// 5 TailscaleOnline     the status was read and says online
/// 6 TailscaleDnsName    the status carries this PC's name                  detail: the name
/// </code>
///
/// Every check needs 1; 3 needs 2; 5 needs 4; 6 needs 5. A check whose dependency failed is not
/// checked. The Codex checks and the Tailscale checks do not depend on each other, so one run reports a
/// failure of each. A settings file that is invalid or unreadable is not a failure: the defaults apply
/// and the report carries the load result. When Codex was found its version is read once, whatever
/// check 3 says. The work runs on the thread pool.
/// </summary>
internal sealed class PrerequisiteChecker : IPrerequisiteChecker
{
    private readonly ISettingsStore _settings;
    private readonly ICodexLocator _codex;
    private readonly ICodexSignInCheck _signIn;
    private readonly ITailscaleCli _tailscale;
    private readonly IHostFileSystem _files;

    public PrerequisiteChecker(
        ISettingsStore settings,
        ICodexLocator codex,
        ICodexSignInCheck signIn,
        ITailscaleCli tailscale,
        IHostFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(codex);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(tailscale);
        ArgumentNullException.ThrowIfNull(files);
        _settings = settings;
        _codex = codex;
        _signIn = signIn;
        _tailscale = tailscale;
        _files = files;
    }

    public Task<PrerequisiteReport> CheckAsync(PrerequisiteOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Task.Run(() => CheckCoreAsync(options, ct), ct);
    }

    private static PrerequisiteResult Result(PrerequisiteId id, bool? passed, string? detail = null) => new(
        id,
        passed switch
        {
            true => PrerequisiteState.Passed,
            false => PrerequisiteState.Failed,
            null => PrerequisiteState.NotChecked,
        },
        passed == true ? detail : null);

    private async Task<PrerequisiteReport> CheckCoreAsync(PrerequisiteOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Check 1. Nothing else is looked at when the data folder cannot be opened.
        if (!EnsureDataFolder(options.DataDirectory))
        {
            return new PrerequisiteReport(
                [
                    Result(PrerequisiteId.DataFolder, false),
                    Result(PrerequisiteId.CodexFound, null),
                    Result(PrerequisiteId.CodexSignedIn, null),
                    Result(PrerequisiteId.TailscaleInstalled, null),
                    Result(PrerequisiteId.TailscaleOnline, null),
                    Result(PrerequisiteId.TailscaleDnsName, null),
                ],
                new SettingsLoadResult(RelaySettings.Default, SettingsFileState.Missing, []),
                CodexExecutablePath: null,
                DnsName: null);
        }

        var load = _settings.Load();

        // Checks 2 and 3, and the version of the Codex that was found.
        var location = _codex.Locate(options.CodexPathOverride ?? load.Settings.CodexExecutablePath);
        var codexPath = location.Problem == CodexProblem.None ? location.Path : null;
        bool? signedIn = null;
        string? codexVersion = null;
        if (codexPath is not null)
        {
            signedIn = await _signIn.IsSignedInAsync(codexPath, ct).ConfigureAwait(false);
            codexVersion = await _signIn.ReadVersionAsync(codexPath, ct).ConfigureAwait(false);
        }

        // Checks 4 to 6, unless an endpoint was given for this run.
        bool? installed = null;
        bool? online = null;
        bool? named = null;
        string? dnsName = null;
        if (!options.SkipTailscale)
        {
            ct.ThrowIfCancellationRequested();
            installed = _tailscale.ExecutablePath is not null;
            if (installed == true)
            {
                var status = await _tailscale.ReadStatusAsync(ct).ConfigureAwait(false);
                online = status is { Failure: TailscaleReadFailure.None, Value.Online: true };
                if (online == true)
                {
                    dnsName = status.Value!.DnsName;
                    named = dnsName is not null;
                }
            }
        }

        ct.ThrowIfCancellationRequested();
        return new PrerequisiteReport(
            [
                Result(PrerequisiteId.DataFolder, true),
                Result(PrerequisiteId.CodexFound, codexPath is not null, codexPath),
                Result(PrerequisiteId.CodexSignedIn, signedIn),
                Result(PrerequisiteId.TailscaleInstalled, installed),
                Result(PrerequisiteId.TailscaleOnline, online),
                Result(PrerequisiteId.TailscaleDnsName, named, dnsName),
            ],
            load,
            codexPath,
            dnsName,
            codexVersion);
    }

    /// <summary>True when the folder exists afterwards. Creating it is the only write of the checks.</summary>
    private bool EnsureDataFolder(string dataDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                return false;
            }

            if (!_files.DirectoryExists(dataDirectory))
            {
                _files.CreateDirectory(dataDirectory);
            }

            return _files.DirectoryExists(dataDirectory);
        }
        catch (Exception exception) when (exception
            is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
