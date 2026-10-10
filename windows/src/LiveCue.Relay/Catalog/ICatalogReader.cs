namespace LiveCue.Relay.Catalog;

/// <summary>An offered model (protocol 7.1). ReasoningEfforts is never empty.</summary>
internal sealed record ModelOffer(string Id, string Name, IReadOnlyList<string> ReasoningEfforts);

/// <summary>The catalogue with retry, last-good and isolation (protocol 7.2).</summary>
internal interface ICatalogReader
{
    /// <summary>
    /// Returns a list the caller owns. Throws RelayException(ASSISTANT_CATALOG_UNAVAILABLE) when three
    /// attempts failed and there has never been a good catalogue. <paramref name="ct"/> ends the caller's
    /// wait only; a shared read in progress continues for the other callers.
    /// </summary>
    Task<IReadOnlyList<ModelOffer>> ReadAsync(CancellationToken ct);
}
