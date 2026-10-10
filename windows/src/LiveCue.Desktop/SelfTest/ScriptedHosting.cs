using System.IO;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.Relay.Processes;

namespace LiveCue.Desktop.SelfTest;

/// <summary>
/// The hosting library as the self-test composes it (W-design 5.13, "Isolation, by construction"): the
/// real supervisor over scripted parts. The relay is a <see cref="ScriptedRelay"/>; the settings are in
/// memory; the prerequisite checks and the mapping manager answer from the script; the process runner
/// and the file system record what is asked of them, start nothing and write nothing.
/// </summary>
internal sealed class ScriptedHosting
{
    public const string DnsName = "pc.example.test";

    /// <summary>A path handed to the supervisor as the data folder. Nothing is ever created there.</summary>
    public static readonly string UnusedDataDirectory =
        Path.Combine(Path.GetTempPath(), "LiveCue-self-test-data-folder-that-must-never-exist");

    public ScriptedHosting(TimeProvider clock, RelayScript relays)
    {
        Mappings = new ScriptedMappings(clock);
        Services = HostingFactory.Create(
            new SupervisorOptions(UnusedDataDirectory, "1.0.0"),
            new HostingOverrides
            {
                Clock = clock,
                Processes = Processes,
                Files = Files,
                Settings = new MemorySettings(),
                Prerequisites = new PassingPrerequisites(),
                Mappings = Mappings,
                RelayFactory = relays,
            });
    }

    public HostingServices Services { get; }

    public RecordingProcesses Processes { get; } = new();

    public RecordingFiles Files { get; } = new();

    public ScriptedMappings Mappings { get; }

    /// <summary>Settings that live for one run and never reach a disk.</summary>
    private sealed class MemorySettings : ISettingsStore
    {
        private RelaySettings _settings = RelaySettings.Default;

        public SettingsLoadResult Load() => new(_settings, SettingsFileState.Missing, []);

        public void Save(RelaySettings settings) => _settings = settings;
    }

    /// <summary>Every check passes, with a Tailscale name on a reserved test domain.</summary>
    private sealed class PassingPrerequisites : IPrerequisiteChecker
    {
        public Task<PrerequisiteReport> CheckAsync(PrerequisiteOptions options, CancellationToken ct)
        {
            PrerequisiteResult[] results =
            [
                new(PrerequisiteId.DataFolder, PrerequisiteState.Passed, null),
                new(PrerequisiteId.CodexFound, PrerequisiteState.Passed, null),
                new(PrerequisiteId.CodexSignedIn, PrerequisiteState.Passed, null),
                new(PrerequisiteId.TailscaleInstalled, PrerequisiteState.Passed, null),
                new(PrerequisiteId.TailscaleOnline, PrerequisiteState.Passed, null),
                new(PrerequisiteId.TailscaleDnsName, PrerequisiteState.Passed, DnsName),
            ];
            return Task.FromResult(new PrerequisiteReport(
                results,
                new SettingsLoadResult(RelaySettings.Default, SettingsFileState.Missing, []),
                CodexExecutablePath: null,
                DnsName));
        }
    }
}

/// <summary>
/// A mapping manager that reads no Tailscale configuration and runs no command. It reports the state
/// the script chose for both modes and counts the executions asked of it, which must stay zero.
/// </summary>
internal sealed class ScriptedMappings(TimeProvider clock) : IMappingManager
{
    /// <summary>What an inspection reports for the selected mode.</summary>
    public MappingState State { get; set; } = MappingState.Present;

    public MappingRefusal BlockedBy { get; set; } = MappingRefusal.None;

    /// <summary>How often a write was asked for. The self-test never confirms a mapping dialog.</summary>
    public int Executions { get; private set; }

    public Task<ConnectionInspection> InspectAsync(RelaySettings settings, CancellationToken ct) => Task.FromResult(
        new ConnectionInspection(
            new MappingInspection(ConnectionMode.Private, State, BlockedBy, State == MappingState.Present, settings.PrivateHttpsPort, "/"),
            new MappingInspection(ConnectionMode.Public, State, BlockedBy, State == MappingState.Present, settings.PublicHttpsPort, settings.PublicPathPrefix),
            StaleOwned: [],
            TailscaleReadFailure.None,
            clock.GetUtcNow(),
            Exposure: []));

