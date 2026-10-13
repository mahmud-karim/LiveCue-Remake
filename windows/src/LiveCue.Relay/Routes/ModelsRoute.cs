using LiveCue.Relay.Catalog;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 3, <c>GET /v1/models</c> (protocol 6.4; W-design 4.6). Served while paused. 200 with every
/// offer as <c>id</c>, <c>name</c> and <c>reasoningEfforts</c>; an empty catalogue is
/// <c>{"models":[]}</c>. When the catalogue cannot be read the desktop is told, and the answer is
/// 503 ASSISTANT_CATALOG_UNAVAILABLE.
/// </summary>
internal sealed class ModelsRoute : IRouteHandler
{
    private readonly RelayContext _context;
    private readonly ICatalogReader _catalog;

    public ModelsRoute(RelayContext context, ICatalogReader catalog)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(catalog);
        _context = context;
        _catalog = catalog;
    }

    public async Task HandleAsync(RouteContext context)
    {
        IReadOnlyList<ModelOffer> offers;
        try
        {
            offers = await _catalog.ReadAsync(context.Work).ConfigureAwait(false);
        }
        catch (RelayException exception) when (exception.Error.Code == RelayCodes.AssistantCatalogUnavailable)
        {
            var events = _context.Events;
            events.Publish(new AssistantNotice(events.Now, exception.Error.Code, exception.Error.Text));
            await context.WriteErrorAsync(exception.Error).ConfigureAwait(false);
            return;
        }

        await context.WriteJsonAsync(StatusCodes.Status200OK, ToJson(offers)).ConfigureAwait(false);
    }

    /// <summary>The document of protocol 6.4.</summary>
    internal static byte[] ToJson(IReadOnlyList<ModelOffer> offers) => WireJson.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteStartArray("models");
        foreach (var offer in offers)
        {
            writer.WriteStartObject();
            writer.WriteString("id", offer.Id);
            writer.WriteString("name", offer.Name);
            writer.WriteStartArray("reasoningEfforts");
            foreach (var effort in offer.ReasoningEfforts)
            {
                writer.WriteStringValue(effort);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    });
}
