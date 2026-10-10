namespace LiveCue.Hosting;

/// <summary>
/// Checks 1 to 6 of architecture 3.11, in order, each at most once (W-design 4.14).
///
/// First version (foundation): check 1 is real (the data folder exists or is created, and the settings
/// are loaded). Checks 2 and 3 pass without looking for Codex. The Tailscale checks are not evaluated
/// when an endpoint override is in force; otherwise check 4 fails, because the foundation's Tailscale
/// CLI reports "not installed", and checks 5 and 6 are not checked. Work package WP-W06 builds the rest.
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
        _ = _codex;
        _ = _signIn;
        return Task.Run(() => Check(options, ct), ct);
    }

    private PrerequisiteReport Check(PrerequisiteOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var folderUsable = EnsureDataFolder(options.DataDirectory);
        var load = folderUsable
            ? _settings.Load()
            : new SettingsLoadResult(RelaySettings.Default, SettingsFileState.Missing, []);
        var codexPath = options.CodexPathOverride ?? load.Settings.CodexExecutablePath;

        var afterFolder = folderUsable ? PrerequisiteState.Passed : PrerequisiteState.NotChecked;
        var tailscaleInstalled = !folderUsable || options.SkipTailscale
            ? PrerequisiteState.NotChecked
            : _tailscale.ExecutablePath is null ? PrerequisiteState.Failed : PrerequisiteState.Passed;

        PrerequisiteResult[] results =
        [
            new(PrerequisiteId.DataFolder, folderUsable ? PrerequisiteState.Passed : PrerequisiteState.Failed, null),
            new(PrerequisiteId.CodexFound, afterFolder, folderUsable ? codexPath : null),
            new(PrerequisiteId.CodexSignedIn, afterFolder, null),
            new(PrerequisiteId.TailscaleInstalled, tailscaleInstalled, null),
            new(PrerequisiteId.TailscaleOnline, PrerequisiteState.NotChecked, null),
            new(PrerequisiteId.TailscaleDnsName, PrerequisiteState.NotChecked, null),
        ];

        return new PrerequisiteReport(results, load, codexPath, DnsName: null);
    }

    private bool EnsureDataFolder(string dataDirectory)
    {
        try
        {
            _files.CreateDirectory(dataDirectory);
            return _files.DirectoryExists(dataDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
