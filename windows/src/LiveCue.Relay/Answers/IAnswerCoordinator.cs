using LiveCue.Relay.Edge;

namespace LiveCue.Relay.Answers;

/// <summary>Routes 7, 8 and 9 behind one object: the slot, the registry and the check order R-1 to R-8.</summary>
internal interface IAnswerCoordinator
{
    /// <summary>Handles one assist or summary request to its response and its desktop events.</summary>
    Task HandleAsync(RouteContext context, AnswerKind kind);

    /// <summary>Route 9: kills the run with that id. False when no request with that id is in flight.</summary>
    bool TryCancel(string requestId);

    /// <summary>Number of requests holding the slot: 0 or 1.</summary>
    int InFlight { get; }
}
