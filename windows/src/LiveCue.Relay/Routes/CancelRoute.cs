using LiveCue.Relay.Answers;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveCue.Relay.Routes;

/// <summary>
/// Route 9, <c>DELETE /v1/requests/{id}</c> (protocol 6.8). A target that does not match the id pattern
/// never reaches this route: the edge answers it 404 NOT_FOUND.
/// </summary>
internal sealed class CancelRoute : IRouteHandler
{
    private static readonly byte[] Cancelled = "{\"ok\":true,\"cancelled\":true}"u8.ToArray();

    private readonly IAnswerCoordinator _answers;

    public CancelRoute(IAnswerCoordinator answers)
    {
        _answers = answers;
    }

    public Task HandleAsync(RouteContext context) =>
        context.RequestId is { } requestId && _answers.TryCancel(requestId)
            ? context.WriteJsonAsync(StatusCodes.Status200OK, Cancelled)
            : context.WriteErrorAsync(RelayErrors.RequestNotFound);
}
