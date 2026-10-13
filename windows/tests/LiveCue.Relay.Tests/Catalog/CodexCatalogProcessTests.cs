using System.Globalization;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.Processes;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Answers;
using LiveCue.Relay.Tests.Processes;
using LiveCue.TestSupport;

namespace LiveCue.Relay.Tests.Catalog;

/// <summary>
/// Statement P2-044 against a real process: the catalogue exchange through the production runner with
/// <c>LiveCue.TestChild</c> standing in for <c>codex app-server</c>. The stand-in never ends by itself,
/// so that only the relay's kill can end it; afterwards no process is left.
/// </summary>
public sealed class CodexCatalogProcessTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task The_exchange_works_against_a_stub_process_and_the_process_is_gone_afterwards()
    {
        using var folder = new TempFolder();
        var report = folder.Combine("report.txt");
        var runner = new StandInCodexRunner(new ProcessRunner(TimeProvider.System), "ok", report);
        using var harness = new AreaHarness(processes: runner, systemClock: true);
        var source = new CodexCatalogSource(harness.Context);

        var entries = await source.ReadAsync(None).WaitAsync(TimeSpan.FromSeconds(60));

        // What a real pipe carried: the three lines of protocol 12.1, each ended by one line feed.
        var received = await TestChild.WaitForLinesAsync(report, 4);
        Assert.Equal(
            [
                """{"id":1,"method":"initialize","params":{"clientInfo":{"name":"livecue_catalog","title":"LiveCue","version":"1.0.0"}}}""",
                """{"method":"initialized"}""",
                """{"id":2,"method":"model/list","params":{"limit":100,"includeHidden":false}}""",
            ],
            received[1..]);

        // The entries received, the cursor ignored, the entry without a model skipped, and the lines
        // around the two replies skipped.
        Assert.Equal(["gpt-stand-in", "gpt-hidden"], entries.Select(entry => entry.Model));
        Assert.Equal("Stand-in", entries[0].DisplayName);
        Assert.False(entries[0].Hidden);
        Assert.Equal(["low", "high"], entries[0].ReasoningEfforts);
        Assert.True(entries[1].Hidden);
        Assert.Equal("gpt-stand-in", Assert.Single(OfferFilter.Apply(entries)).Id);

        // The process is dead when the read returns: the session's dispose kills it and waits for it.
        Assert.True(TestChild.IsGone(int.Parse(received[0], CultureInfo.InvariantCulture)));

        // The relay asked for Codex with the allowlisted environment and the two limits.
        var request = Assert.Single(runner.Requests);
        Assert.Equal(harness.Options.CodexExecutablePath, request.FileName);
        Assert.Equal(["app-server", "--listen", "stdio://"], request.Arguments);
        Assert.Equal(EnvironmentPolicy.CodexAllowlist, request.Environment);
    }

    [Theory]
    [InlineData("early-exit")]
    [InlineData("error")]
    [InlineData("flood")]
    [InlineData("silent")]
    public async Task A_failed_exchange_leaves_no_process(string scenario)
    {
        using var folder = new TempFolder();
        var report = folder.Combine("report.txt");
        var clock = new ManualTimeProvider();
        var runner = new StandInCodexRunner(new ProcessRunner(clock), scenario, report);
        using var harness = new AreaHarness(processes: runner);
        var source = new CodexCatalogSource(harness.Context);

        var read = source.ReadAsync(None);
        if (scenario == "silent")
        {
            // The stand-in has the first line and answers nothing. Both clocks reach the limit of C-44.
            var lines = await TestChild.WaitForLinesAsync(report, 2);
            Assert.False(TestChild.IsGone(int.Parse(lines[0], CultureInfo.InvariantCulture)));
            Assert.False(read.IsCompleted);
            clock.Advance(ProtocolConstants.CatalogAttemptTimeout);
            harness.Clock.Advance(ProtocolConstants.CatalogAttemptTimeout);
        }

        await Assert.ThrowsAsync<CatalogReadException>(() => read.WaitAsync(TimeSpan.FromSeconds(60)));

        var received = await TestChild.WaitForLinesAsync(report, 1);
        await TestChild.WaitUntilGoneAsync(int.Parse(received[0], CultureInfo.InvariantCulture));
        Assert.Equal(0, clock.ScheduledTimers);
        Assert.Equal(0, harness.Clock.ScheduledTimers);
    }
}
