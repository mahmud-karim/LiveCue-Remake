using LiveCue.Relay.Catalog;
using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Answers;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Catalog;

/// <summary>
/// The catalogue with retry, last-good and isolation (protocol 7.2; statements P2-031 to P2-035), over
/// the fake source of fixture <c>L</c> and the manual clock. There is no fallback list anywhere.
/// </summary>
public sealed class CatalogReaderTests : IDisposable
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly AreaHarness _harness = new();
    private readonly CatalogReader _reader;

    public CatalogReaderTests()
    {
        _harness.Catalog.Loader = () => FakeCatalogSource.FixtureL;
        _reader = new CatalogReader(_harness.Context);
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_failing_loader_returns_the_last_good_catalogue()
    {
        var first = await _reader.ReadAsync(None);

        // The loader starts failing, and the cached copy of the source has aged out.
        _harness.Catalog.Fails = true;
        _harness.Clock.Advance(ProtocolConstants.CatalogCacheLife);
        var again = await _harness.PumpAsync(_reader.ReadAsync(None));

        Assert.Equal(1 + ProtocolConstants.CatalogAttempts, _harness.Catalog.Calls);
        AssertSameOffers(first, again);
        Assert.NotSame(first, again);
        Assert.True(AssistantSelection.TryFind(again, "gpt-6-luna", "low", out _));
    }

    [Fact]
    public async Task Changing_a_returned_catalogue_does_not_change_the_next_read()
    {
        var first = await _reader.ReadAsync(None);
        var efforts = Assert.IsAssignableFrom<IList<string>>(first[0].ReasoningEfforts);
        efforts.Clear();
        Assert.IsAssignableFrom<IList<ModelOffer>>(first).Clear();

        var second = await _reader.ReadAsync(None);

        // Also when the changed copy is what a failing loader makes the reader fall back to.
        _harness.Catalog.Fails = true;
        _harness.Clock.Advance(ProtocolConstants.CatalogCacheLife);
        var third = await _harness.PumpAsync(_reader.ReadAsync(None));

        Assert.Equal(["low"], Assert.Single(second).ReasoningEfforts);
        Assert.Equal(["low"], Assert.Single(third).ReasoningEfforts);
    }

    [Fact]
    public async Task An_empty_catalogue_replaces_the_last_good_one()
    {
        Assert.Single(await _reader.ReadAsync(None));

        _harness.Catalog.Loader = () => [];
        _harness.Clock.Advance(ProtocolConstants.CatalogCacheLife);
        var empty = await _reader.ReadAsync(None);

        Assert.Empty(empty);
        Assert.False(AssistantSelection.TryFind(empty, "gpt-6-luna", "low", out _));

        // The empty catalogue is now the last good one: a failing loader does not bring the old one back.
        _harness.Catalog.Fails = true;
        _harness.Clock.Advance(ProtocolConstants.CatalogCacheLife);
        Assert.Empty(await _harness.PumpAsync(_reader.ReadAsync(None)));
    }

    [Fact]
    public async Task Without_any_good_catalogue_the_result_is_unavailable()
    {
        _harness.Catalog.Fails = true;

        var read = _reader.ReadAsync(None);

        // Three attempts, with the gap of C-44 between them on the relay's clock.
        await Waiting.UntilAsync(() => _harness.Catalog.Calls == 1 && _harness.Clock.ScheduledTimers == 1);
        _harness.Clock.Advance(ProtocolConstants.CatalogRetryGap - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        Assert.Equal(1, _harness.Catalog.Calls);
        _harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await Waiting.UntilAsync(() => _harness.Catalog.Calls == 2 && _harness.Clock.ScheduledTimers == 1);
        _harness.Clock.Advance(ProtocolConstants.CatalogRetryGap);
        var failure = await Assert.ThrowsAsync<RelayException>(() => read.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(RelayErrors.AssistantCatalogUnavailable, failure.Error);
        Assert.Equal(3, _harness.Catalog.Calls);
        Assert.Equal(TimeSpan.FromMilliseconds(50), ProtocolConstants.CatalogRetryGap);
        Assert.Equal(0, _harness.Clock.ScheduledTimers);

        // Every later request tries again, and fails the same way: nothing is invented.
        var later = await Assert.ThrowsAsync<RelayException>(() => _harness.PumpAsync(_reader.ReadAsync(None)));
        Assert.Equal(RelayCodes.AssistantCatalogUnavailable, later.Error.Code);
        Assert.Equal(6, _harness.Catalog.Calls);
    }

    [Fact]
    public async Task One_failure_then_success_takes_two_loader_calls()
    {
        _harness.Catalog.FailuresLeft = 1;

        var offers = await _harness.PumpAsync(_reader.ReadAsync(None));

        Assert.Equal("gpt-6-luna", offers[0].Id);
        Assert.Equal(2, _harness.Catalog.Calls);
    }

    [Fact]
    public async Task What_is_offered_is_the_filtered_form_of_what_was_read()
    {
        _harness.Catalog.Loader = () =>
        [
            new RawCatalogEntry("gpt-5.5", null, Hidden: false, ["high", "ultra", "low", "high"]),
            new RawCatalogEntry("gpt-hidden", "Hidden", Hidden: true, ["low"]),
            new RawCatalogEntry("claude-x", "Other", Hidden: false, ["low"]),
            new RawCatalogEntry("gpt-no-efforts", "None left", Hidden: false, ["ultra"]),
        ];

        var offer = Assert.Single(await _reader.ReadAsync(None));

        Assert.Equal(new { Id = "gpt-5.5", Name = "gpt-5.5" }, new { offer.Id, offer.Name });
        Assert.Equal(["none", "high", "low"], offer.ReasoningEfforts);
    }

    [Fact]
    public async Task A_callers_token_ends_its_wait_between_attempts()
    {
        _harness.Catalog.Fails = true;
        using var leaving = new CancellationTokenSource();

        var read = _reader.ReadAsync(leaving.Token);
        await Waiting.UntilAsync(() => _harness.Catalog.Calls == 1 && _harness.Clock.ScheduledTimers == 1);
        await leaving.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, _harness.Catalog.Calls);
        Assert.Equal(0, _harness.Clock.ScheduledTimers);
    }

    [Fact]
    public async Task Without_a_codex_path_and_without_a_source_the_catalogue_is_unavailable()
    {
        // The production source, with no Codex found: every attempt fails without starting anything.
        using var harness = new AreaHarness(codexPath: null);
        var source = new CodexCatalogSource(harness.Context);

        await Assert.ThrowsAsync<CatalogReadException>(() => source.ReadAsync(None));
        Assert.Empty(harness.Processes.Requests);
    }

    private static void AssertSameOffers(IReadOnlyList<ModelOffer> expected, IReadOnlyList<ModelOffer> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Id);
            Assert.Equal(expected[index].Name, actual[index].Name);
            Assert.Equal(expected[index].ReasoningEfforts, actual[index].ReasoningEfforts);
        }
    }
}