    public MappingPlan? PlanApply(ConnectionMode mode, RelaySettings settings, string? dnsName, out MappingRefusal refusal)
    {
        refusal = MappingRefusal.None;
        var isPublic = mode == ConnectionMode.Public;
        var action = isPublic ? MappingAction.ApplyPublic : MappingAction.ApplyPrivate;
        var command = isPublic
            ? MappingCommand.ApplyPublic(settings.PublicHttpsPort, settings.PublicPathPrefix, settings.RelayPort)
            : MappingCommand.ApplyPrivate(settings.PrivateHttpsPort, settings.RelayPort);
        return new MappingPlan(action, command, MappingTexts.Effect(action, null, settings.RelayPort), Entry: null);
    }

    public MappingPlan PlanRemoval(OwnedMapping entry, string? dnsName)
    {
        var isPublic = entry.Mode == ConnectionMode.Public;
        var action = isPublic ? MappingAction.RemovePublic : MappingAction.RemovePrivate;
        var command = isPublic
            ? MappingCommand.RemovePublic(entry.HttpsPort, entry.Path)
            : MappingCommand.RemovePrivate(entry.HttpsPort);
        return new MappingPlan(action, command, MappingTexts.Effect(action, null, 0), entry);
    }

    public Task<MappingResult> ExecuteAsync(MappingPlan plan, CancellationToken ct)
    {
        Executions++;
        return Task.FromResult(new MappingResult(
            MappingOutcome.Refused, MappingRefusal.TailscaleNotInstalled, OthersChanged: false, RelaySettings.Default, After: null));
    }
}

/// <summary>A process runner that starts nothing and remembers that it was asked.</summary>
internal sealed class RecordingProcesses : IProcessRunner
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new ProcessResult(ProcessOutcome.StartFailed, -1, default));
    }

    public Task<IProcessSession> StartSessionAsync(ProcessRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        throw new ProcessFailedException(ProcessOutcome.StartFailed);
    }
}

/// <summary>
/// A file system in which nothing exists. Questions ("does this exist?") are answered "no" and counted
/// as probes; anything that would read content or change the disk is counted as a touch and refused.
/// </summary>
internal sealed class RecordingFiles : IHostFileSystem
{
    private int _probes;
    private int _touches;

    /// <summary>Existence questions asked. Harmless: nothing was read.</summary>
    public int Probes => Volatile.Read(ref _probes);

    /// <summary>Reads of content and writes asked for. Must stay zero.</summary>
    public int Touches => Volatile.Read(ref _touches);

    public string BaseDirectory => ScriptedHosting.UnusedDataDirectory;

    public string ProgramFilesDirectory => ScriptedHosting.UnusedDataDirectory;

    public bool IsOnFixedLocalDrive(string path)
    {
        Interlocked.Increment(ref _probes);
        return false;
    }

    public string? GetEnvironmentVariable(string name)
    {
        Interlocked.Increment(ref _probes);
        return null;
    }

    public bool FileExists(string path)
    {
        Interlocked.Increment(ref _probes);
        return false;
    }

    public bool DirectoryExists(string path)
    {
        Interlocked.Increment(ref _probes);
        return false;
    }

    public void CreateDirectory(string path) => Refuse();

    public byte[] ReadAllBytes(string path)
    {
        Refuse();
        return [];
    }

    public void WriteAllBytesAtomic(string path, byte[] contents) => Refuse();

    public IReadOnlyList<FoundFile> FindFiles(string directory, string fileName, int maxDepth)
    {
        Interlocked.Increment(ref _probes);
        return [];
    }

    private void Refuse()
    {
        Interlocked.Increment(ref _touches);
        throw new IOException("The self-test does not touch the disk.");
    }
}

/// <summary>A clipboard that only remembers what was put on it.</summary>
internal sealed class RecordingClipboard : IClipboard
{
    public List<string> Texts { get; } = [];

    public bool TrySetText(string text)
    {
        Texts.Add(text);
        return true;
    }
}

/// <summary>A launcher that opens nothing and remembers what it was asked to open.</summary>
internal sealed class RecordingLauncher : IExternalLauncher
{
    public List<string> Opened { get; } = [];

    public bool OpenUrl(Uri url)
    {
        Opened.Add(url?.ToString() ?? string.Empty);
        return true;
    }

    public bool OpenFile(string path)
    {
        Opened.Add(path);
        return true;
    }
}
