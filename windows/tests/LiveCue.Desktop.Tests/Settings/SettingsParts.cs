using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Settings;
using LiveCue.Desktop.Tests.Live;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;
using LiveCue.Relay;
using LiveCue.TestSupport;

namespace LiveCue.Desktop.Tests.Settings;

/// <summary>
/// The Settings view model over a <see cref="FakeSession"/>, with the real settings rules and the real
/// endpoint resolver over a file system that exists only in the test. Nothing here starts a process:
/// the hosting library is given a process runner that refuses, and no test asks it for anything.
/// </summary>
internal sealed class SettingsParts
{
    public const string DnsName = "pc.example.test";
    public const string Target = "http://127.0.0.1:47841";

    public SettingsParts(RelaySettings? saved = null)
    {
        if (saved is not null)
        {
            Session.Settings = saved;
        }

        Hosting = HostingFactory.Create(
            new SupervisorOptions(@"C:\LiveCue-tests\data", "1.0.0"),
            new HostingOverrides { Files = Files, Processes = Processes });
        Settings = new SettingsViewModel(Session, Dialogs, Clipboard, Toasts, Launcher, Hosting, new ImmediateDispatcher());
    }

    public FakeSession Session { get; } = new();

    public TestFiles Files { get; } = new();

    public RecordingProcesses Processes { get; } = new();

    public HostingServices Hosting { get; }

    public RecordingDialogHost Dialogs { get; } = new();

    public FakeClipboard Clipboard { get; } = new();

    public RecordingToastHost Toasts { get; } = new();

    public FakeLauncher Launcher { get; } = new();

    public SettingsViewModel Settings { get; }

    /// <summary>A check that passed everything: Codex found and signed in, Tailscale online with its name.</summary>
    public void Checked(string? codexVersion = "1.2.3") =>
        Session.Prerequisites = PageParts.Report(new Dictionary<PrerequisiteId, PrerequisiteState>(), codexVersion: codexVersion);

    /// <summary>An inspection for the saved settings: the selected mode's mapping in that state.</summary>
    public ConnectionInspection Inspect(
        MappingState state,
        MappingRefusal blockedBy = MappingRefusal.None,
        TailscaleReadFailure failure = TailscaleReadFailure.None,
        IReadOnlyList<OwnedMapping>? stale = null,
        IReadOnlyList<ExposedHandler>? exposure = null)
    {
        var saved = Session.Settings;
        var isPublic = saved.ConnectionMode == ConnectionMode.Public;
        var inspection = new ConnectionInspection(
            new MappingInspection(
                ConnectionMode.Private,
                isPublic ? MappingState.Absent : state,
                isPublic ? MappingRefusal.None : blockedBy,
                InLedger: !isPublic && state == MappingState.Present,
                saved.PrivateHttpsPort,
                "/"),
            new MappingInspection(
                ConnectionMode.Public,
                isPublic ? state : MappingState.Absent,
                isPublic ? blockedBy : MappingRefusal.None,
                InLedger: isPublic && state == MappingState.Present,
                saved.PublicHttpsPort,
                saved.PublicPathPrefix),
            stale ?? [],
            failure,
            ManualTimeProvider.DefaultStart,
            exposure ?? []);
        Session.Connection = inspection;
        return inspection;
    }

    public static OwnedMapping Owned(ConnectionMode mode, int httpsPort, string path = "/") =>
        new(mode, httpsPort, path, Target, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
}

/// <summary>
/// A file system that exists only in the test: the files and folders a test names, on the drives it
/// calls fixed. It can be asked whether something exists and nothing else.
/// </summary>
internal sealed class TestFiles : IHostFileSystem
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The drive letters that are fixed local drives.</summary>
    public HashSet<char> FixedDrives { get; } = ['C'];

    public string BaseDirectory => @"C:\LiveCue-tests\app";

    public string ProgramFilesDirectory => @"C:\Program Files";

    /// <summary>A folder that holds the two launcher scripts: a lab folder.</summary>
    public void AddLab(string folder)
    {
        Directories.Add(folder);
        Files.Add(Path.Combine(folder, "Start-ASR-Lab.ps1"));
        Files.Add(Path.Combine(folder, "Stop-ASR-Lab.ps1"));
    }

    public bool IsOnFixedLocalDrive(string path) =>
        path is { Length: >= 2 } && path[1] == ':' && FixedDrives.Contains(char.ToUpperInvariant(path[0]));

    public string? GetEnvironmentVariable(string name) => null;

    public bool FileExists(string path) => Files.Contains(path);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public void CreateDirectory(string path) => throw new NotSupportedException();

    public byte[] ReadAllBytes(string path) => throw new NotSupportedException();

    public void WriteAllBytesAtomic(string path, byte[] contents) => throw new NotSupportedException();

    public IReadOnlyList<FoundFile> FindFiles(string directory, string fileName, int maxDepth) => [];
}
