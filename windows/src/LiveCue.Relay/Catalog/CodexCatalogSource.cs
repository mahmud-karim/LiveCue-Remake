using LiveCue.Relay.Core;

namespace LiveCue.Relay.Catalog;

/// <summary>
/// One read attempt of Codex's model list through <c>codex app-server</c> (protocol 12.1).
///
/// First version (foundation): every attempt fails. Work package WP-W03 builds the exchange.
/// </summary>
internal sealed class CodexCatalogSource : ICatalogSource
{
    public CodexCatalogSource(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public Task<IReadOnlyList<RawCatalogEntry>> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new CatalogReadException();
    }
}
