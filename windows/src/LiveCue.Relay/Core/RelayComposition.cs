using LiveCue.Relay.Answers;
using LiveCue.Relay.Catalog;
using LiveCue.Relay.Edge;
using LiveCue.Relay.LocalModel;
using LiveCue.Relay.Pairing;
using LiveCue.Relay.Routes;
using LiveCue.Relay.Speech;

namespace LiveCue.Relay.Core;

/// <summary>
/// The one place that constructs the relay's areas. The constructor signatures used here are part of
/// the contract (W-design section 3.3, "Composition"): a package that replaces a class keeps its signature.
/// </summary>
internal static class RelayComposition
{
    public static RelayServices Compose(RelayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var lease = new SpeechLease();
        var pairing = new PairingService(context);
        var catalog = new CatalogReader(context);
        var answers = new AnswerCoordinator(context, catalog);
        var localModel = context.Dependencies.LocalModelController ?? new LocalModelController(context, lease);
        var speech = new SpeechBridge(context, localModel, lease);

        var routes = new Dictionary<RouteId, IRouteHandler>
        {
            [RouteId.Health] = new HealthRoute(context),
            [RouteId.PairVerify] = new PairVerifyRoute(context),
            [RouteId.Models] = new ModelsRoute(context, catalog),
            [RouteId.LocalModelStatus] = new LocalModelStatusRoute(context, localModel),
            [RouteId.LocalModelStart] = new LocalModelStartRoute(context, localModel),
            [RouteId.LocalModelStop] = new LocalModelStopRoute(context, localModel),
            [RouteId.Assist] = new AssistRoute(answers),
            [RouteId.SessionSummary] = new SummaryRoute(answers),
            [RouteId.CancelRequest] = new CancelRoute(answers),
            [RouteId.Speech] = new SpeechRoute(context, speech),
        };

        return new RelayServices
        {
            Pairing = pairing,
            Catalog = catalog,
            Answers = answers,
            LocalModel = localModel,
            Speech = speech,
            Lease = lease,
            Routes = routes,
        };
    }
}
