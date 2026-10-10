using System.Text.Json;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 5, <c>POST /v1/local-model/start</c> (protocol 6.6): checks M-1 to M-3 here, M-4 onwards in the
/// controller. 202 when an operation began, 200 when nothing needed doing.
/// </summary>
internal sealed class LocalModelStartRoute : IRouteHandler
{
    private readonly RelayContext _context;
    private readonly ILocalModelController _localModel;

    public LocalModelStartRoute(RelayContext context, ILocalModelController localModel)
    {
        _context = context;
        _localModel = localModel;
    }

    public async Task HandleAsync(RouteContext context)
    {
        var model = await ReadModelAsync(context, _context).ConfigureAwait(false);
        var command = await _localModel.StartAsync(model, context.Work).ConfigureAwait(false);
        await WriteAsync(context, command).ConfigureAwait(false);
    }

    /// <summary>M-1 (paused), M-2 (body rules) and M-3 (the model is one of C-04), shared with route 6.</summary>
    internal static async Task<string> ReadModelAsync(RouteContext context, RelayContext relay)
    {
        if (!relay.Access.Accepting)
        {
            throw new RelayException(RelayErrors.RelayPaused);
        }

        var body = await context.ReadJsonBodyAsync().ConfigureAwait(false);
        if (body.Error is { } error)
        {
            throw new RelayException(error);
        }

        if (!body.Root.TryGetProperty("model", out var member)
            || member.ValueKind != JsonValueKind.String
            || member.GetString() is not { } model
            || !ProtocolConstants.SpeechModels.Contains(model, StringComparer.Ordinal))
        {
            throw new RelayException(RelayErrors.InvalidModel);
        }

        return model;
    }

    /// <summary>The refusal, or the status object with 202 or 200, shared with route 6.</summary>
    internal static Task WriteAsync(RouteContext context, LocalModelCommand command)
    {
        if (command.Refusal is { } refusal)
        {
            return context.WriteErrorAsync(refusal);
        }

        var status = command.Status
            ?? throw new InvalidOperationException("A model command has neither a refusal nor a status.");
        return context.WriteJsonAsync(
            command.Began ? StatusCodes.Status202Accepted : StatusCodes.Status200OK,
            LocalModelStatusRoute.ToJson(status));
    }
}
