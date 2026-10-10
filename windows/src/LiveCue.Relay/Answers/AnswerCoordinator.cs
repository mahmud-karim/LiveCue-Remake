using LiveCue.Relay.Catalog;
using LiveCue.Relay.Core;
using LiveCue.Relay.Edge;
using LiveCue.Relay.Protocol;

namespace LiveCue.Relay.Answers;

/// <summary>
/// Routes 7, 8 and 9 behind one object: the slot, the registry and the check order R-1 to R-8
/// (protocol 8.2).
///
/// First version (foundation): reads the body and then answers 500 ASSISTANT_FAILED, publishing no
/// request event; no request is ever in flight, so a cancel finds nothing. Work package WP-W03 builds
/// the pipeline.
/// </summary>
internal sealed class AnswerCoordinator : IAnswerCoordinator
{
    private readonly IAnswerBackend _backend;
    private readonly ICatalogReader _catalog;

    public AnswerCoordinator(RelayContext context, ICatalogReader catalog)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _backend = context.Dependencies.AnswerBackend ?? new CodexAnswerBackend(context);
    }

    public int InFlight => 0;

    public async Task HandleAsync(RouteContext context, AnswerKind kind)
    {
        _ = _backend;
        _ = _catalog;
        var body = await context.ReadJsonBodyAsync().ConfigureAwait(false);
        throw new RelayException(body.Error ?? RelayErrors.AssistantFailed);
    }

    public bool TryCancel(string requestId) => false;
}
