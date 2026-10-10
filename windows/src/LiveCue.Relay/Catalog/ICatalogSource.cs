namespace LiveCue.Relay.Catalog;

/// <summary>What is kept of one entry of Codex's model list (protocol 12.1).</summary>
internal sealed record RawCatalogEntry(
    string Model,
    string? DisplayName,
    bool Hidden,
    IReadOnlyList<string> ReasoningEfforts);

/// <summary>Thrown for one failed read attempt. Fixed message.</summary>
internal sealed class CatalogReadException : Exception
{
    public CatalogReadException()
        : base("The catalogue could not be read.")
    {
    }
}

/// <summary>Catalogue seam: one read attempt of the raw list.</summary>
internal interface ICatalogSource
{
    /// <summary>Throws <see cref="CatalogReadException"/> on any failure.</summary>
    Task<IReadOnlyList<RawCatalogEntry>> ReadAsync(CancellationToken ct);
}
