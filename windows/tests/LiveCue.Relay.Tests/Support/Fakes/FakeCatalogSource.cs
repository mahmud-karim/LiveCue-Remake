using LiveCue.Relay.Catalog;

namespace LiveCue.Relay.Tests.Support;

/// <summary>
/// The catalogue seam for tests: a loader function with a call counter that can fail or block on a
/// gate. By default it returns the fixture catalogue of protocol section 15: <c>gpt-test</c> with
/// <c>["low"]</c>.
/// </summary>
internal sealed class FakeCatalogSource : ICatalogSource
{
    private int _calls;

    /// <summary>The fixture catalogue of protocol section 15.</summary>
    public static IReadOnlyList<RawCatalogEntry> Fixture { get; } =
        [new RawCatalogEntry("gpt-test", "Test", Hidden: false, ["low"])];

    /// <summary>Fixture <c>L</c> of protocol 15.3: <c>gpt-6-luna</c>, not hidden, <c>["low"]</c>.</summary>
    public static IReadOnlyList<RawCatalogEntry> FixtureL { get; } =
        [new RawCatalogEntry("gpt-6-luna", "GPT-6 Luna", Hidden: false, ["low"])];

    /// <summary>What a read returns. Replace it to script a test.</summary>
    public Func<IReadOnlyList<RawCatalogEntry>> Loader { get; set; } = () => Fixture;

    /// <summary>True: every read attempt fails.</summary>
    public bool Fails { get; set; }

    /// <summary>This many read attempts fail before reads succeed again.</summary>
    public int FailuresLeft { get; set; }

    /// <summary>When set, a read waits for it before it returns or fails.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>How many read attempts were made.</summary>
    public int Calls => Volatile.Read(ref _calls);

    public async Task<IReadOnlyList<RawCatalogEntry>> ReadAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        if (Fails)
        {
            throw new CatalogReadException();
        }

        if (FailuresLeft > 0)
        {
            FailuresLeft--;
            throw new CatalogReadException();
        }

        return Loader();
    }
}
