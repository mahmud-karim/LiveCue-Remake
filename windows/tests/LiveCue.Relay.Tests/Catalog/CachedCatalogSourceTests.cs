using LiveCue.Relay.Catalog;
using LiveCue.Relay.Protocol;
using LiveCue.Relay.Tests.Answers;
using LiveCue.Relay.Tests.LocalModel;
using LiveCue.Relay.Tests.Support;

namespace LiveCue.Relay.Tests.Catalog;

/// <summary>
/// The source cache of protocol 7.2 (statement P2-030): a successful read is reused for the cache life
/// of C-44, concurrent callers share one read, and a failure is never cached. Time is the manual clock.
/// </summary>
public sealed class CachedCatalogSourceTests : IDisposable
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly AreaHarness _harness = new();
    private readonly CachedCatalogSource _cached;

    public CachedCatalogSourceTests()
    {
        _harness.Catalog.Loader = () => FakeCatalogSource.FixtureL;
        _cached = new CachedCatalogSource(_harness.Catalog, _harness.Context);
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Five_reads_run_the_loader_once()
    {
        for (var read = 0; read < 5; read++)
        {
            var offers = OfferFilter.Apply(await _cached.ReadAsync(None));

            Assert.True(AssistantSelection.TryFind(offers, "gpt-6-luna", "low", out var choice));
            Assert.Equal(new AssistantChoice("gpt-6-luna", "low"), choice);
        }

        Assert.Equal(1, _harness.Catalog.Calls);
    }

    [Fact]
    public async Task A_read_is_reused_for_the_cache_life_and_not_a_moment_longer()
    {
        await _cached.ReadAsync(None);

        _harness.Clock.Advance(ProtocolConstants.CatalogCacheLife - TimeSpan.FromMilliseconds(1));
        await _cached.ReadAsync(None);
        Assert.Equal(1, _harness.Catalog.Calls);

        _harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await _cached.ReadAsync(None);
        Assert.Equal(2, _harness.Catalog.Calls);
        Assert.Equal(TimeSpan.FromMinutes(5), ProtocolConstants.CatalogCacheLife);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_read()
    {
        var gate = _harness.Catalog.Gate = AreaHarness.NewGate();

        var callers = Enumerable.Range(0, 4).Select(_ => _cached.ReadAsync(None)).ToArray();
        await Waiting.UntilAsync(() => _harness.Catalog.Calls == 1);
        await Task.Delay(50);
        Assert.All(callers, caller => Assert.False(caller.IsCompleted));
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, _harness.Catalog.Calls);
        Assert.All(results, result => Assert.Equal("gpt-6-luna", Assert.Single(result).Model));
    }

    [Fact]
    public async Task A_failure_is_not_cached()
    {
        _harness.Catalog.FailuresLeft = 2;

        await Assert.ThrowsAsync<CatalogReadException>(() => _cached.ReadAsync(None));
        await Assert.ThrowsAsync<CatalogReadException>(() => _cached.ReadAsync(None));
        var third = await _cached.ReadAsync(None);
        var fourth = await _cached.ReadAsync(None);

        Assert.Equal(3, _harness.Catalog.Calls);
        Assert.Same(third, fourth);
    }

    [Fact]
    public async Task A_callers_token_ends_only_that_callers_wait()
    {
        var gate = _harness.Catalog.Gate = AreaHarness.NewGate();
        using var impatient = new CancellationTokenSource();

        var leaves = _cached.ReadAsync(impatient.Token);
        var stays = _cached.ReadAsync(None);
        await Waiting.UntilAsync(() => _harness.Catalog.Calls == 1);
        await impatient.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaves);

        // The read goes on for the caller that still waits, and for everyone after it.
        Assert.False(stays.IsCompleted);
        gate.SetResult();
        Assert.Equal("gpt-6-luna", Assert.Single(await stays).Model);
        await _cached.ReadAsync(None);
        Assert.Equal(1, _harness.Catalog.Calls);

        // A caller that has already left starts nothing.
        _harness.Clock.Advance(ProtocolConstants.CatalogCacheLife);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _cached.ReadAsync(impatient.Token));
        Assert.Equal(1, _harness.Catalog.Calls);
    }

    [Fact]
    public async Task The_shared_read_ends_when_the_relay_stops()
    {
        _harness.Catalog.Gate = AreaHarness.NewGate();

        var waiting = _cached.ReadAsync(None);
        await Waiting.UntilAsync(() => _harness.Catalog.Calls == 1);
        _harness.Access.BeginStopping();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
