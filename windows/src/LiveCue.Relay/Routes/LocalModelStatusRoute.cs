using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 4, <c>GET /v1/local-model</c> (protocol 6.5): the status object. Never fails for lab reasons.
/// </summary>
internal sealed class LocalModelStatusRoute : IRouteHandler
{
    private readonly ILocalModelController _localModel;

    public LocalModelStatusRoute(RelayContext context, ILocalModelController localModel)
    {
        ArgumentNullException.ThrowIfNull(context);
        _localModel = localModel;
    }

    public async Task HandleAsync(RouteContext context)
    {
        var status = await _localModel.GetStatusAsync(context.Work).ConfigureAwait(false);
        await context.WriteJsonAsync(StatusCodes.Status200OK, ToJson(status)).ConfigureAwait(false);
    }

    /// <summary>The five-member status object of protocol 9.1. Every member is always written.</summary>
    internal static byte[] ToJson(LocalModelStatus status) => WireJson.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("state", status.State switch
        {
            LocalModelState.Starting => "starting",
            LocalModelState.Ready => "ready",
            LocalModelState.Stopping => "stopping",
            LocalModelState.Error => "error",
            _ => "stopped",
        });
        if (status.Model is null)
        {
            writer.WriteNull("model");
        }
        else
        {
            writer.WriteString("model", status.Model);
        }

        writer.WriteBoolean("busy", status.Busy);
        writer.WriteNumber("elapsedSeconds", status.ElapsedSeconds);
        writer.WriteString("message", status.Message);
        writer.WriteEndObject();
    });
}
