using System.Windows;
using LiveCue.Desktop.SelfTest;
using LiveCue.Desktop.Tests.Support;
using LiveCue.Hosting;

namespace LiveCue.Desktop.Tests.SelfTest;

/// <summary>
/// The self-test's isolation (W-design 5.13, "Isolation, by construction"), looked at from outside it:
/// the whole scripted run is made inside the test process, and afterwards the recording seams are asked
/// what reached them. The run's own <c>no-side-effects</c> assertion says the same from inside.
/// </summary>
[Collection(UiSuite.Name)]
[Trait("Category", "Ui")]
public sealed class SelfTestIsolationTests
{
    [Fact]
    public async Task The_recording_seams_see_no_call()
    {
        var lines = new List<string>();
        SelfTestSession? session = null;

        await WpfApp.RunAsync(async () =>
        {
            session = new SelfTestSession(Application.Current, themes: null, new SelfTestOptions(), new SelfTestLog(directory: null, lines.Add));
            await session.RunAsync();
        });

        Assert.NotNull(session);

        // The run did everything: every scene was shown and every assertion held.
        Assert.Equal(Scenes.Names, session.ScenesShown);
        Assert.Contains("no-side-effects", session.AssertionsPassed);
        Assert.Contains("activity-cap", session.AssertionsPassed);
        Assert.Empty(session.ScreenshotsWritten);

        // No process was started, by the supervisor, the checks or the mapping manager.
        Assert.Equal(0, session.Hosting.Processes.Calls);

        // No file was read or written. The file system was only asked whether things exist, and was
        // told "no": that is how the supervisor learns there is no bundled speech lab.
        Assert.Equal(0, session.Hosting.Files.Touches);
        Assert.True(session.Hosting.Files.Probes > 0);
        Assert.False(Directory.Exists(ScriptedHosting.UnusedDataDirectory));

        // Nothing went to the clipboard, and nothing was opened outside the app.
        Assert.Empty(session.Clipboard.Texts);
        Assert.Empty(session.Launcher.Opened);

        // Tailscale was read through the scripted manager only, and no write was asked of it.
        Assert.True(session.Hosting.Mappings.Inspections > 0);
        Assert.Equal(0, session.Hosting.Mappings.Executions);

        // The relay was the scripted one, which binds nothing, and it is stopped.
        Assert.Equal(SupervisorState.Stopped, session.Hosting.Services.Supervisor.State);
        Assert.Null(session.Hosting.Services.Supervisor.Relay);

        // The token of the synthetic pairing was never printed.
        Assert.DoesNotContain(lines, line => line.Contains(SyntheticData.Token, StringComparison.Ordinal));
    }

    [Fact]
    public void The_scripted_file_system_refuses_what_would_touch_the_disk()
    {
        var files = new RecordingFiles();

        Assert.False(files.FileExists(@"C:\anything"));
        Assert.False(files.DirectoryExists(@"C:\anything"));
        Assert.Empty(files.FindFiles(@"C:\anything", "codex.exe", 3));
        Assert.Null(files.GetEnvironmentVariable("LOCALAPPDATA"));
        Assert.Equal(0, files.Touches);

        Assert.Throws<IOException>(() => files.ReadAllBytes(@"C:\anything"));
        Assert.Throws<IOException>(() => files.WriteAllBytesAtomic(@"C:\anything", []));
        Assert.Throws<IOException>(() => files.CreateDirectory(@"C:\anything"));
        Assert.Equal(3, files.Touches);
    }

    [Fact]
    public void The_synthetic_data_names_only_reserved_hosts_and_a_token_that_is_no_token()
    {
        // example.test: a name under a top-level domain reserved for testing (RFC 2606, RFC 6761).
        Assert.Equal("pc.example.test", SyntheticData.DnsName);
        Assert.Equal("https://pc.example.test:11443/", SyntheticData.Endpoint);
        Assert.Equal(43, SyntheticData.Token.Length);
        Assert.Single(SyntheticData.Token.Distinct());
        Assert.Equal(
            "{\"endpoint\":\"https://pc.example.test:11443/\",\"token\":\"" + SyntheticData.Token + "\"}",
            SyntheticData.PairingPayload);

        // The matrix is a QR code of the real size for such a payload: square, with the three finder
        // patterns, and the same every time.
        var matrix = SyntheticData.PairingMatrix();
        Assert.Equal(41, matrix.Length);
        Assert.All(matrix, row => Assert.Equal(41, row.Length));
        foreach (var (row, column) in new[] { (0, 0), (0, 34), (34, 0) })
        {
            // A finder pattern: a dark frame seven modules wide around a light ring.
            Assert.All(Enumerable.Range(0, 7), offset => Assert.True(matrix[row][column + offset] && matrix[row + 6][column + offset]));
            Assert.All(Enumerable.Range(1, 5), offset => Assert.False(matrix[row + 1][column + offset]));
        }

        Assert.Equal(matrix, SyntheticData.PairingMatrix());

        Assert.Equal("selftest-0001", SyntheticData.RequestId);
        Assert.Equal(1000, SyntheticData.Filler(0).Length);
        Assert.Equal(1000, SyntheticData.Filler(399).Length);
        Assert.NotEqual(SyntheticData.Filler(0), SyntheticData.Filler(1));
    }
}
