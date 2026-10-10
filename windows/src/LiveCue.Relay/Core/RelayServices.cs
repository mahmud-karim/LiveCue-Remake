using LiveCue.Relay.Answers;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.Edge;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Routes;
using LiveCue.Relay.Speech;

namespace LiveCue.Relay.Core;

/// <summary>The area instances of one relay, built once by <see cref="RelayComposition"/>.</summary>
internal sealed class RelayServices
{
    public required IPairingService Pairing { get; init; }

    public required ICatalogReader Catalog { get; init; }

    public required IAnswerCoordinator Answers { get; init; }

    public required ILocalModelController LocalModel { get; init; }

    public required ISpeechBridge Speech { get; init; }

    /// <summary>The one speech lease (C-34), shared by the model controller and the speech bridge.</summary>
    public required SpeechLease Lease { get; init; }

    /// <summary>One handler per route of protocol 6.1.</summary>
    public required IReadOnlyDictionary<RouteId, IRouteHandler> Routes { get; init; }
}
