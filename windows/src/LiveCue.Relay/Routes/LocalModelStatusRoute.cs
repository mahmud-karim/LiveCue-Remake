using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 4, <c>GET /v1/local-model</c> (protocol 6.5): 200 with the status object. It never fails for lab
/// reasons (an unreachable lab is the state <c>stopped</c>) and is served while the relay is paused.
/// </summary>
internal sealed class LocalModelStatusRoute : IRouteHandler
{
    private readonly ILocalModelController _localModel;

    public LocalModelStatusRoute(RelayContext context, ILocalModelController localModel)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(localModel);
        _localModel = localModel;
    }

    public async Task HandleAsync(RouteContext context)
    {
        var status = await _localModel.GetStatusAsync(context.Work).ConfigureAwait(false);
        await context.WriteJsonAsync(StatusCodes.Status200OK, ToJson(status)).ConfigureAwait(false);
    }

    /// <summary>
    /// The status object of protocol 9.1, written member by member and never by serialising the record:
    /// exactly <c>state</c>, <c>model</c>, <c>busy</c>, <c>elapsedSeconds</c> and <c>message</c>, in that
    /// order. <c>state</c> is the lower-case name; <c>model</c> is a string or JSON <c>null</c>, never
    /// omitted; <c>busy</c> is a JSON boolean; <c>elapsedSeconds</c> is a JSON integer with no fraction.
    /// The phone decodes this object strictly: a missing member or a number in quotes fails every poll.
    /// </summary>
    internal static byte[] ToJson(LocalModelStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return WireJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", StateName(status.State));
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

    private static string StateName(LocalModelState state) => state switch
    {
        LocalModelState.Stopped => "stopped",
        LocalModelState.Starting => "starting",
        LocalModelState.Ready => "ready",
        LocalModelState.Stopping => "stopping",
        LocalModelState.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(state), "Not a state of protocol 9.1."),
    };
}
