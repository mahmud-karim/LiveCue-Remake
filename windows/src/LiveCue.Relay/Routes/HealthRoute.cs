using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>Route 1, <c>GET /v1/health</c> (protocol 6.2): the pre-built three-member document. No other work.</summary>
internal sealed class HealthRoute : IRouteHandler
{
    private readonly byte[] _document;

    public HealthRoute(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var version = context.Options.RelayVersion;
        _document = WireJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteNumber("protocol", ProtocolConstants.ProtocolVersion);
            writer.WriteString("relayVersion", version);
            writer.WriteEndObject();
        });
    }

    public Task HandleAsync(RouteContext context) => context.WriteJsonAsync(StatusCodes.Status200OK, _document);
}
