using System.Text.Json;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 5, <c>POST /v1/local-model/start</c> (protocol 6.6): the checks M-1 to M-3 here, M-4 to M-6 in the
/// controller. 202 with the beginning status when an operation began, 200 with the current status when
/// nothing needed doing.
/// </summary>
internal sealed class LocalModelStartRoute : IRouteHandler
{
    private readonly RelayContext _context;
    private readonly ILocalModelController _localModel;

    public LocalModelStartRoute(RelayContext context, ILocalModelController localModel)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(localModel);
        _context = context;
        _localModel = localModel;
    }

    public async Task HandleAsync(RouteContext context)
    {
        if (await ReadModelAsync(context, _context).ConfigureAwait(false) is not { } model)
        {
            return;
        }

        var command = await _localModel.StartAsync(model, context.Work).ConfigureAwait(false);
        await WriteAsync(context, command).ConfigureAwait(false);
    }

    /// <summary>
    /// M-1 (paused, before the body is read), M-2 (the body rules of protocol 5.4) and M-3 (<c>model</c> is
    /// a string equal to one of C-04), shared with route 6. Null means a check failed and its answer was
    /// written. Otherwise the result is the member of C-04 itself, never the text of the request.
    /// </summary>
    internal static async Task<string?> ReadModelAsync(RouteContext context, RelayContext relay)
    {
        // M-1.
        if (!relay.Access.Accepting)
        {
            await context.WriteErrorAsync(RelayErrors.RelayPaused).ConfigureAwait(false);
            return null;
        }

        // M-2.
        var body = await context.ReadJsonBodyAsync().ConfigureAwait(false);
        if (body.Error is { } error)
        {
            await context.WriteErrorAsync(error).ConfigureAwait(false);
            return null;
        }

        // M-3.
        if (SpeechModelOf(body.Root) is not { } model)
        {
            await context.WriteErrorAsync(RelayErrors.InvalidModel).ConfigureAwait(false);
            return null;
        }

        return model;
    }

    /// <summary>The refusal as its error, or the status object with 202 or 200; shared with route 6.</summary>
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

    /// <summary>The member of C-04 that the body's <c>model</c> equals (ordinal), or null.</summary>
    private static string? SpeechModelOf(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("model", out var member)
            || member.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        foreach (var known in ProtocolConstants.SpeechModels)
        {
            if (member.ValueEquals(known))
            {
                return known;
            }
        }

        return null;
    }
}
