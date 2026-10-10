using LiveCue.Relay.Core;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Catalog;

/// <summary>
/// The catalogue as routes 3, 7 and 8 see it (protocol 7.2), over <see cref="CachedCatalogSource"/> over
/// the source:
///
/// - one request tries up to the attempts of C-44, with the gap of C-44 between them;
/// - a catalogue that was read replaces the last good copy, also when it is empty;
/// - after the last failed attempt the last good copy is returned, if there has ever been one;
/// - otherwise the result is ASSISTANT_CATALOG_UNAVAILABLE. There is no built-in list of models;
/// - every caller gets a copy of its own.
/// </summary>
internal sealed class CatalogReader : ICatalogReader
{
    private readonly RelayContext _context;
    private readonly CachedCatalogSource _source;
    private List<ModelOffer>? _lastGood;

    public CatalogReader(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _source = new CachedCatalogSource(context.Dependencies.CatalogSource ?? new CodexCatalogSource(context), context);
    }

    public async Task<IReadOnlyList<ModelOffer>> ReadAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var entries = await _source.ReadAsync(ct).ConfigureAwait(false);
                var offers = OfferFilter.Apply(entries);
                Volatile.Write(ref _lastGood, offers);
                return Copy(offers);
            }
            catch (CatalogReadException)
            {
                // A failed attempt.
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The shared read ended because the relay is stopping: a failed attempt for this caller.
            }

            if (attempt >= ProtocolConstants.CatalogAttempts)
            {
                break;
            }

            await Task.Delay(ProtocolConstants.CatalogRetryGap, _context.Clock, ct).ConfigureAwait(false);
        }

        return Volatile.Read(ref _lastGood) is { } lastGood
            ? Copy(lastGood)
            : throw new RelayException(RelayErrors.AssistantCatalogUnavailable);
    }

    /// <summary>A list the caller owns: changing it changes no later result.</summary>
    private static List<ModelOffer> Copy(List<ModelOffer> offers)
    {
        var copy = new List<ModelOffer>(offers.Count);
        foreach (var offer in offers)
        {
            copy.Add(new ModelOffer(offer.Id, offer.Name, new List<string>(offer.ReasoningEfforts)));
        }

        return copy;
    }
}
