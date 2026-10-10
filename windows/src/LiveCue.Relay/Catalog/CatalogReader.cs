using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Catalog;

/// <summary>
/// The catalogue with retry, last-good and isolation (protocol 7.2), over
/// <see cref="CachedCatalogSource"/> over the source.
///
/// First version (foundation): one attempt, no retry and no last-good list. With a source given through
/// <see cref="RelayDependencies"/> it returns that source's entries unfiltered; without one it returns
/// an empty list. A failing source is ASSISTANT_CATALOG_UNAVAILABLE. Work package WP-W03 builds the rest.
/// </summary>
internal sealed class CatalogReader : ICatalogReader
{
    private readonly CachedCatalogSource? _source;

    public CatalogReader(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _source = context.Dependencies.CatalogSource is { } given
            ? new CachedCatalogSource(given, context)
            : null;
    }

    public async Task<IReadOnlyList<ModelOffer>> ReadAsync(CancellationToken ct)
    {
        if (_source is null)
        {
            ct.ThrowIfCancellationRequested();
            return [];
        }

        IReadOnlyList<RawCatalogEntry> entries;
        try
        {
            entries = await _source.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (CatalogReadException)
        {
            throw new RelayException(RelayErrors.AssistantCatalogUnavailable);
        }

        var offers = new List<ModelOffer>(entries.Count);
        foreach (var entry in entries)
        {
            offers.Add(new ModelOffer(
                entry.Model,
                string.IsNullOrEmpty(entry.DisplayName) ? entry.Model : entry.DisplayName,
                [.. entry.ReasoningEfforts]));
        }

        return offers;
    }
}
