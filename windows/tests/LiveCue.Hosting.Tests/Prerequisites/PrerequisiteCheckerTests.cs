using LiveCue.Hosting.Tests.Support;
using LiveCue.Relay;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Hosting.Tests.Prerequisites;

/// <summary>
/// Checks 1 to 6 of architecture 3.11 (W-design 4.14, test plan 2.3), with the real settings store,
/// Codex locator and sign-in check over a fake file system and a fake process runner, and a scripted
/// Tailscale CLI. Nothing real is looked at or started.
/// </summary>
public sealed class PrerequisiteCheckerTests
{
    private const string DataDirectory = @"C:\LiveCue-tests\data";
    private const string Codex = @"C:\Tools\codex.exe";
    private const string Version = "codex-cli 1.2.3";

    private static readonly PrerequisiteOptions Options = new(DataDirectory);

    private readonly FakeHostFileSystem _files = new FakeHostFileSystem()
        .SetEnvironment("PATH", @"C:\Tools")
        .AddFile(Codex);

    private readonly FakeProcessRunner _processes = new();
    private readonly ScriptedTailscaleCli _tailscale = new();

    /// <summary>What <c>codex login status</c> exits with.</summary>
    private int _loginExitCode;

    public PrerequisiteCheckerTests()
    {
        _processes.Default = request => request.Arguments.SequenceEqual(["--version"])
            ? new ProcessResult(ProcessOutcome.Exited, 0, System.Text.Encoding.UTF8.GetBytes(Version + "\n"))
            : new ProcessResult(ProcessOutcome.Exited, _loginExitCode, ReadOnlyMemory<byte>.Empty);
    }

    [Fact]
    public async Task Six_results_in_order()
    {
        var report = await Checker().CheckAsync(Options, CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 6), report.Results.Select(result => (int)result.Id));
        Assert.Equal(
            [
                new PrerequisiteResult(PrerequisiteId.DataFolder, PrerequisiteState.Passed, null),
                new PrerequisiteResult(PrerequisiteId.CodexFound, PrerequisiteState.Passed, Codex),
                new PrerequisiteResult(PrerequisiteId.CodexSignedIn, PrerequisiteState.Passed, null),
                new PrerequisiteResult(PrerequisiteId.TailscaleInstalled, PrerequisiteState.Passed, null),
                new PrerequisiteResult(PrerequisiteId.TailscaleOnline, PrerequisiteState.Passed, null),
                new PrerequisiteResult(PrerequisiteId.TailscaleDnsName, PrerequisiteState.Passed, ScriptedTailscaleCli.DnsName),
            ],
            report.Results);
        Assert.True(report.Passed);
        Assert.Null(report.FirstFailure);
        Assert.Equal(Codex, report.CodexExecutablePath);
        Assert.Equal(ScriptedTailscaleCli.DnsName, report.DnsName);
        Assert.Equal(Version, report.CodexVersion);
        Assert.Equal(SettingsFileState.Missing, report.SettingsLoad.State);

        // Check 1 created the data folder; nothing else was written, and each question was asked once.
        Assert.True(_files.DirectoryExists(DataDirectory));
        Assert.Equal(0, _files.Writes);
        Assert.Equal(
            ["login status", "--version"],
            _processes.Requests.Select(request => string.Join(' ', request.Arguments)));
        Assert.All(_processes.Requests, request => Assert.Equal(Codex, request.FileName));
        Assert.Equal(1, _tailscale.StatusReads);
        Assert.Equal(0, _tailscale.ServeStatusReads);
        Assert.Empty(_tailscale.Writes);

