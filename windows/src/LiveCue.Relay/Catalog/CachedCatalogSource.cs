using LiveCue.Relay.Core;

namespace LiveCue.Relay.Catalog;

/// <summary>
/// The last successful raw list and its time, with one shared read in progress (protocol 7.2).
///
/// First version (foundation): passes every read straight to the source. Work package WP-W03 adds the
/// cache life of C-44 and the shared read.
/// </summary>
internal sealed class CachedCatalogSource : ICatalogSource
{
    private readonly ICatalogSource _source;

    public CachedCatalogSource(ICatalogSource source, RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        _source = source;
    }

    public Task<IReadOnlyList<RawCatalogEntry>> ReadAsync(CancellationToken ct) => _source.ReadAsync(ct);
}
