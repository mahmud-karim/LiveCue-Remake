using LiveCue.Relay.Tests.Support;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Host;

/// <summary>
/// Test plan ST-15, the part that belongs to the relay's start (W-design 4.2 step 5, protocol 12.2): at
/// every start everything under <c>work\</c> is deleted, so that an output file left by a crash or a kill
/// does not outlive the next start. The per-request side of ST-15 is with the answer area.
/// </summary>
public sealed class WorkFolderTests
{
    private const string Canary = "CANARY transcript text that must not survive";

    [Fact]
    public async Task A_start_empties_the_work_folder()
    {
        using var data = new TempFolder();

        // What a killed run leaves behind: a request folder with Codex's output file, and loose files.
        var left = Directory.CreateDirectory(data.Combine("work", "0123456789abcdef0123456789abcdef"));
        await File.WriteAllTextAsync(Path.Combine(left.FullName, "output.json"), Canary);
        Directory.CreateDirectory(Path.Combine(left.FullName, "nested", "deeper"));
        await File.WriteAllTextAsync(Path.Combine(left.FullName, "nested", "deeper", "more.txt"), Canary);
        await File.WriteAllTextAsync(data.Combine("work", "stray.json"), Canary);

        await using var relay = RelayFactory.Create(Options(data), Dependencies(tempRoot: null));
        var events = new EventCollector(relay.Events);
        await relay.StartAsync(CancellationToken.None);
        await events.SettleAsync();

        // The folder is there and empty; nothing in the data folder holds the text any more.
        Assert.True(Directory.Exists(data.Combine("work")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(data.Combine("work")));
        Assert.All(data.FileNames(), name =>
            Assert.DoesNotContain(Canary, File.ReadAllText(data.Combine(name)), StringComparison.Ordinal));
        Assert.Empty(events.OfType<Diagnostic>());
        Assert.Equal(RelayStatus.Running, relay.Status);
    }

    [Fact]
    public async Task A_start_creates_the_work_folder_when_there_is_none()
    {
        using var data = new TempFolder();

        await using var relay = RelayFactory.Create(Options(data), Dependencies(tempRoot: null));
        await relay.StartAsync(CancellationToken.None);

        Assert.True(Directory.Exists(data.Combine("work")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(data.Combine("work")));
    }

    [Fact]
    public async Task A_folder_given_as_temp_root_is_never_emptied_and_no_work_folder_is_made()
    {
        using var data = new TempFolder();
        using var given = new TempFolder();
        await File.WriteAllTextAsync(given.Combine("keep.txt"), "kept");
        Directory.CreateDirectory(given.Combine("kept-folder"));

        await using var relay = RelayFactory.Create(Options(data), Dependencies(tempRoot: given.Path));
        await relay.StartAsync(CancellationToken.None);

        Assert.Equal("kept", await File.ReadAllTextAsync(given.Combine("keep.txt")));
        Assert.True(Directory.Exists(given.Combine("kept-folder")));
        Assert.False(Directory.Exists(data.Combine("work")));
    }

    [Fact]
    public async Task What_cannot_be_deleted_is_left_and_reported_and_the_relay_starts()
    {
        using var data = new TempFolder();
        var locked = Directory.CreateDirectory(data.Combine("work", "locked"));
        var file = Path.Combine(locked.FullName, "output.json");
        await File.WriteAllTextAsync(file, "{}");
        await File.WriteAllTextAsync(data.Combine("work", "free.json"), "{}");

        await using var relay = RelayFactory.Create(Options(data), Dependencies(tempRoot: null));
        var events = new EventCollector(relay.Events);
        await using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await relay.StartAsync(CancellationToken.None);
        }

        await events.SettleAsync();

        // Not a fault: the relay runs, the rest was deleted, and the desktop is told with a fixed text.
        Assert.Equal(RelayStatus.Running, relay.Status);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(data.Combine("work", "free.json")));
        var diagnostic = Assert.Single(events.OfType<Diagnostic>());
        Assert.Equal("relay", diagnostic.Source);
        Assert.StartsWith("A leftover working folder could not be deleted. (", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(data.Path, diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_work_folder_that_cannot_be_made_is_a_start_fault()
    {
        using var data = new TempFolder();
        await File.WriteAllTextAsync(data.Combine("work"), "a file where the folder belongs");

        await using var relay = RelayFactory.Create(Options(data), Dependencies(tempRoot: null));
        var exception = await Assert.ThrowsAsync<RelayStartException>(() => relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayFaultCode.StartFailed, exception.Code);
        Assert.Equal("Could not start the relay.", exception.Message);
        Assert.Equal(RelayStatus.Faulted, relay.Status);
        Assert.Equal(0, relay.BoundPort);
    }

    [Fact]
    public async Task A_work_folder_that_is_a_link_is_not_followed()
    {
        // A junction named work that points at somebody else's folder: "everything in work" must not
        // become "everything in that folder".
        using var data = new TempFolder();
        using var elsewhere = new TempFolder();
        await File.WriteAllTextAsync(elsewhere.Combine("precious.txt"), "precious");
        try
        {
            Directory.CreateSymbolicLink(data.Combine("work"), elsewhere.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Creating a link needs a privilege this account may not have; then there is nothing to prove here.
            return;
        }

        await using var relay = RelayFactory.Create(Options(data), Dependencies(tempRoot: null));
        var thrown = await Assert.ThrowsAsync<RelayStartException>(() => relay.StartAsync(CancellationToken.None));

        Assert.Equal(RelayFaultCode.StartFailed, thrown.Code);
        Assert.Equal("precious", await File.ReadAllTextAsync(elsewhere.Combine("precious.txt")));
        Directory.Delete(data.Combine("work"));
    }

    private static RelayOptions Options(TempFolder data) => new(
        Port: 0,
        AdvertisedEndpoint: RelayTestHost.Endpoint,
        Mode: ConnectionMode.Private,
        DataDirectory: data.Path,
        CodexExecutablePath: RelayTestHost.FakeCodexPath,
        LabFolder: null,
        LabPort: 47842,
        RelayVersion: RelayTestHost.RelayVersion,
        WarmCatalogOnStart: false);

    private static RelayDependencies Dependencies(string? tempRoot) => new()
    {
        Clock = new ManualTimeProvider(),
        Processes = new FakeProcessRunner(),
        TempRoot = tempRoot,
        PairingProtector = new FakePairingProtector(),
        PairingFiles = new InMemoryPairingFiles(),
        SelfCheckClient = new FakeSelfCheckClient(),
        CatalogSource = new FakeCatalogSource(),
        AnswerBackend = new FakeAnswerBackend(),
        LabRuntime = new FakeLabRuntime(),
        LabSocketConnector = new FakeLabSocketConnector(),
    };
}