        // The same six, in the same order, on every other outcome too.
        foreach (var failing in await EveryFailureAsync())
        {
            Assert.Equal(Enumerable.Range(1, 6), failing.Results.Select(result => (int)result.Id));
        }
    }

    [Fact]
    public async Task A_failed_dependency_is_not_checked()
    {
        // 3 needs 2: without a Codex nothing is asked of it.
        var noCodex = await Checker(files: new FakeHostFileSystem()).CheckAsync(Options, CancellationToken.None);
        Assert.Equal(States("PFNPPP"), noCodex.Results.Select(result => result.State));
        Assert.Null(noCodex.CodexExecutablePath);
        Assert.Null(noCodex.CodexVersion);
        Assert.Empty(_processes.Requests);

        // 5 needs 4, and 6 needs 5.
        _tailscale.ExecutablePath = null;
        var statusReads = _tailscale.StatusReads;
        var notInstalled = await Checker().CheckAsync(Options, CancellationToken.None);
        Assert.Equal(States("PPPFNN"), notInstalled.Results.Select(result => result.State));
        Assert.Equal(statusReads, _tailscale.StatusReads);
        Assert.Null(notInstalled.DnsName);

        _tailscale.ExecutablePath = @"C:\Program Files\Tailscale\tailscale.exe";
        _tailscale.Status = ScriptedTailscaleCli.Read(new TailscaleNodeStatus(Online: false, ScriptedTailscaleCli.DnsName));
        var offline = await Checker().CheckAsync(Options, CancellationToken.None);
        Assert.Equal(States("PPPPFN"), offline.Results.Select(result => result.State));
        Assert.Null(offline.DnsName);

        foreach (var failure in (TailscaleReadFailure[])[TailscaleReadFailure.Failed, TailscaleReadFailure.TimedOut, TailscaleReadFailure.Unparseable, TailscaleReadFailure.NotInstalled])
        {
            _tailscale.Status = ScriptedTailscaleCli.Failed<TailscaleNodeStatus>(failure);
            var unread = await Checker().CheckAsync(Options, CancellationToken.None);
            Assert.Equal(States("PPPPFN"), unread.Results.Select(result => result.State));
        }

        _tailscale.Status = ScriptedTailscaleCli.Read(new TailscaleNodeStatus(Online: true, DnsName: null));
        var unnamed = await Checker().CheckAsync(Options, CancellationToken.None);
        Assert.Equal(States("PPPPPF"), unnamed.Results.Select(result => result.State));
        Assert.Equal(PrerequisiteId.TailscaleDnsName, unnamed.FirstFailure!.Id);

        // All need 1: a data folder that cannot be opened leaves the rest unchecked, and nothing is asked.
        _files.CreateDirectoryFails = true;
        var before = (_processes.Requests.Count, _tailscale.StatusReads);
        var noFolder = await Checker().CheckAsync(new PrerequisiteOptions(@"C:\LiveCue-tests\elsewhere"), CancellationToken.None);
        Assert.Equal(States("FNNNNN"), noFolder.Results.Select(result => result.State));
        Assert.Equal(PrerequisiteId.DataFolder, noFolder.FirstFailure!.Id);
        Assert.Equal(SettingsFileState.Missing, noFolder.SettingsLoad.State);
        Assert.True(noFolder.SettingsLoad.Settings.SameValues(RelaySettings.Default));
        Assert.Null(noFolder.CodexExecutablePath);
        Assert.Equal(before, (_processes.Requests.Count, _tailscale.StatusReads));
    }

    [Fact]
    public async Task Independent_failures_are_all_reported()
    {
        // Codex is not signed in and Tailscale is not installed: one run says both.
        _loginExitCode = 1;
        _tailscale.ExecutablePath = null;
        var report = await Checker().CheckAsync(Options, CancellationToken.None);

        Assert.Equal(States("PPFFNN"), report.Results.Select(result => result.State));
        Assert.False(report.Passed);
        Assert.Equal(PrerequisiteId.CodexSignedIn, report.FirstFailure!.Id);

        // The Codex that was found is still reported, with its version, whatever check 3 says.
        Assert.Equal(Codex, report.CodexExecutablePath);
        Assert.Equal(Version, report.CodexVersion);

        // No Codex at all and Tailscale offline: the first failure is the lowest id.
        _tailscale.ExecutablePath = @"C:\Program Files\Tailscale\tailscale.exe";
        _tailscale.Status = ScriptedTailscaleCli.Read(new TailscaleNodeStatus(Online: false, null));
        var both = await Checker(files: new FakeHostFileSystem()).CheckAsync(Options, CancellationToken.None);
        Assert.Equal(States("PFNPFN"), both.Results.Select(result => result.State));
        Assert.Equal(PrerequisiteId.CodexFound, both.FirstFailure!.Id);
    }

    [Fact]
    public async Task Bad_settings_are_not_a_failure()
    {
        var entry = new OwnedMapping(
            ConnectionMode.Private, 11443, "/", "http://127.0.0.1:47841",
            new DateTimeOffset(2026, 10, 10, 8, 15, 30, TimeSpan.Zero));
        var invalid = new InMemorySettingsStore(new RelaySettings { RelayPort = 47831, CodexExecutablePath = @"C:\Gone\codex.exe" })
        {
            State = SettingsFileState.Invalid,
            Problems = [new SettingsProblem(SettingsField.RelayPort, SettingsProblemCode.Forbidden)],
        };

        var report = await Checker(settings: invalid).CheckAsync(Options, CancellationToken.None);

        // Check 1 passes, the defaults apply (so Codex is looked for, not taken from the bad file), and
        // the report carries what the load said.
        Assert.True(report.Passed);
        Assert.Equal(SettingsFileState.Invalid, report.SettingsLoad.State);
        Assert.Equal(new SettingsProblem(SettingsField.RelayPort, SettingsProblemCode.Forbidden), Assert.Single(report.SettingsLoad.Problems));
        Assert.Equal(47841, report.SettingsLoad.Settings.RelayPort);
        Assert.Equal(Codex, report.CodexExecutablePath);

        // The same through the real store: a file that is not JSON, and one that breaks a rule and
        // keeps its valid ledger entry.
        _files.AddFile(DataDirectory + @"\settings.json", "not json");
        var unreadable = await Checker().CheckAsync(Options, CancellationToken.None);
        Assert.True(unreadable.Passed);
        Assert.Equal(SettingsFileState.Unreadable, unreadable.SettingsLoad.State);

        _files.AddFile(
            DataDirectory + @"\settings.json",
            System.Text.Encoding.UTF8.GetString(SettingsJson.Write(RelaySettings.Default with { OwnedMappings = [entry] }))
                .Replace("47841,", "47831,", StringComparison.Ordinal));
        var kept = await Checker().CheckAsync(Options, CancellationToken.None);
        Assert.True(kept.Passed);
        Assert.Equal(SettingsFileState.Invalid, kept.SettingsLoad.State);
        Assert.Equal([entry], kept.SettingsLoad.Settings.OwnedMappings);
    }

    [Fact]
    public async Task The_endpoint_override_skips_tailscale()
    {
        // Nothing of Tailscale would pass, and nothing of it is asked.
        _tailscale.ExecutablePath = null;
        var report = await Checker().CheckAsync(new PrerequisiteOptions(DataDirectory, SkipTailscale: true), CancellationToken.None);

        Assert.Equal(States("PPPNNN"), report.Results.Select(result => result.State));
        Assert.True(report.Passed);
        Assert.Null(report.DnsName);
        Assert.Equal(0, _tailscale.StatusReads);

        // The Codex checks are not skipped with it.
        _loginExitCode = 1;
        var notSignedIn = await Checker().CheckAsync(new PrerequisiteOptions(DataDirectory, SkipTailscale: true), CancellationToken.None);
        Assert.Equal(States("PPFNNN"), notSignedIn.Results.Select(result => result.State));
    }

    [Fact]
    public async Task The_codex_path_comes_from_the_override_then_the_setting_then_discovery()
    {
        var configured = @"C:\Configured\codex.exe";
        var given = @"C:\Given\codex.exe";
        _files.AddFile(configured).AddFile(given);
        var store = new InMemorySettingsStore(new RelaySettings { CodexExecutablePath = configured });

        var fromSetting = await Checker(settings: store).CheckAsync(Options, CancellationToken.None);
        var fromOverride = await Checker(settings: store).CheckAsync(new PrerequisiteOptions(DataDirectory, given), CancellationToken.None);
        var fromDiscovery = await Checker().CheckAsync(Options, CancellationToken.None);

        Assert.Equal(configured, fromSetting.CodexExecutablePath);
        Assert.Equal(given, fromOverride.CodexExecutablePath);
        Assert.Equal(Codex, fromDiscovery.CodexExecutablePath);

        // A path that was given and is missing is a failure, although Codex is on the search path.
        var missing = await Checker().CheckAsync(new PrerequisiteOptions(DataDirectory, @"C:\Gone\codex.exe"), CancellationToken.None);
        Assert.Equal(States("PFNPPP"), missing.Results.Select(result => result.State));
        Assert.Null(missing.CodexExecutablePath);
        Assert.Null(missing.Results[1].Detail);
    }

    [Fact]
    public async Task The_version_is_in_the_report_and_a_changed_version_is_only_a_warning()
    {
        var neverChecked = await Checker().CheckAsync(Options, CancellationToken.None);
        var checkedNow = await Checker(settings: new InMemorySettingsStore(new RelaySettings { CodexContainmentCheckedVersion = Version }))
            .CheckAsync(Options, CancellationToken.None);
        var checkedBefore = await Checker(settings: new InMemorySettingsStore(new RelaySettings { CodexContainmentCheckedVersion = "codex-cli 1.2.2" }))
            .CheckAsync(Options, CancellationToken.None);

        Assert.Equal(Version, neverChecked.CodexVersion);
        Assert.True(neverChecked.ContainmentCheckIsStale);
        Assert.False(checkedNow.ContainmentCheckIsStale);
        Assert.True(checkedBefore.ContainmentCheckIsStale);

        // A warning never makes a check fail.
        Assert.All((PrerequisiteReport[])[neverChecked, checkedNow, checkedBefore], report => Assert.True(report.Passed));

        // A version that cannot be read is "unknown", which is stale as well, and still no failure.
        _processes.Default = _ => new ProcessResult(ProcessOutcome.Exited, 0, ReadOnlyMemory<byte>.Empty);
        var unknown = await Checker(settings: new InMemorySettingsStore(new RelaySettings { CodexContainmentCheckedVersion = Version }))
            .CheckAsync(Options, CancellationToken.None);
        Assert.Null(unknown.CodexVersion);
        Assert.True(unknown.ContainmentCheckIsStale);
        Assert.True(unknown.Passed);
    }

    [Fact]
    public async Task A_cancelled_check_ends_with_cancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Checker().CheckAsync(Options, cancelled.Token));

        using var later = new CancellationTokenSource();
        _processes.Gate = new TaskCompletionSource();
        var running = Checker().CheckAsync(Options, later.Token);
        await later.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    /// <summary>P passed, F failed, N not checked: one letter per check, in id order.</summary>
    private static IEnumerable<PrerequisiteState> States(string letters) => letters.Select(letter => letter switch
    {
        'P' => PrerequisiteState.Passed,
        'F' => PrerequisiteState.Failed,
        _ => PrerequisiteState.NotChecked,
    });

    private PrerequisiteChecker Checker(ISettingsStore? settings = null, FakeHostFileSystem? files = null)
    {
        files ??= _files;
        return new PrerequisiteChecker(
            settings ?? new SettingsStore(DataDirectory, files, new SettingsValidator(files)),
            new CodexLocator(files),
            new CodexSignInCheck(_processes),
            _tailscale,
            files);
    }

    /// <summary>One report for each way a run can fail.</summary>
    private async Task<List<PrerequisiteReport>> EveryFailureAsync()
    {
        var reports = new List<PrerequisiteReport>
        {
            await Checker(files: new FakeHostFileSystem { CreateDirectoryFails = true }).CheckAsync(Options, CancellationToken.None),
            await Checker(files: new FakeHostFileSystem()).CheckAsync(Options, CancellationToken.None),
            await Checker().CheckAsync(new PrerequisiteOptions(DataDirectory, SkipTailscale: true), CancellationToken.None),
        };

        var tailscale = new ScriptedTailscaleCli { ExecutablePath = null };
        reports.Add(await new PrerequisiteChecker(
            new InMemorySettingsStore(), new CodexLocator(_files), new CodexSignInCheck(_processes), tailscale, _files)
            .CheckAsync(Options, CancellationToken.None));
        return reports;
    }
}
