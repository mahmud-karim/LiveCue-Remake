using LiveCue.Relay.Catalog;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 3, <c>GET /v1/models</c> (protocol 6.4). Served while paused.
///
/// First version (foundation): writes what the catalogue reader returns. Publishing AssistantNotice
/// when the catalogue is unavailable is work package WP-W03.
/// </summary>
internal sealed class ModelsRoute : IRouteHandler
{
    private readonly ICatalogReader _catalog;

    public ModelsRoute(RelayContext context, ICatalogReader catalog)
    {
        ArgumentNullException.ThrowIfNull(context);
        _catalog = catalog;
    }

    public async Task HandleAsync(RouteContext context)
    {
        var offers = await _catalog.ReadAsync(context.Work).ConfigureAwait(false);
        var document = WireJson.Write(writer =>
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
        await context.WriteJsonAsync(StatusCodes.Status200OK, document).ConfigureAwait(false);
    }
}
