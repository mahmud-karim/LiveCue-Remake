using LiveCue.Relay.Answers;
using LiveCue.Relay.Edge;

namespace LiveCue.Relay.Routes;

/// <summary>Route 7, <c>POST /v1/assist</c> (protocol section 8). Everything happens in the coordinator.</summary>
internal sealed class AssistRoute : IRouteHandler
{
    private readonly IAnswerCoordinator _answers;

    public AssistRoute(IAnswerCoordinator answers)
    {
        _answers = answers;
    }

    public Task HandleAsync(RouteContext context) => _answers.HandleAsync(context, AnswerKind.Assist);
}
