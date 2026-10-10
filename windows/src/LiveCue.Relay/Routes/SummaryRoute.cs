using LiveCue.Relay.Answers;
using LiveCue.Relay.Edge;

namespace LiveCue.Relay.Routes;

/// <summary>Route 8, <c>POST /v1/session-summary</c> (protocol section 8). Everything happens in the coordinator.</summary>
internal sealed class SummaryRoute : IRouteHandler
{
    private readonly IAnswerCoordinator _answers;

    public SummaryRoute(IAnswerCoordinator answers)
    {
        _answers = answers;
    }

    public Task HandleAsync(RouteContext context) => _answers.HandleAsync(context, AnswerKind.Summary);
}
